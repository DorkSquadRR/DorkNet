using System.IO;
using System.Diagnostics;
using System.Text;

namespace DorkNet.Launcher.Backend;

/// <summary>Spawns + manages the downloaded server binary as a child
/// process. Each running launcher owns one server at a time — the
/// previous one is killed before starting a new one.</summary>
public sealed class ServerProcess : IAsyncDisposable
{
    private Process? _process;
    private readonly StringBuilder _stderrBuffer = new();
    public string? StdoutLogPath { get; private set; }
    public bool IsRunning => _process is not null && !_process.HasExited;

    /// <summary>Boot the server in <paramref name="serverDir"/>.
    /// Passes the user's Photon AppIds + apex domain via environment
    /// so we don't need to write a per-launch appsettings.Local.json.</summary>
    public Task StartAsync(
        string serverDir,
        AppState state,
        string apex,
        CancellationToken ct = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("A server is already running; stop it first.");

        var exe = FindServerExecutable(serverDir);
        StdoutLogPath = Path.Combine(AppPaths.LogsRoot, $"server-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = serverDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5005";
        psi.Environment["Database__Provider"] = "sqlite";
        psi.Environment["Jwt__Secret"] = EnsureJwtSecret();
        psi.Environment["Photon__AppId"] = state.PhotonAppId;
        psi.Environment["Photon__VoiceAppId"] =
            string.IsNullOrEmpty(state.PhotonVoiceAppId) ? state.PhotonAppId : state.PhotonVoiceAppId;
        psi.Environment["Photon__CloudRegion"] = state.PhotonRegion;
        psi.Environment["Domain__Apex"] = apex;

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _process.ErrorDataReceived += (_, e) => { AppendLog(e.Data); if (e.Data is not null) _stderrBuffer.AppendLine(e.Data); };
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        return Task.CompletedTask;
    }

    private void AppendLog(string? line)
    {
        if (line is null || StdoutLogPath is null) return;
        try { File.AppendAllText(StdoutLogPath, line + Environment.NewLine); }
        catch { /* disk full / locked / can't help here */ }
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited) return;
        try
        {
            _process.CloseMainWindow();
            if (!_process.WaitForExit(2000))
            {
                _process.Kill(entireProcessTree: true);
            }
            await _process.WaitForExitAsync();
        }
        catch { /* already gone */ }
    }

    public string TailStderr(int lines = 30)
    {
        var all = _stderrBuffer.ToString().Split('\n');
        return string.Join('\n', all[Math.Max(0, all.Length - lines)..]);
    }

    private static string FindServerExecutable(string serverDir)
    {
        // Self-contained .NET publish produces a single .exe per
        // RuntimeIdentifier. The server's csproj sets AssemblyName so
        // we know the name; fall back to scanning if the rename
        // changes.
        var canonical = Path.Combine(serverDir, "DorkNet.Server.exe");
        if (File.Exists(canonical)) return canonical;
        var any = Directory.GetFiles(serverDir, "*.exe").FirstOrDefault();
        if (any is null)
            throw new FileNotFoundException(
                $"No .exe found in {serverDir}. The release artifact may be corrupt.");
        return any;
    }

    /// <summary>JWT signing key — random per-machine, persisted under
    /// LocalAppData so re-launches keep the same key (so existing
    /// tokens stay valid).</summary>
    private static string EnsureJwtSecret()
    {
        var path = Path.Combine(AppPaths.LocalRoot, "jwt.secret");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 64) return existing;
        }
        var bytes = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var secret = Convert.ToBase64String(bytes);
        File.WriteAllText(path, secret);
        return secret;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process?.Dispose();
    }
}
