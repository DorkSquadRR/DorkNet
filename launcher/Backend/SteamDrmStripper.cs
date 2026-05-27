using System.Diagnostics;
using System.IO;

namespace DorkNet.Launcher.Backend;

/// <summary>Removes the SteamStub DRM wrapper from
/// <c>Recroom_Release.exe</c> so the launcher can spawn it without
/// Steam loaded. No-op when the exe isn't packed (Oculus / Quest
/// builds, or installs already unwrapped by a prior run).
///
/// <para>Detection is a PE-section walk: SteamStub Variant 3.1
/// (the variant used for the 2020 Rec Room build) adds a <c>.bind</c>
/// section that contains the encrypted entry-point stub. The presence
/// of <c>.bind</c> + the standard packed-PE signature is a reliable
/// "needs Steamless" indicator without launching Steamless itself.</para>
///
/// <para>When stripping: the original exe is renamed to
/// <c>Recroom_Release.exe.steamwrapped</c> as a backup, and
/// Steamless's <c>.unpacked.exe</c> output is renamed in its place.
/// Re-runs detect the (now-unpacked) exe and skip.</para></summary>
public static class SteamDrmStripper
{
    /// <summary>Strip Steam DRM from the Rec Room exe in
    /// <paramref name="recRoomDir"/> if needed. Returns a result the
    /// caller can use to skip/show progress steps. The Rec Room dir is
    /// the same path the user picks (i.e. <c>*_Data</c>'s parent,
    /// which contains <c>Recroom_Release.exe</c>).</summary>
    public static async Task<StripResult> StripIfNeededAsync(
        string recRoomDir,
        IProgress<DownloadProgress>? downloadProgress = null,
        CancellationToken ct = default)
    {
        // The user picks the *_Data folder; the .exe lives in its
        // parent. RecRoomPicker.Validate ensures this layout exists
        // before we ever get here.
        var dataFolder = new DirectoryInfo(recRoomDir);
        var installRoot = dataFolder.Parent?.FullName ?? recRoomDir;
        var exe = Path.Combine(installRoot, "Recroom_Release.exe");
        if (!File.Exists(exe)) return StripResult.NotApplicable;

        if (!HasSteamStub(exe)) return StripResult.AlreadyUnpacked;

        var steamlessCli = await SteamlessInstaller.EnsureAsync(downloadProgress, ct);
        var unpacked = exe + ".unpacked.exe";
        if (File.Exists(unpacked)) File.Delete(unpacked);

        var psi = new ProcessStartInfo
        {
            FileName = steamlessCli,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = installRoot,
        };
        psi.ArgumentList.Add("--quiet");
        psi.ArgumentList.Add(exe);

        using var proc = Process.Start(psi) ??
            throw new InvalidOperationException("Failed to spawn Steamless.CLI.exe");
        await proc.WaitForExitAsync(ct);

        if (!File.Exists(unpacked))
        {
            // Steamless prints its findings to stdout, so include that
            // tail in the error to help users figure out what went wrong.
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = await proc.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException(
                "Steamless ran but produced no .unpacked.exe — install may not be the Steam build, " +
                "or the SteamStub variant isn't supported.\n" +
                $"stdout: {stdout.TrimEnd()}\nstderr: {stderr.TrimEnd()}");
        }

        var backup = exe + ".steamwrapped";
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(exe, backup);
        File.Move(unpacked, exe);
        return StripResult.Stripped;
    }

    /// <summary>Walk PE section headers looking for a <c>.bind</c>
    /// section, which SteamStub Variant 3.1 adds during packing.
    /// Returns false on any read error — the patcher's later steps
    /// will catch a malformed exe with a clearer message.</summary>
    private static bool HasSteamStub(string exePath)
    {
        try
        {
            using var fs = File.OpenRead(exePath);
            using var br = new BinaryReader(fs);

            // DOS header at offset 0; PE-header offset stored at 0x3C.
            fs.Position = 0x3C;
            var peOffset = br.ReadInt32();
            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) return false; // "PE\0\0"

            // Skip COFF header (20 bytes) to get to the optional
            // header; we need its size to find the section table.
            fs.Position = peOffset + 4 + 2; // skip Machine
            var sectionCount = br.ReadUInt16();
            fs.Position += 12; // skip TimeStamp, PtrToSymTab, NumSyms
            var optHeaderSize = br.ReadUInt16();
            fs.Position += 2; // skip Characteristics

            // Sections start right after the optional header.
            fs.Position = peOffset + 4 + 20 + optHeaderSize;

            // Each section header is 40 bytes, name is the first 8.
            var nameBytes = new byte[8];
            for (var i = 0; i < sectionCount; i++)
            {
                fs.Read(nameBytes, 0, 8);
                var name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                if (name == ".bind") return true;
                fs.Position += 32; // rest of section header
            }
            return false;
        }
        catch { return false; }
    }
}

public enum StripResult
{
    /// <summary>No <c>Recroom_Release.exe</c> at the expected path —
    /// caller should treat this as a soft skip (Oculus install
    /// shouldn't fail just because the exe name is different).</summary>
    NotApplicable,

    /// <summary>The exe was already unwrapped (no .bind section). No
    /// work done. Common on second runs and on non-Steam installs.</summary>
    AlreadyUnpacked,

    /// <summary>SteamStub was detected and Steamless successfully
    /// produced an unwrapped binary. Original backed up to
    /// <c>Recroom_Release.exe.steamwrapped</c>.</summary>
    Stripped,
}
