using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using ExitGames.Client.Photon.LoadBalancing;  // AuthenticationValues, CustomAuthenticationType
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RecNet;                                  // Core, Login, Matchmaking
using Steamworks;
using UnityEngine;

namespace DorkNet.ClientPatch;

/// <summary>
/// BepInEx 6 IL2CPP plugin that owns every client-side change DorkNet
/// needs to make to the 2020 watch — Photon AppId override, RecNet
/// domain rewrite, and Photon CustomAuth identity injection.
///
/// Replaces the previous patch-client.ps1 byte-edit approach so the
/// flow on a client update is now: download the new build → drop the
/// plugin in BepInEx/plugins/ → done. No more hardcoded resources.assets
/// offsets, no more hosts-file edits, no more mkcert wildcard.
///
/// Configuration lives in BepInEx/config/sh.dork.clientpatch.cfg
/// (auto-created on first run). Edit values, restart the client, the
/// patches re-apply with the new settings.
/// </summary>
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    /// <summary>Replacement domain for every <c>*.rec.net</c> URL the watch
    /// would otherwise hit. <c>"localhost"</c> means
    /// <c>https://ns.rec.net/?v=2</c> → <c>https://ns.localhost/?v=2</c> at
    /// runtime (Uri ctor patch). Empty disables the rewrite.</summary>
    public static ConfigEntry<string> ServerHost = null!;

    /// <summary>Photon Cloud Realtime AppId. When non-empty, replaces
    /// <c>PhotonNetwork.PhotonServerSettings.AppID</c> right before
    /// <c>ConnectUsingSettings</c> hands off to NetworkingPeer.</summary>
    public static ConfigEntry<string> PhotonAppId = null!;

    /// <summary>Photon Voice AppId. Empty falls back to
    /// <see cref="PhotonAppId"/>.</summary>
    public static ConfigEntry<string> PhotonVoiceAppId = null!;

    /// <summary>Send <c>userid</c>+<c>LoginLock</c> AuthValues with the
    /// Photon connect so DorkNet's <c>/photon/customauth</c> endpoint
    /// can validate the session. Disable while debugging Photon
    /// connectivity if AuthValues are interfering.</summary>
    public static ConfigEntry<bool> InjectAuthValues = null!;

    /// <summary>Replacement branding string. Every user-facing UI
    /// label that contains "RecNet" gets the substring replaced
    /// with this value at runtime — so error dialogs read
    /// "Failed to connect to DorkNet" instead of
    /// "Failed to connect to RecNet". Empty disables the rewrite
    /// (useful if you want logs to keep matching upstream
    /// documentation).</summary>
    public static ConfigEntry<string> BrandName = null!;
    public static ConfigEntry<bool> EnableBrandRename = null!;

    /// <summary>Skip the Steam DRM "RestartAppIfNecessary" check. The
    /// vanilla watch calls
    /// <c>Steamworks.SteamAPI.RestartAppIfNecessary(471710)</c> on
    /// boot, which forces a relaunch through Steam if the .exe was
    /// started directly (Steam not running, or launched from
    /// outside Steam). Enabling this returns false from that call so
    /// the watch keeps booting wherever it was started. Has no effect
    /// if you're launching from Steam normally.</summary>
    public static ConfigEntry<bool> SkipSteamRestart = null!;

    /// <summary>Fake out the Steamworks SDK so the watch can boot
    /// without Steam.exe running. Patches the C#-level Steamworks.NET
    /// wrapper methods to return canned success values WITHOUT calling
    /// the underlying native steam_api64.dll, so we don't need
    /// Goldberg / SmartSteamEmu / etc. (which trip Defender as PUA).
    ///
    /// Patches applied when on:
    ///   - SteamAPI.Init             → true
    ///   - SteamAPI.IsSteamRunning   → true
    ///   - SteamAPI.GetHSteamPipe    → fake non-zero handle
    ///   - SteamAPI.GetHSteamUser    → fake non-zero handle
    ///   - SteamUser.GetSteamID      → CSteamID built from FakeSteamId
    ///   - SteamFriends.GetPersonaName → FakeAccountName
    ///   - SteamPlatformManager.GetAuthSessionTicket → fake bytes
    ///
    /// Our DorkNet auth backend doesn't actually validate the Steam
    /// ticket — it just uses platformId (SteamID) as the
    /// cached_login lookup key — so the fake ticket is fine.</summary>
    public static ConfigEntry<bool> FakeSteamApi = null!;

    /// <summary>SteamID64 to fake when <see cref="FakeSteamApi"/> is on.
    /// Match the value our auth backend has on file for the player —
    /// the same SteamID lets the player keep their account across
    /// machines. Empty = deterministic per-machine ID derived from
    /// the OS user + machine name.</summary>
    public static ConfigEntry<string> FakeSteamId = null!;

    /// <summary>Display name returned from
    /// <c>Steamworks.SteamFriends.GetPersonaName</c> when
    /// <see cref="FakeSteamApi"/> is on. Empty = the OS user name.</summary>
    public static ConfigEntry<string> FakeAccountName = null!;
    public static ConfigEntry<bool> EnableTlsTrustBypass = null!;
    public static ConfigEntry<bool> EnablePhotonRegionBypass = null!;
    public static ConfigEntry<bool> EnableBootDiagnostics = null!;
    public static ConfigEntry<bool> DetailedUnityLogs = null!;

    /// <summary>Targeted diagnostics for avatar/store DTOs and UI binding
    /// failures. This is intentionally narrower than raw HTTP tracing so the
    /// logs identify crash-causing item ids/GUIDs without dumping tokens or
    /// every request body.</summary>
    public static ConfigEntry<bool> DetailedAvatarStoreLogs = null!;

    /// <summary>Legacy crash guard that replaces avatar item GUIDs unknown to
    /// the local client with a known-safe item before the client parser runs.
    /// Keep off by default now that the server should only send client-known
    /// wardrobe items; enabling it changes the visible equipped item.</summary>
    public static ConfigEntry<bool> SubstituteUnknownAvatarItems = null!;

    /// <summary>Legacy guard for stale servers that returned the wrong shape
    /// from storefront objective completion. Keep off by default so the client
    /// exercises the real server endpoint.</summary>
    public static ConfigEntry<bool> SkipStorefrontObjectivePosts = null!;

    /// <summary>Legacy mirror guard that blocks direct torso unequip. This can
    /// interfere with normal outfit editing, so it is opt-in only.</summary>
    public static ConfigEntry<bool> BlockDirectTorsoUnequip = null!;

    /// <summary>Cleanup exception suppressor used only while debugging teardown
    /// crashes. Keep off by default so real crashes stay visible.</summary>
    public static ConfigEntry<bool> SuppressScreenCleanupNullRef = null!;
    public static ConfigEntry<bool> FailFastRequiredPatches = null!;
    public static ConfigEntry<bool> CatchAllManagedExceptions = null!;

    public override void Load()
    {
        Log = base.Log;

        ServerHost = Config.Bind(
            "Server", "Host", "localhost",
            "Domain that replaces *.rec.net in every URL the watch builds. " +
            "Empty disables the rewrite (use only if pointing at the official servers).");

        PhotonAppId = Config.Bind(
            "Photon", "AppId", string.Empty,
            "Photon Cloud Realtime AppId (UUID, no curly braces). " +
            "Empty leaves the AppId baked into the build untouched.");

        PhotonVoiceAppId = Config.Bind(
            "Photon", "VoiceAppId", string.Empty,
            "Photon Cloud Voice AppId. Empty falls back to AppId.");

        InjectAuthValues = Config.Bind(
            "Photon", "InjectAuthValues", true,
            "Attach userid + LoginLock to PhotonNetwork.AuthValues right before " +
            "OpAuthenticate so DorkNet's /photon/customauth endpoint can identify " +
            "the player. Disable to debug pure Photon connectivity issues.");

        BrandName = Config.Bind(
            "UI", "BrandName", "DorkNet",
            "Replacement branding string. Any UI label containing 'RecNet' " +
            "gets that substring replaced with this value at runtime, so " +
            "error dialogs and the loading screen show the private-server " +
            "brand. Empty disables the rewrite.");

        EnableBrandRename = Config.Bind(
            "UI", "EnableBrandRename", false,
            "Patch Unity/TMP text setters to replace RecNet with BrandName. " +
            "Disabled by default because broad UI text setter hooks add a lot " +
            "of IL2CPP detours for cosmetic value only.");

        SkipSteamRestart = Config.Bind(
            "Steam", "SkipRestartAppIfNecessary", true,
            "Bypass Steamworks.SteamAPI.RestartAppIfNecessary so the watch " +
            "doesn't relaunch itself through Steam on boot. No effect when " +
            "launched normally from the Steam library; useful when running " +
            "the game directly via Recroom_Release.exe (BepInEx mod-loader " +
            "shims often need this to avoid an infinite restart loop).");

        FakeSteamApi = Config.Bind(
            "Steam", "FakeSteamApi", false,
            "Pretend the Steam SDK initialised even when Steam.exe isn't " +
            "running. Patches Steamworks.NET wrappers to return canned " +
            "success values so the watch's SteamPlatformManager.Initialize " +
            "succeeds without a live Steam process. DorkNet's auth backend " +
            "uses SteamID as a lookup key only — no real ticket validation — " +
            "so the fake ticket is accepted. Default off so launches via " +
            "Steam itself behave normally.");

        FakeSteamId = Config.Bind(
            "Steam", "FakeSteamId", string.Empty,
            "SteamID64 returned by SteamUser.GetSteamID when FakeSteamApi=true. " +
            "Empty = random 63-bit ID generated on first run and persisted " +
            "to BepInEx/config/sh.dork.clientpatch.deviceid. Reuses the " +
            "same ID on every subsequent launch on the same install. " +
            "Pin an explicit ID to share an account across different " +
            "machines (e.g. paste your real SteamID64 to migrate from a " +
            "previous manually-modded client).");

        FakeAccountName = Config.Bind(
            "Steam", "FakeAccountName", string.Empty,
            "Display name returned by SteamFriends.GetPersonaName when " +
            "FakeSteamApi=true. Empty = OS user name.");

        EnableTlsTrustBypass = Config.Bind(
            "Network", "EnableTlsTrustBypass", true,
            "Bypass the old BestHTTP/BouncyCastle certificate verifier. " +
            "Required for many modern certificates, but kept as a switch so " +
            "the patch can be removed when testing plugin stability.");

        EnablePhotonRegionBypass = Config.Bind(
            "Photon", "EnableRegionBypass", true,
            "Bypass the 2020 Photon region ping coroutine and force us. " +
            "Required when modern Photon returns region codes this client " +
            "does not know.");

        EnableBootDiagnostics = Config.Bind(
            "Diagnostics", "EnableBootDiagnostics", false,
            "Patch BootSequence.RegisterError to log extra stack traces. " +
            "Disabled by default because it hooks a fragile IL2CPP boot method.");

        DetailedUnityLogs = Config.Bind(
            "Diagnostics", "DetailedUnityLogs", true,
            "Enable full Unity stack traces for Log/Warning/Error/Exception " +
            "messages and mirror Unity exceptions into the BepInEx log. " +
            "Useful while chasing client teardown crashes.");

        DetailedAvatarStoreLogs = Config.Bind(
            "Diagnostics", "DetailedAvatarStoreLogs", true,
            "Log avatar/store DTOs and BrowsableAvatarItem binding failures. " +
            "Useful when the watch tears down after opening the store, mirror, " +
            "or gift UI. Logs are capped per run to keep LogOutput.log readable.");

        SubstituteUnknownAvatarItems = Config.Bind(
            "Compatibility", "SubstituteUnknownAvatarItems", true,
            "Replace avatar item GUIDs the local client does not know with a " +
            "safe fallback item. This prevents parser crashes from bad server " +
            "data, but can change clothing appearance for unsupported items.");

        SkipStorefrontObjectivePosts = Config.Bind(
            "Compatibility", "SkipStorefrontObjectivePosts", false,
            "Skip RecNet.Storefronts.CompleteObjectives client calls. This was " +
            "only for stale servers that returned the wrong response shape; " +
            "leave off so the server endpoint is tested normally.");

        BlockDirectTorsoUnequip = Config.Bind(
            "Compatibility", "BlockDirectTorsoUnequip", false,
            "Block PlayerAvatar.UnequipOutfitItem when the target body part is " +
            "torso. This can stop the mirror UI from unequipping shirts, so it " +
            "is disabled by default.");

        SuppressScreenCleanupNullRef = Config.Bind(
            "Diagnostics", "SuppressScreenCleanupNullRef", false,
            "Suppress one known ScreenPlayerController.CleanupLocalPlayer " +
            "NullReferenceException during teardown. Disabled by default so " +
            "crashes stay visible in logs.");

        FailFastRequiredPatches = Config.Bind(
            "Diagnostics", "FailFastRequiredPatches", false,
            "Abort plugin startup when a required Harmony patch cannot be " +
            "applied. Disabled by default so one IL2CPP target mismatch is " +
            "logged without taking down every other patch.");

        CatchAllManagedExceptions = Config.Bind(
            "Compatibility", "CatchAllManagedExceptions", true,
            "Install broad managed exception guards: observe task exceptions, " +
            "log AppDomain unhandled exceptions, and swallow exceptions that " +
            "escape the Rec Room scheduler frame loop. This cannot recover " +
            "native Unity access violations.");

        GlobalManagedCatchAll.Install();
        UnityDetailedLogBridge.Install();

        Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} loaded. " +
            $"ServerHost={ServerHost.Value}, PhotonAppId={(string.IsNullOrEmpty(PhotonAppId.Value) ? "<unchanged>" : "<set>")}, " +
            $"InjectAuthValues={InjectAuthValues.Value}, DetailedUnityLogs={DetailedUnityLogs.Value}, " +
            $"DetailedAvatarStoreLogs={DetailedAvatarStoreLogs.Value}, " +
            $"EnableBrandRename={EnableBrandRename.Value}, EnableTlsTrustBypass={EnableTlsTrustBypass.Value}, " +
            $"EnablePhotonRegionBypass={EnablePhotonRegionBypass.Value}, EnableBootDiagnostics={EnableBootDiagnostics.Value}, " +
            $"SubstituteUnknownAvatarItems={SubstituteUnknownAvatarItems.Value}, " +
            $"SkipStorefrontObjectivePosts={SkipStorefrontObjectivePosts.Value}, " +
            $"BlockDirectTorsoUnequip={BlockDirectTorsoUnequip.Value}, " +
            $"SuppressScreenCleanupNullRef={SuppressScreenCleanupNullRef.Value}, " +
            $"FailFastRequiredPatches={FailFastRequiredPatches.Value}, " +
            $"CatchAllManagedExceptions={CatchAllManagedExceptions.Value}");

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        ApplyPatches(harmony);
    }

    private static void ApplyPatches(Harmony harmony)
    {
        // Do not use PatchAll here. On IL2CPP, even discovering and
        // preparing unused patch classes can touch fragile generated methods.
        // Keep the active detour set explicit and close to the manual patcher.
        PatchRequired(harmony, typeof(UriStringCtorPatch), UriStringCtorPatch.Prepare(), "URI string rewrite");
        PatchRequired(harmony, typeof(UriStringKindCtorPatch), UriStringKindCtorPatch.Prepare(), "URI string/kind rewrite");
        PatchRequired(harmony, typeof(PhotonAppIdOverride), PhotonAppIdOverride.Prepare(), "Photon AppId override");
        PatchRequired(harmony, typeof(AuthValuesInjector), AuthValuesInjector.Prepare(), "Photon auth injector");
        PatchRequired(harmony, typeof(TlsTrustServerOnly), TlsTrustServerOnly.Prepare(), "BestHTTP TLS trust bypass");
        PatchRequired(harmony, typeof(TlsTrustLegacy), TlsTrustLegacy.Prepare(), "legacy TLS trust bypass");
        PatchRequired(harmony, typeof(PhotonRegionPingShortcut), PhotonRegionPingShortcut.Prepare(), "Photon region shortcut");

        PatchOptional(harmony, typeof(TMPTextBrandRename), TMPTextBrandRename.Prepare(), "TMP brand rename");
        PatchOptional(harmony, typeof(UITextBrandRename), UITextBrandRename.Prepare(), "Unity UI brand rename");
        PatchOptional(harmony, typeof(SteamRestartBypass), SteamRestartBypass.Prepare(), "Steam restart bypass");

        PatchOptional(harmony, typeof(InteropHelpTestClientFake), SteamFakery.Enabled, "fake Steam client availability");
        PatchOptional(harmony, typeof(InteropHelpTestServerFake), SteamFakery.Enabled, "fake Steam server availability");
        PatchOptional(harmony, typeof(InteropHelpTestPlatformFake), SteamFakery.Enabled, "fake Steam platform availability");
        PatchOptional(harmony, typeof(SteamApiInitFake), SteamFakery.Enabled, "fake Steam init");
        PatchOptional(harmony, typeof(SteamApiIsRunningFake), SteamFakery.Enabled, "fake Steam running");
        PatchOptional(harmony, typeof(SteamApiGetPipeFake), SteamFakery.Enabled, "fake Steam pipe");
        PatchOptional(harmony, typeof(SteamApiGetUserFake), SteamFakery.Enabled, "fake Steam user");
        PatchOptional(harmony, typeof(SteamUserIdFake), SteamFakery.Enabled, "fake Steam id");
        PatchOptional(harmony, typeof(SteamFriendsPersonaNameFake), SteamFakery.Enabled, "fake Steam persona");
        PatchOptional(harmony, typeof(SteamFriendsCountFake), SteamFakery.Enabled, "fake Steam friend count");
        PatchOptional(harmony, typeof(SteamFriendsByIndexFake), SteamFakery.Enabled, "fake Steam friend index");
        PatchOptional(harmony, typeof(SteamApiRunCallbacksFake), SteamFakery.Enabled, "fake Steam callbacks");
        PatchOptional(harmony, typeof(SteamApiShutdownFake), SteamFakery.Enabled, "fake Steam shutdown");
        PatchOptional(harmony, typeof(SteamClientWarningHookFake), SteamFakery.Enabled, "fake Steam warning hook");
        PatchOptional(harmony, typeof(SteamCallbackNativeRegistrationFake), SteamFakery.Enabled, "fake Steam callback registration");
        PatchOptional(harmony, typeof(SteamFriendsFriendNameFake), SteamFakery.Enabled, "fake Steam friend name");
        PatchOptional(harmony, typeof(SteamFriendsAvatarFake), SteamFakery.Enabled, "fake Steam avatar");
        PatchOptional(harmony, typeof(SteamFriendsRichPresenceFake), SteamFakery.Enabled, "fake Steam rich presence");
        PatchOptional(harmony, typeof(SteamUtilsImageRGBAFake), SteamFakery.Enabled, "fake Steam image rgba");
        PatchOptional(harmony, typeof(SteamUtilsImageSizeFake), SteamFakery.Enabled, "fake Steam image size");
        PatchOptional(harmony, typeof(SteamGetAuthTicketShortcut), SteamFakery.Enabled, "fake Steam platform ticket");
        PatchOptional(harmony, typeof(SteamUserGetAuthTicketFake), SteamFakery.Enabled, "fake Steam user ticket");

        PatchOptional(harmony, typeof(AvatarItemParseSubstitute), AvatarItemParseSubstitute.Prepare(), "avatar item substitute");
        PatchOptional(harmony, typeof(AvatarItemParseSubstituteTwoArg), AvatarItemParseSubstituteTwoArg.Prepare(), "avatar item substitute two-arg");
        PatchOptional(harmony, typeof(DynamicAvatarItemImposterCatchAll), GlobalManagedCatchAll.Enabled, "avatar imposter catch-all");
        PatchOptional(harmony, typeof(SchedulerManagedCatchAll), GlobalManagedCatchAll.Enabled, "scheduler managed catch-all");
        PatchOptional(harmony, typeof(StorefrontCompleteObjectivesGuard), StorefrontCompleteObjectivesGuard.Prepare(), "storefront objective guard");
        PatchOptional(harmony, typeof(StorefrontGiftDropDiagnostics), AvatarStoreDiagnostics.Enabled, "storefront gift diagnostics");
        PatchOptional(harmony, typeof(UnlockedAvatarItemDiagnostics), AvatarStoreDiagnostics.Enabled, "unlocked avatar diagnostics");
        PatchOptional(harmony, typeof(GiftPackageDiagnostics), AvatarStoreDiagnostics.Enabled, "gift package diagnostics");
        PatchOptional(harmony, typeof(StorefrontBalanceDiagnostics), AvatarStoreDiagnostics.Enabled, "storefront balance diagnostics");
        PatchOptional(harmony, typeof(PlayerAvatarOutfitDiagnostics), AvatarStoreDiagnostics.Enabled, "player outfit diagnostics");
        PatchOptional(harmony, typeof(RequiredTorsoUnequipGuard), RequiredTorsoUnequipGuard.Prepare(), "torso unequip guard");
        PatchOptional(harmony, typeof(ScreenPlayerCleanupGuard), ScreenPlayerCleanupGuard.Prepare(), "screen cleanup guard");
        PatchOptional(harmony, typeof(BootSequenceRegisterErrorTrace), BootSequenceRegisterErrorTrace.Prepare(), "boot diagnostics");

        Log.LogInfo("Explicit Harmony patch list applied.");
    }

    private static void PatchRequired(Harmony harmony, Type patchType, bool enabled, string label)
    {
        if (!enabled)
        {
            Log.LogInfo($"[patch-skip] {label}");
            return;
        }

        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
            Log.LogInfo($"[patch-ok] {label}");
        }
        catch (Exception ex)
        {
            Log.LogError($"[patch-failed] {label}: {ex}");
            if (FailFastRequiredPatches?.Value == true)
                throw;
        }
    }

    private static void PatchOptional(Harmony harmony, Type patchType, bool enabled, string label)
    {
        if (!enabled)
        {
            Log.LogDebug($"[patch-skip] {label}");
            return;
        }

        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
            Log.LogInfo($"[patch-ok] {label}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[patch-failed] optional {label}: {ex}");
        }
    }
}

internal static class MyPluginInfo
{
    public const string PLUGIN_GUID    = "sh.dork.clientpatch";
    public const string PLUGIN_NAME    = "DorkNet ClientPatch";
    public const string PLUGIN_VERSION = "1.4.8";
}

// ── 1. URI rewrite — every Uri the watch builds with a .rec.net host
//    gets its host substituted to ServerHost. Catches the bootstrap
//    https://ns.rec.net/?v=2 (the only hard-coded URL in the watch —
//    every other service URL comes back in that response, which our
//    server controls). Also catches any odd hard-coded Photon Voice /
//    realtime URLs the SDK might construct.
//
//    Patching the BCL Uri constructor is broader than ideal — any
//    string containing ".rec.net" will be rewritten — but in practice
//    the watch doesn't construct any non-URL strings with that
//    substring. If false positives appear we'll narrow to the
//    HTTPRequest constructor or transpiler the call site.

[HarmonyPatch(typeof(Uri), MethodType.Constructor, new[] { typeof(string) })]
internal static class UriStringCtorPatch
{
    public static bool Prepare() =>
        Plugin.ServerHost != null && !string.IsNullOrEmpty(Plugin.ServerHost.Value);

    public static void Prefix(ref string uriString)
    {
        if (string.IsNullOrEmpty(uriString)) return;
        if (string.IsNullOrEmpty(Plugin.ServerHost.Value)) return;
        if (uriString.IndexOf(".rec.net", StringComparison.OrdinalIgnoreCase) < 0) return;
        var rewritten = uriString.Replace(".rec.net", "." + Plugin.ServerHost.Value);
        if (rewritten != uriString)
        {
            Plugin.Log.LogInfo($"[uri-rewrite] {uriString} → {rewritten}");
            uriString = rewritten;
        }
    }
}

[HarmonyPatch(typeof(Uri), MethodType.Constructor, new[] { typeof(string), typeof(UriKind) })]
internal static class UriStringKindCtorPatch
{
    public static bool Prepare() => UriStringCtorPatch.Prepare();

    public static void Prefix(ref string uriString)
    {
        // Reuse the string-only patch's logic by piping through it. Simpler
        // than copy-pasting and keeps a single rewrite implementation.
        UriStringCtorPatch.Prefix(ref uriString);
    }
}

// ── 2. Photon AppId override — patched at the latest possible moment
//    (right before NetworkingPeer.Connect uses ServerSettings.AppID).
//    Patching the ScriptableObject directly at plugin Load doesn't
//    work because ServerSettings is loaded from Resources lazily; by
//    the time Connect runs, it's loaded and we can mutate it.

[HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.ConnectUsingSettings))]
internal static class PhotonAppIdOverride
{
    public static bool Prepare() =>
        Plugin.PhotonAppId != null && !string.IsNullOrEmpty(Plugin.PhotonAppId.Value);

    public static void Prefix()
    {
        if (string.IsNullOrEmpty(Plugin.PhotonAppId.Value)) return;
        try
        {
            var settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null) return;

            var newAppId = Plugin.PhotonAppId.Value;
            var newVoiceId = string.IsNullOrEmpty(Plugin.PhotonVoiceAppId.Value)
                ? newAppId
                : Plugin.PhotonVoiceAppId.Value;

            if (settings.AppID != newAppId || settings.VoiceAppID != newVoiceId)
            {
                Plugin.Log.LogInfo(
                    $"[photon-appid] override AppID={settings.AppID}→{newAppId}, " +
                    $"VoiceAppID={settings.VoiceAppID}→{newVoiceId}");
                settings.AppID = newAppId;
                settings.VoiceAppID = newVoiceId;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[photon-appid] override failed: {ex.Message}");
        }
    }
}

// ── 3. Photon AuthValues injector — sets userid + LoginLock in
//    PhotonNetwork.AuthValues right before any authenticate op leaves
//    the wire. Verified empirically: vanilla 2020 watch never sets
//    AuthValues itself, so we have to do it externally for our
//    /photon/customauth endpoint to receive identity.

// NetworkingPeer.CallAuthenticate is the watch's single internal entry
// point that ultimately calls LoadBalancingPeer.OpAuthenticate /
// OpAuthenticateOnce on the wire. The vanilla 2020 watch never sets
// PhotonNetwork.AuthValues itself, so we set it here just before
// CallAuthenticate forwards to the base op.
[HarmonyPatch(typeof(NetworkingPeer), nameof(NetworkingPeer.CallAuthenticate))]
internal static class AuthValuesInjector
{
    public static bool Prepare() =>
        Plugin.InjectAuthValues != null && Plugin.InjectAuthValues.Value;

    [HarmonyPrefix]
    public static void EnsureAuthValues(NetworkingPeer __instance)
    {
        if (!Plugin.InjectAuthValues.Value) return;
        try
        {
            var existing = PhotonNetwork.AuthValues;
            if (existing != null && !string.IsNullOrEmpty(existing.UserId))
            {
                // Already set this run — don't clobber. Lets a future
                // patch upstream us if needed.
                return;
            }

            var accountId = Core.LocalAccountId;
            if (accountId <= 0)
            {
                // Pre-login Photon ping has no account yet; let it go
                // through anonymous, the post-login reconnect will pick
                // up our values.
                Plugin.Log.LogInfo("[auth-injector] skip — no LocalAccountId yet");
                return;
            }

            // Read the LoginLock straight from PlayerPrefs (key
            // LoginLockTokenV2). The watch persists it there in
            // Matchmaking.Login at login time, so by the time any
            // Photon op fires it's already on disk — no need to call
            // the private Matchmaking.LoadLockToken().
            var token = MatchmakingHelpers.GetCurrentLockToken();

            var av = new AuthenticationValues
            {
                AuthType = CustomAuthenticationType.Custom,
                UserId   = accountId.ToString(),
            };
            av.AddAuthParameter("userid", accountId.ToString());
            av.AddAuthParameter("LoginLock", token ?? string.Empty);

            PhotonNetwork.AuthValues = av;

            Plugin.Log.LogInfo(
                $"[auth-injector] set Photon AuthValues userid={accountId} " +
                $"LoginLock={(string.IsNullOrEmpty(token) ? "<missing>" : "<set>")}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[auth-injector] failed: {ex.Message}");
        }
    }
}

// ── 4. UI brand rename — replace "RecNet" → BrandName in any text the
//    watch displays. Hook the two universal text-set entry points
//    (Unity UI Text + TMP_Text) so we catch both legacy and modern
//    UI bindings. The watch's hardcoded error strings ("Failed to
//    connect to RecNet (error code: …)", "Connecting to RecNet…",
//    etc.) flow through these setters before reaching the user.

[HarmonyPatch(typeof(TMPro.TMP_Text), nameof(TMPro.TMP_Text.text), MethodType.Setter)]
internal static class TMPTextBrandRename
{
    public static bool Prepare() =>
        Plugin.EnableBrandRename != null && Plugin.EnableBrandRename.Value &&
        Plugin.BrandName != null && !string.IsNullOrEmpty(Plugin.BrandName.Value);

    public static void Prefix(ref string value)
    {
        BrandRenameHelper.Apply(ref value);
    }
}

[HarmonyPatch(typeof(UnityEngine.UI.Text), nameof(UnityEngine.UI.Text.text), MethodType.Setter)]
internal static class UITextBrandRename
{
    public static bool Prepare() => TMPTextBrandRename.Prepare();

    public static void Prefix(ref string value)
    {
        BrandRenameHelper.Apply(ref value);
    }
}

internal static class BrandRenameHelper
{
    public static void Apply(ref string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var brand = Plugin.BrandName.Value;
        if (string.IsNullOrEmpty(brand)) return;
        if (value.IndexOf("RecNet", StringComparison.Ordinal) < 0) return;
        var replaced = value.Replace("RecNet", brand);
        if (replaced != value) value = replaced;
    }
}

// ── 5. Steam DRM bypass — short-circuit
//    Steamworks.SteamAPI.RestartAppIfNecessary so the watch doesn't
//    forcibly re-launch through Steam when the .exe was started
//    directly. Has no effect when launched from the Steam library
//    (RestartAppIfNecessary returns false anyway in that case);
//    matters when running the game outside Steam, which is the
//    common modding/dev path.

[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.RestartAppIfNecessary))]
internal static class SteamRestartBypass
{
    public static bool Prepare() =>
        Plugin.SkipSteamRestart != null && Plugin.SkipSteamRestart.Value;

    public static bool Prefix(ref bool __result)
    {
        if (!Plugin.SkipSteamRestart.Value) return true; // run original
        Plugin.Log.LogInfo("[steam-bypass] short-circuiting RestartAppIfNecessary → false");
        __result = false;
        return false; // skip original
    }
}

// ── 6. Fake Steamworks SDK — when FakeSteamApi=true we hijack the
//    C#-level Steamworks.NET wrapper methods to return canned values
//    WITHOUT calling steam_api64.dll. This is the AV-friendly
//    alternative to Goldberg/SmartSteamEmu — pure managed code,
//    no replacement DLL, no PUA flag.
//
//    Coverage is just-enough for the watch's boot path. We patch:
//      - SteamAPI.Init / IsSteamRunning / GetHSteamPipe / GetHSteamUser
//      - SteamUser.GetSteamID / GetAuthSessionTicket
//      - SteamFriends.GetPersonaName
//    If the watch ever calls a Steamworks method we don't fake, the
//    underlying DLL stub returns its default (usually "no" / 0) and
//    that path quietly does nothing — better than crashing.

internal static class SteamFakery
{
    public static bool Enabled =>
        Plugin.FakeSteamApi != null && Plugin.FakeSteamApi.Value;

    private static ulong? _resolvedSteamId;
    public static ulong ResolvedSteamId
    {
        get
        {
            if (_resolvedSteamId.HasValue) return _resolvedSteamId.Value;
            var raw = Plugin.FakeSteamId.Value;
            if (!string.IsNullOrEmpty(raw) && ulong.TryParse(raw, out var explicitId))
            {
                _resolvedSteamId = explicitId;
                return explicitId;
            }
            // Per-install random ID, persisted to BepInEx/config/
            // sh.dork.clientpatch.deviceid. Generated once on first
            // run; reused on every subsequent launch on the same
            // install. Keeps the same DorkNet account across plugin
            // updates and game updates, but a clean reinstall (or a
            // different machine without the file copied over) gets a
            // brand-new account.
            //
            // Why not SHA256(username + machine)? The mod-31-bit
            // collision space (we packed it into the 9-digit
            // 76561198_xxxxxxxxx slot) made birthday-paradox collisions
            // realistic — Alexa@DESKTOP-XYZ is plausibly the same as
            // someone else's. Random 63-bit gives ~2³¹ users before
            // 50% collision, which we'll comfortably never hit.
            var deviceFile = System.IO.Path.Combine(
                BepInEx.Paths.ConfigPath, "sh.dork.clientpatch.deviceid");
            ulong rand;
            try
            {
                if (System.IO.File.Exists(deviceFile) &&
                    ulong.TryParse(System.IO.File.ReadAllText(deviceFile).Trim(), out var stored))
                {
                    rand = stored;
                }
                else
                {
                    var bytes = new byte[8];
                    System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                    // Mask top bit so the resulting SteamID64 fits the
                    // valid Public-universe individual range (the
                    // top byte stays 0x01).
                    rand = System.BitConverter.ToUInt64(bytes, 0) & 0x7FFF_FFFF_FFFF_FFFFUL;
                    System.IO.File.WriteAllText(deviceFile, rand.ToString());
                    Plugin.Log.LogInfo($"[steam-fake] generated new deviceid → {deviceFile}");
                }
            }
            catch (Exception ex)
            {
                // Filesystem unavailable for some reason — fall back
                // to a one-shot random ID so the boot still proceeds.
                // (Account won't persist across this session, but at
                // least the player can play.)
                Plugin.Log.LogWarning($"[steam-fake] deviceid persistence failed ({ex.Message}); using ephemeral ID");
                var bytes = new byte[8];
                System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                rand = System.BitConverter.ToUInt64(bytes, 0) & 0x7FFF_FFFF_FFFF_FFFFUL;
            }
            // SteamID64 layout: top 32 bits = universe(8) + accountType(4) + instance(20),
            // bottom 32 bits = accountID. The 76561197960265728 prefix is
            // (Public, Individual, Desktop). We OR our random low-32 bits in.
            const ulong SteamId64Base = 76561197960265728UL;
            _resolvedSteamId = SteamId64Base + (rand & 0xFFFF_FFFFUL);
            return _resolvedSteamId.Value;
        }
    }

    public static string ResolvedAccountName =>
        string.IsNullOrEmpty(Plugin.FakeAccountName.Value)
            ? (System.Environment.UserName ?? "DorkNetTester")
            : Plugin.FakeAccountName.Value;
}

// InteropHelp.TestIfAvailableClient/TestIfPlatformSupported/TestIfAvailable
// GameServer are the gate every Steamworks.NET wrapper passes through
// before delegating to the native steam_api64.dll. With FakeSteamApi
// they would otherwise throw "Steamworks is not initialized" because
// no real native pipe was opened — even though our SteamAPI.Init
// prefix returned true, the wrapper checks an internal sentinel set
// by the native init callback. Patch these three to no-op so EVERY
// Steamworks.NET method behind them silently uses our faked output
// (or quietly does nothing for ones we haven't faked).
[HarmonyPatch(typeof(Steamworks.InteropHelp), nameof(Steamworks.InteropHelp.TestIfAvailableClient))]
internal static class InteropHelpTestClientFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix() => !Plugin.FakeSteamApi.Value; // skip original when faking
}

[HarmonyPatch(typeof(Steamworks.InteropHelp), nameof(Steamworks.InteropHelp.TestIfAvailableGameServer))]
internal static class InteropHelpTestServerFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix() => !Plugin.FakeSteamApi.Value;
}

[HarmonyPatch(typeof(Steamworks.InteropHelp), nameof(Steamworks.InteropHelp.TestIfPlatformSupported))]
internal static class InteropHelpTestPlatformFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix() => !Plugin.FakeSteamApi.Value;
}

[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.Init))]
internal static class SteamApiInitFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref bool __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        Plugin.Log.LogInfo("[steam-fake] SteamAPI.Init → true");
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.IsSteamRunning))]
internal static class SteamApiIsRunningFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref bool __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.GetHSteamPipe))]
internal static class SteamApiGetPipeFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref Steamworks.HSteamPipe __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        // HSteamPipe is a struct wrapping a uint — pick anything non-zero.
        __result = new Steamworks.HSteamPipe(1);
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.GetHSteamUser))]
internal static class SteamApiGetUserFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref Steamworks.HSteamUser __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = new Steamworks.HSteamUser(1);
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamUser), nameof(Steamworks.SteamUser.GetSteamID))]
internal static class SteamUserIdFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref Steamworks.CSteamID __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = new Steamworks.CSteamID(SteamFakery.ResolvedSteamId);
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamFriends), nameof(Steamworks.SteamFriends.GetPersonaName))]
internal static class SteamFriendsPersonaNameFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref string __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = SteamFakery.ResolvedAccountName;
        return false;
    }
}

// SteamPlatformManager.PostLoginInitialize iterates the Steam friends
// list (GetFriendCount → for-loop GetFriendByIndex). Without these
// stubs each call falls through TestIfAvailableClient (no-op'd above)
// straight into native steam_api64.dll, which segfaults on our fake
// pipe/user handles. Returning 0 from GetFriendCount short-circuits
// the whole loop so the watch never queries any friend data.
[HarmonyPatch(typeof(Steamworks.SteamFriends), nameof(Steamworks.SteamFriends.GetFriendCount))]
internal static class SteamFriendsCountFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref int __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = 0;
        return false;
    }
}

// Defensive: should never be hit if GetFriendCount returns 0, but if
// some other code path indexes regardless we return an empty CSteamID.
[HarmonyPatch(typeof(Steamworks.SteamFriends), nameof(Steamworks.SteamFriends.GetFriendByIndex))]
internal static class SteamFriendsByIndexFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref Steamworks.CSteamID __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = new Steamworks.CSteamID(0UL);
        return false;
    }
}

// Audit (2026-05-11): grepping every "Call <SteamClass>.<Method>" in
// the watch's IL2CPP ISIL turned up exactly 14 Steamworks methods used
// across the entire binary. The patches above cover Init / IsRunning /
// GetHSteamPipe / GetHSteamUser / GetSteamID / GetPersonaName /
// GetFriendCount / GetFriendByIndex / GetAuthSessionTicket /
// RestartAppIfNecessary. The remaining 6 are stubbed below — every
// one of them needs a no-op or zero return to avoid falling through
// to a native steam_api64.dll call with our fake handles, which
// segfaults the process.

// SteamAPI.RunCallbacks is called every frame from a MonoBehaviour.
// Native call dispatches queued callbacks; with no real pipe, it'd
// dereference null and crash. No-op skips the dispatch — our synthetic
// callbacks (e.g. GetAuthSessionTicketResponse) are fired directly,
// not through this queue.
[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.RunCallbacks))]
internal static class SteamApiRunCallbacksFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix() => !Plugin.FakeSteamApi.Value;
}

// SteamAPI.Shutdown — called on app exit. Native expects to clean up
// a real pipe; no-op so a successful exit doesn't crash.
[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.Shutdown))]
internal static class SteamApiShutdownFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix() => !Plugin.FakeSteamApi.Value;
}

// SteamClient.SetWarningMessageHook installs a callback for Steam's
// internal warnings. No-op — we don't need warnings, and the native
// call would dereference our zero pipe.
[HarmonyPatch(typeof(Steamworks.SteamClient), nameof(Steamworks.SteamClient.SetWarningMessageHook))]
internal static class SteamClientWarningHookFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix() => !Plugin.FakeSteamApi.Value;
}

// The watch registers a few Steam callback bases during
// SteamPlatformManager.Initialize. With FakeSteamApi=true there is no valid
// native Steam pipe behind those callback bases. The one callback the boot path
// needs is synthesized below, so skip native callback registration in fake mode.
[HarmonyPatch]
internal static class SteamCallbackNativeRegistrationFake
{
    public static bool Prepare() => SteamFakery.Enabled;

    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var callbackName in new[]
        {
            "GetAuthSessionTicketResponse_t",
            "GameRichPresenceJoinRequested_t",
            "MicroTxnAuthorizationResponse_t",
            "GameOverlayActivated_t"
        })
        {
            var type = AccessTools.TypeByName("Steamworks.SteamCallbacks+" + callbackName);
            if (type == null)
            {
                Plugin.Log.LogWarning($"[steam-fake] callback type not found: {callbackName}");
                continue;
            }

            var register = AccessTools.Method(type, "RegisterCallback");
            if (register != null) yield return register;

            var unregister = AccessTools.Method(type, "UnregisterCallback");
            if (unregister != null) yield return unregister;
        }
    }

    public static bool Prefix(MethodBase __originalMethod)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        Plugin.Log.LogDebug($"[steam-fake] skip native callback registration: {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
        return false;
    }
}

// SteamFriends.GetFriendPersonaName / GetMediumFriendAvatar — only
// reachable if GetFriendCount > 0, but defensively stubbed in case
// some other call path indexes a friend directly.
[HarmonyPatch(typeof(Steamworks.SteamFriends), nameof(Steamworks.SteamFriends.GetFriendPersonaName))]
internal static class SteamFriendsFriendNameFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref string __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = string.Empty;
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamFriends), nameof(Steamworks.SteamFriends.GetMediumFriendAvatar))]
internal static class SteamFriendsAvatarFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref int __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = 0;
        return false;
    }
}

// SteamFriends.SetRichPresence — sets the "playing X" string visible
// to Steam friends. Cosmetic; native call would crash. Return true
// (success) so the watch doesn't log a failure.
[HarmonyPatch(typeof(Steamworks.SteamFriends), nameof(Steamworks.SteamFriends.SetRichPresence))]
internal static class SteamFriendsRichPresenceFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref bool __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = true;
        return false;
    }
}

// SteamUtils.GetImageRGBA / GetImageSize — used to fetch Steam avatar
// pixel data. Without a real avatar handle (we return 0 from
// GetMediumFriendAvatar), these would normally not be called, but
// stubbed defensively to fail-fast if the watch tries.
[HarmonyPatch(typeof(Steamworks.SteamUtils), nameof(Steamworks.SteamUtils.GetImageRGBA))]
internal static class SteamUtilsImageRGBAFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref bool __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamUtils), nameof(Steamworks.SteamUtils.GetImageSize))]
internal static class SteamUtilsImageSizeFake
{
    public static bool Prepare() => SteamFakery.Enabled;
    public static bool Prefix(ref bool __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;
        __result = false;
        return false;
    }
}


// SteamPlatformManager.GetAuthSessionTicket() returns an IPromise that
// only completes when Steam fires the GetAuthSessionTicketResponse_t
// callback (real Steam dispatches it asynchronously a few hundred ms
// after GetAuthSessionTicket). With FakeSteamApi=true there's no
// native Steam to fire it, so the watch waits ~5s and then logs
// "Timed out waiting for Steam GetAuthSessionTicketResponse" → login
// stalls for 5s before the boot continues.
//
// Postfix-patch the method so the callback fires synthetically right
// after GetAuthSessionTicket returns: build a k_EResultOK response
// pointing at our fake HAuthTicket and feed it back through the watch's
// own OnGetAuthSessionTicketResponse handler. That completes the
// inner promise immediately and the boot doesn't pay the 5s timeout.
[HarmonyPatch(typeof(SteamPlatformManager), nameof(SteamPlatformManager.GetAuthSessionTicket))]
internal static class SteamGetAuthTicketShortcut
{
    public static bool Prepare() => SteamFakery.Enabled;

    public static void Postfix(SteamPlatformManager __instance)
    {
        if (!Plugin.FakeSteamApi.Value) return;
        try
        {
            var response = new Steamworks.GetAuthSessionTicketResponse_t
            {
                m_eResult     = Steamworks.EResult.k_EResultOK,
                m_hAuthTicket = new Steamworks.HAuthTicket(0xDEADBEEFu),
            };
            __instance.OnGetAuthSessionTicketResponse(response);
            Plugin.Log.LogInfo("[steam-fake] synth GetAuthSessionTicketResponse → k_EResultOK");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[steam-fake] synth ticket-response failed: {ex.Message}");
        }
    }
}

// SteamUser.GetAuthSessionTicket is the entry point for the watch's
// auth flow. Real signature:
//   HAuthTicket GetAuthSessionTicket(byte[] pTicket, int cbMaxTicket,
//                                    out uint pcbTicket)
// We fake a 64-byte ticket containing the SteamID + a marker so our
// /cachedlogin endpoint could in principle distinguish forged vs real
// (it currently doesn't validate — see auth/AuthController.cs).
[HarmonyPatch(typeof(Steamworks.SteamUser), nameof(Steamworks.SteamUser.GetAuthSessionTicket))]
internal static class SteamUserGetAuthTicketFake
{
    public static bool Prepare() => SteamFakery.Enabled;

    public static bool Prefix(
        Il2CppStructArray<byte> pTicket,
        int cbMaxTicket,
        ref uint pcbTicket,
        ref Steamworks.HAuthTicket __result)
    {
        if (!Plugin.FakeSteamApi.Value) return true;

        const int desiredLen = 64;
        var len = System.Math.Min(desiredLen, cbMaxTicket);
        // Fill the ticket with deterministic but-not-zero bytes. First
        // 8 bytes = SteamID little-endian; rest = a magic marker so a
        // future server-side validator could detect a forged session.
        var sid = System.BitConverter.GetBytes(SteamFakery.ResolvedSteamId);
        for (int i = 0; i < len; i++)
        {
            pTicket[i] = i < sid.Length ? sid[i] : (byte)('D' ^ (i & 0x0F));
        }
        pcbTicket = (uint)len;
        __result = new Steamworks.HAuthTicket(0xDEADBEEF);
        Plugin.Log.LogInfo($"[steam-fake] GetAuthSessionTicket → {len} bytes, SteamID={SteamFakery.ResolvedSteamId}");
        return false;
    }
}

// ── 7. TLS cert trust — make BouncyCastle (the watch's BestHTTP TLS
//    stack, vendored circa 2020) accept any server certificate.
//
//    Why: Coolify auto-issues Let's Encrypt ECDSA certs (E-series
//    intermediate → ISRG Root X2). The 2020 BouncyCastle bundle
//    pre-dates ISRG Root X2's 2020 launch, so chain validation can't
//    reach a trusted root and the TLS handshake aborts with alert 90
//    user_canceled. Swap the cert verifier hooks for no-ops so chain
//    validation is bypassed; the actual ECDSA crypto still runs.
//
//    Two layers patched:
//      a) NotifyServerCertificate (Legacy + ServerOnly Tls
//         Authentication) — these are the per-handshake callbacks BC
//         invokes after parsing the cert chain. Default impls in this
//         build wrap an ICertificateVerifyer; we short-circuit them.
//      b) AlwaysValidVerifyer.IsValid is already always-true, so any
//         code path that already uses it is fine — the patches above
//         convert the rest.
[HarmonyPatch(typeof(BestHTTP.SecureProtocol.Org.BouncyCastle.Crypto.Tls.ServerOnlyTlsAuthentication),
    nameof(BestHTTP.SecureProtocol.Org.BouncyCastle.Crypto.Tls.ServerOnlyTlsAuthentication.NotifyServerCertificate))]
internal static class TlsTrustServerOnly
{
    public static bool Prepare() =>
        Plugin.EnableTlsTrustBypass != null && Plugin.EnableTlsTrustBypass.Value;

    public static bool Prefix() => false; // skip original — never reject
}

[HarmonyPatch(typeof(Org.BouncyCastle.Crypto.Tls.LegacyTlsAuthentication),
    nameof(Org.BouncyCastle.Crypto.Tls.LegacyTlsAuthentication.NotifyServerCertificate))]
internal static class TlsTrustLegacy
{
    public static bool Prepare() =>
        Plugin.EnableTlsTrustBypass != null && Plugin.EnableTlsTrustBypass.Value;

    public static bool Prefix() => false; // skip original — never reject
}

// ── 8z. Photon region ping bypass — short-circuit
//    PUNNetworkManager.PingPhotonRegions() with a pre-resolved
//    {us:1} dictionary so the broken coroutine never runs.
//
//    The original PingPhotonRegionsInternal coroutine ToDictionary's
//    PhotonNetwork's available-region list keyed by
//    `CloudRegionCode`. The 2020 watch's CloudRegionCode enum knows
//    13 codes (eu, us, asia, jp, au, usw, sa, cae, kr, in, ru, rue,
//    none) — anything else from Photon Cloud's modern NameServer
//    (e.g. usw2, eu2, tr, za, jp2) parses as `none = 4`. Two
//    unknown codes → two regions with key `none` → ToDictionary
//    throws `ArgumentException: An item with the same key has
//    already been added. Key: none` (output_log.txt:209-220), and
//    the coroutine fails before completing the promise — every
//    consumer awaiting it is stuck.
//
//    We can't fix Photon's region list (it comes from
//    Photon Cloud's NameServer, governed by the AppId's dashboard
//    config). Patching the dashboard to disable unknown regions
//    works in theory but the user's AppId is configured for the
//    full set. Cheaper to just hand the watch a "single us region"
//    promise — Rec Room only ever connected to one region per
//    session anyway, and our dorknet config (Photon:CloudRegion)
//    is "us" by default.
[HarmonyPatch(typeof(PUNNetworkManager), nameof(PUNNetworkManager.PingPhotonRegions))]
internal static class PhotonRegionPingShortcut
{
    public static bool Prepare() =>
        Plugin.EnablePhotonRegionBypass != null && Plugin.EnablePhotonRegionBypass.Value;

    public static bool Prefix(
        ref RecRoom.Async.IPromise<Il2CppSystem.Collections.Generic.IReadOnlyDictionary<
            CloudRegionCode, int>> __result)
    {
        try
        {
            // Single-entry dict {us = 1ms}. Watch awaits this
            // promise then picks the "best" (only) region. No ping
            // is actually performed — the promise resolves
            // immediately so downstream Photon connect logic
            // proceeds without waiting on a broken coroutine.
            var dict = new Il2CppSystem.Collections.Generic.Dictionary<
                CloudRegionCode, int>();
            dict[CloudRegionCode.us] = 1;

            var promise = new RecRoom.Async.Promise<Il2CppSystem.Collections.Generic.IReadOnlyDictionary<
                CloudRegionCode, int>>();
            promise.Complete(dict.Cast<Il2CppSystem.Collections.Generic.IReadOnlyDictionary<
                CloudRegionCode, int>>());
            // Il2CppInterop's Promise<T> wrapper isn't covariantly
            // related to IPromise<T> at the C# level, so go through
            // Cast<T>() (every interop wrapper exposes it).
            __result = promise.Cast<RecRoom.Async.IPromise<Il2CppSystem.Collections.Generic.IReadOnlyDictionary<
                CloudRegionCode, int>>>();

            // The ORIGINAL coroutine doesn't just return a dict — at
            // PingPhotonRegionsInternal.txt:928+940 it also calls
            // PhotonNetwork.OverrideBestCloudServer(bestCode) +
            // ConnectToBestCloudServer(gameVersion), which is what
            // actually opens the wire connection to the chosen region.
            // We bypassed the coroutine, so we have to issue those
            // calls ourselves — otherwise PhotonNetwork.IsConnected
            // stays false and BootSequence's
            // <OnEnterPostLoadInitialSceneState>b__68_5 fires
            // RegisterError("Unable to connect to RecNet game
            // servers") (BootSequence.txt:10490).
            PhotonNetwork.OverrideBestCloudServer(CloudRegionCode.us);
            // gameVersion: empty string keeps the watch's existing
            // PhotonServerSettings.AppVersion. The original coroutine
            // at line 940 passes a similarly-stub value.
            PhotonNetwork.ConnectToBestCloudServer(string.Empty);

            Plugin.Log.LogInfo("[photon-region] short-circuit → {us:1} + ConnectToBestCloudServer");
            return false; // skip original
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[photon-region] short-circuit failed: {ex.Message}; falling back to original");
            return true;
        }
    }
}

// ── 8a. Avatar parse exception swallow — wrap
//    AvatarItem.FromRecNetString(string) so an "InvalidOperationException:
//    Missing data for guid …" / AvatarParseException("Bad outfit
//    string …") doesn't propagate up the IL2CPP stack and tear down
//    the watch's player.
//
//    The dorm scene's hardcoded ImposterSpawnManager prefabs reference
//    avatar items by GUID — some of those GUIDs are SWATCH/MASK asset
//    guids (in <c>swatchLookup</c>/<c>maskLookup</c>) instead of
//    AvatarItem guids (in <c>avatarItemDataLookup</c>). The watch's
//    parser only checks the AvatarItem dict and throws on misses;
//    those throws bubble through ImposterSpawnManager.ProcessQueue →
//    Scheduler → Update and ultimately destroy the local player
//    (output_log.txt:1568-1599). Returning a default AvatarItem
//    for the bad guid lets the queue processor skip the item and
//    keep going.
//
//    The two-arg overload (called by the one-arg overload) is what
//    actually throws — patch the entry point so both call sites
//    benefit.
// Substitute unknown GUIDs with a known-safe fallback BEFORE the
// original parser runs — purely returning default(AvatarItem) on
// throw leaves the watch's callers with a struct whose
// AvatarItemData / AvatarItemVisualData are null, which they then
// dereference (NullReferenceException → caught by the finalizer →
// caller dereferences the still-default struct → IL2CPP runtime
// segfault, no managed log).
//
// Strategy: check the GUID prefix against
// AvatarItemWardrobeRuntimeConfig.Config.avatarItemDataLookup
// (loaded lazily from the client's own runtime config). If the GUID
// isn't in the dict, rewrite avatarItemDesc / itemGuid to point
// at <c>FALLBACK_GUID</c> — a confirmed-safe AvatarItem (Hat_Angler)
// that always renders. The original method then runs unchanged on
// the substituted GUID and returns a fully-populated AvatarItem.
// Callers see a real AvatarItem, render the fallback prefab, and
// continue without segfaulting.
internal static class AvatarItemSafelist
{
    // Hat_Angler — first entry in StarterOutfitSelections, slot 0
    // (Head). Its AvatarItem is in avatarItemDataLookup AND its
    // prefab is in avatarItemPrefabLookup, so any caller can render
    // it as a fallback without triggering further misses.
    public const string FallbackGuid = "03f8c394-28fa-4087-978b-8d108f0bd969";

    private static System.Collections.Generic.HashSet<string>? _safe;
    private static int _failCount;

    public static bool IsSafe(string guid)
    {
        if (Plugin.SubstituteUnknownAvatarItems == null ||
            !Plugin.SubstituteUnknownAvatarItems.Value)
            return true;

        if (string.IsNullOrEmpty(guid)) return false;
        if (_safe is null)
        {
            try
            {
                var config = RecRoom.Avatar.Data.Runtime.AvatarItemWardrobeRuntimeConfig.Config;
                if (config == null)
                {
                    if (System.Threading.Interlocked.Increment(ref _failCount) <= 3)
                        Plugin.Log.LogWarning("[avatar-safelist] runtime config is null — returning false");
                    return false;
                }
                if (config.avatarItemDataLookup == null)
                {
                    if (System.Threading.Interlocked.Increment(ref _failCount) <= 3)
                        Plugin.Log.LogWarning("[avatar-safelist] avatarItemDataLookup is null — returning false");
                    return false;
                }
                var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in config.avatarItemDataLookup.Keys)
                {
                    if (!string.IsNullOrEmpty(k)) set.Add(k);
                }
                _safe = set;
                Plugin.Log.LogInfo($"[avatar-safelist] initialized with {set.Count} GUIDs");
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Increment(ref _failCount) <= 3)
                    Plugin.Log.LogWarning($"[avatar-safelist] init failed: {ex.GetType().Name} {ex.Message}");
                return false;
            }
        }
        return _safe.Contains(guid);
    }
}

[HarmonyPatch(typeof(RecRoom.Avatar.Data.Runtime.AvatarItem),
    nameof(RecRoom.Avatar.Data.Runtime.AvatarItem.FromRecNetString),
    new[] { typeof(string) })]
internal static class AvatarItemParseSubstitute
{
    private static int _subCount;
    // Per-GUID first-time log dedup so the log shows which UNIQUE
    // unknown GUIDs are being hit (cap at 50 different guids; after
    // that we suppress to keep the log readable). Without this the
    // first 5 hits of one repeating guid burn the global cap and
    // hide every other guid from the log — which made it impossible
    // to tell whether wardrobe store tiles were being substituted.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _seenGuids
        = new(StringComparer.OrdinalIgnoreCase);

    public static bool Prepare() =>
        Plugin.SubstituteUnknownAvatarItems != null && Plugin.SubstituteUnknownAvatarItems.Value;

    public static void Prefix(ref string avatarItemDesc)
    {
        if (string.IsNullOrEmpty(avatarItemDesc)) return;
        var comma = avatarItemDesc.IndexOf(',');
        var guid = comma < 0 ? avatarItemDesc : avatarItemDesc.Substring(0, comma);
        if (AvatarItemSafelist.IsSafe(guid)) return;
        var hitN = System.Threading.Interlocked.Increment(ref _subCount);
        // Log the FIRST occurrence of each unique unknown guid (up to
        // 50 unique guids), plus a periodic heartbeat every 250 hits
        // total so we know substitution volume without spam.
        var firstSeen = _seenGuids.TryAdd(guid, 0);
        if ((firstSeen && _seenGuids.Count <= 50) || hitN % 250 == 0)
            Plugin.Log.LogWarning(
                $"[avatar-parse] substitute unknown guid '{guid}' → fallback (Hat_Angler) " +
                $"[unique #{_seenGuids.Count} / total #{hitN}]");
        // Keep whatever customization tail the caller had so the
        // 5-segment AvatarItemSelection format stays well-formed.
        var tail = comma < 0 ? ",,," : avatarItemDesc.Substring(comma);
        avatarItemDesc = AvatarItemSafelist.FallbackGuid + tail;
    }

    // Belt-and-suspenders: if a GUID slips past IsSafe but the
    // original still throws (e.g. transient lookup race), return a
    // default item. Better a missing prefab than a hard crash.
    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception __exception,
        string avatarItemDesc,
        ref RecRoom.Avatar.Data.Runtime.AvatarItem __result)
    {
        if (__exception == null) return null;
        if (Plugin.SubstituteUnknownAvatarItems == null ||
            !Plugin.SubstituteUnknownAvatarItems.Value)
            return __exception;

        Plugin.Log.LogWarning(
            $"[avatar-parse] finalizer swallowed {__exception.GetType().Name}: " +
            $"{__exception.Message} — desc='{avatarItemDesc}'");
        __result = default!;
        return null;
    }
}

[HarmonyPatch(typeof(RecRoom.Avatar.Data.Runtime.AvatarItem),
    nameof(RecRoom.Avatar.Data.Runtime.AvatarItem.FromRecNetString),
    new[] { typeof(string), typeof(Il2CppSystem.ArraySegment<string>) })]
internal static class AvatarItemParseSubstituteTwoArg
{
    private static int _subCount;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _seenGuids
        = new(StringComparer.OrdinalIgnoreCase);

    public static bool Prepare() =>
        Plugin.SubstituteUnknownAvatarItems != null && Plugin.SubstituteUnknownAvatarItems.Value;

    public static void Prefix(ref string itemGuid)
    {
        if (string.IsNullOrEmpty(itemGuid)) return;
        if (AvatarItemSafelist.IsSafe(itemGuid)) return;
        var hitN = System.Threading.Interlocked.Increment(ref _subCount);
        var firstSeen = _seenGuids.TryAdd(itemGuid, 0);
        if ((firstSeen && _seenGuids.Count <= 50) || hitN % 250 == 0)
            Plugin.Log.LogWarning(
                $"[avatar-parse] (2-arg) substitute unknown guid '{itemGuid}' → fallback (Hat_Angler) " +
                $"[unique #{_seenGuids.Count} / total #{hitN}]");
        itemGuid = AvatarItemSafelist.FallbackGuid;
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(
        Exception __exception,
        string itemGuid,
        ref RecRoom.Avatar.Data.Runtime.AvatarItem __result)
    {
        if (__exception == null) return null;
        if (Plugin.SubstituteUnknownAvatarItems == null ||
            !Plugin.SubstituteUnknownAvatarItems.Value)
            return __exception;

        Plugin.Log.LogWarning(
            $"[avatar-parse] finalizer swallowed {__exception.GetType().Name}: " +
            $"{__exception.Message} — guid='{itemGuid}'");
        __result = default!;
        return null;
    }
}

internal static class GlobalManagedCatchAll
{
    private static int _installed;
    private static int _logCount;

    public static bool Enabled =>
        Plugin.CatchAllManagedExceptions != null && Plugin.CatchAllManagedExceptions.Value;

    public static void Install()
    {
        if (!Enabled) return;
        if (System.Threading.Interlocked.Exchange(ref _installed, 1) == 1) return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log("appdomain", args.ExceptionObject as Exception);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("task", args.Exception);
            args.SetObserved();
        };

    }

    public static Exception? Swallow(Exception? exception, string source)
    {
        if (exception == null) return null;
        if (!Enabled) return exception;
        Log(source, exception);
        return null;
    }

    private static void Log(string source, Exception? ex)
    {
        if (System.Threading.Interlocked.Increment(ref _logCount) > 80) return;
        if (ex == null)
            Plugin.Log.LogWarning($"[catch-all:{source}] swallowed unknown managed exception");
        else
            Plugin.Log.LogWarning($"[catch-all:{source}] swallowed {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }

}

internal static class UnityDetailedLogBridge
{
    private static int _installed;

    public static bool Enabled =>
        Plugin.DetailedUnityLogs != null && Plugin.DetailedUnityLogs.Value;

    public static void Install()
    {
        if (!Enabled) return;
        if (System.Threading.Interlocked.Exchange(ref _installed, 1) == 1) return;

        try
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
            Plugin.Log.LogInfo("[unity-logs] enabled full stack traces for warnings/errors/exceptions");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[unity-logs] install failed: {ex.GetType().Name} {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(RRUI.Data.Components.DynamicAvatarItemImposter), "OnAvatarItemDescChange")]
internal static class DynamicAvatarItemImposterCatchAll
{
    public static bool Prepare() => GlobalManagedCatchAll.Enabled;

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception __exception)
    {
        return GlobalManagedCatchAll.Swallow(__exception, "avatar-imposter");
    }
}

[HarmonyPatch]
internal static class SchedulerManagedCatchAll
{
    public static bool Prepare() => GlobalManagedCatchAll.Enabled;

    public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        var scheduler = AccessTools.TypeByName("RecRoom.Core.Scheduler");
        if (scheduler == null) yield break;

        foreach (var name in new[]
        {
            "Update",
            "UpdateQueue",
            "LateUpdate",
            "FixedUpdate",
            "OnPostUpdate",
            "OnPreRenderUpdate",
            "OnRigidbodyExLateUpdate"
        })
        {
            var method = AccessTools.Method(scheduler, name);
            if (method != null) yield return method;
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception __exception, MethodBase __originalMethod)
    {
        return GlobalManagedCatchAll.Swallow(__exception, __originalMethod.Name);
    }
}

// ── 9. Storefront objective guard — older DorkNet deployments returned
//    objective-progress JSON from api/storefronts/v1/objectives, but the
//    March 2020 client deserializes that POST as
//    Storefronts.BalanceUpdateResponseDTO. The mismatch throws
//    KeyNotFoundException inside RecNet.Storefronts and tears down the
//    player shortly after entering RecCenter. The server endpoint is fixed
//    in this repo, but this guard keeps clients alive against stale servers.
[HarmonyPatch(typeof(RecNet.Storefronts), nameof(RecNet.Storefronts.CompleteObjectives))]
internal static class StorefrontCompleteObjectivesGuard
{
    public static bool Prepare() =>
        Plugin.SkipStorefrontObjectivePosts != null && Plugin.SkipStorefrontObjectivePosts.Value;

    public static bool Prefix()
    {
        if (Plugin.SkipStorefrontObjectivePosts == null ||
            !Plugin.SkipStorefrontObjectivePosts.Value)
            return true;

        Plugin.Log.LogInfo("[storefront-objectives] skipped client POST; server-side reward sync is disabled");
        return false;
    }
}

// ── 9b. Targeted avatar/store diagnostics — the Unity output log often
//    shows only the RRUI binding property that failed, not the RecNet DTO
//    that populated it. These patches log the exact gift/drop/unlocked item
//    descriptors the client accepted. Avoid patching tiny RRUI property
//    getters here; HarmonyX wrappers around IL2CPP getters can fault before
//    managed finalizers run.
internal static class AvatarStoreDiagnostics
{
    private const int MaxDtoLogs = 120;
    private static int _dtoLogs;

    public static bool Enabled =>
        Plugin.DetailedAvatarStoreLogs != null && Plugin.DetailedAvatarStoreLogs.Value;

    public static void LogStorefrontGiftDrop(RecNet.StorefrontGiftDrop drop, string source)
    {
        if (!Enabled || drop == null || !ShouldLogDto()) return;
        Plugin.Log.LogWarning(
            $"[diag:{source}] StorefrontGiftDrop id={Safe(() => drop.GiftDropId)} " +
            $"type={Safe(() => drop.AvatarItemType)} content={Safe(() => drop.Content)} " +
            $"rarity={Safe(() => drop.Rarity)} name='{Safe(() => drop.FriendlyName)}' " +
            $"desc='{Safe(() => drop.AvatarItemDescOrHairDyeDesc)}' " +
            $"consumable='{Safe(() => drop.ConsumableItemDesc)}' " +
            $"equipment='{Safe(() => drop.EquipmentPrefabName)}' mod='{Safe(() => drop.EquipmentModificationGuid)}'");
    }

    public static void LogUnlockedAvatarItem(RecNet.Avatars.UnlockedAvatarItem item, string source)
    {
        if (!Enabled || item == null || !ShouldLogDto()) return;
        Plugin.Log.LogWarning(
            $"[diag:{source}] UnlockedAvatarItem type={Safe(() => item.AvatarItemType)} " +
            $"platform={Safe(() => item.PlatformMask)} rarity={Safe(() => item.Rarity)} " +
            $"name='{Safe(() => item.FriendlyName)}' desc='{Safe(() => item.AvatarItemDesc)}'");
    }

    public static void LogGiftPackage(RecNet.Avatars.GiftPackage gift, string source)
    {
        if (!Enabled || gift == null || !ShouldLogDto()) return;
        Plugin.Log.LogWarning(
            $"[diag:{source}] GiftPackage id={Safe(() => gift.Id)} valid={Safe(() => gift.IsValid)} " +
            $"consumed={Safe(() => gift.Consumed)} platformOk={Safe(() => gift.SupportsCurrentPlatform)} " +
            $"type={Safe(() => gift.AvatarItemType)} context={Safe(() => gift.GiftContext)} " +
            $"rarity={Safe(() => gift.GiftRarity)} error='{Safe(() => gift.ErrorMessage)}' " +
            $"desc='{Safe(() => gift.AvatarItemDescOrHairDyeDesc)}' " +
            $"consumable='{Safe(() => gift.ConsumableItemDesc)}' " +
            $"equipment='{Safe(() => gift.EquipmentPrefabName)}' mod='{Safe(() => gift.EquipmentModificationGuid)}'");
    }

    private static bool ShouldLogDto() =>
        System.Threading.Interlocked.Increment(ref _dtoLogs) <= MaxDtoLogs;

    public static string Safe<T>(Func<T> read)
    {
        try { return read()?.ToString() ?? "<null>"; }
        catch (Exception ex) { return $"<err:{ex.GetType().Name}>"; }
    }

    public static string DescribeObject(object? value)
    {
        if (value == null) return "<null>";
        try
        {
            var text = value.ToString();
            return string.IsNullOrWhiteSpace(text)
                ? value.GetType().FullName ?? value.GetType().Name
                : text;
        }
        catch (Exception ex)
        {
            return $"<describe-err:{ex.GetType().Name}>";
        }
    }

    public static string DescribeBalance(object? value)
    {
        if (value == null) return "<null>";
        return "CurrencyType=" + Safe(() => ReadMember(value, "CurrencyType")) +
               " BalanceType=" + Safe(() => ReadMember(value, "BalanceType")) +
               " Balance=" + Safe(() => ReadMember(value, "Balance"));
    }

    public static string DescribeAvatarObject(object? value)
    {
        if (value == null) return "<null>";
        var recNet = Safe(() => InvokeNoArg(value, "ToRecNetString"));
        if (!recNet.StartsWith("<err:", StringComparison.Ordinal) &&
            recNet != "<null>" &&
            !string.IsNullOrWhiteSpace(recNet))
            return recNet;

        var type = value.GetType();
        return (type.FullName ?? type.Name) +
               " AvatarItemData=" + Safe(() => ReadMember(value, "AvatarItemData")) +
               " BodyPart=" + Safe(() => ReadMember(value, "BodyPart"));
    }

    private static object? ReadMember(object value, string name)
    {
        var type = value.GetType();
        var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null) return prop.GetValue(value);

        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) return field.GetValue(value);

        return "<missing>";
    }

    private static object? InvokeNoArg(object value, string name)
    {
        var method = value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        return method == null ? "<missing>" : method.Invoke(value, null);
    }
}

[HarmonyPatch(typeof(RecNet.StorefrontGiftDrop), nameof(RecNet.StorefrontGiftDrop.Deserialize))]
internal static class StorefrontGiftDropDiagnostics
{
    public static bool Prepare() => AvatarStoreDiagnostics.Enabled;

    public static void Postfix(RecNet.StorefrontGiftDrop __instance)
    {
        AvatarStoreDiagnostics.LogStorefrontGiftDrop(__instance, "deserialize");
    }
}

[HarmonyPatch(typeof(RecNet.Avatars.UnlockedAvatarItem), nameof(RecNet.Avatars.UnlockedAvatarItem.Deserialize))]
internal static class UnlockedAvatarItemDiagnostics
{
    public static bool Prepare() => AvatarStoreDiagnostics.Enabled;

    public static void Postfix(RecNet.Avatars.UnlockedAvatarItem __instance)
    {
        AvatarStoreDiagnostics.LogUnlockedAvatarItem(__instance, "deserialize");
    }
}

[HarmonyPatch(typeof(RecNet.Avatars.GiftPackage), nameof(RecNet.Avatars.GiftPackage.Deserialize))]
internal static class GiftPackageDiagnostics
{
    public static bool Prepare() => AvatarStoreDiagnostics.Enabled;

    public static void Postfix(RecNet.Avatars.GiftPackage __instance)
    {
        AvatarStoreDiagnostics.LogGiftPackage(__instance, "deserialize");
    }
}

[HarmonyPatch]
internal static class StorefrontBalanceDiagnostics
{
    private static int _logCount;

    public static bool Prepare() => AvatarStoreDiagnostics.Enabled;

    public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(RecNet.Storefronts)))
        {
            if (method.Name is "UpdateCachedBalance" or "UpdateCachedBalances" or
                "RaiseBalanceUpdatedEvent" or "OnStorefrontBalanceUpdated" or
                "OnStorefrontBalancePurchased" or "OnStorefrontBalanceAddReceived")
                yield return method;
        }
    }

    public static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        if (!AvatarStoreDiagnostics.Enabled || System.Threading.Interlocked.Increment(ref _logCount) > 160)
            return;

        try
        {
            Plugin.Log.LogWarning($"[diag:storefront:{__originalMethod.Name}] args={DescribeArgs(__args)}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[diag:storefront:{__originalMethod.Name}] log failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception __exception, MethodBase __originalMethod, object[] __args)
    {
        if (__exception == null) return null;
        Plugin.Log.LogWarning(
            $"[diag:storefront:{__originalMethod.Name}] exception {__exception.GetType().Name}: {__exception.Message} " +
            $"args={DescribeArgs(__args)}\n{__exception.StackTrace}");
        return GlobalManagedCatchAll.Swallow(__exception, "storefront:" + __originalMethod.Name);
    }

    private static string DescribeArgs(object[]? args)
    {
        if (args == null || args.Length == 0) return "<none>";
        var parts = new List<string>();
        for (var i = 0; i < args.Length; i++)
            parts.Add($"#{i}={DescribeArg(args[i])}");
        return string.Join("; ", parts);
    }

    private static string DescribeArg(object? arg)
    {
        if (arg == null) return "<null>";
        if (arg is System.Collections.IEnumerable seq && arg is not string)
        {
            var items = new List<string>();
            var count = 0;
            foreach (var item in seq)
            {
                if (++count <= 8) items.Add(AvatarStoreDiagnostics.DescribeBalance(item));
            }
            return $"[{string.Join(" | ", items)}] count>={count}";
        }

        var typeName = arg.GetType().FullName ?? arg.GetType().Name;
        if (typeName.Contains("Balance", StringComparison.OrdinalIgnoreCase))
            return AvatarStoreDiagnostics.DescribeBalance(arg);
        return AvatarStoreDiagnostics.DescribeObject(arg);
    }
}

[HarmonyPatch]
internal static class PlayerAvatarOutfitDiagnostics
{
    private static int _logCount;

    public static bool Prepare() => AvatarStoreDiagnostics.Enabled;

    public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(PlayerAvatar)))
        {
            if (method.Name is "ApplyLocalPlayerAvatarSettings" or "EquipOutfitItem" or
                "EquipOutfitItemInternal" or "SetLocalPlayerSelectedOutfit" or
                "LocalLoadSavedOutfit" or "UnequipOutfitItem" or "OnPlayerOutfitSelectionsChanged")
                yield return method;
        }
    }

    public static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        if (!AvatarStoreDiagnostics.Enabled || System.Threading.Interlocked.Increment(ref _logCount) > 140)
            return;

        try
        {
            Plugin.Log.LogWarning($"[diag:outfit:{__originalMethod.Name}] args={DescribeArgs(__args)}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[diag:outfit:{__originalMethod.Name}] log failed: {ex.GetType().Name} {ex.Message}");
        }
    }

    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception __exception, MethodBase __originalMethod, object[] __args)
    {
        if (__exception == null) return null;
        Plugin.Log.LogWarning(
            $"[diag:outfit:{__originalMethod.Name}] exception {__exception.GetType().Name}: {__exception.Message} " +
            $"args={DescribeArgs(__args)}\n{__exception.StackTrace}");
        return GlobalManagedCatchAll.Swallow(__exception, "outfit:" + __originalMethod.Name);
    }

    private static string DescribeArgs(object[]? args)
    {
        if (args == null || args.Length == 0) return "<none>";
        var parts = new List<string>();
        for (var i = 0; i < args.Length; i++)
            parts.Add($"#{i}={DescribeArg(args[i])}");
        return string.Join("; ", parts);
    }

    private static string DescribeArg(object? arg)
    {
        if (arg == null) return "<null>";
        if (arg is string s) return s;
        if (arg is System.Collections.IEnumerable seq)
        {
            var items = new List<string>();
            var count = 0;
            foreach (var item in seq)
            {
                if (++count <= 8) items.Add(AvatarStoreDiagnostics.DescribeAvatarObject(item));
            }
            return $"[{string.Join(" | ", items)}] count>={count}";
        }

        var typeName = arg.GetType().FullName ?? arg.GetType().Name;
        if (typeName.Contains("Avatar", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Outfit", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("BodyPart", StringComparison.OrdinalIgnoreCase))
            return AvatarStoreDiagnostics.DescribeAvatarObject(arg);
        return AvatarStoreDiagnostics.DescribeObject(arg);
    }
}

// ── 10. Mirror outfit guard -- the March 2020 mirror UI can try to remove
//    the required torso item when clicking the currently equipped shirt. The
//    client then force-adds a default torso ("Adding required torso item!") and
//    the local player teardown follows shortly after. Treat a direct torso
//    unequip as a no-op; normal shirt swaps still go through EquipOutfitItem.
[HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.UnequipOutfitItem))]
internal static class RequiredTorsoUnequipGuard
{
    private const int TorsoBodyPart = 1;

    public static bool Prepare() =>
        Plugin.BlockDirectTorsoUnequip != null && Plugin.BlockDirectTorsoUnequip.Value;

    public static bool Prefix(object[] __args, ref bool __result)
    {
        if (Plugin.BlockDirectTorsoUnequip == null ||
            !Plugin.BlockDirectTorsoUnequip.Value)
            return true;

        if (TryReadBodyPart(__args, out var bodyPart) && bodyPart == TorsoBodyPart)
        {
            Plugin.Log.LogInfo("[avatar-outfit] blocked direct torso unequip from mirror UI");
            __result = false;
            return false;
        }

        return true;
    }

    private static bool TryReadBodyPart(object[]? args, out int bodyPart)
    {
        bodyPart = int.MinValue;
        if (args is not { Length: > 1 } || args[1] is null) return false;

        try
        {
            bodyPart = Convert.ToInt32(args[1]);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            var text = args[1].ToString();
            if (text?.EndsWith("Torso", StringComparison.OrdinalIgnoreCase) == true)
            {
                bodyPart = TorsoBodyPart;
                return true;
            }

            if (int.TryParse(text, out bodyPart))
                return true;

            Plugin.Log.LogDebug($"[avatar-outfit] could not decode bodyPart argument: {text}");
            return false;
        }
    }
}

// ── 11. Screen cleanup guard — the March 2020 desktop HUD can throw a
//    NullReferenceException while tearing down the local player if the
//    ScreenHUD/cursor objects are already gone. That exception appears in the
//    current crash path after the local player is destroyed. Suppress only this
//    cleanup exception so we can keep the process alive long enough to expose
//    the real disconnect/crash trigger in the logs.
[HarmonyPatch(typeof(RecRoom.ScreenPlayerController), "CleanupLocalPlayer")]
internal static class ScreenPlayerCleanupGuard
{
    public static bool Prepare() =>
        Plugin.SuppressScreenCleanupNullRef != null && Plugin.SuppressScreenCleanupNullRef.Value;

    public static Exception? Finalizer(Exception __exception)
    {
        if (__exception == null) return null;
        if (Plugin.SuppressScreenCleanupNullRef == null ||
            !Plugin.SuppressScreenCleanupNullRef.Value)
            return __exception;

        if (IsCleanupHudNullReference(__exception))
        {
            Plugin.Log.LogWarning("[screen-cleanup] swallowed CleanupLocalPlayer null-ref during local player teardown");
            return null;
        }
        return __exception;
    }

    private static bool IsCleanupHudNullReference(Exception exception)
    {
        if (exception is NullReferenceException) return true;

        // IL2CPP exceptions arrive through Harmony as Il2CppException wrappers,
        // so match the IL2CPP stack text rather than only the managed type.
        var text = exception.ToString();
        return text.Contains("System.NullReferenceException", StringComparison.Ordinal)
            && text.Contains("RecRoom.ScreenPlayerController.CleanupLocalPlayer", StringComparison.Ordinal)
            && text.Contains("RecRoom.UI.ScreenHUDElement.get_RectTransform", StringComparison.Ordinal);
    }
}

// ── 12. Diagnostics — log every BootSequence.RegisterError invocation
//    with a managed-side stack trace so we can tell which API call
//    actually failed (the IL2CPP stack traces in Player.log are
//    obfuscated to method symbols like "RKHMMYKOXZK"). The watch's
//    boot pipeline collapses any failure into "Rec Room update
//    required", which is useless without knowing the actual cause.
[HarmonyPatch(typeof(BootSequence), "RegisterError")]
internal static class BootSequenceRegisterErrorTrace
{
    public static bool Prepare() =>
        Plugin.EnableBootDiagnostics != null && Plugin.EnableBootDiagnostics.Value;

    // The original Prefix(string error) was emitting blank text — Harmony's
    // IL2CPP string marshaling for an instance-method first parameter was
    // dropping the string value (every entry came out as "[boot-error] " in
    // the log). Use HarmonyX's __args to pull the raw arguments and decode
    // them manually, plus dump the actual call site so we know which of
    // the two known callers
    //   - "Error initializing Core Systems"       (BootSequence:5555)
    //   - "Unable to connect to RecNet game servers" (BootSequence:10490)
    // fired.
    public static void Prefix(object[] __args, BootSequence __instance)
    {
        string? err = null;
        try
        {
            if (__args is { Length: > 0 } && __args[0] is { } a0)
            {
                err = a0 switch
                {
                    string s => s,
                    Il2CppSystem.String i2cs => i2cs.ToString(),
                    _ => a0.ToString(),
                };
            }
        }
        catch (Exception ex) { err = $"<unmarshalled: {ex.GetType().Name}>"; }
        Plugin.Log.LogWarning($"[boot-error] '{err ?? "<null>"}'");
        try
        {
            Plugin.Log.LogWarning(new System.Diagnostics.StackTrace(1, true).ToString());
        }
        catch { /* StackTrace can fail in IL2CPP; swallow */ }
    }
}

internal static class MatchmakingHelpers
{
    /// <summary>Reads the watch's persistent LoginLock from PlayerPrefs.
    /// The key is <c>LoginLockTokenV2</c> (verified at
    /// Cpp2IL_ISIL/.../RecNet/Matchmaking.txt:1810 — the
    /// <c>Move rdi, "LoginLockTokenV2"</c> + <c>Call PlayerPrefs.GetString</c>
    /// pair inside <c>Matchmaking.LoadLockToken</c>). Stored value is a
    /// JSON object of <c>RecNet.Matchmaking+LoginLockTokenParams</c>
    /// (Cpp2IL_CS/.../RecNet/Matchmaking.cs:129) with three fields:
    /// <c>Platform</c> (int), <c>PlatformId</c> (string), <c>LockToken</c>
    /// (string). We extract <c>LockToken</c> by scanning for the literal
    /// <c>"LockToken":"…"</c> pair — cheaper than parsing JSON for one
    /// string, and the watch's serialiser doesn't produce escapes that'd
    /// break a naive scan.
    /// </summary>
    public static string? GetCurrentLockToken()
    {
        try
        {
            var raw = PlayerPrefs.GetString("LoginLockTokenV2", string.Empty);
            if (string.IsNullOrEmpty(raw)) return null;
            const string key = "\"LockToken\":\"";
            var ix = raw.IndexOf(key, StringComparison.Ordinal);
            if (ix < 0) return null;
            ix += key.Length;
            var end = raw.IndexOf('"', ix);
            return end < 0 ? null : raw[ix..end];
        }
        catch { return null; }
    }
}
