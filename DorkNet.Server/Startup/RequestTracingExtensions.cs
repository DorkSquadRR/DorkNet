using System.Text.RegularExpressions;
using DorkNet.Server.Services;

namespace DorkNet.Server.Startup;

/// <summary>Per-request observability middleware. Emits one structured
/// log line per non-health request, capturing the response body for any
/// 4xx/5xx so we can see exactly what the game is asking for. Also
/// records each request against <see cref="PlayerLogService"/> for the
/// admin SPA's "Player logs" tab when an authenticated player is
/// attached.
///
/// <para>Skips:
/// <list type="bullet">
///   <item>Health probes (<c>/healthz</c>) — too noisy.</item>
///   <item>WebSocket upgrades — wrapping the response body breaks the
///   handshake and SignalR's WebSocket transport.</item>
///   <item>CDN host responses — they're binary blobs we don't want to
///   serialize into log lines.</item>
/// </list></para>
///
/// <para>Must be registered AFTER <see cref="StaticHostExtensions.MountStaticHost"/>
/// so StaticFileMiddleware's <c>SendFileAsync</c> path doesn't have its
/// underlying connection wrapped by a MemoryStream.</para></summary>
public static class RequestTracingExtensions
{
    public static void UseRequestTracing(this WebApplication app)
    {
        var reqLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.HttpTrace");
        var playerLog = app.Services.GetRequiredService<PlayerLogService>();

        app.Use(async (ctx, next) =>
        {
            if (IsHealthProbe(ctx.Request))
            {
                await next();
                return;
            }

            // WebSocket upgrades take over the underlying connection —
            // wrapping the response body in a MemoryStream confuses the
            // upgrade handshake and prevents SignalR's WebSocket transport
            // from working.
            if (ctx.WebSockets.IsWebSocketRequest)
            {
                reqLogger.LogInformation(
                    "[ws] {Method} {Host}{Path}{Query}",
                    ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
                    ctx.Request.QueryString.Value ?? "");
                await next();
                return;
            }

            var host = ctx.Request.Host.Host;
            var path = ctx.Request.Path.Value ?? "";
            var isStorageLike = host.StartsWith("storage.", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("cdn.", StringComparison.OrdinalIgnoreCase)
                || path.Contains("upload", StringComparison.OrdinalIgnoreCase);

            if (isStorageLike)
            {
                reqLogger.LogInformation(
                    "[req:start] {Method} {Host}{Path}{Query} contentType={ContentType} contentLength={ContentLength}",
                    ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
                    ctx.Request.QueryString.Value ?? "", ctx.Request.ContentType ?? "",
                    ctx.Request.ContentLength);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var originalBody = ctx.Response.Body;
            var captureResponse = ShouldCaptureResponse(ctx.Request);
            MemoryStream? capture = null;
            if (captureResponse)
            {
                capture = new MemoryStream();
                ctx.Response.Body = capture;
            }

            string? requestBody = null;
            if (ctx.Request.ContentLength is > 0 and < 4096 && IsTextLikeRequest(ctx.Request.ContentType))
            {
                ctx.Request.EnableBuffering();
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                requestBody = await reader.ReadToEndAsync();
                ctx.Request.Body.Position = 0;
            }

            Exception? thrown = null;
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                thrown = ex;
                throw;
            }
            finally
            {
                sw.Stop();
                var status = thrown is null ? ctx.Response.StatusCode : 500;
                var responseBody = "";

                if (capture is not null)
                {
                    try
                    {
                        capture.Seek(0, SeekOrigin.Begin);
                        if (ShouldReadResponseBody(ctx.Response.ContentType, status, path))
                            responseBody = await new StreamReader(capture, leaveOpen: true).ReadToEndAsync();
                        capture.Seek(0, SeekOrigin.Begin);
                        await capture.CopyToAsync(originalBody);
                    }
                    finally
                    {
                        ctx.Response.Body = originalBody;
                        await capture.DisposeAsync();
                    }
                }

                // Redact credentials/tokens BEFORE trimming, so a secret
                // can't survive either in the log line, the admin player-log
                // tab, or the truncated tail. (Without this the platformlogin
                // / admin-login bodies wrote the cleartext password to disk.)
                var safeRequest = RedactSecrets(requestBody);
                var safeResponse = RedactSecrets(responseBody) ?? "";
                var trimmedRequest = safeRequest is null ? null
                    : (safeRequest.Length > 200 ? safeRequest[..200] + "..." : safeRequest);
                // 300 chars is plenty to identify an error, but a room-details
                // payload is a few KB and the whole point of capturing it is to
                // see which key is missing — a truncated one answers nothing.
                var responseCap = IsSuccessBodyPath(path) ? 8000 : 300;
                var trimmedResponse = safeResponse.Length > responseCap
                    ? safeResponse[..responseCap] + "..."
                    : safeResponse;
                var authPresence = DescribeAuthPresence(ctx.Request);
                var level = thrown is not null || status >= 500 ? LogLevel.Error
                          : status >= 400 ? LogLevel.Warning
                          : LogLevel.Information;

                // TEMP diagnostic (presence "others online show offline"): the
                // normal [req] line truncates to 300 chars and never reads 2xx
                // bodies, so we can't see the /player roster the watch renders
                // from. Dump the FULL redacted body for /player on any status.
                // Remove once the presence-offline bug is localized.
                if (path.Equals("/player", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(safeResponse))
                {
                    reqLogger.LogInformation(
                        "[presence-debug] {Method} {Host}{Path}{Query} status={Status} body={Body}",
                        ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
                        ctx.Request.QueryString.Value ?? "", status, safeResponse);
                }

                reqLogger.Log(level, thrown,
                    "[req] {Status} {Method} {Host}{Path}{Query} {ElapsedMs}ms auth={AuthPresence} contentType={ContentType} contentLength={ContentLength} req={ReqBody} resp={RespBody}",
                    status, ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
                    ctx.Request.QueryString.Value ?? "", sw.ElapsedMilliseconds, authPresence,
                    ctx.Request.ContentType ?? "", ctx.Request.ContentLength,
                    trimmedRequest ?? "", trimmedResponse);

                var pid = Auth.ControllerBaseExtensions.CurrentPlayerId(ctx.User);
                if (pid is long playerId)
                {
                    playerLog.Record(new PlayerLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        PlayerId = playerId,
                        Method = ctx.Request.Method,
                        Host = ctx.Request.Host.Host,
                        Path = ctx.Request.Path.Value ?? string.Empty,
                        Query = ctx.Request.QueryString.Value ?? string.Empty,
                        Status = status,
                        ElapsedMs = sw.ElapsedMilliseconds,
                        ReqBody = trimmedRequest,
                        RespBody = trimmedResponse,
                    });
                }
            }
        });
    }

    // Property/field names whose values must never reach logs or the admin
    // player-log tab. Covers JSON ("password":"…") and form (password=…)
    // bodies, case-insensitively. The closing quote in the JSON pattern
    // anchors on the whole key, so "passwordHash" is matched but
    // "password_confirmed_at"-style siblings of a listed key are not partial-
    // matched. Add new keys here as new credential-bearing endpoints land.
    private const string SecretKeys =
        "password|passwd|pwd|passwordhash|token|accesstoken|refreshtoken|idtoken|" +
        "secret|clientsecret|apikey|api_key|authorization|deviceauth|sessionkey";

    private static string DescribeAuthPresence(HttpRequest request)
    {
        var hasAuthHeader = request.Headers.ContainsKey("Authorization");
        var hasBearer = request.Headers.Authorization.ToString()
            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        var hasAccessCookie = request.Cookies.ContainsKey(AuthService.AccessCookieName);
        return $"header={hasAuthHeader};bearer={hasBearer};cookie={hasAccessCookie}";
    }

    private static readonly Regex JsonSecretRegex = new(
        "(\"(?:" + SecretKeys + ")\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FormSecretRegex = new(
        "(?<=^|&)(?<k>" + SecretKeys + ")=[^&]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Mask credential/token values in a captured request or
    /// response body so they never land in the request-trace log or the
    /// player-log store. Returns the input unchanged when there's nothing
    /// to redact; null/empty pass through untouched.</summary>
    private static string? RedactSecrets(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        if (body.IndexOf('"') < 0 && body.IndexOf('=') < 0) return body;
        var redacted = JsonSecretRegex.Replace(body, "$1\"***\"");
        redacted = FormSecretRegex.Replace(redacted, "${k}=***");
        return redacted;
    }

    private static bool IsHealthProbe(HttpRequest request)
        => request.Path.Equals("/healthz", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextLikeRequest(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldCaptureResponse(HttpRequest request)
    {
        var host = request.Host.Host;
        if (host.StartsWith("cdn.", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = request.Path.Value ?? "";
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return true;

        return ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".log", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Paths whose SUCCESS bodies are logged as well as their failures.
    ///
    /// Worth stating why this list has to exist: when the client rejects a
    /// response it answers 200 and the client throws while reading it, so the
    /// server log shows a clean 200 and the client shows a bare "Failed to …"
    /// with no URL. Neither side records the payload, and without it every
    /// diagnosis is guesswork about which key the strict reader tripped on.
    ///
    /// Override with <c>Trace__SuccessBodyPaths</c> (comma-separated substrings,
    /// empty to disable). Bodies are still redacted and capped at 300 chars by
    /// the caller.</summary>
    private static readonly string[] SuccessBodyPaths = ResolveSuccessBodyPaths();

    private static string[] ResolveSuccessBodyPaths()
    {
        var configured = Environment.GetEnvironmentVariable("Trace__SuccessBodyPaths");
        if (configured is null)
            return ["/player", "/thread", "/clone"];

        return configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static bool IsSuccessBodyPath(string path) =>
        SuccessBodyPaths.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static bool ShouldReadResponseBody(string? contentType, int status, string path)
    {
        // Failures are always read; successes only for the paths above, because
        // a rejected-but-200 response is invisible otherwise.
        if (status < 400 && !IsSuccessBodyPath(path)) return false;
        if (string.IsNullOrWhiteSpace(contentType)) return true;
        return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }
}
