using System.IO;

namespace DorkNet.Launcher.Backend;

/// <summary>Local dev-time overrides that bypass the GitHub-backed
/// manifest + releases fetches. Set via environment variables before
/// launching <c>dorknet.exe</c>:
///
/// <list type="bullet">
///   <item><c>DORKNET_LOCAL_MANIFEST</c> = full path to a
///   <c>versions.json</c> file. When set + exists,
///   <see cref="VersionsManifest.FetchAsync"/> reads it from disk and
///   skips the GitHub round-trip.</item>
///   <item><c>DORKNET_LOCAL_RELEASES</c> = path to a directory whose
///   children are <c>{version_key}/server/</c> and
///   <c>{version_key}/patcher/</c>. When the launcher would normally
///   download a server / patcher zip, it points at these in-place
///   instead — no copying, no markers, just read the dir.</item>
/// </list>
///
/// <para>Both overrides are no-ops when the env var is unset or the
/// path doesn't exist on disk, so they're safe to leave set
/// permanently in a dev profile.</para></summary>
public static class LocalOverrides
{
    public const string ManifestEnvVar = "DORKNET_LOCAL_MANIFEST";
    public const string ReleasesEnvVar = "DORKNET_LOCAL_RELEASES";

    /// <summary>Returns the configured manifest path if the env var is
    /// set AND the file exists; null otherwise. Caller decides whether
    /// to fall back to the network.</summary>
    public static string? GetLocalManifestPath()
    {
        var path = Environment.GetEnvironmentVariable(ManifestEnvVar);
        if (string.IsNullOrEmpty(path)) return null;
        return File.Exists(path) ? path : null;
    }

    /// <summary>Returns the configured releases-root if the env var is
    /// set AND the directory exists; null otherwise.</summary>
    public static string? GetLocalReleasesRoot()
    {
        var path = Environment.GetEnvironmentVariable(ReleasesEnvVar);
        if (string.IsNullOrEmpty(path)) return null;
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>Returns the local server-dir for <paramref name="versionKey"/>
    /// if the releases-root override is set and the subfolder exists.
    /// Convention: <c>{root}/{version_key}/server/</c>.</summary>
    public static string? GetLocalServerDir(string versionKey)
    {
        var root = GetLocalReleasesRoot();
        if (root is null) return null;
        var dir = Path.Combine(root, versionKey, "server");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>Same as <see cref="GetLocalServerDir"/> but for the
    /// client patcher. Convention: <c>{root}/{version_key}/patcher/</c>.</summary>
    public static string? GetLocalPatcherDir(string versionKey)
    {
        var root = GetLocalReleasesRoot();
        if (root is null) return null;
        var dir = Path.Combine(root, versionKey, "patcher");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>Multi-line description of override state, for the
    /// Settings view. Shows env var values even when the path doesn't
    /// resolve, so the user can debug "I set DORKNET_LOCAL_RELEASES
    /// but it didn't work" without digging into File.Exists.</summary>
    public static string DescribeActive()
    {
        var parts = new List<string>();
        Describe(ManifestEnvVar, isDir: false, parts);
        Describe(ReleasesEnvVar, isDir: true, parts);
        return string.Join("\n", parts);
    }

    private static void Describe(string envVar, bool isDir, List<string> parts)
    {
        var raw = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrEmpty(raw)) return;
        var exists = isDir ? Directory.Exists(raw) : File.Exists(raw);
        parts.Add(exists
            ? $"{envVar} -> {raw}  [active]"
            : $"{envVar} -> {raw}  [SET BUT NOT FOUND on disk]");
    }
}
