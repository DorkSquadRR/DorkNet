# DorkNet — `march-2020-03-10`

This is the **March 2020.03.10** branch of DorkNet — a self-hostable
reimplementation of the Rec Room backend tuned to the wire protocol of
the **Rec Room 2020.03.10** client build (also serves 2020.03.06).

> ⚠️ **Not affiliated with Rec Room Inc. or Against Gravity.**
> See [DISCLAIMER](../../blob/main/DISCLAIMER.md). DorkNet ships no Rec
> Room game assets or modified binaries — you supply your own
> legally-acquired Rec Room install.

If you're not sure which DorkNet branch you want, start at the
[main branch's chart](../../blob/main/BRANCHES.md). The launcher on
main also auto-detects your install.

---

## What this branch targets

| | |
|---|---|
| **Client build** | Rec Room 2020.03.10 (also 2020.03.06) |
| **Schema** | Multi-step EF migrations (~34 phases) |
| **Diverges from `december-2020-12-18` by** | ~170 files |

Subsystems **NOT** present on this branch (these landed in December):

- Clubs surface
- Playlists (curated + user-created)
- Group DMs / chat threads
- Room keys (purchasable room access)
- Per-room co-owner / moderator / host roles
- Late-2020 store catalog additions

Weekly challenges **are** present on this branch: the admin SPA's
**Settings → Server** tab edits the weekly slate, the `CompletedRequired`
flag, and the gift (XP + tokens, plus an optional store skin/consumable
that's granted straight to the player's inventory when the week's
challenges complete).

If you need any of those, you're on the wrong branch — see
[`december-2020-12-18`](../../tree/december-2020-12-18).

---

## Quickstart

**1. Install .NET 9 SDK** and (optionally) `dotnet-ef`:

```pwsh
dotnet tool install --global dotnet-ef
```

**2. Clone this branch:**

```pwsh
git clone --branch march-2020-03-10 https://github.com/DorkSquadRR/DorkNet
cd DorkNet
```

**3. Configure secrets.** Drop in `DorkNet.Server/appsettings.Local.json`
(gitignored) or export environment variables:

| Config key | Env var | Required | Notes |
|---|---|---|---|
| `Jwt:Secret` | `Jwt__Secret` | yes | ≥64 random chars |
| `Photon:AppId` | `Photon__AppId` | yes | Photon Realtime AppId (free at dashboard.photonengine.com) |
| `Photon:VoiceAppId` | `Photon__VoiceAppId` | no | only if voice chat |
| `Photon:CloudRegion` | `Photon__CloudRegion` | no | defaults to `us` |
| `Database:Provider` | `Database__Provider` | no | `sqlite` (default) or `postgres` |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | postgres-only | full Npgsql connection string |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | no | enables SignalR backplane |
| `Domain:Apex` | `DORKNET_DOMAIN` | no | defaults to `localhost`; set for production |

`/connect/token` issues access and refresh tokens with the configured
auth host as the issuer. Set `DORKNET_DOMAIN` for production so OpenID
discovery and JWT validation agree on `https://auth.<domain>`.

**4. Boot:**

```pwsh
dotnet run --project DorkNet.Server
```

First boot creates `bin/Debug/net10.0/data/dorknet.db`, applies all EF
migrations under `DorkNet.Server/Migrations/`, seeds canonical rooms +
store catalog, and starts listening. You should see `Application
started.` in the log; `curl http://localhost:8080/healthz` returns 200.

For Docker / VPS, see [docs/deploy.md](docs/deploy.md).

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
that, then re-run with `-ResumeBuild`.)

### Standalone Quest build (experimental)

The March Quest APK is also IL2CPP and can build the same
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
- March uses the older PhotonServerSettings path: the ClientMod polls
  until `PhotonNetwork.PhotonServerSettings` is loaded, then rewrites
  `AppID`, `VoiceAppID`, and the fixed region by reflection.
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

If MelonLoader isn't installable on the user's machine, the legacy
`tools/install-legacy-client.ps1` does the same patches via direct
byte-edits of `resources.assets` + `GameAssembly.dll`. Slower setup,
no Harmony runtime needed.

---

## Code map

| Folder | Purpose |
|---|---|
| `DorkNet.Server/` | ASP.NET Core backend (controllers, services, data, hubs, middleware) |
| `DorkNet.Server/Migrations/` | EF Core schema migrations applied at boot |
| `DorkNet.Models/` | DTOs shared between server and client mod |
| `DorkNet.ClientMod/` | MelonLoader IL2CPP mod — the client patcher |
| `tools/` | Installers + Cloudflare tunnel templates |

Compared to `december-2020-12-18`, this branch's server is closer to
the original 2020-Q1 wire shape — fewer endpoints, simpler entities,
and the multi-step migrations haven't been consolidated.

### Signup codes

When account creation is disabled (admin **Settings → Server**), the only
way in is an admin-issued single-use **signup code**: generate one in the
admin panel's **Settings → Signup codes** tab (with a descriptor + optional expiry),
hand it to the player, and they redeem it on the site's **`/join`** page —
which creates their account bound to the device their game client
reported, so the next launch logs straight in. The `SignupCodes` /
`PendingDevices` tables post-date the migration chain and are created by
an idempotent `CREATE TABLE IF NOT EXISTS` step at boot (Program.cs), so
existing SQLite/Postgres DBs pick them up without a new migration.

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

Branch-specific:

- Schema changes on this branch don't flow to `december-2020-12-18`
  automatically. If a fix applies to both, cherry-pick it across.
- Don't rebase across branches — they're independent forks.
- New EF migrations: `dotnet ef migrations add <Name> --project DorkNet.Server`.
  `Migrate()` runs on boot; don't run `database update` manually.

---

## License

[AGPL-3.0](LICENSE). Hosted forks must publish their source.
