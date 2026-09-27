# Rec Room 2023-03-21 — scene moods (sky dome / sun / fog) and Rec Royale night mode

Client-side reference for the room "mood" engine the March-2023 client uses
for Maker Pen sky/sun/fog settings, and how `DorkNet.ClientMod`
(`RecRoyaleNight.cs`) reuses it to give Rec Royale a night variant the game
never shipped. Everything below was read from the Cpp2IL ISIL dump
(`C:\tmp\recroom-2023-03-21-isil`), the interop stubs
(`C:\tmp\recroom-2023-03-21-decompiled`) and the built game data
(`RecRoom_Data\resources.assets`, `globalgamemanagers`).

## Ownership chain

| Object | Where | Notes |
|---|---|---|
| `RecRoomSceneManager` | `MonoBehaviourPun`, one per room scene | static `get_Instance` (interop property name obfuscated: `PDCOCJBICGC`); name-preserved field `sceneMoodSettings` |
| `RecRoom.Core.Scene.SceneMoodSettings` | plain class held by the scene manager | initialised by `PCJGOACJEDL(RecRoomSceneManager)` at scene start |
| `RecRoom.Core.Creation.SceneMoods.MoodSetting` | `ScriptableObject` asset | `FriendlyName`, `SkyBoxSettings`, `SunSettings`, `FogSettings`, `BloomSettings`, `ColorGradingSettings`, `BackgroundObjectsSettings`, `VolumeProfile`, `BakedReflection`, `guid` |

`SceneMoodSettings` name-preserved members: `availableMoods`
(`MoodSetting[]`), `defaultMood`, `CurrentMood`, `PrevMood`,
`CurrentRoomMoodIndex`, `SupportsMoodCircuits`, `HasRecRoomSkyboxShader`,
`HasSunSource`, `Volume`, `DefaultVolumeProfile`, `ReflectionIntensity`,
`AmbientLightIntensity`, plus one `PFFMDAJPHJO<T>` blender per settings
block (`SkyBoxMoodSettingsBlender`, `SunMoodSettingsBlender`, …).

## Apply path (what actually changes the picture)

`SceneMoodSettings.MLLBALMKEGJ(MoodSetting)` — ISIL
`RecRoom/Core/Scene/SceneMoodSettings.txt:3025-3581`:

1. `ALCMNGKOEBK()` — pushes the mood's six settings blocks into the blenders
   as the "top" data (6× generic blender set call).
2. `OEKNGEMDMDB(VolumeProfile)` — swaps the URP post-processing profile.
3. `RenderSettings.set_skybox` — the shared Rec Room skybox material; the
   `SkyBoxMoodSettings` blender then writes `_SkyTopColor`-style shader
   props into it (`SkyBoxMoodSettings.FPKCBLPBOFN(Material)`).
4. `RenderSettings.get_sun` → re-aims/recolours the sun (`SunMoodSettings`,
   only when `HasSunSource`).
5. `ReflectionProbe.set_customBakedTexture` ← `MoodSetting.BakedReflection`.
6. 6× blender force-update.

It does **not** write `CurrentRoomMoodIndex`, so calling it is local only.

### RRO scenes: the engine starts disabled

`SceneMoodSettings.isEnabled` (+0x38) gates everything: `get_CurrentMood`
returns null while it is false, `get_RoomMoodsEnabled` is
`isEnabled && defaultMood != null`, and nothing ticks. The flag is only
switched on by the room data-blob loader (`EIMFCPIHEPF.JPKEMACDNCF`, ISIL
`EIMFCPIHEPF.txt:2136-2310`), which custom rooms run and RRO scenes (no
data blob) never do. Its bring-up sequence:

| Call | Meaning |
|---|---|
| `LKIKMLNPCFK(false)` | SetEnabled(false): applies a null mood (blenders reset, default VolumeProfile back) and toggles `BackgroundObjectsRoot` |
| `LIEDFOLBGHL()` | Capture: builds the blenders' "previous" data from `RenderSettings.skybox` / `sun` / fog; sets `hasRecRoomSkyboxShader` = skybox shader name `== "Custom/SkyBox"` |
| `LKIKMLNPCFK(true)` | SetEnabled(true) |

The apply step then swaps `RenderSettings.skybox` to the global
`MoodConfig` skybox material (`LNMDIIFOFPD.NHAEHKJINFH`, +0x18).
The ClientMod replays this exact sequence when it finds `isEnabled` false.

**The apply step only sets blend targets.** The per-frame update
`CEKLEBHDAHA` (ISIL :3895-4031) is what actually writes sky colours,
rotates/recolours the sun, blends fog and background objects and (via
`CPJBHLMCBBC`) pushes bloom/colour grading into the Volume — and it
returns immediately while `get_CurrentMood()` is null. `get_CurrentMood`
(ISIL :297-457) returns null when `isEnabled` is false, else
`_studioRoomUnityMood` if set, else `availableMoods[index]` when the
synced index is set, else `defaultMood`. RRO scenes have `defaultMood`
null and `availableMoods` empty, so even a fully enabled engine with a
successful apply shows only the ungated background objects (moon, stars).
The ClientMod therefore writes the night mood into `defaultMood` and a
one-element `availableMoods` before enabling the engine.

Two flags captured by `LIEDFOLBGHL` gate what the apply step and the
per-frame update are allowed to touch:

| Flag | Set by capture from | Gates |
|---|---|---|
| `hasRecRoomSkyboxShader` (+0xA8) | `RenderSettings.skybox.shader.name == "Custom/SkyBox"` | sky colour blender apply |
| `hasSunSource` (+0xA9) | `RenderSettings.sun != null` | sun direction / colour / intensity |

Note the sun flag is nested inside the sky check: `hasSunSource` can only
become true when the skybox shader test already passed (ISIL :2629-2705).

`RecRoyale_Frontier` fails both at capture time: its skybox is not the
mood shader. The observable result of a naive apply is exactly "moon and
stars appear, sky and sun unchanged" — the mood's
`BackgroundObjectsSettings` are not gated.

Worse, the apply step's own skybox swap does not help in RRO scenes: it
assigns `MoodConfig.SkyBoxMaterial`, and that material
(`Skybox_Moods_Sunny_Default_Mat`, sharedassets77.assets — a custom-room
scene's shared assets) is not loaded there, so `RenderSettings.skybox`
becomes null (plain black sky) and both flags stay false forever. The
`Custom/SkyBox` shader itself is in resources.assets, so
`new Material(Shader.Find("Custom/SkyBox"))` works anywhere.

Workarounds used by the ClientMod: assign the scene's directional `Light`
to `RenderSettings.sun` if unset; apply once; then put a material on the
`Custom/SkyBox` shader into `RenderSettings.skybox` and
`MoodConfig.SkyBoxMaterial`; capture again so both flags flip true; then
apply for real. Post-exposure additionally needs the scene `Volume`
enabled with weight 1 and `UniversalAdditionalCameraData.renderPostProcessing`
on (`RecRoyaleNightForcePostProcessing`). `LILOMACBFPI()` (:2789-2817) force-sets
both flags to true (writes 0x0101 at +0xA8) and clears
`_studioRoomUnityMood`; it is the Studio path and not used here because
a true `hasSunSource` with a null sun would NRE in the update loop.

Fog: `FogMoodSettings.NLDDIDEIEFH()` reads `RenderSettings.fog`,
`fogStartDistance`, `fogEndDistance`, `fogColor` into the block;
`KGBEBONMIII()` writes them back. Swapping `MoodSetting.FogSettings` for
a freshly captured block before the first apply keeps the scene's fog
(`RecRoyaleNightFog: false`). The block exposes only `color`,
`startDistance`, `fadeDistance` (no enabled flag), so "fog off"
(`RecRoyaleNightDisableFog`) pushes both distances to 1e6 and forces
`RenderSettings.fog = false` after each apply.

Frontier's daytime clouds are scene meshes, not mood background objects:
`CloudPlane` ×19 under `Clouds`, static-batched as
`StaticObjectsStaticBatch_Cloud_RecRoyale_Frontier_Mat` (level106).
`RecRoyaleNightHideObjects` disables renderers whose object or
`sharedMaterial` name contains a pattern (default `Cloud`).

Brightness: the mood blocks are plain data the ClientMod edits in place
before the apply (originals restored on scene exit / toggle-off):

| Block · field | Applied as | ClientMod knob |
|---|---|---|
| `ColorGradingMoodSettings.exposure` | `ColorAdjustments.postExposure` via `VolumeParameter<float>.Override` in `KGBEBONMIII` | `RecRoyaleNightExposure` (EV offset) — the only knob that dims baked lightmaps |
| `SunMoodSettings.sunIntensity` | `RenderSettings.sun.intensity` (needs `hasSunSource`) | `RecRoyaleNightSunScale` (×) |
| — | other directional `Light`s (never touched by the engine; Frontier's `RenderSettings.sun` is `SkyboxLight`, the sun-disc light, not the key light) | `RecRoyaleNightMatchSunLights`: copy the driven sun's rotation/colour, intensity × `RecRoyaleNightSunScale` |
| `SkyBoxMoodSettings.ambientLightIntensity` | `RenderSettings.ambientIntensity` | `RecRoyaleNightAmbientScale` (×) |

Other `ColorGradingMoodSettings` fields: `color`, `temperature`, `tint`,
`hueShift`, `saturation`, `contrast`, `colorFilterIntensity`.
`BloomMoodSettings`: `color`, `diffusion`, `intensity`, `threshold`.

### Networked path (do not use for a client-side effect)

`SceneMoodSettings.IPAMPMBDPNB(Guid)` scans `availableMoods` for the GUID
and calls `set_CurrentRoomMoodIndex`. `_currentRoomMoodIndex` is a
`DFAOKLDJPEL<int>` from `RecRoom.Networking.SynchronizedFields.Runtime`,
i.e. a synchronized field that replicates to everyone in the instance.
CircuitsV2 room-settings chips (`RecRoom.CircuitsV2.RoomSettings.RoomSkydome`,
`RoomSun`, `RoomFog`, all `RoomMoodBase<T,U,V>`) drive the same engine
through `ModifyMood(...)` and are also networked.

## Shipped mood assets

All live in `resources.assets` and are registered in the `globalgamemanagers`
ResourceManager table under their lower-cased asset name (no folder), so
`Resources.Load("<name>", typeof(MoodSetting))` works.

| Asset | `FriendlyName` |
|---|---|
| `Night_Calm_Outdoor_Mood` | Calm Night |
| `Night_Spooky_Outdoor_Mood` | Spooky Night |
| `Night_Wild_Outdoor_Mood` | Wild Night |
| `Night_StuntRunner_Outdoor_Mood` | (Stunt Runner night) |
| `OuterSpace_Mood` | Outer Space |
| `Sunset_Cozy_Outdoor_Mood` | Cozy Sunset |
| `Sunset_Burning_Mood` | Burning Sunset |
| `Sunset_Tropical_Outdoor_Mood` | Tropical Sunset |
| `Sunrise_Misty_Outdoor_Mood` | (misty sunrise) |

Reflection cubemaps ship alongside (`Moods_WildNight_ReflectionProbe`,
`Moods_SunnyDay_Skybox_Reflection_Baked`, …) and are referenced from each
`MoodSetting.BakedReflection`.

## Rec Royale specifics

* Only one Rec Royale map in this build:
  `Assets/Activities/RecRoyale/Maps/RecRoyale_Frontier/RecRoyale_Frontier.unity`
  (Unity scene name `RecRoyale_Frontier`). The `"recroyale"` literal in
  `BattleRoyaleManager` is only `get_RuleTypeAutoTag`, not a scene name.
* `BattleRoyaleManager` has no static instance accessor; scene-name
  matching via MelonLoader `OnSceneWasLoaded` is the detection choke point.
* Weapon flashlights already exist: `WeaponFlashlightController` (fields
  `weapon`, `flashlightPrefab`, `flashlightRoot`) spawns
  `WeaponFlashlight` when `get_FlashlightAllowed` is true. That getter
  (ISIL `WeaponFlashlightController.txt:3-75`) is
  `Matchmaking.FBELDNLEKAC() != null && [obj+0x28] == 15` — a game-mode
  gate for a different activity. Interop property name: `KIMMMCCKMJL`.

## Real CV2 mood chips in an RRO scene

The Maker Pen chips ("Skydome Constant", "Room Skydome Modify/Reset", and
the Sun / Fog / Background Objects trios) drive `RoomSkydome`, `RoomSun`,
`RoomFog`, `RoomBackgroundObjects` (`RecRoom.CircuitsV2.RoomSettings`,
all `RoomMoodBase<T,U,V>`), which hang off `RoomElementSettingsManager` (a
`NetworkedSingletonMonoBehaviour` that `CircuitsV2Manager` brings up in
`MPDIMHOMIDP`). Their state persists in the room blob
(`PersistedRoomData` fields 32-35: `room_fog_data`, `room_mood_sun_data`,
`room_mood_background_objects`, `room_mood_skydome`). None of that runs in
a room the client believes has no data blob, so the prerequisite for chips
in Rec Royale is server-side: give the room a blob (`docs/admin.md` → "RRO
room data blob"). The ClientMod probe line
`[royale-night] CV2 probe in 'RecRoyale_Frontier': …` logs instance counts
for `CircuitsV2Manager`, `RoomElementSettingsManager`, `RoomSkydome`,
`RoomSun`, `RoomFog`, `RoomBackgroundObjects` and `MakerPen` so the effect
of the blob can be verified in one join.

## How the ClientMod uses this

`DorkNet.ClientMod/RecRoyaleNight.cs`:

1. `OnSceneWasLoaded` → scene name contains any `RecRoyaleNightScenes`
   entry (default `RecRoyale`) ⇒ arm.
2. `OnUpdate` every 30 frames: `RecRoomSceneManager.Instance.sceneMoodSettings`
   present and `CurrentMood` non-null (engine initialised) ⇒
   `Resources.Load(RecRoyaleNightMood, MoodSetting)` ⇒
   `MLLBALMKEGJ(mood)`. Re-asserted every ~10 s (the engine re-applies its
   own mood on quality-level changes).
3. Hotkey (`RecRoyaleNightToggleKey`, a `UnityEngine.KeyCode` name) toggles;
   off re-applies `CurrentMood`, i.e. the scene's own default.
4. Harmony postfix on `get_FlashlightAllowed` returns `true` while active
   and in a Rec Royale scene (`RecRoyaleNightFlashlights`).

Limits: baked lightmaps are untouched (interiors keep their daytime bake);
darkening comes from the mood's colour grading, sun and ambient settings.
Purely per-client — other players see whatever their own client applies.
