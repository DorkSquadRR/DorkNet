using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Applies a DorkNet client patch to the user's Rec Room
/// install entirely in-process — no PowerShell scripts. The patcher
/// zip fetched from a per-version branch's release MUST contain a
/// <c>manifest.json</c> at root describing what to do; the launcher
/// reads it and performs each step in C#.
///
/// <para>DorkNet ships against <b>MelonLoader</b>. The manifest's
/// default paths target a MelonLoader install layout; per-version
/// branches can override any path explicitly.</para>
///
/// <para>Manifest schema (v1):
/// <code>
/// {
///   "$schema_version": 1,
///   "loader_archive": "MelonLoader.zip",                       // optional — unzipped into &lt;recroom-root&gt;\
///   "plugin_dll": "DorkNet.ClientMod.dll",                     // required — copied to plugin_dest
///   "plugin_dest": "Mods",                                     // optional — defaults to "Mods" (top-level, MelonLoader scans it)
///   "config_template": "dorknet-clientmod.json.template",      // optional — rendered with {HOST}/{PHOTON_APPID}/{PHOTON_VOICE_APPID}
///   "config_dest": "MelonLoader/UserData/dorknet-clientmod.json",  // optional — defaults shown
///   "old_plugin_paths": ["BepInEx/plugins/DorkNet.ClientPatch.dll"], // optional — deleted before install
///   "steam_stub": {                                            // optional — replaces Valve steam_api64.dll
///     "api_dll": "steam_api64.dll",                            //   file in zip
///     "api_dest": "Recroom_Release_Data/Plugins/steam_api64.dll",
///     "api_backup_suffix": ".steam-original",                  //   original is renamed to dll + suffix
///     "appid_file": "steam_appid.txt",                         //   file in zip (or omit if appid_value set)
///     "appid_dest": "steam_appid.txt"                          //   relative to Rec Room root
///   }
/// }
/// </code>
/// Config template placeholders: <c>{HOST}</c>, <c>{PHOTON_APPID}</c>,
/// <c>{PHOTON_VOICE_APPID}</c>, <c>{PHOTON_REGION}</c>. Anything else
/// passes through unchanged.</para></summary>
public sealed class ClientPatcher
{
    // MelonLoader 0.6.x scans <install root>\Mods\ for plugin DLLs —
    // NOT <install root>\MelonLoader\Mods\. UserData is correctly under
    // <install root>\MelonLoader\UserData\ though, since that's what
    // Mod.cs hardcodes when reading its config.
    private const string DefaultPluginDest = "Mods";
    private const string DefaultConfigDest = "MelonLoader/UserData/dorknet-clientmod.json";
    private static readonly string[] BuiltInOldPluginPaths =
    [
        "BepInEx/plugins/DorkNet.ClientPatch.dll",
        "MelonLoader/Mods/DorkNet.ClientMod.dll",
    ];

    public Task<PatchResult> ApplyAsync(
        string patcherDir,
        string recRoomDataPath,
        string photonAppId,
        string photonVoiceAppId,
        string photonRegion,
        string apexHost,
        bool singleOrigin = false,
        bool enableTlsTrustBypass = false,
        CancellationToken ct = default)
    {
        return Task.Run(() => ApplyCore(patcherDir, recRoomDataPath,
            photonAppId, photonVoiceAppId, photonRegion, apexHost, singleOrigin, enableTlsTrustBypass), ct);
    }

    private PatchResult ApplyCore(
        string patcherDir,
        string recRoomDataPath,
        string photonAppId,
        string photonVoiceAppId,
        string photonRegion,
        string apexHost,
        bool singleOrigin,
        bool enableTlsTrustBypass)
    {
        var log = new System.Text.StringBuilder();
        try
        {
            if (string.IsNullOrEmpty(recRoomDataPath))
                return PatchResult.Failure("Rec Room install path is empty.");
            var recRoomRoot = Path.GetDirectoryName(recRoomDataPath.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("Couldn't resolve parent dir of " + recRoomDataPath);
            if (!Directory.Exists(recRoomRoot))
                return PatchResult.Failure($"Rec Room root does not exist: {recRoomRoot}");

            var manifestPath = Path.Combine(patcherDir, "manifest.json");
            if (!File.Exists(manifestPath))
                return PatchResult.Failure(
                    $"Patcher zip is missing manifest.json — the per-version " +
                    $"branch's release is on an older PowerShell-based format. " +
                    $"Looked in: {patcherDir}");
            var manifest = JsonSerializer.Deserialize<PatcherManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null)
                return PatchResult.Failure("manifest.json parse failed.");

            log.AppendLine($"[patch] manifest schema v{manifest.SchemaVersion}");
            log.AppendLine($"[patch] target: {recRoomRoot}");

            // 1. Unpack MelonLoader (or whatever loader the branch chose).
            if (!string.IsNullOrEmpty(manifest.LoaderArchive))
            {
                var zipPath = Path.Combine(patcherDir, manifest.LoaderArchive);
                if (!File.Exists(zipPath))
                    return PatchResult.Failure($"Loader archive listed in manifest but missing on disk: {zipPath}");
                log.AppendLine($"[patch] extracting loader from {Path.GetFileName(zipPath)} → {recRoomRoot}");
                ExtractZipOverwrite(zipPath, recRoomRoot);
            }
            else
            {
                log.AppendLine("[patch] no loader archive in manifest — assuming user already has MelonLoader installed");
            }

            // 2. Remove stale plugin paths (e.g. a previous BepInEx-era DLL).
            foreach (var old in BuiltInOldPluginPaths
                         .Concat(manifest.OldPluginPaths ?? Array.Empty<string>())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var stale = Path.Combine(recRoomRoot, old.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(stale))
                {
                    File.Delete(stale);
                    log.AppendLine($"[patch] removed stale plugin: {old}");
                }
            }

            // 3. Copy the DorkNet plugin DLL.
            if (string.IsNullOrEmpty(manifest.PluginDll))
                return PatchResult.Failure("manifest.plugin_dll is required.");
            var dllSrc = Path.Combine(patcherDir, manifest.PluginDll);
            if (!File.Exists(dllSrc))
                return PatchResult.Failure($"plugin DLL listed in manifest but missing on disk: {dllSrc}");
            var pluginDestRel = NormalizePluginDest(manifest.PluginDest, log);
            var pluginDestDir = Path.Combine(recRoomRoot,
                pluginDestRel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(pluginDestDir);
            var dllDest = Path.Combine(pluginDestDir, Path.GetFileName(manifest.PluginDll));
            File.Copy(dllSrc, dllDest, overwrite: true);
            log.AppendLine($"[patch] installed plugin: {Path.GetFileName(manifest.PluginDll)} → {pluginDestDir}");

            // 4. Replace Valve's steam_api64.dll with the bundled stub
            //    (Goldberg emulator) so the unwrapped exe runs without
            //    Steam loaded. Backs up the original next to the dest
            //    DLL with the configured suffix so users can revert.
            if (manifest.SteamStub is { } stub && !string.IsNullOrEmpty(stub.ApiDll))
            {
                var stubSrc = Path.Combine(patcherDir, stub.ApiDll);
                if (!File.Exists(stubSrc))
                    return PatchResult.Failure($"steam_stub.api_dll listed but missing on disk: {stubSrc}");
                if (string.IsNullOrEmpty(stub.ApiDest))
                    return PatchResult.Failure("steam_stub.api_dest is required when steam_stub is set.");

                var dllTarget = Path.Combine(recRoomRoot, stub.ApiDest.Replace('/', Path.DirectorySeparatorChar));
                var dllTargetDir = Path.GetDirectoryName(dllTarget);
                if (!string.IsNullOrEmpty(dllTargetDir)) Directory.CreateDirectory(dllTargetDir);

                // Back up the original once. If a .steam-original already
                // exists we assume a previous patch ran — leave it alone
                // rather than clobber the actual Valve DLL with our stub.
                var backupSuffix = string.IsNullOrEmpty(stub.ApiBackupSuffix)
                    ? ".steam-original" : stub.ApiBackupSuffix;
                var backupPath = dllTarget + backupSuffix;
                if (File.Exists(dllTarget) && !File.Exists(backupPath))
                {
                    File.Move(dllTarget, backupPath);
                    log.AppendLine($"[patch] backed up Valve steam_api64.dll → {Path.GetFileName(backupPath)}");
                }
                File.Copy(stubSrc, dllTarget, overwrite: true);
                log.AppendLine($"[patch] installed Steam stub: {Path.GetFileName(dllTarget)}");

                // appid_dest is required; appid source can be a literal
                // value or a file in the zip — file wins if both set.
                if (!string.IsNullOrEmpty(stub.AppidDest))
                {
                    var appidTarget = Path.Combine(recRoomRoot,
                        stub.AppidDest.Replace('/', Path.DirectorySeparatorChar));
                    var appidTargetDir = Path.GetDirectoryName(appidTarget);
                    if (!string.IsNullOrEmpty(appidTargetDir)) Directory.CreateDirectory(appidTargetDir);

                    string appidContent;
                    if (!string.IsNullOrEmpty(stub.AppidFile))
                    {
                        var appidSrc = Path.Combine(patcherDir, stub.AppidFile);
                        if (!File.Exists(appidSrc))
                            return PatchResult.Failure($"steam_stub.appid_file listed but missing on disk: {appidSrc}");
                        appidContent = File.ReadAllText(appidSrc).Trim();
                    }
                    else if (!string.IsNullOrEmpty(stub.AppidValue))
                    {
                        appidContent = stub.AppidValue.Trim();
                    }
                    else
                    {
                        return PatchResult.Failure(
                            "steam_stub.appid_dest set without appid_file or appid_value to source the value from.");
                    }
                    File.WriteAllText(appidTarget, appidContent);
                    log.AppendLine($"[patch] wrote {Path.GetFileName(appidTarget)} ({appidContent})");
                }
            }

            // 5. Render the config template.
            if (!string.IsNullOrEmpty(manifest.ConfigTemplate))
            {
                var templatePath = Path.Combine(patcherDir, manifest.ConfigTemplate);
                if (!File.Exists(templatePath))
                    return PatchResult.Failure($"config template listed in manifest but missing: {templatePath}");

                var configDestRel = (manifest.ConfigDest ?? DefaultConfigDest)
                    .Replace('/', Path.DirectorySeparatorChar);
                var configDest = Path.Combine(recRoomRoot, configDestRel);
                var configDestDir = Path.GetDirectoryName(configDest);
                if (!string.IsNullOrEmpty(configDestDir)) Directory.CreateDirectory(configDestDir);

                var rendered = File.ReadAllText(templatePath)
                    .Replace("{HOST}", apexHost ?? "")
                    .Replace("{PHOTON_APPID}", photonAppId ?? "")
                    .Replace("{PHOTON_VOICE_APPID}",
                        string.IsNullOrEmpty(photonVoiceAppId) ? photonAppId ?? "" : photonVoiceAppId)
                    .Replace("{PHOTON_REGION}", string.IsNullOrEmpty(photonRegion) ? "us" : photonRegion)
                    .Replace("{SINGLE_ORIGIN_BASE_URL}", singleOrigin ? $"https://{apexHost}" : "")
                    .Replace("{ENABLE_TLS_TRUST_BYPASS}", enableTlsTrustBypass ? "true" : "false");
                File.WriteAllText(configDest, rendered);
                log.AppendLine($"[patch] wrote config: {Path.GetFileName(configDest)}");
            }

            PatchNamespaceBootstrap(recRoomRoot, log);

            log.AppendLine("[patch] done.");
            return PatchResult.Success(log.ToString());
        }
        catch (Exception ex)
        {
            log.AppendLine();
            log.AppendLine("[patch] FAILED: " + ex.Message);
            return PatchResult.Failure(log.ToString());
        }
    }

    private static string NormalizePluginDest(string? pluginDest, System.Text.StringBuilder log)
    {
        var dest = string.IsNullOrWhiteSpace(pluginDest) ? DefaultPluginDest : pluginDest;
        if (string.Equals(dest.Replace('\\', '/'), "MelonLoader/Mods", StringComparison.OrdinalIgnoreCase))
        {
            log.AppendLine("[patch] manifest uses legacy MelonLoader/Mods destination; installing to top-level Mods instead");
            return DefaultPluginDest;
        }

        return dest;
    }

    private static void PatchNamespaceBootstrap(string recRoomRoot, System.Text.StringBuilder log)
    {
        var metadata = Path.Combine(recRoomRoot,
            "Recroom_Release_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        if (!File.Exists(metadata))
        {
            log.AppendLine("[patch] namespace bootstrap skipped: global-metadata.dat not found");
            return;
        }

        var original = "https://ns.rec.net/?v=2"u8.ToArray();
        var patched = "http://127.0.0.1:0080/#"u8.ToArray();
        var legacyPatched = "http://127.0.0.1:5005/#"u8.ToArray();
        if (original.Length != patched.Length)
            throw new InvalidOperationException("namespace bootstrap patch length mismatch");
        if (legacyPatched.Length != patched.Length)
            throw new InvalidOperationException("legacy namespace bootstrap patch length mismatch");

        var bytes = File.ReadAllBytes(metadata);
        var patchOffset = IndexOf(bytes, patched);
        if (patchOffset >= 0)
        {
            log.AppendLine("[patch] namespace bootstrap already points at local server");
            return;
        }

        var offset = IndexOf(bytes, original);
        var wasLegacyPatch = false;
        if (offset < 0)
        {
            offset = IndexOf(bytes, legacyPatched);
            wasLegacyPatch = offset >= 0;
            if (offset < 0)
            {
                log.AppendLine("[patch] namespace bootstrap skipped: original ns.rec.net literal not found");
                return;
            }
        }

        var backup = metadata + ".dorknet-original";
        if (!File.Exists(backup) && !wasLegacyPatch)
            File.Copy(metadata, backup);

        Buffer.BlockCopy(patched, 0, bytes, offset, patched.Length);
        File.WriteAllBytes(metadata, bytes);
        log.AppendLine(wasLegacyPatch
            ? "[patch] namespace bootstrap: http://127.0.0.1:5005/# -> http://127.0.0.1:0080/#"
            : "[patch] namespace bootstrap: https://ns.rec.net/?v=2 -> http://127.0.0.1:0080/#");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                matched = false;
                break;
            }
            if (matched) return i;
        }
        return -1;
    }

    /// <summary>Extracts a zip on top of an existing directory, replacing
    /// any files that collide. <see cref="ZipFile.ExtractToDirectory"/>
    /// throws on existing files; we walk entries ourselves. Also guards
    /// against zip-slip path-traversal entries (file names that try to
    /// escape destRoot via <c>../</c>).</summary>
    private static void ExtractZipOverwrite(string zipPath, string destRoot)
    {
        var destFull = Path.GetFullPath(destRoot);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            var safeName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var fullDest = Path.GetFullPath(Path.Combine(destRoot, safeName));
            if (!fullDest.StartsWith(destFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && fullDest != destFull)
                throw new InvalidOperationException(
                    $"Refusing to extract '{entry.FullName}' — path escapes destination root.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullDest);
                continue;
            }
            var dir = Path.GetDirectoryName(fullDest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            entry.ExtractToFile(fullDest, overwrite: true);
        }
    }

    private sealed class PatcherManifest
    {
        [JsonPropertyName("$schema_version")] public int SchemaVersion { get; set; }
        [JsonPropertyName("loader_archive")] public string? LoaderArchive { get; set; }
        [JsonPropertyName("plugin_dll")] public string? PluginDll { get; set; }
        [JsonPropertyName("plugin_dest")] public string? PluginDest { get; set; }
        [JsonPropertyName("config_template")] public string? ConfigTemplate { get; set; }
        [JsonPropertyName("config_dest")] public string? ConfigDest { get; set; }
        [JsonPropertyName("old_plugin_paths")] public string[]? OldPluginPaths { get; set; }
        [JsonPropertyName("steam_stub")] public SteamStubBlock? SteamStub { get; set; }
    }

    private sealed class SteamStubBlock
    {
        [JsonPropertyName("api_dll")] public string? ApiDll { get; set; }
        [JsonPropertyName("api_dest")] public string? ApiDest { get; set; }
        [JsonPropertyName("api_backup_suffix")] public string? ApiBackupSuffix { get; set; }
        [JsonPropertyName("appid_file")] public string? AppidFile { get; set; }
        [JsonPropertyName("appid_value")] public string? AppidValue { get; set; }
        [JsonPropertyName("appid_dest")] public string? AppidDest { get; set; }
    }
}

public sealed record PatchResult(bool Ok, string Log)
{
    public static PatchResult Success(string log) => new(true, log);
    public static PatchResult Failure(string log) => new(false, log);
}
