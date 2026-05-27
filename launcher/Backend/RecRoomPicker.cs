using System.IO;
using Microsoft.Win32;

namespace DorkNet.Launcher.Backend;

/// <summary>Windows folder picker for the user's
/// <c>Recroom_Release_Data</c> directory. Per project policy: never
/// auto-detect via Steam library scanning, always ask the user.
///
/// <para>Uses <see cref="OpenFolderDialog"/> (added in .NET 8) so we
/// don't need to drag WinForms into a WPF project for one dialog.</para></summary>
public static class RecRoomPicker
{
    public static string? Pick(string? initialPath = null)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select your Rec Room install's *_Data folder " +
                    "(e.g. Recroom_Release_Data inside the install).",
            InitialDirectory = initialPath ?? Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86),
            Multiselect = false,
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    /// <summary>Scan common manual-install paths and return the newest
    /// valid <c>*_Data</c> folder, or null if nothing matches. First-run
    /// uses this to skip the file dialog when the user has Rec Room in
    /// an obvious spot. Never touches Steam library folders.</summary>
    public static string? Detect()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(user, "Documents", "Recnet", "dist"),
            Path.Combine(user, "Documents", "Recnet-old", "dist"),
            Path.Combine(user, "Documents", "DorkNet", "dist"),
            Path.Combine(user, "Documents", "dorknet"),
            Path.Combine(user, "Documents", "RecRoom"),
            Path.Combine(user, "Downloads"),
            @"C:\Games",
            @"C:\RecRoom",
            @"D:\Games",
            @"D:\RecRoom",
        };

        var candidates = new List<(string Path, DateTime LastWrite)>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var data = Path.Combine(dir, "Recroom_Release_Data");
                    if (!Directory.Exists(data)) continue;
                    if (!Validate(data).ok) continue;
                    candidates.Add((data, Directory.GetLastWriteTimeUtc(data)));
                }
                var directData = Path.Combine(root, "Recroom_Release_Data");
                if (Directory.Exists(directData) && Validate(directData).ok)
                    candidates.Add((directData, Directory.GetLastWriteTimeUtc(directData)));
            }
            catch { /* permission denied — skip */ }
        }

        return candidates
            .OrderByDescending(c => c.LastWrite)
            .Select(c => c.Path)
            .FirstOrDefault();
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
