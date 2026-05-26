using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Admin;

/// <summary>
/// admin.*/api/admin/v1/storage/backfill — one-shot migrator that
/// moves the legacy <c>RoomDataBlobs.Bytes</c> column out of the
/// database and into S3 at the canonical key from
/// <see cref="BlobRouter.Route"/>. After every row's bytes have been
/// uploaded and verified, a follow-up EF migration drops the
/// <c>Bytes</c> column entirely.
///
/// Why this exists: every controller that writes new content already
/// targets S3 directly (the importer, photo upload, profile image,
/// dorm save, room save, …). The <c>Bytes</c> column only holds
/// pre-cutover data — rooms imported before the S3-only refactor
/// landed. This endpoint drains that legacy column into S3 once and
/// then we never look at it again.
///
/// Runs as a background task so the HTTP request returns immediately
/// even for a multi-GB drain. Progress streams to the server log and
/// to a small in-memory <see cref="BackfillStatus"/> snapshot the UI
/// can poll. Idempotent — each row is checked against
/// <see cref="IObjectStorage.ExistsAsync"/> first, and rows whose
/// bytes are already in S3 get their DB <c>Bytes</c> cleared without
/// re-upload.
/// </summary>
[ApiController]
[Route("api/admin/v1/storage")]
[Authorize]
[AdminOnly]
public class StorageBackfillController(
    ILogger<StorageBackfillController> log,
    IServiceScopeFactory scopeFactory) : ControllerBase
{
    private static readonly object _gate = new();
    private static BackfillStatus _status = BackfillStatus.Idle;

    /// <summary>POST <c>/storage/backfill?dryRun=true</c> — start a
    /// drain pass. Returns 200 immediately; the actual work happens
    /// in a background task. Re-POSTing while running returns 409.
    /// Set <c>?dryRun=true</c> on the first pass to log every
    /// (row → bucket/key) decision without writing to S3 or clearing
    /// the DB <c>Bytes</c> column.</summary>
    [HttpPost("backfill")]
    public IActionResult Start([FromQuery] bool dryRun = false)
    {
        lock (_gate)
        {
            if (_status.Running)
                return Conflict(new { error = "already_running", since = _status.StartedAt });
            _status = BackfillStatus.Starting(dryRun);
        }
        _ = Task.Run(() => RunAsync(dryRun));
        return Ok(new { started = true, dryRun });
    }

    /// <summary>GET <c>/storage/backfill/status</c> — current
    /// progress. Updated row-by-row during a run.</summary>
    [HttpGet("backfill/status")]
    public IActionResult Status() => Ok(_status);

    private async Task RunAsync(bool dryRun)
    {
        var startedAt = DateTime.UtcNow;

        try
        {
            // Phase 1: pull the list of row ids that still have bytes.
            // Materialise just the ids (not the bytes) up-front so we
            // don't hold a reader open across per-row S3 PUTs — that
            // would conflict with the per-row ExecuteUpdateAsync on
            // Npgsql, which doesn't allow a second command while a
            // reader is mid-iteration. Bytes get fetched one row at a
            // time below so RAM only holds one blob at a time.
            // Filter on Bytes != null only — Npgsql can't translate
            // .Length on a byte[] property (it sees it as Enumerable.Any
            // and bails). Empty-byte-array rows don't exist in our
            // schema; rows either hold real bytes or NULL after backfill.
            // We still re-check Length on the materialised row before
            // uploading, defensive against any future client-evaluated
            // edge case.
            List<long> rowIds;
            long total;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var dbScoped = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
                rowIds = await dbScoped.RoomDataBlobs
                    .AsNoTracking()
                    .Where(b => b.Bytes != null)
                    .OrderBy(b => b.Id)
                    .Select(b => b.Id)
                    .ToListAsync();
                total = rowIds.Count;
            }
            log.LogInformation(
                "[backfill] starting — rowsWithBytes={Total} dryRun={DryRun}",
                total, dryRun);
            lock (_gate) _status = _status with { Total = total };

            // Phase 2: per-row upload + clear. Each row gets its own
            // short-lived DbContext so the reader/writer conflict is
            // structurally impossible. Memory-bound at one row's
            // bytes plus query overhead.
            foreach (var rowId in rowIds)
            {
                await using var rowScope = scopeFactory.CreateAsyncScope();
                var rowDb = rowScope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
                var storageScoped = rowScope.ServiceProvider.GetRequiredService<IObjectStorage>();

                var row = await rowDb.RoomDataBlobs
                    .AsNoTracking()
                    .Where(b => b.Id == rowId)
                    .Select(b => new { b.Id, b.BlobName, b.Bytes })
                    .FirstOrDefaultAsync();
                if (row is null || row.Bytes is null || row.Bytes.Length == 0)
                {
                    Bump(s => s with { Skipped = s.Skipped + 1 });
                    continue;
                }

                var (bucket, key) = BlobRouter.Route(row.BlobName);
                try
                {
                    if (dryRun)
                    {
                        log.LogInformation(
                            "[backfill:dry] {Blob} → {Bucket}/{Key} ({Bytes} B)",
                            row.BlobName, bucket, key, row.Bytes.Length);
                        Bump(s => s with { Uploaded = s.Uploaded + 1, LastBlobName = row.BlobName });
                        continue;
                    }

                    // Upload unconditionally (S3 PUT is idempotent /
                    // overwriting). Verifying with ExistsAsync first
                    // would let us skip already-mirrored rows, but a
                    // mismatch between S3 and DB bytes (corrupted
                    // earlier upload, partial backfill, etc.) would
                    // leave the divergence in place. Re-uploading
                    // guarantees S3 == DB before we clear DB.
                    var contentType = MimeFromName(row.BlobName);
                    // Per-row timeout: 4 minutes generous enough for a
                    // 100 MB blob over a 5 Mbps origin link, well
                    // inside the 5-minute S3 SDK ceiling. Without a
                    // CT, a hung S3 connection could freeze the whole
                    // drain indefinitely.
                    using var putCt = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                    await storageScoped.PutAsync(bucket, key, row.Bytes, contentType, putCt.Token);

                    // SAFETY GATE: confirm S3 actually persisted the
                    // object before dropping the DB copy. PutAsync
                    // returning success isn't enough — some S3 impls
                    // can ack a write that disappears (eventual
                    // consistency, replication lag, write buffer
                    // dropped on shutdown). HEAD costs nothing.
                    using var headCt = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    if (!await storageScoped.ExistsAsync(bucket, key, headCt.Token))
                        throw new InvalidOperationException(
                            $"S3 PUT {bucket}/{key} returned success but ExistsAsync says no — refusing to clear DB bytes");

                    await rowDb.RoomDataBlobs
                        .Where(b => b.Id == row.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(b => b.Bytes, (byte[]?)null));
                    Bump(s => s with { Uploaded = s.Uploaded + 1, LastBlobName = row.BlobName });
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "[backfill] failed {Blob} → {Bucket}/{Key} — DB bytes left intact",
                        row.BlobName, bucket, key);
                    Bump(s => s with { Failed = s.Failed + 1, LastBlobName = row.BlobName });
                }
            }

            var elapsed = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            lock (_gate) _status = _status with
            {
                Running = false,
                FinishedAt = DateTime.UtcNow,
                ElapsedMs = elapsed,
            };
            log.LogInformation(
                "[backfill] finished — total={Total} uploaded={Up} alreadyMirrored={Already} skipped={Skip} failed={Fail} dryRun={Dry} elapsed={Ms}ms",
                _status.Total, _status.Uploaded, _status.AlreadyMirrored, _status.Skipped, _status.Failed, dryRun, elapsed);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[backfill] crashed");
            lock (_gate) _status = _status with { Running = false, FinishedAt = DateTime.UtcNow, Error = ex.Message };
        }
    }

    private static void Bump(Func<BackfillStatus, BackfillStatus> mutate)
    {
        lock (_gate) _status = mutate(_status);
    }

    private static string MimeFromName(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png"  => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif"  => "image/gif",
        ".webp" => "image/webp",
        ".mp4"  => "video/mp4",
        _ => "application/octet-stream",
    };

    public sealed record BackfillStatus(
        bool Running,
        bool DryRun,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        long Total,
        long Uploaded,
        long AlreadyMirrored,
        long Skipped,
        long Failed,
        string? LastBlobName,
        int? ElapsedMs,
        string? Error)
    {
        public static BackfillStatus Idle => new(false, false, null, null, 0, 0, 0, 0, 0, null, null, null);
        public static BackfillStatus Starting(bool dryRun) =>
            new(true, dryRun, DateTime.UtcNow, null, 0, 0, 0, 0, 0, null, null, null);
    }
}
