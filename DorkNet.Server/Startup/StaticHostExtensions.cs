using Microsoft.Extensions.FileProviders;

namespace DorkNet.Server.Startup;

/// <summary>Per-host static-files branching. Each subdomain
/// (<c>admin.{apex}</c>, the apex, <c>www.{apex}</c>, <c>feed.{apex}</c>)
/// gets its own <see cref="PhysicalFileProvider"/> rooted at
/// <c>wwwroot/&lt;subdir&gt;</c>, so requests like
/// <c>admin.{apex}/style.css</c> map to <c>wwwroot/admin/style.css</c>
/// with no path-prefix munging.
///
/// <para>Uses <see cref="UseWhenExtensions.UseWhen"/> (not MapWhen) so
/// requests for paths the SPA doesn't serve fall through to controllers.
/// MapWhen would fork into a parallel pipeline that terminates without
/// invoking matched endpoints.</para>
///
/// <para>Mounted BEFORE the request-tracing wrapper because
/// StaticFileMiddleware uses <c>IHttpResponseBodyFeature.SendFileAsync</c>,
/// which writes directly to the underlying connection and ignores any
/// <c>Response.Body</c> replacement done downstream.</para></summary>
public static class StaticHostExtensions
{
    public static void MountStaticHost(this WebApplication app, string host, string subdir, bool spaFallback = false)
    {
        var staticHostLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.StaticHost");
        var root = Path.Combine(app.Environment.ContentRootPath, "wwwroot", subdir);
        staticHostLogger.LogInformation(
            "[static-host] {Host} → {Root} (exists: {Exists}, spaFallback: {Spa})",
            host, root, Directory.Exists(root), spaFallback);
        if (!Directory.Exists(root)) return;
        var files = new PhysicalFileProvider(root);

        // Use UseDefaultFiles + UseStaticFiles directly rather than
        // UseFileServer because the latter's nested options initialization
        // doesn't always propagate the FileProvider correctly when wrapped
        // inside UseWhen — leading to the static files middleware looking
        // at the default WebRoot (wwwroot/) instead of our scoped folder.
        var defaultFilesOpts = new DefaultFilesOptions { FileProvider = files };
        var staticFilesOpts = new StaticFileOptions
        {
            FileProvider = files,
            // Don't let the browser cache admin assets — we iterate on
            // the HTML/JS/CSS often and a stale cached page silently
            // swallows every change.
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                headers.CacheControl = "no-cache, no-store, must-revalidate";
                headers.Pragma = "no-cache";
                headers.Expires = "0";
            },
        };

        app.UseWhen(ctx => ctx.Request.Host.Host.Equals(host, StringComparison.OrdinalIgnoreCase),
            branch =>
            {
                // Endpoint-clearing rule: the auto-added UseRouting (which
                // runs before our middleware in minimal hosting) may have
                // matched a non-admin endpoint for paths the static-host
                // should answer — typically NsController.Root for "/" on
                // admin.{apex} and the apex, which would serve the
                // service-URL-map JSON instead of the SPA's index.html.
                // StaticFileMiddleware's ValidateNoEndpoint guard skips
                // serving whenever an endpoint is already matched, so we
                // clear it for non-API paths to let UseStaticFiles run.
                //
                // BUT: /api/* paths MUST keep their matched endpoint —
                // those are admin/site/feed API calls that the matching
                // controller needs to handle.
                branch.Use(async (ctx, next) =>
                {
                    var p = ctx.Request.Path.Value ?? "/";
                    var probe = files.GetFileInfo(p == "/" ? "/index.html" : p);
                    staticHostLogger.LogInformation(
                        "[static-host:{Host}] path={Path} probe={Probe} exists={Exists}",
                        host, p, probe.PhysicalPath ?? "<null>", probe.Exists);
                    if (!p.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                        ctx.SetEndpoint(null);
                    await next();
                });
                branch.UseDefaultFiles(defaultFilesOpts);
                branch.UseStaticFiles(staticFilesOpts);

                // Terminal handler — runs AFTER UseStaticFiles. By this
                // point the request either:
                //   (a) was a static file UseStaticFiles served — Response
                //       already started, we just return.
                //   (b) is an API call we want to fall through to
                //       controllers — call next().
                //   (c) is a SPA deep link (no extension) we want to answer
                //       with index.html so React Router can re-resolve.
                //   (d) is a missing static asset (has extension, file
                //       gone — e.g. /assets/index-<stalehash>.js after a
                //       redeploy). Return 404 with no body — DO NOT fall
                //       through to controllers. Serving JSON {} causes the
                //       browser <script> loader to reject with "disallowed
                //       MIME type"; 404 makes the browser drop its stale
                //       index.html cache and refetch on the next load.
                branch.Use(async (ctx, next) =>
                {
                    if (ctx.Response.HasStarted) return;

                    var path = ctx.Request.Path.Value ?? "/";

                    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                    {
                        await next();
                        return;
                    }

                    if (ctx.Request.Method != "GET" && ctx.Request.Method != "HEAD")
                    {
                        await next();
                        return;
                    }

                    if (Path.HasExtension(path))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    if (!spaFallback)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    var index = files.GetFileInfo("/index.html");
                    if (!index.Exists)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    var hdrs = ctx.Response.Headers;
                    hdrs.CacheControl = "no-cache, no-store, must-revalidate";
                    hdrs.Pragma = "no-cache";
                    hdrs.Expires = "0";
                    await using var stream = index.CreateReadStream();
                    await stream.CopyToAsync(ctx.Response.Body);
                });
            });
    }
}
