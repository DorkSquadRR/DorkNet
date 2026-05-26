using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Data;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Cdn;

/// <summary>
/// Catch-all binary blob server. Owns every <c>GET /{*path}</c> request
/// that hits the API host pool — cdn.* / storage.* / data.* / img.* /
/// the bare apex all funnel into this one controller and we branch on
/// the subdomain prefix inside <see cref="Serve"/>:
///
///   • <c>img.{apex}</c> → image-transform pipeline. Reads from S3
///     (BlobRouter routes by filename suffix), supports on-the-fly
///     <c>?cropSquare=1</c> / <c>?width=N</c> via
///     <see cref="ImageTransformService"/>, falls back to disk
///     (<c>data/images/</c>) and finally a 1×1 transparent PNG so the
///     2020 watch's image cache absorbs misses without 404 retry storms.
///   • everything else (cdn.* / storage.* / data.* / unknown sub) →
///     CDN serve path. S3 first, default RoomDataBlob fallback, image
///     misses still fall back to the transparent PNG (same shape the
///     old ImgController surfaced) so the watch's room-thumbnail
///     pipeline behaves identically post-merge.
///
/// Pre-refactor the two paths lived in separate controllers
/// (ImgController + CdnController) each with their own
/// <c>[Host(...)]</c> filter; HostFilteringMiddleware now controls
/// which hosts the app accepts at all (via DomainConfig) and routing
/// is purely path-based, so the two handlers had to merge to avoid
/// AmbiguousMatchException on <c>GET /{*path}</c>.
///
/// Every response is signed via <see cref="ImageSignatureService.AddContentSignature"/>
/// before being returned — the 2020 watch's image-verify path rejects
/// any image lacking the Content-Signature header.
/// </summary>
[ApiController]
public class CdnController(
    RoomDataBlobService roomDataBlob,
    IObjectStorage storage,
    ImageSignatureService signatures,
    DorkNetDbContext db,
    ILogger<CdnController> logger) : ControllerBase
{
    /// <summary>1x1 transparent PNG used for missing image files.
    /// The 2020 watch treats image HTTP failures harshly (retry-storms
    /// on missing room thumbnails), so image paths fail soft — return
    /// a valid PNG body with a short cache TTL instead of 404.</summary>
    private static readonly byte[] TransparentPng = new byte[]
    {
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,
        0x49,0x48,0x44,0x52,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
        0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,0x00,0x00,0x00,
        0x0D,0x49,0x44,0x41,0x54,0x78,0x9C,0x63,0x00,0x01,0x00,0x00,
        0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,0x00,0x00,0x49,
        0x45,0x4E,0x44,0xAE,0x42,0x60,0x82,
    };

    private static readonly string ImageDir =
        Path.Combine(AppContext.BaseDirectory, "data", "images");

    // Specific route prefixes only — NO global catch-all wildcard.
    // A previous version had [Route("/{*path:minlength(1)}")] which
    // matched every GET on every host, then used an in-handler host
    // gate to NotFound for non-cdn/img subdomains. That broke admin
    // API endpoints on admin.localhost: ASP.NET routing matched the
    // wildcard endpoint at UseRouting time, StaticFileMiddleware then
    // refused to serve admin SPA assets (its ValidateNoEndpoint guard
    // bails when an endpoint is already matched), and MapControllers
    // ran CdnController.Serve which 404'd — same dynamic broke every
    // admin/site API route and asset request.
    //
    // The route templates below cover the watch's actual CDN URL
    // shapes (confirmed via Coolify access logs):
    //   • cdn.{apex}/room/<blob>.dat — RoomDataBlob downloads
    //   • cdn.{apex}/data/<hash>.htr — room thumbnails / .htr files
    //   • cdn.{apex}/config/<name> — LoadingScreenTipData and similar
    //   • img.{apex}/<file>.{png,jpg,jpeg,webp,gif} — image fetches
    //   • img.{apex}/img/<file> — legacy path-prefixed images
    //
    // The in-handler host gate stays as defence-in-depth so that, if
    // a stray request somehow reaches one of these routes on the wrong
    // host (admin.*, api.*, etc.), we still 404 cleanly instead of
    // serving binary CDN bytes.
    [Route("/room/{*path:minlength(1)}")]
    [Route("/data/{*path:minlength(1)}")]
    [Route("/config/{*path:minlength(1)}")]
    [Route("/img/{*path:minlength(1)}")]
    // `cdn.{apex}/video/<BlobName>` — the watch's CommunityBoard
    // builds this URL via `String.Concat("/video/", blobName)` at
    // RecNet/CommunityBoard.txt:1068, so the prefix path needs an
    // explicit route. Without this, the catch-all regex below only
    // matches single-segment names and `/video/cb_video_*.mp4` 404s.
    [Route("/video/{*path:minlength(1)}")]
    // `cdn.{apex}/invention/<BlobName>` — the watch's invention
    // download fetcher (BBHENFCNLAB.GetInventionData at
    // BBHENFCNLAB_NestedType_NBDMFFEGNGJ.txt:207) builds the URL via
    // String.Concat("/invention/", filename). Same byte-fetch flow as
    // /room/, routed to S3 via BlobRouter on the filename.
    [Route("/invention/{*path:minlength(1)}")]
    // ASP.NET route templates treat `[` / `]` as token markers
    // (e.g. `[controller]`). To use them as literals inside a regex
    // constraint they must be doubled — so `[^/]` becomes `[[^/]]`.
    //
    // Extension list: image formats + room/.htr/.inv blobs that the
    // watch fetches directly, plus video formats (mp4/webm/mov/m4v) so
    // community-board video blobs uploaded via /admin/v1/communityboard/
    // video/upload (which writes `cb_video_<hash>.mp4` etc.) resolve
    // through the same bare-filename catch-all.
    [Route("/{path:regex(^[[^/]]+\\.(png|jpg|jpeg|webp|gif|dat|bin|holotar|inv|room|htr|mp4|webm|mov|m4v)$)}")]
    [AcceptVerbs("GET", "HEAD")]
    public async Task<IActionResult> Serve(string? path)
    {
        // Defence-in-depth host check. Without the prior global wildcard
        // these routes are narrow enough that stray hits on admin/api/etc.
        // should be rare, but if they do land here we 404 cleanly instead
        // of serving binary CDN bytes that would confuse the caller.
        var host = Request.Host.Host;
        var isImg = DomainConfig.MatchesSubdomain(host, "img");
        var isCdn = DomainConfig.MatchesSubdomain(host, "cdn")
                 || DomainConfig.MatchesSubdomain(host, "storage")
                 || DomainConfig.MatchesSubdomain(host, "data");
        if (!isImg && !isCdn) return NotFound();

        // Use the full request path (e.g. "config/LoadingScreenTipData",
        // "room/foo.dat") rather than the route-captured suffix so the
        // downstream switch logic — which still keys on the literal
        // "config/LoadingScreenTipData" — keeps working regardless of
        // which of the multiple [Route] templates above matched.
        var fullPath = (Request.Path.Value ?? string.Empty).TrimStart('/');

        if (isImg) return await ServeImage(fullPath);
        return await ServeCdn(fullPath);
    }

    // ── Image pipeline (img.{apex}) ────────────────────────────────────
    private async Task<IActionResult> ServeImage(string? path)
    {
        var safe = Path.GetFileName(path ?? string.Empty);
        // Strip the leading '$' sigil that ImageData.image_name carries
        // for hash-addressed assets. The watch URL-escapes it (%24) and
        // the route binder decodes it back, so what arrives here is
        // literally "$<hash>.jpg" — but the bytes are stored at
        // "<hash>.jpg" (the bare BlobName the importer writes). Without
        // this strip, every placed in-world polaroid 404s into the
        // transparent-PNG fallback and renders as "?" in-game.
        if (safe.StartsWith('$')) safe = safe[1..];
        if (string.IsNullOrWhiteSpace(safe))
        {
            signatures.AddContentSignature(Response, TransparentPng);
            return File(TransparentPng, "image/png");
        }

        var ext = Path.GetExtension(safe).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/png", // default to PNG for unknown — better than octet-stream for browser <img> tags
        };

        // Three-tier resolution: S3 first (canonical post-migration),
        // then on-disk legacy folder (room thumbnails shipped with the
        // server), then RoomDataBlobs.Bytes column (uploads that
        // pre-date the S3 migration). Once the migrator has uploaded
        // everything to S3 and PR 4 drops the Bytes column, the disk
        // and DB fallbacks become rare cases.
        byte[]? s3Bytes = null;
        var (s3Bucket, s3Key) = BlobRouter.Route(safe);
        try
        {
            using var s3Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            s3Bytes = await storage.GetAsync(s3Bucket, s3Key, s3Timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[img] S3 lookup threw host={Host} path='{Path}' file='{Safe}' bucket={Bucket} key={Key}; falling back to disk / DB / placeholder",
                Request.Host.Host, path, safe, s3Bucket, s3Key);
        }

        // Extension-less hash fallback. The 2020 watch references
        // some persistence-view image slots as a bare 32-char hex hash
        // (no .jpg, no .png) — distinct from ImageData.image_name which
        // carries "$<hash>.jpg". The importer stores everything from
        // SubRooms/<scene>/Image/ with an extension, so a bare-hash
        // request misses on first lookup. Retry with the two common
        // image extensions before giving up.
        if ((s3Bytes is null || s3Bytes.Length == 0) && string.IsNullOrEmpty(ext) && LooksLikeHash(safe))
        {
            foreach (var altExt in new[] { ".jpg", ".png" })
            {
                var altName = safe + altExt;
                var (altBucket, altKey) = BlobRouter.Route(altName);
                try
                {
                    using var altTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var altBytes = await storage.GetAsync(altBucket, altKey, altTimeout.Token);
                    if (altBytes is { Length: > 0 })
                    {
                        logger.LogInformation(
                            "[img] s3 hit (ext-fallback) host={Host} path={Path} resolvedAs={Alt} bytes={Bytes}",
                            Request.Host.Host, safe, altName, altBytes.Length);
                        s3Bytes = altBytes;
                        contentType = altExt == ".png" ? "image/png" : "image/jpeg";
                        break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "[img] ext-fallback S3 lookup threw for {Name} bucket={Bucket} key={Key}",
                        altName, altBucket, altKey);
                }
            }
        }

        if (s3Bytes is { Length: > 0 })
        {
            logger.LogInformation("[img] s3 hit host={Host} path={Path} bytes={Bytes}",
                Request.Host.Host, safe, s3Bytes.Length);
            Response.Headers.CacheControl = "public, max-age=300";
            return RespondWithTransforms(s3Bytes, contentType);
        }

        var full = Path.Combine(ImageDir, safe);
        if (System.IO.File.Exists(full))
        {
            logger.LogInformation("[img] disk hit host={Host} path={Path} → {Full}",
                Request.Host.Host, path, full);
            // Short TTL on the public CF edge so swapping a thumbnail
            // (e.g. switching from full-res to a resized cached PNG)
            // propagates within minutes instead of being pinned for
            // hours by CF's default image-cache TTL. The watch keeps
            // its own per-session local cache regardless.
            var diskBytes = await System.IO.File.ReadAllBytesAsync(full);
            Response.Headers.CacheControl = "public, max-age=300";
            return RespondWithTransforms(diskBytes, contentType);
        }

        // S3 is the only canonical store — DB carries text-only
        // metadata, never bytes. If S3 misses, the bytes don't exist
        // on this server; fall through to the transparent-PNG miss.

        // Stop the 404 storm — the watch retries hard on missing room
        // thumbnails. Return a 1x1 transparent PNG so it caches a
        // valid (empty) image and stops asking. Log loudly so the
        // user can see what filename the watch tried but disk lookup
        // failed at: tells us at a glance whether the client is even
        // hitting this controller (no log = wrong host / DNS issue),
        // and if so, what filename it expects vs what's on disk.
        logger.LogWarning(
            "[img] MISS host={Host} path='{Path}' file='{Safe}' lookedAt='{Full}' query='{Query}'",
            Request.Host.Host, path, safe, full, Request.QueryString);
        // Don't let CF pin the empty-fallback for hours — once the real
        // image lands on disk the next request should pick it up.
        Response.Headers.CacheControl = "public, max-age=60";
        signatures.AddContentSignature(Response, TransparentPng);
        return File(TransparentPng, "image/png");
    }

    // ── CDN serve path (cdn.*, storage.*, data.*, apex) ────────────────
    private async Task<IActionResult> ServeCdn(string? path)
    {
        if (string.Equals(path, "config/LoadingScreenTipData", StringComparison.OrdinalIgnoreCase))
        {
            // Tips live in the LoadingScreenTips DB table; admins
            // edit them via the SPA. Wire shape per
            // LoadingScreenTip.cs: List of {Title, Message,
            // ImageName, Context, PlatformMask, HasImage, RoomNames}.
            // HasImage MUST be set explicitly — without it the watch
            // waits forever on the image promise and never renders.
            var rows = await db.LoadingScreenTips
                .Where(t => t.IsActive)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                .AsNoTracking()
                .ToListAsync();
            var tips = rows.Select(t => new
            {
                Title = t.Title,
                Message = t.Message,
                ImageName = t.ImageName,
                Context = t.Context,
                PlatformMask = t.PlatformMask,
                HasImage = !string.IsNullOrEmpty(t.ImageName),
                RoomNames = string.IsNullOrEmpty(t.RoomNamesCsv)
                    ? Array.Empty<string>()
                    : t.RoomNamesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            }).ToArray();
            var payload = JsonSerializer.SerializeToUtf8Bytes(tips);
            signatures.AddContentSignature(Response, payload);
            return new FileContentResult(payload, "application/json");
        }

        var fileName = string.IsNullOrEmpty(path)
            ? string.Empty
            : path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        // Strip the leading '$' sigil that ImageData.image_name uses
        // for hash-addressed assets. The img subdomain branch above
        // already handles this; defense-in-depth for any client that
        // fetches the same name via cdn.* / storage.* / data.* hosts.
        if (!string.IsNullOrEmpty(fileName) && fileName[0] == '$') fileName = fileName[1..];

        if (string.IsNullOrEmpty(fileName))
        {
            logger.LogWarning("[cdn] empty filename host={Host} path={Path} — default blob",
                Request.Host.Host, path);
            var defaultBlob = roomDataBlob.GetDefaultBlob();
            signatures.AddContentSignature(Response, defaultBlob);
            return new FileContentResult(defaultBlob, "application/octet-stream");
        }

        // Two-tier read: S3 first (the canonical store post-migration),
        // then RoomDataBlobs.Bytes as a backstop for any blob that was
        // uploaded before the storage migration ran. Once the migrator
        // has uploaded all existing bytes and the Bytes column is
        // dropped, this fallback becomes a no-op.
        //
        // Defensive: ObjectStorageService.GetAsync only catches 404; an
        // S3 misconfig (wrong endpoint, dead creds, bucket missing) or
        // a network blip throws → we'd otherwise return 500 and the
        // watch's RecNet.Rooms.GetRoomData logs "Failed to download
        // room data" + Photon disconnects the player. Catching here
        // and falling through to the DB / default-blob path keeps the
        // dorm load resilient to S3 outages — players boot into the
        // stock dorm scene instead of getting kicked.
        byte[]? s3Bytes = null;
        var (s3Bucket, s3Key) = BlobRouter.Route(fileName);
        try
        {
            // 15 seconds for room/data blobs (vs 2 seconds on the image
            // path further up): the watch's room-load flow downloads
            // PersistedRoomData and DESERIALISES it as the player enters
            // the room — a default-empty blob means the saved dorm
            // appears completely reset. Better to wait for the real
            // bytes than hand back zeros and silently lose the player's
            // build. Garage on the same host typically responds in
            // 50-300ms; the larger budget only burns on cold-start /
            // network blips.
            using var s3Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            s3Bytes = await storage.GetAsync(s3Bucket, s3Key, s3Timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[cdn] S3 GetAsync threw for {Bucket}/{Key}; serving default blob",
                s3Bucket, s3Key);
        }
        if (s3Bytes is { Length: > 0 })
        {
            logger.LogInformation("[cdn] s3 hit host={Host} file={File} bytes={Bytes}",
                Request.Host.Host, fileName, s3Bytes.Length);
            // Allow CF edge to cache for an hour. RoomDataBlobs are
            // content-addressed by hash so the same BlobName never
            // serves different bytes — safe to cache aggressively.
            Response.Headers.CacheControl = "public, max-age=3600";
            signatures.AddContentSignature(Response, s3Bytes);
            return new FileContentResult(s3Bytes, MimeFromName(fileName));
        }

        // S3 is the only canonical store — DB carries text-only
        // metadata (BlobName, owner, timestamps) and never holds
        // bytes. If S3 misses, the bytes don't exist on this server.

        if (IsImageName(fileName))
        {
            logger.LogWarning("[cdn] image MISS host={Host} file={File} query='{Query}' -> transparent png",
                Request.Host.Host, fileName, Request.QueryString);
            Response.Headers.CacheControl = "public, max-age=60";
            signatures.AddContentSignature(Response, TransparentPng);
            return new FileContentResult(TransparentPng, "image/png");
        }

        logger.LogInformation("[cdn] MISS host={Host} file={File} -> default blob",
            Request.Host.Host, fileName);
        var fallbackBlob = roomDataBlob.GetDefaultBlob();
        signatures.AddContentSignature(Response, fallbackBlob);
        return new FileContentResult(fallbackBlob, "application/octet-stream");
    }

    /// <summary>
    /// Apply the watch's <c>?cropSquare=1</c> + <c>?width=N</c> transforms
    /// per-request on the way out and sign the *resulting* bytes — the
    /// originals stay untouched in S3/disk/DB so a later
    /// <c>?width=512</c> request can re-render from full resolution. If
    /// the transform pipeline can't decode the bytes (corrupt file, GIF
    /// that Skia doesn't support, etc.) we fall back to serving the raw
    /// bytes rather than 5xxing the whole image request.
    /// </summary>
    private IActionResult RespondWithTransforms(byte[] sourceBytes, string sourceContentType)
    {
        var cropSquare = ParseFlag(Request.Query["cropSquare"]);
        var width = ParseInt(Request.Query["width"]);
        var transformed = (cropSquare || width is not null)
            ? ImageTransformService.TryTransform(sourceBytes, sourceContentType, cropSquare, width)
            : null;

        if (transformed is { } t)
        {
            signatures.AddContentSignature(Response, t.Bytes);
            return File(t.Bytes, t.ContentType);
        }

        signatures.AddContentSignature(Response, sourceBytes);
        return File(sourceBytes, sourceContentType);
    }

    private static bool ParseFlag(Microsoft.Extensions.Primitives.StringValues v)
    {
        var s = v.ToString();
        return !string.IsNullOrEmpty(s) && s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseInt(Microsoft.Extensions.Primitives.StringValues v)
        => int.TryParse(v.ToString(), out var n) && n > 0 ? n : null;

    private static bool LooksLikeHash(string s)
    {
        if (s.Length is not (32 or 40)) return false;
        foreach (var c in s)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    private static string MimeFromName(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            ".webp" => "image/webp",
            ".mp4" or ".m4v"  => "video/mp4",
            ".webm" => "video/webm",
            ".mov"  => "video/quicktime",
            _ => "application/octet-stream",
        };
    }

    private static bool IsImageName(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp";
    }
}
