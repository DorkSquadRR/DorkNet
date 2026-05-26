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
                        if (ShouldReadResponseBody(ctx.Response.ContentType, status))
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

                var trimmedRequest = requestBody is null ? null
                    : (requestBody.Length > 200 ? requestBody[..200] + "..." : requestBody);
                var trimmedResponse = responseBody.Length > 300 ? responseBody[..300] + "..." : responseBody;
                var level = thrown is not null || status >= 500 ? LogLevel.Error
                          : status >= 400 ? LogLevel.Warning
                          : LogLevel.Information;

                reqLogger.Log(level, thrown,
                    "[req] {Status} {Method} {Host}{Path}{Query} {ElapsedMs}ms contentType={ContentType} contentLength={ContentLength} req={ReqBody} resp={RespBody}",
                    status, ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
                    ctx.Request.QueryString.Value ?? "", sw.ElapsedMilliseconds,
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

    private static bool ShouldReadResponseBody(string? contentType, int status)
    {
        if (status < 400) return false;
        if (string.IsNullOrWhiteSpace(contentType)) return true;
        return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
    }
}
