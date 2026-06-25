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
    public string HostStyle { get; }
    public bool UsesHyphenSubdomains => HostStyle == "hyphen";

    public DomainConfig(string apex, string scheme = "https", string hostStyle = "dot")
    {
        Apex = apex;
        Scheme = NormalizeScheme(scheme);
        HostStyle = NormalizeHostStyle(hostStyle);
    }

    public string Sub(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return Apex;
        return UsesHyphenSubdomains ? $"{prefix}-{Apex}" : $"{prefix}.{Apex}";
    }

    public string Url(string prefix, string path = "")
    {
        var host = string.IsNullOrEmpty(prefix) ? Apex : Sub(prefix);
        if (string.IsNullOrEmpty(path)) return $"{Scheme}://{host}";
        return $"{Scheme}://{host}{(path.StartsWith('/') ? path : "/" + path)}";
    }

    public static bool MatchesSubdomain(string host, string prefix)
        => host.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
           || host.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeScheme(string? value)
    {
        var scheme = string.IsNullOrWhiteSpace(value) ? "https" : value.Trim().TrimEnd(':', '/', '\\').ToLowerInvariant();
        return scheme is "http" or "https" ? scheme : "https";
    }

    private static string NormalizeHostStyle(string? value)
    {
        var style = string.IsNullOrWhiteSpace(value) ? "dot" : value.Trim().ToLowerInvariant();
        return style is "hyphen" or "dash" or "flat" ? "hyphen" : "dot";
    }
}
