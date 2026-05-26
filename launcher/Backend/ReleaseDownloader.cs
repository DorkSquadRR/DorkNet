using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Fetches the latest server + client-patcher release
/// artifacts for a given version branch from GitHub Releases. Caches
/// under <see cref="AppPaths.ServersRoot"/> /
/// <see cref="AppPaths.PatchersRoot"/> so re-launches don't re-download
/// the same artifact.
///
/// <para>Per-branch release naming convention (see RELEASES.md):
/// <list type="bullet">
///   <item><c>dorknet-server-{version_key}-win-x64.zip</c></item>
///   <item><c>dorknet-clientpatch-{version_key}.zip</c></item>
/// </list>
/// The branch's <c>release_tag_prefix</c> is the prefix for tag names
/// (e.g. <c>v1-december-2026.05.25</c>) so the launcher finds tags
/// regardless of the suffix scheme used.</para></summary>
public sealed class ReleaseDownloader
{
    private const string ReleasesApi =
        "https://api.github.com/repos/DorkSquadRR/DorkNet/releases";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub requires a User-Agent on API calls.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DorkNet-Launcher/0.1");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Returns the path to the unpacked server-binary
    /// directory for <paramref name="version"/>, downloading + caching
    /// it if not already present.</summary>
    public async Task<string> EnsureServerAsync(
        VersionEntry version,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(AppPaths.ServersRoot, version.VersionKey);
        var marker = Path.Combine(dir, ".dorknet-version");
        // If the marker file exists, the server is already unpacked
        // for this tag and re-downloading would be wasted bandwidth.
        if (File.Exists(marker)) return dir;

        var asset = await FindAssetAsync(
            tagPrefix: version.ReleaseTagPrefix,
            assetName: $"dorknet-server-{version.VersionKey}-win-x64.zip",
            ct);

        var zipPath = Path.Combine(AppPaths.LocalRoot, $"download-{Guid.NewGuid():N}.zip");
        await DownloadAsync(asset.BrowserDownloadUrl, zipPath, progress, ct);

        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        ZipFile.ExtractToDirectory(zipPath, dir);
        File.Delete(zipPath);
        File.WriteAllText(marker, asset.NodeId);
        return dir;
    }

    /// <summary>Returns the path to the unpacked client-patcher dir
    /// for <paramref name="version"/>, downloading + caching if needed.</summary>
    public async Task<string> EnsurePatcherAsync(
        VersionEntry version,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(AppPaths.PatchersRoot, version.VersionKey);
        var marker = Path.Combine(dir, ".dorknet-version");
        if (File.Exists(marker)) return dir;

        var asset = await FindAssetAsync(
            tagPrefix: version.ReleaseTagPrefix,
            assetName: $"dorknet-clientpatch-{version.VersionKey}.zip",
            ct);

        var zipPath = Path.Combine(AppPaths.LocalRoot, $"download-{Guid.NewGuid():N}.zip");
        await DownloadAsync(asset.BrowserDownloadUrl, zipPath, progress, ct);

        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        ZipFile.ExtractToDirectory(zipPath, dir);
        File.Delete(zipPath);
        File.WriteAllText(marker, asset.NodeId);
        return dir;
    }

    private async Task<ReleaseAsset> FindAssetAsync(
        string tagPrefix, string assetName, CancellationToken ct)
    {
        // List releases (paginated, but we only need the most recent
        // matching prefix). Default page size is 30 which covers all
        // realistic per-version release histories.
        var releases = await Http.GetFromJsonAsync<List<Release>>(ReleasesApi, ct)
            ?? throw new InvalidOperationException("GitHub Releases returned null");

        var matching = releases
            .Where(r => r.TagName.StartsWith(tagPrefix, StringComparison.Ordinal))
            .OrderByDescending(r => r.PublishedAt)
            .ToList();

        if (matching.Count == 0)
            throw new InvalidOperationException(
                $"No release found with tag prefix '{tagPrefix}'. " +
                "Run the per-version branch's release workflow before booting host mode.");

        foreach (var rel in matching)
        {
            var asset = rel.Assets.FirstOrDefault(a =>
                a.Name.Equals(assetName, StringComparison.Ordinal));
            if (asset is not null) return asset;
        }

        throw new InvalidOperationException(
            $"Found {matching.Count} release(s) matching '{tagPrefix}' but none contained " +
            $"the asset '{assetName}'. Check the release-workflow asset naming.");
    }

    private async Task DownloadAsync(
        string url, string destPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var totalBytes = resp.Content.Headers.ContentLength ?? -1L;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            progress?.Report(new DownloadProgress(readTotal, totalBytes));
        }
    }

    private sealed class Release
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; set; }
        [JsonPropertyName("assets")] public List<ReleaseAsset> Assets { get; set; } = new();
    }

    private sealed class ReleaseAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("node_id")] public string NodeId { get; set; } = "";
    }
}

public readonly record struct DownloadProgress(long BytesRead, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? (double)BytesRead / TotalBytes : 0;
}
