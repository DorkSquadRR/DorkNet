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
| `Jwt:Secret` | `Jwt__Secret` | yes | ≥64 random chars |
| `Photon:AppId` | `Photon__AppId` | yes | Photon Realtime AppId (free at dashboard.photonengine.com) |
| `Photon:VoiceAppId` | `Photon__VoiceAppId` | no | only if voice chat |
| `Photon:CloudRegion` | `Photon__CloudRegion` | no | defaults to `us` |
| `Database:Provider` | `Database__Provider` | no | `sqlite` (default) or `postgres` |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | postgres-only | full Npgsql connection string |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | no | enables SignalR backplane |
| `Domain:Apex` | `DORKNET_DOMAIN` | no | defaults to `localhost`; set for production |

**4. Boot:**

```pwsh
dotnet run --project DorkNet.Server
```

First boot creates `bin/Debug/net9.0/data/dorknet.db`, runs every
`LegacyUpgrades` pass, seeds canonical rooms / store catalog / club
categories / playlists, and starts listening. You should see
`Application started.` in the log; `curl http://localhost:8080/healthz`
returns 200.

For a full Docker / VPS deploy, see
[main's Advanced setup guide](../../blob/main/docs/advanced-setup.md).

---

## Patching your client

```pwsh
.\tools\install-plugin.ps1 `
  -RecRoomPath "C:\Path\To\Recroom_Release_Data" `
  -PhotonAppId "<your-photon-realtime-app-id>" `
  -PhotonVoiceAppId "<your-photon-voice-app-id>"
```

This installs the BepInEx 6 plugin into the client's `BepInEx/plugins/`
and writes its config under `BepInEx/config/`. The plugin rewrites all
`*.rec.net` URIs to your configured DorkNet apex, swaps the Photon
AppIds, and bypasses TLS verification on the client's HTTPS calls so
self-signed certs work.

---

## Code map

| Folder | Purpose |
|---|---|
| `DorkNet.Server/` | ASP.NET Core backend (controllers, services, data, hubs, middleware) |
| `DorkNet.Models/` | DTOs shared between server and client mod |
| `DorkNet.ClientPatch/` | BepInEx IL2CPP plugin (production patcher) |
| `DorkNet.ClientMod/` | MelonLoader port (alternative patcher) |
| `tools/` | Installers (`install-plugin.ps1`, `install-melon.ps1`, `install-legacy-client.ps1`, `remove-eac.ps1`), Cloudflare tunnel templates |

See [docs/architecture.md](docs/architecture.md) for the full mental
model (project layout, request lifecycle, watch-mirror controller
pattern, where to start contributing).

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
