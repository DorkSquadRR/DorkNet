using DorkNet.Server.Auth;
using DorkNet.Server.Hubs;
using DorkNet.Server.Services;
using Microsoft.AspNetCore.Http.Connections;

namespace DorkNet.Server.Startup;

/// <summary>Assembles the DorkNet middleware pipeline in runtime order.</summary>
public static class MiddlewarePipelineExtensions
{
    public static void UseDorkNetPipeline(this WebApplication app)
    {
        var domainCfg = app.Services.GetRequiredService<DomainConfig>();

        // Easy Launcher single-origin mode. localtunnel gives us one public
        // hostname, so /__dn/{service}/... is mapped to the internal subdomain
        // host shape before endpoint selection runs.
        app.Use(async (ctx, next) =>
        {
            if (domainCfg.SingleOriginEnabled &&
                TryMapSingleOriginPath(ctx.Request.Path, out var service, out var rest))
            {
                ctx.Request.Host = new HostString(service == "www" ? domainCfg.Apex : domainCfg.Sub(service));
                ctx.Request.Path = string.IsNullOrEmpty(rest) ? "/" : rest;
            }

            await next();
        });

        app.UseRouting();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.MountStaticHost(domainCfg.Sub("admin"), "admin", spaFallback: true);
        app.MountStaticHost(domainCfg.Apex, "site", spaFallback: true);
        app.MountStaticHost($"www.{domainCfg.Apex}", "site", spaFallback: true);
        app.MountStaticHost(domainCfg.Sub("feed"), "feed");

        app.UseRequestTracing();

        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Host.Host.Equals(domainCfg.Sub("api"), StringComparison.OrdinalIgnoreCase) &&
                (ctx.Request.Path == "/admin" || ctx.Request.Path.StartsWithSegments("/admin")))
            {
                ctx.Response.Redirect($"https://{domainCfg.Sub("admin")}/", permanent: false);
                return;
            }
            await next();
        });

        app.UseMiddleware<IpBanCheckMiddleware>();

        app.UseMiddleware<IdentityServerGameTokenRequestMiddleware>();
        app.UseIdentityServer();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<BanCheckMiddleware>();
        app.MapControllers();

        app.MapHub<NotifyHub>("/hub/v1", opts =>
        {
            opts.Transports = HttpTransportType.WebSockets;
            opts.TransportSendTimeout = TimeSpan.FromSeconds(30);
        }).RequireHost(domainCfg.Sub("notify"));
    }

    private static bool TryMapSingleOriginPath(PathString path, out string service, out PathString rest)
    {
        service = "";
        rest = PathString.Empty;
        if (!path.StartsWithSegments("/__dn", out var tail)) return false;

        var value = tail.Value ?? "";
        if (value.Length <= 1) return false;
        var slash = value.IndexOf('/', 1);
        service = slash < 0 ? value[1..] : value[1..slash];
        if (string.IsNullOrWhiteSpace(service)) return false;

        rest = slash < 0 ? "/" : value[slash..];
        return true;
    }
}
