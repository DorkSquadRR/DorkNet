namespace DorkNet.Server.Services;

/// <summary>Single source of truth for the deployment apex domain.
/// Read from <c>DORKNET_DOMAIN</c> env var (or config key
/// <c>Domain:Apex</c>) at startup. Defaults to <c>localhost</c>. Replaces
/// per-controller <c>[Host("api.rec.net","api.localhost")]</c> filters —
/// allowed hosts are derived as <c>{apex}</c> + <c>*.{apex}</c>, fed
/// into HostFilteringMiddleware. Subdomain-discriminating handlers
/// (Cdn vs Img on path "/{*path}") branch on
/// <c>Request.Host.Host.Split('.')[0]</c>.</summary>
public sealed class DomainConfig
{
    public string Apex { get; }
    public string Scheme { get; }
    public string? Port { get; }

    public DomainConfig(string apex, string scheme = "https", string? port = null)
    {
        Apex = apex;
        Scheme = string.IsNullOrWhiteSpace(scheme) ? "https" : scheme.TrimEnd(':', '/', '\\');
        Port = string.IsNullOrWhiteSpace(port) ? null : port.Trim().TrimStart(':');
    }

    public string Sub(string prefix) => $"{prefix}.{Apex}";
    public string Url(string host) => Port is null ? $"{Scheme}://{host}" : $"{Scheme}://{host}:{Port}";
    public string SubUrl(string prefix) => Url(Sub(prefix));
    public static bool MatchesSubdomain(string host, string prefix)
        => host.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
}
