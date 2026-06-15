# DorkNet — `december-2020-12-18`

This is the **December 2020.12.18** branch of DorkNet — a self-hostable
reimplementation of the Rec Room backend tuned to the wire protocol of
the **Rec Room 2020.12.18** client build.

> ⚠️ **Not affiliated with Rec Room Inc. or Against Gravity.**
> See [DISCLAIMER](../../blob/main/DISCLAIMER.md). DorkNet ships no Rec
> Room game assets or modified binaries — you supply your own
> legally-acquired Rec Room 2020.12.18 install.

If you're not sure which DorkNet branch you want, start at the
[main branch's chart](../../blob/main/BRANCHES.md). The launcher on
main also auto-detects your install.

---

## What this branch targets

| | |
|---|---|
| **Client build** | Rec Room 2020.12.18 |
| **Version key** (sent as `X-DorkNet-Version` header) | `december_2020_12_18` |
| **Version plugin** | `DorkNet.Server/Versions/Late2020/Late2020VersionPlugin.cs` |
| **Schema** | One EF `Initial` migration; SQLite + Postgres both work |
| **Runtime topology** | Gateway-fronted microservices with dedicated service slices and a monolith fallback |
| **Diverges from `march-2020-03-10` by** | ~170 files |

Subsystems present on this branch that the March branch doesn't have:

- Chat threads + group DMs (`ChatThreads`, `ChatThreadMembers`)
- Clubs surface (`clubs.{apex}/*` — announcements, categories, subscriptions)
- Playlists (curated + user-created)
- Weekly challenges
- Room keys (purchasable room access)
- Room roles (per-room co-owner / moderator / host grants)
- Leaderboard channel metadata
- Loading-screen tips

---

## Quickstart

**1. Install .NET 9 SDK** and (optionally) `dotnet-ef`:

```pwsh
dotnet tool install --global dotnet-ef
```

**2. Clone this branch:**

```pwsh
git clone --branch december-2020-12-18 https://github.com/DorkSquadRR/DorkNet
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

```pwsh
.\tools\install-melon.ps1 `
  -RecRoomPath "C:\Path\To\Recroom_Release_Data" `
  -PhotonAppId "<your-photon-realtime-app-id>" `
  -PhotonVoiceAppId "<your-photon-voice-app-id>"
```

This installs the MelonLoader mod (`DorkNet.ClientMod`) into the
client's `Mods/` folder and writes its config to
`MelonLoader/UserData/dorknet-clientmod.json`. The mod rewrites all
`*.rec.net` URIs to your configured DorkNet apex, swaps the Photon
AppIds, and bypasses TLS verification on the client's HTTPS calls so
self-signed certs work. (First run prints "launch the game once"; do
that, then re-run with `-ResumeBuild` to finish — see the script's
`-?` help.)

### Standalone Quest build (experimental)

The December Quest APK is also IL2CPP and can build the same
`DorkNet.ClientMod` source against LemonLoader's Android runtime. The
current Quest path is still device-tested manually: build the mod as a
net8 LemonLoader DLL, install it into a LemonLoader-patched APK, then
sideload the resigned APK.

From this branch:

```pwsh
dotnet build .\DorkNet.ClientMod\DorkNet.ClientMod.csproj `
  -p:TargetFrameworks=net8.0 `
  -p:MelonLoaderDir="C:\path\to\LemonLoader\melon_data\MelonLoader"
```

Output lands at
`DorkNet.ClientMod\bin\Debug\net8.0\DorkNet.ClientMod.dll`. Desktop
builds still default to net6 and keep the historical flat output path.

Quest notes:

- Target package: `com.AgainstGravity.RecRoom`.
- Use a public HTTPS DorkNet host; keep `"EnableTlsTrustBypass": false`
  for Quest builds.
- The Dec Photon hooks are discovered from the unique
  `PUNNetworkManager` methods returning `AppSettings` and
  `AuthenticationValues`, with the known Dec obfuscated names as fallback.
- Standalone APK patching/resigning is not yet wrapped by
  `install-melon.ps1`; this is the porting path, not a public one-click
  installer yet.

### Debug console (opt-in)

The 2020 client ships a built-in dev console
(`RecRoom.Debugging.DebugConsole`) with commands like `SetTimeScale`,
`Fly`, `Teleport`, `GoToRoom`, and `KillAllEnemies` — normally locked to
developer accounts. Set `"EnableDebugConsole": true` in
`dorknet-clientmod.json` and relaunch: the mod force-toggles the console
UI on a hotkey (`"DebugConsoleToggleKey"`, default `BackQuote` = the `~`
key) and silences `CheatManager` so the movement/time commands don't drop
you to the dorm. Both default off.

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

---

## Database & migrations

One EF migration (`Migrations/<timestamp>_Initial.cs`) captures the
schema. SQLite dev DBs apply it via `Database.Migrate()`; Postgres uses
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

## Contributing

See [main's CONTRIBUTING guide](../../blob/main/CONTRIBUTING.md) for
the project-wide rules.

Branch-specific: schema changes on this branch don't flow to
`march-2020-03-10` automatically. If a fix applies to both, cherry-pick
it across. Don't rebase across branches.

---

## License

[AGPL-3.0](LICENSE). Hosted forks must publish their source.
