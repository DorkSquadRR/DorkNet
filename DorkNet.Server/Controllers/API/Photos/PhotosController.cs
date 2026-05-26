using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Photos;

/// <summary>
/// api.rec.net/api/photos/v1/* — social-feed wrapper around the
/// in-game camera. The camera itself uses storage.rec.net/upload with
/// FileType=Image to push the PNG bytes; that returns a Filename. The
/// watch's "Share" panel then POSTs here with that Filename, a caption
/// and any tagged players to create the public-feed photo.
///
/// Public reads (feed, by-player, by-room) are anonymous so the
/// feed.rec.net frontend can browse without an admin token.
/// Mutations (post, cheer, delete) require auth.
/// </summary>
[ApiController]
// Two hosts: api.rec.net is what the in-game watch hits; feed.rec.net
// also serves these endpoints so the public frontend website can do
// same-origin fetches without CORS.
[Route("api/photos/v1")]
public class PhotosController(
    DorkNetDbContext db,
    PlayerPresenceService presence,
    NotificationService notifications,
    LevelService level,
    DomainConfig domain) : ControllerBase
{
    public sealed record PostPhotoRequest(
        string ImageName,
        string? Caption,
        string? TaggedPlayerIds,
        long? RoomId,
        bool? IsPublic);

    /// <summary>POST api/photos/v1/post — promote an uploaded image
    /// blob to the photo feed. Body: ImageName (filename returned by
    /// storage.rec.net/upload), Caption, TaggedPlayerIds (CSV),
    /// optional RoomId (defaults to caller's current room from
    /// PlayerPresenceService), IsPublic (defaults true). Returns the
    /// created PhotoEntity.</summary>
    [HttpPost("post")]
    [Authorize]
    public async Task<ActionResult> PostPhoto([FromBody] PostPhotoRequest body)
    {
        var me = this.RequireCurrentPlayerId();
        if (string.IsNullOrWhiteSpace(body.ImageName))
            return BadRequest(new { error = "missing_image_name" });

        // Verify the blob actually exists and was uploaded by us. Without
        // this check anyone could cite another player's filename and
        // post-attribute the photo to themselves.
        var blob = await db.RoomDataBlobs
            .Where(b => b.BlobName == body.ImageName)
            .Select(b => new { b.UploadedByPlayerId })
            .FirstOrDefaultAsync();
        if (blob is null)
            return NotFound(new { error = "image_blob_not_found" });
        if (blob.UploadedByPlayerId != me)
            return Forbid();

        // Auto-resolve current room if the caller didn't supply one.
        var roomId = body.RoomId ?? presence.GetRoom(me)?.RoomId ?? 0;

        var photo = new PhotoEntity
        {
            UploaderPlayerId = me,
            BlobName = body.ImageName,
            Caption = (body.Caption ?? string.Empty).Trim(),
            TaggedPlayerIdsCsv = NormaliseTaggedIds(body.TaggedPlayerIds, exceptId: me),
            RoomId = roomId,
            IsPublic = body.IsPublic ?? true,
        };
        db.Photos.Add(photo);
        await db.SaveChangesAsync();

        // Posting a photo gives a small XP bump — encourages camera use.
        await level.AwardXpAsync(me, LevelService.InventionSavedXp, $"photo_posted:{photo.Id}");

        // Push to tagged players so their "Photos of me" feed refreshes.
        foreach (var taggedId in ParseTagged(photo.TaggedPlayerIdsCsv))
            await notifications.NotifyAsync(taggedId,
                PushNotificationId.SubscriptionUpdateProfile,
                new { Reason = "TaggedInPhoto", PhotoId = photo.Id, From = me });

        return Ok(ToDto(photo));
    }

    /// <summary>GET api/photos/v1/feed — public newest-first feed.
    /// Anonymous-safe (so the frontend website can render without a
    /// JWT). Soft-deleted and private photos are excluded.</summary>
    [HttpGet("feed")]
    [AllowAnonymous]
    public async Task<ActionResult> Feed([FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var rows = await db.Photos
            .Where(p => p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/by/{playerId} — photos posted by a
    /// specific player. Anonymous-safe. Private photos hidden unless
    /// the caller is the uploader (or an admin).</summary>
    [HttpGet("by/{playerId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> ByPlayer(long playerId,
        [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var me = this.CurrentPlayerId();
        var includePrivate = me == playerId || await IsAdminAsync(me);

        var q = db.Photos.Where(p => p.UploaderPlayerId == playerId && p.DeletedAt == null);
        if (!includePrivate) q = q.Where(p => p.IsPublic);
        var rows = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/of/{playerId} — photos the player
    /// is tagged in. Anonymous-safe.</summary>
    [HttpGet("of/{playerId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> OfPlayer(long playerId,
        [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var needle = $",{playerId},";
        // SQLite EF translates `string.Contains` to LIKE — wrapping the
        // CSV with leading/trailing commas avoids "12" matching "112".
        var rows = await db.Photos
            .Where(p => p.IsPublic && p.DeletedAt == null &&
                ("," + p.TaggedPlayerIdsCsv + ",").Contains(needle))
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/in/{roomId} — photos taken in a
    /// specific room. Anonymous-safe.</summary>
    [HttpGet("in/{roomId:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> InRoom(long roomId,
        [FromQuery] int take = 20, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var rows = await db.Photos
            .Where(p => p.RoomId == roomId && p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichAsync(rows));
    }

    /// <summary>GET api/photos/v1/{id} — single photo detail.
    /// Increments ViewCount as a side effect (denormalised for the
    /// "trending" sort).</summary>
    [HttpGet("{id:long}")]
    [AllowAnonymous]
    public async Task<ActionResult> Get(long id)
    {
        var p = await db.Photos.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null || p.DeletedAt is not null) return NotFound();
        if (!p.IsPublic)
        {
            var me = this.CurrentPlayerId();
            if (me != p.UploaderPlayerId && !await IsAdminAsync(me)) return NotFound();
        }
        p.ViewCount += 1;
        await db.SaveChangesAsync();
        var enriched = await EnrichAsync(new[] { p });
        return Ok(enriched.First());
    }

    /// <summary>POST api/photos/v1/{id}/cheer — like a photo. Idempotent
    /// per (caller, photo) pair. Pushes a notification to the
    /// uploader so their watch can refresh the count.</summary>
    [HttpPost("{id:long}/cheer")]
    [Authorize]
    public async Task<ActionResult> Cheer(long id)
    {
        var me = this.RequireCurrentPlayerId();
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null || photo.DeletedAt is not null) return NotFound();

        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == me && c.TargetPhotoId == id &&
            c.TargetPlayerId == 0 && c.TargetRoomId == 0);
        if (existing is not null) return Ok(new { already_cheered = true });

        db.Cheers.Add(new CheerEntity
        {
            FromPlayerId = me,
            TargetPhotoId = id,
        });
        photo.CheerCount += 1;
        await db.SaveChangesAsync();

        if (photo.UploaderPlayerId != me)
        {
            await level.AwardXpAsync(photo.UploaderPlayerId,
                LevelService.CheerReceivedXp, $"photo_cheer:{id}");
            await notifications.NotifyAsync(photo.UploaderPlayerId,
                PushNotificationId.SubscriptionUpdateProfile,
                new { Reason = "PhotoCheer", PhotoId = id, From = me });
        }
        return Ok(new { cheered = true, count = photo.CheerCount });
    }

    /// <summary>DELETE api/photos/v1/{id} — soft-delete. Allowed for
    /// the uploader or an admin. The blob bytes stay in
    /// RoomDataBlobs for audit; the row's DeletedAt timestamp hides
    /// it from feeds.</summary>
    [HttpDelete("{id:long}")]
    [Authorize]
    public async Task<ActionResult> Delete(long id)
    {
        var me = this.RequireCurrentPlayerId();
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null) return NotFound();
        if (photo.UploaderPlayerId != me && !await IsAdminAsync(me))
            return Forbid();
        photo.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Trim/dedupe tagged ids and drop the uploader's own id.
    /// CSV in, CSV out.</summary>
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

    private static IEnumerable<long> ParseTagged(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Enumerable.Empty<long>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => long.TryParse(s, out var v) ? v : 0L)
                 .Where(v => v > 0);

    private async Task<bool> IsAdminAsync(long? playerId)
    {
        if (playerId is not long id) return false;
        return await db.Players.Where(p => p.Id == id).Select(p => p.IsAdmin).FirstOrDefaultAsync();
    }

    /// <summary>Materialise photo rows into the wire DTO shape. One
    /// extra DB hit fetches the uploader display names and room names
    /// in batch so the JSON has human-readable labels — saves the
    /// frontend from a per-row N+1.</summary>
    private async Task<List<object>> EnrichAsync(IEnumerable<PhotoEntity> photos)
    {
        var photoList = photos.ToList();
        if (photoList.Count == 0) return new();

        var uploaderIds = photoList.Select(p => p.UploaderPlayerId).Distinct().ToList();
        var roomIds = photoList.Select(p => p.RoomId).Where(id => id > 0).Distinct().ToList();

        var uploaderNames = await db.Players
            .Where(p => uploaderIds.Contains(p.Id))
            .Select(p => new { p.Id, p.DisplayName, p.Username })
            .ToListAsync();
        var roomNames = await db.Rooms
            .Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();

        var uMap = uploaderNames.ToDictionary(u => u.Id);
        var rMap = roomNames.ToDictionary(r => r.Id);

        return photoList.Select(p =>
        {
            uMap.TryGetValue(p.UploaderPlayerId, out var u);
            rMap.TryGetValue(p.RoomId, out var r);
            return (object)new
            {
                p.Id,
                p.UploaderPlayerId,
                UploaderDisplayName = u?.DisplayName ?? $"Player_{p.UploaderPlayerId}",
                UploaderUsername = u?.Username ?? string.Empty,
                p.BlobName,
                ImageUrl = $"https://{domain.Sub("cdn")}/{p.BlobName}",
                p.Caption,
                p.RoomId,
                RoomName = r?.Name ?? string.Empty,
                TaggedPlayerIds = ParseTagged(p.TaggedPlayerIdsCsv).ToArray(),
                p.IsPublic,
                p.CheerCount,
                p.ViewCount,
                p.CreatedAt,
            };
        }).ToList();
    }

    private object ToDto(PhotoEntity p) => new
    {
        p.Id,
        p.UploaderPlayerId,
        p.BlobName,
        ImageUrl = $"https://{domain.Sub("cdn")}/{p.BlobName}",
        p.Caption,
        p.RoomId,
        TaggedPlayerIds = ParseTagged(p.TaggedPlayerIdsCsv).ToArray(),
        p.IsPublic,
        p.CheerCount,
        p.ViewCount,
        p.CreatedAt,
    };
}
