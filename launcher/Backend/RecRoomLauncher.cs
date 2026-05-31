using System.Diagnostics;
using System.IO;

namespace DorkNet.Launcher.Backend;

/// <summary>Launches the user's already-patched Rec Room install
/// directly. Never goes through Steam — DorkNet users may not own
/// the game on Steam.
///
/// <para><b>2020.03 vs 2020.12 build layout:</b></para>
/// <list type="bullet">
///   <item>2020.03 (pre-EAC): single binary <c>Recroom_Release.exe</c>
///   alongside <c>Recroom_Release_Data/</c>. Run that directly.</item>
///   <item>2020.12+ (EAC): the real Unity binary is <c>RecRoom.exe</c>
///   with its data in <c>RecRoom_Data/</c>; <c>Recroom_Release.exe</c>
///   is the Easy Anti-Cheat launcher wrapper that Steam invokes. Run
///   <c>RecRoom.exe</c> directly to skip EAC entirely — EAC never
///   loads into the game process, no binary patching required (this is
///   the same approach <c>tools/remove-eac.ps1</c> documents on the
///   per-version branch).</item>
/// </list>
/// Detection: an <c>EasyAntiCheat/</c> folder next to the binaries
/// flags the EAC layout. Falls back to whichever exe exists if the
/// heuristic is ambiguous.</summary>
public static class RecRoomLauncher
{
    /// <summary>The picked install path is the <c>*_Data</c> folder;
    /// the executable lives at its parent. Returns true if
    /// <see cref="Process.Start(ProcessStartInfo)"/> succeeded.
    /// <paramref name="screenMode"/> passes Rec Room's
    /// <c>+forcemode:screen</c> arg so the game opens in 2D desktop
    /// mode instead of trying to bind a headset.</summary>
    public static bool TryLaunch(string recRoomDataPath, bool screenMode, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(recRoomDataPath))
        {
            error = "No Rec Room install picked.";
            return false;
        }
        var parent = Path.GetDirectoryName(recRoomDataPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent))
        {
            error = $"Couldn't resolve the parent directory of '{recRoomDataPath}'.";
            return false;
        }

        var exe = PickExecutable(parent);
        if (exe is null)
        {
            error = $"Neither RecRoom.exe nor Recroom_Release.exe found next to '{recRoomDataPath}'. " +
                    "Pick the *_Data folder that sits beside the game binary.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = parent,
                UseShellExecute = true,
                Arguments = screenMode ? "+forcemode:screen" : "",
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Pick the right game exe for the install. Prefer
    /// <c>RecRoom.exe</c> on EAC builds (so we skip the EAC launcher
    /// wrapper). Fall back to whichever single binary exists when only
    /// one is present.</summary>
    private static string? PickExecutable(string parent)
    {
        var recRoomExe = Path.Combine(parent, "RecRoom.exe");
        var legacyExe  = Path.Combine(parent, "Recroom_Release.exe");
        var hasEac     = Directory.Exists(Path.Combine(parent, "EasyAntiCheat"));

        // EAC build: RecRoom.exe is the actual game; Recroom_Release.exe
        // is the EAC launcher wrapper. Use the former — it never loads
        // EAC into the process.
        if (hasEac && File.Exists(recRoomExe)) return recRoomExe;
        // Only-one-present cases.
        if (File.Exists(legacyExe))            return legacyExe;
        if (File.Exists(recRoomExe))           return recRoomExe;
        return null;
    }
}
