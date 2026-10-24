# DorkNet — `march-2023-03-21`

This is the **March 2023** branch of DorkNet — a self-hostable
reimplementation of the Rec Room backend tuned to the wire protocol of
the **Rec Room 2023.03.21** PC client build (`appVersion` `20230317`).

> ⚠️ **Not affiliated with Rec Room Inc. or Against Gravity.**
> See [DISCLAIMER](../../blob/main/DISCLAIMER.md). DorkNet ships no Rec
> Room game assets or modified binaries — you supply your own
> legally-acquired Rec Room 2023.03.21 install.

If you're not sure which DorkNet branch you want, start at the
[main branch's chart](../../blob/main/BRANCHES.md). The launcher on
main also auto-detects your install.

---

## What this branch targets

| | |
|---|---|
| **Client build** | Rec Room 2023.03.21 (PC, IL2CPP); reports `appVersion` `20230317` |
| **Accepted build IDs** (`api/versioncheck/v4?v=`, mapped in `appsettings.json` → `BuildIdToVersionKey`) | `20230317`, `20230321` |
| **Version key** (sent as `X-DorkNet-Version` header) | `march_2023_03_21` |
| **Version plugin** | `DorkNet.Server/Versions/Late2020/Late2020VersionPlugin.cs` (generation `March2023`; the folder name is historical) |
| **Schema** | EF migrations plus an idempotent schema bootstrap; SQLite + Postgres both work |
| **Runtime topology** | Gateway-fronted microservices with dedicated service slices and a monolith fallback |
| **Diverges from `december-2020-12-18` by** | ~220 files |

This branch serves **only** the 2023 client. It does not advertise or
answer the older 2020 build keys; 2020 and 2018 clients have their own
branches.

What the 2023 client gets here that the 2020 branches don't have:

- **Every 2023 client route.** All HTTP routes the 2023.03.21 client can
  issue reach a handler under the verb it actually uses, and
  `DorkNet.Server.Tests/ClientRouteCoverageTests.cs` fails the build if one
  regresses. Routes, verbs and wire shapes are in
  [docs/recroom-2023-client-api-complete.md](docs/recroom-2023-client-api-complete.md).
- **Realtime over the SignalR hub** (`hub/v1` on the notify host): every
  push matches the client's own reader — friends, messages, kicks and
  logouts, gifts, balances, consumables, player events, level-ups and
  account updates — and presence reaches both friends and any player who
  subscribed to you, honouring your online-status visibility:
  [docs/recroom-2023-notifications.md](docs/recroom-2023-notifications.md).
- **Room saves and sub-rooms** through the storage host, plus cloning and
  private instances:
  [docs/recroom-2023-room-save.md](docs/recroom-2023-room-save.md).
- **CircuitsV2 limits** served as gameconfig and lifted well past retail
  (network budgets, chips, compute, cloud values, ink):
  [docs/recroom-2023-circuitsv2-limits.md](docs/recroom-2023-circuitsv2-limits.md).
- **Room shops**: room consumables, room currencies and room keys:
  [docs/recroom-2023-room-consumables.md](docs/recroom-2023-room-consumables.md).
- **2023 wardrobe**: owned items, color variants keyed by combination GUID,
  saved outfits and custom avatar items (custom shirts).
- **2023 profile**: bio, display emoji, pronouns, identity flags, banner
  image and online-status visibility.
- Inventions, clubs (roles, permissions, gallery), chat threads, playlists,
  quest reward chests, leaderboards, and the influencer and moderation
  surfaces.

---

## Quickstart

**1. Install .NET 9 SDK** and (optionally) `dotnet-ef`:

```pwsh
dotnet tool install --global dotnet-ef
```

**2. Clone this branch:**

```pwsh
git clone --branch march-2023-03-21 https://github.com/DorkSquadRR/DorkNet
cd DorkNet
```

**3. Configure secrets.** Drop in `DorkNet.Server/appsettings.Local.json`
(gitignored) or export environment variables:

| Config key | Env var | Required | Notes |
|---|---|---|---|
| `Jwt:Secret` | `DORKNET_JWT_SECRET` or `Jwt__Secret` | yes | >=64 random chars |
| `Photon:AppId` | `Photon__AppId` | yes | Photon Realtime AppId (free at dashboard.photonengine.com) |
| `Photon:VoiceAppId` | `Photon__VoiceAppId` | no | only if voice chat |
| `Photon:CloudRegion` | `Photon__CloudRegion` | no | defaults to `us` |
| `Database:Provider` | `Database__Provider` | no | `sqlite` (default) or `postgres` |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | postgres-only | full Npgsql connection string |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | no | enables SignalR backplane |
| `Domain:Apex` | `DORKNET_DOMAIN` | no | defaults to `localhost`; set for production |
| `S3:Endpoint` | `S3__Endpoint` | production | S3-compatible API endpoint for blobs/images |
| `S3:AccessKey` | `S3__AccessKey` | production | S3 access key |
| `S3:SecretKey` | `S3__SecretKey` | production | S3 secret key |
| `S3:Region` | `S3__Region` | no | defaults to `garage`; use `auto` for R2 |

**4. Boot a local standalone server:**

```pwsh
dotnet run --project DorkNet.Server
```

First boot creates `bin/Debug/net9.0/data/dorknet.db`, runs every
`LegacyUpgrades` pass, seeds canonical rooms / store catalog / club
categories / playlists, and starts listening. You should see
`Application started.` in the log; `curl http://localhost:8080/healthz`
returns 200.

For the production microservices stack, Docker, and Dokploy deploy
notes, see
[docs/deploy.md](docs/deploy.md).

---

## Patching your client

The 2023.03.21 PC build is laid out like this:

```
<install>/
  RecRoom.exe          <- the game; run this
  RecRoom_Data/        <- globalgamemanagers, resources.assets, levelN
```

The client is IL2CPP, so it is patched at runtime with **MelonLoader
0.6.x** and the `DorkNet.ClientMod` mod:

1. Install MelonLoader 0.6.x into the install folder, launch
   `RecRoom.exe` once so MelonLoader generates its IL2CPP assemblies, then
   quit.
2. Build the mod and copy it into `Mods\` next to `RecRoom.exe`:

   ```pwsh
   $env:DOTNET_ROOT = "C:\Program Files\dotnet"
   dotnet build .\DorkNet.ClientMod -c Release
   Copy-Item .\DorkNet.ClientMod\bin\Release\DorkNet.ClientMod.dll "<install>\Mods\"
   ```

   Close the game first; the copy fails while `RecRoom.exe` holds the DLL.
3. Copy `DorkNet.ClientMod\dorknet-clientmod.json.template` to
   `<install>\UserData\dorknet-clientmod.json` (MelonLoader 0.6 keeps
   `UserData\` at the install root), then fill in your DorkNet apex and
   Photon AppIds. The config is read once at startup.

The mod rewrites all `*.rec.net` URIs to your DorkNet apex, swaps the
Photon AppIds (they are hard-coded in the IL2CPP binary, so this has to
happen at runtime), and can bypass TLS verification on the client's HTTPS
calls so self-signed certs work.

> `tools\install-melon.ps1` automates these steps for the **2020** layout
> (`Recroom_Release.exe` / `Recroom_Release_Data`). It does not recognise
> the 2023 `RecRoom.exe` / `RecRoom_Data` layout yet, so use the manual
> steps above for 2023.

### Quest

`DorkNet.Quest` patches the Rec Room 2023.03.21 Quest client (versionCode
`2128210`) for sideloading onto a standalone Meta Quest. That build reports
`appVersion` `20230317`, the same version as the PC client, so Quest and PC
players can share rooms.

The patcher injects a single native hook library into the APK. The library
redirects the Rec Room name server to your DorkNet host, supplies your
Photon AppIds, and turns off the client's certificate pinning and
image-signature checks so a self-hosted server works. A build without a
Meta app ID lets players sign in with DorkNet credentials and no Meta
account. Each hook checks a fingerprint of its target first, so the patch
refuses to apply to any other build.

You supply your own extracted APK and OBB; no game binaries are
redistributed. The build can package itself as a `.xapk` that players
install from the **Quest** page of your DorkNet site (`https://<apex>/quest`)
in Chrome or Edge over USB, with no adb or SideQuest. Build, install and
troubleshooting steps are in [DorkNet.Quest/README.md](DorkNet.Quest/README.md),
and the operator view is in [docs/quest.md](docs/quest.md).

> Tested on a Quest 2: the hooks arm, the client reaches the server and the
> browser installer works. Full in-game play has not been verified yet.

### Debugging (opt-in — `DorkNet.DebugMod`)

The shipping `DorkNet.ClientMod` carries only what's needed to connect and
survive anti-cheat. All diagnostics — HTTP/request tracing, join/quit/studio/
room-load traces, the process-exception sink, auth-token tracing, the
registration-dialog trace, and the in-game debug console — live in a separate
`DorkNet.DebugMod`. Drop `DorkNet.DebugMod.dll` into the `Mods` folder next to
`DorkNet.ClientMod.dll` to turn them on; remove it to ship a quiet, minimal
client. Debug knobs are read from the same `dorknet-clientmod.json`.

Retail builds keep the `RecRoom.Debugging.DebugConsole` UI and serialized
`DebugConsoleCommandConfig` list, but the public 2023 client strips the
native command submit path. With `DorkNet.DebugMod` installed, set
`"EnableDebugConsole": true` and relaunch: it force-toggles the console UI on
a hotkey (`"DebugConsoleToggleKey"`, default `BackQuote` = the `~` key),
intercepts the dead submit callback, and invokes commands from the runtime
command metadata directly. Type `help` in the console to list discovered
commands. `"DevCommands"` may contain optional `"Label=Command args"`
shortcuts. The feature stays off by default and also silences the retail
`CheatManager` callbacks used by movement/time commands.

### Desktop Screen Sharing FPS (opt-in)

The Maker Pen "Desktop Sharing Screen"
(`RecRoom.Tools.Productivity.DesktopScreenSharingDisplay`) broadcasts at a
baked refresh rate (~5 fps). Set `"DesktopScreenShareFps"` in
`dorknet-clientmod.json` (e.g. `30`) and relaunch to override it — the mod
rewrites the gadget's `screenShareImageRefreshFrequency` at runtime and,
when `"DesktopScreenShareRaisePhotonRate"` is true (default), lifts
`PhotonNetwork.SendRate`/`SerializationRate` to match so the frames
actually transmit (the image streams over `OnPhotonSerializeView`, capped
at the serialization rate otherwise). This is **global** — it raises every
object's network sync rate, so back it off on a busy room.
`"DesktopScreenShareResolution"`/`"DesktopScreenShareQuality"` (both `0` =
keep the prefab value) trade per-frame size for bandwidth if a high FPS
saturates the link. Only the player *sharing* needs the override. Default
`0` (off).

### Sign-in page instead of a new account

When the title screen finds no remembered account for the machine, Rec Room
creates a brand-new account automatically. On a server with sign-ups off that
is refused and the title screen keeps retrying. By default the mod opens the
**sign-in page** instead, so players log into their existing DorkNet account.
Set `"AutoCreateAccount": true` in `dorknet-clientmod.json` to keep Rec Room's
behaviour on a server that allows sign-ups. The Quest build has the same
option (`AutoCreateAccount` in `dorknet-quest.cfg`).

### RRO quest team size (opt-in)

RRO quests ship with a baked 4-player `GameConfigurationAsset` team cap.
Set `"QuestMaxTeamSize"` in `dorknet-clientmod.json` (for example `8`) to
raise that cap for the allowlisted quest configs in
`"QuestMaxTeamSizeRooms"`:

```json
{
  "QuestMaxTeamSize": 8,
  "QuestMaxTeamSizeRooms": [
    "CrimsonCauldron",
    "Crescendo",
    "GoldenTrophy",
    "IsleOfLostSkulls",
    "TheRiseofJumbotron"
  ]
}
```

The quest host must run the mod: the host builds the game configuration
that gets networked to joiners. The patch also relaxes the quest spawn
filter for extra players, so player indexes above the four baked spawn
slots can reuse existing quest spawn points instead of being left out.
Default `0` (off).

### Rec Royale night mode (opt-in)

Rec Royale never shipped a night variant, but the client carries the room
"mood" engine (`RecRoom.Core.Scene.SceneMoodSettings`) and ready-made
night `MoodSetting` assets in `resources.assets`. Set
`"RecRoyaleNightMode"` in `dorknet-clientmod.json` and the mod applies one
of those moods (sky dome, sun, fog, bloom and colour grading) as soon as
the `RecRoyale_Frontier` scene finishes loading, and unlocks the weapon
flashlight prefab while it is on:

```json
{
  "RecRoyaleNightMode": true,
  "RecRoyaleNightMood": "Night_Calm_Outdoor_Mood",
  "RecRoyaleNightScenes": ["RecRoyale"],
  "RecRoyaleNightFlashlights": true,
  "RecRoyaleNightFog": false,
  "RecRoyaleNightExposure": -3.5,
  "RecRoyaleNightSunScale": 0.15,
  "RecRoyaleNightAmbientScale": 0.2,
  "RecRoyaleNightForcePostProcessing": true,
  "RecRoyaleNightMatchSunLights": true,
  "RecRoyaleNightDisableFog": false,
  "RecRoyaleNightHideObjects": ["Cloud"],
  "RecRoyaleNightToggleKey": "N"
}
```

`RecRoyaleNightDisableFog` turns fog off entirely while night is on
(`RenderSettings.fog` forced off and the mood's fog block pushed out of
range); it is ignored when `RecRoyaleNightFog` is `true`.
`RecRoyaleNightHideObjects` hides every renderer whose object or material
name contains one of the entries; the default hides Frontier's daytime
cloud meshes (`CloudPlane`, `Clouds`, the `_Cloud_RecRoyale_Frontier_Mat`
static batch). Both are restored on toggle-off or scene change.

The mood engine only ever drives `RenderSettings.sun`, which on Frontier
is the sun-disc light, not the map's key light, so daylight would keep
casting. `RecRoyaleNightMatchSunLights` (default `true`) copies the
engine-driven sun's rotation and colour onto every other directional
light and scales their intensity by `RecRoyaleNightSunScale`.

Two more RRO quirks the mod works around: the mood skybox material the
engine swaps in (`MoodConfig.SkyBoxMaterial`) is not loaded outside
custom-room scenes, so the mod builds one on the `Custom/SkyBox` shader
and hands it to the engine (otherwise the sky is plain black and the
engine refuses to touch sky colours or the sun); and post-exposure only
shows when the scene Volume is on and the camera renders
post-processing, which `RecRoyaleNightForcePostProcessing` (default
`true`) forces while night mode is active.

Brightness: RRO maps are lightmapped, and baked lighting ignores the sun
and ambient changes a mood makes, so the mod also darkens the mood data
itself before applying it. `RecRoyaleNightExposure` is an EV offset added
to the mood's colour-grading post-exposure (a full-frame post effect, so
it dims lightmapped surfaces too; `0` = mood default), and
`RecRoyaleNightSunScale` / `RecRoyaleNightAmbientScale` multiply the
mood's sun and ambient intensities (`1` = unchanged). Raise the exposure
toward `0` if it is too dark.

RRO scenes have no room data blob, so the mood engine starts disabled
there; the mod replays the room-data loader's bring-up (disable, capture
the scene's own lighting, enable), points `RenderSettings.sun` at the
scene's directional light (Frontier ships without the reference, which
would otherwise skip every sun change), and re-captures once the mood
skybox material is in place so the sky colours apply. `RecRoyaleNightFog`
is off by default: the mood's fog block is swapped for one captured from
the scene, so fog stays as the map author left it.

Shipped moods: `Night_Calm_Outdoor_Mood` ("Calm Night"),
`Night_Spooky_Outdoor_Mood` ("Spooky Night"), `Night_Wild_Outdoor_Mood`
("Wild Night"), `Night_StuntRunner_Outdoor_Mood` and `OuterSpace_Mood`.
`RecRoyaleNightToggleKey` is an optional `UnityEngine.KeyCode` name that
flips night on/off in-game (off restores the scene's own mood); setting a
key also arms the feature when `RecRoyaleNightMode` is `false`. This is
purely **client-side**: the mod calls the mood engine's apply step
directly and never touches the networked room-mood index, so other players
keep their own view. Baked lightmaps are not changed, so interiors stay
lit; the mood's colour grading does the darkening. Default `false` (off).
Wire-level notes: `docs/recroom-2023-scene-moods.md`.

---

## Code map

| Folder | Purpose |
|---|---|
| `DorkNet.Server/` | Shared ASP.NET Core controller/service stack, standalone fallback, monolith fallback |
| `DorkNet.Models/` | DTOs shared between server and client mod |
| `DorkNet.Contracts/` | Shared service names, route ownership, service-map options, and health/probe contracts |
| `DorkNet.ServiceDefaults/` | Shared service health, JSON, HTTP client, service identity, and route-guard setup |
| `DorkNet.Gateway/` | Edge reverse proxy with service-map, route-table, and service-health endpoints |
| `DorkNet.Services.*` | Gateway-fronted service hosts for identity, rooms, notify, content, social, commerce, platform, moderation, and web |
| `DorkNet.ClientMod/` | MelonLoader IL2CPP mod — the client patcher |
| `tools/` | Installers (`install-melon.ps1`, `install-legacy-client.ps1`, `remove-eac.ps1`), Cloudflare tunnel templates |
| `Dockerfile`, `Dockerfile.service`, `docker-compose.microservices*.yml` | Standalone fallback image, reusable service image, and local/Dokploy microservices entrypoints |

See [docs/architecture.md](docs/architecture.md) for the full mental
model (project layout, request lifecycle, watch-mirror controller
pattern, where to start contributing).

### Client API contracts

Rec Room is shut down, so the clients can only be studied statically.
[docs/recroom-2023-client-api-complete.md](docs/recroom-2023-client-api-complete.md)
is the complete contract for the March-2023 client: every route it can
call, the HTTP verb, the request and response shapes, and a per-subsystem
diff against what this server currently implements. Read it before adding
or changing any endpoint that build touches — it also documents how to
recover verbs and response shapes from the disassembly yourself.

The equivalent material for the December-2020 client is in the
`recroom-2020-client-*` files alongside it.

### Microservices

The production topology is the gateway-fronted compose stack.
`docker-compose.microservices.yml` starts the gateway, identity, rooms,
notify, content, social, commerce, platform, moderation, web, and
monolith fallback service hosts plus Compose-managed Postgres and Redis
for local testing. `docker-compose.microservices.dokploy.yml` is the
Dokploy version; it adds a `cloudflared` sidecar so Cloudflare Tunnel
can route the apex and wildcard hostnames to the gateway without Dokploy
domain rows.

The gateway routes owned host/path slices to the dedicated service hosts.
The `web` service owns the apex, `www`, `admin`, and `feed` static hosts.
The monolith fallback is still present for unknown route families, so
public client URLs remain stable while the split continues.

The compose file intentionally does not start object storage. Point it at
your separate S3-compatible instance:

```pwsh
$env:DORKNET_S3_ENDPOINT="https://your-s3-endpoint"
$env:DORKNET_S3_ACCESS_KEY="..."
$env:DORKNET_S3_SECRET_KEY="..."
$env:DORKNET_S3_REGION="garage" # or auto for R2
docker compose -f docker-compose.microservices.yml up --build
```

For deployment details, including Dokploy env vars, Cloudflare Tunnel
routing, external Postgres, and S3 setup, see
[docs/deploy.md](docs/deploy.md).

For the browser admin console, its routes, auth model, and production
troubleshooting, see [docs/admin.md](docs/admin.md).

---

## Database & migrations

The consolidated `Initial` migration plus the migrations added since
(`Migrations/`) capture the schema. SQLite dev DBs apply it via `Database.Migrate()`; Postgres uses
`EnsureCreated()` behind a transaction-scoped advisory lock. The
entity model is the single source of truth — to add a column, edit the
entity, boot, done (on SQLite — Postgres prod needs a migration).

Data transforms that can't be schema (seed renames, computed backfills)
go in [`DorkNet.Server/Data/LegacyUpgrades.cs`](DorkNet.Server/Data/LegacyUpgrades.cs).
See [`DorkNet.Server/Data/MIGRATIONS.md`](DorkNet.Server/Data/MIGRATIONS.md)
for the full discipline.

Tables added after the consolidated `Initial` migration (e.g.
`SignupCodes` / `PendingDevices`) are created by an idempotent
`CREATE TABLE IF NOT EXISTS` step in
[`Startup/DatabaseBootstrap.cs`](DorkNet.Server/Startup/DatabaseBootstrap.cs)
so both providers pick them up on existing DBs without a new migration
(the Postgres path is `EnsureCreated`-only and never replays migrations).

### Signup codes

When account creation is disabled (admin **Settings → Server**), the only
way in is an admin-issued single-use **signup code**: generate one in the
admin panel's **Settings → Signup codes** tab (with a descriptor + optional expiry),
hand it to the player, and they redeem it on the site's **`/join`** page —
which creates their account bound to the device their game client
reported, so the next launch logs straight in.

### Everyone-is-friends toggle

For small servers where searching + friend-requesting each other is
friction, admin **Settings → Server → "Everyone is friends"** makes every
account a friend of every other account. It writes **no** relationship
rows — the friend graph is synthesized at read time
(`RelationshipQueries.EffectiveFriendIdsAsync`, gated on
`ServerSettings.GlobalFriendsEnabled`), so flipping it off reverts
instantly. Blocks are still honored and the system/coach account is
excluded, as are auto-generated `Player_NNN` placeholder accounts that
never set a real username (they only appear if there's a genuine friend
row). Flipping it broadcasts `RelationshipsInvalid` to every connected
watch, which calls `Relationships.RefreshList` and re-pulls
`api/relationships/v2/get` — so players see everyone appear (or disappear)
**without relogging**. It covers the friends list, the friends-online HUD,
and room-move presence fan-out.

---

## Single-server defaults (review before going public)

Several values are hardcoded or defaulted for the convenience of running a
small, trusted, single-operator server. They work out of the box for a group
of friends but should be reviewed / adapted before exposing the server to the
public internet:

- **Client build version (`appVersion`).** Hardcoded to this branch's client
  build (`20230317`) in the presence/match/notification responses
  (`MatchPlayerController`, `PlayerPresenceController`, `NotificationService`,
  `RoomsController`). It must match the build everyone runs, or players get a
  "version mismatch" and can't join each other. If you serve a different
  client build, change it (ideally hoist it to one shared constant).
- **No real authentication.** Accounts are keyed by a per-install `deviceId`
  the client generates (`PlayerService.GetOrCreateByDeviceAsync`) — **not** a
  verified identity (Steam ID is deliberately ignored so emulators work).
  Anyone who can reach the server with a patched client gets/creates an
  account. Use **Settings → Server → disable signups** + the
  [signup-code](#signup-codes) flow to gate who can join; there is no password
  wall by default.
- **First account becomes admin.** The first account created on a fresh DB is
  granted `IsAdmin` so there's always a root admin. On a public server, create
  your admin account first and verify no one else beat you to it.
- **Online status visibility.** Each player's in-game choice (Public,
  Friends only, Favorite friends only, Appear offline) is stored and
  honoured by both `GET /player` and the live presence pushes. Players who
  never pick one are Public. "Friends only" follows the
  [everyone-is-friends](#everyone-is-friends-toggle) toggle, so with that
  toggle on it shows them to everyone.
- **Room / matchmaking capacity.** New matchmade sessions and subrooms default
  to **8** players and are capped at **40** (`GameSessionService`,
  `RoomSceneEntity.MaxPlayers`, admin `MaxCapacity` clamp). The in-game "Max
  Player Count in This Subroom" slider raises it per room (slider ceiling 40).
- **Avatar unlock clamp.** The wardrobe clamp defaults high
  (`Avatar:MaxUnlockedItems` 4000 / per-slot 1000) for this desktop client —
  tune in config if you change the ownership model.
- **Hosts / domain.** The apex and per-service subdomains come from
  `DomainConfig` (`Domain:Apex`) / `appsettings`; set these to your own
  domain. The `Everyone-is-friends`, signup-disable, and similar switches live
  in **admin Settings**, not source.

None of these are security boundaries as shipped — treat the server as trusted
LAN/friends infrastructure unless you harden the items above.

---

## Contributing

See [main's CONTRIBUTING guide](../../blob/main/CONTRIBUTING.md) for
the project-wide rules.

Branch-specific: this is the repo's default branch, and Dependabot and
server PRs target it. Schema changes here don't flow to the 2020 or 2018
branches automatically. If a fix applies to more than one, cherry-pick
it across. Don't rebase across branches.

---

## Acknowledgements

Thanks to [RecFlare](https://github.com/recflare), an independent
open-source 2023 Rec Room server by djdevin, for sharing their work in the
open. Their server and Quest client were a valuable second opinion while
building this branch:

- **Quest:** `DorkNet.Quest` follows their approach of a single native hook
  library loaded through a `DT_NEEDED` entry on `libmain.so`. Their notes
  also showed which client checks a self-hosted server has to get past.
- **Wire contracts:** their implementation pointed us at several things we
  then confirmed in our own client disassembly. These include the profile
  edit form fields, the saved-outfit keys, the `{Success, Error}` envelope
  behind `isCreationAllowedForAccount`, and the gameconfig catalogue,
  including the `PlayMenuCarouselOrdering_*` format.
- **Accounts:** their warning that a Quest client without a Meta account
  sends the same platform id from every headset led to the placeholder
  platform-id handling in `PlayerService`.

No RecFlare code is included in DorkNet. RecFlare targets a newer client
(20230414), so every finding was re-verified against the 2023.03.21 build
before use.

---

## License

[AGPL-3.0](LICENSE). Hosted forks must publish their source.
