using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace DorkNet.Server.Services;

/// <summary>
/// Reads the client builds CI publishes (<c>ci/downloads/publish.py</c>): a
/// <c>manifest.json</c> plus the files it lists, in a bucket served over plain
/// HTTP (e.g. Garage's web endpoint on the internal network). The site's
/// Downloads and Quest pages fetch them through the server, so the bucket
/// itself never has to be reachable from outside.
///
/// Configured with <c>DorkNet:Downloads:BaseUrl</c> (the folder holding
/// <c>manifest.json</c>) and optionally <c>DorkNet:Downloads:Host</c> (the
/// Host header to send, for web endpoints that pick the bucket by host name).
/// Without a base URL <see cref="IsConfigured"/> is false and the site hides
/// both features.
/// </summary>
public sealed class DownloadsService
{
    public const string HttpClientName = "downloads";

    // How long the bucket may take to start answering. The body then streams
    // with no limit (the client has none; the caller's abort token ends it).
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri? _baseUri;
    private readonly string? _host;

    public DownloadsService(IConfiguration config, IHttpClientFactory httpClientFactory, ILogger<DownloadsService> log)
    {
        _httpClientFactory = httpClientFactory;
        var baseUrl = config["DorkNet:Downloads:BaseUrl"]?.Trim();
        if (!string.IsNullOrEmpty(baseUrl) && Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            _baseUri = uri;
        }
        var host = config["DorkNet:Downloads:Host"]?.Trim();
        _host = string.IsNullOrEmpty(host) ? null : host;

        if (_baseUri is null)
            log.LogInformation("[downloads] no DorkNet:Downloads:BaseUrl; the Downloads page and Quest latest-build install are off");
        else
            log.LogInformation("[downloads] reading builds from {BaseUrl}{Host}", _baseUri, _host is null ? "" : $" (Host: {_host})");
    }

    public bool IsConfigured => _baseUri is not null;

    /// <summary>The published manifest, or null when none has been published yet.</summary>
    public async Task<JsonObject?> GetManifestAsync(CancellationToken ct)
    {
        using var res = await SendAsync("manifest.json", range: null, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) as JsonObject;
    }

    /// <summary>A manifest entry by its bucket path, or null when the manifest
    /// doesn't list it. Only listed files are ever served.</summary>
    public static JsonObject? FindFile(JsonObject manifest, string path)
    {
        if (manifest["releases"] is not JsonArray releases) return null;
        return releases.OfType<JsonObject>()
            .SelectMany(r => r["files"] is JsonArray files ? files.OfType<JsonObject>() : [])
            .FirstOrDefault(f => string.Equals((string?)f["path"], path, StringComparison.Ordinal));
    }

    /// <summary>GET a file, forwarding the caller's Range header. The caller
    /// owns the response and streams its content.</summary>
    public Task<HttpResponseMessage> GetFileAsync(string path, string? range, CancellationToken ct)
        => SendAsync(path, range, ct);

    private async Task<HttpResponseMessage> SendAsync(string path, string? range, CancellationToken ct)
    {
        if (_baseUri is null) throw new InvalidOperationException("Downloads are not configured.");
        var url = new Uri(_baseUri, string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (_host is not null) req.Headers.Host = _host;
        if (!string.IsNullOrEmpty(range) && RangeHeaderValue.TryParse(range, out var r)) req.Headers.Range = r;
        var client = _httpClientFactory.CreateClient(HttpClientName);
        // The timeout covers only getting the headers: with ResponseHeadersRead
        // the token is released once they arrive, and disposing the source
        // here stops its timer before the body is streamed.
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headers.CancelAfter(HeaderTimeout);
        try
        {
            return await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headers.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"The downloads bucket didn't answer within {HeaderTimeout.TotalSeconds:0} s.");
        }
    }
}
