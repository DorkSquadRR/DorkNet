using System.IO;
using System.Net.Http;

namespace DorkNet.Launcher.Backend;

/// <summary>Resolves a usable <c>cloudflared.exe</c>: prefers a
/// side-by-side binary, falls back to one on PATH, otherwise downloads
/// the latest official release from Cloudflare's GitHub mirror into
/// <see cref="AppPaths.LocalRoot"/> and caches it there forever.
///
/// <para>The cached binary lives at <c>%LOCALAPPDATA%\DorkNet\cloudflared.exe</c>.
/// Re-runs of the launcher reuse it; "update" happens by deleting the
/// cached file and letting the next launch re-download. Worth doing
/// occasionally for security fixes.</para>
///
/// <para>Download source: <c>https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe</c>
/// — GitHub redirects this stable URL to the most-recent release's
/// asset, so we never have to query the Releases API.</para></summary>
public static class CloudflaredInstaller
{
    private const string LatestDownloadUrl =
        "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    /// <summary>Returns the path to a usable cloudflared.exe. Downloads
    /// + caches on first call if needed. Throws if download fails AND
    /// nothing is found locally.</summary>
    public static async Task<string> EnsureAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (TryFindLocal(out var existing)) return existing;

        var dest = Path.Combine(AppPaths.LocalRoot, "cloudflared.exe");
        Directory.CreateDirectory(AppPaths.LocalRoot);

        await DownloadAsync(LatestDownloadUrl, dest, progress, ct);

        if (!File.Exists(dest) || new FileInfo(dest).Length < 1_000_000)
            throw new InvalidOperationException(
                $"cloudflared download produced an unexpectedly small file at {dest}. " +
                "Check internet connection + retry.");

        return dest;
    }

    /// <summary>Synchronous lookup of an already-installed cloudflared.exe
    /// without touching the network. Returns null if not found anywhere.</summary>
    public static string? FindCached()
    {
        return TryFindLocal(out var path) ? path : null;
    }

    private static bool TryFindLocal(out string path)
    {
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        if (File.Exists(sideBySide)) { path = sideBySide; return true; }

        var cached = Path.Combine(AppPaths.LocalRoot, "cloudflared.exe");
        if (File.Exists(cached)) { path = cached; return true; }

        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(';')
            .Select(p => Path.Combine(p.Trim(), "cloudflared.exe"))
            .FirstOrDefault(File.Exists);
        if (onPath is not null) { path = onPath; return true; }

        path = string.Empty;
        return false;
    }

    private static async Task DownloadAsync(
        string url, string destPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        // Use a temp file + atomic move so a half-downloaded binary
        // doesn't get picked up as cached on the next run.
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
