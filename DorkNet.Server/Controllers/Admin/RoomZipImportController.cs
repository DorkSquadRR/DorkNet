using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Admin;

/// <summary>
/// admin.rec.net/api/admin/v1/rooms/zip-bulk-import — preferred room
/// import path going forward. The legacy multi-file endpoint
/// (<c>POST api/admin/v1/rooms/import</c>) stays alongside this for
/// tooling that already targets it.
///
/// The archive format mirrors RecNet's own room export shape so a
/// user can download a room from rec.net, drop the zip into the admin
/// page, and we re-create it with full fidelity — including the
/// <c>.htr</c> AudioSampler / Holotar assets already extracted into
/// each subroom folder so we never have to hit Rec Room's CDN:
///
///   root.zip/
///     Rooms/Room_&lt;id&gt;_&lt;name&gt;_&lt;timestamp&gt;/
///       RoomDetails.json                       (full RecNet v4 details schema)
///       RoomImage.jpg | &lt;hash&gt;.jpg
///       Promo/Image/PromoImage_*.jpg
///       SubRooms/&lt;SceneName&gt;/
///         Subroom.json                         (per-subroom manifest)
///         &lt;hash&gt;.room                    (referenced by Subroom.CurrentSave.DataBlob)
///         AudioSampler/&lt;hash&gt;.htr
///         Holotar/&lt;hash&gt;.htr
///         Image/PVImage_&lt;hash&gt;.jpg
///     Inventions/Invention_&lt;id&gt;_&lt;name&gt;_&lt;timestamp&gt;/
///       Invention.json
///       InventionDetails.json
///       InventionVersion.json
///       InventionImage.jpg
///       &lt;hash&gt;.inv
///
/// Entry-scene resolution: the order in <c>RoomDetails.SubRooms[]</c>
/// is the entry order (RecNet API convention). The first entry is the
/// entry scene. The admin can override with an explicit
/// <c>RoomDetails.EntryScene</c> field.
/// </summary>
[ApiController]
[Route("api/admin/v1/rooms")]
[Authorize]
[AdminOnly]
public class RoomZipImportController(
    DorkNetDbContext db,
    RoomBlobNormalizerService roomBlobNormalizer,
    IObjectStorage storage,
    ILogger<RoomZipImportController> logger) : ControllerBase
{
    private long CurrentAdminId => this.RequireCurrentPlayerId();

    // ── Room-blob normaliser toggle ─────────────────────────────────────
    // The normaliser re-encodes .room bytes through the 2020 schema to
    // strip non-canonical encodings. It's been tripping the watch into
    // "Error attempting to parse room data stream" — the generated 2020
    // protobuf bindings appear to be missing fields or to emit them in
    // a wire-order the watch's parser disagrees with.
    //
    // Per-import flag (`normalizeBlobs` in the form / finalize body)
    // defaults OFF. The SPA's importer surfaces it as a checkbox so an
    // admin can opt back in once we're confident a particular zip's
    // contents survive the re-encode. The diagnostic parse still runs
    // either way (its parse-OK / parse-FAIL log line is useful), but
    // when the flag is false the original bytes are what reach S3.

    /// <summary>Write a blob to S3 at its canonical (bucket, key) AND
    /// record the metadata row in <see cref="RoomDataBlobEntity"/>.
    /// Bytes are NOT persisted on the DB row — S3 is the only place
    /// they live. Previously the importer wrote bytes to the
    /// <c>Bytes</c> column and skipped S3 entirely, so freshly
    /// imported rooms were only reachable through the read-path's
    /// DB-fallback branch. Centralised here so every importer call
    /// site (scene saves, .htr/PV/polaroid assets, .meta sentinels,
    /// invention saves, room thumbnails, promo images) ends up in
    /// S3 the same way.</summary>
    private async Task<RoomDataBlobEntity> WriteBlobAsync(
        long roomId, string blobName, byte[] bytes, long uploaderPlayerId,
        string? referencesCsv = null, string? contentTypeOverride = null)
    {
        var (bucket, key) = BlobRouter.Route(blobName);
        var contentType = contentTypeOverride ?? MimeFromName(blobName);
        try
        {
            await storage.PutAsync(bucket, key, bytes, contentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[zip-import] S3 PUT failed bucket={Bucket} key={Key} blob={Blob} bytes={Bytes} — row will still be recorded so the import isn't lost",
                bucket, key, blobName, bytes.Length);
        }

        var entity = new RoomDataBlobEntity
        {
            RoomId = roomId,
            BlobName = blobName,
            UploadedByPlayerId = uploaderPlayerId,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = referencesCsv ?? string.Empty,
        };
        db.RoomDataBlobs.Add(entity);
        return entity;
    }

    private static string MimeFromName(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".htr" => "application/octet-stream",
        ".room" => "application/octet-stream",
        ".meta" => "application/octet-stream",
        ".inv" => "application/octet-stream",
        ".dat" => "application/octet-stream",
        _ => "application/octet-stream",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── RoomDetails.json DTOs ──────────────────────────────────────────

    public sealed class RoomDetailsDto
    {
        public long? RoomId { get; set; }
        public string? Name { get; set; }
        public string? FriendlyName { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public int? WarningMask { get; set; }
        public string? CustomWarning { get; set; }
        public long? CreatorAccountId { get; set; }
        public int? State { get; set; }
        public int? Accessibility { get; set; }
        public bool? SupportsLevelVoting { get; set; }
        public bool? IsRRO { get; set; }
        public bool? SupportsScreens { get; set; }
        public bool? SupportsWalkVR { get; set; }
        public bool? SupportsTeleportVR { get; set; }
        public bool? SupportsVRLow { get; set; }
        public bool? SupportsMobile { get; set; }
        public bool? SupportsJuniors { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public bool? IsDorm { get; set; }
        public int? MaxPlayers { get; set; }
        public bool? CloningAllowed { get; set; }
        public bool? DisableMicAutoMute { get; set; }
        public StatsDto? Stats { get; set; }
        public List<TagDto>? Tags { get; set; }
        public List<SubRoomDto>? SubRooms { get; set; }
        public string? EntryScene { get; set; } // admin override; not in real exports
    }

    public sealed class StatsDto
    {
        public int? CheerCount { get; set; }
        public int? FavoriteCount { get; set; }
        public int? VisitorCount { get; set; }
        public int? VisitCount { get; set; }
    }

    public sealed class TagDto
    {
        public string? Tag { get; set; }
        public int? Type { get; set; }
        public bool? IsPrimaryGenre { get; set; }
    }

    public sealed class SubRoomDto
    {
        public long? SubRoomId { get; set; }
        public string? UnitySceneId { get; set; }
        public string? Name { get; set; }
        public bool? IsSandbox { get; set; }
        public int? MaxPlayers { get; set; }
        public int? Accessibility { get; set; }
        public CurrentSaveDto? CurrentSave { get; set; }
    }

    public sealed class CurrentSaveDto
    {
        public long? SubRoomDataSaveId { get; set; }
        public string? DataBlob { get; set; }
    }

    // ── Invention DTOs ────────────────────────────────────────────────

    public sealed class InventionJsonDto
    {
        public long? InventionId { get; set; }
        public string? ReplicationId { get; set; }
        public long? CreatorPlayerId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? LongDescription { get; set; }
        public string? ImageName { get; set; }
        public int? UgcVersion { get; set; }
        public int? CurrentVersionNumber { get; set; }
        public int? LatestVersionNumber { get; set; }
        public int? Accessibility { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public DateTime? FirstPublishedAt { get; set; }
        public long? CreationRoomId { get; set; }
        public int? NumPlayersHaveUsedInRoom { get; set; }
        public int? CheerCount { get; set; }
        public int? CreatorPermission { get; set; }
        public int? GeneralPermission { get; set; }
        public bool? IsAgInvention { get; set; }
        public bool? IsRecRoomApproved { get; set; }
    }

    public sealed class InventionVersionDto
    {
        public int? VersionNumber { get; set; }
        public string? BlobName { get; set; }
    }

    public sealed class InventionDetailsDto
    {
        public List<TagDto>? Tags { get; set; }
    }

    // ── Endpoint ──────────────────────────────────────────────────────

    [HttpPost("zip-bulk-import")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 2_000_000_000)] // 2 GB
    public async Task<IActionResult> ZipImport(
        [FromForm(Name = "archive")] IFormFile? archive,
        [FromForm(Name = "creatorPlayerId")] long? creatorPlayerId,
        [FromForm(Name = "normalizeBlobs")] bool? normalizeBlobs)
    {
        if (archive is null || archive.Length == 0)
            return BadRequest(new { error = "missing_archive" });

        await using var stream = archive.OpenReadStream();
        return await RunImportAsync(
            stream, archive.Length, creatorPlayerId,
            normalizeBlobs: normalizeBlobs ?? false);
    }

    // ── Chunked upload (Cloudflare 100MB workaround) ─────────────────
    //
    // The whole multipart-zip path 413s through Cloudflare's edge for
    // anything over 100 MB on free/pro plans. Real RecNet exports
    // routinely run 150-300 MB. Splitting the upload into ≤90 MB
    // chunks keeps each request safely under the cap; the server
    // appends chunks to a temp file and only invokes the import logic
    // once the finalize call comes in.
    //
    // Sessions are in-memory and short-lived (admins use the importer
    // synchronously). If the process restarts before finalize, the
    // upload is lost — acceptable for an admin tool used by 1-2 people.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ZipUploadSession> Sessions = new();

    public sealed class ZipUploadSession
    {
        public Guid Id { get; init; }
        public string TempPath { get; init; } = string.Empty;
        public long TotalBytes { get; set; }
        public long ReceivedBytes { get; set; }
        public string FileName { get; init; } = string.Empty;
        public long AdminPlayerId { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    public sealed record ChunkInitRequest(string FileName, long TotalBytes);

    /// <summary>POST <c>zip-upload-init</c> — allocates a temp file and
    /// returns an <c>uploadId</c> the SPA threads through subsequent
    /// chunk POSTs. Capped at 2 GB total; opens a sparse temp file so
    /// disk usage tracks actual bytes received.</summary>
    [HttpPost("zip-upload-init")]
    public ActionResult ChunkInit([FromBody] ChunkInitRequest body)
    {
        if (body.TotalBytes <= 0 || body.TotalBytes > 2_000_000_000)
            return BadRequest(new { error = "invalid_size" });

        // Prune stale sessions before allocating a new one. Anything
        // older than 2 hours with no activity is abandoned.
        foreach (var (sid, sess) in Sessions)
        {
            if (DateTime.UtcNow - sess.CreatedAt > TimeSpan.FromHours(2))
            {
                Sessions.TryRemove(sid, out _);
                try { if (System.IO.File.Exists(sess.TempPath)) System.IO.File.Delete(sess.TempPath); } catch { /* best effort */ }
            }
        }

        var id = Guid.NewGuid();
        var safeName = Path.GetFileName(body.FileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"dorknet-ziplupload-{id:N}.zip");
        // Pre-allocate the file at full length so chunk writes at
        // arbitrary offsets work without the OS having to grow it
        // page-by-page during the random writes.
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(body.TotalBytes);
        }
        Sessions[id] = new ZipUploadSession
        {
            Id = id,
            TempPath = tempPath,
            TotalBytes = body.TotalBytes,
            FileName = safeName,
            AdminPlayerId = CurrentAdminId,
        };
        return Ok(new { uploadId = id });
    }

    /// <summary>POST <c>zip-upload-chunk/{id}?offset=N</c> — writes the
    /// posted body at <c>offset</c> in the session's temp file.
    /// Per-call size capped at 100 MB (Kestrel-side, well below the
    /// Cloudflare 100 MB edge cap so we never see a 413). Body is the
    /// raw chunk bytes; the SPA's <c>uploadZipInChunks</c> helper sends
    /// the right slice.</summary>
    [HttpPost("zip-upload-chunk/{id:guid}")]
    [DisableRequestSizeLimit]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> ChunkAppend(Guid id, [FromQuery] long offset)
    {
        if (!Sessions.TryGetValue(id, out var session)) return NotFound(new { error = "unknown_session" });
        if (session.AdminPlayerId != CurrentAdminId) return Forbid();
        if (offset < 0 || offset >= session.TotalBytes)
            return BadRequest(new { error = "bad_offset", offset, total = session.TotalBytes });

        await using var fs = new FileStream(session.TempPath, FileMode.Open, FileAccess.Write, FileShare.Read);
        fs.Position = offset;
        var written = 0L;
        await Request.Body.CopyToAsync(fs);
        written = fs.Position - offset;
        session.ReceivedBytes = Math.Max(session.ReceivedBytes, fs.Position);
        return Ok(new
        {
            uploadId = id,
            offset,
            written,
            received = session.ReceivedBytes,
            total = session.TotalBytes,
        });
    }

    /// <summary>Body for <c>zip-upload-finalize</c>. <see cref="CreatorPlayerId"/>
    /// stamps every imported room/invention with that player as the
    /// owner. <see cref="SelectedRoomFolders"/> and
    /// <see cref="SelectedInventionFolders"/>, when non-null and
    /// non-empty, restrict the import to only those top-level folders
    /// inside the archive — the SPA's preview offers a checkbox per
    /// room/invention so admins can skip a bad export's broken entry
    /// without rebuilding the zip. Null/empty means "import
    /// everything" (back-compat with older SPA builds).</summary>
    public sealed record ChunkFinalizeRequest(
        long? CreatorPlayerId,
        List<string>? SelectedRoomFolders,
        List<string>? SelectedInventionFolders,
        bool? NormalizeBlobs);

    /// <summary>POST <c>zip-upload-finalize/{id}</c> — runs the existing
    /// zip import on the assembled temp file then deletes it. Same
    /// response shape as <c>zip-bulk-import</c>.</summary>
    [HttpPost("zip-upload-finalize/{id:guid}")]
    public async Task<IActionResult> ChunkFinalize(Guid id, [FromBody] ChunkFinalizeRequest? body)
    {
        if (!Sessions.TryGetValue(id, out var session)) return NotFound(new { error = "unknown_session" });
        if (session.AdminPlayerId != CurrentAdminId) return Forbid();

        try
        {
            await using var stream = System.IO.File.OpenRead(session.TempPath);
            return await RunImportAsync(
                stream,
                session.TotalBytes,
                body?.CreatorPlayerId,
                body?.SelectedRoomFolders,
                body?.SelectedInventionFolders,
                normalizeBlobs: body?.NormalizeBlobs ?? false);
        }
        finally
        {
            Sessions.TryRemove(id, out _);
            try { if (System.IO.File.Exists(session.TempPath)) System.IO.File.Delete(session.TempPath); }
            catch (Exception ex) { logger.LogWarning(ex, "[zip-import] failed to clean up {Path}", session.TempPath); }
        }
    }

    /// <summary>DELETE <c>zip-upload-abort/{id}</c> — drop an in-flight
    /// upload session (e.g. user navigated away). No-op if the id is
    /// already gone.</summary>
    [HttpDelete("zip-upload-abort/{id:guid}")]
    public IActionResult ChunkAbort(Guid id)
    {
        if (Sessions.TryRemove(id, out var session))
        {
            try { if (System.IO.File.Exists(session.TempPath)) System.IO.File.Delete(session.TempPath); }
            catch (Exception ex) { logger.LogWarning(ex, "[zip-import] failed to clean up {Path}", session.TempPath); }
        }
        return Ok();
    }

    // ── Shared import core (called by both single-shot and chunked) ──

    private async Task<IActionResult> RunImportAsync(
        Stream archiveStream,
        long archiveBytes,
        long? creatorPlayerId,
        List<string>? selectedRoomFolders = null,
        List<string>? selectedInventionFolders = null,
        bool normalizeBlobs = false)
    {
        var creator = creatorPlayerId ?? CurrentAdminId;
        if (!await db.Players.AnyAsync(p => p.Id == creator))
            return BadRequest(new { error = "creator_not_found", creator });

        using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

        // Index entries by normalized path. We do path-based lookups
        // throughout (per-room folder, per-subroom folder, per-invention
        // folder), so a flat dictionary keyed by full path is simplest.
        var entryByPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
            entryByPath[Normalize(e.FullName)] = e;
        }

        // Find every room manifest (`Rooms/<folder>/RoomDetails.json`).
        // Folder names are like "Room_3163127_YarrHarrHeist_20260509_180319"
        // but we don't parse anything out of them — RoomDetails.json is
        // the source of truth.
        var roomFolders = entryByPath.Keys
            .Where(k => k.StartsWith("Rooms/", StringComparison.OrdinalIgnoreCase)
                     && k.EndsWith("/RoomDetails.json", StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Substring(0, k.Length - "/RoomDetails.json".Length))
            .ToList();

        // Inventions can live either at the zip root (Inventions/Invention_<id>_<name>_<ts>/)
        // OR inside a room folder (Rooms/Room_<id>_<name>_<ts>/Inventions/Invention_…).
        // Both layouts show up in the wild — handle either.
        var inventionFolders = entryByPath.Keys
            .Where(k => k.EndsWith("/Invention.json", StringComparison.OrdinalIgnoreCase)
                     && (k.StartsWith("Inventions/", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("/Inventions/", StringComparison.OrdinalIgnoreCase)))
            .Select(k => k[..^"/Invention.json".Length])
            .ToList();

        if (roomFolders.Count == 0 && inventionFolders.Count == 0)
            return BadRequest(new { error = "empty_archive_no_manifests" });

        // If the admin supplied an explicit selection (non-null,
        // non-empty), filter to just those folders. Comparison is
        // case-insensitive and tolerates leading/trailing slashes that
        // the client might have included. Anything in the selection
        // that we can't match against the archive is just ignored —
        // the per-room report will simply not include it.
        if (selectedRoomFolders is { Count: > 0 })
        {
            var allowed = selectedRoomFolders
                .Select(s => Normalize(s).TrimEnd('/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            roomFolders = roomFolders.Where(f => allowed.Contains(f)).ToList();
        }
        if (selectedInventionFolders is { Count: > 0 })
        {
            var allowed = selectedInventionFolders
                .Select(s => Normalize(s).TrimEnd('/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            inventionFolders = inventionFolders.Where(f => allowed.Contains(f)).ToList();
        }

        var roomReports = new List<object>();
        long nextRoomId = (await db.Rooms
            .Where(r => r.Id >= 1000)
            .Select(r => (long?)r.Id)
            .MaxAsync() ?? 999) + 1;

        foreach (var folder in roomFolders)
        {
            try
            {
                var result = await ImportRoomAsync(folder, nextRoomId, creator, entryByPath, normalizeBlobs);
                roomReports.Add(result);
                if (result.GetType().GetProperty("ok")!.GetValue(result) as bool? == true)
                    nextRoomId++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[zip-import] room folder='{Folder}' failed", folder);
                roomReports.Add(new { folder, ok = false, error = ex.Message });
            }
        }

        var inventionReports = new List<object>();
        foreach (var folder in inventionFolders)
        {
            try { inventionReports.Add(await ImportInventionAsync(folder, creator, entryByPath)); }
            catch (Exception ex)
            {
                logger.LogError(ex, "[zip-import] invention folder='{Folder}' failed", folder);
                inventionReports.Add(new { folder, ok = false, error = ex.Message });
            }
        }

        return Ok(new
        {
            archiveBytes,
            roomCount = roomFolders.Count,
            inventionCount = inventionFolders.Count,
            rooms = roomReports,
            inventions = inventionReports,
        });
    }

    // ── Per-room import ───────────────────────────────────────────────

    private async Task<object> ImportRoomAsync(
        string roomFolder,
        long roomId,
        long creator,
        Dictionary<string, ZipArchiveEntry> entries,
        bool normalizeBlobs)
    {
        // RoomDetails.json
        var detailsKey = $"{roomFolder}/RoomDetails.json";
        var details = await ReadJsonAsync<RoomDetailsDto>(entries[detailsKey]) ?? new RoomDetailsDto();
        var name = !string.IsNullOrWhiteSpace(details.Name) ? details.Name!.Trim() : Path.GetFileName(roomFolder);

        if (await db.Rooms.AnyAsync(r => r.Name == name))
            return new { name, ok = false, error = "duplicate_name", folder = roomFolder };

        // Subroom manifests + blobs. Walk each folder under
        // <roomFolder>/SubRooms/<scene>/ and pair its Subroom.json with
        // the .room blob it references via CurrentSave.DataBlob, plus
        // any AudioSampler/Holotar .htr files alongside.
        var subroomRoot = $"{roomFolder}/SubRooms/";
        var scenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in entries.Keys.Where(k => k.StartsWith(subroomRoot, StringComparison.OrdinalIgnoreCase)))
        {
            var rel = path.Substring(subroomRoot.Length);
            var slash = rel.IndexOf('/');
            if (slash <= 0) continue;
            var sceneName = rel.Substring(0, slash);
            if (!scenes.ContainsKey(sceneName)) scenes[sceneName] = sceneName;
        }
        var resolvedScenes = new List<ResolvedSubroom>();
        var missingScenes = new List<string>();

        foreach (var sceneName in scenes.Keys)
        {
            var sceneFolder = $"{subroomRoot}{sceneName}";
            var subroomJsonKey = $"{sceneFolder}/Subroom.json";
            if (!entries.TryGetValue(subroomJsonKey, out var subroomJsonEntry))
            {
                missingScenes.Add($"{sceneName} (missing Subroom.json)");
                continue;
            }
            var manifest = await ReadJsonAsync<SubRoomDto>(subroomJsonEntry) ?? new SubRoomDto();
            var blobFile = manifest.CurrentSave?.DataBlob;
            if (string.IsNullOrWhiteSpace(blobFile))
            {
                missingScenes.Add($"{sceneName} (no CurrentSave.DataBlob)");
                continue;
            }
            var blobKey = $"{sceneFolder}/{blobFile}";
            if (!entries.TryGetValue(blobKey, out var blobEntry))
            {
                missingScenes.Add($"{sceneName} → {blobFile}");
                continue;
            }

            var blobBytes = await ReadAllAsync(blobEntry);
            // The normaliser still runs so we keep the parse-OK / parse-FAIL
            // log line — useful telemetry even when we're not persisting
            // its output. Per-import `normalizeBlobs` flag (defaulting
            // off because the re-encode currently crashes the watch on
            // load) decides which bytes actually reach S3.
            var norm = roomBlobNormalizer.Normalize(blobBytes);
            var persistedBytes = normalizeBlobs ? norm.Bytes : blobBytes;

            // Collect .htr asset blobs that live alongside this subroom.
            var htrAssets = new List<(string Name, ZipArchiveEntry Entry)>();
            foreach (var sub in new[] { "AudioSampler", "Holotar" })
            {
                var prefix = $"{sceneFolder}/{sub}/";
                foreach (var (k, e) in entries.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    if (k.EndsWith(".htr", StringComparison.OrdinalIgnoreCase))
                        htrAssets.Add((Path.GetFileName(k), e));
                }
            }
            // Image/ folder: persistence-view previews (PVImage_<hash>.ext)
            // and — since the exporter consolidated layouts — also the
            // placed-in-world polaroids that used to live in Polaroids/.
            //
            // The watch fetches BOTH URLs for the same content:
            //   * `img.localhost/PVImage_<hash>.png` — persistence-view preview
            //   * `img.localhost/<hash>.png` — placed polaroid (room blob
            //     references this bare-hash form, decoded directly from
            //     the .room protobuf in our sample)
            // So for any `PVImage_`-prefixed file we save the bytes under
            // BOTH BlobNames. Same file in S3/DB twice is cheap and saves
            // us from missing one of the lookup paths.
            const string pvPrefix = "PVImage_";
            var pvImages = new List<(string Name, ZipArchiveEntry Entry)>();
            var polaroids = new List<(string Name, ZipArchiveEntry Entry)>();
            var imageFolderPrefix = $"{sceneFolder}/Image/";
            foreach (var (k, e) in entries.Where(kv => kv.Key.StartsWith(imageFolderPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                var fileName = Path.GetFileName(k);
                pvImages.Add((fileName, e));
                if (fileName.StartsWith(pvPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var bareHashName = fileName[pvPrefix.Length..];
                    polaroids.Add((bareHashName, e));
                }
            }
            // Legacy fallback: older archives shipped polaroids as a
            // separate Polaroids/ subfolder. Still support those so we
            // don't regress on older test zips that haven't been re-
            // exported. The persistence loop's de-dupe will collapse
            // overlaps from the Image/-derived path.
            var polaroidFolderPrefix = $"{sceneFolder}/Polaroids/";
            foreach (var (k, e) in entries.Where(kv => kv.Key.StartsWith(polaroidFolderPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                polaroids.Add((Path.GetFileName(k), e));
            }

            resolvedScenes.Add(new ResolvedSubroom(
                Manifest: manifest,
                SceneName: sceneName,
                Bytes: persistedBytes,
                RawBytes: blobBytes.Length,
                // When the toggle is off we still report `norm.Normalized` —
                // it reflects whether the diagnostic parse succeeded, not
                // whether we actually applied the normalised output.
                NormalizedOk: norm.Normalized,
                HtrAssets: htrAssets,
                PvImages: pvImages,
                Polaroids: polaroids));
            logger.LogInformation(
                "[zip-import] room='{Room}' scene='{Scene}' blob={Blob} raw={Raw:N0} normOutput={Norm:N0} persistedFrom={Source} htr={Htr} pv={Pv} polaroids={Pol}",
                name, sceneName, blobFile, blobBytes.Length, norm.Bytes.Length,
                normalizeBlobs ? "normaliser" : "raw",
                htrAssets.Count, pvImages.Count, polaroids.Count);
        }

        if (resolvedScenes.Count == 0)
            return new { name, ok = false, error = "no_resolvable_subrooms", missingScenes, folder = roomFolder };

        // Entry resolution: order subrooms by manifest order if
        // RoomDetails.SubRooms[] is present; otherwise by sceneName.
        // EntryScene field overrides everything.
        var manifestOrder = (details.SubRooms ?? new())
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s.Name!)
            .ToList();
        if (manifestOrder.Count > 0)
        {
            resolvedScenes = resolvedScenes
                .OrderBy(r => manifestOrder.IndexOf(r.SceneName) is int i && i >= 0 ? i : int.MaxValue)
                .ThenBy(r => r.SceneName)
                .ToList();
        }
        else
        {
            resolvedScenes = resolvedScenes.OrderBy(r => r.SceneName).ToList();
        }

        // Entry resolution cascade:
        //   1. Explicit `RoomDetails.EntryScene` override (admin-set,
        //      not in real exports).
        //   2. <hash>.meta protobuf at the room root — RecNet's
        //      published-snapshot manifest. Field 2 is the entry
        //      subroom INDEX into RoomDetails.SubRooms[] (0-based).
        //      Decoded directly without a generated schema because
        //      the message is tiny (6 bytes total in the known sample).
        //   3. First entry in manifest order.
        //   4. Alphabetical fallback.
        ResolvedSubroom? metaEntry = null;
        int? metaIndex = null;
        string? metaFileName = null;
        var metaCandidate = entries
            .Where(kv => kv.Key.StartsWith($"{roomFolder}/", StringComparison.OrdinalIgnoreCase)
                      && !kv.Key[$"{roomFolder}/".Length..].Contains('/')
                      && kv.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .FirstOrDefault();
        if (metaCandidate is not null)
        {
            metaFileName = Path.GetFileName(metaCandidate.FullName);
            var metaBytes = await ReadAllAsync(metaCandidate);
            metaIndex = TryReadEntrySubroomIndexFromMeta(metaBytes);
            logger.LogInformation("[zip-import] room='{Room}' .meta={Meta} bytes={Bytes} entryIndex={Idx}",
                name, metaFileName, metaBytes.Length, metaIndex);
            if (metaIndex is int idx && idx >= 0 && idx < manifestOrder.Count)
            {
                var sceneAtIndex = manifestOrder[idx];
                metaEntry = resolvedScenes.FirstOrDefault(r =>
                    string.Equals(r.SceneName, sceneAtIndex, StringComparison.OrdinalIgnoreCase));
            }
        }

        ResolvedSubroom entry;
        string entrySource;
        if (!string.IsNullOrWhiteSpace(details.EntryScene)
            && resolvedScenes.FirstOrDefault(r => string.Equals(r.SceneName, details.EntryScene, StringComparison.OrdinalIgnoreCase)) is { } overrideEntry)
        {
            entry = overrideEntry; entrySource = "RoomDetails.EntryScene";
        }
        else if (metaEntry is not null)
        {
            entry = metaEntry; entrySource = $"meta_index={metaIndex}";
        }
        else
        {
            entry = resolvedScenes[0]; entrySource = manifestOrder.Count > 0 ? "manifest_first" : "alphabetical_first";
        }

        // Image: try ImageName lookup first (RecNet-style hash filename),
        // then RoomImage.* fallback (convenience format).
        ZipArchiveEntry? imageEntry = null;
        if (!string.IsNullOrWhiteSpace(details.ImageName)
            && entries.TryGetValue($"{roomFolder}/{details.ImageName}", out var hashed))
        {
            imageEntry = hashed;
        }
        else
        {
            imageEntry = entries.FirstOrDefault(kv =>
                kv.Key.StartsWith($"{roomFolder}/RoomImage.", StringComparison.OrdinalIgnoreCase)).Value;
        }

        // Reorder so entry is first. Manifest order preserved for the rest.
        var ordered = new List<ResolvedSubroom> { entry };
        ordered.AddRange(resolvedScenes.Where(r => !ReferenceEquals(r, entry)));

        var slug = name.ToLowerInvariant().Replace(' ', '_');
        // Prefix with room_<id>_ so BlobRouter parks every .room scene
        // save alongside its room_<id>_v*.dat versioned snapshots in
        // saves/room/<id>/. Same routing parser handles both shapes;
        // no per-extension special case.
        string MakeBlobName(string sceneName) => $"room_{roomId}_{slug}_{sceneName.ToLowerInvariant()}.room";

        // Use the entry subroom's UnitySceneId as the room's location.
        var entryLocation = entry.Manifest.UnitySceneId ?? "a75f7547-79eb-47c6-8986-6767abcb4f92";

        // Persist .htr assets and PV images at the GLOBAL level
        // (RoomId=0) under their original hash filenames. The CDN
        // / img.* controllers look up by exact blob name, so this
        // matches what HtrAssetMirrorService would have produced if it
        // had to fetch them from rec.net.
        //
        // We track three counters per asset type:
        //   * referenced  — total unique filenames the archive points at
        //   * imported    — newly inserted into RoomDataBlobs this run
        //   * alreadyInDb — already present from a prior import / mirror
        // The third is important to surface: bulk-purge-custom keeps
        // shared assets intentionally (other rooms might use them),
        // so a re-import after purge will legitimately report 0 newly
        // imported with non-zero already-in-DB. Reporting only "0
        // imported" reads as "no HTR uploaded" which is wrong.
        var htrAssetCount = 0;
        var pvImageCount = 0;
        var polaroidCount = 0;
        var allHtrAssets = ordered.SelectMany(s => s.HtrAssets).ToList();
        var allPvImages = ordered.SelectMany(s => s.PvImages).ToList();
        var allPolaroids = ordered.SelectMany(s => s.Polaroids).ToList();

        // De-dupe within the archive first (same .htr can appear under
        // multiple scenes), then de-dupe against the DB. Both layers
        // matter for accurate reporting.
        var uniqueHtrNames = allHtrAssets.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var uniquePvNames  = allPvImages.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var uniquePolaroidNames = allPolaroids.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var existingHtrNames = new HashSet<string>(
            await db.RoomDataBlobs
                .Where(b => uniqueHtrNames.Contains(b.BlobName))
                .Select(b => b.BlobName).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var existingPvNames = new HashSet<string>(
            await db.RoomDataBlobs
                .Where(b => uniquePvNames.Contains(b.BlobName))
                .Select(b => b.BlobName).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var existingPolaroidNames = new HashSet<string>(
            await db.RoomDataBlobs
                .Where(b => uniquePolaroidNames.Contains(b.BlobName))
                .Select(b => b.BlobName).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var htrAlreadyInDbAtStart = existingHtrNames.Count;
        var pvAlreadyInDbAtStart  = existingPvNames.Count;
        var polaroidAlreadyInDbAtStart = existingPolaroidNames.Count;

        var imageBlobName = string.Empty;

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // Subroom blobs
            foreach (var s in ordered)
            {
                await WriteBlobAsync(roomId, MakeBlobName(s.SceneName), s.Bytes, creator);
            }
            // HTR assets (shared content — keyed by hash, RoomId=0)
            foreach (var (hname, hentry) in allHtrAssets)
            {
                if (existingHtrNames.Contains(hname)) continue;
                existingHtrNames.Add(hname);
                var bytes = await ReadAllAsync(hentry);
                await WriteBlobAsync(0, hname, bytes, creator);
                htrAssetCount++;
            }
            // PV images (persistence-view previews; shared)
            foreach (var (pname, pentry) in allPvImages)
            {
                if (existingPvNames.Contains(pname)) continue;
                existingPvNames.Add(pname);
                var bytes = await ReadAllAsync(pentry);
                await WriteBlobAsync(0, pname, bytes, creator);
                pvImageCount++;
            }
            // Polaroids (placed in-world; watch fetches by bare hash).
            foreach (var (polName, polEntry) in allPolaroids)
            {
                if (existingPolaroidNames.Contains(polName)) continue;
                existingPolaroidNames.Add(polName);
                var bytes = await ReadAllAsync(polEntry);
                await WriteBlobAsync(0, polName, bytes, creator);
                polaroidCount++;
            }
            // Room-root opaque marker files (e.g. <hash>.meta — 6-byte
            // sentinel the exporter drops, purpose unclear but watching
            // for fetches just in case). Rename to room_<id>_<hash>.meta
            // so BlobRouter parks them in saves/room/<id>/ alongside the
            // versioned save snapshots — keeps every byte that belongs
            // to a specific room in one S3 folder.
            var rootMetaPrefix = $"{roomFolder}/";
            foreach (var (k, mentry) in entries.Where(kv =>
                kv.Key.StartsWith(rootMetaPrefix, StringComparison.OrdinalIgnoreCase)
                && !kv.Key[rootMetaPrefix.Length..].Contains('/')
                && kv.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            {
                var originalName = Path.GetFileName(k);
                var fname = $"room_{roomId}_{originalName}";
                if (await db.RoomDataBlobs.AnyAsync(b => b.BlobName == fname)) continue;
                var bytes = await ReadAllAsync(mentry);
                await WriteBlobAsync(roomId, fname, bytes, creator);
            }

            // Room image
            if (imageEntry is not null)
            {
                var bytes = await ReadAllAsync(imageEntry);
                var ext = Path.GetExtension(imageEntry.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                imageBlobName = $"image_room_{roomId}{ext}";
                await WriteBlobAsync(0, imageBlobName, bytes, creator);
            }

            // Promo images (kept under their original hashed names so
            // the room's promo strip resolves them via img.* CDN).
            var promoPrefix = $"{roomFolder}/Promo/Image/";
            foreach (var (k, pentry) in entries.Where(kv => kv.Key.StartsWith(promoPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                var fname = Path.GetFileName(k);
                if (await db.RoomDataBlobs.AnyAsync(b => b.BlobName == fname)) continue;
                var bytes = await ReadAllAsync(pentry);
                await WriteBlobAsync(0, fname, bytes, creator);
            }

            db.Rooms.Add(new RoomEntity
            {
                Id = roomId,
                Name = name,
                Description = details.Description?.Trim() ?? $"Imported from zip ({ordered.Count} scenes)",
                CreatorPlayerId = creator,
                ImageName = imageBlobName,
                State = details.State ?? 0,
                Accessibility = details.Accessibility ?? 1,
                IsAGRoom = false,
                IsDormRoom = details.IsDorm ?? false,
                CloningAllowed = details.CloningAllowed ?? true,
                SupportsLevelVoting = details.SupportsLevelVoting ?? false,
                SupportsVRLow = details.SupportsVRLow ?? true,
                SupportsMobile = details.SupportsMobile ?? false,
                SupportsScreens = details.SupportsScreens ?? true,
                SupportsWalkVR = details.SupportsWalkVR ?? true,
                SupportsTeleportVR = details.SupportsTeleportVR ?? true,
                AllowsJuniors = details.SupportsJuniors ?? true,
                RoomWarningMask = details.WarningMask ?? 0,
                CustomRoomWarning = details.CustomWarning ?? string.Empty,
                DisableMicAutoMute = details.DisableMicAutoMute ?? false,
                LocationReplicationId = entryLocation,
                TagsCsv = JoinTags(details.Tags),
                CheerCount = details.Stats?.CheerCount ?? 0,
                FavoriteCount = details.Stats?.FavoriteCount ?? 0,
                VisitCount = details.Stats?.VisitCount ?? 0,
                VisitorCount = details.Stats?.VisitorCount ?? 0,
                HotScore = 5.0,
                CurrentDataBlobName = MakeBlobName(entry.SceneName),
                CreatedAt = details.CreatedAt ?? DateTime.UtcNow,
            });

            int idx = 0;
            foreach (var s in ordered)
            {
                db.RoomScenes.Add(new RoomSceneEntity
                {
                    RoomId = roomId,
                    OrderIndex = idx++,
                    Name = s.SceneName,
                    RoomSceneLocationId = s.Manifest.UnitySceneId ?? entryLocation,
                    DataBlobName = MakeBlobName(s.SceneName),
                    MaxPlayers = s.Manifest.MaxPlayers ?? 8,
                    IsSandbox = s.Manifest.IsSandbox ?? false,
                    CanMatchmakeInto = true,
                    DataModifiedAt = DateTime.UtcNow,
                });
            }

            db.AdminActions.Add(new AdminActionEntity
            {
                AdminPlayerId = CurrentAdminId,
                Action = "zip_import_room",
                TargetType = "room",
                TargetId = roomId,
                Reason = $"name={name} scenes={ordered.Count} entry={entry.SceneName} src={entrySource} " +
                         $"htr={htrAssetCount}new+{htrAlreadyInDbAtStart}existed " +
                         $"pv={pvImageCount}new+{pvAlreadyInDbAtStart}existed " +
                         $"polaroids={polaroidCount}new+{polaroidAlreadyInDbAtStart}existed",
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        return new
        {
            name,
            ok = true,
            roomId,
            folder = roomFolder,
            sceneCount = ordered.Count,
            entryScene = entry.SceneName,
            entryDetectionSource = entrySource,
            missingScenes = missingScenes.Count > 0 ? missingScenes : null,
            tags = details.Tags?.Select(t => t.Tag).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            imageBlob = string.IsNullOrEmpty(imageBlobName) ? null : imageBlobName,
            htrAssets = new
            {
                referenced = uniqueHtrNames.Count,
                newlyImported = htrAssetCount,
                alreadyInDb = htrAlreadyInDbAtStart,
            },
            pvImages = new
            {
                referenced = uniquePvNames.Count,
                newlyImported = pvImageCount,
                alreadyInDb = pvAlreadyInDbAtStart,
            },
            polaroids = new
            {
                referenced = uniquePolaroidNames.Count,
                newlyImported = polaroidCount,
                alreadyInDb = polaroidAlreadyInDbAtStart,
            },
            // Legacy fields kept for back-compat with anything that
            // already reads them; new code should prefer htrAssets /
            // pvImages / polaroids above.
            htrAssetsImported = htrAssetCount,
            pvImagesImported = pvImageCount,
            stats = new
            {
                cheers = details.Stats?.CheerCount ?? 0,
                favorites = details.Stats?.FavoriteCount ?? 0,
                visitors = details.Stats?.VisitorCount ?? 0,
                visits = details.Stats?.VisitCount ?? 0,
            },
            scenes = ordered.Select((s, i) => new
            {
                orderIndex = i,
                name = s.SceneName,
                blobName = MakeBlobName(s.SceneName),
                bytes = s.Bytes.Length,
                rawBytes = s.RawBytes,
                normalized = s.NormalizedOk,
                unitySceneId = s.Manifest.UnitySceneId,
                isSandbox = s.Manifest.IsSandbox,
                maxPlayers = s.Manifest.MaxPlayers,
                htrAssets = s.HtrAssets.Count,
                pvImages = s.PvImages.Count,
            }),
        };
    }

    // ── Per-invention import ─────────────────────────────────────────

    private async Task<object> ImportInventionAsync(
        string folder,
        long creator,
        Dictionary<string, ZipArchiveEntry> entries)
    {
        // Invention.json
        var meta = await ReadJsonAsync<InventionJsonDto>(entries[$"{folder}/Invention.json"])
                   ?? new InventionJsonDto();
        InventionVersionDto version = entries.TryGetValue($"{folder}/InventionVersion.json", out var ve)
            ? await ReadJsonAsync<InventionVersionDto>(ve) ?? new InventionVersionDto()
            : new InventionVersionDto();
        InventionDetailsDto extras = entries.TryGetValue($"{folder}/InventionDetails.json", out var de)
            ? await ReadJsonAsync<InventionDetailsDto>(de) ?? new InventionDetailsDto()
            : new InventionDetailsDto();

        var name = !string.IsNullOrWhiteSpace(meta.Name) ? meta.Name!.Trim() : Path.GetFileName(folder);

        // Find the .inv blob — by manifest reference if present, else
        // first .inv file in the invention folder.
        ZipArchiveEntry? blobEntry = null;
        if (!string.IsNullOrWhiteSpace(version.BlobName)
            && entries.TryGetValue($"{folder}/{version.BlobName}", out var byManifest))
        {
            blobEntry = byManifest;
        }
        else
        {
            blobEntry = entries.FirstOrDefault(kv =>
                kv.Key.StartsWith($"{folder}/", StringComparison.OrdinalIgnoreCase)
                && kv.Key.EndsWith(".inv", StringComparison.OrdinalIgnoreCase)).Value;
        }
        if (blobEntry is null)
            return new { name, ok = false, error = "no_inv_blob", folder };

        // Image (optional)
        ZipArchiveEntry? imageEntry = null;
        if (!string.IsNullOrWhiteSpace(meta.ImageName)
            && entries.TryGetValue($"{folder}/{meta.ImageName}", out var byHash))
        {
            imageEntry = byHash;
        }
        else
        {
            imageEntry = entries.FirstOrDefault(kv =>
                kv.Key.StartsWith($"{folder}/InventionImage.", StringComparison.OrdinalIgnoreCase)).Value;
        }

        var blobBytes = await ReadAllAsync(blobEntry);

        var entity = new InventionEntity
        {
            CreatorPlayerId = creator,
            ReplicationId = !string.IsNullOrWhiteSpace(meta.ReplicationId)
                ? meta.ReplicationId!.Trim()
                : Guid.NewGuid().ToString("N"),
            Name = name,
            Description = meta.Description?.Trim() ?? string.Empty,
            ImageName = string.Empty, // set below after we know the blob name
            IsAgInvention = meta.IsAgInvention ?? true,
            IsPublished = false,
            Permission = meta.Accessibility ?? 0,
            CreatorPermission = meta.CreatorPermission ?? 100,
            GeneralPermission = meta.GeneralPermission ?? 0,
            CurrentVersionNumber = meta.CurrentVersionNumber ?? version.VersionNumber ?? 1,
            NumPlayersHaveUsedInRoom = meta.NumPlayersHaveUsedInRoom ?? 0,
            CheerCount = meta.CheerCount ?? 0,
            CreationRoomId = meta.CreationRoomId,
            TagsCsv = JoinTags(extras.Tags),
            CreatedAt = meta.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = meta.ModifiedAt ?? DateTime.UtcNow,
            FirstPublishedAt = meta.FirstPublishedAt,
        };
        db.Inventions.Add(entity);
        await db.SaveChangesAsync();

        var blobName = $"invention_{entity.Id}_v{entity.CurrentVersionNumber}.dat";
        entity.CurrentBlobName = blobName;
        await WriteBlobAsync(0, blobName, blobBytes, creator);

        string? imageBlobName = null;
        if (imageEntry is not null)
        {
            var bytes = await ReadAllAsync(imageEntry);
            var ext = Path.GetExtension(imageEntry.Name).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            imageBlobName = $"image_invention_{entity.Id}{ext}";
            entity.ImageName = imageBlobName;
            await WriteBlobAsync(0, imageBlobName, bytes, creator);
        }

        db.AdminActions.Add(new AdminActionEntity
        {
            AdminPlayerId = CurrentAdminId,
            Action = "zip_import_invention",
            TargetType = "invention",
            TargetId = entity.Id,
            Reason = $"name={name} blob={blobName} bytes={blobBytes.Length}",
        });
        await db.SaveChangesAsync();

        logger.LogInformation(
            "[zip-import] invention id={Id} name={Name} bytes={Bytes:N0} image={Image}",
            entity.Id, name, blobBytes.Length, imageBlobName ?? "(none)");

        return new
        {
            name,
            ok = true,
            inventionId = entity.Id,
            folder,
            blobName,
            imageBlob = imageBlobName,
            bytes = blobBytes.Length,
            tags = extras.Tags?.Select(t => t.Tag).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private static async Task<byte[]> ReadAllAsync(ZipArchiveEntry entry)
    {
        await using var s = entry.Open();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static async Task<T?> ReadJsonAsync<T>(ZipArchiveEntry entry)
    {
        await using var s = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(s, JsonOpts);
    }

    private static string JoinTags(List<TagDto>? tags)
    {
        if (tags is null || tags.Count == 0) return "community";
        var values = tags
            .Select(t => t.Tag?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count > 0 ? string.Join(',', values!) : "community";
    }

    private sealed record ResolvedSubroom(
        SubRoomDto Manifest,
        string SceneName,
        byte[] Bytes,
        int RawBytes,
        bool NormalizedOk,
        List<(string Name, ZipArchiveEntry Entry)> HtrAssets,
        List<(string Name, ZipArchiveEntry Entry)> PvImages,
        List<(string Name, ZipArchiveEntry Entry)> Polaroids);

    /// <summary>Decode RecNet's room-snapshot manifest <c>.meta</c> and
    /// return the entry-subroom index it encodes, or null if the file
    /// isn't shaped like one. The full <c>.proto</c> schema isn't part
    /// of the generated C# bindings, but the known-sample bytes are
    /// trivially small and parseable by hand:
    ///
    ///   10 01 1A 02 08 01
    ///   ┬──── ┬──────────
    ///   │     │
    ///   │     └─ field 3 (length-delimited), inner = { field 1 (varint) = 1 }
    ///   └─ field 2 (varint) = 1
    ///
    /// Empirically field 2 is the entry subroom INDEX into
    /// <c>RoomDetails.SubRooms[]</c> (0-based), and field 3's nested
    /// field 1 echoes the same value (probably "published version =
    /// 1"). We only need field 2 for the entry-selection decision.</summary>
    private static int? TryReadEntrySubroomIndexFromMeta(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            using var input = new CodedInputStream(bytes);
            while (!input.IsAtEnd)
            {
                var tag = input.ReadTag();
                var field = WireFormat.GetTagFieldNumber(tag);
                var wire = WireFormat.GetTagWireType(tag);
                if (field == 2 && wire == WireFormat.WireType.Varint)
                {
                    return (int)input.ReadInt64();
                }
                input.SkipLastField();
            }
        }
        catch
        {
            return null;
        }
        return null;
    }
}
