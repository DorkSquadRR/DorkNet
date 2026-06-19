using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace DorkNet.Launcher.Backend;

/// <summary>Resolves a usable <c>Steamless.CLI.exe</c>: prefers a
/// side-by-side install, falls back to the cached copy under
/// <c>%LOCALAPPDATA%\DorkNet\Steamless\</c>, otherwise downloads the
/// pinned release zip from atom0s/Steamless on GitHub.
///
/// <para>Steamless strips the SteamStub Variant 3.1 DRM wrapper from
/// Rec Room's <c>Recroom_Release.exe</c> so the launcher can run it
/// without Steam loaded. Rec Room users on Oculus or non-Steam builds
/// don't need this; we detect the wrapper presence in
/// <see cref="SteamDrmStripper"/> and only invoke Steamless when the
/// .bind section is actually present.</para>
///
/// <para>Pinned to v3.1.0.5 — current stable that handles 2020-era
/// Steam DRM. Newer versions are drop-in compatible but we pin so a
/// breaking upstream change doesn't surprise hosts.</para></summary>
public static class SteamlessInstaller
{
    private const string PinnedVersion = "v3.1.0.5";
    // Upstream names the asset "Steamless.{ver}.-.by.atom0s.zip" — NOT
    // "Steamless.{ver}.zip". Getting this wrong is a 404 at the
    // "Unlock Rec Room from Steam" step.
    private const string ZipUrl =
        "https://github.com/atom0s/Steamless/releases/download/" +
        PinnedVersion + "/Steamless." + PinnedVersion + ".-.by.atom0s.zip";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public static async Task<string> EnsureAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (TryFindLocal(out var existing)) return existing;

        var cacheDir = Path.Combine(AppPaths.LocalRoot, "Steamless");
        var zipPath = Path.Combine(AppPaths.LocalRoot, $"Steamless.{PinnedVersion}.zip");
        Directory.CreateDirectory(AppPaths.LocalRoot);

        await DownloadAsync(ZipUrl, zipPath, progress, ct);
        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length < 500_000)
            throw new InvalidOperationException(
                $"Steamless download produced an unexpectedly small file at {zipPath}. " +
                "Check internet connection + retry.");

        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        Directory.CreateDirectory(cacheDir);
        ZipFile.ExtractToDirectory(zipPath, cacheDir);

        var cli = Path.Combine(cacheDir, "Steamless.CLI.exe");
        if (!File.Exists(cli))
            throw new InvalidOperationException(
                $"Extracted Steamless is missing Steamless.CLI.exe at {cli} — release layout may have changed");
        return cli;
    }

    private static bool TryFindLocal(out string path)
    {
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "Steamless", "Steamless.CLI.exe");
        if (File.Exists(sideBySide)) { path = sideBySide; return true; }

        var cached = Path.Combine(AppPaths.LocalRoot, "Steamless", "Steamless.CLI.exe");
        if (File.Exists(cached)) { path = cached; return true; }

        path = string.Empty;
        return false;
    }

    private static async Task DownloadAsync(
        string url, string destPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var temp = destPath + ".download";
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(temp))
            {
                var buf = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct);
                    readTotal += read;
                    progress?.Report(new DownloadProgress(readTotal, total));
                }
            }
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(temp, destPath);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }
}
