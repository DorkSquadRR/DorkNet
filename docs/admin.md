# DorkNet Admin Page

The admin page is the browser operations console for DorkNet. In the
microservices deployment it lives at `https://admin.<domain>/` and is
served by the `web` service from the built Vite SPA assets.

The admin backend endpoints are under `/api/admin/v1`. Requests from the
SPA are same-origin, so `https://admin.<domain>/api/admin/v1/*` is also
handled by the `web` service route on the admin host. The same
`/api/admin/*` route family is available as a path-owned moderation slice
for non-admin-host routing, so `/internal/routes` can show moderation as
the owner for that path family.

## Runtime Routing

| Item | Value |
|---|---|
| SPA source | `DorkNet.Server/admin-ui` |
| Build output | `DorkNet.Server/wwwroot/admin` |
| Container path | `/app/wwwroot/admin` |
| Public browser host | `admin.<domain>` |
| API base used by the SPA | `/api/admin/v1` |
| Login endpoint | `POST /api/admin/v1/login` |
| Same-origin admin-host service | `web` |
| Path-routed admin API slice | `moderation` |

`DorkNet.Gateway` preserves the original Host header when it proxies to
backend services. That matters for the admin page: the `admin.<domain>`
host route must reach `web`, and `web` must contain
`/app/wwwroot/admin/index.html`.

## Authentication

The first account created on a fresh database is promoted to admin so
there is always a bootstrap operator. The login form posts to
`/api/admin/v1/login`; the SPA stores the returned values in browser
`localStorage`:

| Key | Contents |
|---|---|
| `dorknet.admin.token` | DorkNet admin JWT |
| `dorknet.admin.me` | Admin identity object used by the layout |

Every admin API request sends `Authorization: Bearer <token>`. Protected
actions are guarded by `AdminOnlyAttribute`, which checks the resolved
player row still has `IsAdmin = true`. A `401` clears the local session
and sends the browser back to login.

If the admin host is public, put it behind Cloudflare Access or an
equivalent outer access control. The DorkNet JWT is still required after
that outer check.

## Navigation

| Section | Routes | Workflows |
|---|---|---|
| Overview | `/` | Live ops dashboard, online players, active sessions, quick kick/ban/broadcast actions |
| Moderation | `/players` | Player directory, bans, reports, per-player ban/grant/gift/password/avatar/account actions |
| Activity | `/activity` | Admin audit log, per-player request logs, QA test cases + GitHub issue linking |
| Content | `/rooms`, `/rooms/:id`, `/import-room`, `/content` | Room list/detail, room import, instances, leaderboards, community board, loading tips, 3D Charades word lists |
| Operations | `/broadcast`, `/settings` | Server broadcast, server toggles (signups, everyone-is-friends, profanity filter, imported-room version clamp), signup codes, weekly challenges, Play menu tags, Rec Center doors, game config values |

Several older admin URLs are kept as redirects:

| Old route | Current route |
|---|---|
| `/bans` | `/players?tab=bans` |
| `/reports` | `/players?tab=reports` |
| `/gift`, `/passwords`, `/grants` | `/players` |
| `/audit` | `/activity?tab=audit` |
| `/logs` | `/activity?tab=logs` |
| `/rr-originals`, `/instances`, `/leaderboards` | `/rooms` |
| `/community` | `/content?tab=community` |
| `/loading-tips` | `/content?tab=tips` |
| `/signup-codes` | `/settings?tab=signup` |

`/import-room-legacy` still exists for the legacy room importer.

## 3D Charades word lists (`/content?tab=charades`)

The March 2023 client fetches a charades deck at card-box spawn from
`GET api/activities/charades/v1/words/{source}`, where `{source}` is one
of three baked `CardBox.cardSource` slots — `Charades`,
`CharadesAprilFoolsDay`, and `Icebreakers` (verified in the 2023.03.21
il2cpp dump). The response is a JSON array of
`{ "EN_US": "<phrase>", "Difficulty": <int> }` (`Difficulty` is the client
`CNMMMNJJDMM` enum: 0 easy, 1 hard, 10 very hard, 20 icebreaker).

The admin tab exposes:

- A **library** of unlimited named word lists (`CharadesWordListEntity`
  rows), each with a per-card difficulty. Paste-import accepts one phrase
  per line with an optional `| easy|hard|veryhard|icebreaker` suffix.
- **Live card slots** — three dropdowns binding each client slot to any
  library list. Switching a slot just repoints its binding
  (`ServerSettingsEntity.CharadesSlotBindingsJson`); it takes effect on the
  next card-box refresh (room rejoin). An unbound slot falls back to the
  built-in list seeded for it.

Three built-in lists (Default / April Fools / Icebreakers) are seeded on
first boot and bound to their slots. Seeding is idempotent — admin edits
survive restarts.

## Profanity filter toggle (`/settings`)

`ServerSettingsEntity.ProfanityFilterDisabled` gates the server-side
`api/sanitize/*` filter. When on, every sanitize route returns input
unchanged and treats all text as clean, so room/invention names and chat
are never censored. Off by default; checked per request so it takes effect
immediately.

## Imported-room version clamp (`/settings`)

`ServerSettingsEntity.RoomBlobVersionClampDisabled` gates the CDN's
`PersistedRoomData` version clamp
(`RoomDataBlobService.ClampVersionsFor2023`). Rooms imported from modern
RecNet zip exports carry room-data blobs whose top-level version varints
(field 1 `DEPRECATED_RoomPersistenceVersion`, field 30
`PersistedRoomVersion`) are far past what the March-2023 client knows —
a Sep-2025 save stamps `version=131` against the client's maximum of 16
(`RoomDataBlobService.Client2023MaxPersistedRoomVersion`; the proto's
`LatestVersion=19` came from a newer build's dump and this client rejects
it) — and the client rejects the whole room with its "update Rec Room to
visit this room" gate before spawning anything.

This clamp only addresses the room-**header** version gate. A room whose
CircuitsV2 chip graph (`circuit_data`, field 18) was saved in a newer Rec
Room is rejected by a separate CircuitsV2 version gate regardless — check
the client's `Player.log`, where the stack frame under "Booting player to
dorm" names the failing subsystem (`CircuitsV2Manager` = the circuit
graph, not the header). That case is not fixable by this clamp.

While the clamp is active (the default), the CDN serve path rewrites the
two varints down to the 2023 maxima on the way out for `.room` / `.meta` /
`.dat` blobs. The rewrite is wire-surgical: every other byte is preserved,
already-compatible blobs pass through byte-identical, and nothing stored
in S3 is modified. Toggle endpoint:
`POST api/admin/v1/settings/room-blob-version-clamp` with
`{"Disabled": bool}`. Clamped responses are served with a 60s edge TTL so
flipping the toggle lands within a minute.

## Per-sub-room max players (`/rooms` → room detail)

Player caps are two separate settings, and conflating them is the easy
mistake here:

| Setting | Column | Scope |
| --- | --- | --- |
| `MaxCapacity` | `RoomEntity.MaxCapacity` | What the ROOM advertises; flows into `RoomInstance.MaxCapacity` for matchmaking and into the room-level `MaxPlayers` of the details payload. |
| `MaxPlayers` | `RoomSceneEntity.MaxPlayers` | What ONE sub-room caps itself at; appears per entry in the details `SubRooms[]`. |

The room-level figure must not be read off sub-room 0 — that collapses the
two into one and makes an admin edit to a sub-room silently move the
room's advertised capacity.

Sub-room caps have two write paths onto the same column:

- **In game** (room owner only) — the "Max Player Count in This Subroom"
  slider issues `PUT rooms/{id}/subrooms/{sub}/maxplayers`. Unlike most of
  its siblings this one does **not** form-encode: `NLDBPDCNNCF.MNBPCGJNLNP`
  (`Int64, Int64, Int32`) builds a `BLNOGFGHIIF` whose single JSON key is
  `maxPlayers`, with verb 3 = PUT. It reads the reply back as
  `FGCPNAACHIK`, so the response is the full room-details shape and has to
  carry the new value.
- **Admin** (any room, ownership not required):
  - `GET api/admin/v1/rooms/{id}/subrooms` — every sub-room with its cap,
    matchmaking flag, sandbox flag and current data blob. Row ids go out as
    **strings**; the SPA is JavaScript and mangles integers past 2^53.
  - `PUT api/admin/v1/rooms/{id}/subrooms/{subRoomId}/maxplayers` with
    `{"MaxPlayers": int}`. `subRoomId` is the sub-room's **order index** —
    the id the client and the rest of the wire use — not the row's primary
    key.

Both paths clamp to 1..80, matching the room-level cap in
`rooms/{id}/props`: 0 would make a sub-room unjoinable, and a shared range
keeps the two settings from disagreeing about what is a legal value.

Enforcement caveat carried over from `RoomEntity.MaxCapacity`: this is the
advertised cap. The client never sets Photon `RoomOptions.MaxPlayers`, so
hard enforcement still needs a ClientMod Photon patch.

## RRO room data blob — Maker Pen and chips in a Rec Room Original (`/rooms` → room detail → Sub-rooms)

Baked Rec Room Originals (Rec Royale, Paintball, Laser Tag, …) are seeded
with an empty `RoomSceneEntity.DataBlobName` and empty
`RoomEntity.CurrentDataBlobName`. Every details builder and matchmaking
resolver (`RoomsController.CurrentOrSyntheticDataBlobName`,
`GoToController.ResolveInitialDataBlobAsync`,
`MatchPlayerController`) turns that into an empty `DataBlob`, and the 2023
client reads an empty blob name as "this room has no room data": it skips
the persistence download entirely, so there are no room roles (no Maker
Pen despite the CDN's RRO role overlay), no CV2 room-settings chips, no
room mood engine and no saves. Real Rec Room served these rooms a blob —
Rec Royale's game-rule chip (`RecRoyaleSolos_GameRuleWrapperCircuit` /
`RecRoyaleSquads_GameRuleWrapperCircuit`, prefabs in `resources.assets`)
is delivered that way; the Frontier scene has no baked game rule.

The Sub-rooms tab shows, per sub-room, the blob the client will actually
be told to download (`effectiveBlobName`) and, for baked originals, two
actions:

- **Enable Maker Pen blob** — `POST api/admin/v1/rooms/{id}/subrooms/{subRoomId}/blob/enable`.
  Stamps the synthetic default name (`room_{id}_dorknet_v8.dat` for the
  entry scene, `room_{id}_dorknet_v8_sub{n}.dat` otherwise) on the scene
  and, for sub-room 0, on the room. No resolver changes are involved: they
  already return a non-empty name verbatim, and `CdnController` already
  answers that name for AG rooms with the RRO-editable blob (permissive
  room roles, no objects) while nothing is saved. `IsRRO` is derived from
  `IsAGRoom`, so the client keeps loading the baked map and only
  additionally fetches the blob. Idempotent; a no-op on a sub-room that
  already has a blob. Seeded originals such as Rec Royale have **no
  `RoomScenes` row at all** ("Scenes 0"); the list synthesises a virtual
  "Home" entry (`isVirtual: true`, max players = room capacity) and enable
  creates the real row with the game-side defaults
  (`RoomsController.GetOrCreateSceneForMutationAsync`).
  What the CDN hands back for that name while nothing is saved is a
  **per-room stub** built by `RoomDataBlobService.BuildRroEditableBlob`:
  the skeleton of a real save (Rec Center's `room_100_v1.dat` was the
  reference) — `version` 16 (the client's ceiling; the old roles-only stub
  was stamped 19 and the client fetched it and silently loaded the room
  with no roles, i.e. no Maker Pen), `activity_id` = the room's
  `LocationReplicationId`, `last_save_time`, empty connectable graph /
  object model, scene settings and the four `room_mood_*` blocks, plus the
  permissive role collection. The miss path now runs the same 2023
  version clamp as the S3-hit path. It also carries the room's own three
  **game-role objects** (a default role named after the room, plus the
  built-in "In-Game" and "Eliminated" roles, well-known GUIDs shared by
  every room save — Rec Center and Stunt Runner both have exactly these).
  The 2023 client grants Maker Pen off the player's *game* role
  (`ICreatorRole.CreatorRoleCanUseMakerPen` on `GameRole`), so a blob with
  no role objects leaves even a co-owner with nothing to hold — that was
  the second reason "enable + co-owner" still showed no Maker Pen. The
  default role sets `can_use_maker_pen` to true, matching the permissive
  room-role overlay; the built-in roles mirror the reference saves.
  For the two Rec Royale rooms the stub also carries the room's
  **game-rule chip** (`RecRoyaleSolos_GameRuleWrapperCircuit` prefab id
  `983db81a…`, `RecRoyaleSquads_…` `857f7500…`, read from the client's
  spawnable-tool table; a saved Squads chip is header + prefab id + tag +
  node id + an empty wrapper payload), so the match logic exists on first
  join without anyone spawning it by hand.
- **Upload save** — `POST api/admin/v1/rooms/{id}/subrooms/{subRoomId}/blob/upload?mode=full|basics`,
  multipart `file` = a real `PersistedRoomData` save from ANY client
  version (`.binpb` / `.room` / `.dat`, e.g. a RecNet export of a custom
  Rec Royale room). The server rewrites it for this room
  (`RoomDataBlobService.PrepareUploadedRoomBlob`): `activity_id` → the
  room's `LocationReplicationId`, drops `output_log`, `sub_room_id` and
  every field the 2023 schema doesn't know, clamps the header versions to
  the 2023 maxima, and in `basics` mode keeps only the game-role (field 50)
  and game-rule (fields 40/41) objects while stripping the circuit payloads
  (`circuit_data`, connectable graph, `circuit_v2_data` — a newer
  CircuitsV2 graph is rejected by the client's own CV2 version gate no
  matter what). Stored under a fresh `room_{id}_upload_*.dat`, recorded in
  `RoomDataBlobs`, and the sub-room (and room, for sub-room 0) is pointed
  at it. On serve the CDN applies the RRO role overlay and clamp exactly
  as for Rec Center's saved blob. Response carries a `summary`
  (`viewsIn/viewsKept/roleViews/gameRuleViews/versionIn/versionOut`).
- **Reset to baked scene** — `POST api/admin/v1/rooms/{id}/subrooms/{subRoomId}/blob/reset`.
  Clears both pointers. This is the escape hatch if a save left the room
  unplayable. Saved blobs stay in `RoomDataBlobs`, so the in-game restore
  list can bring one back later.

Both are audited (`enable_subroom_blob`, `reset_subroom_blob`). Who can
then save the room in game is the normal rule in `SaveDataCore`: owner,
any admin, or an accepted co-owner (`RoomRoles.Role == 0`). Grant a
co-owner role from the Roles tab if a non-admin should be able to save.

Procedure that makes Rec Royale playable AND editable:

1. Enable the blob on the Solos room's sub-room 0 (and the Squads room's).
2. Join as an admin; the Maker Pen is available. Spawn the room's game-rule
   chip (`RecRoyaleSolos_GameRuleWrapperCircuit` in Solos,
   `RecRoyaleSquads_GameRuleWrapperCircuit` in Squads) — via the palette's
   Game Rules section, or the debug console's `DebugSpawnTool` if it is not
   listed — then save. Without that chip there is no `BattleRoyaleManager`
   in the scene and the match never starts.
3. From then on the saved blob (with the game-rule chip and whatever
   room-settings chips were placed) is what everyone loads. If a save
   breaks the room, Reset to baked scene.

Tests: `RroRoomBlobTests`.

### Why Rec Royale still has no Maker Pen after all of the above

The Maker Pen permission check (`AGMBHDNGOEH.IJEIEBGJJLB`) first runs a
room precondition (`LALJEMHEIBH`) that, via `MLKAFHHHEGA.GHPIBJPNOFF` →
`PNPLLCBBMJP.JGPBNEHFLJA`, looks the current room up in
`AGRoomRuntimeConfig` — a table **baked into the client's
`resources.assets`** — and returns a per-original boolean. Rec Center and
Stunt Runner allow creation there; Rec Royale does not. No blob, role or
server response changes that table, and it is consulted only for rooms the
client considers originals (`IsRRO` = our `IsAGRoom`).

Server-side experiment: clone Rec Royale, flip the clone's **Rec Room
Original (IsRRO)** checkbox off on the General tab
(`rooms/{id}/props` `IsAGRoom: false`), upload a real save to it, join. If
the client still loads the Frontier scene for a non-original, Rec Royale
becomes an editable template; if the scene fails to load, the table gate
has to be bypassed client-side (ClientMod postfix on `GHPIBJPNOFF`).

## Test cases ↔ GitHub issues

QA test cases can file and track GitHub issues. Configure with:

| Key | Meaning |
| --- | --- |
| `GitHub:Token` | PAT with `issues:write` on the target repo |
| `GitHub:Repository` | `owner/name`, e.g. `DorkSquadRR/DorkNet` |
| `GitHub:ReconcileIntervalMinutes` | Sweep interval, default 15; `0` disables sweeps but leaves the manual endpoint working |

**Without a token nothing breaks.** `IGitHubIssues.IsConfigured` is false, the
endpoints answer `503` with the reason, and the background reconciler logs once
and idles. A server with no GitHub credentials is a normal deployment.

UI: **Activity → Test cases** (`/activity?tab=testcases`). File or unlink an
issue per case, or reconcile them all. The buttons disable themselves with an
explanation when GitHub is unconfigured rather than failing on click.

Sub-room caps live on the room page: **Rooms → a room → Sub-rooms**.

Endpoints (admin-gated — these are not part of the 2023 client's surface):

- `POST api/testcasemanagement/v1/testcase/{id}/issue` — file and link.
  **Idempotent**: a case already carrying a live issue returns that issue
  instead of filing a duplicate, so sweeping a failing pass repeatedly is safe.
  The issue body carries the description, key, room, test pass and tester
  comments; labels are `qa` plus the case's own tags.
- `DELETE api/testcasemanagement/v1/testcase/{id}/issue` — unlink only. The
  issue itself is left alone; closing someone's issue as a side effect of
  tidying a QA link would be the wrong call.
- `POST api/testcasemanagement/v1/issues/sync` — reconcile now. The background
  service runs the same code path on its interval, so manual and automatic
  cannot drift.
- `GET api/admin/v1/testcases[?passId=&status=]` — the list the SPA renders,
  with each case's issue number and link.

Each of the three also answers under `/api/admin/v1/testcases/...`, because the
admin SPA speaks that prefix exclusively.

### Where the link is stored

In `TestCaseEntity.JiraBugUrl` — the field Rec Room's own QA tooling used for
the bug filed against a failing case. Reusing it means **no migration**, which
matters more here than the name: the Postgres path is `EnsureCreated`-only and
never replays migrations, so a new column has to be added twice (migration *and*
an idempotent `Ensure*` patch) or it is simply missing in production. A field
that already exists everywhere has neither problem, and the admin UI renders it
already. A leftover genuine Jira link is ignored rather than misparsed — the
issue number is only read from a URL matching the GitHub issue shape.

### What the reconciler will and won't change

| Issue | Case status | Result |
| --- | --- | --- |
| Closed | Failed | → Passed |
| Open | Passed | → Failed |
| anything | Claimed / NotYetTested | untouched |

Closed means fixed: the case that was failing on that bug now passes, and
reopening the issue undoes it. Only those two states are the reconciler's to
move — a case a tester has Claimed, or one nobody has run yet, is never
rewritten underneath them.

## Build And Deploy

The service Dockerfile builds the admin SPA for service images that need
static assets. In the microservices stack the `web` image must include:

```text
/app/wwwroot/admin/index.html
/app/wwwroot/admin/assets/*
```

The admin SPA calls relative URLs, so it does not need a configured API
origin as long as the browser is opened on `https://admin.<domain>/`.

The native admin mobile app points at the same public admin host; see
[`../DorkNet.AdminMobile/README.md`](../DorkNet.AdminMobile/README.md).

## Smoke Checks

Check the static host:

```bash
curl -I https://admin.yourdomain.com/
```

Check that protected admin API routing is alive. A `401` is expected
without a valid admin token:

```bash
curl -i https://admin.yourdomain.com/api/admin/v1/stats
```

Check the built assets inside the running `web` container:

```bash
docker exec <web-container> sh -lc 'find /app/wwwroot/admin -maxdepth 2 -type f | sort | head -50'
```

Useful logs:

```bash
docker logs <stack>-gateway-1 --tail=200
docker logs <stack>-web-1 --tail=200
docker logs <stack>-moderation-1 --tail=200
```

For normal browser admin traffic, start with `gateway` and `web`.
Check `moderation` when you are testing `/api/admin/*` through a
non-admin host such as `api.<domain>`.

## Troubleshooting

| Symptom | Check |
|---|---|
| `admin.<domain>/` returns 404 | `web` logs for `probe="/app/wwwroot/admin/index.html" exists=False`; rebuild/redeploy the `web` image |
| Admin page loads but actions 404 | Gateway `/internal/routes`, then whether the request host is `admin.<domain>` or a path-routed host |
| Login or actions return 401 | Token expired, local session cleared, or the player no longer has `IsAdmin = true` |
| Browser spins forever | Open the browser network tab, then check `gateway` and `web` logs for the stuck URL |
| Upload/import fails near 100 MB | Use the chunked importer; the SPA uses 50 MB chunks to stay below Cloudflare request limits |
