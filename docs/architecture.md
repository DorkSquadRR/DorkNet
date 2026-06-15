# Architecture

A mental model of the DorkNet codebase for new contributors. Read this
before you touch code; it explains layout, request flow, the design
patterns that look weird in isolation, and where to start.

For deployment, see [`deploy.md`](deploy.md). For wire-protocol
reverse-engineering deliverables, see the `recroom-2020-client-*`
files in this folder.

---

## Projects in the solution

```
DorkNet.sln
├── DorkNet.Server             Shared ASP.NET Core backend + monolith fallback
├── DorkNet.Models             Shared DTO + serialization types
├── DorkNet.Contracts          Shared service contracts for the service split
├── DorkNet.ServiceDefaults    Shared service-host setup
├── DorkNet.Gateway            Gateway/edge reverse proxy
├── DorkNet.Services.Identity  Auth/accounts service host
├── DorkNet.Services.Rooms     Rooms/matchmaking service host
├── DorkNet.Services.Notify    Notify/chat service host
├── DorkNet.Services.Content   CDN/images/photos/storage service host
├── DorkNet.Services.Social    Clubs/groups/events/social service host
├── DorkNet.Services.Commerce  Store/econ/inventory service host
├── DorkNet.Services.Platform  Service directory/config/platform service host
├── DorkNet.Services.Moderation Bug reporting/moderation service host
├── DorkNet.Services.Web       Public/admin/feed web host
└── DorkNet.ClientMod          MelonLoader 6 IL2CPP mod — the client patcher
```

| Project | Responsibility | Target |
|---|---|---|
| `DorkNet.Server` | Shared REST/controller/service stack, database bootstrap, standalone fallback, monolith fallback | .NET 9 |
| `DorkNet.Models` | DTOs the wire protocol speaks. No project dependencies. | .NET 9 |
| `DorkNet.Contracts` | Service names, route ownership, service-map options, health responses, probe responses | .NET 9 |
| `DorkNet.ServiceDefaults` | Common service health checks, JSON defaults, HTTP clients, service identity, route guard | .NET 9 |
| `DorkNet.Gateway` | Edge reverse proxy; routes host/path slices to services and exposes service health probes | .NET 9 |
| `DorkNet.Services.Identity` | Accounts, auth, platform login, JWT issuance route slice | .NET 9 |
| `DorkNet.Services.Rooms` | Rooms, room keys, matchmaking, discovery route slice | .NET 9 |
| `DorkNet.Services.Notify` | SignalR edge, messages/chat, notification fan-out route slice | .NET 9 |
| `DorkNet.Services.Content` | CDN paths, image/photo APIs, uploads, S3-backed blobs | .NET 9 |
| `DorkNet.Services.Social` | Clubs, groups, announcements, player events, subscriptions | .NET 9 |
| `DorkNet.Services.Commerce` | Catalog, storefronts, econ, inventory, inventions | .NET 9 |
| `DorkNet.Services.Platform` | Service URLs, config, version checks, geo, strings, telemetry-style surfaces | .NET 9 |
| `DorkNet.Services.Moderation` | Bug reporting, player reporting, sanitize, path-routed admin API, testcase routes | .NET 9 |
| `DorkNet.Services.Web` | Public site, admin static host, same-origin admin browser API, feed static host, site API | .NET 9 |
| `DorkNet.ClientMod` | MelonLoader 0.6.x IL2CPP mod — the client patcher, JSON config | .NET 6 |

`ClientMod` applies the client-side patches needed to point the 2020
watch at a DorkNet server: URI rewrite (`.rec.net` → configured apex),
Photon AppId override, and TLS trust bypass. It loads under
MelonLoader 0.6.x and reads its settings from a JSON config under
`MelonLoader/UserData/`.

---

## Server folder layout

```
DorkNet.Server/
├── Auth/          JWT config, IP-ban + player-ban middleware, AdminOnly attribute
├── Compat/        Version detection middleware, client-version registry, HttpContext extensions
├── Controllers/   REST endpoints (see below)
├── Data/          DbContext, Entities/, EF migrations, LegacyUpgrades, seed JSON
├── Hubs/          SignalR NotifyHub (real-time presence on notify.{apex}/hub/v1)
├── Protos/        Protobuf schemas — trimmed `dorknet_room_data.proto` + full decompiled `recroom_2020.proto`
├── Services/      ~27 business-logic services (PlayerService, RoomService, etc.)
├── Versions/      Per-version plugins; Late2020VersionPlugin marks the 2020.12.18 build
├── admin-ui/      Vite + React SPA source — builds to wwwroot/admin/
├── site/          Public website Vite SPA source — builds to wwwroot/site/
├── wwwroot/       Static file root, with per-host subdirs (admin/, site/, feed/)
├── Startup/       Host composition, DI registration, database bootstrap, middleware pipeline
└── Program.cs     Short composition root
```

## Runtime topology

The production runtime is the gateway-fronted service split.
`DorkNet.Server` remains available as the shared compatibility runtime
and as a standalone debug/fallback image. In the compose stack it runs as
`monolith` behind the gateway for route families that have not been
assigned to a dedicated service yet.

The service split works like this:

- `DorkNet.Gateway` reverse-proxies public requests by host/path.
- `DorkNet.Services.Identity`, `DorkNet.Services.Rooms`,
  `DorkNet.Services.Notify`, `DorkNet.Services.Content`,
  `DorkNet.Services.Social`, `DorkNet.Services.Commerce`,
  `DorkNet.Services.Platform`, `DorkNet.Services.Moderation`, and
  `DorkNet.Services.Web` run the shared server stack behind a route
  ownership guard. That keeps response shapes identical while moving
  traffic by domain/path slice.
- `DorkNet.Gateway` exposes `/internal/services` and
  `/internal/services/health`, plus `/internal/routes` for the active
  proxy table.
- `DorkNet.Services.Identity` exposes
  `/internal/identity/capabilities`.
- `DorkNet.Services.Rooms` exposes `/internal/rooms/capabilities`.
- `DorkNet.Services.Notify` exposes `/internal/notify/capabilities`.
- The other slices expose matching `/internal/{service}/capabilities`
  endpoints.

`docker-compose.microservices.yml` starts gateway, all dedicated service
slices, the monolith fallback, and Compose-managed Postgres and Redis for
local testing. `docker-compose.microservices.dokploy.yml` is the Dokploy
production shape: it adds a `cloudflared` sidecar that joins the same
Compose network and forwards Cloudflare Tunnel traffic to
`http://gateway:8080`. Object storage is external S3-compatible storage
configured with `DORKNET_S3_*` environment variables; the compose files
do not run MinIO or Garage.

`Dockerfile.service` is the reusable service image. It accepts
`PROJECT_PATH` and `APP_DLL` build args, rebuilds the admin/site Vite
SPAs for service images that need static assets, publishes the selected
.NET project, and runs that DLL in the runtime image. The root
`Dockerfile` builds only the standalone/fallback `DorkNet.Server` image.

For the admin browser console specifically, including the host routing
split between the admin static host and `/api/admin/v1`, see
[`admin.md`](admin.md).

### Controllers/ groupings

20 top-level groups, each responsible for one product surface the
watch (game client) talks to. The grouping mirrors the watch's URL
layout, which is why some folders are large — splitting them would
obscure the mirror relationship.

| Folder | Surface |
|---|---|
| `API/` | Main REST API split by subsystem (Avatar, Store, Rooms, Messages, Inventions, Leaderboard, BugReporting, Moderation, …) |
| `Accounts/` | Account creation, login, profile updates |
| `Admin/` | Admin SPA backend, gated by `AdminOnlyAttribute` |
| `Auth/` | Photon custom-auth (`/photon/customauth`), version-check `/api/versioncheck/v4` |
| `Cdn/` | Image transforms, room blob serving |
| `Clubs/` | `clubs.{apex}/*` — clubs surface |
| `Commerce/` | `commerce.{apex}/*` — monetization catalog |
| `Discovery/` | `discovery.{apex}/*` — room discovery + search |
| `Econ/` | `econ.{apex}/*` — currency + inventory |
| `Geo/` | `geo.{apex}/*` — region lookup |
| `Health/` | `/healthz` probe |
| `Match/` | Matchmaking + `/goto/*` flow |
| `Notify/` | REST-side companion to the SignalR hub |
| `Ns/` | `ns.{apex}/*` — service-URL registry the watch queries on startup |
| `PlayerSettings/`, `Rooms/`, `Site/`, `Storage/`, `Strings/` | Smaller surfaces with self-explanatory scope |

Destructive admin account removal lives in `AdminController` under
`DELETE api/admin/v1/players/{id}` and is surfaced from the Players
detail modal. It requires two exact confirmations: the current username
and `DELETE {id}`. The endpoint refuses system, self, and still-admin
accounts; personal rows are deleted while durable authored content is
reassigned to the system account.

---

## Request lifecycle

Middleware ordering matters in ASP.NET Core; the pipeline order in
`Program.cs` is below. Numbers are the order, not line numbers.

1. **Logging** — Serilog enriches every request with structured fields.
2. **HTTPS redirect** — Only when Kestrel has an HTTPS endpoint (direct
   mode); a no-op behind nginx/cloudflared.
3. **WebSocket config** — 30-second keep-alive pings to keep
   cloudflared from killing idle tunnels.
4. **Per-host static branches** (`MountStaticHost`) — `admin.{apex}`,
   apex, `feed.{apex}` each serve their own SPA from `wwwroot/<dir>`.
   Mounted with `UseWhen` (not `MapWhen`) so API calls under those
   subdomains still reach the controllers.
5. **IP-ban check** — earliest filter; rejects banned IPs before any
   work runs.
6. **Version detection** (`VersionDetectionMiddleware`) — reads
   `X-DorkNet-Version`, validates against the version registry,
   returns 426 on mismatch. Apex + admin/site/feed subdomains skip
   the gate (humans on browsers don't carry a client version).
7. **JWT authentication** — `UseAuthentication`.
8. **Authorization** — role/policy checks.
9. **Player-ban check** (`BanCheckMiddleware`) — after auth so it can
   see `ctx.User`.
10. **MapControllers** — route to controller actions.
11. **SignalR hub** — `NotifyHub` on `notify.{apex}/hub/v1`, WebSocket-only,
    `RequireHost` filter.

---

## Multi-tenancy + the domain config

DorkNet is built to run many concurrent players. Every feature assumes
multi-user semantics, not single-player sandbox. There's no global
"current player"; everything threads `playerId` through the request.

The deployment apex (default `localhost`, overridable via
`Domain:Apex` config or `DORKNET_DOMAIN` env var) is read once at
startup into the `DomainConfig` singleton. Inject it anywhere code
needs to build a subdomain URL — never hardcode `localhost`.

```csharp
public sealed class Foo(DomainConfig domain) {
    public string Url() => $"https://cdn.{domain.Apex}/...";
}
```

Per-controller `[Host("api.rec.net", "api.localhost")]` filters are
**gone**; allowed hosts are derived from `{apex}` + `*.{apex}` and
enforced by `HostFilteringMiddleware`. Subdomain-discriminating
handlers branch on `Request.Host.Host.Split('.')[0]`.

---

## Version plugins

`Versions/` holds an `IVersionPlugin` per client generation we support.
Today there's one (`Late2020VersionPlugin` for the 2020.12.18 build).

The pattern is a marker registry:

- `VersionKeys` — the wire-format identifiers a plugin claims.
- `Generation` — human-readable name controllers branch on via
  `ctx.GetClientVersion().Generation`.
- `RegisterStrategies(IServiceCollection)` — for plugins that ship
  generation-specific implementations (today empty; will grow as
  wire shapes diverge across builds).

When a new client build is wire-compatible with an existing
generation, just add its key to `VersionKeys`. When it diverges, add
a new plugin and register strategy bindings keyed by generation.

---

## Reverse-engineering provenance

DorkNet is a clean-room reimplementation of the 2020.12.18 Rec Room
backend protocol. ~46 files carry `Cpp2IL` provenance comments tying
specific code to decompiled client internals. Example:

```csharp
// Wire shape per Cpp2IL_ISIL/.../PLILLKHMNDA.txt deserializer:
// gap-skipping enum, not 0..N
public const int Member = 10, Moderator = 20, CoOwner = 30, Creator = 100;
```

The decompiled dumps themselves are not in this repo (`dist/RecRoom-2020.12.18-isil/`
locally, gitignored). The comments are the bridge — if you're writing
or fixing wire-shape code, always check the decompiled source first
rather than guessing from symptoms.

The `docs/recroom-2020-client-*` files are an exhaustive catalog of
client-side endpoints, DTOs, and request expectations extracted from
the same decompile. They're the ground truth when a controller needs
a new endpoint.

---

## Database story

Two providers, switched by `Database:Provider` config:

- **SQLite** — default for local dev. File at `bin/<config>/data/dorknet.db`.
- **Postgres** — production. Connection string from `ConnectionStrings:Default`.

Schema lives in `Data/Entities/*` and `Data/DorkNetDbContext.cs`.
SQLite dev DBs apply EF migrations under `Data/Migrations/` via
`Database.Migrate()`. The production Postgres path still uses
`EnsureCreated()` plus idempotent bootstrap work under the same
advisory-lock discipline. Data transforms that can't be expressed as
schema migrations (seed renames, computed backfills) go in
`Data/LegacyUpgrades.cs` as idempotent methods registered in `RunAsync()`.
See
[`Data/MIGRATIONS.md`](../DorkNet.Server/Data/MIGRATIONS.md) for the
discipline: one release = one migration; everything else is
`LegacyUpgrades`.

---

## Where to start

If you've never touched this codebase, the most approachable path:

1. Read a small **Service** (`ConfigService`, `DomainConfig`,
   `OnlinePresenceService`) to learn the DI pattern.
2. Read a small **Controller** (`Health/HealthController`,
   `Strings/StringsController`) to see how services are injected and
   responses are shaped.
3. Trace one **end-to-end request** through `Rooms/` or `Players/` —
   middleware → controller → service → DbContext → response. The
   middleware pipeline (above) is the lens.
4. Look at one **`Cpp2IL`-cited line** in a controller to see how
   wire shapes are pinned to decompiled provenance.

When in doubt: open `DorkNet.Contracts/DorkNetRouteOwnership.cs`,
`DorkNet.Gateway/Program.cs`, `DorkNet.Server/Program.cs`, and the
`Versions/Late2020/Late2020VersionPlugin.cs` summary doc-comment.
Together they explain route ownership, proxying, the shared server
pipeline, and version compatibility.

---

## Acknowledged sharp edges

These look messy. They're messy on purpose — the alternative is
worse.

- **Some controllers are large.** `AdminController.cs`, `RoomsController.cs`,
  `GoToController.cs` are big because they mirror watch surfaces that
  are themselves sprawling. Splitting them by endpoint group would
  obscure the surface-mirror relationship that makes the code
  navigable when paired with the watch's decompiled call sites.
- **Cpp2IL comments reference paths outside the repo.** That's
  intentional — the decompiled source is provenance only, not
  shippable. Comments give a future contributor a breadcrumb to
  re-derive the wire shape if the decompile changes.
- **Microservices use shared server code during extraction.** The
  dedicated service hosts run the proven controller/service stack behind
  route guards instead of copying controllers into new projects. This
  keeps the client contract stable while route families are peeled away
  from the monolith fallback.
- **Two-provider EF.** SQLite and Postgres each have their own
  EnsureCreated/Migrate dance because some Postgres-only column types
  drift the model snapshot. The split is annoying but lets one
  codebase serve both local dev (zero-config SQLite) and production
  (managed Postgres).
- **Hardcoded `rec.net` URLs in `RoomService` + `HtrAssetMirrorService`.**
  These pull canonical assets (room thumbnails, `.htr` asset data)
  from the official Rec Room CDN at seed time. The alternative is
  shipping the assets in the repo, which would put DorkNet on
  legally weaker footing.
