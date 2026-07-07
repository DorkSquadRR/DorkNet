// DorkNet client mod (MelonLoader IL2CPP). Applies the minimum set of
// Harmony patches needed for the 2020 watch to connect to a DorkNet
// server:
//
//   1. URI rewrite      — `.rec.net` → user-configured host
//   2. TLS trust bypass — make BouncyCastle never reject server certs
//
// Plus the anti-cheat BYPASS patches (anti-tamper / ToxMod / file-hash)
// needed so a modded client isn't kicked, and two opt-in features that
// are OFF by default (Desktop Screen-Share FPS, RRO quest party size).
//
// This is the STABLE, shipping mod: it carries no diagnostic/tracing
// code. All of that lives in the separate DorkNet.DebugMod (drop it in
// the Mods folder to debug); the dev-menu / dev-debug-tools tooling
// lives outside the public repo entirely.
//
// Config lives in <MelonLoader>\UserData\dorknet-clientmod.json. The
// install-melon.ps1 wrapper writes that file on install; you can also
// edit it by hand and relaunch the client.
//
// Why string-based AccessTools lookups instead of typeof(Il2Cpp...):
// MelonLoader's Il2CppInterop adds an `Il2Cpp` prefix to game-side
// namespaces (so `RecNet.Core` becomes `Il2CppRecNet.Core`) but the
// exact policy for globally-namespaced types like `PhotonNetwork` and
// for nested types varies across MelonLoader minor versions. Looking
// types up by their original game-side name via AccessTools.TypeByName
// (which searches every loaded assembly) keeps this file working
// regardless of the prefix scheme, and avoids needing a verified
// Il2CppAssemblies directory just to write the source.

using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

[assembly: MelonInfo(typeof(DorkNet.ClientMod.Mod), "DorkNet ClientMod", "1.0.0", "Dork Squad")]
[assembly: MelonGame(null, null)]

namespace DorkNet.ClientMod;

public class Mod : MelonMod
{
    internal static MelonLogger.Instance Log => Instance!.LoggerInstance;
    internal static Mod? Instance;
    private static readonly Dictionary<string, Type> ResolvedTypeCache = new();
    private readonly HashSet<string> _diagnosticPatchLabels = new();
    private bool _networkPatchesRegistered;
    private bool _antiTamperCallbackPatchesComplete;
    private int _antiTamperCallbackRetryFrame;
    private bool _toxModPatchesComplete;
    private int _toxModRetryFrame;

    // Config backed by a plain JSON file under MelonLoader/UserData.
    // The defaults below match the recommended install, so dropping this
    // mod in without writing a config file still gives working behaviour.
    public static class Cfg
    {
        public static string ServerHost          = "localhost";
        public static bool   EnableTlsTrustBypass = true;

        // ── Desktop Screen Sharing gadget FPS ──
        // RecRoom.Tools.Productivity.DesktopScreenSharingDisplay broadcasts
        // the shared desktop image. Its cadence is a baked [SerializeField]
        // float screenShareImageRefreshFrequency — the broadcast tick gates
        // on (Time.time - lastSend >= 1f / frequency), verified at
        // DesktopScreenSharingDisplay.txt:697-703, so the field IS the FPS
        // and its editor-only [Range] is NOT enforced at runtime. Set a
        // target here to override it (default prefab value is low, ~5).
        // 0 = leave the prefab value alone (patch not even registered).
        public static float  DesktopScreenShareFps = 0f;
        // The captured frame ships via the gadget's IPunObservable
        // OnPhotonSerializeView, which only fires at
        // PhotonNetwork.SerializationRate (default 10 Hz) — so 30 fps capture
        // still delivers ~10 unless we raise it. This lifts SendRate +
        // SerializationRate to >= the FPS target. GLOBAL: affects every
        // PhotonView's sync traffic, so turn off if it lags a busy room.
        public static bool   DesktopScreenShareRaisePhotonRate = true;
        // Optional per-frame size knobs (0 = leave the prefab value). Higher
        // FPS needs smaller frames to fit the Photon serialization budget;
        // lower these if 30 fps saturates bandwidth (chunked frames stall).
        public static int    DesktopScreenShareResolution = 0; // horizontal px
        public static int    DesktopScreenShareQuality = 0;    // JPEG quality

        // Raise RRO quest party size past the baked 4. The cap lives in each
        // quest GameConfigurationAsset.TeamConfigurations[].MaxTeamSize
        // ScriptableObject (baked in the client bundle; no server lever). The
        // quest scoreboard + party HUD build rows dynamically, so they
        // auto-scale once the team cap is raised. 0 = off (leave at 4).
        // Only single-team, quest-named configs are touched, so PvP team
        // sizes (paintball/laser tag) are never changed. See QuestTeamSize.
        public static int    QuestMaxTeamSize = 0;
        // Only these RRO quest GameConfigurationAssets get their team size
        // bumped (matched loosely: case/space-insensitive, filler words like
        // "the/of/for/quest" ignored, so "IsleOfLostSkulls" matches the
        // in-game "Isle of the Lost Skulls"). Empty = bump nothing. The
        // generic "Quest" config is intentionally NOT in the default list.
        public static string[] QuestMaxTeamSizeRooms =
            { "CrimsonCauldron", "Crescendo", "GoldenTrophy", "IsleOfLostSkulls", "TheRiseofJumbotron" };
    }
    public override void OnInitializeMelon()
    {
        Instance = this;
        LoadConfig();
        Log.Msg($"DorkNet ClientMod loaded — host={Cfg.ServerHost}, tlsBypass={Cfg.EnableTlsTrustBypass}");
        RegisterNetworkPatches();
    }
    public override void OnLateInitializeMelon()
    {
        RegisterNetworkPatches();
        TryPatchByName("RecRoom.AntiCheat.EACManager",
                       "GenerateChallengeResponse",
                       args: new[] { typeof(string) },
                       prefix: nameof(AntiCheatPatches.GenerateChallengeResponse_Prefix));
        // TLS cert-validation bypass — gated behind Cfg.EnableTlsTrustBypass.
        // Even when the prefix's return value is "let original run", the
        // detour itself still has to be JIT-installed when BouncyCastle
        // does its first handshake, and MonoMod's CompileMethodHook
        // fatal-CLRs (0x80131506) installing trampolines into these
        // third-party interface implementations. Only register the
        // detours if the bypass is actually wanted; on a setup with
        // valid certs (the default) skip the patch entirely.
        if (Cfg.EnableTlsTrustBypass)
        {
            TryPatchByName("BestHTTP.SecureProtocol.Org.BouncyCastle.Crypto.Tls.ServerOnlyTlsAuthentication",
                           "NotifyServerCertificate",
                           prefix: nameof(TlsPatches.NotifyServerCertificate_Prefix));
            TryPatchByName("Org.BouncyCastle.Crypto.Tls.LegacyTlsAuthentication",
                           "NotifyServerCertificate",
                           prefix: nameof(TlsPatches.NotifyServerCertificate_Prefix));
        }
        // Anti-tamper / ToxMod / file-hash-checker are anti-cheat BYPASS
        // patches — core, always on, or the modded client gets kicked.
        RegisterAntiTamperCallbackPatches(logMisses: true);
        RegisterToxModPatches(logMisses: true);
        PatchFileHashCheckerCallback();
        // Desktop Screen Sharing FPS override. Only register when a target
        // is set; the postfix on the gadget's Awake rewrites the baked
        // refresh-frequency field (and, if enabled, raises the Photon
        // serialization rate so the higher capture rate actually transmits).
        if (Cfg.DesktopScreenShareFps > 0f)
        {
            TryPatchByName("RecRoom.Tools.Productivity.DesktopScreenSharingDisplay",
                           "Awake", args: Type.EmptyTypes,
                           postfix: nameof(ScreenSharePatches.DisplayAwake_Postfix));
        }

        // RRO quest party-size bump. Prefix the runtime config-apply path so
        // we can read the config's Name (scope to the allowlisted quests) and
        // bump GameConfigurationData.TeamConfigurations[].TeamSize before the
        // team manager / pre-game roster reads it. Only when a target is set.
        if (Cfg.QuestMaxTeamSize > 0)
        {
            // The runtime team cap is built from the baked GameConfigurationAsset
            // (a readable ScriptableObject) by GameConfigurationAsset.GDECPANDBHJ()
            // — it reads asset.TeamConfigurations[i].MaxTeamSize (a typed int
            // struct-array) into the protobuf TeamSize. Prefix that builder and
            // bump the source array IN PLACE before it converts, scoped by
            // asset.Name. This avoids the obfuscated protobuf entirely (its
            // generic RepeatedField getter isn't emitted by Il2CppInterop).
            // Host-authoritative: the quest host must run the mod. See QuestTeamSize.
            TryPatchByName("RecRoom.Core.GameManagement.GameConfigurationAsset", "GDECPANDBHJ",
                           prefix: nameof(QuestTeamSize.Build_Prefix));
            QuestTeamSize.TryPatchSpawnIndexNormalizers(HarmonyInstance);
            TryPatchByName("GameSpawnManager", "FKEICBPCMPJ",
                           postfix: nameof(QuestTeamSize.RelaxEmptySpawnFilter_Postfix));
        }

    }
    private bool PatchFileHashCheckerCallback()
    {
        var prefixMethod = GetPatchMethod(nameof(FileHashCheckerPatches.InitializeCallback_Prefix));
        var patched = 0;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in GetLoadableTypes(asm))
            {
                if (!MatchesFileHashCheckerCallbackType(type)) continue;
                foreach (var method in AccessTools.GetDeclaredMethods(type))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters) continue;
                    if (!MatchesFileHashCheckerCallbackMethod(method)) continue;
                    try
                    {
                        HarmonyInstance.Patch(method, prefix: new HarmonyMethod(prefixMethod));
                        patched++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[patch-fail] file hash checker callback {type.FullName}.{method.Name}: {ex.Message}");
                    }
                }
            }
        }

        if (patched == 0)
        {
            Log.Warning("[patch-miss] file hash checker callback");
            return false;
        }

        return true;
    }
    private void RegisterAntiTamperCallbackPatches(bool logMisses)
    {
        var complete = true;
        complete &= PatchCallbackMethod(
            "hile warning callback",
            type => TypeNameContains(type, "FPIBGPIAOBI") && TypeNameContains(type, "KHIIHOIFDMP"),
            method => MethodNameContains(method, "CreateHileWarning") && MethodNameContains(method, "b__0"),
            nameof(AntiTamperPatches.CreateHileWarningCallback_Prefix),
            logMisses);
        complete &= PatchCallbackMethod(
            "unknown DLL callback",
            type => TypeNameContains(type, "CheatManager") && TypeNameContains(type, "PNHDGEKCJCH"),
            method => MethodNameContains(method, "OnUnknownDLLDetected") && MethodNameContains(method, "b__0"),
            nameof(AntiTamperPatches.UnknownDllDetectedCallback_Prefix),
            logMisses);
        _antiTamperCallbackPatchesComplete = complete;
    }
    private void RegisterToxModPatches(bool logMisses)
    {
        var complete = true;
        complete &= TryPatchDiagnosticByName("ToxMod.ToxModVoiceComponent",
                                             "CanInitializeToxMod",
                                             args: Type.EmptyTypes,
                                             prefix: nameof(ToxModPatches.CanInitializeToxMod_Prefix),
                                             logMiss: logMisses);
        _toxModPatchesComplete = complete;
    }
    private bool PatchCallbackMethod(string label,
                                     Func<Type, bool> typePredicate,
                                     Func<MethodInfo, bool> methodPredicate,
                                     string prefix,
                                     bool logMiss)
    {
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        var prefixMethod = GetPatchMethod(prefix);
        var patched = 0;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in GetLoadableTypes(asm))
            {
                if (!typePredicate(type)) continue;
                foreach (var method in AccessTools.GetDeclaredMethods(type))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters) continue;
                    if (!methodPredicate(method)) continue;
                    try
                    {
                        HarmonyInstance.Patch(method, prefix: new HarmonyMethod(prefixMethod));
                        patched++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[patch-fail] {label} {type.FullName}.{method.Name}: {ex.Message}");
                    }
                }
            }
        }

        if (patched == 0)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}");
            return false;
        }

        _diagnosticPatchLabels.Add(label);
        return true;
    }
    private static bool TypeNameContains(Type type, string value)
    {
        var fullName = type.FullName ?? type.Name;
        return fullName.Contains(value, StringComparison.Ordinal);
    }
    private static bool MethodNameContains(MethodInfo method, string value)
        => method.Name.Contains(value, StringComparison.Ordinal);
    private static bool MatchesFileHashCheckerCallbackType(Type type)
    {
        var fullName = type.FullName ?? type.Name;
        return fullName.Contains("BEPEAHOOLDB", StringComparison.Ordinal)
            && fullName.Contains("PBENKLNHBHL", StringComparison.Ordinal);
    }
    private static bool MatchesFileHashCheckerCallbackMethod(MethodInfo method)
    {
        if (method.ReturnType != typeof(void)) return false;
        var parameters = method.GetParameters();
        if (parameters.Length != 1) return false;

        var name = method.Name;
        if (name == "<InitializeFileHashChecker>b__0") return true;
        if (name == "_InitializeFileHashChecker_b__0") return true;
        return name.Contains("InitializeFileHashChecker", StringComparison.Ordinal)
            && name.Contains("b__0", StringComparison.Ordinal);
    }
    private void RegisterNetworkPatches()
    {
        if (_networkPatchesRegistered) return;

        var complete = true;
        complete &= TryPatch("Uri ctor (string)", typeof(Uri), "ctor",
                 new[] { typeof(string) },
                 prefix: nameof(UriPatches.UriStringCtor_Prefix));
        complete &= TryPatch("Uri ctor (string, UriKind)", typeof(Uri), "ctor",
                 new[] { typeof(string), typeof(UriKind) },
                 prefix: nameof(UriPatches.UriStringCtor_Prefix));
        complete &= TryPatchByName("BestHTTP.HTTPRequest", "Send",
                 prefix: nameof(UriPatches.HttpRequestSend_Prefix));
        complete &= TryPatchHttpManagerStringSendRequestOverloads();
        complete &= TryPatchByName("BestHTTP.HTTPManager", "SendRequest",
                 args: new[] { ResolveType("BestHTTP.HTTPRequest") ?? typeof(object) },
                 prefix: nameof(UriPatches.HttpManagerSendRequestObject_Prefix));

        _networkPatchesRegistered = complete;
    }
    private bool TryPatchHttpManagerStringSendRequestOverloads()
    {
        var type = ResolveType("BestHTTP.HTTPManager");
        if (type is null)
        {
            Log.Warning("[patch-miss] BestHTTP.HTTPManager.SendRequest(string): type not found");
            return false;
        }

        var prefix = GetPatchMethod(nameof(UriPatches.HttpManagerSendRequestString_Prefix));
        var patched = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != "SendRequest") continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string)) continue;
            HarmonyInstance.Patch(method, prefix: new HarmonyMethod(prefix));
            patched++;
        }

        if (patched == 0)
        {
            Log.Warning("[patch-miss] BestHTTP.HTTPManager.SendRequest(string)");
            return false;
        }

        return true;
    }
    public override void OnUpdate()
    {
        if (!_antiTamperCallbackPatchesComplete && ++_antiTamperCallbackRetryFrame >= 300)
        {
            _antiTamperCallbackRetryFrame = 0;
            RegisterAntiTamperCallbackPatches(logMisses: false);
        }

        if (!_toxModPatchesComplete && ++_toxModRetryFrame >= 300)
        {
            _toxModRetryFrame = 0;
            RegisterToxModPatches(logMisses: false);
        }
    }
    public override void OnApplicationQuit()
    {
    }
    // ── Config ────────────────────────────────────────────────────────
    private static void LoadConfig()
    {
        try
        {
            var userData = ResolveUserDataDirectory();
            Directory.CreateDirectory(userData);

            var path = Path.Combine(userData, "dorknet-clientmod.json");
            if (!File.Exists(path))
            {
                return;
            }
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (TryGetConfigValue(r, "ServerHost", out var v))           Cfg.ServerHost = v.GetString() ?? Cfg.ServerHost;
            if (TryGetConfigValue(r, "EnableTlsTrustBypass", out v))     Cfg.EnableTlsTrustBypass = v.GetBoolean();
            if (TryGetConfigValue(r, "DesktopScreenShareFps", out v))             Cfg.DesktopScreenShareFps = (float)v.GetDouble();
            if (TryGetConfigValue(r, "DesktopScreenShareRaisePhotonRate", out v)) Cfg.DesktopScreenShareRaisePhotonRate = v.GetBoolean();
            if (TryGetConfigValue(r, "DesktopScreenShareResolution", out v))      Cfg.DesktopScreenShareResolution = v.GetInt32();
            if (TryGetConfigValue(r, "DesktopScreenShareQuality", out v))         Cfg.DesktopScreenShareQuality = v.GetInt32();
            if (TryGetConfigValue(r, "QuestMaxTeamSize", out v))                  Cfg.QuestMaxTeamSize = v.GetInt32();
            if (TryGetConfigValue(r, "QuestMaxTeamSizeRooms", out v) && v.ValueKind == JsonValueKind.Array)
            {
                var rooms = new List<string>();
                foreach (var e in v.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                        rooms.Add(e.GetString()!);
                Cfg.QuestMaxTeamSizeRooms = rooms.ToArray(); // explicit (incl. empty = none)
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[config] load failed, using defaults: {ex.Message}");
        }
    }
    private static string ResolveUserDataDirectory()
    {
        foreach (var typeName in new[]
        {
            "MelonLoader.MelonEnvironment, MelonLoader",
            "MelonLoader.MelonUtils, MelonLoader",
        })
        {
            var type = Type.GetType(typeName);
            var prop = type?.GetProperty("UserDataDirectory", BindingFlags.Public | BindingFlags.Static);
            if (prop?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        var modDll = Assembly.GetExecutingAssembly().Location;
        var modsDir = Path.GetDirectoryName(modDll) ?? string.Empty;
        var gameDir = Path.GetDirectoryName(modsDir) ?? AppContext.BaseDirectory;
        return Path.Combine(gameDir, "MelonLoader", "UserData");
    }
    private static bool TryGetConfigValue(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
    private bool TryPatch(string label, Type target, string methodName,
                          Type[]? args, string? prefix = null, string? postfix = null,
                          bool logMiss = true)
    {
        try
        {
            var method = methodName == "ctor"
                ? (MethodBase)target.GetConstructor(args ?? Type.EmptyTypes)!
                : (args is null ? AccessTools.Method(target, methodName)
                                : AccessTools.Method(target, methodName, args));
            if (method is null)
            {
                // `logMiss` is throttled by the retry-loop caller — once per
                // 5 seconds in OnUpdate, every time in the one-shot initial
                // RegisterDiagnostics. Without the gate the per-second retry
                // loop would spam `[patch-miss]` lines forever for any method
                // whose name drifted in a newer game build.
                if (logMiss) Log.Warning($"[patch-miss] {label}");
                return false;
            }
            HarmonyInstance.Patch(method,
                prefix:  prefix  is null ? null : new HarmonyMethod(GetPatchMethod(prefix)),
                postfix: postfix is null ? null : new HarmonyMethod(GetPatchMethod(postfix)));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[patch-fail] {label}: {ex.Message}");
            return false;
        }
    }
    private bool TryPatchByName(string typeName, string methodName,
                                Type[]? args = null, string? prefix = null, string? postfix = null,
                                bool logMiss = true)
    {
        var label = FormatPatchLabel(typeName, methodName, args);
        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}: type not found");
            return false;
        }
        return TryPatch(label, type, methodName, args, prefix, postfix, logMiss);
    }
    internal static Type? ResolveType(string name)
    {
        if (ResolvedTypeCache.TryGetValue(name, out var cached))
            return cached;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(name, throwOnError: false);
                if (t is not null) return CacheResolvedType(name, t);
                var dottedPrefixed = asm.GetType("Il2Cpp." + name, throwOnError: false);
                if (dottedPrefixed is not null) return CacheResolvedType(name, dottedPrefixed);
                var prefixed = asm.GetType("Il2Cpp" + name, throwOnError: false);
                if (prefixed is not null) return CacheResolvedType(name, prefixed);
                // For namespaced types like "RecNet.Core", also try with
                // the Il2Cpp prefix on the namespace (i.e. "Il2CppRecNet.Core").
                var dot = name.IndexOf('.');
                if (dot > 0)
                {
                    var nsPrefixed = asm.GetType("Il2Cpp" + name.Substring(0, dot) + name.Substring(dot), throwOnError: false);
                    if (nsPrefixed is not null) return CacheResolvedType(name, nsPrefixed);
                }
            }
            catch { /* unloaded / dynamic asm — skip */ }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var t in GetLoadableTypes(asm))
            {
                if (MatchesRequestedType(t, name))
                    return CacheResolvedType(name, t);
            }
        }
        return null;
    }
    private static Type CacheResolvedType(string name, Type type)
    {
        ResolvedTypeCache[name] = type;
        return type;
    }
    private static IEnumerable<Type> GetLoadableTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var types = new List<Type>();
            foreach (var t in ex.Types)
            {
                if (t is not null) types.Add(t);
            }
            return types;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
    private static bool MatchesRequestedType(Type type, string requested)
    {
        var prefixed = "Il2Cpp" + requested;
        var dottedPrefixed = "Il2Cpp." + requested;
        if (type.FullName == requested || type.FullName == prefixed || type.FullName == dottedPrefixed) return true;
        if (type.Name == requested || type.Name == prefixed) return true;
        return type.FullName?.EndsWith("." + requested, StringComparison.Ordinal) == true ||
               type.FullName?.EndsWith(".Il2Cpp." + requested, StringComparison.Ordinal) == true ||
               type.FullName?.EndsWith(".Il2Cpp" + requested, StringComparison.Ordinal) == true;
    }
    private static string FormatPatchLabel(string typeName, string methodName, Type[]? args)
    {
        if (args is null) return $"{typeName}.{methodName}";
        var names = new List<string>();
        foreach (var arg in args)
            names.Add(arg.Name);
        return $"{typeName}.{methodName}({string.Join(",", names)})";
    }
    private MethodInfo GetPatchMethod(string name)
    {
        // Look in the core + feature patch holder classes.
        foreach (var holder in new[] { typeof(UriPatches), typeof(AntiCheatPatches), typeof(AntiTamperPatches), typeof(FileHashCheckerPatches), typeof(ToxModPatches), typeof(TlsPatches), typeof(ScreenSharePatches), typeof(QuestTeamSize) })
        {
            var m = holder.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (m is not null) return m;
        }
        throw new MissingMethodException($"patch method '{name}' not found on any holder");
    }
    private bool TryPatchDiagnosticByName(string typeName, string methodName,
                                          Type[]? args = null, string? prefix = null, string? postfix = null,
                                          bool logMiss = true)
    {
        var label = FormatPatchLabel(typeName, methodName, args);
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        if (!TryPatchByName(typeName, methodName, args, prefix, postfix, logMiss))
            return false;

        _diagnosticPatchLabels.Add(label);
        return true;
    }
}

internal static class ScreenSharePatches
{
    private static bool _photonRateApplied;

    public static void DisplayAwake_Postfix(object __instance)
    {
        try
        {
            var fps = Mod.Cfg.DesktopScreenShareFps;
            if (fps <= 0f || __instance is null) return;
            var t = __instance.GetType();

            SetField(t, __instance, "screenShareImageRefreshFrequency", fps);
            if (Mod.Cfg.DesktopScreenShareResolution > 0)
                SetField(t, __instance, "screenShareImageHorizontalResolution", Mod.Cfg.DesktopScreenShareResolution);
            if (Mod.Cfg.DesktopScreenShareQuality > 0)
                SetField(t, __instance, "screenShareImageCompressionQuality", Mod.Cfg.DesktopScreenShareQuality);


            if (Mod.Cfg.DesktopScreenShareRaisePhotonRate && !_photonRateApplied)
                RaisePhotonRates((int)Math.Ceiling(fps));
        }
        catch (Exception ex) { Mod.Log.Warning($"[screenshare] Awake postfix failed: {ex.Message}"); }
    }

    private static void SetField(Type t, object inst, string name, object value)
    {
        var f = AccessTools.Field(t, name);
        if (f is null) { Mod.Log.Warning($"[screenshare] field '{name}' not found on {t.Name}"); return; }
        f.SetValue(inst, value);
    }

    // Lift PhotonNetwork.SendRate + SerializationRate to >= hz. PUN requires
    // SerializationRate <= SendRate, so SendRate is kept at the max of the
    // two. Both are public static int members (property on some PUN builds,
    // field on others) — handle either. GLOBAL: every PhotonView's
    // OnPhotonSerializeView now fires at this rate.
    private static void RaisePhotonRates(int hz)
    {
        var pn = Mod.ResolveType("PhotonNetwork");
        if (pn is null) { Mod.Log.Warning("[screenshare] PhotonNetwork not found; can't raise serialization rate (delivered FPS will stay ~10)"); return; }
        var send = ReadInt(pn, "SendRate");
        var ser  = ReadInt(pn, "SerializationRate");
        var target = Math.Max(hz, 1);
        var newSend = Math.Max(target, Math.Max(send, ser));
        WriteInt(pn, "SendRate", newSend);
        WriteInt(pn, "SerializationRate", target);
        _photonRateApplied = true;
    }

    private static int ReadInt(Type t, string name)
    {
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        if (p is not null && p.CanRead && p.GetValue(null) is int pv) return pv;
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
        if (f is not null && f.GetValue(null) is int fv) return fv;
        return 0;
    }

    private static void WriteInt(Type t, string name, int value)
    {
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        if (p is not null && p.CanWrite) { p.SetValue(null, value); return; }
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
        if (f is not null) f.SetValue(null, value);
    }
}
internal static class UriPatches
{
    public static void HttpRequestSetUri_Prefix(ref Uri __0)
    {
        RewriteUriArg(ref __0, "httprequest-seturi-rewrite");
    }

    public static void HttpRequestPrepareUri_Prefix(ref Uri __0)
    {
        RewriteUriArg(ref __0, "httprequest-prepareuri-rewrite");
    }

    public static void HttpManagerSendRequestString_Prefix(ref string url)
    {
        RewriteString(ref url, "httpmanager-rewrite");
    }

    public static void HttpManagerSendRequestObject_Prefix(object request)
    {
        RewriteRequestUri(request, "httpmanager-request-rewrite");
    }

    public static void UriStringCtor_Prefix(ref string uriString)
    {
        RewriteString(ref uriString, "uri-rewrite");
    }

    public static void HttpRequestSend_Prefix(object __instance)
    {
        RewriteRequestUri(__instance, "http-rewrite");
    }

    private static void RewriteRequestUri(object request, string label)
    {
        try
        {
            if (request is null) return;
            var type = request.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (TryRewriteUriProperty(request, type.GetProperty("Uri", flags), label)) return;
            if (TryRewriteUriField(request, type.GetField("<Uri>k__BackingField", flags), label)) return;

            foreach (var prop in type.GetProperties(flags))
            {
                if (prop.Name.IndexOf("Uri", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (TryRewriteUriProperty(request, prop, label)) return;
            }

            foreach (var field in type.GetFields(flags))
            {
                if (field.Name.IndexOf("Uri", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (TryRewriteUriField(request, field, label)) return;
            }
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[{label}] failed: {ex.Message}");
        }
    }

    private static void RewriteUriArg(ref Uri uri, string label)
    {
        if (uri is null) return;
        var rewritten = RewriteUri(uri);
        if (rewritten is null) return;

        uri = rewritten;
    }

    private static bool TryRewriteUriProperty(object request, PropertyInfo? prop, string label)
    {
        if (prop is null || !prop.CanRead || !prop.CanWrite) return false;
        if (prop.GetIndexParameters().Length != 0) return false;
        if (prop.GetValue(request) is not Uri uri) return false;

        var rewritten = RewriteUri(uri);
        if (rewritten is null) return false;

        prop.SetValue(request, rewritten);
        return true;
    }

    private static bool TryRewriteUriField(object request, FieldInfo? field, string label)
    {
        if (field is null || field.IsInitOnly) return false;
        if (field.GetValue(request) is not Uri uri) return false;

        var rewritten = RewriteUri(uri);
        if (rewritten is null) return false;

        field.SetValue(request, rewritten);
        return true;
    }

    private static void RewriteString(ref string uriString, string label)
    {
        if (string.IsNullOrEmpty(uriString)) return;
        if (uriString.IndexOf(".rec.net", StringComparison.OrdinalIgnoreCase) < 0) return;
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) return;

        var rewrittenUri = RewriteUri(uri);
        if (rewrittenUri is null) return;

        var rewritten = rewrittenUri.ToString();
        if (rewritten == uriString) return;
        uriString = rewritten;
    }

    private static Uri? RewriteUri(Uri uri)
    {
        var host = Mod.Cfg.ServerHost;
        if (string.IsNullOrEmpty(host)) return null;
        if (!uri.Host.EndsWith(".rec.net", StringComparison.OrdinalIgnoreCase)) return null;

        var service = uri.Host[..^".rec.net".Length];
        if (string.IsNullOrWhiteSpace(service)) return null;

        return new UriBuilder(uri)
        {
            Host = service + "." + host,
        }.Uri;
    }
}
internal static class AntiCheatPatches
{
    public static bool GenerateChallengeResponse_Prefix(string FIMBPCACMKJ, ref string __result)
    {
        __result = "AAAAAAAAAAAAAAAAAAAAAAAA";
        return false;
    }
}
internal static class AntiTamperPatches
{
    public static bool CreateHileWarningCallback_Prefix(bool shouldQuit)
    {
        return false;
    }

    public static bool UnknownDllDetectedCallback_Prefix()
    {
        return false;
    }
}
internal static class FileHashCheckerPatches
{
    public static bool InitializeCallback_Prefix(object? result)
    {
        return false;
    }
}
internal static class ToxModPatches
{
    public static bool CanInitializeToxMod_Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }

}
internal static class TlsPatches
{
    // Both BouncyCastle TLS authenticators have the same `void
    // NotifyServerCertificate(...)` signature whose body throws when the
    // chain doesn't validate. Returning false from a Harmony prefix on a
    // void method skips the original — the certificate is accepted by
    // virtue of NotifyServerCertificate never throwing.
    public static bool NotifyServerCertificate_Prefix()
    {
        return !Mod.Cfg.EnableTlsTrustBypass; // false skips original; true (bypass off) lets it run
    }
}
