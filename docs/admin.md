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
| Activity | `/activity` | Admin audit log and per-player request logs |
| Content | `/rooms`, `/rooms/:id`, `/import-room`, `/content` | Room list/detail, room import, instances, leaderboards, community board, loading tips |
| Operations | `/broadcast`, `/settings` | Server broadcast, server toggles, signup codes, weekly challenges, Play menu tags, Rec Center doors, game config values |

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
