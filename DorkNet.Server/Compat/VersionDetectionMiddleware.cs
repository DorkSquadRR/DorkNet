using System.Text.Json;
using Microsoft.AspNetCore.Http;
using DorkNet.Server.Services;

namespace DorkNet.Server.Compat;

/// <summary>Extracts the client version from <c>X-DorkNet-Version</c>
/// on every request, validates it against this deployment's allow-list,
/// and either:
/// <list type="bullet">
///   <item>Stamps <see cref="HttpContextExtensions.ClientVersion"/> with
///   the resolved <see cref="ClientVersion"/> and forwards the request, OR</item>
///   <item>Returns <c>HTTP 426 Upgrade Required</c> with a JSON body
///   describing the mismatch when the client's version isn't in
///   <see cref="VersionRegistry.SupportedVersionKeys"/>.</item>
/// </list>
///
/// <para>Headerless requests fall back to
/// <see cref="VersionRegistry.DefaultVersionKey"/> when set. That's
/// the transition pathway — once every shipped client mod stamps the
/// header, set <c>DorkNet:DefaultClientVersion</c> to empty and
/// headerless requests get rejected too.</para>
///
/// <para>Some routes intentionally don't carry a client identity
/// (the public-facing site at the apex, the admin SPA at
/// <c>admin.{apex}</c>, health checks). Those are excluded by host
/// + path prefix below — they always skip the version gate and
/// receive <see cref="ClientVersion.Unknown"/>. The apex is whatever
/// <see cref="DomainConfig.Apex"/> resolved to at startup, so forks
/// running on a different domain get the same exemption automatically.</para>
/// </summary>
public sealed class VersionDetectionMiddleware(
    RequestDelegate next,
    VersionRegistry registry,
    DomainConfig domain,
    ILogger<VersionDetectionMiddleware> logger)
{
    private const string HeaderName = "X-DorkNet-Version";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (IsExempt(ctx))
        {
            ctx.SetClientVersion(ClientVersion.Unknown);
            await next(ctx);
            return;
        }

        var headerVal = ctx.Request.Headers[HeaderName].ToString();
        var versionKey = string.IsNullOrWhiteSpace(headerVal)
            ? registry.DefaultVersionKey
            : headerVal.Trim();

        // No header AND no default configured — reject. Once every
        // client mod sends the header this is the path that catches
        // probes / misconfigured clients.
        if (string.IsNullOrWhiteSpace(versionKey))
        {
            await WriteMismatch(ctx, "client_version_missing", got: null);
            return;
        }

        var resolved = registry.Resolve(versionKey);
        if (resolved == ClientVersion.Unknown)
        {
            // The client claims a version but no plugin knows it. That
            // means either a typo in the header or a build of Rec Room
            // we haven't onboarded yet. Either way, reject — server
            // can't reliably serve a version it doesn't have a
            // strategy bundle for.
            await WriteMismatch(ctx, "client_version_unknown", got: versionKey);
            return;
        }

        if (!registry.IsSupported(versionKey))
        {
            // Known version, but this specific deployment is
            // configured to accept a different set. e.g. a March
            // client hitting a December-only server.
            await WriteMismatch(ctx, "client_version_not_accepted", got: versionKey);
            return;
        }

        ctx.SetClientVersion(resolved);
        await next(ctx);
    }

    private bool IsExempt(HttpContext ctx)
    {
        // Hosts that don't serve the watch — these are humans on a
        // browser or admin tooling, not the game client.
        var host = ctx.Request.Host.Host.ToLowerInvariant();
        var apex = domain.Apex.ToLowerInvariant();
        if (host == apex || host == "www." + apex) return true;
        if (host.StartsWith("admin.")) return true;
        if (host.StartsWith("site.")) return true;
        if (host.StartsWith("feed.")) return true;

        // Server health / readiness probes.
        var path = ctx.Request.Path.Value;
        if (path is null) return false;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) return true;

        // The boot-time version gate itself — the whole POINT of this
        // endpoint is to receive a mismatched-version client and reply
        // with the watch's native "Update Required" payload. Gating it
        // here would 426 the request before VersionCheckController
        // could respond.
        if (path.StartsWith("/api/versioncheck/", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private async Task WriteMismatch(HttpContext ctx, string error, string? got)
    {
        logger.LogWarning(
            "[version-gate] rejecting request path={Path} host={Host} reason={Reason} got={Got} supported=[{Supported}]",
            ctx.Request.Path, ctx.Request.Host.Host, error, got ?? "(none)",
            string.Join(",", registry.SupportedVersionKeys));

        ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            error,
            got,
            supported = registry.SupportedVersionKeys.ToArray(),
            message = error switch
            {
                "client_version_missing" =>
                    "Client did not send an X-DorkNet-Version header. Update your DorkNet client mod.",
                "client_version_unknown" =>
                    $"Client claims version '{got}', which no version plugin in this server recognises.",
                "client_version_not_accepted" =>
                    $"Client version '{got}' is known but this deployment is configured to accept only: " +
                    string.Join(", ", registry.SupportedVersionKeys) + ".",
                _ => error,
            },
        };
        await JsonSerializer.SerializeAsync(ctx.Response.Body, body);
    }
}
