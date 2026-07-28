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
