using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Fetches <c>tunnelto</c> straight from the upstream
/// release feed (agrinman/tunnelto) the first time the launcher needs
/// it, so end users don't have to set up Scoop / Cargo / brew before
/// hosting. The downloaded binary lands in <see cref="AppPaths.LocalRoot"/>,
/// which is the first path <see cref="TunneltoTunnel"/> probes after
/// the side-by-side install dir.
///
/// <para>Upstream's release assets (as of 0.1.18) are uneven:
/// <list type="bullet">
///   <item>Windows ships a direct <c>tunnelto-windows.exe</c> — easy.</item>
///   <item>Linux ships a <c>tunnelto-linux.tar.gz</c> archive — easy.</item>
///   <item>macOS only publishes Homebrew bottles, which we can't crack
///   open cleanly. macOS falls through to a clear "brew install tunnelto"
///   error instead.</item>
/// </list></para></summary>
public sealed class TunneltoInstaller
{
    private const string ReleasesApi =
        "https://api.github.com/repos/agrinman/tunnelto/releases/latest";

    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DorkNet-Launcher-TunneltoInstaller/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Make sure tunnelto is available somewhere
    /// <see cref="TunneltoTunnel"/> will find it. If it's already
    /// installed, returns immediately. Otherwise downloads the
    /// upstream release into <see cref="AppPaths.LocalRoot"/>.
    /// Throws with platform-specific install hints when auto-install
    /// isn't possible (currently macOS).</summary>
    public async Task<string> EnsureInstalledAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Quickly probe the existing-install paths first so a repeat
        // host start doesn't hammer the GitHub API.
        var existing = TryFindExisting();
        if (existing is not null) return existing;

        if (OperatingSystem.IsMacOS())
        {
            // Upstream macOS releases are Homebrew bottles, not portable
            // binaries — we'd have to fish the prefix out and that's a
            // mess. Tell the user to use brew instead.
            throw new PlatformNotSupportedException(
                "Tunnelto auto-install isn't supported on macOS — install it manually: " +
                "`brew install tunnelto` (or `cargo install tunnelto`).");
        }

        if (OperatingSystem.IsLinux() &&
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            // The only Linux asset is x64; ARM users have to build
            // from source or pull via cargo.
            throw new PlatformNotSupportedException(
                "Tunnelto auto-install only covers Linux x64. On arm64, run: " +
                "`cargo install tunnelto` (or grab a build from github.com/agrinman/tunnelto/releases).");
        }

        var release = await Http.GetFromJsonAsync<GitHubRelease>(ReleasesApi, ct)
            ?? throw new InvalidOperationException("Couldn't reach github.com/agrinman/tunnelto/releases.");

        var (assetName, kind) = AssetForCurrentPlatform();
        var asset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (asset?.BrowserDownloadUrl is null)
            throw new InvalidOperationException(
                $"The upstream release doesn't ship a '{assetName}' asset. " +
                $"Got assets: {string.Join(", ", release.Assets?.Select(a => a.Name) ?? Array.Empty<string>())}");

        var binaryName = OperatingSystem.IsWindows() ? "tunnelto.exe" : "tunnelto";
        var dest = Path.Combine(AppPaths.LocalRoot, binaryName);

        // Stream to a temp file first so a half-finished download
        // doesn't leave a broken binary in place.
        var stagingPath = Path.Combine(AppPaths.LocalRoot, $"tunnelto-staging-{Guid.NewGuid():N}");
        try
        {
            await DownloadAsync(asset.BrowserDownloadUrl, stagingPath, progress, ct);
            switch (kind)
            {
                case AssetKind.RawExe:
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(stagingPath, dest);
                    break;
                case AssetKind.TarGz:
                    ExtractTunneltoFromTarGz(stagingPath, dest);
                    File.Delete(stagingPath);
                    // Mark executable on Unix — extracted files often
                    // come out without the mode bits set.
                    if (!OperatingSystem.IsWindows())
                        TryChmodExec(dest);
                    break;
            }
            return dest;
        }
        catch
        {
            if (File.Exists(stagingPath)) try { File.Delete(stagingPath); } catch { }
            throw;
        }
    }

    /// <summary>Mirror of <see cref="TunneltoTunnel.ResolveExecutable"/>'s
    /// search order — kept here so the installer can short-circuit when
    /// the user already installed tunnelto via Scoop / brew / cargo.</summary>
    private static string? TryFindExisting()
    {
        var isWindows = OperatingSystem.IsWindows();
        var name = isWindows ? "tunnelto.exe" : "tunnelto";

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppPaths.LocalRoot, name),
        };
        if (isWindows)
        {
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop", "shims", name));
        }
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // PATH scan — last resort, in case the user has it somewhere
        // unusual like /usr/local/bin or via cargo's bin dir.
        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(p => Path.Combine(p.Trim(), name))
            .FirstOrDefault(File.Exists);
        return onPath;
    }

    private static (string assetName, AssetKind kind) AssetForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return ("tunnelto-windows.exe", AssetKind.RawExe);
        if (OperatingSystem.IsLinux())   return ("tunnelto-linux.tar.gz", AssetKind.TarGz);
        throw new PlatformNotSupportedException("No tunnelto auto-install for this platform.");
    }

    /// <summary>Walks the tarball looking for a regular file named
    /// "tunnelto" (typically at the root). Writes it to
    /// <paramref name="dest"/>. The archive itself is a single-binary
    /// release so we don't need to faithfully recreate the directory
    /// structure.</summary>
    private static void ExtractTunneltoFromTarGz(string archivePath, string dest)
    {
        using var fs = File.OpenRead(archivePath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new TarReader(gz);
        while (tar.GetNextEntry() is { } entry)
        {
            // Only interested in the binary itself. The archive may
            // contain README / LICENSE which we skip.
            var fname = Path.GetFileName(entry.Name);
            if (entry.EntryType != TarEntryType.RegularFile) continue;
            if (!fname.Equals("tunnelto", StringComparison.OrdinalIgnoreCase)) continue;

            if (File.Exists(dest)) File.Delete(dest);
            using var outFs = File.Create(dest);
            entry.DataStream?.CopyTo(outFs);
            return;
        }
        throw new InvalidDataException(
            "tunnelto-linux.tar.gz didn't contain a 'tunnelto' binary. Asset may be malformed.");
    }

    /// <summary>Mark a file as executable on Unix systems. Uses
    /// <see cref="File.SetUnixFileMode"/> (available since .NET 7).
    /// No-op on Windows since the NTFS execute bit doesn't apply.</summary>
    private static void TryChmodExec(string path)
    {
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch { /* best-effort */ }
    }

    private static async Task DownloadAsync(
        string url, string destPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            progress?.Report(new DownloadProgress(read, total));
        }
    }

    private enum AssetKind { RawExe, TarGz }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }
    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
