using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using RecRoom.Protobuf2020;

namespace DorkNet.Server.Services;

/// <summary>
/// Background mirror for the .htr assets that <c>HolotarPersistenceData</c>
/// and <c>AudioSamplerPersistenceData</c> embedded in a PersistedRoomData
/// blob reference. Each ref is a string BlobName (typically
/// <c>{hash}.htr</c>) the watch fetches from
/// <c>cdn.rec.net/data/{name}</c>; without those bytes mirrored locally,
/// the watch's holotar projectors render empty and audio sampler nodes
/// produce silence after the YarrHarrHeist-style import otherwise
/// succeeds.
///
/// The admin Import Room endpoint kicks this off as fire-and-forget
/// (<see cref="EnqueueAsync"/>) right after the synchronous import
/// finishes, so the upload response returns immediately and the
/// 25 MB-ish download stream happens in the background. Per-asset
/// progress + summary are logged.
///
/// Idempotent: assets already in <see cref="RoomDataBlobEntity"/> by
/// BlobName are skipped without re-fetching.
/// </summary>
public sealed class HtrAssetMirrorService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<HtrAssetMirrorService> logger)
{
    private const string CdnBase = "https://cdn.rec.net/data/";
    // Two known persistence-view sub-message types that have a
    // BlobName string referencing an external .htr (or extension-less
    // base64 id) on the official CDN.
    private static readonly HashSet<string> HtrCarrierMessageNames = new(StringComparer.Ordinal)
    {
        "HolotarPersistenceData",
        "AudioSamplerPersistenceData",
    };

    /// <summary>Fire-and-forget: schedule extraction + download in a
    /// background task. Returns immediately. Errors are logged, never
    /// thrown — a failure here doesn't break the original import.</summary>
    public void EnqueueAsync(IReadOnlyList<byte[]> roomBlobs, string contextLabel)
    {
        if (roomBlobs is null || roomBlobs.Count == 0) return;
        // Snapshot the byte arrays now — by the time the background
        // task runs, the calling controller's scope is disposed, but
        // the byte[]s are owned by this list and live as long as the
        // closure does.
        _ = Task.Run(async () =>
        {
            try
            {
                await MirrorAsync(roomBlobs, contextLabel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[htr-mirror] background mirror crashed for {Context}", contextLabel);
            }
        });
    }

    /// <summary>Synchronous (awaitable) version. Use <see cref="EnqueueAsync"/>
    /// for fire-and-forget from a controller; use this directly if you
    /// want to wait for completion (admin tooling / one-shot scripts).</summary>
    public async Task<MirrorResult> MirrorAsync(IReadOnlyList<byte[]> roomBlobs, string contextLabel)
    {
        // Step 1: parse each blob with the full 2020 schema and collect
        // every Holotar/AudioSampler BlobName ref. Skip blobs that fail
        // to parse — better to lose a few refs than to abort the mirror.
        var refs = new HashSet<string>(StringComparer.Ordinal);
        var parseFailures = 0;
        foreach (var bytes in roomBlobs)
        {
            try
            {
                var msg = PersistedRoomData.Parser.ParseFrom(bytes);
                CollectHtrRefs(msg, refs);
            }
            catch (InvalidProtocolBufferException)
            {
                parseFailures++;
            }
        }

        if (refs.Count == 0)
        {
            logger.LogInformation(
                "[htr-mirror] {Context}: no holotar/audio refs in {Count} blob(s); nothing to do",
                contextLabel, roomBlobs.Count);
            return new MirrorResult(0, 0, 0, parseFailures, Array.Empty<string>());
        }

        // Step 2: filter against what's already in the DB.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();

        var refList = refs.ToList();
        var existing = await db.RoomDataBlobs
            .Where(b => refList.Contains(b.BlobName))
            .Select(b => b.BlobName)
            .ToListAsync();
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);
        var todo = refList.Where(n => !existingSet.Contains(n)).ToList();

        logger.LogInformation(
            "[htr-mirror] {Context}: {Total} unique refs; {Skip} already mirrored; {Todo} to download",
            contextLabel, refList.Count, existingSet.Count, todo.Count);

        if (todo.Count == 0)
        {
            return new MirrorResult(refList.Count, existingSet.Count, 0, parseFailures, Array.Empty<string>());
        }

        // Step 3: download + insert.
        var http = httpClientFactory.CreateClient("htr-mirror");
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DorkNet-htr-mirror/1.0");

        var failures = new List<string>();
        var nowIso = DateTime.UtcNow;
        long totalBytes = 0;

        foreach (var name in todo)
        {
            try
            {
                using var resp = await http.GetAsync(CdnBase + Uri.EscapeDataString(name));
                if (!resp.IsSuccessStatusCode)
                {
                    failures.Add($"{name}: HTTP {(int)resp.StatusCode}");
                    logger.LogWarning("[htr-mirror] {Name} → HTTP {Code}", name, (int)resp.StatusCode);
                    continue;
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes.Length == 0)
                {
                    failures.Add($"{name}: empty body");
                    continue;
                }
                // Dual-write: S3 (canonical post-migration) + Postgres
                // BYTEA (read fallback for code that hasn't migrated).
                // S3 PUT happens first so a transient Redis/Postgres
                // hiccup doesn't leave us with a metadata row pointing
                // at a missing object. Failures here cause this asset
                // to be skipped; the import as a whole still succeeds.
                var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
                try
                {
                    var (bucket, key) = BlobRouter.Route(name);
                    await storage.PutAsync(
                        bucket,
                        key,
                        bytes,
                        "application/octet-stream");
                }
                catch (Exception putEx)
                {
                    failures.Add($"{name}: s3 put failed: {putEx.Message}");
                    logger.LogWarning(putEx, "[htr-mirror] {Name} S3 PUT failed", name);
                    continue;
                }
                db.RoomDataBlobs.Add(new RoomDataBlobEntity
                {
                    RoomId = 0,           // shared / unowned asset
                    BlobName = name,
                    UploadedByPlayerId = 1,
                    UploadedAt = nowIso,
                    ReferencedFilenamesCsv = string.Empty,
                });
                totalBytes += bytes.Length;
                logger.LogInformation("[htr-mirror] + {Name} ({Bytes:N0} bytes)", name, bytes.Length);
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
                logger.LogWarning(ex, "[htr-mirror] {Name} failed", name);
            }
        }

        // SaveChanges in one batch at the end keeps the DB write fast
        // for the 100+-asset case YarrHarrHeist-sized rooms produce.
        await db.SaveChangesAsync();

        var inserted = todo.Count - failures.Count;
        logger.LogInformation(
            "[htr-mirror] {Context} done: inserted {Ins}, failed {Fail}, total bytes {Bytes:N0}",
            contextLabel, inserted, failures.Count, totalBytes);

        return new MirrorResult(refList.Count, existingSet.Count, inserted, parseFailures, failures);
    }

    private static void CollectHtrRefs(IMessage msg, HashSet<string> sink)
    {
        var desc = msg.Descriptor;
        if (HtrCarrierMessageNames.Contains(desc.Name))
        {
            // Field 1 is BlobName on both carrier types.
            var f = desc.FindFieldByNumber(1);
            if (f is not null)
            {
                var v = f.Accessor.GetValue(msg) as string;
                if (!string.IsNullOrWhiteSpace(v)) sink.Add(v);
            }
            return;
        }

        foreach (var field in desc.Fields.InFieldNumberOrder())
        {
            if (field.FieldType != FieldType.Message) continue;
            if (field.IsRepeated)
            {
                if (field.Accessor.GetValue(msg) is System.Collections.IEnumerable list)
                {
                    foreach (var item in list)
                    {
                        if (item is IMessage child) CollectHtrRefs(child, sink);
                    }
                }
            }
            else
            {
                // proto3: HasValue is true when sub-message is non-null.
                var value = field.Accessor.GetValue(msg) as IMessage;
                if (value is not null) CollectHtrRefs(value, sink);
            }
        }
    }

    public sealed record MirrorResult(
        int TotalRefs,
        int AlreadyMirrored,
        int Inserted,
        int RoomBlobParseFailures,
        IReadOnlyList<string> AssetDownloadFailures);
}
