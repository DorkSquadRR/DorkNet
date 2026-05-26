using System.Diagnostics;

namespace DorkNet.Launcher.Backend;

/// <summary>Invokes the per-version branch's <c>install-plugin.ps1</c>
/// (or <c>install-legacy-client.ps1</c> as a fallback) on the user's
/// Rec Room install. The patcher zip is unpacked by
/// <see cref="ReleaseDownloader.EnsurePatcherAsync"/> first; we just
/// run the script from inside that unpacked tree.</summary>
public sealed class ClientPatcher
{
    public async Task<PatchResult> ApplyAsync(
        string patcherDir,
        string recRoomPath,
        string photonAppId,
        string photonVoiceAppId,
        string apexHost,
        CancellationToken ct = default)
    {
        var script = Path.Combine(patcherDir, "install-plugin.ps1");
        if (!File.Exists(script))
        {
            // Some branches may ship only the legacy installer.
            script = Path.Combine(patcherDir, "install-legacy-client.ps1");
            if (!File.Exists(script))
            {
                return PatchResult.Failure(
                    "Patcher zip is missing install-plugin.ps1 and install-legacy-client.ps1. " +
                    "Open an issue with the version key + the contents of " +
                    $"{patcherDir}.");
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = patcherDir,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-RecRoomPath"); psi.ArgumentList.Add(recRoomPath);
        psi.ArgumentList.Add("-PhotonAppId"); psi.ArgumentList.Add(photonAppId);
        psi.ArgumentList.Add("-PhotonVoiceAppId");
        psi.ArgumentList.Add(string.IsNullOrEmpty(photonVoiceAppId) ? photonAppId : photonVoiceAppId);
        if (!string.IsNullOrEmpty(apexHost))
        {
            psi.ArgumentList.Add("-ServerHost"); psi.ArgumentList.Add(apexHost);
        }

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to spawn powershell.exe");
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        return p.ExitCode == 0
            ? PatchResult.Success(stdout)
            : PatchResult.Failure(string.IsNullOrEmpty(stderr) ? stdout : stderr);
    }
}

public sealed record PatchResult(bool Ok, string Log)
{
    public static PatchResult Success(string log) => new(true, log);
    public static PatchResult Failure(string log) => new(false, log);
}
