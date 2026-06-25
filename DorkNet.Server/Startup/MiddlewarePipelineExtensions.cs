using DorkNet.Server.Auth;
using DorkNet.Server.Compat;
using DorkNet.Server.Hubs;
using DorkNet.Server.Services;
using Microsoft.AspNetCore.Http.Connections;

namespace DorkNet.Server.Startup;

/// <summary>Assembles the full middleware pipeline in canonical order.
/// Ordering rules (the comments at each Use call explain the why):
/// <list type="number">
///   <item>Swagger (dev only)</item>
///   <item>HTTPS redirect (Kestrel-direct mode only)</item>
///   <item>WebSocket keepalive override</item>
///   <item>Static hosts (admin/site/feed) BEFORE request tracing because
///   StaticFileMiddleware bypasses Response.Body wrapping</item>
///   <item>Request tracing</item>
///   <item>api.{apex}/admin → admin.{apex} redirect</item>
///   <item>IP-ban (earliest filter, no DB beyond a small lookup)</item>
///   <item>Version detection (gates anonymous calls too)</item>
///   <item>Authentication → Authorization → player-ban</item>
///   <item>MapControllers + SignalR hub</item>
/// </list></summary>
public static class MiddlewarePipelineExtensions
{
    public static void UseDorkNetPipeline(this WebApplication app)
    {
        var domainCfg = app.Services.GetRequiredService<DomainConfig>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // HTTP → HTTPS redirect — only kicks in when an HTTPS endpoint is
        // configured in appsettings (Kestrel-direct mode). When fronted
        // by nginx, both endpoints are effectively HTTP from Kestrel's
        // perspective and this is a no-op.
        app.UseHttpsRedirection();

        // 2-minute WebSocket pings are too lazy for cloudflared — the
        // tunnel kills the underlying TCP stream after ~90-100s of silence
        // even though SignalR's app-layer pings would arrive on schedule.
        // Send a ping frame every 30s so the tunnel always sees fresh
        // traffic; SignalR's own KeepAliveInterval (15s) stays under this
        // floor.
        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        // Per-host static branches — admin SPA, public site, legacy feed.* —
        // all keyed off the configured apex so a single DORKNET_DOMAIN env
        // var moves every static host together.
        app.MountStaticHost(domainCfg.Sub("admin"),  "admin", spaFallback: true);
        // Public-facing site at the apex (e.g. localhost + www variant).
        // React-router routes are client-side so spaFallback must be true.
        app.MountStaticHost(domainCfg.Apex,          "site",  spaFallback: true);
        app.MountStaticHost(domainCfg.Sub("www"),    "site",  spaFallback: true);
        // Old feed.* subdomain kept as legacy; new visitors land on the apex.
        app.MountStaticHost(domainCfg.Sub("feed"),   "feed");

        // Request/response tracing — emits one structured log per non-health
        // request, with the response body included for any 4xx/5xx. Mounted
        // AFTER static hosts so SendFileAsync's underlying connection isn't
        // wrapped.
        app.UseRequestTracing();

        // Friendly redirect from the old api.{apex}/admin path → admin.{apex}.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Host.Host.Equals(domainCfg.Sub("api"), StringComparison.OrdinalIgnoreCase) &&
                (ctx.Request.Path == "/admin" || ctx.Request.Path.StartsWithSegments("/admin")))
            {
                ctx.Response.Redirect(domainCfg.Url("admin", "/"), permanent: false);
                return;
            }
            await next();
        });

        // IP-level bans run BEFORE authentication so banned-IP traffic gets
        // cut off at the earliest point possible — no JWT round-trip, no DB
        // hit beyond the (small) IpBans lookup.
        app.UseMiddleware<IpBanCheckMiddleware>();

        // Client-version gate. Reads X-DorkNet-Version, validates against
        // the per-deployment allow-list, and 426s mismatches before they
        // reach a controller. Sits before auth on purpose so anonymous
        // calls (login, photon custom auth) are gated too.
        app.UseMiddleware<VersionDetectionMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();
        // Sits between auth and the controllers — by this point ctx.User has
        // the validated principal (or is empty for anonymous calls). Bans are
        // enforced uniformly without each controller having to remember.
        app.UseMiddleware<BanCheckMiddleware>();
        app.MapControllers();

        // SignalR hub on the notify.* subdomain — RequireHost prevents the
        // same path from being matched on api.* or other subdomains.
        // Pin transport to WebSockets-only: letting SignalR fall back to
        // ServerSentEvents or LongPolling makes "is the proxy idle?" a
        // per-poll question instead of a stream-level one. WebSockets keep
        // one socket hot with our 10s pings.
        app.MapHub<NotifyHub>("/hub/v1", opts =>
        {
            opts.Transports = HttpTransportType.WebSockets;
            // SignalR's per-connection idle disconnect — must be > the
            // longest keepalive interval in the stack.
            opts.TransportSendTimeout = TimeSpan.FromSeconds(30);
        }).RequireHost(domainCfg.Sub("notify"));
    }
}
