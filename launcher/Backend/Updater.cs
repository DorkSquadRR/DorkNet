using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Rolling-update check against GitHub releases. The launcher
/// asks for the latest release tag on startup, compares it to its own
/// assembly version, and (if newer) downloads the new self-contained
/// <c>dorknet.exe</c> next to the existing one + writes a tiny swap
/// script. The UI surfaces a banner with a button that runs the script
/// and exits — the script waits for the process to release the exe,
/// copies the new build in place, and relaunches.</summary>
public sealed class Updater
{
    private const string ReleasesApi =
        "https://api.github.com/repos/DorkSquadRR/DorkNet/releases/latest";

    /// <summary>Asset name produced by the Inno installer's publish
    /// step. Must match what's attached to GitHub releases.</summary>
    private const string LauncherAssetName = "dorknet.exe";

    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DorkNet-Launcher-Updater/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Current launcher version, read from the assembly's
    /// InformationalVersion / FileVersion attribute. Falls back to 0.0
    /// in dev builds where neither is set.</summary>
    public static Version CurrentVersion
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info) && TryParseLoose(info, out var v)) return v;
            var fv = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            if (!string.IsNullOrWhiteSpace(fv) && TryParseLoose(fv, out var fvv)) return fvv;
            return new Version(0, 0);
        }
    }

    /// <summary>Looks up GitHub's latest release. Returns null on
    /// network failure or if the local version is already at-or-newer
    /// than what's published. Never throws — update checks must never
    /// block app launch.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(ReleasesApi, ct);
            if (release is null || string.IsNullOrEmpty(release.TagName)) return null;
            if (!TryParseLoose(release.TagName, out var latest)) return null;
            if (latest <= CurrentVersion) return null;

            var asset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, LauncherAssetName, StringComparison.OrdinalIgnoreCase));
            if (asset?.BrowserDownloadUrl is null) return null;

            return new UpdateInfo(latest, asset.BrowserDownloadUrl, release.HtmlUrl);
        }
        catch { return null; }
    }

    /// <summary>Downloads the new exe to the user's local update
    /// staging dir, writes a swap .cmd next to it, and returns the
    /// path to that .cmd. Caller is expected to launch it and exit.</summary>
    public async Task<string> StageUpdateAsync(
        UpdateInfo info,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var stagingDir = Path.Combine(AppPaths.LocalRoot, "update");
        Directory.CreateDirectory(stagingDir);
        var newExePath = Path.Combine(stagingDir, LauncherAssetName);

        using (var response = await Http.GetAsync(info.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(newExePath);
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

        var currentExe = GetCurrentExePath();
        var swapScript = Path.Combine(stagingDir, "swap.cmd");
        // The swap script:
        //   1. Pings until the running dorknet exits (we exit immediately
        //      after spawning it, so this resolves fast).
        //   2. Copies the new exe over the old one. /Y suppresses
        //      confirmation; failure exits with the original errorlevel.
        //   3. Starts the new exe in its install directory.
        //   4. Self-deletes via the start-then-del trick.
        var script =
            "@echo off\r\n" +
            "setlocal\r\n" +
            $"set OLD=\"{currentExe}\"\r\n" +
            $"set NEW=\"{newExePath}\"\r\n" +
            ":wait\r\n" +
            "tasklist /FI \"IMAGENAME eq dorknet.exe\" 2>NUL | find /I \"dorknet.exe\" >NUL\r\n" +
            "if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
            "copy /Y %NEW% %OLD% >NUL || (echo Copy failed & exit /b 1)\r\n" +
            "start \"\" %OLD%\r\n" +
            "del \"%~f0\"\r\n";
        await File.WriteAllTextAsync(swapScript, script, ct);
        return swapScript;
    }

    /// <summary>Best-effort path to the currently-running exe. Used by
    /// the swap script as the destination to overwrite.</summary>
    public static string GetCurrentExePath()
    {
        // Process.MainModule.FileName is the reliable source — handles
        // single-file deployments (where AppContext.BaseDirectory points
        // at an extracted temp dir, not the actual exe location).
        var p = System.Diagnostics.Process.GetCurrentProcess();
        return p.MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "dorknet.exe");
    }

    /// <summary>Parse "v0.2.0" / "0.2.0" / "v0.2.0-rc1" loosely into a
    /// <see cref="Version"/>. Pre-release suffixes are dropped.</summary>
    private static bool TryParseLoose(string s, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim().TrimStart('v', 'V');
        // Strip pre-release / build metadata suffix (semver "-..." or "+...").
        var cut = t.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) t = t.Substring(0, cut);
        // Pad to at least two segments so "1" → "1.0" parses cleanly.
        if (!t.Contains('.')) t += ".0";
        return Version.TryParse(t, out version!);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }
    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

public sealed record UpdateInfo(Version Version, string DownloadUrl, string? ReleaseUrl);
