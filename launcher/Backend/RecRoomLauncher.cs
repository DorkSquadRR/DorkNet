using System.Diagnostics;
using System.IO;

namespace DorkNet.Launcher.Backend;

/// <summary>Launches the user's already-patched Rec Room install
/// directly via <c>Recroom_Release.exe</c>. Never goes through Steam —
/// DorkNet users may not own the game on Steam.</summary>
public static class RecRoomLauncher
{
    /// <summary>The picked install path is the <c>Recroom_Release_Data</c>
    /// folder; the executable lives at <c>&lt;parent&gt;\Recroom_Release.exe</c>.
    /// Returns true if Process.Start succeeded.</summary>
    public static bool TryLaunch(string recRoomDataPath, out string error)
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
        var exe = Path.Combine(parent, "Recroom_Release.exe");
        if (!File.Exists(exe))
        {
            error = $"Recroom_Release.exe not found at '{exe}'. " +
                    "Pick the *_Data folder that sits next to it.";
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = parent,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
