// Rec Royale night mode — client-side, opt-in.
//
// Rec Royale never shipped a night variant, but the March-2023 client
// carries everything needed to build one from the game's own tooling:
//
//   • Every room scene owns a RecRoomSceneManager (name-preserved field
//     `sceneMoodSettings` → RecRoom.Core.Scene.SceneMoodSettings). That
//     object is the room "mood" engine behind the Maker Pen sky/sun/fog
//     settings: it holds `availableMoods` (MoodSetting[]), `defaultMood`
//     and `CurrentMood`, plus blenders for skybox, sun, fog, bloom and
//     colour grading.
//   • SceneMoodSettings.MLLBALMKEGJ(MoodSetting) is the apply step
//     (ISIL: SceneMoodSettings.txt:3025-3581). It pushes the mood's six
//     settings blocks into the blenders (ALCMNGKOEBK), swaps the URP
//     VolumeProfile (OEKNGEMDMDB), sets RenderSettings.skybox to the
//     global MoodConfig skybox material (LNMDIIFOFPD.NHAEHKJINFH, shader
//     "Custom/SkyBox"), re-aims RenderSettings.sun, sets every
//     ReflectionProbe.customBakedTexture to the mood's baked cubemap and
//     force-updates the blenders. It does NOT touch the networked mood
//     index, so calling it is purely local.
//   • The build ships ready-made night moods as Resources assets
//     (resources.assets; ResourceManager table in globalgamemanagers):
//     Night_Calm_Outdoor_Mood ("Calm Night"), Night_Spooky_Outdoor_Mood
//     ("Spooky Night"), Night_Wild_Outdoor_Mood ("Wild Night"),
//     Night_StuntRunner_Outdoor_Mood and OuterSpace_Mood ("Outer Space").
//   • The only Rec Royale map in this build is
//     Assets/Activities/RecRoyale/Maps/RecRoyale_Frontier/RecRoyale_Frontier.unity.
//
// RRO SCENES HAVE NO ROOM DATA BLOB. The mood engine's `isEnabled` flag
// (SceneMoodSettings+0x38) is only switched on by the room-data loader
// (EIMFCPIHEPF.JPKEMACDNCF, ISIL :2136-2310), which runs for custom rooms
// only. While it is off, get_CurrentMood returns null and nothing ticks.
// The loader's bring-up sequence is:
//     LKIKMLNPCFK(false)   // SetEnabled(false): applies a null mood
//     LIEDFOLBGHL()        // capture the scene's own skybox/sun/fog as the
//                          // blenders' "previous" data (checks shader name
//                          // "Custom/SkyBox" → HasRecRoomSkyboxShader)
//     LKIKMLNPCFK(true)    // SetEnabled(true)
// This mod replays exactly that before applying the night mood.
//
// Deliberately NOT used: SceneMoodSettings.IPAMPMBDPNB(Guid) /
// set_CurrentRoomMoodIndex. `_currentRoomMoodIndex` is a
// RecRoom.Networking.SynchronizedFields DFAOKLDJPEL<int>, i.e. it
// replicates to every player in the instance. Applying the mood directly
// keeps night mode a per-client visual choice.
//
// Weapon flashlights: WeaponFlashlightController.get_FlashlightAllowed
// (ISIL: WeaponFlashlightController.txt:3-75) is
// `Matchmaking.FBELDNLEKAC() != null && [+0x28] == 15` — a game-mode
// gate for an activity that isn't Rec Royale. A Harmony postfix forces it
// true while night mode is active, so weapons picked up in the dark get
// the game's own flashlight prefab.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DorkNet.ClientMod;

internal static class RecRoyaleNight
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    // Runtime state. `Active` starts from Cfg.RecRoyaleNightMode and flips
    // with the hotkey; `_inRoyale` tracks whether the current scene is one
    // of Cfg.RecRoyaleNightScenes.
    public static bool Active { get; private set; }
    private static bool _inRoyale;
    private static string _sceneName = "";
    private static bool _appliedThisScene;
    private static int _frame;
    private static int _sceneFrames;         // frames since the Royale scene loaded
    private static int _reassertFrames;      // frames since the last re-apply

    // Per-scene bring-up bookkeeping so toggling off can put things back.
    private static bool _engineWasDisabled;  // we enabled the mood engine ourselves
    private static object? _originalSkybox;  // RenderSettings.skybox before we touched it
    private static bool _sunWasNull;         // we assigned RenderSettings.sun ourselves
    private static object? _originalFog;     // MoodSetting.FogSettings before we swapped it
    private static object? _fogPatchedMood;  // the mood asset whose FogSettings we replaced
    private static object? _darkPatchedMood; // the mood asset whose exposure/sun/ambient we changed
    private static float? _origExposure, _origSunIntensity, _origAmbient;
    private static bool _diagLogged;

    private const int PollEveryFrames = 30;          // ~0.5 s @ 60 fps
    private const int SettleFrames = 90;             // let the scene's own lighting settle first
    private const int PollGiveUpFrames = 60 * 120;   // stop polling after ~2 min
    private const int ReassertEveryFrames = 60 * 10; // re-apply every ~10 s

    // Resolved reflection handles (lazy, cached).
    private static bool _resolved;
    private static Type? _sceneManagerType;
    private static PropertyInfo? _sceneManagerInstance;
    private static FieldInfo? _sceneMoodSettingsField;
    private static PropertyInfo? _sceneMoodSettingsProp;
    private static Type? _moodSettingType;
    private static MethodInfo? _applyMood;       // MLLBALMKEGJ(MoodSetting)
    private static MethodInfo? _setEnabled;      // LKIKMLNPCFK(bool)
    private static MethodInfo? _capture;         // LIEDFOLBGHL()
    private static FieldInfo? _isEnabled;        // isEnabled
    private static PropertyInfo? _roomMoodsEnabled;
    private static PropertyInfo? _hasSkyShader;
    private static PropertyInfo? _hasSunSource;
    private static PropertyInfo? _volume;
    private static FieldInfo? _availableMoods;
    private static PropertyInfo? _currentMood;
    private static PropertyInfo? _friendlyName;
    private static PropertyInfo? _renderSkybox;  // UnityEngine.RenderSettings.skybox
    private static PropertyInfo? _renderSun;     // UnityEngine.RenderSettings.sun
    private static Type? _lightType;             // UnityEngine.Light
    private static Type? _fogSettingsType;       // FogMoodSettings
    private static MethodInfo? _fogCapture;      // FogMoodSettings.NLDDIDEIEFH() — read RenderSettings fog
    private static PropertyInfo? _moodFog;       // MoodSetting.FogSettings
    private static Type? _materialType;          // UnityEngine.Material
    private static Type? _shaderType;            // UnityEngine.Shader
    private static Type? _moodConfigHolder;      // LNMDIIFOFPD (static MoodConfig holder)
    private static Type? _cameraDataType;        // UnityEngine.Rendering.Universal.UniversalAdditionalCameraData
    private static object? _ourSkybox;           // Material we created for the mood skybox
    private static object? _nightMood;           // MoodSetting interop wrapper
    private static string _nightMoodLoadedName = "";

    // Hotkey
    private static bool _inputResolved;
    private static MethodInfo? _getKeyDown;
    private static object? _toggleKey;

    // ── registration ──────────────────────────────────────────────────
    public static void Register(HarmonyLib.Harmony harmony)
    {
        Active = Mod.Cfg.RecRoyaleNightMode;
        Mod.Log.Msg($"[royale-night] enabled={Active} mood={Mod.Cfg.RecRoyaleNightMood} " +
                    $"scenes=[{string.Join(",", Mod.Cfg.RecRoyaleNightScenes)}] " +
                    $"flashlights={Mod.Cfg.RecRoyaleNightFlashlights} " +
                    $"hotkey={(string.IsNullOrWhiteSpace(Mod.Cfg.RecRoyaleNightToggleKey) ? "<none>" : Mod.Cfg.RecRoyaleNightToggleKey)}");

        if (Mod.Cfg.RecRoyaleNightFlashlights)
            TryPatchFlashlightGate(harmony);
    }

    // POSTFIX on WeaponFlashlightController.get_FlashlightAllowed. The
    // interop property carries the obfuscated name (KIMMMCCKMJL in the
    // 2023-03-21 build) while the il2cpp method keeps `get_FlashlightAllowed`,
    // so resolve by name first and fall back to "the only read-only bool
    // property declared on the type".
    private static void TryPatchFlashlightGate(HarmonyLib.Harmony harmony)
    {
        try
        {
            var type = Mod.ResolveType("WeaponFlashlightController");
            if (type is null)
            {
                Mod.Log.Warning("[patch-miss] WeaponFlashlightController: type not found");
                return;
            }

            MethodInfo? getter = AccessTools.PropertyGetter(type, "FlashlightAllowed")
                              ?? AccessTools.PropertyGetter(type, "KIMMMCCKMJL");
            if (getter is null)
            {
                PropertyInfo? only = null;
                int count = 0;
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (p.PropertyType != typeof(bool) || !p.CanRead || p.CanWrite || p.GetIndexParameters().Length != 0) continue;
                    only = p; count++;
                }
                if (count == 1) getter = only!.GetGetMethod(true);
            }
            if (getter is null)
            {
                Mod.Log.Warning("[patch-miss] WeaponFlashlightController.get_FlashlightAllowed");
                return;
            }

            var postfix = typeof(RecRoyaleNight).GetMethod(nameof(FlashlightAllowed_Postfix), BindingFlags.Public | BindingFlags.Static)!;
            harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception ex)
        {
            Mod.Log.Error($"[patch-fail] WeaponFlashlightController.get_FlashlightAllowed: {ex.Message}");
        }
    }

    public static void FlashlightAllowed_Postfix(ref bool __result)
    {
        if (Active && _inRoyale) __result = true;
    }

    // ── lifecycle (called from Mod) ───────────────────────────────────
    public static void OnSceneLoaded(string sceneName)
    {
        _sceneName = sceneName ?? "";
        var was = _inRoyale;
        _inRoyale = MatchesScene(_sceneName);
        if (_inRoyale)
        {
            _appliedThisScene = false;
            _sceneFrames = 0;
            _reassertFrames = 0;
            _engineWasDisabled = false;
            _originalSkybox = null;
            _sunWasNull = false;
            _diagLogged = false;
            RestoreMoodPatches();
            Mod.Log.Msg($"[royale-night] scene '{_sceneName}' loaded — night mode {(Active ? "will apply" : "is off (hotkey to enable)")}");
        }
        else if (was)
        {
            _appliedThisScene = false;
            _engineWasDisabled = false;
            _originalSkybox = null;
            _sunWasNull = false;
            RestoreMoodPatches();
        }
    }

    public static void Tick()
    {
        _frame++;
        PollHotkey();

        if (!_inRoyale || !Active) return;
        _sceneFrames++;
        if (_frame % PollEveryFrames != 0) return;

        if (!_appliedThisScene)
        {
            if (_sceneFrames < SettleFrames || _sceneFrames > PollGiveUpFrames) return;
            if (TryApply(true)) _appliedThisScene = true;
            return;
        }

        // Cheap safeguard: the game re-applies its own mood on quality
        // changes / studio events. Re-assert every ~10 s; when the blenders
        // are already at target this is visually a no-op.
        _reassertFrames += PollEveryFrames;
        if (_reassertFrames >= ReassertEveryFrames)
        {
            _reassertFrames = 0;
            TryApply(true);
        }
    }

    public static void Toggle()
    {
        Active = !Active;
        Mod.Log.Msg($"[royale-night] toggled {(Active ? "ON" : "OFF")}");
        if (!_inRoyale) return;
        if (TryApply(Active)) _appliedThisScene = Active;
        else if (!Active) _appliedThisScene = false;
    }

    // ── the actual apply ──────────────────────────────────────────────
    // night=true  → bring the mood engine up if needed, apply the night mood
    // night=false → restore: scene mood if the engine was already running,
    //               otherwise disable the engine again + put the skybox back
    private static bool TryApply(bool night)
    {
        try
        {
            if (!_resolved && !Resolve()) return false;
            if (_applyMood is null) return false;

            var manager = GetSceneManager();
            if (manager is null) { Diag("scene manager instance is null"); return false; }

            var moods = _sceneMoodSettingsField?.GetValue(manager) ?? _sceneMoodSettingsProp?.GetValue(manager);
            if (moods is null) { Diag("sceneMoodSettings is null"); return false; }

            // `isEnabled` reads differently through the interop property vs the
            // field (seen True/False for the same state), so don't gate on it.
            // RoomMoodsEnabled (= isEnabled && defaultMood != null) is computed
            // game-side and is false for every RRO scene; bring the engine up
            // exactly once per Royale scene whenever it is not true.
            var enabled = GetMember(moods, "isEnabled") is true;
            var roomMoodsEnabled = Read(_roomMoodsEnabled, moods) is true;
            LogState(moods, enabled);

            if (night)
            {
                var target = GetNightMood();
                if (target is null) return false;

                if (!roomMoodsEnabled && !_engineWasDisabled)
                {
                    // Replay the room-data loader's bring-up. Remember the
                    // scene's own skybox so toggle-off can restore it.
                    if (_originalSkybox is null && _renderSkybox is not null)
                        _originalSkybox = _renderSkybox.GetValue(null);
                    if (_setEnabled is null || _capture is null)
                    {
                        Mod.Log.Warning("[royale-night] mood engine is disabled in this scene and SetEnabled/Capture are unresolved — cannot bring it up");
                        return false;
                    }

                    // RRO scenes leave RenderSettings.sun unset, and the engine
                    // skips every sun change (direction, colour, intensity) when
                    // HasSunSource is false. Point it at the scene's directional
                    // light BEFORE capture so the flag comes out true.
                    EnsureSun();

                    // Keep the scene's own fog unless asked otherwise: replace
                    // the mood's FogSettings with a block captured from the
                    // current RenderSettings before anything changes them.
                    if (!Mod.Cfg.RecRoyaleNightFog) PreserveFog(target);

                    // Baked lightmaps can't be dimmed, so darken the frame via
                    // the mood's own post-exposure and tone down sun/ambient.
                    PatchDarkness(target);

                    _setEnabled.Invoke(moods, new object[] { false });
                    _capture.Invoke(moods, null);

                    // The per-frame update (CEKLEBHDAHA, ISIL :3895-4031) returns
                    // at once while get_CurrentMood() is null, and the one-shot
                    // apply only sets blend TARGETS — the update is what writes
                    // sky colours, rotates the sun and pushes exposure. In RRO
                    // scenes CurrentMood is null because defaultMood is unset.
                    // Register the night mood as this scene's defaultMood and
                    // sole availableMoods entry so the engine runs like a
                    // one-mood custom room.
                    InstallSceneMood(moods, target);

                    _setEnabled.Invoke(moods, new object[] { true });
                    _engineWasDisabled = true;
                    ProbeCircuitsSupport();
                    var cm = _currentMood?.GetValue(moods);
                    Mod.Log.Msg($"[royale-night] mood engine brought up (skyShader={Read(_hasSkyShader, moods)} sun={Read(_hasSunSource, moods)} volume={!(Read(_volume, moods) is null)} " +
                                $"roomMoodsEnabled={Read(_roomMoodsEnabled, moods)} currentMood={(cm is null || IsUnityNull(cm) ? "<null>" : Describe(cm))})");

                    // First apply swaps RenderSettings.skybox to
                    // MoodConfig.SkyBoxMaterial. In RRO scenes that material is
                    // not loaded (it lives in the custom-room scene's shared
                    // assets), so the skybox ends up null → black sky and the
                    // engine's HasRecRoomSkyboxShader / HasSunSource stay false
                    // (both need shader "Custom/SkyBox" on RenderSettings.skybox).
                    // Supply a material built from that shader, re-capture so
                    // the flags flip, then fall through to the real apply.
                    _applyMood.Invoke(moods, new[] { target });
                    EnsureMoodSkybox();
                    _capture.Invoke(moods, null);
                    Mod.Log.Msg($"[royale-night] re-captured (skyShader={Read(_hasSkyShader, moods)} sun={Read(_hasSunSource, moods)} skybox={DescribeSkybox()})");
                    EnsurePostProcessing(moods);
                }

                _applyMood.Invoke(moods, new[] { target });
                if (Mod.Cfg.RecRoyaleNightMatchSunLights) MatchOtherSunLights();
                if (Mod.Cfg.RecRoyaleNightDisableFog && !Mod.Cfg.RecRoyaleNightFog) ForceFogOff();
                HideObjects();
                Mod.Log.Msg($"[royale-night] applied mood '{Describe(target)}' in '{_sceneName}'");
                return true;
            }

            // restore
            if (_engineWasDisabled)
            {
                RestoreOtherSunLights();
                ShowObjects();
                RestoreFogState();
                _setEnabled?.Invoke(moods, new object[] { false }); // applies the captured "previous" data
                UninstallSceneMood(moods);
                if (_originalSkybox is not null && _renderSkybox is not null)
                    _renderSkybox.SetValue(null, _originalSkybox);
                if (_sunWasNull && _renderSun is not null)
                    _renderSun.SetValue(null, null);
                RestoreMoodPatches();
                _engineWasDisabled = false;
                _originalSkybox = null;
                _sunWasNull = false;
                Mod.Log.Msg($"[royale-night] mood engine disabled again, scene skybox/sun restored in '{_sceneName}'");
                return true;
            }

            var current = _currentMood?.GetValue(moods);
            if (current is null || IsUnityNull(current)) { Diag("no CurrentMood to restore"); return false; }
            _applyMood.Invoke(moods, new[] { current });
            Mod.Log.Msg($"[royale-night] restored scene mood '{Describe(current)}' in '{_sceneName}'");
            return true;
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[royale-night] apply failed: {(ex.InnerException ?? ex).Message}");
            return false;
        }
    }

    private static void LogState(object moods, bool enabled)
    {
        if (_diagLogged) return;
        _diagLogged = true;
        try
        {
            var arr = GetMember(moods, "availableMoods");
            var cur = _currentMood?.GetValue(moods);
            Mod.Log.Msg($"[royale-night] engine state: isEnabled={enabled} roomMoodsEnabled={Read(_roomMoodsEnabled, moods)} " +
                        $"availableMoods={Count(arr)} currentMood={(cur is null || IsUnityNull(cur) ? "<null>" : Describe(cur))} " +
                        $"skyShader={Read(_hasSkyShader, moods)} sun={Read(_hasSunSource, moods)} volume={!(Read(_volume, moods) is null)}");
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] state log failed: {ex.Message}"); }
    }

    private static object? Read(PropertyInfo? p, object o)
    {
        try { return p?.GetValue(o); } catch { return null; }
    }

    private static void Diag(string why)
    {
        if (_frame % (PollEveryFrames * 20) == 0) Mod.Log.Msg($"[royale-night] waiting: {why}");
    }

    private static bool Resolve()
    {
        _resolved = true;

        _sceneManagerType = Mod.ResolveType("RecRoomSceneManager");
        var moodSettingsType = Mod.ResolveType("RecRoom.Core.Scene.SceneMoodSettings");
        _moodSettingType = Mod.ResolveType("RecRoom.Core.Creation.SceneMoods.MoodSetting");
        if (_sceneManagerType is null || moodSettingsType is null || _moodSettingType is null)
        {
            Mod.Log.Warning($"[royale-night] types missing — sceneManager={_sceneManagerType is not null} " +
                            $"sceneMoodSettings={moodSettingsType is not null} moodSetting={_moodSettingType is not null}");
            return false;
        }

        // RecRoomSceneManager.Instance: the il2cpp getter is get_Instance but
        // the interop property name is obfuscated (PDCOCJBICGC in 2023-03-21).
        // Pick the static property whose type is RecRoomSceneManager.
        _sceneManagerInstance = AccessTools.Property(_sceneManagerType, "Instance");
        if (_sceneManagerInstance is null)
        {
            foreach (var p in _sceneManagerType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (p.PropertyType == _sceneManagerType && p.CanRead && p.GetIndexParameters().Length == 0)
                { _sceneManagerInstance = p; break; }
            }
        }

        _sceneMoodSettingsField = AccessTools.Field(_sceneManagerType, "sceneMoodSettings");
        if (_sceneMoodSettingsField is null)
        {
            foreach (var p in _sceneManagerType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.PropertyType == moodSettingsType && p.CanRead) { _sceneMoodSettingsProp = p; break; }
        }

        _currentMood      = AccessTools.Property(moodSettingsType, "CurrentMood");
        _roomMoodsEnabled = AccessTools.Property(moodSettingsType, "RoomMoodsEnabled");
        _hasSkyShader     = AccessTools.Property(moodSettingsType, "HasRecRoomSkyboxShader");
        _hasSunSource     = AccessTools.Property(moodSettingsType, "HasSunSource");
        _volume           = AccessTools.Property(moodSettingsType, "Volume");
        _availableMoods   = AccessTools.Field(moodSettingsType, "availableMoods");
        _isEnabled        = AccessTools.Field(moodSettingsType, "isEnabled");
        _friendlyName     = AccessTools.Property(_moodSettingType, "FriendlyName");

        // Apply step: MLLBALMKEGJ(MoodSetting) in 2023-03-21. Fallback: the
        // only non-setter void method taking exactly one MoodSetting.
        _applyMood = AccessTools.Method(moodSettingsType, "MLLBALMKEGJ", new[] { _moodSettingType });
        if (_applyMood is null)
        {
            foreach (var m in moodSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.ReturnType != typeof(void) || m.Name.StartsWith("set_", StringComparison.Ordinal)) continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == _moodSettingType) { _applyMood = m; break; }
            }
        }

        // SetEnabled(bool): LKIKMLNPCFK in 2023-03-21. Fallback: the only
        // public non-setter void(bool) declared on the type.
        _setEnabled = AccessTools.Method(moodSettingsType, "LKIKMLNPCFK", new[] { typeof(bool) });
        if (_setEnabled is null)
        {
            MethodInfo? only = null; int n = 0;
            foreach (var m in moodSettingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.ReturnType != typeof(void) || m.Name.StartsWith("set_", StringComparison.Ordinal)) continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(bool)) { only = m; n++; }
            }
            if (n == 1) _setEnabled = only;
        }

        // Capture current scene lighting: LIEDFOLBGHL() in 2023-03-21. Too
        // many void() methods on the type to pick by shape — name only.
        _capture = AccessTools.Method(moodSettingsType, "LIEDFOLBGHL", Type.EmptyTypes);

        var renderSettings = Mod.ResolveType("UnityEngine.RenderSettings");
        _renderSkybox = renderSettings is null ? null : AccessTools.Property(renderSettings, "skybox");
        _renderSun    = renderSettings is null ? null : AccessTools.Property(renderSettings, "sun");
        _lightType    = Mod.ResolveType("UnityEngine.Light");

        // Fog preservation: FogMoodSettings() + NLDDIDEIEFH() reads
        // RenderSettings.fog/fogStartDistance/fogEndDistance/fogColor into the
        // block (ISIL FogMoodSettings.txt, called right after the ctor in
        // LIEDFOLBGHL). MoodSetting.FogSettings is the serialized reference
        // the apply step hands to the fog blender.
        _fogSettingsType = Mod.ResolveType("RecRoom.Core.Creation.SceneMoods.MoodBlendData.FogMoodSettings");
        _fogCapture = _fogSettingsType is null ? null : AccessTools.Method(_fogSettingsType, "NLDDIDEIEFH", Type.EmptyTypes);
        _moodFog    = AccessTools.Property(_moodSettingType, "FogSettings");

        _materialType     = Mod.ResolveType("UnityEngine.Material");
        _shaderType       = Mod.ResolveType("UnityEngine.Shader");
        _moodConfigHolder = Mod.ResolveType("LNMDIIFOFPD");
        _cameraDataType   = Mod.ResolveType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");

        var ok = _sceneManagerInstance is not null
              && (_sceneMoodSettingsField is not null || _sceneMoodSettingsProp is not null)
              && _applyMood is not null;
        Mod.Log.Msg($"[royale-night] resolved: instance={_sceneManagerInstance is not null} " +
                    $"moodSettings={_sceneMoodSettingsField is not null || _sceneMoodSettingsProp is not null} " +
                    $"apply={_applyMood is not null} setEnabled={_setEnabled is not null} capture={_capture is not null} " +
                    $"isEnabled={_isEnabled is not null} currentMood={_currentMood is not null} skybox={_renderSkybox is not null} " +
                    $"sun={_renderSun is not null} light={_lightType is not null} fogCapture={_fogCapture is not null} moodFog={_moodFog is not null}");
        return ok;
    }

    // RenderSettings.sun ← the scene's directional Light when unset. Frontier
    // ships without the reference ("Sun Source reference has not been set in
    // Lighting settings" is the engine's own warning), so nothing sun-related
    // from the mood would otherwise apply.
    private static void EnsureSun()
    {
        try
        {
            if (_renderSun is null || _lightType is null) return;
            var current = _renderSun.GetValue(null);
            if (current is not null && !IsUnityNull(current))
            {
                Mod.Log.Msg($"[royale-night] RenderSettings.sun already set: '{ObjectName(current)}'");
                return;
            }

            var unityObject = Mod.ResolveType("UnityEngine.Object");
            var il2cppLight = Il2CppTypeOf(_lightType);
            if (unityObject is null || il2cppLight is null) return;

            object? directional = null;
            foreach (var m in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "FindObjectsOfType" || m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1) continue;
                var arr = m.Invoke(null, new object[] { il2cppLight });
                var n = Count(arr);
                float best = -1f;
                for (int i = 0; i < n; i++)
                {
                    var item = Item(arr, i);
                    if (item is null) continue;
                    var light = AsType(item, _lightType);
                    if (light is null) continue;
                    // LightType.Directional == 1
                    var kind = GetMember(light, "type");
                    if (kind is null || Convert.ToInt32(kind) != 1) continue;
                    var intensity = GetMember(light, "intensity") is float f ? f : 0f;
                    if (intensity > best) { best = intensity; directional = light; }
                }
                break;
            }

            if (directional is null)
            {
                Mod.Log.Warning("[royale-night] no directional Light found in scene — sun direction/colour will not change");
                return;
            }
            _renderSun.SetValue(null, directional);
            _sunWasNull = true;
            Mod.Log.Msg($"[royale-night] RenderSettings.sun ← '{ObjectName(directional)}'");
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] EnsureSun failed: {(ex.InnerException ?? ex).Message}"); }
    }

    // Swap the mood asset's FogSettings for a block captured from the
    // scene's current RenderSettings so applying the mood leaves fog as-is.
    // The asset is shared for the session, so RestoreFog puts the original
    // block back when we leave or toggle off.
    private static void PreserveFog(object mood)
    {
        try
        {
            if (_fogSettingsType is null || _fogCapture is null || _moodFog is null || !_moodFog.CanWrite)
            {
                Mod.Log.Warning("[royale-night] fog preservation unavailable (FogMoodSettings/NLDDIDEIEFH/FogSettings unresolved) — mood fog will apply");
                return;
            }
            if (ReferenceEquals(_fogPatchedMood, mood) && _originalFog is not null) return;

            var captured = Activator.CreateInstance(_fogSettingsType);
            if (captured is null) return;
            _fogCapture.Invoke(captured, null);
            if (Mod.Cfg.RecRoyaleNightDisableFog)
            {
                // The block has no exposed enabled flag; push it out of range so
                // even if the blender re-enables fog each frame it is invisible.
                SetMember(captured, "startDistance", 1_000_000f);
                SetMember(captured, "fadeDistance", 1_000_000f);
            }

            _originalFog = _moodFog.GetValue(mood);
            _fogPatchedMood = mood;
            _moodFog.SetValue(mood, captured);
            Mod.Log.Msg("[royale-night] mood fog replaced with the scene's own fog settings");
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] PreserveFog failed: {(ex.InnerException ?? ex).Message}"); }
    }

    private static void RestoreFog()
    {
        try
        {
            if (_fogPatchedMood is not null && _originalFog is not null && _moodFog is not null && _moodFog.CanWrite)
                _moodFog.SetValue(_fogPatchedMood, _originalFog);
        }
        catch { }
        _fogPatchedMood = null;
        _originalFog = null;
    }

    private static void RestoreMoodPatches()
    {
        RestoreFog();
        RestoreDarkness();
        RestoreOtherSunLights();
        // Scene objects die with the scene; just forget them.
        _probed = false;
        _hidden.Clear();
        _hideScanned = false;
        _fogStateSaved = false;
        _origFogEnabled = null;
        // The scene's SceneMoodSettings dies with the scene; just forget it.
        _sceneMoodInstalled = false;
        _sceneMoodsObj = null;
        _origDefaultMood = _origAvailableMoods = null;
    }

    // The engine drives exactly one light: RenderSettings.sun (Frontier:
    // "SkyboxLight", the sun-disc light). The map's actual key light is a
    // separate directional Light it never touches, so daylight keeps
    // casting. Copy the engine-driven sun's rotation and colour onto every
    // other directional light and scale their intensity down. Originals are
    // kept per light and restored on toggle-off / scene change.
    private sealed class LightState
    {
        public object Light = null!;
        public object? Intensity, Color, Rotation, ShadowStrength;
    }
    private static readonly List<LightState> _otherSuns = new();

    private static void MatchOtherSunLights()
    {
        try
        {
            if (_renderSun is null || _lightType is null) return;
            var sun = _renderSun.GetValue(null);
            if (sun is null || IsUnityNull(sun)) return;
            var sunPtr = (sun as Il2CppObjectBase)?.Pointer ?? IntPtr.Zero;
            var sunTransform = GetMember(sun, "transform");
            var sunRotation = sunTransform is null ? null : GetMember(sunTransform, "rotation");
            var sunColor = GetMember(sun, "color");

            var unityObject = Mod.ResolveType("UnityEngine.Object");
            var il2cppLight = Il2CppTypeOf(_lightType);
            if (unityObject is null || il2cppLight is null) return;

            MethodInfo? findAll = null;
            foreach (var m in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "FindObjectsOfType" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1) { findAll = m; break; }
            if (findAll is null) return;

            var arr = findAll.Invoke(null, new object[] { il2cppLight });
            var n = Count(arr);
            var touched = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var item = Item(arr, i);
                if (item is null) continue;
                var light = AsType(item, _lightType);
                if (light is null) continue;
                var kind = GetMember(light, "type");
                if (kind is null || Convert.ToInt32(kind) != 1) continue; // Directional only
                var ptr = (light as Il2CppObjectBase)?.Pointer ?? IntPtr.Zero;
                if (ptr != IntPtr.Zero && ptr == sunPtr) continue;      // engine already drives this one

                var state = _otherSuns.Find(s => (s.Light as Il2CppObjectBase)?.Pointer == ptr);
                if (state is null)
                {
                    state = new LightState
                    {
                        Light = light,
                        Intensity = GetMember(light, "intensity"),
                        Color = GetMember(light, "color"),
                        ShadowStrength = GetMember(light, "shadowStrength"),
                        Rotation = GetMember(light, "transform") is object tr ? GetMember(tr, "rotation") : null,
                    };
                    _otherSuns.Add(state);
                }

                if (state.Intensity is float orig)
                    SetMember(light, "intensity", orig * Mod.Cfg.RecRoyaleNightSunScale);
                if (sunColor is not null) SetMember(light, "color", sunColor);
                if (sunRotation is not null && GetMember(light, "transform") is object t)
                    SetMember(t, "rotation", sunRotation);
                touched.Add(ObjectName(light));
            }
            if (touched.Count > 0 && !_otherSunsLogged)
            {
                _otherSunsLogged = true;
                Mod.Log.Msg($"[royale-night] matched {touched.Count} other directional light(s) to the mood sun: {string.Join(", ", touched)}");
            }
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] MatchOtherSunLights failed: {(ex.InnerException ?? ex).Message}"); }
    }
    private static bool _otherSunsLogged;

    // Register the mood as the scene's defaultMood + sole availableMoods entry
    // (get_CurrentMood returns availableMoods[index] when the synced index is
    // set, else defaultMood — either way our mood). Originals are kept so
    // UninstallSceneMood can put the scene back to "no room moods".
    private static object? _sceneMoodsObj;
    private static object? _origDefaultMood, _origAvailableMoods;
    private static bool _sceneMoodInstalled;

    private static void InstallSceneMood(object moods, object mood)
    {
        try
        {
            if (_sceneMoodInstalled && ReferenceEquals(_sceneMoodsObj, moods)) return;
            _sceneMoodsObj = moods;
            _origDefaultMood = GetMember(moods, "defaultMood");
            _origAvailableMoods = GetMember(moods, "availableMoods");

            var okDefault = SetMember(moods, "defaultMood", mood);

            // availableMoods is an Il2CppReferenceArray<MoodSetting>; build a
            // one-element array of the same interop type and fill slot 0.
            var okArray = false;
            var arrProp = moods.GetType().GetProperty("availableMoods", Any);
            var arrType = arrProp?.PropertyType ?? _origAvailableMoods?.GetType();
            if (arrType is not null)
            {
                object? arr = null;
                foreach (var sig in new[] { new[] { typeof(long) }, new[] { typeof(int) } })
                {
                    var ctor = arrType.GetConstructor(sig);
                    if (ctor is null) continue;
                    try { arr = ctor.Invoke(new object[] { sig[0] == typeof(long) ? 1L : 1 }); break; } catch { }
                }
                if (arr is not null)
                {
                    var set = arrType.GetMethod("set_Item", new[] { typeof(int), _moodSettingType! });
                    if (set is null)
                    {
                        foreach (var m in arrType.GetMethods())
                            if (m.Name == "set_Item" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(int)) { set = m; break; }
                    }
                    try { set?.Invoke(arr, new[] { 0, mood }); okArray = set is not null && SetMember(moods, "availableMoods", arr); } catch { }
                }
            }

            _sceneMoodInstalled = okDefault || okArray;
            Mod.Log.Msg($"[royale-night] installed mood as scene default (defaultMood={okDefault} availableMoods={okArray})");
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] InstallSceneMood failed: {(ex.InnerException ?? ex).Message}"); }
    }

    private static void UninstallSceneMood(object moods)
    {
        try
        {
            if (!_sceneMoodInstalled) return;
            if (_origDefaultMood is not null) SetMember(moods, "defaultMood", _origDefaultMood);
            else SetMember(moods, "defaultMood", null!);
            if (_origAvailableMoods is not null) SetMember(moods, "availableMoods", _origAvailableMoods);
        }
        catch { }
        _sceneMoodInstalled = false;
        _sceneMoodsObj = null;
        _origDefaultMood = _origAvailableMoods = null;
    }

    // ── fog off / hide objects ────────────────────────────────────────
    private static bool _fogStateSaved;
    private static object? _origFogEnabled;
    private static Type? _renderSettingsType;

    private static void ForceFogOff()
    {
        try
        {
            _renderSettingsType ??= Mod.ResolveType("UnityEngine.RenderSettings");
            if (_renderSettingsType is null) return;
            var p = _renderSettingsType.GetProperty("fog", BindingFlags.Public | BindingFlags.Static);
            if (p is null) return;
            if (!_fogStateSaved) { _origFogEnabled = p.GetValue(null); _fogStateSaved = true; }
            if (p.GetValue(null) is true) p.SetValue(null, false);
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] ForceFogOff failed: {ex.Message}"); }
    }

    private static void RestoreFogState()
    {
        try
        {
            if (_fogStateSaved && _renderSettingsType is not null && _origFogEnabled is bool b)
                _renderSettingsType.GetProperty("fog", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, b);
        }
        catch { }
        _fogStateSaved = false;
        _origFogEnabled = null;
    }

    // Disable every Renderer whose GameObject or material name contains one
    // of Cfg.RecRoyaleNightHideObjects. Runs each apply cycle but only scans
    // once per scene (the matches are static scenery); ShowObjects re-enables.
    private static readonly List<object> _hidden = new();
    private static bool _hideScanned;

    private static void HideObjects()
    {
        if (_hideScanned) return;
        _hideScanned = true;
        try
        {
            var patterns = new List<string>();
            foreach (var s in Mod.Cfg.RecRoyaleNightHideObjects)
                if (!string.IsNullOrWhiteSpace(s)) patterns.Add(s.Trim());
            if (patterns.Count == 0) return;

            var rendererType = Mod.ResolveType("UnityEngine.Renderer");
            var unityObject = Mod.ResolveType("UnityEngine.Object");
            var il2cppRenderer = rendererType is null ? null : Il2CppTypeOf(rendererType);
            if (rendererType is null || unityObject is null || il2cppRenderer is null) return;

            MethodInfo? findAll = null;
            foreach (var m in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "FindObjectsOfType" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1) { findAll = m; break; }
            if (findAll is null) return;

            var arr = findAll.Invoke(null, new object[] { il2cppRenderer });
            var n = Count(arr);
            var names = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                var item = Item(arr, i);
                if (item is null) continue;
                var r = AsType(item, rendererType);
                if (r is null) continue;
                var name = ObjectName(r);
                var matName = "";
                try { var mat = GetMember(r, "sharedMaterial"); if (mat is not null && !IsUnityNull(mat)) matName = ObjectName(mat); } catch { }
                var hit = false;
                foreach (var p in patterns)
                    if (name.Contains(p, StringComparison.OrdinalIgnoreCase) || matName.Contains(p, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                if (!hit) continue;
                if (GetMember(r, "enabled") is true && SetMember(r, "enabled", false))
                {
                    _hidden.Add(r);
                    names.Add(name);
                }
            }
            Mod.Log.Msg($"[royale-night] hid {_hidden.Count} renderer(s) matching [{string.Join(",", patterns)}]: {string.Join(", ", names)}");
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] HideObjects failed: {(ex.InnerException ?? ex).Message}"); }
    }

    private static void ShowObjects()
    {
        foreach (var r in _hidden)
        {
            try { if (!IsUnityNull(r)) SetMember(r, "enabled", true); } catch { }
        }
        _hidden.Clear();
        _hideScanned = false;
    }

    // ── CV2 room-settings probe ───────────────────────────────────────
    // The Maker Pen mood chips ("Skydome Constant", "Room Skydome Modify",
    // Sun/Fog/Background Objects equivalents) drive RoomSkydome / RoomSun /
    // RoomFog components that hang off RoomElementSettingsManager, which
    // CircuitsV2Manager brings up. Log whether any of that exists in this
    // scene so we know what a chips-in-Rec-Royale build would have to add.
    private static bool _probed;
    private static void ProbeCircuitsSupport()
    {
        if (_probed) return;
        _probed = true;
        try
        {
            var unityObject = Mod.ResolveType("UnityEngine.Object");
            if (unityObject is null) return;
            MethodInfo? findAll = null;
            foreach (var m in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "FindObjectsOfType" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1) { findAll = m; break; }
            if (findAll is null) return;

            var parts = new List<string>();
            foreach (var name in new[]
            {
                "RecRoom.CircuitsV2.CircuitsV2Manager",
                "RecRoom.CircuitsV2.RoomSettings.RoomElementSettingsManager",
                "RecRoom.CircuitsV2.RoomSettings.RoomSkydome",
                "RecRoom.CircuitsV2.RoomSettings.RoomSun",
                "RecRoom.CircuitsV2.RoomSettings.RoomFog",
                "RecRoom.CircuitsV2.RoomSettings.RoomBackgroundObjects",
                "MakerPen",
            })
            {
                var t = Mod.ResolveType(name);
                if (t is null) { parts.Add($"{name.Substring(name.LastIndexOf('.') + 1)}=<type missing>"); continue; }
                var il2 = Il2CppTypeOf(t);
                if (il2 is null) { parts.Add($"{t.Name}=?"); continue; }
                int n = 0;
                try { n = Count(findAll.Invoke(null, new object[] { il2 })); } catch (Exception ex) { parts.Add($"{t.Name}=err:{(ex.InnerException ?? ex).Message}"); continue; }
                parts.Add($"{t.Name}={n}");
            }
            Mod.Log.Msg($"[royale-night] CV2 probe in '{_sceneName}': {string.Join(" ", parts)}");
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] CV2 probe failed: {(ex.InnerException ?? ex).Message}"); }
    }

    private static void RestoreOtherSunLights()
    {
        foreach (var s in _otherSuns)
        {
            try
            {
                if (IsUnityNull(s.Light)) continue;
                if (s.Intensity is not null) SetMember(s.Light, "intensity", s.Intensity);
                if (s.Color is not null) SetMember(s.Light, "color", s.Color);
                if (s.ShadowStrength is not null) SetMember(s.Light, "shadowStrength", s.ShadowStrength);
                if (s.Rotation is not null && GetMember(s.Light, "transform") is object t) SetMember(t, "rotation", s.Rotation);
            }
            catch { }
        }
        _otherSuns.Clear();
        _otherSunsLogged = false;
    }

    // Make sure RenderSettings.skybox is a material on the "Custom/SkyBox"
    // shader. The apply step assigns MoodConfig.SkyBoxMaterial
    // (LNMDIIFOFPD.NHAEHKJINFH → SkyBoxMaterial); in RRO scenes that
    // reference resolves to nothing, leaving the skybox null. Prefer any
    // already-loaded material on that shader, else create one, and also
    // write it into MoodConfig so later applies keep using it.
    private static void EnsureMoodSkybox()
    {
        try
        {
            if (_renderSkybox is null || _materialType is null || _shaderType is null) return;

            var current = _renderSkybox.GetValue(null);
            var currentShader = current is null || IsUnityNull(current) ? "" : ShaderNameOf(current);
            if (currentShader == "Custom/SkyBox") return;

            object? mat = _ourSkybox is not null && !IsUnityNull(_ourSkybox) ? _ourSkybox : null;

            // 1. Any loaded material already on the shader (e.g. the real
            //    Skybox_Moods_Sunny_Default_Mat if some scene brought it in).
            if (mat is null)
            {
                var resources = Mod.ResolveType("UnityEngine.Resources");
                var il2cppMat = Il2CppTypeOf(_materialType);
                if (resources is not null && il2cppMat is not null)
                {
                    foreach (var m in resources.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "FindObjectsOfTypeAll" || m.IsGenericMethodDefinition || m.GetParameters().Length != 1) continue;
                        var arr = m.Invoke(null, new object[] { il2cppMat });
                        var n = Count(arr);
                        for (int i = 0; i < n; i++)
                        {
                            var item = Item(arr, i);
                            if (item is null) continue;
                            var wrapped = AsType(item, _materialType);
                            if (wrapped is not null && ShaderNameOf(wrapped) == "Custom/SkyBox") { mat = wrapped; break; }
                        }
                        break;
                    }
                }
                if (mat is not null) Mod.Log.Msg($"[royale-night] using loaded skybox material '{ObjectName(mat)}'");
            }

            // 2. Build one: new Material(Shader.Find("Custom/SkyBox")).
            if (mat is null)
            {
                var find = AccessTools.Method(_shaderType, "Find", new[] { typeof(string) });
                var shader = find?.Invoke(null, new object[] { "Custom/SkyBox" });
                if (shader is null || IsUnityNull(shader))
                {
                    Mod.Log.Warning("[royale-night] Shader.Find(\"Custom/SkyBox\") returned nothing — sky colours will not apply");
                    return;
                }
                var ctor = _materialType.GetConstructor(new[] { _shaderType }) ?? _materialType.GetConstructor(new[] { shader.GetType() });
                if (ctor is null)
                {
                    Mod.Log.Warning("[royale-night] Material(Shader) constructor not found");
                    return;
                }
                mat = ctor.Invoke(new[] { shader });
                SetMember(mat, "name", "DorkNet_RecRoyaleNight_Skybox");
                Mod.Log.Msg("[royale-night] created skybox material on Custom/SkyBox");
            }

            _ourSkybox = mat;
            _renderSkybox.SetValue(null, mat);

            // Keep the engine's own apply path pointing at it too.
            if (_moodConfigHolder is not null)
            {
                foreach (var p in _moodConfigHolder.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                    var cfg = p.GetValue(null);
                    if (cfg is null || IsUnityNull(cfg)) continue;
                    if (SetMember(cfg, "SkyBoxMaterial", mat)) Mod.Log.Msg("[royale-night] MoodConfig.SkyBoxMaterial ← our material");
                    break;
                }
            }
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] EnsureMoodSkybox failed: {(ex.InnerException ?? ex).Message}"); }
    }

    private static string ShaderNameOf(object material)
    {
        try
        {
            var shader = GetMember(material, "shader");
            return shader is null || IsUnityNull(shader) ? "" : ObjectName(shader);
        }
        catch { return ""; }
    }

    private static string DescribeSkybox()
    {
        try
        {
            var sky = _renderSkybox?.GetValue(null);
            if (sky is null || IsUnityNull(sky)) return "<null>";
            return $"'{ObjectName(sky)}' [{ShaderNameOf(sky)}]";
        }
        catch { return "?"; }
    }

    // Post-exposure lives in the URP Volume the mood engine drives. It only
    // shows if the Volume is enabled with weight and the camera renders
    // post-processing (Rec Room's graphics settings can turn that off).
    private static void EnsurePostProcessing(object moods)
    {
        try
        {
            var volume = Read(_volume, moods);
            if (volume is not null && !IsUnityNull(volume))
            {
                var enabled = GetMember(volume, "enabled");
                var weight = GetMember(volume, "weight");
                var isGlobal = GetMember(volume, "isGlobal");
                var profile = GetMember(volume, "profile");
                Mod.Log.Msg($"[royale-night] volume: enabled={enabled} weight={weight} global={isGlobal} profile={(profile is null ? "<null>" : ObjectName(profile))}");
                if (Mod.Cfg.RecRoyaleNightForcePostProcessing)
                {
                    if (enabled is false) SetMember(volume, "enabled", true);
                    if (weight is float w && w < 1f) SetMember(volume, "weight", 1f);
                }
            }
            else Mod.Log.Warning("[royale-night] scene mood Volume is null — post-exposure darkening unavailable");

            if (_cameraDataType is null) { Mod.Log.Warning("[royale-night] UniversalAdditionalCameraData type not found"); return; }
            var unityObject = Mod.ResolveType("UnityEngine.Object");
            var il2cppCam = Il2CppTypeOf(_cameraDataType);
            if (unityObject is null || il2cppCam is null) return;
            foreach (var m in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "FindObjectsOfType" || m.IsGenericMethodDefinition || m.GetParameters().Length != 1) continue;
                var arr = m.Invoke(null, new object[] { il2cppCam });
                var n = Count(arr);
                int forced = 0;
                var states = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    var item = Item(arr, i);
                    if (item is null) continue;
                    var cam = AsType(item, _cameraDataType);
                    if (cam is null) continue;
                    var pp = GetMember(cam, "renderPostProcessing");
                    states.Add($"{ObjectName(cam)}={pp}");
                    if (pp is false && Mod.Cfg.RecRoyaleNightForcePostProcessing && SetMember(cam, "renderPostProcessing", true)) forced++;
                }
                Mod.Log.Msg($"[royale-night] camera post-processing: {string.Join(", ", states)}{(forced > 0 ? $" → forced on ({forced})" : "")}");
                break;
            }
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] EnsurePostProcessing failed: {(ex.InnerException ?? ex).Message}"); }
    }

    // Darken the mood data in place before it is applied:
    //   ColorGradingSettings.exposure   += RecRoyaleNightExposure  (EV; the
    //       ColorGrading blender writes it into ColorAdjustments.postExposure
    //       via VolumeParameter.Override — the only knob that also dims baked
    //       lightmaps, since it's a full-frame post effect)
    //   SunSettings.sunIntensity        *= RecRoyaleNightSunScale
    //   SkyBoxSettings.ambientLightIntensity *= RecRoyaleNightAmbientScale
    // Originals are kept and restored by RestoreDarkness so the shared asset
    // is clean again for any custom room that uses the same mood.
    private static void PatchDarkness(object mood)
    {
        try
        {
            if (ReferenceEquals(_darkPatchedMood, mood)) return;
            RestoreDarkness();

            var grading = GetMember(mood, "ColorGradingSettings");
            var sun     = GetMember(mood, "SunSettings");
            var sky     = GetMember(mood, "SkyBoxSettings");

            if (grading is not null && Mod.Cfg.RecRoyaleNightExposure != 0f && GetMember(grading, "exposure") is float ev)
            {
                _origExposure = ev;
                SetMember(grading, "exposure", ev + Mod.Cfg.RecRoyaleNightExposure);
            }
            if (sun is not null && Mod.Cfg.RecRoyaleNightSunScale != 1f && GetMember(sun, "sunIntensity") is float si)
            {
                _origSunIntensity = si;
                SetMember(sun, "sunIntensity", si * Mod.Cfg.RecRoyaleNightSunScale);
            }
            if (sky is not null && Mod.Cfg.RecRoyaleNightAmbientScale != 1f && GetMember(sky, "ambientLightIntensity") is float ai)
            {
                _origAmbient = ai;
                SetMember(sky, "ambientLightIntensity", ai * Mod.Cfg.RecRoyaleNightAmbientScale);
            }
            _darkPatchedMood = mood;
            Mod.Log.Msg($"[royale-night] darkened mood: exposure {_origExposure?.ToString("0.##") ?? "?"}→{(_origExposure + Mod.Cfg.RecRoyaleNightExposure)?.ToString("0.##") ?? "?"} EV, " +
                        $"sun ×{Mod.Cfg.RecRoyaleNightSunScale} ({_origSunIntensity?.ToString("0.##") ?? "?"}), ambient ×{Mod.Cfg.RecRoyaleNightAmbientScale} ({_origAmbient?.ToString("0.##") ?? "?"})");
        }
        catch (Exception ex) { Mod.Log.Warning($"[royale-night] PatchDarkness failed: {(ex.InnerException ?? ex).Message}"); }
    }

    private static void RestoreDarkness()
    {
        try
        {
            if (_darkPatchedMood is not null)
            {
                var grading = GetMember(_darkPatchedMood, "ColorGradingSettings");
                var sun     = GetMember(_darkPatchedMood, "SunSettings");
                var sky     = GetMember(_darkPatchedMood, "SkyBoxSettings");
                if (grading is not null && _origExposure is float e) SetMember(grading, "exposure", e);
                if (sun is not null && _origSunIntensity is float s) SetMember(sun, "sunIntensity", s);
                if (sky is not null && _origAmbient is float a) SetMember(sky, "ambientLightIntensity", a);
            }
        }
        catch { }
        _darkPatchedMood = null;
        _origExposure = _origSunIntensity = _origAmbient = null;
    }

    private static bool SetMember(object o, string name, object value)
    {
        var t = o.GetType();
        var p = t.GetProperty(name, Any);
        if (p is not null && p.CanWrite) { try { p.SetValue(o, value); return true; } catch { } }
        var f = t.GetField(name, Any);
        if (f is not null) { try { f.SetValue(o, value); return true; } catch { } }
        return false;
    }

    // Il2CppInterop exposes il2cpp fields as properties (and, on some
    // versions, as fields). Read either.
    private static object? GetMember(object o, string name)
    {
        var t = o.GetType();
        var p = t.GetProperty(name, Any);
        if (p is not null && p.CanRead && p.GetIndexParameters().Length == 0)
        { try { return p.GetValue(o); } catch { } }
        var f = t.GetField(name, Any);
        if (f is not null) { try { return f.GetValue(o); } catch { } }
        return null;
    }

    // Il2CppType.From(Type) → Il2CppSystem.Type, via reflection so this
    // project keeps not referencing Il2Cppmscorlib at compile time.
    private static object? Il2CppTypeOf(Type type)
    {
        foreach (var m in typeof(Il2CppType).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != "From" || m.IsGenericMethodDefinition) continue;
            var ps = m.GetParameters();
            if (ps.Length >= 1 && ps[0].ParameterType == typeof(Type))
            {
                var args = new object?[ps.Length];
                args[0] = type;
                for (int i = 1; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
                var r = m.Invoke(null, args);
                if (r is not null) return r;
            }
        }
        return null;
    }

    private static object? GetSceneManager()
    {
        var inst = _sceneManagerInstance?.GetValue(null);
        if (inst is null || IsUnityNull(inst)) return null;
        return inst;
    }

    // Loads the configured MoodSetting asset. Resources.Load(path, Type)
    // pulls it out of resources.assets by name (the ResourceManager table
    // keys are the lower-cased asset names, no folder). Fallback:
    // Resources.FindObjectsOfTypeAll(MoodSetting) by name, which only sees
    // assets already in memory.
    private static object? GetNightMood()
    {
        var wanted = (Mod.Cfg.RecRoyaleNightMood ?? "").Trim();
        if (wanted.Length == 0) wanted = "Night_Calm_Outdoor_Mood";
        if (_nightMood is not null && _nightMoodLoadedName == wanted && !IsUnityNull(_nightMood))
            return _nightMood;

        _nightMood = null;
        var resources = Mod.ResolveType("UnityEngine.Resources");
        if (resources is null || _moodSettingType is null)
        {
            Mod.Log.Warning("[royale-night] UnityEngine.Resources not found");
            return null;
        }

        var il2cppType = Il2CppTypeOf(_moodSettingType);
        if (il2cppType is null)
        {
            Mod.Log.Warning("[royale-night] Il2CppType.From(Type) not found");
            return null;
        }
        object? found = null;

        foreach (var m in resources.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != "Load" || m.IsGenericMethodDefinition) continue;
            var ps = m.GetParameters();
            if (ps.Length != 2 || ps[0].ParameterType != typeof(string)) continue;
            try
            {
                found = m.Invoke(null, new object[] { wanted, il2cppType });
                if (found is not null && !IsUnityNull(found)) break;
                found = null;
            }
            catch (Exception ex) { Mod.Log.Warning($"[royale-night] Resources.Load failed: {(ex.InnerException ?? ex).Message}"); }
        }

        if (found is null)
        {
            foreach (var m in resources.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "FindObjectsOfTypeAll" || m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1) continue;
                try
                {
                    var arr = m.Invoke(null, new object[] { il2cppType });
                    var n = Count(arr);
                    var names = new List<string>();
                    for (int i = 0; i < n; i++)
                    {
                        var item = Item(arr, i);
                        if (item is null) continue;
                        var name = ObjectName(item);
                        names.Add(name);
                        if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) { found = item; break; }
                    }
                    if (found is null)
                        Mod.Log.Warning($"[royale-night] mood '{wanted}' not in memory; loaded moods: {string.Join(", ", names)}");
                }
                catch (Exception ex) { Mod.Log.Warning($"[royale-night] FindObjectsOfTypeAll failed: {(ex.InnerException ?? ex).Message}"); }
                break;
            }
        }

        if (found is null)
        {
            Mod.Log.Warning($"[royale-night] could not load MoodSetting '{wanted}'");
            return null;
        }

        // Resources.Load hands back a UnityEngine.Object wrapper; re-wrap the
        // same il2cpp pointer as MoodSetting so the apply method accepts it.
        _nightMood = AsType(found, _moodSettingType);
        _nightMoodLoadedName = wanted;
        return _nightMood;
    }

    private static object? AsType(object obj, Type target)
    {
        if (target.IsInstanceOfType(obj)) return obj;
        if (obj is Il2CppObjectBase il2cpp)
        {
            var ctor = target.GetConstructor(new[] { typeof(IntPtr) });
            if (ctor is not null) return ctor.Invoke(new object[] { il2cpp.Pointer });
        }
        return obj;
    }

    // ── hotkey ────────────────────────────────────────────────────────
    private static void PollHotkey()
    {
        var key = Mod.Cfg.RecRoyaleNightToggleKey;
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            if (!_inputResolved) ResolveInput(key);
            if (_getKeyDown is null || _toggleKey is null) return;
            if (_getKeyDown.Invoke(null, new[] { _toggleKey }) is true) Toggle();
        }
        catch (Exception ex)
        {
            Mod.Log.Warning($"[royale-night] hotkey poll failed: {ex.Message}");
            _getKeyDown = null; // stop spamming
        }
    }

    private static void ResolveInput(string key)
    {
        _inputResolved = true;
        var keyCodeType = Mod.ResolveType("UnityEngine.KeyCode");
        var inputType = Mod.ResolveType("UnityEngine.Input");
        if (keyCodeType is null || inputType is null)
        {
            Mod.Log.Warning("[royale-night] UnityEngine.Input/KeyCode not found — hotkey disabled");
            return;
        }
        try { _toggleKey = Enum.Parse(keyCodeType, key, ignoreCase: true); }
        catch
        {
            Mod.Log.Warning($"[royale-night] '{key}' is not a UnityEngine.KeyCode — hotkey disabled");
            return;
        }
        _getKeyDown = AccessTools.Method(inputType, "GetKeyDown", new[] { keyCodeType });
        if (_getKeyDown is null) Mod.Log.Warning("[royale-night] Input.GetKeyDown(KeyCode) not found — hotkey disabled");
    }

    // ── helpers ───────────────────────────────────────────────────────
    private static bool MatchesScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        foreach (var s in Mod.Cfg.RecRoyaleNightScenes)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (sceneName.Contains(s.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // UnityEngine.Object wrappers are non-null C# objects even when the
    // native object is destroyed/missing; `== null` is overloaded in the
    // interop, so route through the static op_Equality when present.
    private static bool IsUnityNull(object obj)
    {
        try
        {
            var t = obj.GetType();
            var eq = t.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (eq is not null && eq.GetParameters().Length == 2)
                return eq.Invoke(null, new[] { obj, null }) is true;
        }
        catch { }
        return false;
    }

    private static string ObjectName(object obj)
    {
        try
        {
            var p = obj.GetType().GetProperty("name", Any);
            return p?.GetValue(obj) as string ?? "";
        }
        catch { return ""; }
    }

    private static string Describe(object mood)
    {
        var friendly = "";
        try { friendly = _friendlyName?.GetValue(mood) as string ?? ""; } catch { }
        var name = ObjectName(mood);
        return friendly.Length > 0 ? $"{friendly} ({name})" : name;
    }

    private static int Count(object? arr)
    {
        if (arr is null) return 0;
        if (arr is Array a) return a.Length;
        var p = arr.GetType().GetProperty("Length") ?? arr.GetType().GetProperty("Count");
        return (p?.GetValue(arr) as int?) ?? 0;
    }

    private static object? Item(object? arr, int i)
    {
        if (arr is null) return null;
        if (arr is Array a) return a.GetValue(i);
        var gi = arr.GetType().GetMethod("get_Item", new[] { typeof(int) });
        return gi?.Invoke(arr, new object[] { i });
    }
}
