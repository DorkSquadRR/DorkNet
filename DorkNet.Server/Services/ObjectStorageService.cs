using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;

namespace DorkNet.Server.Services;

/// <summary>
/// Thin S3-compatible object-storage abstraction. Targets Garage in
/// production Coolify (one-click service); same SDK works against any
/// S3 endpoint for local dev (MinIO / LocalStack) or future migration
/// (Cloudflare R2, real AWS).
///
/// Two buckets, split by mutability rather than feature:
/// <list type="bullet">
/// <item><c>Buckets.Content</c> — hash-addressed, immutable, dedup'd.
///     HTR audio/holotar bytes, PV preview images, polaroids, camera
///     photos, profile avatars, room thumbnails. Same bytes are
///     referenced by many rooms / players, so they're keyed primarily
///     by hash and (for player-attributed content) sharded by
///     <c>{kind}/{playerId}/{filename}</c>.</item>
/// <item><c>Buckets.Saves</c> — owner-mutable user data. Dorm save
///     snapshots, custom-room save snapshots, invention saves, holotar
///     / video user uploads. Keyed by <c>{kind}/{ownerId or roomId}/
///     {filename}</c>. Old versions can be lifecycle'd off; daily
///     backup priority lives here.</item>
/// </list>
///
/// Routing happens via <see cref="BlobRouter"/> — every BlobName has
/// exactly one canonical (bucket, key). Removed the candidate-list
/// fallback the older 3-bucket layout needed because content was
/// scattered across feature-themed buckets.
///
/// **Fallback when no S3 is configured**: writes go to disk under
/// <c>data/object-fallback/{bucket}/{key}</c>. Keeps local single-instance
/// dev working without spinning up MinIO. Production Coolify always
/// has the env vars set so the disk path is a dev-only convenience.
///
/// Falls back silently to disk so the controllers don't have to
/// branch on configuration. <see cref="IsS3Configured"/> is exposed
/// for the migrator + admin tooling that needs to know the actual mode.
/// </summary>
public interface IObjectStorage
{
    bool IsS3Configured { get; }

    /// <summary>Upload bytes. Overwrites any existing object with the
    /// same key. Returns the byte count actually written (matches
    /// <paramref name="bytes"/>.Length in success cases; differs only
    /// if the underlying store reports otherwise).</summary>
    Task<long> PutAsync(string bucket, string key, byte[] bytes, string contentType, CancellationToken ct = default);

    /// <summary>Download bytes for a given key. Returns null when the
    /// object doesn't exist. Throws on transport errors so callers can
    /// distinguish "not there" from "broken".</summary>
    Task<byte[]?> GetAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>True when an object with this key exists. Cheaper than
    /// GetAsync when the caller only needs existence (e.g. CdnController
    /// MISS check before falling back to default blob).</summary>
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);
}

public static class Buckets
{
    public static string Content { get; set; } = "dorknet-content";
    public static string Saves   { get; set; } = "dorknet-saves";
}

/// <summary>
/// Single source of truth for "where does this filename live in S3?".
/// Read path (Cdn/Img controllers) and write path (Storage/Images
/// controllers, importers, mirrors) BOTH call <see cref="Route(string)"/>
/// with the same filename — guaranteed-consistent placement, no
/// candidate fallback chain, no read/write divergence.
///
/// Routing rules, in priority order:
///   1. Save-shaped filenames (<c>dorm_p&lt;player&gt;_v*.dat</c>,
///      <c>room_&lt;id&gt;_v*.dat</c>, <c>invention_p&lt;player&gt;_*.dat</c>)
///      → <see cref="Buckets.Saves"/>, sharded by owner/room so per-owner
///      lifecycle policies can prune old versions independently.
///   2. Everything else routes to <see cref="Buckets.Content"/> by file
///      extension: <c>.htr</c> → <c>htr/</c>, image extensions →
///      <c>image/</c>, <c>.mp4</c> → <c>video/</c>, anything else →
///      <c>blob/</c>. Ownership is already encoded in the filename
///      (<c>img_p&lt;player&gt;_*</c>, <c>holotar_p&lt;player&gt;_*</c>,
///      …) so we don't shard by player in Content — a query like "all
///      of player N's stuff" goes through the DB
///      (<c>RoomDataBlobs.UploadedByPlayerId</c>) which is the source
///      of truth anyway.
///
/// Net effect: any two paths that share the same BlobName produce the
/// same (bucket, key). Same holotar bytes uploaded by a player then
/// later referenced from a room save → one S3 object, not two.
/// </summary>
public static class BlobRouter
{
    public static (string Bucket, string Key) Route(string fileName)
    {
        // Saves first — these filename shapes are unambiguous.
        if (TryGetPlayerId(fileName, "dorm_p",      out var p)) return (Buckets.Saves, $"dorm/{p}/{fileName}");
        if (TryGetPlayerId(fileName, "invention_p", out p))     return (Buckets.Saves, $"invention/{p}/{fileName}");
        if (TryGetRoomId  (fileName,                out var r)) return (Buckets.Saves, $"room/{r}/{fileName}");

        // Content, by file extension. Owner (if any) stays in the filename.
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".htr") return (Buckets.Content, $"htr/{fileName}");
        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp")
                           return (Buckets.Content, $"image/{fileName}");
        // All video formats land under video/ — the CdnController's
        // `/video/{*path}` route maps every BlobName here verbatim, and
        // keeping the prefix consistent across extensions means an
        // admin grep'ing the bucket finds every uploaded clip in one
        // place regardless of source container format.
        if (ext is ".mp4" or ".m4v" or ".webm" or ".mov")
                           return (Buckets.Content, $"video/{fileName}");
        return (Buckets.Content, $"blob/{fileName}");
    }

    private static bool TryGetPlayerId(string fileName, string prefix, out long playerId)
    {
        playerId = 0;
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var start = prefix.Length;
        var end = fileName.IndexOf('_', start);
        return end > start && long.TryParse(fileName[start..end], out playerId);
    }

    private static bool TryGetRoomId(string fileName, out long roomId)
    {
        roomId = 0;
        const string prefix = "room_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        // Permissive: anything starting with "room_<digits>_" or
        // "room_<digits>." routes to that room's save folder. Covers
        // the canonical save snapshot (room_<id>_v<N>.dat) AND any
        // room-tagged sidecar (room_<id>_<hash>.meta, room_<id>_thumb.jpg,
        // future per-room attachments) — same parser, same folder,
        // no separate matcher per content type.
        var endUnderscore = fileName.IndexOf('_', prefix.Length);
        var endDot = fileName.IndexOf('.', prefix.Length);
        var end =
            endUnderscore > 0 && (endDot < 0 || endUnderscore < endDot) ? endUnderscore :
            endDot > 0 ? endDot : -1;
        return end > prefix.Length && long.TryParse(fileName[prefix.Length..end], out roomId);
    }
}

public class ObjectStorageService : IObjectStorage, IDisposable
{
    private readonly IAmazonS3? _s3;
    private readonly string _diskFallbackRoot;
    private readonly ILogger<ObjectStorageService> _log;

    public bool IsS3Configured => _s3 is not null;

    public ObjectStorageService(IConfiguration config, ILogger<ObjectStorageService> log)
    {
        _log = log;
        _diskFallbackRoot = Path.Combine(AppContext.BaseDirectory, "data", "object-fallback");
        // Bucket names are env-overridable so a new game generation can point at
        // fresh buckets (e.g. dorknet2023-*) without a code change. Applied here
        // (before the disk/S3 branch) so the disk fallback paths use them too.
        if (config["S3:ContentBucket"] is { Length: > 0 } contentBucket) Buckets.Content = contentBucket;
        if (config["S3:SavesBucket"]   is { Length: > 0 } savesBucket)   Buckets.Saves   = savesBucket;
        _log.LogInformation(
            "[storage] buckets content={Content} saves={Saves}", Buckets.Content, Buckets.Saves);

        // Provider switch: env-driven. The Garage one-click service in
        // Coolify exposes its endpoint on http://garage:3900 and supplies
        // access/secret keys via env. ForcePathStyle is required because
        // Garage (like MinIO) doesn't do virtual-host-style.
        var endpoint = config["S3:Endpoint"];
        var accessKey = config["S3:AccessKey"];
        var secretKey = config["S3:SecretKey"];
        if (string.IsNullOrWhiteSpace(endpoint)
         || string.IsNullOrWhiteSpace(accessKey)
         || string.IsNullOrWhiteSpace(secretKey))
        {
            _log.LogInformation(
                "[storage] no S3 endpoint/credentials configured — falling back to disk at {Root}",
                _diskFallbackRoot);
            return;
        }
        var s3Config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            MaxErrorRetry = config.GetValue<int?>("S3:MaxErrorRetry") ?? 1,
            // S3:TimeoutSeconds is the HARD ceiling enforced by the AWS
            // SDK at the HttpClient level — it kills the entire upload
            // request once exceeded, even if a CancellationToken passed
            // by the caller is more generous. The 8-second value we
            // shipped originally was too tight for the storage
            // migrator (some HTR blobs are 25-50 MB; on a typical
            // 10 Mbps home upload that's 20-40 s). 300 s gives plenty
            // of headroom; per-call CancellationTokens still impose
            // tighter bounds where the caller needs them (e.g. the
            // 8 s CTS in StorageController.StoreBlobObjectAsync keeps
            // in-game upload paths fast-failing).
            Timeout = TimeSpan.FromSeconds(config.GetValue<int?>("S3:TimeoutSeconds") ?? 300),
            // Garage requires *some* AuthenticationRegion string, value
            // irrelevant. The S3 SDK signs requests against this region;
            // using a non-empty placeholder avoids a SignatureDoesNotMatch
            // error. R2 / real AWS will set this via env.
            AuthenticationRegion = config["S3:Region"] ?? "garage",
            // AWSSDK 3.7.412+ Flexible Checksums sends x-amz-checksum-*
            // headers Garage rejects, breaking payload signatures. Limit
            // checksum computation to operations that genuinely require one.
            RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (config.GetValue<bool>("S3:DisableTlsCertificateValidation"))
        {
            _log.LogWarning(
                "[storage] S3 TLS certificate validation is disabled. Use only for private/self-hosted endpoints.");
            s3Config.HttpClientFactory = new UnsafeCertificateHttpClientFactory();
        }

        _s3 = new AmazonS3Client(accessKey, secretKey, s3Config);
        _log.LogInformation("[storage] S3 client configured for endpoint={Endpoint}", endpoint);
    }

    public async Task<long> PutAsync(string bucket, string key, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        if (_s3 is { } s3)
        {
            async Task<PutObjectResponse> PutObjectAsync()
            {
                using var stream = new MemoryStream(bytes);
                return await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    InputStream = stream,
                    ContentType = contentType,
                    UseChunkEncoding = false,
                }, ct);
            }

            PutObjectResponse resp;
            try
            {
                resp = await PutObjectAsync();
            }
            catch (AmazonS3Exception ex) when (IsNoSuchBucket(ex))
            {
                _log.LogWarning(
                    ex,
                    "[storage] bucket {Bucket} was missing while writing {Key}; creating it and retrying",
                    bucket,
                    key);
                await CreateBucketAsync(s3, bucket, ct);
                resp = await PutObjectAsync();
            }

            if (resp.HttpStatusCode is not (System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Created))
                throw new InvalidOperationException($"S3 PUT {bucket}/{key} returned {resp.HttpStatusCode}");
            return bytes.Length;
        }

        // Disk fallback — used in dev when no S3 is configured.
        var path = DiskPath(bucket, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
        return bytes.Length;
    }

    public async Task<byte[]?> GetAsync(string bucket, string key, CancellationToken ct = default)
    {
        if (_s3 is { } s3)
        {
            try
            {
                using var resp = await s3.GetObjectAsync(bucket, key, ct);
                using var ms = new MemoryStream();
                await resp.ResponseStream.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        var path = DiskPath(bucket, key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        if (_s3 is { } s3)
        {
            try
            {
                await s3.GetObjectMetadataAsync(bucket, key, ct);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (AmazonS3Exception ex)
            {
                // Anything else — 403 (Garage's admin key lacks the
                // HeadObject permission on this bucket), 5xx, transport
                // wobble — is NOT a definitive "object missing" signal.
                // Treat as "unknown" and return true so the upload path
                // doesn't return 500 just because verification couldn't
                // run. The caller logs the warning so the asymmetric
                // ACL still shows up in telemetry.
                _log.LogWarning(ex,
                    "[storage] HEAD {Bucket}/{Key} returned {Status}; treating as 'present (unverifiable)'",
                    bucket, key, ex.StatusCode);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "[storage] HEAD {Bucket}/{Key} threw {Type}; treating as 'present (unverifiable)'",
                    bucket, key, ex.GetType().Name);
                return true;
            }
        }
        return File.Exists(DiskPath(bucket, key));
    }

    private string DiskPath(string bucket, string key) =>
        Path.Combine(_diskFallbackRoot, bucket, key.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsNoSuchBucket(AmazonS3Exception ex) =>
        ex.StatusCode == System.Net.HttpStatusCode.NotFound
        || string.Equals(ex.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase);

    private async Task CreateBucketAsync(IAmazonS3 s3, string bucket, CancellationToken ct)
    {
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest
            {
                BucketName = bucket,
            }, ct);
            _log.LogInformation("[storage] created S3 bucket {Bucket}", bucket);
        }
        catch (AmazonS3Exception ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.Conflict
            || string.Equals(ex.ErrorCode, "BucketAlreadyExists", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "BucketAlreadyOwnedByYou", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInformation("[storage] S3 bucket {Bucket} already exists", bucket);
        }
    }

    private sealed class UnsafeCertificateHttpClientFactory : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
            return new HttpClient(handler);
        }
    }

    public void Dispose() => _s3?.Dispose();
}
