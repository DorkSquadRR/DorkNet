using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DorkNet.Launcher.Backend;

/// <summary>Wraps Tunnelto for remote hosting with provider-owned
/// subdomains. The user supplies a wildcard-capable base such as
/// <c>dorknet.tunnelto.me</c>; the server then publishes
/// <c>api.dorknet.tunnelto.me</c>, <c>auth.dorknet.tunnelto.me</c>,
/// and friends.</summary>
public sealed class TunneltoTunnel : IAsyncDisposable
{
    private Process? _process;
    private TaskCompletionSource<string>? _urlReady;
    public string? PublicUrl { get; private set; }

    public async Task<string> StartAsync(
        string requestedHost,
        int localPort,
        CancellationToken ct = default)
    {
        var exe = ResolveExecutable();
        var tunnelHost = NormalizeHost(requestedHost);
        if (string.IsNullOrWhiteSpace(tunnelHost))
            throw new InvalidOperationException("Enter a Tunnelto base host first, e.g. dorknet.tunnelto.me.");

        _urlReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("add");
        psi.ArgumentList.Add(tunnelHost);
        psi.ArgumentList.Add(localPort.ToString());

        _process = Process.Start(psi) ??
            throw new InvalidOperationException("Failed to spawn tunnelto.exe");
        _process.OutputDataReceived += (_, e) => ParseLine(e.Data);
        _process.ErrorDataReceived += (_, e) => ParseLine(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));
        PublicUrl = await _urlReady.Task.WaitAsync(timeoutCts.Token);
        return PublicUrl;
    }

    public static string NormalizeHost(string value)
    {
        var host = value.Trim();
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host = new Uri(host).Host;
        }

        host = host.Trim().TrimEnd('/').ToLowerInvariant();
        if (host.StartsWith("*.", StringComparison.Ordinal)) host = host[2..];
        return host;
    }

    public static string GenerateBaseHost()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var token = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"dorknet-{token}.tunnelto.me";
    }

    private static string ResolveExecutable()
    {
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "tunnelto.exe");
        if (File.Exists(sideBySide)) return sideBySide;

        var local = Path.Combine(AppPaths.LocalRoot, "tunnelto.exe");
        if (File.Exists(local)) return local;

        var scoopShim = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "scoop", "shims", "tunnelto.exe");
        if (File.Exists(scoopShim)) return scoopShim;

        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(';')
            .Select(p => Path.Combine(p.Trim(), "tunnelto.exe"))
            .FirstOrDefault(File.Exists);
        if (onPath is not null) return onPath;

        throw new FileNotFoundException(
            "tunnelto.exe was not found. Install Tunnelto first: " +
            "scoop bucket add tunnelto https://github.com/asabi/scoop-tunnelto && scoop install tunnelto");
    }

    private static readonly Regex UrlRegex = new(
        @"https://(?!dashboard\.)(?:[a-z0-9][a-z0-9\-]*\.)+tunnelto\.me",
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
