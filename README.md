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
**Server settings** page edits the weekly slate, the `CompletedRequired`
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

**4. Boot:**

```pwsh
dotnet run --project DorkNet.Server
```

First boot creates `bin/Debug/net9.0/data/dorknet.db`, applies all EF
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

When account creation is disabled (admin **Server settings**), the only
way in is an admin-issued single-use **signup code**: generate one in the
admin panel's **Signup codes** page (with a descriptor + optional expiry),
hand it to the player, and they redeem it on the site's **`/join`** page —
which creates their account bound to the device their game client
reported, so the next launch logs straight in. The `SignupCodes` /
`PendingDevices` tables post-date the migration chain and are created by
an idempotent `CREATE TABLE IF NOT EXISTS` step at boot (Program.cs), so
existing SQLite/Postgres DBs pick them up without a new migration.

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
