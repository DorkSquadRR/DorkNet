// MelonLoader port of the DorkNet.ClientPatch BepInEx plugin. Applies
// the minimum set of Harmony patches needed for the 2020 watch to
// connect to a DorkNet server:
//
//   1. URI rewrite      — `.rec.net` → user-configured host
//   2. Photon AppId     — override the baked-in Photon Cloud AppId
//   3. AuthValues       — inject `userid` + `LoginLock` for /photon/customauth
//   4. TLS trust bypass — make BouncyCastle never reject server certs
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
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

[assembly: MelonInfo(typeof(DorkNet.ClientMod.Mod), "DorkNet ClientMod", "1.0.0", "Dork Squad")]
[assembly: MelonGame(null, null)]

namespace DorkNet.ClientMod;

public class Mod : MelonMod
{
    internal static MelonLogger.Instance Log => Instance!.LoggerInstance;
    internal static Mod? Instance;
    internal static string? DiagnosticsLogPath;
    private static readonly Dictionary<string, Type> ResolvedTypeCache = new();
    private readonly HashSet<string> _diagnosticPatchLabels = new();
    private bool _networkPatchesRegistered;
    private bool _diagnosticCoreRegistered;
    private bool _diagnosticGameComplete;
    private int _diagnosticRetryFrame;
    private int _photonOverridePollFrame;

    // Loader-agnostic config — same .Value-shaped accessor the patches
    // would see in the BepInEx port, just backed by a plain JSON file
    // instead of BepInEx's TOML config system. Defaults match the BepInEx
    // plugin's defaults so dropping this mod in without writing a config
    // file gives the same behaviour as the recommended install.
    public static class Cfg
    {
        public static string ServerHost          = "localhost";
        public static string SingleOriginBaseUrl = "";
        public static string PhotonAppId         = "";
        public static string PhotonVoiceAppId    = "";
        // Forced PhotonServerSettings.PreferredRegion (with HostType
        // switched to PhotonCloud) so two watches don't auto-ping
        // themselves into different "best" regions and end up in
        // parallel Photon rooms sharing only a name. PhotonPatches
        // .ApplyForcedRegion prefers the server-told region from
        // Matchmaking.LocalRoomInstance.PhotonRegionId when available
        // and falls back to this config value on initial boot before
        // any /goto has returned. Default matches the server's
        // appsettings.json Photon:CloudRegion ("eu").
        public static string PhotonCloudRegion   = "eu";
        public static bool   EnableTlsTrustBypass = true;
        // Photon Custom Auth injector was parked 2026-05-28; see
        // attic/AuthValuesInjector.cs.attic for the code + restore notes.
    }

    public override void OnInitializeMelon()
    {
        Instance = this;
        Log.Msg("=== DorkNet ClientMod loading ===");
        LoadConfig();
        RegisterNetworkPatches();
        DiagnosticPatches.Write("[lifecycle] OnInitializeMelon");
    }

    public override void OnLateInitializeMelon()
    {
        DiagnosticPatches.Write("[lifecycle] OnLateInitializeMelon");
        Log.Msg("=== Registering client patches ===");
        RegisterNetworkPatches();
        TryPatchByName("PhotonNetwork",       "ConnectUsingSettings",
                       prefix: nameof(PhotonPatches.PhotonAppIdOverride_Prefix));
        // NetworkingPeer.CallAuthenticate hook (Photon Custom Auth
        // injector) parked — see attic/AuthValuesInjector.cs.attic.
        TryPatchByName("BestHTTP.SecureProtocol.Org.BouncyCastle.Crypto.Tls.ServerOnlyTlsAuthentication",
                       "NotifyServerCertificate",
                       prefix: nameof(TlsPatches.NotifyServerCertificate_Prefix));
        TryPatchByName("Org.BouncyCastle.Crypto.Tls.LegacyTlsAuthentication",
                       "NotifyServerCertificate",
                       prefix: nameof(TlsPatches.NotifyServerCertificate_Prefix));
        // Post-save blob-name refresh. The 2020 watch's
        // RoomPersistenceManager.RoomDataBlobName is only ever set by
        // /goto-driven flows — neither the SubscriptionUpdateRoom push
        // nor SubscriptionUpdatePresence pushes the server fires after
        // save propagate into this field. So the PostSaveReloading
        // sub-state's MasterReloadRoomAsync deserializes the cached
        // bytes from the OLD blob name (the one /goto returned at boot),
        // wiping any MakerPen object the user just placed even though
        // the new blob is already on S3.
        //
        // Verified live: after a dorm save with object placement, server
        // pushes SubscriptionUpdateRoom + PresenceUpdate both carrying
        // dataBlob="dorm_p<id>_v(N+1).dat", but the watch's
        // set_RoomDataBlobName trace immediately after the save shows
        // old="v(N)" new="v(N)" — no propagation, no fresh download,
        // no respawn. Leave+return triggers /goto which DOES update
        // RoomDataBlobName, so the fix is to do the same write
        // ourselves: when entering the SavingRoomSuccess state on the
        // RoomPersistenceManager, read the just-pushed
        // Rooms.LocalRoomScene.DataBlobName and force-set
        // __instance.RoomDataBlobName to it. That fires the real
        // setter → OnRoomDataBlobNameChanged → DownloadRoomDataBlobAsync
        // for the new blob → PostSaveReloading picks up the fresh
        // bytes and respawns the MakerPen objects.
        TryPatchByName("RecRoom.Persistence.RoomPersistenceManager",
                       "set_RoomDataBlobName",
                       args: new[] { typeof(string) },
                       prefix: nameof(SavePatches.SetRoomDataBlobName_Prefix));
        // Intercept the SubscriptionUpdateRoom push BEFORE the watch's
        // (cachedRoomId, cachedSubRoomId) early-out gate drops it. We
        // stash the just-arrived Scenes[0].DataBlobName into a static
        // so the set_RoomDataBlobName hijack can use it — otherwise
        // LocalRoomDetails / LocalRoomScene never get the new blob
        // name on the second-or-later push for the same (room, sub)
        // tuple (= every post-save push in a dorm).
        TryPatchByName("RecNet.Rooms",
                       "OnSubscriptionUpdateRoom",
                       prefix: nameof(SavePatches.OnSubscriptionUpdateRoom_Prefix));
        // Force AccountExtensions.CanLocalPlayerChat to return true. The
        // 2020 watch's room-chat send path (PlayerEmotes.SendChatEmote
        // Coroutine d__53 offset 040) gates on this — if false, the
        // coroutine bails before raising the Photon RPC, so the sender
        // doesn't even see their own message. The check ANDs three
        // signals: LocalAccount.TreatAsJunior (ObscuredBool at offset
        // 0x40), a PlatformManager flag, and a ModerationManager
        // singleton field. At least one returns the wrong value against
        // our server. Force-allow — single-tenant private server, text
        // moderation isn't a concern.
        TryPatchByName("RecNet.AccountExtensions",
                       "CanLocalPlayerChat",
                       args: Type.EmptyTypes,
                       postfix: nameof(ChatPatches.CanLocalPlayerChat_Postfix));
        // PlayerEmotes.RemoveInvalidCharactersFromMessage walks the chat
        // font asset's character table via TMP_FontAsset.HasCharacters
        // to strip glyphs the font can't render. On 2020 Rec Room the
        // chat font field can be null at the moment this fires (font
        // asset bundle hasn't loaded yet?) and HasCharacters NREs deep
        // inside Unity's TMP — which propagates out through the chat
        // send callback and aborts the RPC. The watch's own ISIL has
        // "skipping invalid character check" log paths for missing
        // assets but doesn't actually short-circuit on the null field
        // we hit. Skip the method entirely; chat lines will just keep
        // any glyphs the font can't render (most common: emoji).
        TryPatchByName("RecRoom.Players.PlayerEmotes",
                       "RemoveInvalidCharactersFromMessage",
                       prefix: nameof(ChatPatches.RemoveInvalidCharactersFromMessage_Prefix));
        // Trace hooks so we can see exactly where chat dies. Send path:
        //  KeyboardInputField.submit → RoomChatMenu.SendEmoteMessage(string)
        //  → PlayerEmotes.SendChatEmote(msg, true)
        //  → SendChatEmoteCoroutine d__53 → PurifyString HTTP call
        //  → b__0 callback (error, cleanVersion) — fires Photon RPC
        //    AND local-echoes via ProcessNewChatMessageReceived
        //  → on each receiver (including self): RpcChatEmote(msg) →
        //    ProcessNewChatMessageReceived → ReceiveRoomChat appends to
        //    the chat log buffer.
        TryPatchByName("RecRoom.Players.PlayerEmotes",
                       "SendChatEmote",
                       args: new[] { typeof(string), typeof(bool) },
                       prefix: nameof(ChatPatches.SendChatEmote_Prefix));
        TryPatchByName("RecRoom.Players.PlayerEmotes",
                       "ProcessNewChatMessageReceived",
                       args: new[] { typeof(string), typeof(bool) },
                       prefix: nameof(ChatPatches.ProcessNewChatMessageReceived_Prefix));
        TryPatchByName("RecRoom.Players.PlayerEmotes",
                       "RpcChatEmote",
                       args: new[] { typeof(string) },
                       prefix: nameof(ChatPatches.RpcChatEmote_Prefix));
        RegisterDiagnostics();
        Log.Msg("=== Client patches registered ===");
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

        Log.Msg($"[patch-ok] BestHTTP.HTTPManager.SendRequest(string) overloads x{patched}");
        return true;
    }

    public override void OnUpdate()
    {
        // Photon AppId override poller. The harmony hook on
        // PhotonNetwork.ConnectUsingSettings registers in IL2CPP but
        // doesn't actually fire in this build (verified in the
        // MelonLoader log — [patch-ok] appears but [photon-appid]
        // never does), so we poll until PhotonServerSettings is loaded
        // and write the AppId then. Stops calling once the override
        // succeeds. Throttled to every 6th frame (~10 Hz at 60 fps) so
        // the failure log doesn't drown the file when PhotonServerSettings
        // isn't loaded yet.
        if (!PhotonPatches.OverrideApplied && (_photonOverridePollFrame++ % 6) == 0)
            PhotonPatches.TryApplyOverride(reason: "update-poll");

        if (_diagnosticGameComplete) return;
        if (_diagnosticRetryFrame++ > 3600) return;
        if ((_diagnosticRetryFrame % 60) != 0) return;

        var logMisses = (_diagnosticRetryFrame % 300) == 0;
        _diagnosticGameComplete = RegisterGameDiagnostics(logMisses);
        if (_diagnosticGameComplete)
            DiagnosticPatches.Write("[diagnostics] all game-side diagnostic hooks registered");
    }

    public override void OnApplicationQuit()
    {
        DiagnosticPatches.Write("[lifecycle] OnApplicationQuit");
    }

    // ── Config ────────────────────────────────────────────────────────
    private static void LoadConfig()
    {
        try
        {
            // Compute the UserData path ourselves rather than calling
            // MelonLoader.MelonUtils.UserDataDirectory (which is renamed
            // to MelonEnvironment.UserDataDirectory in newer 0.6.x builds
            // and shifts again in 0.7.x). Anchoring off the mod DLL's
            // location keeps this stable across versions: the DLL lives
            // in <game>/Mods/, so two dirname()'s reach the game root,
            // then MelonLoader/UserData/ is right next to it.
            var modDll = Assembly.GetExecutingAssembly().Location;
            var modsDir = Path.GetDirectoryName(modDll) ?? string.Empty;
            var gameDir = Path.GetDirectoryName(modsDir) ?? string.Empty;
            var userData = Path.Combine(gameDir, "MelonLoader", "UserData");
            Directory.CreateDirectory(userData);
            DiagnosticsLogPath = Path.Combine(userData, "dorknet-diagnostics.log");
            var path = Path.Combine(userData, "dorknet-clientmod.json");
            if (!File.Exists(path))
            {
                Log.Msg($"[config] no file at {path}; using defaults (ServerHost={Cfg.ServerHost})");
                return;
            }
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (r.TryGetProperty("ServerHost", out var v))           Cfg.ServerHost = v.GetString() ?? Cfg.ServerHost;
            if (r.TryGetProperty("SingleOriginBaseUrl", out v))      Cfg.SingleOriginBaseUrl = (v.GetString() ?? "").TrimEnd('/');
            if (r.TryGetProperty("PhotonAppId", out v))              Cfg.PhotonAppId = v.GetString() ?? Cfg.PhotonAppId;
            if (r.TryGetProperty("PhotonVoiceAppId", out v))         Cfg.PhotonVoiceAppId = v.GetString() ?? Cfg.PhotonVoiceAppId;
            if (r.TryGetProperty("PhotonCloudRegion", out v))        Cfg.PhotonCloudRegion = v.GetString() ?? Cfg.PhotonCloudRegion;
            if (r.TryGetProperty("EnableTlsTrustBypass", out v))     Cfg.EnableTlsTrustBypass = v.GetBoolean();
            // "InjectAuthValues" key in the template is now ignored —
            // see attic/AuthValuesInjector.cs.attic.
            Log.Msg($"[config] loaded: ServerHost={Cfg.ServerHost}, SingleOrigin={(string.IsNullOrEmpty(Cfg.SingleOriginBaseUrl) ? "<off>" : Cfg.SingleOriginBaseUrl)}, PhotonAppId={(string.IsNullOrEmpty(Cfg.PhotonAppId) ? "<unset>" : "<set>")}, " +
                    $"EnableTlsTrustBypass={Cfg.EnableTlsTrustBypass}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[config] load failed, using defaults: {ex.Message}");
        }
    }

    // ── Patch dispatch helpers ────────────────────────────────────────
    private bool TryPatch(string label, Type target, string methodName,
                          Type[]? args, string? prefix = null, string? postfix = null)
    {
        try
        {
            var method = methodName == "ctor"
                ? (MethodBase)target.GetConstructor(args ?? Type.EmptyTypes)!
                : (args is null ? AccessTools.Method(target, methodName)
                                : AccessTools.Method(target, methodName, args));
            if (method is null)
            {
                Log.Warning($"[patch-miss] {label}");
                return false;
            }
            HarmonyInstance.Patch(method,
                prefix:  prefix  is null ? null : new HarmonyMethod(GetPatchMethod(prefix)),
                postfix: postfix is null ? null : new HarmonyMethod(GetPatchMethod(postfix)));
            Log.Msg($"[patch-ok] {label}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[patch-fail] {label}: {ex.Message}");
            return false;
        }
    }

    // Resolve the game-side type by its ORIGINAL name (no Il2Cpp prefix)
    // by walking every loaded assembly. Tries the exact name first, then
    // the same name with an `Il2Cpp` prefix, then a substring scan as a
    // last resort — covers all the IL2CPP interop naming schemes the user
    // might end up with depending on the MelonLoader / Il2CppInterop
    // version installed.
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
        return TryPatch(label, type, methodName, args, prefix, postfix);
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
        if (type.FullName == requested || type.FullName == prefixed) return true;
        if (type.Name == requested || type.Name == prefixed) return true;
        return type.FullName?.EndsWith("." + requested, StringComparison.Ordinal) == true ||
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
        // Look in all three patch holder classes — small enough that a
        // linear scan is cheaper than per-class lookups.
        foreach (var holder in new[] { typeof(UriPatches), typeof(PhotonPatches), typeof(TlsPatches), typeof(DiagnosticPatches), typeof(SavePatches), typeof(ChatPatches) })
        {
            var m = holder.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (m is not null) return m;
        }
        throw new MissingMethodException($"patch method '{name}' not found on any holder");
    }

    private void RegisterDiagnostics()
    {
        Log.Msg("=== Registering diagnostics ===");
        if (!_diagnosticCoreRegistered)
        {
            TryPatchByName("UnityEngine.Application", "Quit", Type.EmptyTypes,
                           prefix: nameof(DiagnosticPatches.ApplicationQuit_Prefix));
            TryPatchByName("UnityEngine.Application", "Quit", new[] { typeof(int) },
                           prefix: nameof(DiagnosticPatches.ApplicationQuitInt_Prefix));
            _diagnosticCoreRegistered = true;
        }

        _diagnosticGameComplete = RegisterGameDiagnostics(logMisses: true);
        Log.Msg("=== Diagnostics registered ===");
    }

    private bool RegisterGameDiagnostics(bool logMisses)
    {
        var complete = true;
        complete &= TryPatchDiagnosticByName("BootSequence", "RegisterError", new[] { typeof(string) },
                                            prefix: nameof(DiagnosticPatches.BootSequenceRegisterError_Prefix),
                                            logMiss: logMisses);

        foreach (var method in new[]
        {
            "UnityTimeCheat",
            "ObscuredTypeCheat",
            "DeveloperFlagCheat",
            "HeightChangeCheat",
            "AdvancedMotionCheat",
            "StreamingAssetCheat"
        })
        {
            complete &= TryPatchDiagnosticByName("AnalyticsHelper", method,
                                                 prefix: nameof(DiagnosticPatches.AnalyticsCheat_Prefix),
                                                 logMiss: logMisses);
        }

        complete &= TryPatchDiagnosticByName("SessionManager", "TryApplicationQuit",
                                             prefix: nameof(DiagnosticPatches.SessionManagerTryApplicationQuit_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("SteamManager", "Awake",
                                             prefix: nameof(DiagnosticPatches.NamedMethod_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("SteamPlatformManager", "PreLoginInitialize",
                                             prefix: nameof(DiagnosticPatches.NamedMethod_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("SteamPlatformManager", "PostLoginInitialize",
                                             prefix: nameof(DiagnosticPatches.NamedMethod_Prefix),
                                             logMiss: logMisses);
        return complete;
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

// ── Patch holders ─────────────────────────────────────────────────────
// Each holder is a static class with public static Prefix methods that
// Harmony invokes. Kept separate from Mod for readability and so the
// reflection lookup in GetPatchMethod stays simple.

internal static class UriPatches
{
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
            var uriProp = request.GetType().GetProperty("Uri", BindingFlags.Public | BindingFlags.Instance);
            if (uriProp?.GetValue(request) is not Uri uri) return;

            var rewritten = RewriteUri(uri);
            if (rewritten is null) return;

            uriProp.SetValue(request, rewritten);
            Mod.Log.Msg($"[{label}] {uri} → {rewritten}");
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[{label}] failed: {ex.Message}");
        }
    }

    private static void RewriteString(ref string uriString, string label)
    {
        if (string.IsNullOrEmpty(uriString)) return;
        if (uriString.IndexOf(".rec.net", StringComparison.OrdinalIgnoreCase) < 0) return;

        if (!string.IsNullOrEmpty(Mod.Cfg.SingleOriginBaseUrl) &&
            Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            var rewrittenUri = RewriteUri(uri);
            if (rewrittenUri is null) return;
            var rewrittenText = rewrittenUri.ToString();
            if (rewrittenText == uriString) return;
            Mod.Log.Msg($"[{label}] {uriString} → {rewrittenText}");
            uriString = rewrittenText;
            return;
        }

        var host = Mod.Cfg.ServerHost;
        if (string.IsNullOrEmpty(host)) return;
        var rewritten = uriString.Replace(".rec.net", "." + host);
        if (rewritten == uriString) return;
        Mod.Log.Msg($"[{label}] {uriString} → {rewritten}");
        uriString = rewritten;
    }

    private static Uri? RewriteUri(Uri uri)
    {
        var host = Mod.Cfg.ServerHost;
        if (string.IsNullOrEmpty(host)) return null;
        if (!uri.Host.EndsWith(".rec.net", StringComparison.OrdinalIgnoreCase)) return null;

        var service = uri.Host[..^".rec.net".Length];
        if (string.IsNullOrWhiteSpace(service)) return null;

        if (!string.IsNullOrEmpty(Mod.Cfg.SingleOriginBaseUrl) &&
            Uri.TryCreate(Mod.Cfg.SingleOriginBaseUrl, UriKind.Absolute, out var baseUri))
        {
            var path = CombinePath(baseUri.AbsolutePath, "__dn/" + service, uri.AbsolutePath);
            return new UriBuilder(baseUri)
            {
                Path = path,
                Query = uri.Query.TrimStart('?'),
            }.Uri;
        }

        return new UriBuilder(uri)
        {
            Host = service + "." + host,
        }.Uri;
    }

    private static string CombinePath(params string[] parts)
    {
        var cleaned = new List<string>();
        foreach (var part in parts)
        {
            var p = (part ?? "").Trim('/');
            if (p.Length > 0) cleaned.Add(p);
        }
        return "/" + string.Join("/", cleaned);
    }
}

internal static class PhotonPatches
{
    /// <summary>True once <see cref="TryApplyOverride"/> has successfully
    /// written the user's AppId into <c>PhotonServerSettings</c>. The
    /// per-frame poller in <see cref="Mod.OnUpdate"/> stops calling once
    /// this flips true.</summary>
    public static bool OverrideApplied;

    public static void PhotonAppIdOverride_Prefix()
    {
        // Kept as a belt-and-braces hook in case the watch's actual
        // connect path does happen to flow through ConnectUsingSettings
        // (it doesn't, in March's IL2CPP build — verified via log). The
        // poller in OnUpdate is what actually gets the override in
        // before the wire connect.
        TryApplyOverride(reason: "prefix");
    }

    /// <summary>Tracks how many polling attempts we've logged so the
    /// "type not found yet" line doesn't drown the log file. Cleared
    /// once the override succeeds.</summary>
    private static int _missLogCount;

    /// <summary>Mutates <see cref="PhotonServerSettings.AppID"/> +
    /// <c>VoiceAppID</c> + region with the values from
    /// <see cref="Mod.Cfg"/>. Returns true once the override has been
    /// written successfully; subsequent calls become no-ops. Returns
    /// false (and logs nothing on most retries) when PhotonServerSettings
    /// hasn't been loaded by the watch yet — the poller retries every
    /// frame until the ScriptableObject shows up.</summary>
    public static bool TryApplyOverride(string reason)
    {
        if (OverrideApplied) return true;
        if (string.IsNullOrEmpty(Mod.Cfg.PhotonAppId)) return false;
        try
        {
            // Use Mod.ResolveType (the same resolver TryPatchByName uses)
            // — it falls back to a full assembly scan after the prefix
            // attempts, which is how it finds the IL2CPP-mangled types
            // that the local FindGameType helper misses.
            var photonNetwork = Mod.ResolveType("PhotonNetwork");
            if (photonNetwork is null)
            {
                // Log the miss only on the first call + once every ~10s
                // after that, to avoid the log spam previous build hit.
                if (_missLogCount == 0 || _missLogCount % 300 == 0)
                    Mod.Log.Msg($"[photon-appid] PhotonNetwork type not found yet (#{_missLogCount}, {reason})");
                _missLogCount++;
                return false;
            }
            var settingsProp = photonNetwork.GetProperty("PhotonServerSettings", BindingFlags.Public | BindingFlags.Static);
            var settings = settingsProp?.GetValue(null);
            if (settings is null) return false; // ScriptableObject not loaded yet; quietly retry next frame.

            var settingsType = settings.GetType();
            var appIdProp = settingsType.GetProperty("AppID") ?? settingsType.GetProperty("AppId");
            var voiceProp = settingsType.GetProperty("VoiceAppID") ?? settingsType.GetProperty("VoiceAppId");

            var newId = Mod.Cfg.PhotonAppId;
            var newVoice = string.IsNullOrEmpty(Mod.Cfg.PhotonVoiceAppId) ? newId : Mod.Cfg.PhotonVoiceAppId;

            appIdProp?.SetValue(settings, newId);
            voiceProp?.SetValue(settings, newVoice);
            Mod.Log.Msg($"[photon-appid] override AppID/VoiceAppID → {newId} / {newVoice} ({reason})");

            // Force a deterministic Photon region so two watches running
            // this mod end up on the same Photon master and "JoinByName"
            // produces ONE shared room (not two parallel ones in eu/us
            // because each watch auto-picked a different best region).
            //
            // The 2020 watch's default HostType is BestRegion(4) — every
            // launch pings all Photon Cloud regions and picks the lowest
            // latency. Two players geographically apart hit different
            // "best" regions; same room name, two regions = two rooms.
            // Server-side our matchmaking response carries
            // `photonRegionId` but the watch's Photon code never reads
            // it; the only consumer is Matchmaking.SetPhotonRegionPings
            // which only reports pings *back* to the server.
            //
            // Strategy: switch HostType to PhotonCloud(1) and set
            // PreferredRegion. Prefer the server-told region (the most
            // recent Matchmaking.LocalRoomInstance.PhotonRegionId, if
            // any) over the config default — that way a server-side
            // region change propagates without redeploying the mod.
            ApplyForcedRegion(settings, settingsType);
            OverrideApplied = true;
            return true;
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[photon-appid] override failed ({reason}): {ex.Message}");
            return false;
        }
    }

    /// <summary>Forces <c>PhotonServerSettings.HostType = PhotonCloud</c>
    /// and writes <c>PreferredRegion</c>. Honors any region the server
    /// has handed us in <c>Matchmaking.LocalRoomInstance.PhotonRegionId</c>
    /// first; falls back to <c>Cfg.PhotonCloudRegion</c> when no room
    /// instance has been received yet (initial boot).</summary>
    private static void ApplyForcedRegion(object settings, Type settingsType)
    {
        try
        {
            var hostTypeProp = settingsType.GetField("HostType")
                ?? (System.Reflection.MemberInfo?)settingsType.GetProperty("HostType");
            var preferredProp = settingsType.GetField("PreferredRegion")
                ?? (System.Reflection.MemberInfo?)settingsType.GetProperty("PreferredRegion");
            if (hostTypeProp is null || preferredProp is null)
            {
                Mod.Log.Warning("[photon-region] PhotonServerSettings.HostType/PreferredRegion not found");
                return;
            }

            // ServerSettings.HostingOption.PhotonCloud = 1 (verified in
            // Cpp2IL_CS/.../ServerSettings.cs:8). Resolve the actual enum
            // type from the field/property so we don't depend on a
            // specific Il2Cpp proxy namespace.
            Type? hostTypeEnum = (hostTypeProp as FieldInfo)?.FieldType
                                 ?? ((PropertyInfo)hostTypeProp).PropertyType;
            Type? regionEnum   = (preferredProp as FieldInfo)?.FieldType
                                 ?? ((PropertyInfo)preferredProp).PropertyType;

            // Prefer server-provided region over config. We read it via
            // the same reflection style as the existing patches so the
            // Il2Cpp type-name suffix doesn't bite us.
            string region = ReadServerProvidedRegion() ?? Mod.Cfg.PhotonCloudRegion ?? "us";

            object hostValue = Enum.ToObject(hostTypeEnum!, 1); // PhotonCloud
            object regionValue;
            try { regionValue = Enum.Parse(regionEnum!, region.ToLowerInvariant(), ignoreCase: true); }
            catch
            {
                Mod.Log.Warning($"[photon-region] '{region}' is not a CloudRegionCode enum value; defaulting to us");
                regionValue = Enum.ToObject(regionEnum!, 1); // CloudRegionCode.us = 1
                region = "us";
            }

            if (hostTypeProp is FieldInfo hostField) hostField.SetValue(settings, hostValue);
            else ((PropertyInfo)hostTypeProp).SetValue(settings, hostValue);
            if (preferredProp is FieldInfo prefField) prefField.SetValue(settings, regionValue);
            else ((PropertyInfo)preferredProp).SetValue(settings, regionValue);

            Mod.Log.Msg($"[photon-region] forced HostType=PhotonCloud PreferredRegion={region}");
        }
        catch (Exception ex) { Mod.Log.Warning($"[photon-region] apply failed: {ex.Message}"); }
    }

    /// <summary>Reads
    /// <c>RecNet.Matchmaking.LocalRoomInstance.PhotonRegionId</c> as a
    /// lowercase enum name (eu, us, asia, …). Returns null if no room
    /// instance has been received yet, or if any link in the chain is
    /// null. Server-told region wins over the static config default
    /// because the server is authoritative about which Photon region
    /// hosts the room the watch is about to join.</summary>
    private static string? ReadServerProvidedRegion()
    {
        try
        {
            var matchmaking = FindGameType("RecNet.Matchmaking");
            var localRoom = matchmaking?.GetProperty("LocalRoomInstance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (localRoom is null) return null;
            var regionObj = localRoom.GetType().GetProperty("PhotonRegionId")?.GetValue(localRoom);
            if (regionObj is null) return null;
            // CloudRegionCode enum.ToString() gives the lowercase region
            // name (eu, us, asia, …) — see CloudRegionCode.cs.
            return regionObj.ToString();
        }
        catch { return null; }
    }

    // AuthValuesInjector_Prefix + GetCurrentLockToken parked 2026-05-28
    // — see attic/AuthValuesInjector.cs.attic. Photon Custom Auth is
    // now disabled at the AppId level in the Photon dashboard, so the
    // watch no longer needs to attach userid+LoginLock to Photon
    // Authenticate ops.

    private static Type? FindGameType(string name) => Mod.ResolveType(name);
}

internal static class SavePatches
{
    // Last DataBlobName parsed off a SubscriptionUpdateRoom push for
    // our currently-loaded room. Captured BEFORE the watch's caching
    // gate (Rooms.OnSubscriptionUpdateRoom early-outs on a repeat
    // (roomId, subRoomId) tuple — which is every post-save push for a
    // dorm). SetRoomDataBlobName_Prefix reads this to hijack the
    // would-be no-op setter call into a real transition.
    private static volatile string? s_lastPushedBlobName;
    private static long s_lastPushedRoomId;
    private static long s_lastPushedSubRoomId;

    // Hook on Notifications.OnNotificationReceived(string json). Fires for
    // every SignalR message before any handler dispatch. Parses the JSON
    // and, when the Id is SubscriptionUpdateRoom, stashes the new blob.
    public static void OnNotificationReceived_Prefix(string message)
    {
        try
        {
            if (string.IsNullOrEmpty(message)) return;
            // Fast reject: only parse JSON if it could plausibly be a
            // SubscriptionUpdateRoom payload. Saves cycles on the (very
            // frequent) RoomInstanceUpdate / PresenceUpdate / chat pushes.
            if (message.IndexOf("\"SubscriptionUpdateRoom\"", StringComparison.Ordinal) < 0)
                return;
            using var doc = System.Text.Json.JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            if (!root.TryGetProperty("Id", out var idEl) ||
                idEl.ValueKind != System.Text.Json.JsonValueKind.String ||
                !string.Equals(idEl.GetString(), "SubscriptionUpdateRoom", StringComparison.Ordinal))
                return;
            if (!root.TryGetProperty("Msg", out var msg) ||
                msg.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            if (!msg.TryGetProperty("Room", out var room) ||
                room.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            long roomId = 0;
            if (room.TryGetProperty("RoomId", out var ridEl) &&
                ridEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                roomId = ridEl.GetInt64();
            if (!msg.TryGetProperty("Scenes", out var scenes) ||
                scenes.ValueKind != System.Text.Json.JsonValueKind.Array ||
                scenes.GetArrayLength() == 0) return;
            var first = scenes[0];
            long subRoomId = 0;
            if (first.TryGetProperty("RoomSceneId", out var sidEl) &&
                sidEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                subRoomId = sidEl.GetInt64();
            if (!first.TryGetProperty("DataBlobName", out var blobEl) ||
                blobEl.ValueKind != System.Text.Json.JsonValueKind.String) return;
            var blob = blobEl.GetString();
            if (string.IsNullOrEmpty(blob)) return;
            s_lastPushedRoomId    = roomId;
            s_lastPushedSubRoomId = subRoomId;
            s_lastPushedBlobName  = blob;
            Mod.Log.Msg($"[save-reload-fix] captured push via OnNotificationReceived: roomId={roomId} subRoomId={subRoomId} blob={blob}");
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[save-reload-fix] OnNotificationReceived prefix failed: {ex.Message}");
        }
    }

    public static void OnSubscriptionUpdateRoom_Prefix(object message)
    {
        try
        {
            if (message is null) return;
            var dict = message;

            // The watch parses this dict as Dictionary<string, object>
            // via Util.GetKey. We need three keys: RoomId, SubRoomId,
            // and Scenes[0].DataBlobName. The Il2Cpp dict supports
            // get_Item(key) — call it via reflection.
            var getItem = dict.GetType().GetMethod("get_Item", new[] { typeof(string) });
            if (getItem is null) return;

            // The push wire shape from server (BuildRoomDetails) is
            // {"Room":{...}, "Scenes":[{...}], ...}. The Room id lives
            // on the Room sub-object; the scene's blob on Scenes[0].
            object? roomObj   = TryGet(dict, getItem, "Room");
            object? scenesObj = TryGet(dict, getItem, "Scenes");
            if (roomObj is null || scenesObj is null) return;

            long roomId    = 0;
            long subRoomId = 0;
            try
            {
                var roomItem = roomObj.GetType().GetMethod("get_Item", new[] { typeof(string) });
                if (roomItem is not null)
                {
                    var rid = roomItem.Invoke(roomObj, new object[] { "RoomId" });
                    if (rid is not null) roomId = Convert.ToInt64(rid.ToString());
                }
            }
            catch { /* roomId stays 0 — we'll log it and bail later */ }

            // Scenes is an IL2CPP List<RoomScene>. Take the first
            // (current dorm has one scene; multi-scene rooms target
            // Scenes[currentSubRoomId] specifically, but the post-save
            // push always carries the scene that was just saved).
            object? firstScene = null;
            try
            {
                var scenesIndexer = scenesObj.GetType().GetMethod("get_Item", new[] { typeof(int) });
                firstScene = scenesIndexer?.Invoke(scenesObj, new object[] { 0 });
            }
            catch { /* leave firstScene null */ }
            if (firstScene is null) return;

            string? newBlob = null;
            try
            {
                var sceneItem = firstScene.GetType().GetMethod("get_Item", new[] { typeof(string) });
                if (sceneItem is not null)
                {
                    var blob = sceneItem.Invoke(firstScene, new object[] { "DataBlobName" });
                    newBlob = blob?.ToString();
                    var sid = sceneItem.Invoke(firstScene, new object[] { "RoomSceneId" });
                    if (sid is not null) subRoomId = Convert.ToInt64(sid.ToString());
                }
            }
            catch { /* leave newBlob null */ }

            if (string.IsNullOrEmpty(newBlob)) return;

            s_lastPushedRoomId    = roomId;
            s_lastPushedSubRoomId = subRoomId;
            s_lastPushedBlobName  = newBlob;
            Mod.Log.Msg($"[save-reload-fix] captured SubscriptionUpdateRoom push: roomId={roomId} subRoomId={subRoomId} blob={newBlob}");
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[save-reload-fix] OnSubscriptionUpdateRoom prefix failed: {ex.Message}");
        }
    }

    private static object? TryGet(object dict, MethodInfo getItem, string key)
    {
        try { return getItem.Invoke(dict, new object[] { key }); }
        catch { return null; }
    }

    // RoomPersistenceManager.set_RoomDataBlobName runs the change-event
    // path only when the new value differs from the cached field. The
    // 2020 watch's post-save propagation reads from RoomInstance.DataBlob
    // (set by /goto, never updated by SubscriptionUpdateRoom), so on a
    // save it calls the setter with the OLD blob — a no-op that skips
    // OnRoomDataBlobNameChanged. The cached PersistedRoomData stays
    // pointed at the boot-time bytes, and PostSaveReloading deserializes
    // those over the live world — wiping any MakerPen object the user
    // just placed.
    //
    // Live trace evidence (Latest.log, dorm save at 17:14): the watch's
    // own set_RoomDataBlobName fires with old="v10" new="v10" ~900ms
    // after b__4 returns True, even though the SubscriptionUpdateRoom
    // push already carried Scenes[0].DataBlobName="v11".
    //
    // Strategy: intercept the setter. If the incoming value matches the
    // current value (no-op) AND Rooms.LocalRoomScene.DataBlobName has a
    // newer version (the freshly-pushed one), substitute that into the
    // value parameter so the real setter sees a transition and fires
    // OnRoomDataBlobNameChanged → DownloadRoomDataBlobAsync(newBlob).
    // Falls through silently when no override is needed.
    public static void SetRoomDataBlobName_Prefix(object __instance, ref string value)
    {
        string currentForLog = "<unread>";
        string sceneForLog   = "<unread>";
        string scenes0ForLog = "<unread>";
        try
        {
            if (__instance is null)
            {
                Mod.Log.Msg("[save-reload-fix] prefix: __instance null — skipping");
                return;
            }

            var rpmType = __instance.GetType();
            var blobNameProp = rpmType.GetProperty("RoomDataBlobName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (blobNameProp is null)
            {
                Mod.Log.Msg($"[save-reload-fix] prefix: RoomDataBlobName property not found on {rpmType.FullName}");
                return;
            }
            currentForLog = (blobNameProp.GetValue(__instance) as string) ?? "<null>";

            var roomsType = FindGameType("RecNet.Rooms");
            if (roomsType is null)
            {
                Mod.Log.Msg("[save-reload-fix] prefix: RecNet.Rooms type not found");
                return;
            }

            var localScene = roomsType.GetProperty("LocalRoomScene",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (localScene is not null)
                sceneForLog = (localScene.GetType().GetProperty("DataBlobName")?.GetValue(localScene) as string) ?? "<null>";

            var localDetails = roomsType.GetProperty("LocalRoomDetails",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (localDetails is not null)
            {
                var scenes = localDetails.GetType().GetProperty("Scenes")?.GetValue(localDetails);
                if (scenes is not null)
                {
                    var indexer = scenes.GetType().GetMethod("get_Item", new[] { typeof(int) });
                    var first = indexer?.Invoke(scenes, new object[] { 0 });
                    if (first is not null)
                        scenes0ForLog = (first.GetType().GetProperty("DataBlobName")?.GetValue(first) as string) ?? "<null>";
                }
            }

            // Unconditional trace of EVERYTHING we read so we can see in
            // the log exactly why we did or didn't override.
            Mod.Log.Msg($"[save-reload-fix] prefix fired: value=\"{value}\" current=\"{currentForLog}\" " +
                        $"LocalRoomScene.DataBlobName=\"{sceneForLog}\" Scenes[0].DataBlobName=\"{scenes0ForLog}\" " +
                        $"lastPushedBlob=\"{s_lastPushedBlobName ?? "<unset>"}\"");

            // Pick a candidate "newer" blob name. Priority order:
            //   1. Last-pushed-from-SubscriptionUpdateRoom (captured by
            //      OnSubscriptionUpdateRoom_Prefix BEFORE the watch's
            //      cache gate drops the push).
            //   2. LocalRoomScene.DataBlobName.
            //   3. Scenes[0].DataBlobName.
            string? pushedBlob = s_lastPushedBlobName;
            if (string.IsNullOrEmpty(pushedBlob) || string.Equals(pushedBlob, currentForLog, StringComparison.Ordinal))
                pushedBlob = sceneForLog is "<unread>" or "<null>" ? null : sceneForLog;
            if (string.IsNullOrEmpty(pushedBlob) || string.Equals(pushedBlob, currentForLog, StringComparison.Ordinal))
                pushedBlob = scenes0ForLog is "<unread>" or "<null>" ? null : scenes0ForLog;

            if (string.IsNullOrEmpty(pushedBlob)) return;
            if (string.Equals(pushedBlob, value, StringComparison.Ordinal)) return;
            if (string.Equals(pushedBlob, currentForLog, StringComparison.Ordinal)) return;

            Mod.Log.Msg($"[save-reload-fix] hijacking set_RoomDataBlobName({value}); substituting newer blob {pushedBlob}");
            value = pushedBlob;
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[save-reload-fix] set_RoomDataBlobName prefix failed: {ex.Message} " +
                            $"(value=\"{value}\" current=\"{currentForLog}\" scene=\"{sceneForLog}\" scenes0=\"{scenes0ForLog}\")");
        }
    }

    private static Type? FindGameType(string name) => Mod.ResolveType(name);
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

internal static class DiagnosticPatches
{
    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [diagnostic] {message}";
        try { Mod.Log.Msg(line); } catch { }
        try
        {
            if (!string.IsNullOrEmpty(Mod.DiagnosticsLogPath))
                File.AppendAllText(Mod.DiagnosticsLogPath, line + Environment.NewLine);
        }
        catch { }
    }

    public static void ApplicationQuit_Prefix()
    {
        Write("[quit] UnityEngine.Application.Quit()");
        WriteStack();
    }

    public static void ApplicationQuitInt_Prefix(int exitCode)
    {
        Write($"[quit] UnityEngine.Application.Quit({exitCode})");
        WriteStack();
    }

    public static void BootSequenceRegisterError_Prefix(string error)
    {
        Write($"[boot-error] BootSequence.RegisterError: {error}");
        WriteStack();
    }

    public static void AnalyticsCheat_Prefix(MethodBase __originalMethod)
    {
        Write($"[cheat-analytics] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
        WriteStack();
    }

    public static void NamedMethod_Prefix(MethodBase __originalMethod)
    {
        Write($"[call] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
        WriteStack();
    }

    public static bool SessionManagerTryApplicationQuit_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-blocked] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
        WriteStack();
        return false;
    }

    private static void WriteStack()
    {
        try
        {
            Write("[stack] " + new StackTrace(skipFrames: 2, fNeedFileInfo: false));
        }
        catch (Exception ex)
        {
            Write("[stack] failed: " + ex.Message);
        }
    }
}

internal static class ChatPatches
{
    private static bool s_logged;

    // Force RecNet.AccountExtensions.CanLocalPlayerChat to return true.
    // PlayerEmotes.SendChatEmoteCoroutine d__53 offset 040 calls this
    // before raising the in-room Photon chat RPC; if it returns false
    // the coroutine bails silently and the sender doesn't even see
    // their own message echo.
    public static void CanLocalPlayerChat_Postfix(ref bool __result)
    {
        if (!__result && !s_logged)
        {
            s_logged = true;
            Mod.Log.Msg("[chat-fix] CanLocalPlayerChat returned false; forcing true to unblock room chat");
        }
        __result = true;
    }

    // Skip the original. Returning false from a Harmony prefix stops
    // the underlying call.
    public static bool RemoveInvalidCharactersFromMessage_Prefix()
    {
        Mod.Log.Msg("[chat-trace] RemoveInvalidCharactersFromMessage skipped");
        return false;
    }

    public static void SendChatEmote_Prefix(string message)
    {
        Mod.Log.Msg($"[chat-trace] SendChatEmote msg=\"{message}\"");
    }

    public static void ProcessNewChatMessageReceived_Prefix(string message, bool playAudioForLocalPlayer)
    {
        Mod.Log.Msg($"[chat-trace] ProcessNewChatMessageReceived msg=\"{message}\" audio={playAudioForLocalPlayer}");
    }

    public static void RpcChatEmote_Prefix(string message)
    {
        Mod.Log.Msg($"[chat-trace] RpcChatEmote (RPC RECEIVED) msg=\"{message}\"");
    }
}
