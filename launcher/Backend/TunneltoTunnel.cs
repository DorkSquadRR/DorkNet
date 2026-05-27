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
    private readonly object _outputLock = new();
    private readonly Queue<string> _recentOutput = new();
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
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(localPort.ToString());
        if (TryGetTunneltoSubdomain(tunnelHost, out var subdomain))
        {
            psi.ArgumentList.Add("--subdomain");
            psi.ArgumentList.Add(subdomain);
        }

        _process = Process.Start(psi) ??
            throw new InvalidOperationException("Failed to spawn tunnelto.exe");
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            if (_urlReady is null || _urlReady.Task.IsCompleted) return;
            _urlReady.TrySetException(new InvalidOperationException(
                "Tunnelto exited before reporting a public URL." + FormatRecentOutput()));
        };
        _process.OutputDataReceived += (_, e) => ParseLine(e.Data);
        _process.ErrorDataReceived += (_, e) => ParseLine(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var readyTask = _urlReady.Task;
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(45), ct);
        var completed = await Task.WhenAny(readyTask, timeoutTask);
        if (completed == timeoutTask)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "Tunnelto didn't report a public URL within 45 seconds." + FormatRecentOutput());
        }

        PublicUrl = await readyTask;
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

    private static bool TryGetTunneltoSubdomain(string host, out string subdomain)
    {
        const string suffix = ".tunnelto.me";
        subdomain = "";
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;

        subdomain = host[..^suffix.Length];
        return !string.IsNullOrWhiteSpace(subdomain);
    }

    private static string ResolveExecutable()
    {
        var isWindows = OperatingSystem.IsWindows();
        var name = isWindows ? "tunnelto.exe" : "tunnelto";

        var sideBySide = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(sideBySide)) return sideBySide;

        var local = Path.Combine(AppPaths.LocalRoot, name);
        if (File.Exists(local)) return local;

        if (isWindows)
        {
            var scoopShim = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop", "shims", name);
            if (File.Exists(scoopShim)) return scoopShim;
        }

        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(p => Path.Combine(p.Trim(), name))
            .FirstOrDefault(File.Exists);
        if (onPath is not null) return onPath;

        var hint = isWindows
            ? "scoop bucket add tunnelto https://github.com/asabi/scoop-tunnelto && scoop install tunnelto"
            : "cargo install tunnelto  # or grab a binary from github.com/agrinman/tunnelto/releases";
        throw new FileNotFoundException(
            $"{name} was not found. Install Tunnelto first: {hint}");
    }

    private static readonly Regex UrlRegex = new(
        @"https://(?!dashboard\.)(?:[a-z0-9][a-z0-9\-]*\.)+tunnelto\.me",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void ParseLine(string? line)
    {
        if (line is null) return;
        lock (_outputLock)
        {
            _recentOutput.Enqueue(line);
            while (_recentOutput.Count > 8) _recentOutput.Dequeue();
        }

        if (_urlReady is null || _urlReady.Task.IsCompleted) return;
        var match = UrlRegex.Match(line);
        if (match.Success) _urlReady.TrySetResult(match.Value);
    }

    private string FormatRecentOutput()
    {
        lock (_outputLock)
        {
            if (_recentOutput.Count == 0) return "";
            return " Last Tunnelto output: " + string.Join(" | ", _recentOutput);
        }
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
