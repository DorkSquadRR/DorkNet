using System.Windows.Forms;

namespace DorkNet.Launcher.Backend;

/// <summary>Windows folder picker for the user's
/// <c>Recroom_Release_Data</c> directory. Per project policy: never
/// auto-detect via Steam library scanning, always ask the user.</summary>
public static class RecRoomPicker
{
    public static string? Pick(string? initialPath = null)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select your Rec Room install's *_Data folder " +
                          "(e.g. Recroom_Release_Data inside the install).",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = initialPath ?? Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86),
        };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// <summary>Quick sanity check that the picked path looks like a
    /// Rec Room install. Doesn't validate the build version — that's
    /// the patcher's job. Just checks for the canonical files so we
    /// catch "user picked their Documents folder" mistakes early.</summary>
    public static (bool ok, string reason) Validate(string path)
    {
        if (string.IsNullOrEmpty(path)) return (false, "no path selected");
        if (!Directory.Exists(path)) return (false, $"folder does not exist: {path}");

        // Recroom_Release_Data/StreamingAssets is the consistent
        // checkable subfolder across all Rec Room builds we target.
        var streaming = Path.Combine(path, "StreamingAssets");
        if (!Directory.Exists(streaming))
            return (false, "folder is missing StreamingAssets — pick the *_Data folder inside your Rec Room install");

        return (true, "");
    }
}
