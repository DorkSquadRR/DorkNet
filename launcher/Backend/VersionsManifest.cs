using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Fetches the canonical branch-to-build mapping from
/// <c>versions.json</c> on the public DorkNet repo's <c>main</c>
/// branch. Falls back to the locally-cached copy when offline, so the
/// launcher opens even with no internet (host mode just can't fetch
/// new server builds).
///
/// <para>Source URL is hardcoded — it's the entire point of the
/// launcher knowing where to look. To run against a fork's manifest,
/// edit <see cref="ManifestUrl"/>.</para></summary>
public sealed class VersionsManifest
{
    public const string ManifestUrl =
        "https://raw.githubusercontent.com/DorkSquadRR/DorkNet/main/versions.json";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    [JsonPropertyName("$schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("branches")] public List<VersionEntry> Branches { get; set; } = new();

    public async Task<VersionsManifest?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var m = await Http.GetFromJsonAsync<VersionsManifest>(ManifestUrl, ct);
            if (m is not null)
            {
                File.WriteAllText(AppPaths.ManifestCacheFile,
                    JsonSerializer.Serialize(m));
            }
            return m;
        }
        catch
        {
            // Offline / 404 / parse failure — fall back to last-seen
            // cache. Returns null only when there's no cache either
            // (first launch + no internet — the launcher reports the
            // failure to the user in that case).
            return LoadCached();
        }
    }

    public static VersionsManifest? LoadCached()
    {
        if (!File.Exists(AppPaths.ManifestCacheFile)) return null;
        try
        {
            var json = File.ReadAllText(AppPaths.ManifestCacheFile);
            return JsonSerializer.Deserialize<VersionsManifest>(json);
        }
        catch { return null; }
    }
}

public sealed class VersionEntry
{
    [JsonPropertyName("branch")] public string Branch { get; set; } = "";
    [JsonPropertyName("client_build")] public string ClientBuild { get; set; } = "";
    [JsonPropertyName("alt_builds")] public List<string> AltBuilds { get; set; } = new();
    [JsonPropertyName("version_key")] public string VersionKey { get; set; } = "";
    [JsonPropertyName("release_tag_prefix")] public string ReleaseTagPrefix { get; set; } = "";
    [JsonPropertyName("supported")] public bool Supported { get; set; }
}
