namespace DorkNet.Server.Compat;

/// <summary>Identifies a specific Rec Room client build.
/// <para>
/// <see cref="Key"/> is the wire-format identifier the client mod
/// stamps on every request via the <c>X-DorkNet-Version</c> header.
/// Format is <c>{branch}_{semver}</c>: <c>march_2020_03_10</c>,
/// <c>december_2020_12_18</c>. Keep it lowercase, underscore-separated;
/// it shows up in admin logs and error responses.
/// </para>
/// <para>
/// <see cref="Generation"/> is the wire-format-compatibility group:
/// every version inside the same generation shares the same wire
/// shapes (JSON DTO casing, route names, etc.) and therefore the same
/// strategy implementations. Adding a new build that's protocol-compatible
/// with an existing generation = a one-line registration; only a new
/// generation entails new strategy code.
/// </para>
/// </summary>
public sealed record ClientVersion(string Key, string Generation)
{
    /// <summary>Sentinel returned when a request has no
    /// <c>X-DorkNet-Version</c> header AND no
    /// <c>DorkNet:DefaultClientVersion</c> is configured. Controllers
    /// can branch on this to either fall through to a sensible default
    /// or 426 the request.</summary>
    public static readonly ClientVersion Unknown = new("unknown", "unknown");
}
