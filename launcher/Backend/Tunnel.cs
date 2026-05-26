using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DorkNet.Launcher.Backend;

/// <summary>Wraps Cloudflare's <c>cloudflared</c> quick-tunnel so the
/// host doesn't need to open ports / configure DNS. Each launch gets
/// a fresh random <c>*.trycloudflare.com</c> URL.
///
/// <para>Requires <c>cloudflared.exe</c> on PATH or alongside the
/// launcher binary. If absent, returns a clear error and the host
/// can fall back to LAN-only play.</para></summary>
public sealed class Tunnel : IAsyncDisposable
{
    private Process? _process;
    private TaskCompletionSource<string>? _urlReady;
    public string? PublicUrl { get; private set; }

    public async Task<string> StartAsync(int localPort, CancellationToken ct = default)
    {
        var exe = FindCloudflared() ??
            throw new FileNotFoundException(
                "cloudflared not found. Install from " +
                "https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/ " +
                "and put cloudflared.exe on PATH (or next to the DorkNet launcher).");

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

    private static string? FindCloudflared()
    {
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        if (File.Exists(sideBySide)) return sideBySide;
        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(';')
            .Select(p => Path.Combine(p, "cloudflared.exe"))
            .FirstOrDefault(File.Exists);
        return onPath;
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
