using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using DorkNet.Models.Auth;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Images;

/// <summary>
/// api.rec.net/api/images/v* — in-game camera photo endpoints. The 2020
/// watch's camera UI ("save photo to album", "share photo") posts here.
///
/// Wire shape (observed from server logs — the 2020 watch hits these
/// without a corresponding storage.rec.net/upload, meaning the bytes
/// come straight in this request body):
///   POST api/images/v4/uploadsaved
///     - body: multipart/form-data with the image file plus optional
///       fields (Caption, Description, RoomId, IsPublic, TaggedPlayerIds).
///       On the 2020 client the field name varies — we accept "File",
///       "Image", and any *.png/*.jpg upload for safety.
///     - response: an ImageInfo-shaped object (PascalCase keys). The
///       watch's success path calls a JSON parser whose strict keys are
///       AccountId, ImageName, CreatedAt — so all three are required.
///       Returning [] (the previous catch-all) made the watch silently
///       drop the photo.
///
/// Saved photos are stored as <see cref="PhotoEntity"/> rows. The 2020
/// client sends camera share state inside an imgMeta JSON form field
/// (accessibility=1 for public ShareCamera uploads), while newer callers
/// can still send top-level IsPublic.
/// </summary>
[ApiController]
public class ImagesController(
    DorkNetDbContext db,
    PlayerPresenceService presence,
    LevelService level,
    IObjectStorage storage,
    ImageSignatureService signatures,
    NotificationService notifications,
    DomainConfig domain,
    ILogger<ImagesController> logger) : ControllerBase
{
    private static readonly string ImageDir =
        Path.Combine(AppContext.BaseDirectory, "data", "images");

    private static readonly byte[] TransparentPng = new byte[]
    {
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,
        0x49,0x48,0x44,0x52,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
        0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,0x00,0x00,0x00,
        0x0D,0x49,0x44,0x41,0x54,0x78,0x9C,0x63,0x00,0x01,0x00,0x00,
        0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,0x00,0x00,0x49,
        0x45,0x4E,0x44,0xAE,0x42,0x60,0x82,
    };

    /// <summary>POST api/images/v{2-5}/uploadsaved — receive an image
    /// from the in-game camera "save to album" button. Persists the
    /// bytes in <see cref="RoomDataBlobEntity"/> and creates a
    /// <see cref="PhotoEntity"/> row keyed by the uploader. The photo
    /// defaults to IsPublic=false (saved-only) so it doesn't surface
    /// on the public feed unless the user explicitly hits Share.</summary>
    [HttpPost("api/images/v2/uploadsaved")]
    [HttpPost("api/images/v3/uploadsaved")]
    [HttpPost("api/images/v4/uploadsaved")]
    [HttpPost("api/images/v5/uploadsaved")]
    [Authorize]
    public async Task<IActionResult> UploadSaved()
    {
        var pid = this.RequireCurrentPlayerId();
        var bytes = await ReadImageBytesAsync();
        if (bytes is null || bytes.Length == 0)
        {
            logger.LogWarning("[images] uploadsaved by {Pid}: no file bytes received", pid);
            return BadRequest(new { error = "missing_file" });
        }

        // Pull optional metadata from the form (the 2020 watch sometimes
        // includes a caption / room context, sometimes posts bytes only).
        var form = Request.HasFormContentType ? await Request.ReadFormAsync() : null;
        var imageMeta = ParseImageMeta(form);
        var caption = form?["Caption"].ToString() ?? form?["Description"].ToString() ?? string.Empty;
        var taggedIds = form?["TaggedPlayerIds"].ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(taggedIds) && imageMeta?.PlayerIds.Count > 0)
            taggedIds = string.Join(",", imageMeta.PlayerIds);
        long roomId = 0;
        if (form is not null && long.TryParse(form["RoomId"].ToString(), out var rid)) roomId = rid;
        if (roomId == 0 && imageMeta?.RoomId is > 0) roomId = imageMeta.RoomId.Value;
        if (roomId == 0) roomId = presence.GetRoom(pid)?.RoomId ?? 0;
        var explicitPublic = form is null ? null : TryReadBool(form["IsPublic"].ToString());
        var isPublic = explicitPublic ?? (imageMeta?.Accessibility == 1 || imageMeta?.SavedImageType == 1);
        var isProfileThumbnail = IsProfileThumbnail(form);
        LogUploadForm("uploadsaved", pid, form, isProfileThumbnail);

        // Detect format. The watch uses PNG; if a JPEG slips through we
        // tag it accordingly so the cdn serves the right MIME.
        var extension = SniffExtension(bytes);
        var blobName = $"img_p{pid}_{Guid.NewGuid():N}.{extension}";

        // Dual-write during the migration window: S3 is the canonical
        // store going forward, but we keep the DB Bytes column populated
        // so reads from older code paths still work. PR 4 drops the
        // column once every blob has a confirmed S3 copy.
        try
        {
            using var s3Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var (bucket, key) = BlobRouter.Route(blobName);
            await storage.PutAsync(
                bucket, key, bytes,
                extension == "png" ? "image/png" : "image/jpeg",
                s3Timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[images] S3 upload failed for {Blob}; keeping DB fallback so the client does not fail",
                blobName);
        }

        db.RoomDataBlobs.Add(new RoomDataBlobEntity
        {
            RoomId = 0,
            BlobName = blobName,
            UploadedByPlayerId = pid,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = string.Empty,
        });

        var photo = new PhotoEntity
        {
            UploaderPlayerId = pid,
            BlobName = blobName,
            Caption = caption.Trim(),
            TaggedPlayerIdsCsv = NormaliseTaggedIds(taggedIds, exceptId: pid),
            RoomId = roomId,
            IsPublic = isPublic,
        };
        db.Photos.Add(photo);

        if (isProfileThumbnail)
        {
            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == pid);
            if (player is not null)
            {
                player.ProfileImageName = blobName;
                logger.LogInformation(
                    "[images] profile thumbnail upload set profile image: player={PlayerId} blob={Blob}",
                    pid,
                    blobName);
            }
        }

        await db.SaveChangesAsync();

        if (isProfileThumbnail)
        {
            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == pid);
            if (player is not null)
                await NotifyAccountImageChangedAsync(player);
        }

        // Small XP bump for taking a photo — encourages camera use.
        await level.AwardXpAsync(pid, LevelService.RoomVisitXp, $"photo_saved:{photo.Id}");

        logger.LogInformation(
            "[images] uploaded saved photo {Id} by {Pid} ({Bytes} bytes) → {Blob}",
            photo.Id, pid, bytes.Length, blobName);

        return Ok(BuildImageInfo(photo, pid));
    }

    /// <summary>POST api/images/v{1-5}/profile — direct profile image upload.
    /// Older clients use this instead of tagging an uploadsaved request as a
    /// ProfileThumbnail, so this route must always update Account.ProfileImage.</summary>
    [HttpPost("api/images/v1/profile")]
    [HttpPost("api/images/v2/profile")]
    [HttpPost("api/images/v3/profile")]
    [HttpPost("api/images/v4/profile")]
    [HttpPost("api/images/v5/profile")]
    [Authorize]
    public async Task<IActionResult> UploadProfile()
    {
        var pid = this.RequireCurrentPlayerId();
        var bytes = await ReadImageBytesAsync();
        if (bytes is null || bytes.Length == 0)
        {
            logger.LogWarning("[images] profile upload by {Pid}: no file bytes received", pid);
            return BadRequest(new { error = "missing_file" });
        }

        var form = Request.HasFormContentType ? await Request.ReadFormAsync() : null;
        LogUploadForm("profile", pid, form, isProfile: true);

        var extension = SniffExtension(bytes);
        var blobName = $"img_p{pid}_{Guid.NewGuid():N}.{extension}";
        var contentType = extension == "png" ? "image/png" : "image/jpeg";

        try
        {
            using var s3Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var (bucket, key) = BlobRouter.Route(blobName);
            await storage.PutAsync(bucket, key, bytes, contentType, s3Timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[images] profile S3 upload failed for {Blob}; keeping DB fallback so the client does not fail",
                blobName);
        }

        db.RoomDataBlobs.Add(new RoomDataBlobEntity
        {
            RoomId = 0,
            BlobName = blobName,
            UploadedByPlayerId = pid,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = string.Empty,
        });

        var photo = new PhotoEntity
        {
            UploaderPlayerId = pid,
            BlobName = blobName,
            Caption = string.Empty,
            TaggedPlayerIdsCsv = string.Empty,
            RoomId = presence.GetRoom(pid)?.RoomId ?? 0,
            IsPublic = false,
        };
        db.Photos.Add(photo);

        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == pid);
        if (player is not null)
            player.ProfileImageName = blobName;

        await db.SaveChangesAsync();

        if (player is not null)
            await NotifyAccountImageChangedAsync(player);

        logger.LogInformation(
            "[images] uploaded profile photo {Id} by {Pid} ({Bytes} bytes) -> {Blob}",
            photo.Id, pid, bytes.Length, blobName);

        return Ok(new
        {
            ImageName = blobName,
            AccountId = (int)pid,
            CreatedAt = photo.CreatedAt,
        });
    }

    /// <summary>GET api/images/v{2-5}/saved — list the caller's saved
    /// photos. The watch's "My Photos" tab calls this to populate the
    /// album view.</summary>
    [HttpGet("api/images/v2/saved")]
    [HttpGet("api/images/v3/saved")]
    [HttpGet("api/images/v4/saved")]
    [HttpGet("api/images/v5/saved")]
    [Authorize]
    public async Task<IActionResult> GetSaved()
    {
        var pid = this.RequireCurrentPlayerId();
        var rows = await db.Photos
            .Where(p => p.UploaderPlayerId == pid && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Take(200)
            .ToListAsync();
        return Ok(rows.Select(p => BuildImageInfo(p, pid)));
    }

    /// <summary>GET <c>api/images/v2/named</c> — the watch's
    /// <c>Images.DownloadNamedImageMappings</c> endpoint. Returns a
    /// list of <c>NamedImageDTO</c> entries; an empty list is a valid
    /// response (the watch's <c>ExpectListResponse</c> rejects an
    /// object body but tolerates an empty array).</summary>
    [HttpGet("api/images/v2/named")]
    public IActionResult NamedImages() => Ok(Array.Empty<object>());

    /// <summary>GET <c>api/images/v1/slideshow</c> — the watch's lobby
    /// slideshow info. Response shape per <c>SlideshowInfoDTO.Deserialize</c>:
    /// <c>{ValidTill (DateTime, required), Images (list, required)}</c>.
    /// A short <c>ValidTill</c> lets the Rec Center board pick up newly
    /// shared camera photos without needing a server restart.</summary>
    [HttpGet("api/images/v1/slideshow")]
    public async Task<IActionResult> SlideshowInfo()
    {
        var rows = await db.Photos
            .Where(p => p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync();

        return Ok(new
        {
            ValidTill = DateTime.UtcNow.AddMinutes(5),
            Images = rows.Select(p => BuildImageInfo(p, p.UploaderPlayerId)),
        });
    }

    /// <summary>Binary image fallback for clients that request image bytes
    /// through api/images/{name} instead of img.rec.net/{name}. Specific JSON
    /// endpoints above still win route matching; this only handles CDN-shaped
    /// image paths.</summary>
    [HttpGet("api/images/{*path}", Order = 100)]
    public async Task<IActionResult> ServeImageFromApiHost(string? path)
    {
        var fileName = Path.GetFileName(path ?? string.Empty);
        // Strip the leading '$' sigil that ImageData.image_name uses for
        // hash-addressed assets. Watch sends "%24<hash>.jpg"; route
        // decode gives us "$<hash>.jpg"; the bytes are stored at
        // "<hash>.jpg". Without the strip, placed in-world polaroids
        // miss in S3 and the watch renders the "?" fallback.
        if (!string.IsNullOrEmpty(fileName) && fileName[0] == '$') fileName = fileName[1..];
        if (string.IsNullOrWhiteSpace(fileName) || !IsImageName(fileName))
            return NotFound();

        var result = await LoadImageBytesAsync(fileName);
        if (result.Bytes is { Length: > 0 })
        {
            logger.LogInformation(
                "[images] api image hit host={Host} path={Path} file={File} source={Source} bytes={Bytes} query='{Query}'",
                Request.Host.Host,
                path,
                fileName,
                result.Source,
                result.Bytes.Length,
                Request.QueryString);
            Response.Headers.CacheControl = "public, max-age=300";
            signatures.AddContentSignature(Response, result.Bytes);
            return File(result.Bytes, MimeFromName(fileName));
        }

        logger.LogWarning(
            "[images] api image MISS host={Host} path={Path} file={File} query='{Query}' -> transparent png",
            Request.Host.Host,
            path,
            fileName,
            Request.QueryString);
        Response.Headers.CacheControl = "public, max-age=60";
        signatures.AddContentSignature(Response, TransparentPng);
        return File(TransparentPng, "image/png");
    }

    /// <summary>GET api/images/v{2-5}/byaccount/{accountId} — list a
    /// player's PUBLIC photos. Drives the "their album" tab in
    /// the watch's player profile.</summary>
    [HttpGet("api/images/v2/byaccount/{accountId:long}")]
    [HttpGet("api/images/v3/byaccount/{accountId:long}")]
    [HttpGet("api/images/v4/byaccount/{accountId:long}")]
    [HttpGet("api/images/v5/byaccount/{accountId:long}")]
    public async Task<IActionResult> GetByAccount(long accountId,
        [FromQuery] int take = 50, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);
        var rows = await db.Photos
            .Where(p => p.UploaderPlayerId == accountId && p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(rows.Select(p => BuildImageInfo(p, accountId)));
    }

    /// <summary>POST api/images/v{2-5}/share/{id} — flip a saved photo
    /// to public so it shows up on feed.rec.net. The watch's "Share"
    /// button hits this; if the watch posts to a different URL we'll
    /// add it when we see it in logs.</summary>
    [HttpPost("api/images/v2/share/{id:long}")]
    [HttpPost("api/images/v3/share/{id:long}")]
    [HttpPost("api/images/v4/share/{id:long}")]
    [HttpPost("api/images/v5/share/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Share(long id)
    {
        var pid = this.RequireCurrentPlayerId();
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null) return NotFound();
        if (photo.UploaderPlayerId != pid) return Forbid();
        photo.IsPublic = true;
        await db.SaveChangesAsync();
        return Ok(BuildImageInfo(photo, pid));
    }

    /// <summary>DELETE api/images/v{2-5}/{id} — uploader (or admin)
    /// soft-deletes a saved photo.</summary>
    [HttpDelete("api/images/v2/{id:long}")]
    [HttpDelete("api/images/v3/{id:long}")]
    [HttpDelete("api/images/v4/{id:long}")]
    [HttpDelete("api/images/v5/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Delete(long id)
    {
        var pid = this.RequireCurrentPlayerId();
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null) return NotFound();
        if (photo.UploaderPlayerId != pid)
        {
            var isAdmin = await db.Players.Where(x => x.Id == pid).Select(x => x.IsAdmin).FirstOrDefaultAsync();
            if (!isAdmin) return Forbid();
        }
        photo.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    // ── Legacy v1 ops (real persistence, was Ack-only stubs) ─────────

    public sealed class CheerImageRequest
    {
        public long ImageId { get; set; }
        public long PhotoId { get; set; }
    }

    public sealed class BulkCheeredRequest
    {
        public List<long>? SavedImageIds { get; set; }
        public List<long>? ImageIds { get; set; }
    }

    [HttpPost("api/images/v4/cheered/bulk")]
    [Authorize]
    public async Task<IActionResult> CheeredBulk([FromBody] BulkCheeredRequest? body)
    {
        var pid = this.RequireCurrentPlayerId();
        var ids = (body?.SavedImageIds ?? body?.ImageIds ?? new())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToList();
        if (ids.Count == 0) return Ok(Array.Empty<object>());

        var cheered = await db.Cheers
            .Where(c => c.FromPlayerId == pid && ids.Contains(c.TargetPhotoId))
            .Select(c => c.TargetPhotoId)
            .ToListAsync();
        var set = cheered.ToHashSet();
        return Ok(ids.Select(id => new
        {
            SavedImageId = id,
            IsCheered = set.Contains(id),
        }));
    }

    /// <summary>POST <c>api/images/v1/cheer</c> — cheer a photo.
    /// Was previously Ack-only; now persists a <c>CheerEntity</c> row
    /// keyed on <c>TargetPhotoId</c> + bumps <c>Photos.CheerCount</c>.
    /// Idempotent per (player, photo).</summary>
    [HttpPost("api/images/v1/cheer")]
    [Authorize]
    public async Task<IActionResult> CheerV1([FromBody] CheerImageRequest? body,
        [FromForm(Name = "ImageId")] long? imageIdForm,
        [FromForm(Name = "PhotoId")] long? photoIdForm)
    {
        var pid = this.RequireCurrentPlayerId();
        var photoId = body?.PhotoId ?? body?.ImageId
            ?? photoIdForm ?? imageIdForm ?? 0;
        if (photoId <= 0) return BadRequest(new { error = "missing photoId" });

        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId && p.DeletedAt == null);
        if (photo is null) return NotFound();

        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == pid && c.TargetPhotoId == photoId);
        if (existing is null)
        {
            db.Cheers.Add(new CheerEntity
            {
                FromPlayerId = pid,
                TargetPhotoId = photoId,
                CheeredAt = DateTime.UtcNow,
            });
            photo.CheerCount += 1;
            await db.SaveChangesAsync();
        }
        return Ok(new { photo.Id, photo.CheerCount });
    }

    /// <summary>GET <c>api/images/v1/listsaved</c> — the 2020 client
    /// deserializes this as SavedImagesListDTO { Images: string[] }.
    /// Do not return the richer v2/v4 photo-object list here.</summary>
    [HttpGet("api/images/v1/listsaved")]
    [Authorize]
    public async Task<IActionResult> ListSavedV1()
    {
        var pid = this.RequireCurrentPlayerId();
        var names = await db.Photos
            .Where(p => p.UploaderPlayerId == pid && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.BlobName)
            .Take(200)
            .ToListAsync();
        return Ok(new { Images = names });
    }

    public sealed class ModifyAccessibilityRequest
    {
        public long PhotoId { get; set; }
        public long ImageId { get; set; }
        public string? ImageName { get; set; }
        public bool IsPublic { get; set; }
    }

    /// <summary>POST <c>api/images/v1/modifyaccessibility</c> — flip
    /// the IsPublic flag on a saved photo. Was Ack-only; now writes
    /// the column.</summary>
    [HttpPost("api/images/v1/modifyaccessibility")]
    [Authorize]
    public async Task<IActionResult> ModifyAccessibility(
        [FromBody] ModifyAccessibilityRequest? body,
        [FromForm(Name = "PhotoId")] long? photoIdForm,
        [FromForm(Name = "ImageId")] long? imageIdForm,
        [FromForm(Name = "ImageName")] string? imageNameForm,
        [FromForm(Name = "IsPublic")] bool? isPublicForm)
    {
        var pid = this.RequireCurrentPlayerId();
        var photoId = body?.PhotoId ?? body?.ImageId
            ?? photoIdForm ?? imageIdForm ?? 0;
        var imageName = body?.ImageName ?? imageNameForm;
        if (photoId <= 0 && string.IsNullOrWhiteSpace(imageName))
            return BadRequest(new { error = "missing photoId" });
        var isPublic = body?.IsPublic ?? isPublicForm ?? true;

        var photo = photoId > 0
            ? await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId)
            : await db.Photos.FirstOrDefaultAsync(p => p.BlobName == imageName);
        if (photo is null) return NotFound();
        if (photo.UploaderPlayerId != pid) return Forbid();
        photo.IsPublic = isPublic;
        await db.SaveChangesAsync();
        return Ok(new { photo.Id, photo.IsPublic });
    }

    public sealed class DeleteSavedRequest
    {
        public long PhotoId { get; set; }
        public long ImageId { get; set; }
        public string? ImageName { get; set; }
    }

    /// <summary>POST <c>api/images/v1/deletesaved</c> + DELETE alias —
    /// soft-delete a saved photo. Was Ack-only; now flips
    /// <c>DeletedAt</c>.</summary>
    [HttpPost("api/images/v1/deletesaved")]
    [HttpDelete("api/images/v1/deletesaved/{id:long}")]
    [Authorize]
    public async Task<IActionResult> DeleteSavedV1(
        long? id,
        [FromBody] DeleteSavedRequest? body,
        [FromForm(Name = "PhotoId")] long? photoIdForm,
        [FromForm(Name = "ImageId")] long? imageIdForm,
        [FromForm(Name = "ImageName")] string? imageNameForm)
    {
        var photoId = id ?? body?.PhotoId ?? body?.ImageId
            ?? photoIdForm ?? imageIdForm ?? 0;
        var imageName = body?.ImageName ?? imageNameForm;
        if (photoId <= 0 && string.IsNullOrWhiteSpace(imageName))
            return BadRequest(new { error = "missing photoId" });
        if (photoId <= 0)
        {
            var pid = this.RequireCurrentPlayerId();
            var photo = await db.Photos.FirstOrDefaultAsync(p =>
                p.UploaderPlayerId == pid && p.BlobName == imageName && p.DeletedAt == null);
            if (photo is null) return NotFound();
            photo.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Ok(new RecNetResult { Success = true, Error = string.Empty });
        }
        return await Delete(photoId);
    }

    public sealed class SendLinkRequest
    {
        public long PhotoId { get; set; }
        public long ImageId { get; set; }
        public List<long>? RecipientIds { get; set; }
    }

    /// <summary>POST <c>api/images/v1/sendlink</c> — send a link to a
    /// photo as a DM to one or more recipients. Was Ack-only; now
    /// inserts one <see cref="MessageEntity"/> per recipient.</summary>
    [HttpPost("api/images/v1/sendlink")]
    [Authorize]
    public async Task<IActionResult> SendLink(
        [FromBody] SendLinkRequest? body,
        [FromForm(Name = "PhotoId")] long? photoIdForm,
        [FromForm(Name = "ImageId")] long? imageIdForm,
        [FromForm(Name = "RecipientIds")] string? recipientIdsForm)
    {
        var pid = this.RequireCurrentPlayerId();
        var photoId = body?.PhotoId ?? body?.ImageId ?? photoIdForm ?? imageIdForm ?? 0;
        if (photoId <= 0) return BadRequest(new { error = "missing photoId" });
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId);
        if (photo is null) return NotFound();

        var recipients = body?.RecipientIds ?? ParseIdList(recipientIdsForm) ?? new();
        if (recipients.Count == 0) return Ok(new { Sent = 0 });

        var preview = $"shared a photo (id {photoId})";
        foreach (var rid in recipients.Distinct())
        {
            db.Messages.Add(new MessageEntity
            {
                SenderPlayerId = pid,
                RecipientPlayerId = rid,
                Body = $"{preview} → /img/{photo.BlobName}",
            });
        }
        await db.SaveChangesAsync();
        return Ok(new { Sent = recipients.Count });
    }

    private static List<long>? ParseIdList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.TryParse(s.Trim(), out var v) ? v : 0L)
            .Where(v => v > 0)
            .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Read raw image bytes from the request — supports both
    /// multipart/form-data (the typical case, with field "File" or
    /// "Image") and a raw octet-stream body (some 2020 client paths).</summary>
    private async Task<byte[]?> ReadImageBytesAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            // Try common field names; fall back to the first uploaded file.
            var file = form.Files["File"]
                ?? form.Files["Image"]
                ?? form.Files["image"]
                ?? form.Files["file"]
                ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return null;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            return ms.ToArray();
        }

        // Raw body fallback — when the watch posts the bytes directly.
        if (Request.ContentLength is > 0)
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            return ms.ToArray();
        }
        return null;
    }

    /// <summary>Detect PNG vs JPEG by magic bytes — anything else
    /// defaults to .png since the 2020 client's camera always saves
    /// PNG.</summary>
    private static string SniffExtension(byte[] bytes)
    {
        if (bytes.Length >= 4 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "png";
        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "jpg";
        return "png";
    }

    private async Task<(byte[]? Bytes, string Source)> LoadImageBytesAsync(string fileName)
    {
        var (bucket, key) = BlobRouter.Route(fileName);
        try
        {
            using var s3Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var s3Bytes = await storage.GetAsync(bucket, key, s3Timeout.Token);
            if (s3Bytes is { Length: > 0 })
                return (s3Bytes, $"s3:{bucket}/{key}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[images] API image S3 lookup threw for {File} bucket={Bucket} key={Key}",
                fileName, bucket, key);
        }

        // S3 is the only canonical store. Disk-on-server is a legacy
        // surface used by tools/fetch-room-images.py'd thumbnails that
        // ship with the server — keep that as a last resort, but DB
        // bytes are gone.
        var full = Path.Combine(ImageDir, fileName);
        if (System.IO.File.Exists(full))
            return (await System.IO.File.ReadAllBytesAsync(full), "disk");

        return (null, "miss");
    }


    private static bool TryGetPlayerId(string fileName, string prefix, out long playerId)
    {
        playerId = 0;
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var start = prefix.Length;
        var end = fileName.IndexOf('_', start);
        return end > start && long.TryParse(fileName[start..end], out playerId);
    }

    private static bool IsImageName(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".avif";
    }

    private static string MimeFromName(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            _ => "image/png",
        };
    }

    private void LogUploadForm(string endpoint, long playerId, IFormCollection? form, bool isProfile)
    {
        if (form is null)
        {
            logger.LogInformation(
                "[images] {Endpoint} metadata player={PlayerId} form=false profile={Profile}",
                endpoint,
                playerId,
                isProfile);
            return;
        }

        var fileNames = string.Join(
            ",",
            form.Files.Select(f => $"{f.Name}:{f.FileName}:{f.Length}"));
        var fieldNames = string.Join(",", form.Keys);
        var meta = form["imgMeta"].ToString();
        if (string.IsNullOrWhiteSpace(meta))
            meta = form["imageMeta"].ToString();
        if (meta.Length > 512)
            meta = meta[..512];

        logger.LogInformation(
            "[images] {Endpoint} metadata player={PlayerId} files={Files} fields={Fields} profile={Profile} imgMeta={ImgMeta}",
            endpoint,
            playerId,
            fileNames,
            fieldNames,
            isProfile,
            meta);
    }

    private static bool IsProfileThumbnail(IFormCollection? form)
    {
        if (form is null) return false;

        static bool IsProfileValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Trim().Equals("4", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("ProfileThumbnail", StringComparison.OrdinalIgnoreCase);
        }

        if (IsProfileValue(form["SavedImageType"].ToString())
            || IsProfileValue(form["savedImageType"].ToString())
            || IsProfileValue(form["ImageType"].ToString())
            || IsProfileValue(form["imageType"].ToString()))
            return true;

        var meta = form["imgMeta"].ToString();
        if (string.IsNullOrWhiteSpace(meta))
            meta = form["imageMeta"].ToString();
        if (string.IsNullOrWhiteSpace(meta))
            return false;

        if (IsProfileValue(meta))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(meta);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var name in new[] { "savedImageType", "SavedImageType", "imageType", "ImageType" })
            {
                if (!doc.RootElement.TryGetProperty(name, out var value))
                    continue;
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n == 4)
                    return true;
                if (value.ValueKind == JsonValueKind.String && IsProfileValue(value.GetString()))
                    return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private sealed class ParsedImageMeta
    {
        public int? SavedImageType { get; init; }
        public int? Accessibility { get; init; }
        public long? RoomId { get; init; }
        public IReadOnlyList<long> PlayerIds { get; init; } = Array.Empty<long>();
    }

    private static ParsedImageMeta? ParseImageMeta(IFormCollection? form)
    {
        if (form is null) return null;

        var meta = form["imgMeta"].ToString();
        if (string.IsNullOrWhiteSpace(meta))
            meta = form["imageMeta"].ToString();
        if (string.IsNullOrWhiteSpace(meta))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(meta);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            return new ParsedImageMeta
            {
                SavedImageType = TryGetInt(doc.RootElement, "savedImageType", "SavedImageType", "imageType", "ImageType"),
                Accessibility = TryGetInt(doc.RootElement, "accessibility", "Accessibility"),
                RoomId = TryGetLong(doc.RootElement, "roomId", "RoomId"),
                PlayerIds = TryGetLongArray(doc.RootElement, "playerIds", "PlayerIds"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? TryReadBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (bool.TryParse(value, out var parsed)) return parsed;
        if (value == "1") return true;
        if (value == "0") return false;
        return null;
    }

    private static int? TryGetInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
                return n;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out n))
                return n;
        }

        return null;
    }

    private static long? TryGetLong(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
                return n;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out n))
                return n;
        }

        return null;
    }

    private static IReadOnlyList<long> TryGetLongArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                continue;

            return value.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out var n)
                    ? n
                    : x.ValueKind == JsonValueKind.String && long.TryParse(x.GetString(), out n)
                        ? n
                        : 0L)
                .Where(x => x > 0)
                .Distinct()
                .ToArray();
        }

        return Array.Empty<long>();
    }

    /// <summary>Build the response shape every images/v* endpoint
    /// returns. PascalCase keys + redundant aliases so whichever JSON
    /// parser the 2020 watch uses, it finds the field it wants. The
    /// most important keys are AccountId, ImageName, CreatedAt — those
    /// are what the watch's strict deserialiser reads. <c>Url</c>
    /// is built against the configured deployment apex (DORKNET_DOMAIN)
    /// so every callsite for the URL string flows through one source
    /// of truth.</summary>
    private object BuildImageInfo(PhotoEntity p, long accountId)
    {
        var cdnHost = domain.Sub("cdn");
        // Accessibility maps from IsPublic so the 2020.12 watch's IBANLCLBGLM
        // deserialiser reads a non-default value (0=Private, 1=Public).
        var accessibility = p.IsPublic ? 1 : 0;
        return new
        {
            // 2020.12 watch's IBANLCLBGLM strict keys (per
            // docs/recroom-2020-client-response-contracts.md:1048).
            // Missing any of these throws KeyNotFoundException in LitJson
            // and the gallery empties out.
            Id = p.Id,
            ImageName = p.BlobName,
            AccountId = (int)accountId,
            // Server-side mirror of AccountId as PlayerId — some
            // call-sites read PlayerId (lowercase id) instead of
            // AccountId. Including both is harmless on the watch.
            PlayerId = (int)accountId,
            RoomId = p.RoomId,
            // PlayerEventId is always 0 here (photo-event linkage isn't
            // tracked yet); the field still has to be present so the
            // strict deserialiser doesn't bail.
            PlayerEventId = 0L,
            Accessibility = accessibility,
            // AccessibilityLocked = whether the watch can flip the
            // privacy toggle. We don't gate on this server-side so the
            // value is always false (UI is free to toggle).
            AccessibilityLocked = false,
            // ImageType: 0=ScreenShot, 1=ProfileImage, 2=ImageOfMe, …
            // we don't differentiate yet — surface 0 unconditionally.
            Type = 0,
            ImageType = 0,
            CreatedAt = p.CreatedAt,
            TaggedPlayerIds = ParseTagged(p.TaggedPlayerIdsCsv).ToArray(),
            CheerCount = p.CheerCount,
            // We don't track per-photo comments yet — surface 0 for
            // the strict-key requirement.
            CommentCount = 0,
            // Bonus fields — useful for other clients (and our own feed UI).
            Filename = p.BlobName,
            Url = $"https://{cdnHost}/{p.BlobName}",
            Caption = p.Caption,
            Description = p.Caption,
            IsPublic = p.IsPublic,
            ViewCount = p.ViewCount,
        };
    }

    private static IEnumerable<long> ParseTagged(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Enumerable.Empty<long>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => long.TryParse(s, out var v) ? v : 0L)
                 .Where(v => v > 0);

    private Task NotifyAccountImageChangedAsync(PlayerEntity player)
    {
        var account = new DorkNet.Models.Auth.RecNetAccount
        {
            AccountId = (int)player.Id,
            RawUsername = player.Username,
            Username = player.Username,
            DisplayName = player.DisplayName ?? player.Username,
            ProfileImage = player.ProfileImageName ?? string.Empty,
            TreatAsJunior = player.IsJunior,
            HasBirthday = true,
            Platforms = 1,
        };
        var selfAccount = new DorkNet.Models.Auth.RecNetSelfAccount
        {
            AccountId = account.AccountId,
            RawUsername = account.RawUsername,
            Username = account.Username,
            DisplayName = account.DisplayName,
            ProfileImage = account.ProfileImage,
            TreatAsJunior = account.TreatAsJunior,
            HasBirthday = account.HasBirthday,
            Platforms = account.Platforms,
            Email = player.Email ?? string.Empty,
            Phone = player.Phone ?? string.Empty,
            Birthday = player.Birthday,
            JuniorState = player.IsJunior ? 1 : 0,
            ParentAccountId = null,
        };
        return Task.WhenAll(
            notifications.NotifyTypedAsync(player.Id, "AccountUpdate", account),
            notifications.NotifyTypedAsync(player.Id, "SelfAccountUpdate", selfAccount));
    }

    private static string NormaliseTaggedIds(string? csv, long exceptId)
    {
        if (string.IsNullOrWhiteSpace(csv)) return string.Empty;
        var ids = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var v) ? v : 0L)
            .Where(v => v > 0 && v != exceptId)
            .Distinct()
            .ToList();
        return string.Join(",", ids);
    }
}
