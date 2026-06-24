// DorkNet client mod (MelonLoader IL2CPP). Applies the minimum set of
// Harmony patches needed for the 2020 watch to connect to a DorkNet
// server:
//
//   1. URI rewrite      — `.rec.net` → user-configured host
//   2. TLS trust bypass — make BouncyCastle never reject server certs
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
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
    internal static string? DiagnosticsLogPath;
    private static readonly Dictionary<string, Type> ResolvedTypeCache = new();
    private readonly HashSet<string> _diagnosticPatchLabels = new();
    private bool _networkPatchesRegistered;
    private bool _joinTracePatchesRegistered;
    private bool _quitTraceCoreRegistered;
    private bool _quitTraceGameComplete;
    private int _quitTraceRetryFrame;
    private bool _antiTamperCallbackPatchesComplete;
    private int _antiTamperCallbackRetryFrame;
    private bool _toxModPatchesComplete;
    private int _toxModRetryFrame;
    private bool _diagnosticCoreRegistered;
    private bool _diagnosticGameComplete;
    private bool _firstUpdateLogged;
    private bool _processExceptionHandlersRegistered;

    // Config backed by a plain JSON file under MelonLoader/UserData.
    // The defaults below match the recommended install, so dropping this
    // mod in without writing a config file still gives working behaviour.
    public static class Cfg
    {
        public static string ServerHost          = "localhost";
        public static bool   EnableTlsTrustBypass = true;
        // Log-only trace for the stuck first-dorm "Change Username?"
        // dialog. When true, RegistrationPatches hooks the dorm
        // registration-prompt flow + the confirm-dialog buttons and writes
        // what fires to MelonLoader/UserData/dorknet-diagnostics.log, so a
        // brand-new account that reproduces the freeze tells us exactly
        // where it breaks (does the click reach Button_Affirmative? does
        // the prompt even run?). No behaviour change — purely diagnostic.
        // See RegistrationPatches and the registration site in
        // OnLateInitializeMelon.
        public static bool   TraceRegistrationDialog = true;
        // Force-enable the client's built-in RecRoom.Debugging.DebugConsole
        // (normally dev-gated) and silence CheatManager's tamper detectors
        // so movement/time commands (SetTimeScale, Fly, Teleport, …) don't
        // drop you to the dorm. Default OFF — purely opt-in. The toggle key
        // is a UnityEngine.KeyCode name; BackQuote is the `~` key.
        public static bool   EnableDebugConsole = false;
        public static string DebugConsoleToggleKey = "BackQuote";

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

        // One-shot dev-UI diagnostic (no behaviour change). When true,
        // ~5s after load the mod logs whether the watch's native developer
        // menu content actually exists in this build (HomeScreenFlow.
        // devMenuPanel / devButtonPrefab present vs NULL), the dev-flow list
        // counts, and whether RecRoom.Debugging.DebugConsole is in the scene
        // + whether its static Execute() command path works. This tells us
        // stripped-vs-gated so we know whether to un-gate the game's own dev
        // menu or build a custom watch entry. Output goes to the MelonLoader
        // console + dorknet-diagnostics.log.
        public static bool   DiagnoseDevMenu = false;

        // Inject a "Dev Console" button into the watch's native dev-menu
        // panel (HomeScreenFlow.devMenuPanel) by cloning the game's own
        // devButtonPrefab. The panel/prefab exist in this build (just gated
        // off), so we instantiate + activate + label, then wire its click to
        // run console commands. Native Button3D → works in VR and Screen Mode.
        public static bool   EnableDevWatchButton = false;
        // Preset dev commands shown as watch buttons (when EnableDevWatchButton
        // is on). Each entry is "Label=command"; clicking runs
        // DebugConsole.Execute(command). Edit to match this build's actual
        // DebugConsoleCommandConfig command names/args.
        public static string[] DevCommands = { "Help=help", "Fly=fly", "NoClip=noclip" };

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
        Log.Msg("=== DorkNet ClientMod loading ===");
        LoadConfig();
        RegisterNetworkPatches();
    }

    public override void OnLateInitializeMelon()
    {
        Log.Msg("=== Registering client patches ===");
        RegisterNetworkPatches();
        TryPatchByName("RecRoom.AntiCheat.EACManager",
                       "GenerateChallengeResponse",
                       args: new[] { typeof(string) },
                       prefix: nameof(AntiCheatPatches.GenerateChallengeResponse_Prefix));
        TryPatchByName("LBNJFPOLCDL", "JHLKGBGJKKK",
                       args: new[] { typeof(string), typeof(string), typeof(string) },
                       prefix: nameof(AuthPatches.CaptureTokenArgs_Prefix));
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
        RegisterQuitTracePatches(logMisses: true);
        RegisterAntiTamperCallbackPatches(logMisses: true);
        RegisterToxModPatches(logMisses: true);
        PatchFileHashCheckerCallback();
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

        // Desktop Screen Sharing FPS override. Only register when a target
        // is set; the postfix on the gadget's Awake rewrites the baked
        // refresh-frequency field (and, if enabled, raises the Photon
        // serialization rate so the higher capture rate actually transmits).
        if (Cfg.DesktopScreenShareFps > 0f)
        {
            TryPatchByName("RecRoom.Tools.Productivity.DesktopScreenSharingDisplay",
                           "Awake", args: Type.EmptyTypes,
                           postfix: nameof(ScreenSharePatches.DisplayAwake_Postfix));
            Log.Msg($"[screenshare] override armed: target {Cfg.DesktopScreenShareFps} fps " +
                    $"(raisePhotonRate={Cfg.DesktopScreenShareRaisePhotonRate})");
        }

        TryPatchByName("RecRoom.Tools.MakerPenVisuals",
                       "set_LaserPointerEnabled",
                       args: new[] { typeof(bool) },
                       prefix: nameof(MakerPenGiftPreviewPatches.LaserPointerEnabled_Prefix));

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
            Log.Msg($"[questsize] armed: target {Cfg.QuestMaxTeamSize} for [{string.Join(", ", Cfg.QuestMaxTeamSizeRooms)}]");
        }
        Log.Msg("=== Client patches registered ===");
    }

    // ── Room-join + watch-button trace ────────────────────────────────
    // Traces the full "press the Dorm Room button → join the room" chain
    // so a stuck join (e.g. the watch's Dorm Room button doing nothing)
    // is debuggable from the log alone. The chain, verified against the
    // 2020.12.18 IL2CPP dump:
    //
    //   AGUI.StackedUI.HomeScreenFlow.Button_DormRoom()      (button)
    //     → SessionManager.RunJoinRoom("DormRoom", …)        (join entry)
    //        → OJMCBOKJFOF.NHBPIIGDAJP(name)  →  GET /goto/room/{name}
    //           rejects with "No such room"  (OJMCBOKJFOF.txt:958-1019)
    //        → .Then(SessionManager.RunJoinRoom(RoomInstance, …))
    //        → on reject: SessionManager+<>c.<RunJoinRoom>b__165_1(error)
    //           → NotificationManager.Play("An error occured", "…happyfox…")
    //              (the "contact recroom.happyfox.com" toast)
    //
    // We can't express the obfuscated RunJoinRoom param types as a Type[]
    // for AccessTools, so every RunJoinRoom overload + every /goto overload
    // is patched by iterating declared methods by name, and the prefixes
    // read arguments generically via Harmony's `object[] __args`.
    private void RegisterJoinTracePatches()
    {
        if (_joinTracePatchesRegistered) return;
        Log.Msg("=== Registering room-join trace ===");

        // Watch "Dorm Room" button. Void, no params, unique name —
        // HomeScreenFlow.txt:4791, calls RunJoinRoom("DormRoom",…) at :4848.
        TryPatchByName("AGUI.StackedUI.HomeScreenFlow", "Button_DormRoom",
                       args: Type.EmptyTypes,
                       prefix: nameof(JoinPatches.ButtonDormRoom_Prefix));

        // Every SessionManager.RunJoinRoom overload (string-name entry +
        // the RoomInstance inner legs). Logs the method + its arguments.
        PatchAllOverloads("SessionManager", "RunJoinRoom",
                          nameof(JoinPatches.RunJoinRoom_Prefix));

        // The /goto/room/{name|id} promise builders — both the string
        // (room-name) and Int64 (room-id) overloads of NHBPIIGDAJP.
        PatchAllOverloads("OJMCBOKJFOF", "NHBPIIGDAJP",
                          nameof(JoinPatches.Goto_Prefix));

        // The RunJoinRoom promise-chain callbacks compiled onto the
        // SessionManager <>c display class — most importantly the reject
        // handler b__165_1(string error) that raises the happyfox toast.
        // Match by the (de-mangled) substring "RunJoinRoom" + the lambda
        // marker "b__" so we hit the callbacks but not the RunJoinRoom
        // state-machine MoveNext.
        PatchNestedLambdas("SessionManager", "RunJoinRoom",
                           nameof(JoinPatches.JoinCallback_Prefix));

        // Additionally capture the REJECT REASON string on b__165_1 (the
        // happyfox-toast handler). Matches only that one lambda; it ends
        // up with two prefixes (flow + error string), which is harmless.
        PatchNestedLambdas("SessionManager", "RunJoinRoom_b__165_1",
                           nameof(JoinPatches.JoinError_Prefix));

        _joinTracePatchesRegistered = true;
        Log.Msg("=== Room-join trace registered ===");
    }

    // Patch every declared method on `typeName` named `methodName`,
    // regardless of signature, with a single prefix. Used for obfuscated
    // overload sets whose param types we can't name in a Type[].
    private bool PatchAllOverloads(string typeName, string methodName, string prefix, bool logMiss = true)
    {
        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {typeName}.{methodName}(*): type not found");
            return false;
        }
        var prefixMethod = GetPatchMethod(prefix);
        var patched = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != methodName) continue;
            if (method.IsAbstract || method.ContainsGenericParameters) continue;
            try
            {
                HarmonyInstance.Patch(method, prefix: new HarmonyMethod(prefixMethod));
                patched++;
            }
            catch (Exception ex)
            {
                Log.Warning($"[patch-fail] {typeName}.{methodName} overload: {ex.Message}");
            }
        }
        if (patched == 0)
        {
            if (logMiss) Log.Warning($"[patch-miss] {typeName}.{methodName}(*): no overloads matched");
            return false;
        }
        Log.Msg($"[patch-ok] {typeName}.{methodName}(*) x{patched}");
        return true;
    }

    // Patch compiler-generated lambda methods (names containing "b__")
    // on the nested display classes of `typeName` whose (de-mangled) name
    // also contains `nameContains`. Il2CppInterop mangles the angle
    // brackets of `<RunJoinRoom>b__165_1` to `_003C…_003E` but leaves the
    // method name itself intact, so a plain substring match still works.
    private bool PatchNestedLambdas(string typeName, string nameContains, string prefix, bool logMiss = true)
    {
        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {typeName}+<lambdas>({nameContains}): type not found");
            return false;
        }
        var prefixMethod = GetPatchMethod(prefix);
        var patched = 0;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var method in AccessTools.GetDeclaredMethods(nested))
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                if (method.Name.IndexOf("b__", StringComparison.Ordinal) < 0) continue;
                if (method.Name.IndexOf(nameContains, StringComparison.Ordinal) < 0) continue;
                try
                {
                    HarmonyInstance.Patch(method, prefix: new HarmonyMethod(prefixMethod));
                    patched++;
                    Log.Msg($"[patch-ok] {typeName}+{nested.Name}.{method.Name}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[patch-fail] {typeName}+{nested.Name}.{method.Name}: {ex.Message}");
                }
            }
        }
        if (patched == 0 && logMiss)
            Log.Warning($"[patch-miss] {typeName}+<lambdas>({nameContains}): none matched");
        return patched > 0;
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
                        Log.Msg($"[patch-ok] file hash checker callback {type.FullName}.{method.Name}");
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
        complete &= TryPatchDiagnosticByName("ToxMod.ToxModVoiceComponent",
                                             "PhotonVoiceCreated",
                                             prefix: nameof(ToxModPatches.PhotonVoiceCreated_Prefix),
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
                        Log.Msg($"[patch-ok] {label} {type.FullName}.{method.Name}");
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
        complete &= TryPatchHttpRequestUriConstructors();
        TryPatchByName("BestHTTP.HTTPRequest", "set_Uri",
                 args: new[] { typeof(Uri) },
                 prefix: nameof(UriPatches.HttpRequestSetUri_Prefix));
        TryPatchByName("BestHTTP.HTTPRequest", "PrepareUri",
                 args: new[] { typeof(Uri) },
                 prefix: nameof(UriPatches.HttpRequestPrepareUri_Prefix));
        complete &= TryPatchByName("BestHTTP.HTTPRequest", "Send",
                 prefix: nameof(UriPatches.HttpRequestSend_Prefix));
        complete &= TryPatchHttpManagerStringSendRequestOverloads();
        complete &= TryPatchByName("BestHTTP.HTTPManager", "SendRequest",
                 args: new[] { ResolveType("BestHTTP.HTTPRequest") ?? typeof(object) },
                 prefix: nameof(UriPatches.HttpManagerSendRequestObject_Prefix));

        _networkPatchesRegistered = complete;
    }

    private bool TryPatchHttpRequestUriConstructors()
    {
        var type = ResolveType("BestHTTP.HTTPRequest");
        if (type is null)
        {
            Log.Warning("[patch-miss] BestHTTP.HTTPRequest..ctor(Uri): type not found");
            return false;
        }

        var prefix = GetPatchMethod(nameof(UriPatches.HttpRequestCtorUri_Prefix));
        var patched = 0;
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length == 0 || !IsUriType(parameters[0].ParameterType)) continue;

            try
            {
                HarmonyInstance.Patch(ctor, prefix: new HarmonyMethod(prefix));
                patched++;
            }
            catch (Exception ex)
            {
                Log.Warning($"[patch-fail] BestHTTP.HTTPRequest..ctor({FormatParameterTypes(parameters)}): {ex.Message}");
            }
        }

        if (patched == 0)
        {
            Log.Warning("[patch-miss] BestHTTP.HTTPRequest..ctor(Uri)");
            return false;
        }

        Log.Msg($"[patch-ok] BestHTTP.HTTPRequest..ctor(Uri first) overloads x{patched}");
        return true;
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

    private static bool IsUriType(Type type)
    {
        return type == typeof(Uri) || type.FullName == typeof(Uri).FullName || type.Name == nameof(Uri);
    }

    private static string FormatParameterTypes(ParameterInfo[] parameters)
    {
        var names = new List<string>();
        foreach (var parameter in parameters)
            names.Add(parameter.ParameterType.Name);
        return string.Join(",", names);
    }

    public override void OnUpdate()
    {
        if (!_firstUpdateLogged)
        {
            _firstUpdateLogged = true;
            Log.Msg("[lifecycle] first OnUpdate tick (Unity frame loop is live)");
        }

        if (!_quitTraceGameComplete && ++_quitTraceRetryFrame >= 300)
        {
            _quitTraceRetryFrame = 0;
            RegisterQuitTracePatches(logMisses: false);
        }

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
            DiagnosticsLogPath = Path.Combine(userData, "dorknet-diagnostics.log");
            var path = Path.Combine(userData, "dorknet-clientmod.json");
            if (!File.Exists(path))
            {
                Log.Msg($"[config] no file at {path}; using defaults (ServerHost={Cfg.ServerHost})");
                return;
            }
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (TryGetConfigValue(r, "ServerHost", out var v))           Cfg.ServerHost = v.GetString() ?? Cfg.ServerHost;
            if (TryGetConfigValue(r, "EnableTlsTrustBypass", out v))     Cfg.EnableTlsTrustBypass = v.GetBoolean();
            if (TryGetConfigValue(r, "TraceRegistrationDialog", out v))  Cfg.TraceRegistrationDialog = v.GetBoolean();
            if (TryGetConfigValue(r, "EnableDebugConsole", out v))       Cfg.EnableDebugConsole = v.GetBoolean();
            if (TryGetConfigValue(r, "DebugConsoleToggleKey", out v))    Cfg.DebugConsoleToggleKey = v.GetString() ?? Cfg.DebugConsoleToggleKey;
            if (TryGetConfigValue(r, "DesktopScreenShareFps", out v))             Cfg.DesktopScreenShareFps = (float)v.GetDouble();
            if (TryGetConfigValue(r, "DesktopScreenShareRaisePhotonRate", out v)) Cfg.DesktopScreenShareRaisePhotonRate = v.GetBoolean();
            if (TryGetConfigValue(r, "DesktopScreenShareResolution", out v))      Cfg.DesktopScreenShareResolution = v.GetInt32();
            if (TryGetConfigValue(r, "DesktopScreenShareQuality", out v))         Cfg.DesktopScreenShareQuality = v.GetInt32();
            if (TryGetConfigValue(r, "DiagnoseDevMenu", out v))                   Cfg.DiagnoseDevMenu = v.GetBoolean();
            if (TryGetConfigValue(r, "QuestMaxTeamSize", out v))                  Cfg.QuestMaxTeamSize = v.GetInt32();
            if (TryGetConfigValue(r, "QuestMaxTeamSizeRooms", out v) && v.ValueKind == JsonValueKind.Array)
            {
                var rooms = new List<string>();
                foreach (var e in v.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                        rooms.Add(e.GetString()!);
                Cfg.QuestMaxTeamSizeRooms = rooms.ToArray(); // explicit (incl. empty = none)
            }
            if (TryGetConfigValue(r, "EnableDevWatchButton", out v))          Cfg.EnableDevWatchButton = v.GetBoolean();
            if (TryGetConfigValue(r, "DevCommands", out v) && v.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var e in v.EnumerateArray())
                {
                    var s = e.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                }
                if (list.Count > 0) Cfg.DevCommands = list.ToArray();
            }
            Log.Msg($"[config] loaded from {path}: ServerHost={Cfg.ServerHost}, " +
                    $"EnableTlsTrustBypass={Cfg.EnableTlsTrustBypass}");
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

    // ── Patch dispatch helpers ────────────────────────────────────────
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
        return TryPatch(label, type, methodName, args, prefix, postfix, logMiss);
    }

    private bool TryPatchDeclaredMethodByReturnType(string typeName, string returnTypeName,
                                                    string fallbackMethodName,
                                                    string? prefix = null, string? postfix = null,
                                                    bool logMiss = true)
    {
        var type = ResolveType(typeName);
        var label = $"{typeName}.<returns {returnTypeName}>";
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}: type not found");
            return TryPatchByName(typeName, fallbackMethodName, args: null, prefix, postfix, logMiss);
        }

        var matches = new List<MethodInfo>();
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.IsAbstract || method.ContainsGenericParameters || method.IsSpecialName) continue;
            if (ReturnTypeMatches(method.ReturnType, returnTypeName)) matches.Add(method);
        }

        if (matches.Count == 1)
        {
            var method = matches[0];
            try
            {
                HarmonyInstance.Patch(method,
                    prefix:  prefix  is null ? null : new HarmonyMethod(GetPatchMethod(prefix)),
                    postfix: postfix is null ? null : new HarmonyMethod(GetPatchMethod(postfix)));
                Log.Msg($"[patch-ok] {typeName}.{method.Name} (returns {returnTypeName})");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[patch-fail] {typeName}.{method.Name} (returns {returnTypeName}): {ex.Message}");
                return false;
            }
        }

        if (matches.Count > 1)
        {
            var names = new List<string>();
            foreach (var method in matches) names.Add(method.Name);
            Log.Warning($"[patch-ambiguous] {label}: {string.Join(", ", names)}; falling back to {fallbackMethodName}");
        }
        else if (logMiss)
        {
            Log.Warning($"[patch-miss] {label}; falling back to {fallbackMethodName}");
        }

        return TryPatchByName(typeName, fallbackMethodName, args: null, prefix, postfix, logMiss);
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

    private static bool ReturnTypeMatches(Type type, string requested)
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
        // Look in all three patch holder classes — small enough that a
        // linear scan is cheaper than per-class lookups.
        foreach (var holder in new[] { typeof(UriPatches), typeof(HttpTracePatches), typeof(AuthPatches), typeof(AntiCheatPatches), typeof(AntiTamperPatches), typeof(FileHashCheckerPatches), typeof(ToxModPatches), typeof(TlsPatches), typeof(QuitTracePatches), typeof(DiagnosticPatches), typeof(RoomLoadDiagnostics), typeof(SavePatches), typeof(ChatPatches), typeof(JoinPatches), typeof(RegistrationPatches), typeof(DebugConsolePatches), typeof(ScreenSharePatches), typeof(MakerPenGiftPreviewPatches), typeof(QuestTeamSize) })
        {
            var m = holder.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (m is not null) return m;
        }
        throw new MissingMethodException($"patch method '{name}' not found on any holder");
    }

    private void RegisterQuitTracePatches(bool logMisses)
    {
        if (!_quitTraceCoreRegistered)
        {
            TryPatchByName("UnityEngine.Application", "Quit", Type.EmptyTypes,
                           prefix: nameof(QuitTracePatches.ApplicationQuit_Prefix),
                           logMiss: logMisses);
            TryPatchByName("UnityEngine.Application", "Quit", new[] { typeof(int) },
                           prefix: nameof(QuitTracePatches.ApplicationQuitInt_Prefix),
                           logMiss: logMisses);
            _quitTraceCoreRegistered = true;
        }

        var complete = true;
        complete &= TryPatchDiagnosticByName("SessionManager", "TryApplicationQuit",
                                             prefix: nameof(QuitTracePatches.SessionManagerTryApplicationQuit_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("SessionManager", "TryApplicationQuit",
                                             args: new[] { typeof(int) },
                                             prefix: nameof(QuitTracePatches.SessionManagerTryApplicationQuitInt_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("SessionManager", "FatalApplicationQuit",
                                             args: new[] { typeof(int), typeof(string) },
                                             prefix: nameof(QuitTracePatches.SessionManagerFatalApplicationQuit_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("OPEOLPDLEBO", "GDNAOLMIDLM",
                                             prefix: nameof(QuitTracePatches.ApplicationWantsToQuit_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("OPEOLPDLEBO", "DGHJGMBNHCL",
                                             prefix: nameof(QuitTracePatches.CreateExitTask_Prefix),
                                             logMiss: logMisses);

        _quitTraceGameComplete = complete;
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
        // CheatManager fires these when its anti-cheat heuristics trip —
        // logging them surfaces "client thinks it's been tampered with",
        // a common cause of silent dorm-drops / kicks on a modded client.
        // Names verified in CheatManager.txt for 2020.12.18; the older
        // build's "AnalyticsHelper.<X>Cheat" names don't exist here, and
        // there's no DeveloperFlag/StreamingAsset callback nor a
        // BootSequence.RegisterError sink in this build — those legacy
        // hooks were removed (they only ever logged [patch-miss] forever).
        foreach (var method in new[]
        {
            "OnTimeCheatDetected",
            "OnObscuredTypeCheatDetected",
            "OnHeightCheatDetected",
            "OnAdvancedMovementCheatDetected",
        })
        {
            TryPatchDiagnosticByName("CheatManager", method,
                                      prefix: nameof(DiagnosticPatches.AnalyticsCheat_Prefix),
                                      logMiss: logMisses);
        }

        TryPatchDiagnosticByName("SessionManager", "TryApplicationQuit",
                                 prefix: nameof(DiagnosticPatches.SessionManagerTryApplicationQuit_Prefix),
                                 logMiss: logMisses);
        TryPatchDiagnosticByName("SessionManager", "TryApplicationQuit",
                                 args: new[] { typeof(int) },
                                 prefix: nameof(DiagnosticPatches.SessionManagerTryApplicationQuitInt_Prefix),
                                 logMiss: logMisses);
        TryPatchDiagnosticByName("SessionManager", "FatalApplicationQuit",
                                 args: new[] { typeof(int), typeof(string) },
                                 prefix: nameof(DiagnosticPatches.SessionManagerFatalApplicationQuit_Prefix),
                                 logMiss: logMisses);
        RegisterDeepRoomDiagnostics(logMisses);
        // SteamManager.Awake + SteamPlatformManager.*LoginInitialize were
        // pure-logging diagnostics that fired during Unity's very first
        // wake-up tick. On december (EAC build, IL2CPP wrapped Steam
        // methods) MonoMod's lazy CompileMethodHook on the trampoline
        // crashes the CLR with 0x80131506 the instant Awake JITs. The
        // hooks gave us no behavioral leverage — only a `[call] …` log
        // line — so they're disabled. If anyone needs Steam-side visibility
        // later, route it through the game's own log lines instead.
        return true;
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

    private bool TryPatchDiagnosticAllOverloads(string typeName, string methodName,
                                                string? prefix = null, string? postfix = null,
                                                string? finalizer = null, bool logMiss = true)
    {
        var label = $"{typeName}.{methodName}(*)";
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}: type not found");
            return false;
        }

        var patched = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != methodName) continue;
            if (method.IsAbstract || method.ContainsGenericParameters) continue;
            try
            {
                HarmonyInstance.Patch(method,
                    prefix: prefix is null ? null : new HarmonyMethod(GetPatchMethod(prefix)),
                    postfix: postfix is null ? null : new HarmonyMethod(GetPatchMethod(postfix)),
                    finalizer: finalizer is null ? null : new HarmonyMethod(GetPatchMethod(finalizer)));
                patched++;
            }
            catch (Exception ex)
            {
                Log.Warning($"[patch-fail] {type.FullName}.{method.Name}: {ex.Message}");
            }
        }

        if (patched == 0)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}: no overloads matched");
            return false;
        }

        _diagnosticPatchLabels.Add(label);
        Log.Msg($"[patch-ok] {label} x{patched}");
        return true;
    }

    private bool RegisterDeepRoomDiagnostics(bool logMisses)
    {
        var complete = true;

        complete &= TryPatchDiagnosticByName("UnityEngine.Debug", "LogError",
                                             args: new[] { typeof(object) },
                                             prefix: nameof(RoomLoadDiagnostics.LogError_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("UnityEngine.Debug", "LogException",
                                             args: new[] { typeof(Exception) },
                                             prefix: nameof(RoomLoadDiagnostics.LogException_Prefix),
                                             logMiss: logMisses);

        foreach (var method in new[]
        {
            "ConnectUsingSettings",
            "ConnectToRegion",
            "JoinRoom",
            "JoinOrCreateRoom",
            "CreateRoom",
            "LeaveRoom",
            "Disconnect",
        })
        {
            complete &= TryPatchDiagnosticAllOverloads("PhotonNetwork", method,
                                                       prefix: nameof(RoomLoadDiagnostics.NamedCall_Prefix),
                                                       finalizer: nameof(RoomLoadDiagnostics.NamedCall_Finalizer),
                                                       logMiss: logMisses);
        }

        foreach (var item in new (string Type, string Method)[]
        {
            ("CMMIJKCDKPL", "CDNPLAGEDBK"),
            ("KGHEDLNPANK", "CDNPLAGEDBK"),
            ("HMPNINFEDFB", "IJIOJAIBPKE"),
            ("PhotonNetwork", "Disconnect"),
        })
        {
            complete &= TryPatchDiagnosticAllOverloads(item.Type, item.Method,
                                                       prefix: nameof(RoomLoadDiagnostics.NamedCall_Prefix),
                                                       finalizer: nameof(RoomLoadDiagnostics.NamedCall_Finalizer),
                                                       logMiss: logMisses);
        }

        complete &= PatchKnownRoomLoadMoveNext(logMisses);
        return true;
    }

    private bool PatchKnownRoomLoadMoveNext(bool logMiss)
    {
        const string label = "room-load-state-machines.MoveNext";
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        var nameNeedles = new[]
        {
            "OMJKHJLFOCO", "AFMPFLKGABO",
            "AOEBBIFGIKI", "FINJCCNPHEP", "FDDFOKNJIFL", "GMNHJNAHAAI",
            "MHDOLJJPEAD", "LFGLDJCMJDN", "PFDIKJHMBDI", "ICAGCHKPIOM",
            "JEBACILNLJI", "DINBAFNHLPJ", "HLHPNABABME", "PAPLNIPKAMG",
            "IBEOONPEELF", "JHPHMNGOFLB", "NLDBPDCNNCF", "LONOBOBHMJL",
            "BNDIAONDFFF", "HOONLPFFADG", "PHODPEHLHGM",
        };

        var patched = 0;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in GetLoadableTypes(asm))
            {
                var fullName = type.FullName ?? type.Name;
                var match = false;
                foreach (var needle in nameNeedles)
                {
                    if (fullName.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    {
                        match = true;
                        break;
                    }
                }
                if (!match) continue;

                IEnumerable<MethodInfo> methods;
                try { methods = AccessTools.GetDeclaredMethods(type); }
                catch { continue; }

                foreach (var method in methods)
                {
                    if (method.Name != "MoveNext") continue;
                    if (method.IsAbstract || method.ContainsGenericParameters) continue;
                    try
                    {
                        HarmonyInstance.Patch(method,
                            finalizer: new HarmonyMethod(GetPatchMethod(nameof(RoomLoadDiagnostics.MoveNext_Finalizer))));
                        patched++;
                        Log.Msg($"[patch-ok] room-load MoveNext {type.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[patch-fail] room-load MoveNext {type.FullName}: {ex.Message}");
                    }
                }
            }
        }

        if (patched == 0)
        {
            if (logMiss) Log.Warning("[patch-miss] room-load state-machine MoveNext hooks");
            return false;
        }

        _diagnosticPatchLabels.Add(label);
        return true;
    }

    private void RegisterProcessExceptionHandlers()
    {
        if (_processExceptionHandlersRegistered) return;
        _processExceptionHandlersRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                DiagnosticPatches.Write($"[process-exception] unhandled terminating={e.IsTerminating} {RoomLoadDiagnostics.FormatException(ex, e.ExceptionObject)}");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                DiagnosticPatches.Write($"[process-exception] unobserved-task {RoomLoadDiagnostics.FormatException(e.Exception, e.Exception)}");
            }
            catch { }
        };
    }
}

// ── Patch holders ─────────────────────────────────────────────────────
// Each holder is a static class with public static Prefix methods that
// Harmony invokes. Kept separate from Mod for readability and so the
// reflection lookup in GetPatchMethod stays simple.

internal static class RoomLoadDiagnostics
{
    private static readonly Dictionary<string, int> Counts = new();
    private static int _globalCount;

    public static void LogError_Prefix(object? message)
    {
        DiagnosticPatches.Write($"[unity-error] LogError {Sanitize(message?.ToString() ?? "<null>", 1600)}");
        WriteStack("unity-error");
    }

    public static void LogException_Prefix(Exception exception)
    {
        DiagnosticPatches.Write($"[unity-exception] {FormatException(exception, exception)}");
        WriteStack("unity-exception");
    }

    public static void NamedCall_Prefix(MethodBase __originalMethod)
    {
        var name = FullMethodName(__originalMethod);
        if (!ShouldLog("call:" + name, 80)) return;

        DiagnosticPatches.Write($"[runtime-call] ENTER {name} [{Signature(__originalMethod)}]");
        if (IsImportantCall(name))
            WriteStack("runtime-call");
    }

    public static Exception? NamedCall_Finalizer(Exception? __exception, MethodBase __originalMethod)
    {
        if (__exception is not null)
        {
            DiagnosticPatches.Write($"[runtime-call] EXCEPTION {FullMethodName(__originalMethod)} {FormatException(__exception, __exception)}");
            WriteStack("runtime-call-exception");
        }
        return __exception;
    }

    public static void MoveNext_Prefix(object __instance, MethodBase __originalMethod)
    {
        var name = FullMethodName(__originalMethod);
        if (!ShouldLog("enter:" + name, 120)) return;
        DiagnosticPatches.Write($"[room-load] ENTER {name} state={DumpInstanceFields(__instance, 28)}");
    }

    public static void MoveNext_Postfix(object __instance, MethodBase __originalMethod)
    {
        var name = FullMethodName(__originalMethod);
        if (!ShouldLog("exit:" + name, 120)) return;
        DiagnosticPatches.Write($"[room-load] EXIT {name} state={DumpInstanceFields(__instance, 28)}");
    }

    public static Exception? MoveNext_Finalizer(Exception? __exception, object __instance, MethodBase __originalMethod)
    {
        if (__exception is not null)
        {
            DiagnosticPatches.Write(
                $"[room-load] EXCEPTION {FullMethodName(__originalMethod)} " +
                $"{FormatException(__exception, __exception)} state={DumpInstanceFields(__instance, 40)}");
            WriteStack("room-load-exception");
        }
        return __exception;
    }

    public static string FormatException(Exception? ex, object? fallback)
    {
        if (ex is null) return fallback?.ToString() ?? "<null>";
        var builder = new StringBuilder();
        AppendException(builder, ex, 0);
        return Sanitize(builder.ToString(), 5000);
    }

    private static void AppendException(StringBuilder builder, Exception ex, int depth)
    {
        if (depth > 4) return;
        if (builder.Length > 0) builder.Append(" | ");
        builder.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            builder.Append(" stack=").Append(ex.StackTrace);
        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                AppendException(builder, inner, depth + 1);
        }
        else if (ex.InnerException is not null)
        {
            AppendException(builder, ex.InnerException, depth + 1);
        }
    }

    private static bool ShouldLog(string key, int perKeyLimit)
    {
        lock (Counts)
        {
            _globalCount++;
            if (_globalCount > 12000)
                return false;

            Counts.TryGetValue(key, out var count);
            if (count >= perKeyLimit)
                return false;

            Counts[key] = count + 1;
            return true;
        }
    }

    private static string DumpInstanceFields(object? instance, int maxFields)
    {
        if (instance is null) return "<null>";
        try
        {
            var type = instance.GetType();
            var parts = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in type.GetFields(flags))
            {
                if (parts.Count >= maxFields)
                {
                    parts.Add("...");
                    break;
                }

                object? value;
                try { value = field.GetValue(instance); }
                catch (Exception ex)
                {
                    parts.Add($"{field.Name}=<read failed {ex.GetType().Name}>");
                    continue;
                }

                var described = DescribeValue(value, 400);
                if (ShouldExpandField(field, value))
                    described += " " + DumpNestedFields(value, 16);
                parts.Add($"{field.Name}={described}");
            }

            return $"{type.FullName} {{{string.Join("; ", parts)}}}";
        }
        catch (Exception ex)
        {
            return $"<dump failed {ex.GetType().Name}: {Sanitize(ex.Message, 400)}>";
        }
    }

    private static bool ShouldExpandField(FieldInfo field, object? value)
    {
        if (value is null) return false;
        if (value is string) return false;

        var valueType = value.GetType();
        if (valueType.IsPrimitive || valueType.IsEnum || value is decimal)
            return false;
        if (value is IEnumerable)
            return false;

        var fieldName = field.Name ?? string.Empty;
        var typeName = valueType.FullName ?? valueType.Name;
        return fieldName.IndexOf("this", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("OMJKHJLFOCO", StringComparison.Ordinal) >= 0 ||
               typeName.IndexOf("RoomPermissions", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string DumpNestedFields(object value, int maxFields)
    {
        try
        {
            var type = value.GetType();
            var parts = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in type.GetFields(flags))
            {
                if (parts.Count >= maxFields)
                {
                    parts.Add("...");
                    break;
                }

                object? nested;
                try { nested = field.GetValue(value); }
                catch (Exception ex)
                {
                    parts.Add($"{field.Name}=<read failed {ex.GetType().Name}>");
                    continue;
                }

                parts.Add($"{field.Name}={DescribeValue(nested, 220)}");
            }

            return "{nested " + string.Join("; ", parts) + "}";
        }
        catch (Exception ex)
        {
            return $"{{nested dump failed {ex.GetType().Name}: {Sanitize(ex.Message, 220)}}}";
        }
    }

    private static string DescribeValue(object? value, int limit)
    {
        if (value is null) return "null";

        var type = value.GetType();
        if (value is string text) return "\"" + Sanitize(text, limit) + "\"";
        if (value is Exception ex) return FormatException(ex, ex);
        if (type.IsPrimitive || type.IsEnum || value is decimal)
            return Sanitize(value.ToString() ?? "<null>", limit);
        if (value is IEnumerable enumerable && value is not string)
            return DescribeEnumerable(enumerable, type, limit);

        var rendered = value.ToString();
        if (string.IsNullOrWhiteSpace(rendered) || rendered == type.FullName)
            return "<" + type.FullName + ">";
        return "<" + type.FullName + " " + Sanitize(rendered, limit) + ">";
    }

    private static string DescribeEnumerable(IEnumerable enumerable, Type type, int limit)
    {
        var parts = new List<string>();
        var count = 0;
        foreach (var item in enumerable)
        {
            if (count++ >= 6)
            {
                parts.Add("...");
                break;
            }
            parts.Add(DescribeValue(item, 120));
        }
        return "<" + type.FullName + " [" + Sanitize(string.Join(", ", parts), limit) + "]>";
    }

    private static string Signature(MethodBase method)
    {
        try
        {
            var parameters = method.GetParameters();
            var parts = new List<string>(parameters.Length);
            foreach (var parameter in parameters)
                parts.Add(parameter.ParameterType.FullName ?? parameter.ParameterType.Name);
            return string.Join(", ", parts);
        }
        catch
        {
            return "<signature unread>";
        }
    }

    private static string FullMethodName(MethodBase method) =>
        $"{method.DeclaringType?.FullName ?? "<null>"}.{method.Name}";

    private static bool IsImportantCall(string name) =>
        name.IndexOf("Disconnect", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Join", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Photon", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("CDNPLAGEDBK", StringComparison.Ordinal) >= 0 ||
        name.IndexOf("IJIOJAIBPKE", StringComparison.Ordinal) >= 0;

    private static void WriteStack(string tag)
    {
        try
        {
            DiagnosticPatches.Write($"[{tag}-stack] " + new StackTrace(skipFrames: 2, fNeedFileInfo: false));
        }
        catch (Exception ex)
        {
            DiagnosticPatches.Write($"[{tag}-stack] failed: {ex.Message}");
        }
    }

    private static string Sanitize(string value, int limit)
    {
        var redacted = Regex.Replace(value, @"(?i)(Bearer\s+)[-._~+/=A-Za-z0-9]+", "$1<redacted>");
        redacted = Regex.Replace(redacted, @"(?i)(access_token|accessToken|refresh_token|refreshToken|authorization|token|key)=([^&\s]+)", "$1=<redacted>");
        redacted = redacted.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return redacted.Length <= limit ? redacted : redacted[..limit] + "...<truncated>";
    }
}

// ── Debug console access ───────────────────────────────────────────────
// The 2020 client ships RecRoom.Debugging.DebugConsole — a real in-game
// dev console (text input + a DebugConsoleCommandConfig command table:
// SetTimeScale, Fly, Teleport, GoToRoom, KillAllEnemies, DebugCompleteDaily,
// …). Retail gates it behind a developer flag. This holder force-toggles
// its UI on a hotkey and (via the OnLateInitializeMelon patch above) skips
// CheatManager's tamper detectors so movement/time commands don't drop you
// to the dorm.
//
// Everything is resolved by reflection at runtime so the SAME code works on
// both client builds despite their differing obfuscation:
//   * The show/hide toggle is the one non-compiler-generated private
//     instance void(bool) on DebugConsole — ShowInputField on 2020.03,
//     MCNBJFECHLJ on 2020.12. We try the readable name, then the obfuscated
//     one, then fall back to that structural match.
//   * The live component is found via UnityEngine.Object.FindObjectOfType
//     (name-stable) rather than the SingletonMonoBehaviour<T> accessor.
//   * Execute(string) keeps its readable name on both builds (kept here as
//     a scripted-command entry point even though the hotkey drives the UI).
internal static class DebugConsolePatches
{
    private static bool _consoleVisible;
    private static bool _pollErrorLogged;
    private static int _probeFrame;
    private static bool _probeDone;

    private static bool _inputResolved;
    private static MethodInfo? _getKeyDown;     // UnityEngine.Input.GetKeyDown(KeyCode)
    private static object? _toggleKeyValue;     // boxed KeyCode enum value

    private static bool _consoleResolved;
    private static Type? _consoleType;
    private static MethodInfo? _toggleMethod;     // ShowInputField(bool) / MCNBJFECHLJ(bool)
    private static MethodInfo? _findObjectOfType; // UnityEngine.Object.FindObjectOfType(Type)

    // Harmony prefix returning false → skips the original CheatManager
    // detector, so a tripped heuristic never runs its punish/drop path.
    public static bool SuppressCheatDetected_Prefix() => false;

    // Polled every frame from Mod.OnUpdate while EnableDebugConsole is set.
    public static void PollToggleKey()
    {
        try
        {
            if (!_inputResolved) ResolveInput();
            // One-shot probe ~3s in (≈180 frames @60fps): log whether the
            // console object actually exists, so we learn that even if the
            // hotkey never fires.
            if (!_probeDone && _probeFrame++ > 180)
            {
                _probeDone = true;
                ProbeConsole();
            }
            if (_getKeyDown is null || _toggleKeyValue is null) return;
            if (_getKeyDown.Invoke(null, new[] { _toggleKeyValue }) is true)
            {
                Mod.Log.Msg($"[debugconsole] toggle key '{Mod.Cfg.DebugConsoleToggleKey}' detected");
                Toggle();
            }
        }
        catch (Exception ex)
        {
            if (!_pollErrorLogged) { _pollErrorLogged = true; Mod.Log.Warning($"[debugconsole] poll failed (logged once): {ex}"); }
        }
    }

    // Diagnostic: reports whether a live DebugConsole component exists and
    // whether the show/hide method resolved — without needing a keypress.
    private static void ProbeConsole()
    {
        if (!_consoleResolved) ResolveConsole();
        if (_consoleType is null) { Mod.Log.Warning("[debugconsole] probe: DebugConsole type NOT resolved"); return; }
        try
        {
            var active = _findObjectOfType?.Invoke(null, new object[] { _consoleType });
            Mod.Log.Msg($"[debugconsole] probe: type={_consoleType.FullName}, toggleMethod={(_toggleMethod?.Name ?? "<none>")}, activeInstance={(active is null ? "null" : "found")}");
        }
        catch (Exception ex) { Mod.Log.Warning($"[debugconsole] probe failed: {ex.Message}"); }
    }

    // Runs DebugConsole.Execute(command) directly — bypasses the UI/gate
    // entirely. Handy for scripted one-shots; the hotkey path uses the UI.
    public static void ExecuteCommand(string command)
    {
        if (!_consoleResolved) ResolveConsole();
        if (_consoleType is null) return;
        try
        {
            var execute = AccessTools.Method(_consoleType, "Execute", new[] { typeof(string) });
            if (execute is null) { Mod.Log.Warning("[debugconsole] Execute(string) not found"); return; }
            execute.Invoke(null, new object[] { command });
            Mod.Log.Msg($"[debugconsole] executed: {command}");
        }
        catch (Exception ex) { Mod.Log.Warning($"[debugconsole] execute '{command}' failed: {ex.Message}"); }
    }

    private static void ResolveInput()
    {
        _inputResolved = true;
        var keyCodeType = Mod.ResolveType("UnityEngine.KeyCode");
        var inputType   = Mod.ResolveType("UnityEngine.Input");
        if (keyCodeType is null || inputType is null)
        {
            Mod.Log.Warning("[debugconsole] UnityEngine.Input/KeyCode not found — hotkey disabled");
            return;
        }
        try { _toggleKeyValue = Enum.Parse(keyCodeType, Mod.Cfg.DebugConsoleToggleKey, ignoreCase: true); }
        catch
        {
            Mod.Log.Warning($"[debugconsole] '{Mod.Cfg.DebugConsoleToggleKey}' is not a KeyCode; defaulting to BackQuote");
            _toggleKeyValue = Enum.Parse(keyCodeType, "BackQuote");
        }
        _getKeyDown = AccessTools.Method(inputType, "GetKeyDown", new[] { keyCodeType });
        if (_getKeyDown is null) Mod.Log.Warning("[debugconsole] Input.GetKeyDown(KeyCode) not found");
        else Mod.Log.Msg($"[debugconsole] hotkey armed: '{Mod.Cfg.DebugConsoleToggleKey}' (poll live)");
    }

    private static void ResolveConsole()
    {
        _consoleResolved = true;
        _consoleType = Mod.ResolveType("RecRoom.Debugging.DebugConsole");
        if (_consoleType is null) { Mod.Log.Warning("[debugconsole] DebugConsole type not found"); return; }

        // Prefer the readable 2020.03 name, then the 2020.12 obfuscated
        // name, then a structural fallback that fits both builds.
        _toggleMethod = AccessTools.Method(_consoleType, "ShowInputField", new[] { typeof(bool) })
                        ?? AccessTools.Method(_consoleType, "MCNBJFECHLJ", new[] { typeof(bool) })
                        ?? FindToggleByShape(_consoleType);
        if (_toggleMethod is null) Mod.Log.Warning("[debugconsole] show/hide method not found");

        var objType = Mod.ResolveType("UnityEngine.Object");
        _findObjectOfType = objType is null ? null
            : AccessTools.Method(objType, "FindObjectOfType", new[] { typeof(Type) });
        if (_findObjectOfType is null) Mod.Log.Warning("[debugconsole] Object.FindObjectOfType(Type) not found");
    }

    // The console's show/hide is the single private, instance, void method
    // taking exactly one bool, excluding compiler-generated members (the
    // property backing setter on 2020.12 is also void(bool) but is marked
    // [CompilerGenerated]).
    private static MethodInfo? FindToggleByShape(Type t)
    {
        MethodInfo? found = null;
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (m.ReturnType != typeof(void)) continue;
            var ps = m.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(bool)) continue;
            if (m.Name.StartsWith("set_", StringComparison.Ordinal)) continue;
            if (m.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) continue;
            if (found is not null) return null; // ambiguous — don't guess
            found = m;
        }
        return found;
    }

    public static void Toggle()
    {
        if (!_consoleResolved) ResolveConsole();
        if (_consoleType is null || _toggleMethod is null || _findObjectOfType is null) return;
        try
        {
            var instance = _findObjectOfType.Invoke(null, new object[] { _consoleType });
            if (instance is null)
            {
                Mod.Log.Warning("[debugconsole] no live DebugConsole in scene — it may not be instantiated on this build");
                return;
            }
            // Best-effort: flip the static "show button" enable so the
            // game's own Update keeps the console available instead of
            // re-hiding it. Failures are ignored (obfuscated builds).
            EnableShowButton();
            _consoleVisible = !_consoleVisible;
            _toggleMethod.Invoke(instance, new object[] { _consoleVisible });
            Mod.Log.Msg($"[debugconsole] {(_consoleVisible ? "opened" : "closed")}");
        }
        catch (Exception ex) { Mod.Log.Warning($"[debugconsole] toggle failed: {ex.Message}"); }
    }

    private static void EnableShowButton()
    {
        if (_consoleType is null) return;
        try
        {
            var prop = _consoleType.GetProperty("ShowButton", BindingFlags.Public | BindingFlags.Static);
            if (prop is not null && prop.CanWrite) { prop.SetValue(null, true); return; }
            foreach (var name in new[] { "showButton", "IEGCNLHPKFH" })
            {
                var f = _consoleType.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (f is not null && f.FieldType == typeof(bool)) { f.SetValue(null, true); return; }
            }
        }
        catch { /* best-effort — the toggle still works without it */ }
    }
}


// ── Dev-menu runtime probe (one-shot diagnostic) ────────────────────────
// Answers stripped-vs-gated for the watch developer menu. With a real dev
// account + forced flag the menu still doesn't appear, so the gate isn't the
// account flag — either the native dev-menu prefab content is stripped from
// this retail build, or a build-level flag skips its init. This logs the
// actual runtime state once ~5s after load (no behaviour change) so we know
// whether to un-gate the game's own dev menu or build our own watch entry.
// Called from OnUpdate when Cfg.DiagnoseDevMenu is set; self-gates to run
// once. Not a Harmony holder — invoked directly, so it's not registered in
// GetPatchMethod.
internal static class DevMenuProbe
{
    private static int _frame;
    private static bool _done;

    public static void Tick()
    {
        if (_done) return;
        _frame++;
        if (_frame < 120) return;        // ~2s — let MelonLoader/game boot
        if ((_frame % 60) != 0) return;  // then check ~once per second
        // Only fire once a REAL, in-scene HomeScreenFlow exists with its
        // UIBase wired (it gets assigned when the watch StackedUI initialises
        // in the dorm). The earlier version fired during boot on the loaded
        // PREFAB (Resources.FindObjectsOfTypeAll returns prefabs/inactive,
        // whose runtime UIBase is null) and, being one-shot, captured nothing
        // and never retried. NO timeout now — we wait until the watch is up.
        var homeType = Mod.ResolveType("AGUI.StackedUI.HomeScreenFlow") ?? Mod.ResolveType("HomeScreenFlow");
        var home = homeType != null ? FindLiveHome(homeType) : null;
        if (home == null)
        {
            if ((_frame % 600) == 0)
                Mod.Log.Msg("[devmenu-probe] waiting for an in-scene HomeScreenFlow — enter your dorm / open the watch…");
            return;
        }
        _done = true;
        try { Run(homeType!, home); }
        catch (Exception ex) { Mod.Log.Warning($"[devmenu-probe] failed: {ex}"); }
    }

    // The live HomeScreenFlow (UIBase wired), skipping the prefab/inactive
    // copies FindObjectsOfTypeAll also returns.
    private static object? FindLiveHome(Type homeType)
    {
        try
        {
            var il2 = ToIl2CppType(homeType);
            if (il2 is null) return null;
            var resources = Mod.ResolveType("UnityEngine.Resources");
            var all = OneArgMethod(resources, "FindObjectsOfTypeAll", il2)?.Invoke(null, new[] { il2 });
            int n = ArrLen(all);
            for (int i = 0; i < n; i++)
            {
                var o = ArrItem(all, i);
                if (o is null) continue;
                if (GetMemberValue(homeType, o, "UIBase", "get_UIBase") != null) return o;
            }
        }
        catch { }
        return null;
    }

    private static void Run(Type homeType, object homeInst)
    {
        Mod.Log.Msg("=== [devmenu-probe] runtime dev-UI state ===");
        ProbeType("RecRoom.Core.WatchUI",            "WatchUI",        DumpWatchUi);
        Mod.Log.Msg($"[devmenu-probe] HomeScreenFlow: type={homeType.FullName}, liveInstance=FOUND (UIBase wired)");
        try { DumpHomeScreen(homeType, homeInst); }
        catch (Exception ex) { Mod.Log.Warning($"[devmenu-probe] HomeScreenFlow dump failed: {ex.Message}"); }
        ProbeType("RecRoom.Debugging.DebugConsole",  "DebugConsole",   DumpDebugConsole);
        Mod.Log.Msg("=== [devmenu-probe] done ===");
    }

    private static void ProbeType(string fullName, string shortName, Action<Type, object?> dump)
    {
        var t = Mod.ResolveType(fullName) ?? Mod.ResolveType(shortName);
        if (t is null) { Mod.Log.Msg($"[devmenu-probe] {shortName}: TYPE NOT FOUND"); return; }
        var inst = FindOne(t);
        Mod.Log.Msg($"[devmenu-probe] {shortName}: type={t.FullName}, liveInstance={(inst is null ? "null" : "FOUND")}");
        try { dump(t, inst); } catch (Exception ex) { Mod.Log.Warning($"[devmenu-probe] {shortName} dump failed: {ex.Message}"); }
    }

    private static void DumpHomeScreen(Type t, object? inst)
    {
        if (inst is null) { Mod.Log.Msg("  (no HomeScreenFlow instance live yet — open the watch, then re-probe)"); return; }
        // Il2CppInterop exposes IL2CPP instance fields as PROPERTIES on the
        // managed proxy, so a plain AccessTools.Field misses them (the earlier
        // "field not found" was a false negative). Try property first, then
        // field. NULL => content stripped from the prefab (can't un-gate);
        // present => only gated.
        foreach (var name in new[] { "devMenuPanel", "devButtonPrefab", "reportBugButtonSpacer" })
        {
            var (found, mt, v) = GetMember(t, inst, name);
            if (!found) { Mod.Log.Msg($"  {name}: <not found as property or field>"); continue; }
            var extra = "";
            if (v != null && name == "devMenuPanel")
            {
                try { var cc = v.GetType().GetProperty("childCount")?.GetValue(v); if (cc != null) extra = $" childCount={cc}"; } catch { }
            }
            Mod.Log.Msg($"  {name} ({mt.Name}): {(v is null ? "NULL  <-- stripped/unset" : "present")}{extra}");
        }
        // Calibration: are ordinary serialized field names exposed at all on
        // the runtime proxy? If 'profileButton' (a non-dev [SerializeField])
        // IS found but devMenuPanel isn't, the dev fields are genuinely absent
        // in this build. If profileButton is ALSO not found, Il2CppInterop
        // isn't exposing these fields by name (need a different access path).
        foreach (var cal in new[] { "profileButton", "settingsButton" })
        {
            var (cf, cmt, cv) = GetMember(t, inst, cal);
            Mod.Log.Msg($"  [calib] {cal}: {(cf ? $"FOUND ({cmt.Name}, {(cv is null ? "null" : "present")})" : "not found")}");
        }
        // Named lookups miss → dump the proxy's actual member names to see
        // Il2CppInterop's scheme, then enumerate Button3D children directly
        // (no field names needed) — those are our clone sources for injection.
        var fs = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var ps = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var fn = new List<string>(); foreach (var f in fs) fn.Add(f.Name);
        var pn = new List<string>(); foreach (var p in ps) pn.Add(p.Name);
        Mod.Log.Msg($"  [members] fields={fn.Count}, props={pn.Count}");
        if (fn.Count > 0) Mod.Log.Msg($"  [fieldnames] {string.Join(", ", fn.GetRange(0, Math.Min(40, fn.Count)))}");
        if (pn.Count > 0) Mod.Log.Msg($"  [propnames] {string.Join(", ", pn.GetRange(0, Math.Min(40, pn.Count)))}");
        DumpWatchButtons(t, inst);
        DumpCollections(t, inst, "HomeScreenFlow");
        DumpStackedUi(t, inst);
    }

    // Walk HomeScreenFlow -> UIBase (StackedUIBase) and dump the data we need
    // to build a CUSTOM watch page by cloning a real flow:
    //   * flowResources catalog  — the TypeName of every PUSHABLE flow; these
    //     are our clone-target candidates (pick a simple self-contained one).
    //   * the Type->flow cache    — what's already instantiated (do NOT mutate
    //     these cached singletons; clone an independent copy instead).
    //   * ScreenHeader presence   — proves a cloned flow brings its own title
    //     bar + back button for free.
    // Names are obfuscated, so we match by SHAPE (array-of-prefab-ref,
    // Dictionary<Type,flow>) rather than by field name.
    private static void DumpStackedUi(Type homeType, object homeInst)
    {
        var ui = GetMemberValue(homeType, homeInst, "UIBase", "get_UIBase");
        if (ui is null) { Mod.Log.Msg("  [stackedui] UIBase: null (could not reach StackedUIBase)"); return; }
        var uiType = ui.GetType();
        Mod.Log.Msg($"  [stackedui] UIBase type={uiType.FullName}");

        var hdr = GetMemberValue(homeType, homeInst, "ScreenHeader", "get_ScreenHeader");
        if (hdr != null)
        {
            var ht = hdr.GetType();
            var hp = new List<string>(); foreach (var p in ht.GetProperties()) hp.Add(p.Name);
            Mod.Log.Msg($"  [stackedui] home ScreenHeader={ht.Name}; props={string.Join(",", hp.GetRange(0, Math.Min(30, hp.Count)))}");
        }
        else Mod.Log.Msg("  [stackedui] home ScreenHeader: null");

        foreach (var f in uiType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            object? v; try { v = f.GetValue(ui); } catch { continue; }
            if (v is null) continue;
            int n = ArrLen(v);
            // Flow-prefab catalog: a non-empty array whose elements expose a
            // TypeName string (FlowPrefabResourceReference).
            if (n > 0 && n <= 300 && (v is Array || v.GetType().Name.IndexOf("Array", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var first = ArrItem(v, 0);
                var et = first?.GetType().Name ?? "?";
                if (et.IndexOf("Flow", StringComparison.OrdinalIgnoreCase) < 0 &&
                    et.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase) < 0 &&
                    et.IndexOf("Resource", StringComparison.OrdinalIgnoreCase) < 0 &&
                    et.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Mod.Log.Msg($"  [stackedui] flow catalog '{f.Name}': {et}[{n}]");
                for (int i = 0; i < n; i++)
                {
                    var e = ArrItem(v, i); if (e is null) continue;
                    Mod.Log.Msg($"      [{i}] {ReadStringMember(e)}");
                }
                continue;
            }
            // Flow cache: Dictionary<Type, StackedUIFlowBase>.
            var vt = v.GetType();
            if (vt.IsGenericType && vt.Name.StartsWith("Dictionary"))
            {
                var ga = vt.GetGenericArguments();
                if (ga.Length == 2 && ga[1].Name.IndexOf("Flow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var cp = vt.GetProperty("Count");
                    Mod.Log.Msg($"  [stackedui] flow cache '{f.Name}': Dictionary<{ga[0].Name},{ga[1].Name}> count={(cp?.GetValue(v) as int? ?? -1)}");
                }
            }
        }
    }

    // Property by name, else parameterless getter method, else null.
    private static object? GetMemberValue(Type t, object inst, string prop, string getter)
    {
        var (found, _, v) = GetMember(t, inst, prop);
        if (found && v != null) return v;
        var m = t.GetMethod(getter, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                            null, Type.EmptyTypes, null);
        if (m != null) { try { return m.Invoke(inst, null); } catch { } }
        return v;
    }

    // First non-empty string field/property on an object — for obfuscated
    // wrappers, that's the TypeName of a FlowPrefabResourceReference.
    private static string ReadStringMember(object o)
    {
        var t = o.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                { try { if (p.GetValue(o) is string s && s.Length > 0) return s; } catch { } }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (f.FieldType == typeof(string))
                { try { if (f.GetValue(o) is string s && s.Length > 0) return s; } catch { } }
        return "(no string member)";
    }

    // Enumerate Button3D components under the HomeScreenFlow via Unity's
    // GetComponentsInChildren(Type, includeInactive) — needs no field names.
    // Confirms we can grab native buttons to clone for the dev entry.
    private static void DumpWatchButtons(Type homeType, object homeInst)
    {
        var btnType = Mod.ResolveType("AGUI.StackedUI.Button3D");
        if (btnType is null) { Mod.Log.Msg("  [buttons] Button3D type NOT FOUND"); return; }
        var il2 = ToIl2CppType(btnType);
        if (il2 is null) { Mod.Log.Msg("  [buttons] Button3D il2cppType null"); return; }
        MethodInfo? m = null;
        foreach (var mi in homeType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (mi.Name != "GetComponentsInChildren") continue;
            var mp = mi.GetParameters();
            if (mp.Length == 2 && mp[0].ParameterType.IsInstanceOfType(il2) && mp[1].ParameterType == typeof(bool)) { m = mi; break; }
        }
        if (m is null) { Mod.Log.Msg("  [buttons] GetComponentsInChildren(Type,bool) not found"); return; }
        object? arr;
        try { arr = m.Invoke(homeInst, new object[] { il2, true }); }
        catch (Exception ex) { Mod.Log.Msg($"  [buttons] GetComponentsInChildren threw: {(ex.InnerException ?? ex).Message}"); return; }
        int n = ArrLen(arr);
        Mod.Log.Msg($"  [buttons] Button3D children: {n}");
        for (int i = 0; i < n && i < 20; i++)
        {
            var b = ArrItem(arr, i);
            var nm = b?.GetType().GetProperty("name")?.GetValue(b)?.ToString() ?? "?";
            Mod.Log.Msg($"    [{i}] {nm}");
        }
    }

    private static int ArrLen(object? arr)
    {
        if (arr is null) return 0;
        if (arr is Array a) return a.Length;
        var lp = arr.GetType().GetProperty("Length") ?? arr.GetType().GetProperty("Count");
        return (lp?.GetValue(arr) as int?) ?? 0;
    }

    private static object? ArrItem(object? arr, int i)
    {
        if (arr is null) return null;
        if (arr is Array a) return a.GetValue(i);
        var gi = arr.GetType().GetMethod("get_Item", new[] { typeof(int) });
        return gi?.Invoke(arr, new object[] { i });
    }

    private static (bool found, Type type, object? val) GetMember(Type t, object inst, string name)
    {
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
            { try { return (true, p.PropertyType, p.GetValue(inst)); } catch { } }
        var f = AccessTools.Field(t, name);
        if (f != null) { try { return (true, f.FieldType, f.GetValue(inst)); } catch { } }
        return (false, typeof(object), null);
    }

    private static void DumpWatchUi(Type t, object? inst)
    {
        if (inst is null) { Mod.Log.Msg("  (no WatchUI instance live yet)"); return; }
        DumpCollections(t, inst, "WatchUI");
    }

    private static void DumpDebugConsole(Type t, object? inst)
    {
        foreach (var name in new[] { "GJIKMPIMDHH", "IEGCNLHPKFH", "ShowButton", "ShowErrorsInHeadset", "ShowUnityDevelopmentConsole" })
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null && p.CanRead) { Mod.Log.Msg($"  static {name} = {p.GetValue(null)}"); continue; }
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) Mod.Log.Msg($"  static {name} = {f.GetValue(null)}");
            }
            catch (Exception ex) { Mod.Log.Msg($"  {name}: read failed ({ex.Message})"); }
        }
        // Does the static command path work without the console UI in-scene?
        var exec = AccessTools.Method(t, "Execute", new[] { typeof(string) });
        if (exec is null) { Mod.Log.Msg("  Execute(string): <not found>"); return; }
        try { exec.Invoke(null, new object[] { "help" }); Mod.Log.Msg("  Execute(\"help\"): invoked OK — static command path is usable"); }
        catch (Exception ex) { var e = ex.InnerException ?? ex; Mod.Log.Msg($"  Execute(\"help\"): threw {e.GetType().Name} ({e.Message})"); }
    }

    // Log every array / List<> field whose element type mentions Flow/Watch,
    // with its count — surfaces the dev-flow list (devOnlyFlows-equivalent)
    // regardless of its obfuscated name, so we can see if dev flows exist.
    private static void DumpCollections(Type t, object inst, string label)
    {
        var members = new List<(string name, object? val)>();
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            { try { members.Add((f.Name, f.GetValue(inst))); } catch { } }
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            { if (p.CanRead && p.GetIndexParameters().Length == 0) { try { members.Add((p.Name, p.GetValue(inst))); } catch { } } }

        foreach (var (name, v) in members)
        {
            if (v is null) continue;
            int? count = null; string elem = "";
            if (v is Array arr) { count = arr.Length; elem = v.GetType().GetElementType()?.Name ?? "?"; }
            else
            {
                var ft = v.GetType();
                if (ft.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(ft))
                {
                    var cp = ft.GetProperty("Count");
                    if (cp != null && cp.PropertyType == typeof(int))
                    {
                        count = (int?)cp.GetValue(v);
                        var ga = ft.GetGenericArguments();
                        elem = ga.Length > 0 ? ga[0].Name : "?";
                    }
                }
            }
            if (count.HasValue &&
                (elem.IndexOf("Flow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 elem.IndexOf("Watch", StringComparison.OrdinalIgnoreCase) >= 0))
                Mod.Log.Msg($"  {label}.{name}: {elem}[{count}]");
        }
    }

    // Under Il2CppInterop, UnityEngine.Object.FindObjectOfType takes an
    // Il2CppSystem.Type (NOT System.Type), so AccessTools.Method(...,
    // typeof(Type)) misses — that's the "FindObjectOfType(Type) not found"
    // in the log. Convert the managed Type to an Il2CppSystem.Type via
    // Il2CppType.From, then use Resources.FindObjectsOfTypeAll (which also
    // returns INACTIVE objects + loaded prefab instances — important, the
    // watch UI is usually inactive until opened). Falls back to the
    // active-only FindObjectOfType.
    private static object? FindOne(Type t)
    {
        try
        {
            var il2cppType = ToIl2CppType(t);
            if (il2cppType is null) return null;

            var resources = Mod.ResolveType("UnityEngine.Resources");
            var all = OneArgMethod(resources, "FindObjectsOfTypeAll", il2cppType);
            var first = FirstOf(all?.Invoke(null, new[] { il2cppType }));
            if (first != null) return first;

            var objType = Mod.ResolveType("UnityEngine.Object");
            var findOne = OneArgMethod(objType, "FindObjectOfType", il2cppType);
            return findOne?.Invoke(null, new[] { il2cppType });
        }
        catch { return null; }
    }

    private static object? ToIl2CppType(Type managed)
    {
        try
        {
            var il2cppTypeType = Mod.ResolveType("Il2CppInterop.Runtime.Il2CppType");
            var from = il2cppTypeType?.GetMethod("From", new[] { typeof(Type), typeof(bool) })
                       ?? il2cppTypeType?.GetMethod("From", new[] { typeof(Type) });
            if (from is null) return null;
            var args = from.GetParameters().Length == 2
                ? new object[] { managed, true } : new object[] { managed };
            return from.Invoke(null, args);
        }
        catch { return null; }
    }

    // Find a public static method `name` whose single parameter accepts `arg`.
    private static MethodInfo? OneArgMethod(Type? t, string name, object arg)
    {
        if (t is null) return null;
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != name) continue;
            var ps = m.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(arg)) return m;
        }
        return null;
    }

    // First element of an Il2CppReferenceArray / Array / IList, or null.
    private static object? FirstOf(object? arr)
    {
        if (arr is null) return null;
        if (arr is Array a) return a.Length > 0 ? a.GetValue(0) : null;
        var at = arr.GetType();
        var lenProp = at.GetProperty("Length") ?? at.GetProperty("Count");
        if (lenProp?.GetValue(arr) is int n && n > 0)
        {
            var getItem = at.GetMethod("get_Item", new[] { typeof(int) });
            if (getItem != null) return getItem.Invoke(arr, new object[] { 0 });
        }
        return null;
    }
}

// ── Desktop Screen Sharing FPS override ─────────────────────────────────
// RecRoom.Tools.Productivity.DesktopScreenSharingDisplay broadcasts the
// shared desktop image. Capture cadence is the baked [SerializeField]
// float screenShareImageRefreshFrequency; the broadcast tick gates on
//   if (Time.time - lastSend >= 1f / screenShareImageRefreshFrequency)
// (DesktopScreenSharingDisplay.txt:697-703) — so the field is literally the
// FPS, with no runtime clamp (the [Range] is editor-only). We rewrite it in
// an Awake postfix. The captured frame is transmitted via the component's
// IPunObservable OnPhotonSerializeView, which only fires at
// PhotonNetwork.SerializationRate (default 10 Hz), so we also lift the
// Photon send/serialization rate to cover the target — otherwise a 30 fps
// capture still only delivers ~10. Field names are un-obfuscated on both
// builds (confirmed in the 2020.12.18 dump), so a plain AccessTools.Field
// lookup works.
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

            Mod.Log.Msg($"[screenshare] {t.Name}: refreshFrequency→{fps}fps" +
                        (Mod.Cfg.DesktopScreenShareResolution > 0 ? $" res→{Mod.Cfg.DesktopScreenShareResolution}px" : "") +
                        (Mod.Cfg.DesktopScreenShareQuality > 0 ? $" quality→{Mod.Cfg.DesktopScreenShareQuality}" : ""));

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
        Mod.Log.Msg($"[screenshare] Photon rates raised (GLOBAL): SendRate {send}→{newSend}, SerializationRate {ser}→{target}");
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

internal static class MakerPenGiftPreviewPatches
{
    private static bool _loggedMissingPointer;
    private static bool _loggedMissingRestrictionFlag;

    public static bool LaserPointerEnabled_Prefix(object __instance, bool ONGBFDACHHG)
    {
        try
        {
            if (__instance is null) return true;
            var t = __instance.GetType();
            if (ReadField(__instance, t, "pointerLineRenderer") is null)
            {
                if (!_loggedMissingPointer)
                {
                    _loggedMissingPointer = true;
                    Mod.Log.Msg("[makerpen-preview] skipped LaserPointerEnabled on MakerPenVisuals with no pointerLineRenderer");
                }
                return false;
            }

            if (ONGBFDACHHG && ReadField(__instance, t, "EANJBIDHAMN") is null)
            {
                if (!_loggedMissingRestrictionFlag)
                {
                    _loggedMissingRestrictionFlag = true;
                    Mod.Log.Msg("[makerpen-preview] skipped LaserPointerEnabled=true on MakerPenVisuals with no laser restriction flag");
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[makerpen-preview] guard failed, letting original run: {ex.Message}");
        }

        return true;
    }

    private static object? ReadField(object instance, Type type, string name)
    {
        var field = AccessTools.Field(type, name) ?? AccessTools.Field(type.BaseType, name);
        return field?.GetValue(instance);
    }
}

internal static class AuthPatches
{
    private static string? _latestAccessToken;
    private static int _tokenLogCount;

    public static void CaptureTokenArgs_Prefix(object? __0, object? __1, object? __2)
    {
        try
        {
            var accessToken = __0?.ToString();
            if (LooksLikeJwt(accessToken))
                _latestAccessToken = accessToken;

            if (_tokenLogCount++ < 3)
            {
                DiagnosticPatches.Write(
                    "[auth-trace] token setter " +
                    $"access={Presence(__0)} refresh={Presence(__1)} key={Presence(__2)}");
            }
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[auth-trace] token capture failed: {ex.Message}");
        }
    }

    private static bool LooksLikeJwt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var dots = 0;
        foreach (var ch in value)
            if (ch == '.') dots++;
        return dots == 2;
    }

    private static string Presence(object? value) =>
        string.IsNullOrWhiteSpace(value?.ToString()) ? "missing" : "present";
}

internal static class HttpTracePatches
{
    private const int BodyPreviewBytes = 700;
    private static readonly Regex SensitivePairRegex = new(
        @"(?i)(password|client_secret|access_token|accessToken|refresh_token|refreshToken|authorization|token|key)=([^&\s]+)",
        RegexOptions.Compiled);
    private static readonly Regex BearerRegex = new(
        @"(?i)Bearer\s+[-._~+/=A-Za-z0-9]+",
        RegexOptions.Compiled);
    private static readonly Regex SensitiveHeaderRegex = new(
        @"(?im)^(Authorization|Cookie|Set-Cookie|X-RNSIG):\s*[^\r\n]+",
        RegexOptions.Compiled);
    private static readonly Regex SensitiveJsonStringRegex = new(
        @"(?i)""([^""]*(?:access_token|accessToken|refresh_token|refreshToken|authorization|token|secret|client_secret|key)[^""]*)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled);
    private static readonly Dictionary<IntPtr, string> RecNetRequestInfoByPtr = new();
    private static int _requestId;
    private static int _failureLogCount;
    private static int _responseTupleLogCount;
    private static int _builderSendLogCount;

    public static void RecNetRequestSend_Prefix(object __instance, MethodBase __originalMethod)
    {
        TraceRequestObject(__instance, $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
    }

    public static void RecNetRequestBuilderCtor_Prefix(object? __0, object? __1, object? __2)
    {
        var id = NextRequestId();
        DiagnosticPatches.Write(
            $"[http-trace] #{id} BNDIAONDFFF.ctor method={SanitizeInline(__0?.ToString() ?? "<null>")} " +
            $"service={SanitizeInline(__1?.ToString() ?? "<null>")} path={SanitizeInline(__2?.ToString() ?? "<null>")}");
    }

    public static void RecNetRequestBuilderCtor_Postfix(object __instance, object? __0, object? __1, object? __2)
    {
        try
        {
            var ptr = TryGetIl2CppPtr(__instance);
            if (ptr == IntPtr.Zero) return;

            var info =
                $"ptr=0x{ptr.ToInt64():X} method={SanitizeInline(__0?.ToString() ?? "<null>")} " +
                $"service={SanitizeInline(__1?.ToString() ?? "<null>")} path={SanitizeInline(__2?.ToString() ?? "<null>")}";
            lock (RecNetRequestInfoByPtr)
            {
                RecNetRequestInfoByPtr[ptr] = info;
                if (RecNetRequestInfoByPtr.Count > 256)
                    RecNetRequestInfoByPtr.Clear();
            }

            DiagnosticPatches.Write($"[recnet-request-map] {info}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-request-map", ex);
        }
    }

    public static void RecNetBuilderSend_Prefix(object __instance, MethodBase __originalMethod)
    {
        if (System.Threading.Interlocked.Increment(ref _builderSendLogCount) > 120) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-send] ENTER {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-send-enter", ex);
        }
    }

    public static void RecNetBuilderSend_Postfix(object __instance, MethodBase __originalMethod)
    {
        if (_builderSendLogCount > 120) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-send] EXIT {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-send-exit", ex);
        }
    }

    public static void RecNetResponseTuple_Prefix(object __instance, MethodBase __originalMethod)
    {
        if (System.Threading.Interlocked.Increment(ref _responseTupleLogCount) > 80) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-response-map] ENTER {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)} {DumpObjectFields(__instance, 20, 600)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-response-map-enter", ex);
        }
    }

    public static void RecNetResponseTuple_Postfix(object __instance, MethodBase __originalMethod)
    {
        if (_responseTupleLogCount > 80) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-response-map] EXIT {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)} {DumpObjectFields(__instance, 20, 600)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-response-map-exit", ex);
        }
    }

    public static Exception? RecNetResponseTuple_Finalizer(Exception? __exception, object __instance, MethodBase __originalMethod)
    {
        if (__exception is not null)
        {
            DiagnosticPatches.Write(
                $"[recnet-response-map] EXCEPTION {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)} {RoomLoadDiagnostics.FormatException(__exception, __exception)} " +
                $"state={DumpObjectFields(__instance, 36, 1200)}");
        }
        return __exception;
    }

    public static void HeaderSet_Prefix(object __instance, object? __0, object? __1, MethodBase __originalMethod)
    {
        try
        {
            var name = __0?.ToString() ?? "<null>";
            var value = RedactHeaderValue(name, __1?.ToString());
            var uri = TryReadRequestUri(__instance)?.ToString() ?? "<uri unread>";
            DiagnosticPatches.Write(
                $"[http-header] {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} " +
                $"{SanitizeInline(uri)} {name}: {value}");
        }
        catch (Exception ex)
        {
            LogFailure("header", ex);
        }
    }

    public static void CallCallback_Prefix(object __instance, MethodBase __originalMethod)
    {
        try
        {
            DiagnosticPatches.Write(
                $"[http-callback] ENTER {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{DescribeRequestAndResponse(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("callback-enter", ex);
        }
    }

    public static void CallCallback_Postfix(object __instance, MethodBase __originalMethod)
    {
        try
        {
            DiagnosticPatches.Write(
                $"[http-callback] EXIT {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{DescribeRequestAndResponse(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("callback-exit", ex);
        }
    }

    public static void TraceUrl(string? url, string source)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;

        var id = NextRequestId();
        DiagnosticPatches.Write($"[http-trace] #{id} {source} url={SanitizeInline(url)}");
    }

    public static void TraceRequestObject(object? request, string source)
    {
        if (request is null) return;

        try
        {
            var id = NextRequestId();
            var uri = TryReadRequestUri(request)?.ToString() ?? "<uri unread>";
            var method = TryReadHttpMethod(request);
            var headers = TryReadHeaders(request);
            var body = TryReadBodyPreview(request);

            DiagnosticPatches.Write(
                $"[http-trace] #{id} {source} method={method} uri={SanitizeInline(uri)} " +
                $"headers={headers} body={body}");
        }
        catch (Exception ex)
        {
            LogFailure(source, ex);
        }
    }

    private static int NextRequestId() => System.Threading.Interlocked.Increment(ref _requestId);

    private static Uri? TryReadRequestUri(object request)
    {
        if (TryInvokeUriMethod(request, "KPKNHFBHPHO", out var uri)) return uri;
        if (TryInvokeUriMethod(request, "IBLHNHHBNNE", out uri)) return uri;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = request.GetType();

        if (TryReadUriValue(type.GetProperty("Uri", flags)?.GetValue(request), out uri)) return uri;
        if (TryReadUriValue(type.GetProperty("CurrentUri", flags)?.GetValue(request), out uri)) return uri;
        if (TryReadUriValue(type.GetField("<Uri>k__BackingField", flags)?.GetValue(request), out uri)) return uri;

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.Name.IndexOf("Uri", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            try
            {
                if (TryReadUriValue(prop.GetValue(request), out uri)) return uri;
            }
            catch { }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.Name.IndexOf("Uri", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                if (TryReadUriValue(field.GetValue(request), out uri)) return uri;
            }
            catch { }
        }

        return null;
    }

    private static bool TryInvokeUriMethod(object request, string methodName, out Uri? uri)
    {
        uri = null;
        try
        {
            var method = AccessTools.Method(request.GetType(), methodName, Type.EmptyTypes);
            if (method is null) return false;
            return TryReadUriValue(method.Invoke(request, Array.Empty<object>()), out uri);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadUriValue(object? value, out Uri? uri)
    {
        uri = null;
        if (value is null) return false;
        if (value is Uri direct)
        {
            uri = direct;
            return true;
        }

        var text = value.ToString();
        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    private static string TryReadHttpMethod(object request)
    {
        foreach (var name in new[] { "MethodType", "Method", "LNJILMKOBIP" })
        {
            if (TryReadMemberValue(request, name, out var value))
                return SanitizeInline(value?.ToString() ?? "<null>");
        }

        return "<method unread>";
    }

    private static string TryReadHeaders(object request)
    {
        try
        {
            var dump = AccessTools.Method(request.GetType(), "DumpHeaders", Type.EmptyTypes);
            if (dump?.Invoke(request, Array.Empty<object>()) is object rawDump)
                return SanitizeInline(rawDump.ToString() ?? "<empty>");
        }
        catch { }

        var known = new List<string>();
        foreach (var name in new[]
        {
            "Authorization", "Cookie", "X-RNSIG", "Accept-Language",
            "Content-Type", "User-Agent"
        })
        {
            var value = TryReadHeaderValue(request, name);
            if (!string.IsNullOrWhiteSpace(value))
                known.Add($"{name}:{RedactHeaderValue(name, value)}");
        }

        return known.Count == 0 ? "<headers unread>" : string.Join("; ", known);
    }

    private static string? TryReadHeaderValue(object request, string name)
    {
        foreach (var methodName in new[] { "GetFirstHeaderValue", "FKHEHIMHHBP" })
        {
            try
            {
                var method = AccessTools.Method(request.GetType(), methodName, new[] { typeof(string) });
                var value = method?.Invoke(request, new object[] { name })?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            catch { }
        }

        return null;
    }

    private static string TryReadBodyPreview(object request)
    {
        foreach (var methodName in new[] { "BGMFJOKNJAJ", "GetEntityBody" })
        {
            try
            {
                var method = AccessTools.Method(request.GetType(), methodName, Type.EmptyTypes);
                if (method is null) continue;
                var body = method.Invoke(request, Array.Empty<object>());
                var bytes = TryCopyBytes(body, BodyPreviewBytes + 1);
                if (bytes is null) continue;
                if (bytes.Length == 0) return "<empty>";

                var shown = Math.Min(bytes.Length, BodyPreviewBytes);
                var text = Encoding.UTF8.GetString(bytes, 0, shown);
                var suffix = bytes.Length > BodyPreviewBytes ? "...<truncated>" : "";
                return $"{bytes.Length} bytes \"{SanitizeInline(text)}{suffix}\"";
            }
            catch { }
        }

        return "<body unread>";
    }

    private static string DescribeRequestAndResponse(object request)
    {
        var uri = TryReadRequestUri(request)?.ToString() ?? "<uri unread>";
        var method = TryReadHttpMethod(request);
        var response = TryReadResponseObject(request);
        var responseText = response is null ? "<response unread>" : DescribeResponseObject(response);
        var requestState = DescribeKnownMembers(request, "State", "Exception", "Error", "IsCancellationRequested");
        return $"method={method} uri={SanitizeInline(uri)} request={requestState} response={responseText}";
    }

    private static object? TryReadResponseObject(object request)
    {
        foreach (var name in new[] { "Response", "response", "_response", "<Response>k__BackingField" })
        {
            if (TryReadMemberValue(request, name, out var value) && value is not null)
                return value;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var prop in request.GetType().GetProperties(flags))
        {
            if (prop.Name.IndexOf("Response", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            try
            {
                var value = prop.GetValue(request);
                if (value is not null) return value;
            }
            catch { }
        }

        foreach (var field in request.GetType().GetFields(flags))
        {
            if (field.Name.IndexOf("Response", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                var value = field.GetValue(request);
                if (value is not null) return value;
            }
            catch { }
        }

        return null;
    }

    private static string DescribeResponseObject(object response)
    {
        var known = DescribeKnownMembers(response,
            "StatusCode", "Message", "IsSuccess", "DataAsText", "Data", "Exception", "Error");
        return $"{response.GetType().FullName} {{{known}}}";
    }

    private static string DescribeKnownMembers(object obj, params string[] names)
    {
        var parts = new List<string>();
        foreach (var name in names)
        {
            if (!TryReadMemberValue(obj, name, out var value)) continue;
            parts.Add($"{name}={FormatValue(value, 700)}");
        }
        return parts.Count == 0 ? "<no known members>" : string.Join("; ", parts);
    }

    private static string FormatValue(object? value, int limit)
    {
        if (value is null) return "null";
        if (value is byte[] bytes)
            return $"byte[{bytes.Length}]";
        if (value is IEnumerable enumerable && value is not string)
            return FormatEnumerable(enumerable, value.GetType(), limit);
        var text = value is Exception ex
            ? $"{ex.GetType().Name}: {ex.Message}"
            : value.ToString() ?? "<null>";
        text = SanitizeInline(text);
        return text.Length <= limit ? text : text[..limit] + "...<truncated>";
    }

    private static string FormatEnumerable(IEnumerable enumerable, Type type, int limit)
    {
        try
        {
            var parts = new List<string>();
            var countText = TryReadEnumerableCount(enumerable, out var count) ? count.ToString() : "?";
            var shown = 0;

            foreach (var item in enumerable)
            {
                if (shown++ >= 6)
                {
                    parts.Add("...");
                    break;
                }

                parts.Add(FormatValue(item, 180));
            }

            var rendered = $"{type.FullName} count={countText} [{string.Join(", ", parts)}]";
            return rendered.Length <= limit ? rendered : rendered[..limit] + "...<truncated>";
        }
        catch (Exception ex)
        {
            return $"{type.FullName} <enumeration failed {ex.GetType().Name}: {SanitizeInline(ex.Message)}>";
        }
    }

    private static bool TryReadEnumerableCount(IEnumerable enumerable, out int count)
    {
        count = 0;
        if (enumerable is ICollection collection)
        {
            count = collection.Count;
            return true;
        }

        try
        {
            var prop = enumerable.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.GetValue(enumerable) is int value)
            {
                count = value;
                return true;
            }
        }
        catch { }

        return false;
    }

    private static byte[]? TryCopyBytes(object? value, int limit)
    {
        if (value is null) return null;
        if (value is byte[] direct)
            return CopyLimited(direct, limit);

        if (value is IEnumerable enumerable)
        {
            var bytes = new List<byte>();
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                try { bytes.Add(Convert.ToByte(item)); }
                catch { return null; }
                if (bytes.Count >= limit) break;
            }
            return bytes.ToArray();
        }

        return null;
    }

    private static byte[] CopyLimited(byte[] source, int limit)
    {
        var count = Math.Min(source.Length, limit);
        var copy = new byte[count];
        Array.Copy(source, copy, count);
        return copy;
    }

    private static bool TryReadMemberValue(object request, string name, out object? value)
    {
        value = null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = request.GetType();

        try
        {
            var prop = type.GetProperty(name, flags);
            if (prop is not null && prop.CanRead && prop.GetIndexParameters().Length == 0)
            {
                value = prop.GetValue(request);
                return true;
            }
        }
        catch { }

        try
        {
            var field = type.GetField(name, flags);
            if (field is not null)
            {
                value = field.GetValue(request);
                return true;
            }
        }
        catch { }

        try
        {
            var method = AccessTools.Method(type, name, Type.EmptyTypes);
            if (method is not null)
            {
                value = method.Invoke(request, Array.Empty<object>());
                return true;
            }
        }
        catch { }

        return false;
    }

    private static string DumpObjectFields(object? instance, int maxFields, int valueLimit)
    {
        if (instance is null) return "<null>";

        try
        {
            var type = instance.GetType();
            var parts = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in type.GetFields(flags))
            {
                if (parts.Count >= maxFields)
                {
                    parts.Add("...");
                    break;
                }

                object? value;
                try { value = field.GetValue(instance); }
                catch (Exception ex)
                {
                    parts.Add($"{field.Name}=<read failed {ex.GetType().Name}>");
                    continue;
                }

                parts.Add($"{field.Name}={FormatValue(value, valueLimit)}");
            }

            return $"{type.FullName} {{{string.Join("; ", parts)}}}";
        }
        catch (Exception ex)
        {
            return $"<field dump failed {ex.GetType().Name}: {SanitizeInline(ex.Message)}>";
        }
    }

    private static string LookupRecNetRequestInfo(object instance)
    {
        var ptr = TryGetIl2CppPtr(instance);
        if (ptr == IntPtr.Zero) return "ptr=<unread>";

        lock (RecNetRequestInfoByPtr)
        {
            return RecNetRequestInfoByPtr.TryGetValue(ptr, out var info)
                ? info
                : $"ptr=0x{ptr.ToInt64():X} request=<unmapped>";
        }
    }

    private static string DumpBndNativeFields(object? instance)
    {
        var ptr = TryGetIl2CppPtr(instance);
        if (ptr == IntPtr.Zero) return "native=<unread>";

        try
        {
            var method = Marshal.ReadByte(ptr, 16);
            var service = Marshal.ReadInt32(ptr, 20);
            var pathPtr = Marshal.ReadIntPtr(ptr, 24);
            var fieldsPtr = Marshal.ReadIntPtr(ptr, 32);
            var filesPtr = Marshal.ReadIntPtr(ptr, 40);
            var objectFieldsPtr = Marshal.ReadIntPtr(ptr, 48);
            var bodyKind = Marshal.ReadInt32(ptr, 56);
            var callbackPtr = Marshal.ReadIntPtr(ptr, 64);
            var path = ReadIl2CppString(pathPtr);

            return
                "native={" +
                $"method={method}; service={service}; path={SanitizeInline(path)}; " +
                $"pathPtr=0x{pathPtr.ToInt64():X}; fieldsPtr=0x{fieldsPtr.ToInt64():X}; " +
                $"filesPtr=0x{filesPtr.ToInt64():X}; objectFieldsPtr=0x{objectFieldsPtr.ToInt64():X}; " +
                $"bodyKind={bodyKind}; callbackPtr=0x{callbackPtr.ToInt64():X}" +
                "}";
        }
        catch (Exception ex)
        {
            return $"native=<dump failed {ex.GetType().Name}: {SanitizeInline(ex.Message)}>";
        }
    }

    private static string ReadIl2CppString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "<null>";

        try
        {
            return IL2CPP.Il2CppStringToManaged(ptr) ?? "<null>";
        }
        catch (Exception ex)
        {
            return $"<string read failed 0x{ptr.ToInt64():X} {ex.GetType().Name}>";
        }
    }

    private static IntPtr TryGetIl2CppPtr(object? instance)
    {
        try
        {
            return instance is Il2CppObjectBase il2CppObject
                ? IL2CPP.Il2CppObjectBaseToPtrNotNull(il2CppObject)
                : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static string RedactHeaderValue(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";
        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("X-RNSIG", StringComparison.OrdinalIgnoreCase))
        {
            return "<redacted>";
        }

        return SanitizeInline(value);
    }

    private static string SanitizeInline(string value)
    {
        var redacted = SensitivePairRegex.Replace(value, "$1=<redacted>");
        redacted = BearerRegex.Replace(redacted, "Bearer <redacted>");
        redacted = SensitiveHeaderRegex.Replace(redacted, "$1: <redacted>");
        redacted = SensitiveJsonStringRegex.Replace(redacted, "\"$1\":\"<redacted>\"");
        redacted = redacted.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return redacted.Length <= 1200 ? redacted : redacted[..1200] + "...<truncated>";
    }

    private static void LogFailure(string source, Exception ex)
    {
        if (_failureLogCount++ < 10)
            Mod.Log.Warning($"[http-trace] {source} failed: {ex.GetType().Name}: {ex.Message}");
    }
}

internal static class UriPatches
{
    public static void HttpRequestCtorUri_Prefix(object __0)
    {
        HttpTracePatches.TraceUrl(__0?.ToString(), "BestHTTP.HTTPRequest..ctor(Uri)");
    }

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
        HttpTracePatches.TraceUrl(url, "BestHTTP.HTTPManager.SendRequest(string)");
    }

    public static void HttpManagerSendRequestObject_Prefix(object request)
    {
        RewriteRequestUri(request, "httpmanager-request-rewrite");
    }

    public static void UriStringCtor_Prefix(ref string uriString)
    {
        RewriteString(ref uriString, "uri-rewrite");
        HttpTracePatches.TraceUrl(uriString, "System.Uri..ctor(string)");
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

        Mod.Log.Msg($"[{label}] {uri} -> {rewritten}");
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
        Mod.Log.Msg($"[{label}] {uri} -> {rewritten}");
        return true;
    }

    private static bool TryRewriteUriField(object request, FieldInfo? field, string label)
    {
        if (field is null || field.IsInitOnly) return false;
        if (field.GetValue(request) is not Uri uri) return false;

        var rewritten = RewriteUri(uri);
        if (rewritten is null) return false;

        field.SetValue(request, rewritten);
        Mod.Log.Msg($"[{label}] {uri} -> {rewritten}");
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

        return new UriBuilder(uri)
        {
            Host = service + "." + host,
        }.Uri;
    }
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

internal static class AntiCheatPatches
{
    public static bool GenerateChallengeResponse_Prefix(string FIMBPCACMKJ, ref string __result)
    {
        __result = "AAAAAAAAAAAAAAAAAAAAAAAA";
        Mod.Log.Msg("[eac] using local challenge response for copied test client");
        return false;
    }
}

internal static class AntiTamperPatches
{
    public static bool CreateHileWarningCallback_Prefix(bool shouldQuit)
    {
        Mod.Log.Msg($"[hile] suppressed warning callback shouldQuit={shouldQuit}");
        return false;
    }

    public static bool UnknownDllDetectedCallback_Prefix()
    {
        Mod.Log.Msg("[cheatmanager] suppressed unknown DLL fatal callback");
        return false;
    }
}

internal static class FileHashCheckerPatches
{
    public static bool InitializeCallback_Prefix(object? result)
    {
        Mod.Log.Msg($"[filecheck] suppressed verifier callback result={result?.GetType().FullName ?? "null"}");
        return false;
    }
}

internal static class ToxModPatches
{
    public static bool CanInitializeToxMod_Prefix(ref bool __result)
    {
        __result = false;
        Mod.Log.Msg("[toxmod] disabled local ToxMod init");
        return false;
    }

    public static bool PhotonVoiceCreated_Prefix()
    {
        Mod.Log.Msg("[toxmod] skipped PhotonVoiceCreated");
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

internal static class QuitTracePatches
{
    public static bool ApplicationQuit_Prefix()
    {
        Write("[quit-trace] UnityEngine.Application.Quit()");
        WriteStack();
        return true;
    }

    public static bool ApplicationQuitInt_Prefix(int exitCode)
    {
        Write($"[quit-trace] UnityEngine.Application.Quit({exitCode})");
        WriteStack();
        if (exitCode == 1550502249 || exitCode == 533223478)
        {
            Write($"[quit-blocked] UnityEngine.Application.Quit fatal client exit {exitCode}");
            return false;
        }
        return true;
    }

    public static bool SessionManagerTryApplicationQuit_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)}");
        WriteStack();
        return true;
    }

    public static bool SessionManagerTryApplicationQuitInt_Prefix(int DKOGJICFAEP, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)}({DKOGJICFAEP})");
        WriteStack();
        return true;
    }

    public static bool SessionManagerFatalApplicationQuit_Prefix(int DKOGJICFAEP, string EPAIMCEIMPA, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)}({DKOGJICFAEP}, {EPAIMCEIMPA})");
        WriteStack();
        return true;
    }

    public static void ApplicationWantsToQuit_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)} wants-to-quit handler entered");
        WriteStack();
    }

    public static void CreateExitTask_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)} creating exit task");
        WriteStack();
    }

    private static string FormatOriginal(MethodBase method)
    {
        return $"{method.DeclaringType?.FullName}.{method.Name}";
    }

    private static void Write(string message)
    {
        try { Mod.Log.Msg(message); } catch { }
    }

    private static void WriteStack()
    {
        try
        {
            Write("[quit-trace-stack] " + new StackTrace(skipFrames: 2, fNeedFileInfo: false));
        }
        catch (Exception ex)
        {
            Write("[quit-trace-stack] failed: " + ex.Message);
        }
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

    public static bool ApplicationQuit_Prefix()
    {
        Write("[quit] UnityEngine.Application.Quit()");
        WriteStack();
        return true;
    }

    public static bool ApplicationQuitInt_Prefix(int exitCode)
    {
        Write($"[quit] UnityEngine.Application.Quit({exitCode})");
        WriteStack();
        return true;
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
        Write($"[quit-trace] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
        WriteStack();
        return true;
    }

    public static bool SessionManagerTryApplicationQuitInt_Prefix(int DKOGJICFAEP, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}({DKOGJICFAEP})");
        WriteStack();
        return true;
    }

    public static bool SessionManagerFatalApplicationQuit_Prefix(int DKOGJICFAEP, string EPAIMCEIMPA, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}({DKOGJICFAEP}, {EPAIMCEIMPA})");
        WriteStack();
        return true;
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

// Log-only diagnostics for the stuck first-dorm "Change Username?" dialog.
// See the registration site in OnLateInitializeMelon and
// Cfg.TraceRegistrationDialog. Nothing here changes game behaviour — every
// hook only writes to the diagnostics log.
internal static class RegistrationPatches
{
    private static bool s_loggedFullyReg;

    // RRUI.Data.NUX.RegistrationModel.IsFullyRegistered() — postfix reads
    // (does NOT modify) the return value. False ⇒ the dorm raises the
    // "Change Username?" prompt; true ⇒ no prompt. Logged once so the file
    // stays readable (it's polled from several call sites every boot).
    public static void IsFullyRegistered_Postfix(bool __result)
    {
        if (!s_loggedFullyReg)
        {
            s_loggedFullyReg = true;
            DiagnosticPatches.Write($"[reg-trace] RegistrationModel.IsFullyRegistered() = {__result}  ({(__result ? "no prompt — fully registered" : "NOT registered → dorm will raise the Change Username? prompt")})");
        }
    }

    // DormroomSceneManager.<PromptForRegistration>b__6_* lambdas. b__6_1 is
    // the dialog-response handler (a dialog was shown). Names reach the
    // prefix via __originalMethod so we can tell which branch ran.
    public static void PromptLambda_Prefix(MethodBase __originalMethod)
    {
        DiagnosticPatches.Write($"[reg-trace] DormroomSceneManager prompt lambda fired: {__originalMethod.Name}");
    }

    // ConfirmUIDialog button handlers. If one of these fires when the user
    // taps a dialog button but the modal stays open, the break is in the
    // resolve/hide completer (UIDialog`1.Respond's close path) — not input
    // routing. "Okay" on the screenshot is Button_Affirmative.
    public static bool ButtonAffirmative_Prefix(object __instance)
    {
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_Affirmative pressed (this is 'Okay')");

        if (!IsRegistrationWarningDialog(__instance))
        {
            return true;
        }

        if (TryResolveAffirmativeWithoutClose(__instance))
        {
            DiagnosticPatches.Write("[registration-fix] resolved first-dorm registration warning without the broken close-before-resolve path");
            return false;
        }

        DiagnosticPatches.Write("[registration-fix] registration warning matched, but reflection resolve failed; falling back to original button handler");
        return true;
    }

    public static void ButtonNegative_Prefix() =>
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_Negative pressed");
    public static void ButtonNotNow_Prefix() =>
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_NotNow pressed");
    public static void ButtonCancel_Prefix() =>
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_Cancel pressed");

    private static bool IsRegistrationWarningDialog(object instance)
    {
        var title = ReadTextField(instance, "titleText");
        var body = ReadTextField(instance, "bodyText");

        return Contains(title, "Change Username")
            && Contains(body, "permanently lost")
            && Contains(body, "password");
    }

    private static bool TryResolveAffirmativeWithoutClose(object instance)
    {
        try
        {
            var respond = AccessTools.Method(instance.GetType(), "Respond");
            if (respond is null)
            {
                respond = AccessTools.Method(instance.GetType().BaseType, "Respond");
            }

            if (respond is null)
            {
                return false;
            }

            var parameters = respond.GetParameters();
            if (parameters.Length != 2)
            {
                return false;
            }

            var response = CreateAffirmativeResponse(parameters[0].ParameterType);
            respond.Invoke(instance, new[] { response, (object)false });
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticPatches.Write($"[registration-fix] resolve failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static object CreateAffirmativeResponse(Type responseType)
    {
        if (responseType.IsEnum)
        {
            return Enum.ToObject(responseType, 1);
        }

        return Convert.ChangeType(1, responseType);
    }

    private static string? ReadTextField(object instance, string fieldName)
    {
        try
        {
            var field = AccessTools.Field(instance.GetType(), fieldName);
            var textObject = field?.GetValue(instance);
            if (textObject is null)
            {
                return null;
            }

            var property = AccessTools.Property(textObject.GetType(), "text");
            if (property?.GetValue(textObject) is string text)
            {
                return text;
            }

            var getter = AccessTools.Method(textObject.GetType(), "get_text");
            return getter?.Invoke(textObject, Array.Empty<object>()) as string;
        }
        catch (Exception ex)
        {
            DiagnosticPatches.Write($"[registration-fix] could not read {fieldName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static bool Contains(string? value, string fragment)
    {
        return value?.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

// Trace hooks for the watch's room-join flow. Registered by
// Mod.RegisterJoinTracePatches — see that method for the verified call
// chain. Everything here is log-only; no prefix returns false, so the
// game's own join logic runs unchanged.
internal static class JoinPatches
{
    // AGUI.StackedUI.HomeScreenFlow.Button_DormRoom() — the watch's
    // "Dorm Room" button. If this never fires, the button's UnityEvent
    // binding is the problem, not the join chain.
    public static void ButtonDormRoom_Prefix()
    {
        DiagnosticPatches.Write("[join-trace] >>> watch DORM ROOM button pressed (HomeScreenFlow.Button_DormRoom)");
    }

    // Any SessionManager.RunJoinRoom overload. We DON'T read argument
    // values here: Harmony's `object[] __args` forces a trampoline that
    // boxes the obfuscated IL2CPP reference-type params (KMKPEOGJDFK,
    // RoomInstance, …), and MonoMod fatal-CLRs (0x80131506) compiling it
    // when the boot-time dorm auto-join first calls this method. Logging
    // the overload's parameter *types* (pure reflection metadata, no value
    // boxing) is enough to tell which overload fired — the entry overload
    // (String,…) is the "DormRoom" press; the others are inner legs.
    public static void RunJoinRoom_Prefix(MethodBase __originalMethod)
    {
        DiagnosticPatches.Write($"[join-trace] SessionManager.RunJoinRoom  [{Sig(__originalMethod)}]");
    }

    // OJMCBOKJFOF.NHBPIIGDAJP(name|id) — the get-room-by-name / by-id
    // resolver that feeds the join. __0 is the single first param: a room
    // NAME string (String overload) or room ID (Int64 overload). Reading
    // it via `object __0` is safe — it's a string/long, not one of the
    // obfuscated ref-types that crashed `object[] __args`. This is the
    // value we need to compare boot (works) vs button (fails).
    public static void Goto_Prefix(MethodBase __originalMethod, object __0)
    {
        var arg = __0?.ToString() ?? "<null>";
        DiagnosticPatches.Write($"[join-trace] /goto resolve {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}(\"{arg}\")  [{Sig(__originalMethod)}]");
    }

    // RunJoinRoom promise-chain callbacks (SessionManager <>c lambdas).
    // b__165_1 is the reject handler that raises the "An error occured /
    // contact recroom.happyfox.com" toast — so if THIS name appears, the
    // join failed; the success legs (other b__*) firing means it advanced.
    // (Same __args/0x80131506 constraint as above — name only.)
    public static void JoinCallback_Prefix(MethodBase __originalMethod)
    {
        DiagnosticPatches.Write($"[join-trace] RunJoinRoom callback {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
    }

    // Dedicated capture for the reject handler b__165_1(string error) —
    // the answer to "why did the join fail". Single string param, so
    // `object __0` is safe (same as OnCustomAuthenticationFailed). This
    // is the string that becomes the happyfox toast.
    public static void JoinError_Prefix(object __0)
    {
        var err = __0?.ToString() ?? "<null>";
        DiagnosticPatches.Write($"[join-trace] !!! RunJoinRoom REJECTED — error=\"{err}\"  (raises the happyfox toast)");
    }

    // Parameter-type list from reflection metadata only — never reads
    // instance values, so it can't trigger the MonoMod trampoline crash.
    private static string Sig(MethodBase m)
    {
        var ps = m.GetParameters();
        var names = new List<string>(ps.Length);
        foreach (var p in ps) names.Add(p.ParameterType.Name);
        return $"{ps.Length} args: {string.Join(",", names)}";
    }
}

