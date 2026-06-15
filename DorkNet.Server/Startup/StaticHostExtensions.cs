using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace DorkNet.Server.Startup;

/// <summary>Per-host static-files branching for admin, public site, and feed hosts.</summary>
public static class StaticHostExtensions
{
    public static void MountStaticHost(this WebApplication app, string host, string subdir, bool spaFallback = false)
    {
        var staticHostLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.StaticHost");
        var root = Path.Combine(app.Environment.ContentRootPath, "wwwroot", subdir);
        staticHostLogger.LogInformation(
            "[static-host] {Host} -> {Root} (exists: {Exists}, spaFallback: {Spa})",
            host, root, Directory.Exists(root), spaFallback);
        if (!Directory.Exists(root)) return;
        var files = new PhysicalFileProvider(root);

        var defaultFilesOpts = new DefaultFilesOptions { FileProvider = files };
        var staticFilesOpts = new StaticFileOptions
        {
            FileProvider = files,
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
