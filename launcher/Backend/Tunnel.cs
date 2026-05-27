using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DorkNet.Launcher.Backend;

/// <summary>Wraps Cloudflare's <c>cloudflared</c> quick-tunnel so the
/// host doesn't need to open ports / configure DNS. Each launch gets
/// a fresh random <c>*.trycloudflare.com</c> URL.
///
/// <para>cloudflared.exe is resolved via
/// <see cref="CloudflaredInstaller.EnsureAsync"/>, which checks
/// side-by-side first, then PATH, then downloads the latest official
/// release from Cloudflare's GitHub mirror on first run. Grandma never
/// has to "install Cloudflare Tunnel" manually.</para></summary>
public sealed class Tunnel : IAsyncDisposable
{
    private Process? _process;
    private TaskCompletionSource<string>? _urlReady;
    public string? PublicUrl { get; private set; }

    public async Task<string> StartAsync(
        int localPort,
        IProgress<DownloadProgress>? downloadProgress = null,
        CancellationToken ct = default)
    {
        var exe = await CloudflaredInstaller.EnsureAsync(downloadProgress, ct);

        _urlReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("tunnel");
        psi.ArgumentList.Add("--no-autoupdate");
        psi.ArgumentList.Add("--url");
        psi.ArgumentList.Add($"http://127.0.0.1:{localPort}");

        _process = Process.Start(psi) ??
            throw new InvalidOperationException("Failed to spawn cloudflared.exe");
        _process.OutputDataReceived += (_, e) => ParseLine(e.Data);
        _process.ErrorDataReceived += (_, e) => ParseLine(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        PublicUrl = await _urlReady.Task.WaitAsync(timeoutCts.Token);
        return PublicUrl;
    }

    private static readonly Regex UrlRegex = new(
        @"https://[a-z0-9\-]+\.trycloudflare\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void ParseLine(string? line)
    {
        if (line is null || _urlReady is null || _urlReady.Task.IsCompleted) return;
        var match = UrlRegex.Match(line);
        if (match.Success) _urlReady.TrySetResult(match.Value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        catch { }
        _process.Dispose();
    }
}
