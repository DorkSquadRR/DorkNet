namespace DorkNet.Launcher.Backend;

/// <summary>Single source of truth for where the Easy app stores
/// per-user state, cached server binaries, and run-time logs. Roots at
/// <c>%APPDATA%\DorkNet</c> (roaming) for state/config, and
/// <c>%LOCALAPPDATA%\DorkNet</c> (non-roaming) for the larger cached
/// server binaries — the launcher works without either, but caching
/// downloads avoids re-fetching on every launch.</summary>
public static class AppPaths
{
    public static string RoamingRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DorkNet");

    public static string LocalRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DorkNet");

    /// <summary>Per-user app state — mode (host/join), picked Rec Room
    /// path, selected version, Photon AppIds, etc. Roaming so it
    /// follows the user across machines if they use roaming profiles.</summary>
    public static string StateFile => Path.Combine(RoamingRoot, "state.json");

    /// <summary>Downloaded server binaries, one folder per version key
    /// (e.g. <c>servers\december_2020_12_18\</c>). Big — these are
    /// self-contained .NET publishes, ~50 MB each. Local (non-roaming)
    /// so they don't bloat domain roaming.</summary>
    public static string ServersRoot => Path.Combine(LocalRoot, "servers");

    /// <summary>Downloaded client-patcher zips and the unpacked patcher
    /// files, one folder per version.</summary>
    public static string PatchersRoot => Path.Combine(LocalRoot, "patchers");

    /// <summary>Cached version-manifest snapshot + ETag — lets the
    /// launcher boot offline (using the last-seen versions.json) and
    /// skips the round-trip when nothing changed.</summary>
    public static string ManifestCacheFile => Path.Combine(LocalRoot, "versions.cache.json");

    /// <summary>Rolling log of launcher operations + the server's
    /// stdout/stderr per-session. Useful when "it didn't start" needs
    /// to be debugged.</summary>
    public static string LogsRoot => Path.Combine(LocalRoot, "logs");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RoamingRoot);
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(ServersRoot);
        Directory.CreateDirectory(PatchersRoot);
        Directory.CreateDirectory(LogsRoot);
    }
}
