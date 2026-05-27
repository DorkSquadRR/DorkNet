using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace DorkNet.Launcher.Backend;

/// <summary>Runs a localtunnel client. Prefer angelobreuer/localtunnel.net
/// because it is a native .NET CLI and does not require Node.js; fall
/// back to npx localtunnel when users already have Node on PATH.</summary>
public sealed class LocaltunnelTunnel : IAsyncDisposable
{
    private Process? _process;
    private TaskCompletionSource<string>? _urlReady;
    private readonly object _outputLock = new();
    private readonly Queue<string> _recentOutput = new();

    public string? PublicUrl { get; private set; }

    public async Task<string> StartAsync(int localPort, CancellationToken ct = default)
    {
        var executable = await LocaltunnelInstaller.TryEnsureAsync(ct: ct);
        LocaltunnelKind kind;
        if (executable is not null)
        {
            kind = LocaltunnelKind.DotNet;
        }
        else
        {
            executable = ResolveExecutable(out kind);
        }
        _urlReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (kind == LocaltunnelKind.DotNet)
        {
            psi.ArgumentList.Add("--no-dashboard");
            psi.ArgumentList.Add("--host");
            psi.ArgumentList.Add("127.0.0.1");
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(localPort.ToString());
            psi.ArgumentList.Add("http");
        }
        else
        {
            psi.ArgumentList.Add("--yes");
            psi.ArgumentList.Add("localtunnel");
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(localPort.ToString());
            psi.ArgumentList.Add("--local-host");
            psi.ArgumentList.Add("127.0.0.1");
        }

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start localtunnel through npx.");
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            if (_urlReady is null || _urlReady.Task.IsCompleted) return;
            _urlReady.TrySetException(new InvalidOperationException(
                "localtunnel exited before reporting a public URL." + FormatRecentOutput()));
        };
        _process.OutputDataReceived += (_, e) => ParseLine(e.Data);
        _process.ErrorDataReceived += (_, e) => ParseLine(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var completed = await Task.WhenAny(_urlReady.Task, Task.Delay(TimeSpan.FromSeconds(60), ct));
        if (completed != _urlReady.Task)
        {
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("localtunnel did not report a public URL within 60 seconds." + FormatRecentOutput());
        }

        PublicUrl = await _urlReady.Task;
        return PublicUrl;
    }

    private static string ResolveExecutable(out LocaltunnelKind kind)
    {
        var ltName = OperatingSystem.IsWindows() ? "localtunnel.exe" : "localtunnel";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, ltName),
            Path.Combine(AppPaths.LocalRoot, ltName),
        };
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            kind = LocaltunnelKind.DotNet;
            return candidate;
        }
        var ltOnPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(p => Path.Combine(p.Trim(), ltName))
            .FirstOrDefault(File.Exists);
        if (ltOnPath is not null)
        {
            kind = LocaltunnelKind.DotNet;
            return ltOnPath;
        }

        var name = OperatingSystem.IsWindows() ? "npx.cmd" : "npx";
        var onPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(p => Path.Combine(p.Trim(), name))
            .FirstOrDefault(File.Exists);
        if (onPath is not null)
        {
            kind = LocaltunnelKind.Npx;
            return onPath;
        }

        throw new FileNotFoundException(
            "localtunnel was not found. Put angelobreuer/localtunnel.net's localtunnel.exe next to dorknet.exe, or install Node.js LTS for the npx fallback.");
    }

    private static readonly Regex UrlRegex = new(
        @"https://[a-z0-9][a-z0-9\-]*\.(?:loca\.lt|localtunnel\.me)",
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
            return _recentOutput.Count == 0
                ? ""
                : " Last localtunnel output: " + string.Join(" | ", _recentOutput);
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

    private enum LocaltunnelKind { DotNet, Npx }
}
