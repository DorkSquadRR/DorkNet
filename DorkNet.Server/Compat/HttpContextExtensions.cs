using Microsoft.AspNetCore.Http;

namespace DorkNet.Server.Compat;

/// <summary>Helpers to read the active <see cref="ClientVersion"/>
/// off the current request. The version is set by
/// <c>VersionDetectionMiddleware</c> early in the pipeline so any
/// downstream consumer (controllers, services, filters) can resolve
/// it without re-parsing the header.</summary>
public static class HttpContextExtensions
{
    private const string ClientVersionKey = "DorkNet.ClientVersion";

    /// <summary>Stamp the resolved version on the request. Called by
    /// <c>VersionDetectionMiddleware</c>; not for general use.</summary>
    internal static void SetClientVersion(this HttpContext ctx, ClientVersion v)
        => ctx.Items[ClientVersionKey] = v;

    /// <summary>The client version for this request. Returns
    /// <see cref="ClientVersion.Unknown"/> if the middleware hasn't
    /// run (e.g. a unit test that calls a controller method directly)
    /// rather than null, so callers can safely branch on
    /// <c>Generation</c> without null-checks.</summary>
    public static ClientVersion ClientVersion(this HttpContext ctx)
        => ctx.Items.TryGetValue(ClientVersionKey, out var v)
           && v is ClientVersion cv
            ? cv
            : Compat.ClientVersion.Unknown;
}
