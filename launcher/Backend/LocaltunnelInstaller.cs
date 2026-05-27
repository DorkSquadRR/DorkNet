using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

public static class LocaltunnelInstaller
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/angelobreuer/localtunnel.net/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(3),
    };

    static LocaltunnelInstaller()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("DorkNet-Launcher/1.0");
    }

    public static async Task<string?> TryEnsureAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var existing = FindExisting();
        if (existing is not null) return existing;

        try
        {
            Directory.CreateDirectory(AppPaths.LocalRoot);
            var release = await Http.GetFromJsonAsync<GitHubRelease>(LatestReleaseApi, ct);
            var asset = release?.Assets?
                .Where(a => !string.IsNullOrEmpty(a.BrowserDownloadUrl))
                .OrderByDescending(a => ScoreAsset(a.Name ?? ""))
                .FirstOrDefault(a => ScoreAsset(a.Name ?? "") > 0);
            if (asset is null) return null;

            var zipPath = Path.Combine(AppPaths.LocalRoot, $"localtunnel-{Guid.NewGuid():N}.zip");
            await DownloadAsync(asset.BrowserDownloadUrl!, zipPath, progress, ct);
            var extractDir = Path.Combine(AppPaths.LocalRoot, "localtunnel.net");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            File.Delete(zipPath);

            return Directory.GetFiles(extractDir, OperatingSystem.IsWindows() ? "localtunnel.exe" : "localtunnel",
                    SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindExisting()
    {
        var name = OperatingSystem.IsWindows() ? "localtunnel.exe" : "localtunnel";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppPaths.LocalRoot, name),
            Path.Combine(AppPaths.LocalRoot, "localtunnel.net", name),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        return Directory.Exists(Path.Combine(AppPaths.LocalRoot, "localtunnel.net"))
            ? Directory.GetFiles(Path.Combine(AppPaths.LocalRoot, "localtunnel.net"), name, SearchOption.AllDirectories)
                .FirstOrDefault()
            : null;
    }

    private static int ScoreAsset(string name)
    {
        var n = name.ToLowerInvariant();
        if (!n.EndsWith(".zip")) return 0;
        if (OperatingSystem.IsWindows() && n.Contains("win") && n.Contains("x64")) return 100;
        if (OperatingSystem.IsLinux() && n.Contains("linux") && n.Contains("x64")) return 100;
        if (OperatingSystem.IsMacOS() && (n.Contains("osx") || n.Contains("mac")) && n.Contains("x64")) return 100;
        if (OperatingSystem.IsWindows() && n.Contains("win")) return 50;
        return n.Contains("localtunnel") ? 10 : 0;
    }

    private static async Task DownloadAsync(
        string url,
        string destPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);
        var buffer = new byte[128 * 1024];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            progress?.Report(new DownloadProgress(read, total));
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
