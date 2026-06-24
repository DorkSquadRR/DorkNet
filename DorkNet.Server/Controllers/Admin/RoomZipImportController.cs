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
    IServiceScopeFactory scopeFactory,
    ILogger<RoomZipImportController> logger) : ControllerBase
{
    private long CurrentAdminId => this.RequireCurrentPlayerId();
    private const long MaxArchiveBytes = 25_000_000_000L;

    // ── Room-blob normaliser toggle ─────────────────────────────────────
    // The normaliser does two things now (see RoomBlobNormalizerService):
    //   1. Project modern Rec Room's TransformData.quaternion_rotation
    //      (field 6) onto the legacy Euler rotation Vector3 (field 2)
    //      the 2020.12 watch reads. Without this, every shape in a
    //      modern-authored room renders at rotation=(0,0,0) — the
    //      "Studio-imported shapes are misplaced" bug.
    //   2. Re-encode through the 2020 PersistedRoomData schema to drop
    //      any non-canonical wire encodings the watch's stricter parser
    //      doesn't accept.
    //
    // Per-import flag (`normalizeBlobs` in the form / finalize body)
    // defaults ON now that the projection pass exists. The SPA still
    // surfaces it as a checkbox so an admin can opt out for an
    // already-Euler-encoded blob if a particular import misbehaves.
    // The diagnostic parse always runs (its parse-OK / parse-FAIL log
    // line is useful telemetry).

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
        string? referencesCsv = null, string? contentTypeOverride = null,
        DateTime? uploadedAt = null, long? subRoomId = null)
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
            UploadedAt = uploadedAt ?? DateTime.UtcNow,
            ReferencedFilenamesCsv = referencesCsv ?? string.Empty,
            SubRoomId = subRoomId,
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
        ".assetbundle" => "application/octet-stream",
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

    /// <summary>Sidecar JSON next to a History/&lt;blob&gt;.room entry. All
    /// fields optional — the importer reads them to populate
    /// RoomDataBlobEntity.UploadedAt + audit trail when present, and falls
    /// back to <see cref="DateTime.UtcNow"/> for the timestamp otherwise.</summary>
    public sealed class HistorySidecarDto
    {
        public long? SubRoomDataSaveId { get; set; }
        /// <summary>Parent sub-room id. The Restore UI fetches
        /// <c>rooms/{roomId}/subrooms/{subRoomId}/datahistory</c>; without
        /// a SubRoomId on each row we'd have to return the whole room's
        /// blob mix under every sub-room's history list. Optional: when
        /// missing, the importer derives it from the SubRooms/&lt;scene&gt;
        /// folder name suffix (<c>SubRoomName__&lt;subRoomId&gt;</c>).</summary>
        public long? SubRoomId { get; set; }
        public string? UnityAssetId { get; set; }
        public string? DataBlob { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? Description { get; set; }
        public long? SavedByAccountId { get; set; }
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
    [RequestFormLimits(MultipartBodyLengthLimit = MaxArchiveBytes)]
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
            normalizeBlobs: normalizeBlobs ?? true);
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
    /// chunk POSTs. Capped at 5 GB total; opens a sparse temp file so
    /// disk usage tracks actual bytes received. The cap is generous
    /// because the live RecNet exports of large story-mode rooms
    /// (large multi-scene Studio quests) easily
    /// run 2-3 GB before any of the per-room recovery zip bundling.</summary>
    [HttpPost("zip-upload-init")]
    public ActionResult ChunkInit([FromBody] ChunkInitRequest body)
    {
        if (body.TotalBytes <= 0 || body.TotalBytes > MaxArchiveBytes)
            return BadRequest(new { error = "invalid_size", maxBytes = MaxArchiveBytes, gotBytes = body.TotalBytes });

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

    /// <summary>In-memory background job state for an async import.
    /// The HTTP request that kicked the import off only stays alive
    /// for milliseconds (it returns 202 immediately); the work runs
    /// in <c>Task.Run</c> against a freshly-created DI scope so it
    /// outlives the request. The SPA polls
    /// <c>GET zip-import-status/{jobId}</c> for progress + result.
    /// Jobs auto-expire 1 hour after finish (so the dictionary doesn't
    /// grow forever).</summary>
    public sealed class ImportJob
    {
        public Guid Id { get; init; }
        public long AdminPlayerId { get; init; }
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }
        /// <summary>"running" | "done" | "failed".</summary>
        public string State { get; set; } = "running";
        /// <summary>Short progress string updated by the import core
        /// after each room/invention completes ("room 5/12: …").</summary>
        public string? Phase { get; set; }
        /// <summary>On success: the same anonymous payload the sync
        /// endpoint would have returned. On failure (validation
        /// error): the <c>{ error = ..., ... }</c> object the sync
        /// endpoint would have wrapped in BadRequest.</summary>
        public object? Result { get; set; }
        /// <summary>Exception message when <see cref="State"/> is
        /// "failed". <see cref="Result"/> stays null in that case.</summary>
        public string? Error { get; set; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ImportJob> Jobs = new();

    /// <summary>POST <c>zip-upload-finalize/{id}</c> — kicks off the
    /// import on the assembled temp file in a background task and
    /// returns 202 immediately with a <c>jobId</c>. The synchronous
    /// path got 524'd by Cloudflare on multi-GB archives (edge
    /// disconnects clients at 100s but the server kept processing
    /// successfully). Now: the upload session is consumed here, the
    /// finalize request returns before any timeout, and the SPA polls
    /// the status endpoint for completion.</summary>
    [HttpPost("zip-upload-finalize/{id:guid}")]
    public ActionResult ChunkFinalize(Guid id, [FromBody] ChunkFinalizeRequest? body)
    {
        if (!Sessions.TryGetValue(id, out var session)) return NotFound(new { error = "unknown_session" });
        var adminId = CurrentAdminId;
        if (session.AdminPlayerId != adminId) return Forbid();

        // Capture before we leave the request context.
        var tempPath = session.TempPath;
        var totalBytes = session.TotalBytes;
        var creator = body?.CreatorPlayerId ?? adminId;
        var selectedRoomFolders = body?.SelectedRoomFolders;
        var selectedInventionFolders = body?.SelectedInventionFolders;
        var normalizeBlobs = body?.NormalizeBlobs ?? true;

        var jobId = Guid.NewGuid();
        var job = new ImportJob { Id = jobId, AdminPlayerId = adminId };
        Jobs[jobId] = job;
        Sessions.TryRemove(id, out _);

        // Prune jobs older than 1 hour past completion to keep the
        // dictionary bounded. Admin only runs the importer a few times
        // a day, so this stays tiny.
        foreach (var (jid, j) in Jobs)
        {
            if (j.FinishedAt is { } fin && DateTime.UtcNow - fin > TimeSpan.FromHours(1))
                Jobs.TryRemove(jid, out _);
        }

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            // Build a fresh background controller instance bound to
            // the new scope's services. The original `this` controller
            // is tied to the request scope and will be disposed the
            // moment we return below — we cannot reference its `db`,
            // `storage`, etc. from the bg task.
            var bgDb = sp.GetRequiredService<DorkNetDbContext>();
            var bgNorm = sp.GetRequiredService<RoomBlobNormalizerService>();
            var bgStorage = sp.GetRequiredService<IObjectStorage>();
            var bgLogger = sp.GetRequiredService<ILogger<RoomZipImportController>>();
            var bgController = new RoomZipImportController(bgDb, bgNorm, bgStorage, scopeFactory, bgLogger);

            try
            {
                await using var stream = System.IO.File.OpenRead(tempPath);
                var outcome = await bgController.RunImportCoreAsync(
                    stream, totalBytes, creator, adminId,
                    selectedRoomFolders, selectedInventionFolders, normalizeBlobs,
                    jobPhase: p => job.Phase = p);
                job.Result = outcome.Payload;
                job.State = outcome.Success ? "done" : "failed";
            }
            catch (Exception ex)
            {
                bgLogger.LogError(ex, "[zip-import job {Job}] failed", jobId);
                job.Error = ex.Message;
                job.State = "failed";
            }
            finally
            {
                job.FinishedAt = DateTime.UtcNow;
                try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
                catch (Exception ex) { bgLogger.LogWarning(ex, "[zip-import] failed to clean up {Path}", tempPath); }
            }
        });

        return Accepted(new
        {
            jobId,
            state = job.State,
            statusUrl = $"/api/admin/v1/rooms/zip-import-status/{jobId}",
        });
    }

    /// <summary>GET <c>zip-import-status/{jobId}</c> — SPA polls this
    /// every few seconds while the import is running. Returns the
    /// final result payload once <c>state == "done"</c> or
    /// <c>"failed"</c>; on "failed" the payload either includes a
    /// validation error (creator_not_found etc.) or just an
    /// <c>error</c> string from the caught exception.</summary>
    [HttpGet("zip-import-status/{jobId:guid}")]
    public ActionResult GetImportStatus(Guid jobId)
    {
        if (!Jobs.TryGetValue(jobId, out var job)) return NotFound(new { error = "unknown_job" });
        if (job.AdminPlayerId != CurrentAdminId) return Forbid();
        return Ok(new
        {
            jobId = job.Id,
            state = job.State,
            phase = job.Phase,
            startedAt = job.StartedAt,
            finishedAt = job.FinishedAt,
            elapsedSeconds = ((job.FinishedAt ?? DateTime.UtcNow) - job.StartedAt).TotalSeconds,
            result = job.Result,
            error = job.Error,
        });
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

    /// <summary>Outcome of a single zip import. Wraps both success
    /// payloads and validation errors into one object so the same
    /// import code can serve the sync <c>zip-bulk-import</c> endpoint
    /// AND the async <c>zip-upload-finalize</c> background job (which
    /// has no <see cref="IActionResult"/> to return — it stores the
    /// outcome in an <see cref="ImportJob"/> for the SPA to poll).</summary>
    public sealed class ImportOutcome
    {
        /// <summary>true → payload is the success response;
        /// false → payload includes an "error" property and the
        /// sync endpoint should return 400.</summary>
        public bool Success { get; init; }
        /// <summary>The anonymous payload that would have been wrapped
        /// in Ok()/BadRequest() in the sync path.</summary>
        public object Payload { get; init; } = new { };
    }

    private async Task<IActionResult> RunImportAsync(
        Stream archiveStream,
        long archiveBytes,
        long? creatorPlayerId,
        List<string>? selectedRoomFolders = null,
        List<string>? selectedInventionFolders = null,
        bool normalizeBlobs = false)
    {
        var adminId = CurrentAdminId;
        var outcome = await RunImportCoreAsync(
            archiveStream, archiveBytes,
            creatorPlayerId ?? adminId,
            adminId,
            selectedRoomFolders, selectedInventionFolders, normalizeBlobs,
            jobPhase: null);
        return outcome.Success
            ? Ok(outcome.Payload)
            : BadRequest(outcome.Payload);
    }

    /// <summary>Pure import worker: no HttpContext access (caller passes
    /// the resolved <paramref name="creator"/>), no IActionResult
    /// wrapping. Background jobs call this via a freshly-resolved
    /// scope so they can outlive the request that kicked them off.
    /// <paramref name="jobPhase"/>, when non-null, gets updated with a
    /// short progress string after each room/invention is processed so
    /// the status endpoint can report meaningful progress.</summary>
    private async Task<ImportOutcome> RunImportCoreAsync(
        Stream archiveStream,
        long archiveBytes,
        long creator,
        long adminId,
        List<string>? selectedRoomFolders,
        List<string>? selectedInventionFolders,
        bool normalizeBlobs,
        Action<string>? jobPhase)
    {
        if (!await db.Players.AnyAsync(p => p.Id == creator))
            return new ImportOutcome { Success = false, Payload = new { error = "creator_not_found", creator } };

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

        var studioRoomFolders = entryByPath.Keys
            .Where(k => k.EndsWith("/room.json", StringComparison.OrdinalIgnoreCase))
            .Select(k => k[..^"/room.json".Length])
            .Where(f => HasStudioSaveEntries(f, entryByPath.Keys))
            .Where(f => !roomFolders.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();
        roomFolders.AddRange(studioRoomFolders);

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
            return new ImportOutcome { Success = false, Payload = new { error = "empty_archive_no_manifests" } };

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

        var roomIdx = 0;
        foreach (var folder in roomFolders)
        {
            roomIdx++;
            jobPhase?.Invoke($"room {roomIdx}/{roomFolders.Count}: {folder}");
            try
            {
                var result = await ImportRoomAsync(folder, nextRoomId, creator, adminId, entryByPath, normalizeBlobs);
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
        var invIdx = 0;
        foreach (var folder in inventionFolders)
        {
            invIdx++;
            jobPhase?.Invoke($"invention {invIdx}/{inventionFolders.Count}: {folder}");
            try { inventionReports.Add(await ImportInventionAsync(folder, creator, adminId, entryByPath)); }
            catch (Exception ex)
            {
                logger.LogError(ex, "[zip-import] invention folder='{Folder}' failed", folder);
                inventionReports.Add(new { folder, ok = false, error = ex.Message });
            }
        }

        jobPhase?.Invoke($"done: {roomFolders.Count} rooms, {inventionFolders.Count} inventions");
        return new ImportOutcome
        {
            Success = true,
            Payload = new
            {
                archiveBytes,
                roomCount = roomFolders.Count,
                inventionCount = inventionFolders.Count,
                rooms = roomReports,
                inventions = inventionReports,
            },
        };
    }

    // ── Per-room import ───────────────────────────────────────────────

    private async Task<object> ImportRoomAsync(
        string roomFolder,
        long roomId,
        long creator,
        long adminId,
        Dictionary<string, ZipArchiveEntry> entries,
        bool normalizeBlobs)
    {
        // RoomDetails.json. Studio single-room dumps use room.json at
        // the room root; it is the same rooms.rec.net detail payload and
        // can be read through RoomDetailsDto directly.
        var detailsKey = $"{roomFolder}/RoomDetails.json";
        var studioDetailsKey = $"{roomFolder}/room.json";
        ZipArchiveEntry detailsEntry;
        if (entries.TryGetValue(detailsKey, out var recNetDetailsEntry))
            detailsEntry = recNetDetailsEntry;
        else if (entries.TryGetValue(studioDetailsKey, out var studioDetailsEntry))
            detailsEntry = studioDetailsEntry;
        else
            throw new InvalidOperationException($"Missing RoomDetails.json or room.json in {roomFolder}");

        var details = await ReadJsonAsync<RoomDetailsDto>(detailsEntry) ?? new RoomDetailsDto();
        var name = !string.IsNullOrWhiteSpace(details.Name) ? details.Name!.Trim() : StripStudioSuffix(ZipFileName(roomFolder));

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

        if (scenes.Count == 0 && entries.ContainsKey(studioDetailsKey))
        {
            (resolvedScenes, missingScenes) = await ResolveStudioDumpSubroomsAsync(
                roomFolder, details, name, entries, normalizeBlobs);
        }

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

            // History/ folder — every non-canonical save dropped by the
            // studio-dump-to-import-zip converter (with --every-save). Each
            // `<DataBlob>.room` becomes a separate RoomDataBlobEntity row so
            // it shows up in `GET api/rooms/v1/datahistory/{roomId}`. The
            // optional `<DataBlob>.json` sidecar carries CreatedAt; missing
            // sidecars fall back to "now".
            var history = new List<HistorySave>();
            var historyPrefix = $"{sceneFolder}/History/";
            // Derive SubRoomId from the folder name (SubRooms/<scene>) when
            // the sidecar omits it. The converter names scene folders as
            // either the rec.net Name (no id), or — when running our own
            // dumper — the raw `<scene>__<subRoomId>`. Falling back to
            // manifest.SubRoomId catches the no-suffix case.
            long? subRoomIdFromFolder = null;
            var lastUnderscores = sceneName.LastIndexOf("__", StringComparison.Ordinal);
            if (lastUnderscores >= 0
                && long.TryParse(sceneName[(lastUnderscores + 2)..], out var parsedSub))
            {
                subRoomIdFromFolder = parsedSub;
            }
            var manifestSub = manifest.SubRoomId;
            foreach (var (k, hEntry) in entries.Where(kv =>
                kv.Key.StartsWith(historyPrefix, StringComparison.OrdinalIgnoreCase)
                && kv.Key.EndsWith(".room", StringComparison.OrdinalIgnoreCase)
                && !kv.Key[historyPrefix.Length..].Contains('/')))
            {
                var blobName = Path.GetFileName(k);
                var sidecarKey = $"{k}.json";
                HistorySidecarDto? sidecar = null;
                if (entries.TryGetValue(sidecarKey, out var sidecarEntry))
                    sidecar = await ReadJsonAsync<HistorySidecarDto>(sidecarEntry);
                history.Add(new HistorySave(
                    DataBlob: blobName,
                    RoomEntry: hEntry,
                    SubRoomId: sidecar?.SubRoomId ?? subRoomIdFromFolder ?? manifestSub,
                    SubRoomDataSaveId: sidecar?.SubRoomDataSaveId,
                    CreatedAt: sidecar?.CreatedAt,
                    Description: sidecar?.Description,
                    SavedByAccountId: sidecar?.SavedByAccountId));
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
                StudioUnityAssetId: null,
                HtrAssets: htrAssets,
                PvImages: pvImages,
                Polaroids: polaroids,
                AssetBundles: new List<(string Name, ZipArchiveEntry Entry)>(),
                History: history));
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
        imageEntry ??= FindStudioPhotoEntry(roomFolder, details.ImageName, entries);

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
        var assetBundleCount = 0;
        var allHtrAssets = ordered.SelectMany(s => s.HtrAssets).ToList();
        var allPvImages = ordered.SelectMany(s => s.PvImages).ToList();
        var allPolaroids = ordered.SelectMany(s => s.Polaroids).ToList();
        var allAssetBundles = ordered.SelectMany(s => s.AssetBundles).ToList();

        // De-dupe within the archive first (same .htr can appear under
        // multiple scenes), then de-dupe against the DB. Both layers
        // matter for accurate reporting.
        var uniqueHtrNames = allHtrAssets.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var uniquePvNames  = allPvImages.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var uniquePolaroidNames = allPolaroids.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var uniqueAssetBundleNames = allAssetBundles.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
        var existingAssetBundleNames = new HashSet<string>(
            await db.RoomDataBlobs
                .Where(b => uniqueAssetBundleNames.Contains(b.BlobName))
                .Select(b => b.BlobName).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var htrAlreadyInDbAtStart = existingHtrNames.Count;
        var pvAlreadyInDbAtStart  = existingPvNames.Count;
        var polaroidAlreadyInDbAtStart = existingPolaroidNames.Count;
        var assetBundleAlreadyInDbAtStart = existingAssetBundleNames.Count;

        // History (SubRooms/<scene>/History/<DataBlob>.room) — pre-load
        // existing names so the de-dup HashSet covers prior imports + same
        // hash referenced under multiple scenes.
        var allHistoryNames = ordered
            .SelectMany(s => s.History.Select(h => h.DataBlob))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingHistoryNames = new HashSet<string>(
            await db.RoomDataBlobs
                .Where(b => allHistoryNames.Contains(b.BlobName))
                .Select(b => b.BlobName).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);
        var historyAlreadyInDbAtStart = existingHistoryNames.Count;
        var historyImported = 0;
        var historySkipped = 0;

        var imageBlobName = string.Empty;

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // Subroom blobs — canonical save per scene + every History/ entry
            // restoring the rec.net DataBlob filename verbatim so the in-game
            // `Restore` UI's `datahistory` list shows up as expected.
            //
            // De-dup the History writes the same way HTR / PV / Polaroid
            // sections do: pre-load existing BlobNames from the DB, then
            // append in-memory as we queue each new row. Without this the
            // unique index `IX_RoomDataBlobs_BlobName` fires on the
            // SaveChanges when two scenes share a history hash (common: a
            // baseline blob that didn't change between subroom edits).
            var seenHistoryNames = new HashSet<string>(
                existingHistoryNames, StringComparer.OrdinalIgnoreCase);
            // Watch's URL uses RoomSceneEntity.OrderIndex (e.g. /datahistory
            // for `subRoom=11` is asking for the scene at OrderIndex 11),
            // NOT the rec.net SubRoomId from the manifest (e.g. 9717143).
            // RoomDataBlobs.SubRoomId must store the OrderIndex so the
            // datahistory query (`WHERE SubRoomId = subRoomId`) matches.
            // Use the same idx that the RoomSceneEntity insert loop below
            // assigns — both iterate `ordered` in lockstep.
            int historyOrderIdx = 0;
            foreach (var s in ordered)
            {
                await WriteBlobAsync(roomId, MakeBlobName(s.SceneName), s.Bytes, creator);

                foreach (var h in s.History)
                {
                    if (!seenHistoryNames.Add(h.DataBlob))
                    {
                        historySkipped++;
                        continue;
                    }
                    var hBytes = await ReadAllAsync(h.RoomEntry);
                    // Don't normalise historical blobs — they're a snapshot of
                    // exactly what rec.net served at the time, and replaying
                    // them through the normaliser would mutate the bytes
                    // in subtle ways that obscure provenance. The watch reads
                    // them via CDN just like a normal save, so the same
                    // protobuf parser applies anyway.
                    await WriteBlobAsync(
                        roomId: roomId,
                        blobName: h.DataBlob,
                        bytes: hBytes,
                        uploaderPlayerId: h.SavedByAccountId ?? creator,
                        uploadedAt: h.CreatedAt,
                        subRoomId: historyOrderIdx);
                    historyImported++;
                }
                historyOrderIdx++;
            }
            logger.LogInformation(
                "[zip-import] room='{Room}' history imports={Imported} (skipped={Skipped})",
                name, historyImported, historySkipped);
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
            // Studio baked Unity asset bundles. Keep original filenames;
            // Studio save metadata points the 2023 client at
            // cdn.../room/<filename>.assetbundle.
            foreach (var (bundleName, bundleEntry) in allAssetBundles)
            {
                if (existingAssetBundleNames.Contains(bundleName)) continue;
                existingAssetBundleNames.Add(bundleName);
                var bytes = await ReadAllAsync(bundleEntry);
                await WriteBlobAsync(0, bundleName, bytes, creator);
                assetBundleCount++;
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
            var studioPromoPrefix = $"{roomFolder}/photos/";
            foreach (var (k, pentry) in entries.Where(kv =>
                kv.Key.StartsWith(studioPromoPrefix, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(kv.Key).StartsWith("promo", StringComparison.OrdinalIgnoreCase)))
            {
                var fname = $"PromoImage_{Path.GetFileName(k)}";
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
                IsStudioRoom = entries.ContainsKey(studioDetailsKey),
                IsRoomLinkedToRecRoomStudio = entries.ContainsKey(studioDetailsKey),
                StudioSessionId = details.RoomId?.ToString() ?? TryParseStudioId(ZipFileName(roomFolder))?.ToString() ?? string.Empty,
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
                TagsCsv = EnsureStudioTags(JoinTags(details.Tags), entries.ContainsKey(studioDetailsKey)),
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
                    StudioSubRoomDataSaveId = s.Manifest.CurrentSave?.SubRoomDataSaveId,
                    StudioUnityAssetId = s.StudioUnityAssetId ?? string.Empty,
                    StudioAssetBundleNamesCsv = string.Join(',', s.AssetBundles
                        .Select(a => a.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)),
                    MaxPlayers = s.Manifest.MaxPlayers ?? 8,
                    IsSandbox = s.Manifest.IsSandbox ?? false,
                    CanMatchmakeInto = true,
                    DataModifiedAt = DateTime.UtcNow,
                });
            }

            db.AdminActions.Add(new AdminActionEntity
            {
                AdminPlayerId = adminId,
                Action = "zip_import_room",
                TargetType = "room",
                TargetId = roomId,
                Reason = $"name={name} scenes={ordered.Count} entry={entry.SceneName} src={entrySource} " +
                         $"htr={htrAssetCount}new+{htrAlreadyInDbAtStart}existed " +
                         $"pv={pvImageCount}new+{pvAlreadyInDbAtStart}existed " +
                         $"polaroids={polaroidCount}new+{polaroidAlreadyInDbAtStart}existed " +
                         $"bundles={assetBundleCount}new+{assetBundleAlreadyInDbAtStart}existed",
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
            assetBundles = new
            {
                referenced = uniqueAssetBundleNames.Count,
                newlyImported = assetBundleCount,
                alreadyInDb = assetBundleAlreadyInDbAtStart,
            },
            history = new
            {
                referenced = allHistoryNames.Count,
                newlyImported = historyImported,
                alreadyInDb = historyAlreadyInDbAtStart,
                skipped = historySkipped,
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
                assetBundles = s.AssetBundles.Count,
            }),
        };
    }

    private async Task<(List<ResolvedSubroom> Resolved, List<string> Missing)> ResolveStudioDumpSubroomsAsync(
        string roomFolder,
        RoomDetailsDto details,
        string roomName,
        Dictionary<string, ZipArchiveEntry> entries,
        bool normalizeBlobs)
    {
        var resolvedScenes = new List<ResolvedSubroom>();
        var missingScenes = new List<string>();
        var rootPrefix = $"{roomFolder}/";
        var sceneFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in entries.Keys.Where(k => k.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var rel = path[rootPrefix.Length..];
            var slash = rel.IndexOf('/');
            if (slash <= 0) continue;
            var sceneFolder = rel[..slash];
            if (rel[(slash + 1)..].StartsWith("saves/", StringComparison.OrdinalIgnoreCase))
                sceneFolders.Add(sceneFolder);
        }

        foreach (var sceneFolderName in sceneFolders.OrderBy(StripStudioSuffix, StringComparer.OrdinalIgnoreCase))
        {
            var sceneName = StripStudioSuffix(sceneFolderName);
            var savesPrefix = $"{rootPrefix}{sceneFolderName}/saves/";
            var saves = new List<(long SaveId, HistorySidecarDto Sidecar, ZipArchiveEntry DataEntry)>();

            foreach (var (path, jsonEntry) in entries.Where(kv =>
                kv.Key.StartsWith(savesPrefix, StringComparison.OrdinalIgnoreCase)
                && kv.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !kv.Key[savesPrefix.Length..].Contains('/')))
            {
                var stem = Path.GetFileNameWithoutExtension(path);
                if (!long.TryParse(stem, out var saveId)) continue;
                if (!entries.TryGetValue($"{savesPrefix}{saveId}__data.room", out var dataEntry))
                {
                    missingScenes.Add($"{sceneName} save {saveId} (missing {saveId}__data.room)");
                    continue;
                }
                var sidecar = await ReadJsonAsync<HistorySidecarDto>(jsonEntry) ?? new HistorySidecarDto();
                saves.Add((sidecar.SubRoomDataSaveId ?? saveId, sidecar, dataEntry));
            }

            if (saves.Count == 0)
            {
                missingScenes.Add($"{sceneName} (no usable saves)");
                continue;
            }

            var current = saves.OrderBy(s => s.SaveId).Last();
            var subroomDetails = details.SubRooms?.FirstOrDefault(s =>
                (s.SubRoomId.HasValue && s.SubRoomId == (current.Sidecar.SubRoomId ?? TryParseStudioId(sceneFolderName)))
                || string.Equals(s.Name, sceneName, StringComparison.OrdinalIgnoreCase));
            var manifest = new SubRoomDto
            {
                SubRoomId = current.Sidecar.SubRoomId ?? subroomDetails?.SubRoomId ?? TryParseStudioId(sceneFolderName),
                UnitySceneId = subroomDetails?.UnitySceneId,
                Name = subroomDetails?.Name ?? sceneName,
                IsSandbox = subroomDetails?.IsSandbox ?? true,
                MaxPlayers = subroomDetails?.MaxPlayers ?? details.MaxPlayers ?? 8,
                Accessibility = subroomDetails?.Accessibility ?? details.Accessibility ?? 1,
                CurrentSave = new CurrentSaveDto
                {
                    SubRoomDataSaveId = current.SaveId,
                    DataBlob = current.Sidecar.DataBlob ?? ZipFileName(current.DataEntry.FullName),
                },
            };

            var blobBytes = await ReadAllAsync(current.DataEntry);
            var norm = roomBlobNormalizer.Normalize(blobBytes);
            var persistedBytes = normalizeBlobs ? norm.Bytes : blobBytes;

            var htrAssets = new List<(string Name, ZipArchiveEntry Entry)>();
            var pvImages = new List<(string Name, ZipArchiveEntry Entry)>();
            var polaroids = new List<(string Name, ZipArchiveEntry Entry)>();
            var assetBundles = new List<(string Name, ZipArchiveEntry Entry)>();

            foreach (var save in saves)
            {
                var bundlePrefix = $"{savesPrefix}{save.SaveId}__bundle_";
                foreach (var (path, assetEntry) in entries.Where(kv =>
                    kv.Key.StartsWith(bundlePrefix, StringComparison.OrdinalIgnoreCase)
                    && kv.Key.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase)
                    && !kv.Key[bundlePrefix.Length..].Contains('/')))
                {
                    assetBundles.Add((Path.GetFileName(path), assetEntry));
                }

                var refPrefix = $"{savesPrefix}{save.SaveId}__ref_";
                foreach (var (path, assetEntry) in entries.Where(kv =>
                    kv.Key.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase)
                    && !kv.Key[refPrefix.Length..].Contains('/')))
                {
                    var assetName = path[refPrefix.Length..];
                    var ext = Path.GetExtension(assetName).ToLowerInvariant();
                    if (ext == ".htr")
                    {
                        htrAssets.Add((assetName, assetEntry));
                    }
                    else if (IsImageExt(ext))
                    {
                        if (assetName.StartsWith("PVImage_", StringComparison.OrdinalIgnoreCase))
                        {
                            pvImages.Add((assetName, assetEntry));
                            polaroids.Add((assetName["PVImage_".Length..], assetEntry));
                        }
                        else
                        {
                            pvImages.Add(($"PVImage_{assetName}", assetEntry));
                            polaroids.Add((assetName, assetEntry));
                        }
                    }
                }
            }

            var history = new List<HistorySave>();
            foreach (var save in saves.Where(s => s.SaveId != current.SaveId))
            {
                history.Add(new HistorySave(
                    DataBlob: !string.IsNullOrWhiteSpace(save.Sidecar.DataBlob)
                        ? save.Sidecar.DataBlob!
                        : ZipFileName(save.DataEntry.FullName),
                    RoomEntry: save.DataEntry,
                    SubRoomId: save.Sidecar.SubRoomId ?? manifest.SubRoomId,
                    SubRoomDataSaveId: save.Sidecar.SubRoomDataSaveId ?? save.SaveId,
                    CreatedAt: save.Sidecar.CreatedAt,
                    Description: save.Sidecar.Description,
                    SavedByAccountId: save.Sidecar.SavedByAccountId));
            }

            resolvedScenes.Add(new ResolvedSubroom(
                Manifest: manifest,
                SceneName: sceneName,
                Bytes: persistedBytes,
                RawBytes: blobBytes.Length,
                NormalizedOk: norm.Normalized,
                StudioUnityAssetId: current.Sidecar.UnityAssetId,
                HtrAssets: htrAssets,
                PvImages: pvImages,
                Polaroids: polaroids,
                AssetBundles: assetBundles,
                History: history));

            logger.LogInformation(
                "[zip-import] studio-dump room='{Room}' scene='{Scene}' save={SaveId} raw={Raw:N0} normOutput={Norm:N0} persistedFrom={Source} htr={Htr} pv={Pv} polaroids={Pol} bundles={Bundles} history={History}",
                roomName, sceneName, current.SaveId, blobBytes.Length, norm.Bytes.Length,
                normalizeBlobs ? "normaliser" : "raw",
                htrAssets.Count, pvImages.Count, polaroids.Count, assetBundles.Count, history.Count);
        }

        return (resolvedScenes, missingScenes);
    }

    // ── Per-invention import ─────────────────────────────────────────

    private async Task<object> ImportInventionAsync(
        string folder,
        long creator,
        long adminId,
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

        // Preserve the ORIGINAL Rec Room InventionId so the .room blob's
        // embedded invention references resolve. The watch's blob format
        // stores invention refs as `InventionId` longs (verified by
        // varint-scanning a known room blob); if we let EF auto-generate
        // a new Id, every invention-spawn reference inside the blob
        // points at a non-existent id → invention shape doesn't spawn →
        // big visible holes (floor missing, custom geometry absent, etc.).
        //
        // Skip the explicit-Id path when the json doesn't carry an
        // InventionId (corrupt exports) or when our local DB already has
        // a row with that id (cross-room collision: rare, but if a single
        // zip imports two rooms that both reference the same shared
        // invention with the same id, the second import would conflict —
        // fall back to auto-id and accept the lookup loss for that one).
        long? preservedId = null;
        if (meta.InventionId is long origId && origId > 0)
        {
            var idTaken = await db.Inventions.AsNoTracking().AnyAsync(x => x.Id == origId);
            if (!idTaken) preservedId = origId;
            else logger.LogWarning("[zip-import] invention id {Id} already exists in DB; using auto-id (refs from .room blob may not resolve)", origId);
        }

        // Map CreationRoomId from the ORIGINAL Rec Room room id (carried
        // in the json) to our LOCAL room id when we're importing the
        // invention alongside a room in the same archive. Without this
        // remap, /api/inventions/v1/room?id={localRoomId} returns nothing
        // (watch asks with our local id; the row carries the original
        // remote id). Fall back to the original id when we can't resolve
        // the local room (e.g. invention imported standalone).
        long? remappedRoomId = meta.CreationRoomId;
        if (meta.CreationRoomId is long origRoomId)
        {
            // Walk recent zip_import_room admin actions for the same
            // session to find which local room id maps to the original.
            // The Reason text encodes "name={Name}" but not the original
            // id; the room's Description sometimes hints. Simpler: match
            // by name when the room json's CreationRoomId equals
            // RoomDetails.RoomId we read earlier. We pass the local
            // room id in via the caller chain when available.
            //
            // For zips that import the room AND its inventions together,
            // ImportRoomAsync runs first and adds the room with the new
            // local id, so by the time ImportInventionAsync runs we can
            // look up the room by RoomDetails.RoomId == meta.CreationRoomId
            // (the original) — but we don't store that field. To avoid
            // an entity schema change here, attempt a lookup by the
            // local id matching the original (handles single-room imports
            // where the id happens to match) and otherwise leave as-is.
            // The watch endpoint will need to handle both id spaces.
            remappedRoomId = origRoomId;
        }

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
            CreationRoomId = remappedRoomId,
            TagsCsv = JoinTags(extras.Tags),
            CreatedAt = meta.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = meta.ModifiedAt ?? DateTime.UtcNow,
            FirstPublishedAt = meta.FirstPublishedAt,
        };
        if (preservedId is long pid) entity.Id = pid;
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

        // Insert the per-version row for the watch's
        // /api/inventions/v1/version?inventionId=X&version=N lookup.
        // Without this the watch's room-load chain can't resolve a
        // BlobName to download (returns 404 → invention spawns as empty
        // placeholder), even though the bytes are on disk and the
        // InventionEntity has CurrentBlobName set. The /v3/save path
        // (api/inventions) writes this row at create time — the zip
        // importer was missing the same step.
        var exists = await db.InventionVersions.AnyAsync(
            v => v.InventionId == entity.Id && v.VersionNumber == entity.CurrentVersionNumber);
        if (!exists)
        {
            db.InventionVersions.Add(new InventionVersionEntity
            {
                InventionId   = entity.Id,
                ReplicationId = entity.ReplicationId ?? Guid.NewGuid().ToString("D"),
                VersionNumber = entity.CurrentVersionNumber,
                BlobName      = blobName,
                // Costs aren't in our minimal InventionVersionDto today;
                // safe defaults until we extend the DTO to read them.
                InstantiationCost = 0,
                LightsCost        = 0,
            });
        }

        db.AdminActions.Add(new AdminActionEntity
        {
            AdminPlayerId = adminId,
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

    private static string ZipFileName(string path)
    {
        var normalized = Normalize(path).TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string StripStudioSuffix(string name)
    {
        var marker = name.LastIndexOf("__", StringComparison.Ordinal);
        return marker > 0 && long.TryParse(name[(marker + 2)..], out _)
            ? name[..marker]
            : name;
    }

    private static long? TryParseStudioId(string name)
    {
        var marker = name.LastIndexOf("__", StringComparison.Ordinal);
        return marker > 0 && long.TryParse(name[(marker + 2)..], out var id) ? id : null;
    }

    private static bool IsImageExt(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => true,
        _ => false,
    };

    private static bool HasStudioSaveEntries(string roomFolder, IEnumerable<string> paths)
    {
        var prefix = $"{roomFolder.TrimEnd('/')}/";
        return paths.Any(k =>
        {
            if (!k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !k.EndsWith("__data.room", StringComparison.OrdinalIgnoreCase))
                return false;
            var rel = k[prefix.Length..];
            var parts = rel.Split('/');
            return parts.Length == 3
                && parts[0].Contains("__", StringComparison.Ordinal)
                && string.Equals(parts[1], "saves", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ZipArchiveEntry? FindStudioPhotoEntry(
        string roomFolder,
        string? imageName,
        Dictionary<string, ZipArchiveEntry> entries)
    {
        var photosPrefix = $"{roomFolder}/photos/";
        if (!string.IsNullOrWhiteSpace(imageName))
        {
            var byImageName = entries.FirstOrDefault(kv =>
                kv.Key.StartsWith(photosPrefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(kv.Key), imageName, StringComparison.OrdinalIgnoreCase)).Value;
            if (byImageName is not null) return byImageName;

            var imageStem = Path.GetFileNameWithoutExtension(imageName);
            byImageName = entries.FirstOrDefault(kv =>
                kv.Key.StartsWith(photosPrefix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileNameWithoutExtension(kv.Key), imageStem, StringComparison.OrdinalIgnoreCase)).Value;
            if (byImageName is not null) return byImageName;
        }

        return entries.FirstOrDefault(kv =>
            kv.Key.StartsWith(photosPrefix, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileNameWithoutExtension(kv.Key), "main", StringComparison.OrdinalIgnoreCase)).Value;
    }

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

    private static string EnsureStudioTags(string tagsCsv, bool isStudioDump)
    {
        if (!isStudioDump) return tagsCsv;
        var values = (tagsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!values.Contains("community", StringComparer.OrdinalIgnoreCase))
            values.Add("community");
        if (!values.Contains("studio", StringComparer.OrdinalIgnoreCase))
            values.Add("studio");
        if (!values.Contains("developer", StringComparer.OrdinalIgnoreCase))
            values.Add("developer");
        return string.Join(',', values);
    }

    private sealed record ResolvedSubroom(
        SubRoomDto Manifest,
        string SceneName,
        byte[] Bytes,
        int RawBytes,
        bool NormalizedOk,
        string? StudioUnityAssetId,
        List<(string Name, ZipArchiveEntry Entry)> HtrAssets,
        List<(string Name, ZipArchiveEntry Entry)> PvImages,
        List<(string Name, ZipArchiveEntry Entry)> Polaroids,
        List<(string Name, ZipArchiveEntry Entry)> AssetBundles,
        List<HistorySave> History);

    /// <summary>One historical save under SubRooms/&lt;scene&gt;/History/.
    /// The .room blob bytes go into S3 under the original
    /// <see cref="DataBlob"/> filename so the watch's `Restore` UI can
    /// fetch them by name. The optional .json sidecar carries the
    /// original <see cref="CreatedAt"/> + provenance, which we mirror
    /// into <see cref="RoomDataBlobEntity.UploadedAt"/> so the in-game
    /// history list sorts in real chronological order.</summary>
    private sealed record HistorySave(
        string DataBlob,
        ZipArchiveEntry RoomEntry,
        long? SubRoomId,
        long? SubRoomDataSaveId,
        DateTime? CreatedAt,
        string? Description,
        long? SavedByAccountId);

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
