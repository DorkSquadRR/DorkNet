# DorkNet ClientPatch BepInEx Plugin

Single client-side artefact that does **everything** DorkNet needs to
patch into the 2020 watch:

1. Rewrites every `*.rec.net` URL the watch builds → user-configured
   replacement host (default `localhost`). Catches the bootstrap
   `https://ns.rec.net/?v=2`; every other service URL flows in from
   the nameserver response which the DorkNet server controls. No
   more hosts file, no more wildcard mkcert (Cloudflare Tunnel terminates
   real TLS at the edge of the configured host).
2. Overrides Photon Cloud `AppId` / `VoiceAppId` right before
   `PhotonNetwork.ConnectUsingSettings`. No more byte-edits to
   `resources.assets`, no more hardcoded offsets to chase across game
   versions.
3. Attaches `userid` + `LoginLock` to `PhotonNetwork.AuthValues` right
   before any authenticate op leaves the wire, so DorkNet's
   `/photon/customauth` endpoint can identify the player and enforce
   single-session.

Configuration lives in `BepInEx/config/sh.dork.clientpatch.cfg`. Edit
values, restart the client, patches re-apply with the new settings.

## Quickstart (recommended)

Use the wrapper script — handles BepInEx install, interop generation,
plugin build, and config drop in one go:

```pwsh
.\tools\install-plugin.ps1 `
    -RecRoomPath  "C:\Program Files (x86)\Steam\steamapps\common\RecRoom\Recroom_Release_Data" `
    -PhotonAppId  <your-photon-realtime-app-id>
```

The script auto-downloads a pinned BepInEx 6 IL2CPP build from
`builds.bepinex.dev` on first run and caches it at
`%LOCALAPPDATA%\DorkNet\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.696.zip`
so re-runs on the same machine don't re-download. To override the
pinned build (newer/older), pass `-BepInExZip <path>`.

After a client update, just re-run with the same args. The script
re-builds the plugin against the new game's interop assemblies.

To remove: `.\tools\install-plugin.ps1 -RecRoomPath ... -Revert`.

The legacy `patch-client.ps1` (asset-byte-edit + hosts file + mkcert
wildcard) is kept for backwards compat — see the file's header for
when you'd still want it.

## Manual install (if you don't want to use install-plugin.ps1)

Same steps, just done by hand. Useful for understanding the layout.

## Why this exists

The vanilla 2020 client never sets `PhotonNetwork.AuthValues` — verified
by an exhaustive grep across both `Assembly-CSharp.dll` and the Photon
`Assembly-CSharp-firstpass.dll` (no callers of
`PhotonNetwork.AuthValues =`, `new AuthenticationValues(...)`,
`AddAuthParameter(...)`, or `SetAuthPostData(...)`). Photon Cloud
therefore assigns a random per-connection UserId and our
`/photon/customauth` callback receives no client identity, so we can't
reject duplicate sessions.

This plugin patches `NetworkingPeer.OpAuthenticate` /
`OpAuthenticateOnce` (the single chokepoint right before Photon's
authenticate op leaves the wire) and attaches:

- `AuthType   = CustomAuthenticationType.Custom`
- `UserId     = <RecNet account id>`
- `AuthGetParameters: userid=<id>, LoginLock=<token>`

That makes the Photon Cloud `{userid}` / `{LoginLock}` placeholders
populate, our endpoint validates `LoginLock` against
`PlayerPresenceService` (Redis), and Photon's own UserId-collision
detection kicks duplicate connections automatically.

## Prerequisites

- 2020 client build (Steam build `20200306`, AppID `471710`).
- BepInEx **6.x IL2CPP** — *not* the regular Mono build. Download from
  https://github.com/BepInEx/BepInEx/releases (look for the
  `BepInEx_unix_x86_64_6.0.0-be.696_*.zip` line, Windows variant).
- .NET 6 SDK on the build machine (this project uses `net6.0`).

## One-time Setup

1. **Install BepInEx 6 IL2CPP into the client directory.** Extract the
   release zip into the folder containing `Recroom_Release.exe`. After
   extraction the layout looks like:

   ```
   <RecRoomPath>/
     Recroom_Release.exe
     BepInEx/
       core/
       config/
       plugins/        ← we drop our DLL here
     dotnet/           ← BepInEx-shipped runtime
     winhttp.dll       ← BepInEx loader
     doorstop_config.ini
   ```

2. **Generate Il2Cpp interop assemblies.** Run the game once. BepInEx
   inspects the IL2CPP metadata on first launch and writes interop
   assemblies to `BepInEx/interop/`. This is a one-time ~30s step;
   re-do it any time the game updates.

   You should see lines like
   `[Info: BepInEx] Generating Il2Cpp interop assemblies` in
   `BepInEx/LogOutput.log`.

3. **Quit the game.** The interop assemblies are now sitting in
   `BepInEx/interop/Il2CppAssembly-CSharp.dll` etc.

## Building the plugin

The csproj references BepInEx core assemblies via `BepInExCore` and generated
interop assemblies via `BepInExInterop`. Build from the repo root:

```pwsh
$env:BepInExCore = "C:\Program Files (x86)\Steam\steamapps\common\RecRoom\BepInEx\core"
$env:BepInExInterop = "C:\Program Files (x86)\Steam\steamapps\common\RecRoom\BepInEx\interop"
dotnet build DorkNet.ClientPatch -c Release
```

Output lands at:

```
DorkNet.ClientPatch/bin/Release/DorkNet.ClientPatch.dll
```

## Installing the plugin

Copy the built DLL into the game's `BepInEx/plugins/` folder:

```pwsh
Copy-Item `
    DorkNet.ClientPatch/bin/Release/DorkNet.ClientPatch.dll `
    "$env:BepInExInterop\..\plugins\"
```

Or use the patcher (next section).

## Integration with `patch-client.ps1`

The patcher tool grew an `-InstallBepInEx` switch. Pass the path to a
freshly-extracted BepInEx 6 IL2CPP zip and it'll:

1. Extract BepInEx into the client folder if not already present.
2. Build this plugin against the existing `BepInEx/interop` directory.
3. Copy the DLL into `BepInEx/plugins/`.
4. Append a `Photon CustomAuth integration` step to the patch summary.

```pwsh
.\tools\patch-client.ps1 `
    -RecRoomPath "C:\…\RecRoom\Recroom_Release_Data" `
    -PhotonAppId  cb0880d9-… `
    -InstallBepInEx "C:\Downloads\BepInEx_unix_x86_64_6.0.0-be.696_…zip"
```

If the interop folder doesn't exist yet (game never launched
post-BepInEx-install), the patcher will tell you to launch the game
once and re-run with `-InstallBepInEx` to finish the build step.

## Verifying the plugin loaded

Launch the client. Tail `BepInEx/LogOutput.log` and look for:

```
[Info   :   BepInEx] Loading [DorkNet ClientPatch 1.0.0]
[Info   :DorkNet ClientPatch] DorkNet ClientPatch v1.0.0 loading…
[Info   :DorkNet ClientPatch] Photon AuthValues injection patches applied.
```

After login + the first Photon connect, also expect:

```
[Info   :DorkNet ClientPatch] [auth-injector] set Photon AuthValues — userid=1811750, LoginLock=<set>
```

On the server side, the `[photon-auth] received query={...}` log line
should now contain `userid=1811750&LoginLock=<guid>`.

## Server-side: flip strict validation back on

Once the plugin is shipping LoginLock in production:

1. Restore the strict-mode body of
   [`PhotonCustomAuthController`](../DorkNet.Server/Controllers/Auth/PhotonCustomAuthController.cs)
   — accept the parameters, look up the canonical lock from
   `PlayerPresenceService.ValidateLock(playerId, loginLock)`, and
   return `ResultCode 3` on mismatch.
2. In the Photon dashboard, flip **"Reject if Auth Failed"** ON.
3. Now a duplicate-account login on a *different* machine fails Photon
   auth → never reaches the dorm.

## Troubleshooting

**`Could not load file or assembly 'Il2CppAssembly-CSharp'`**
The interop folder isn't where the csproj expects. Set the
`BepInExInterop` env var to your install's interop path before `dotnet
build`.

**`No interop assemblies found, Il2CppType.Of<…>().GetField fails`**
Game never launched post-BepInEx-install. Launch once, quit, re-build.

**Plugin loads but `[auth-injector] skip — no LocalAccountId yet` on every connect**
The Photon connect happens before login completes. Inspect the log
order — if RecNet auth genuinely hasn't run yet, the plugin can't help
on that particular connect; it'll succeed on the next one (post-login
Photon reconnect when joining a room).

**Game crashes on launch with BepInEx but works without**
Either the plugin threw on Load (check `LogOutput.log` for stack
trace) or the BepInEx version mismatches — confirm you grabbed the
IL2CPP, *not* Mono, build for Windows x86_64.
