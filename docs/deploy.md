# DorkNet Deployment

The recommended production shape is the gateway-fronted microservices
compose stack. The standalone `DorkNet.Server` image still exists for
local debugging and emergency single-container fallback, but new Dokploy
deployments should use `docker-compose.microservices.dokploy.yml`.

Object storage is always a separate S3-compatible service. The compose
files start Postgres and Redis, but they do not start MinIO, Garage, or
any other object-storage emulator.

---

## Production Topology

Public traffic enters at `DorkNet.Gateway`. The gateway preserves the
original Host header, routes owned host/path families to dedicated
service hosts, and sends unassigned route families to the `monolith`
fallback. That lets the 2020 client keep using the same public URLs
while route slices move out of the fallback server.

| Service | Purpose |
|---|---|
| `gateway` | Edge reverse proxy, service map, route table, and service health probes |
| `identity` | Auth, accounts, platform login, JWT issuance |
| `rooms` | Rooms, room keys, playlists, matchmaking, discovery |
| `notify` | Notify, messages/chat, SignalR, notification fan-out |
| `content` | CDN paths, uploads, images, photos, room blobs, storage APIs |
| `social` | Clubs, groups, announcements, player events, subscriptions |
| `commerce` | Catalog, storefronts, econ, inventory, inventions |
| `platform` | Service directory, config, version checks, geo, strings |
| `moderation` | Bug reports, player reports, sanitize, path-routed admin API, testcase routes |
| `web` | Apex/www/admin/feed static hosts, same-origin admin browser API, and site API |
| `monolith` | Fallback shared server for route families not split yet |
| `postgres` | Bundled Postgres for the compose network |
| `redis` | Bundled Redis for ephemeral state and fan-out |
| `cloudflared` | Cloudflare Tunnel sidecar in the Dokploy compose file |

Every backend service uses the same database, Redis, S3, Photon, domain,
JWT, and version-gate settings. The dedicated slices currently reuse the
shared `DorkNet.Server` controller/service stack behind route ownership
guards, so response contracts stay identical while the architecture is
split.

Auth tokens are issued by the identity service using the configured
auth host (`https://auth.<apex>` or the equivalent configured host style)
as the JWT/OpenID issuer. `DORKNET_JWT_SECRET` still supplies the signing
key for every service that validates bearer tokens.

---

## Dokploy Microservices

Create a Dokploy **Compose** service from:

```text
docker-compose.microservices.dokploy.yml
```

Use the GitHub repo/branch as the source and keep the build context at
the repository root. Do not add Dokploy domain rows for this compose
stack when using the Cloudflare Tunnel sidecar; public hostname routing
belongs to Cloudflare.

Set these Dokploy Compose environment variables:

```env
DORKNET_DOMAIN=yourdomain.com
DORKNET_JWT_SECRET=replace-with-at-least-64-random-characters
CLOUDFLARE_TUNNEL_TOKEN=<token from Cloudflare Zero Trust tunnel>

# Bundled compose Postgres. Optional when DORKNET_POSTGRES_CONNECTION_STRING is set.
POSTGRES_PASSWORD=replace-with-a-random-db-password

# External object storage. Required for production.
DORKNET_S3_ENDPOINT=https://your-s3-api-endpoint
DORKNET_S3_ACCESS_KEY=your-access-key
DORKNET_S3_SECRET_KEY=your-secret-key
DORKNET_S3_REGION=garage

# Photon Cloud.
Photon__AppId=your-photon-realtime-app-id
Photon__VoiceAppId=your-photon-voice-app-id
Photon__CloudRegion=eu

# December 2020 client version gate.
DORKNET_DEFAULT_CLIENT_VERSION=december_2020_12_18
DORKNET_SUPPORTED_VERSION=december_2020_12_18
DORKNET_DECEMBER_BUILD_VERSION_KEY=december_2020_12_18
```

The compose file maps the `DORKNET_S3_*` values into `S3__*` for every
backend service, and maps the version variables into `DorkNet__*`
configuration keys. `Photon__*` is passed directly to each service.

### Database Options

By default, service containers connect to the bundled compose Postgres:

```env
Host=postgres;Port=5432;Database=dorknet;Username=dorknet;Password=${POSTGRES_PASSWORD}
```

To use a Dokploy-managed or external Postgres instead, set a full
connection string:

```env
DORKNET_POSTGRES_CONNECTION_STRING=Host=<postgres-host>;Port=5432;Database=dorknet;Username=<user>;Password=<password>
```

For hosted Postgres that requires TLS:

```env
DORKNET_POSTGRES_CONNECTION_STRING=Host=<postgres-host>;Port=5432;Database=dorknet;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

`docker-compose.microservices.dokploy.yml` attaches every backend service
that opens database connections to both the stack's default network and
Dokploy's external `dokploy-network`. If you use another Dokploy-managed
database service, the hostname in `DORKNET_POSTGRES_CONNECTION_STRING`
must resolve to a Docker-network address from inside a DorkNet service:

```bash
docker exec <web-or-platform-container> getent hosts <postgres-host>
```

That should return a container/network IP, not `127.0.0.1`.

If you use the bundled compose Postgres, `POSTGRES_PASSWORD` must match
the password that initialized the existing Postgres volume. Changing
`POSTGRES_PASSWORD` later does not update the already-created `dorknet`
database user. Restore the original password, reset it inside Postgres,
or recreate the Postgres volume.

### Cloudflare Tunnel

In Cloudflare Zero Trust, configure two public hostnames:

```text
yourdomain.com      -> http://gateway:8080
*.yourdomain.com    -> http://gateway:8080
```

The wildcard covers `api`, `auth`, `rooms`, `notify`, `cdn`, `clubs`,
`commerce`, and the other client-facing subdomains. The apex rule is
separate because wildcard DNS does not cover the root domain. Let
Cloudflare create the tunnel DNS records, or create CNAME records to the
tunnel target Cloudflare gives you.

Do not point the tunnel at individual service containers. All public
traffic should go to `http://gateway:8080`.

### Public Hosts

The client service map and the browser surfaces expect these hosts to
reach the gateway:

```text
yourdomain.com
www.yourdomain.com
admin.yourdomain.com
api.yourdomain.com
auth.yourdomain.com
accounts.yourdomain.com
bugreporting.yourdomain.com
cards.yourdomain.com
cdn.yourdomain.com
chat.yourdomain.com
clubs.yourdomain.com
cms.yourdomain.com
commerce.yourdomain.com
data.yourdomain.com
datacollection.yourdomain.com
discovery.yourdomain.com
econ.yourdomain.com
feed.yourdomain.com
gamelogs.yourdomain.com
geo.yourdomain.com
img.yourdomain.com
leaderboard.yourdomain.com
link.yourdomain.com
lists.yourdomain.com
match.yourdomain.com
moderation.yourdomain.com
notify.yourdomain.com
ns.yourdomain.com
platformnotifications.yourdomain.com
playersettings.yourdomain.com
roomcomments.yourdomain.com
roomieintegrations.yourdomain.com
rooms.yourdomain.com
storage.yourdomain.com
strings.yourdomain.com
strings-cdn.yourdomain.com
studio.yourdomain.com
thorn.yourdomain.com
videos.yourdomain.com
```

With Cloudflare Tunnel, the apex and wildcard hostname rules cover the
list. If you deploy without a tunnel, every host must route to the
gateway container on port `8080`.

### Smoke Checks

After Dokploy finishes rebuilding, verify the gateway and service map:

```bash
curl https://api.yourdomain.com/healthz
curl https://api.yourdomain.com/internal/services
curl https://api.yourdomain.com/internal/services/health
curl https://api.yourdomain.com/internal/routes
curl https://api.yourdomain.com/api/versioncheck/v4?v=20201210
```

The version check should return:

```json
{"VersionStatus":0}
```

Browser hosts should route to `web` and return static files:

```bash
curl -I https://yourdomain.com/
curl -I https://admin.yourdomain.com/
curl -I https://feed.yourdomain.com/
```

Inside the running web container, these files must exist:

```bash
docker exec <web-container> sh -lc 'ls -la /app/wwwroot/admin /app/wwwroot/site /app/wwwroot/feed'
```

Expected key files:

```text
/app/wwwroot/admin/index.html
/app/wwwroot/site/index.html
/app/wwwroot/feed/index.html
```

`/internal/routes` shows which host/path patterns route to each service
slice or to the monolith fallback. `/internal/services/health` reports
gateway-visible health for each backend service.

For the admin page's browser routes, auth flow, same-origin API calls,
and focused troubleshooting, see [`admin.md`](admin.md).

### Useful Logs

```bash
docker logs <stack>-gateway-1 --tail=200
docker logs <stack>-web-1 --tail=200
docker logs <stack>-moderation-1 --tail=200
docker logs <stack>-platform-1 --tail=200
docker logs <stack>-cloudflared-1 --tail=200
```

If `admin.<domain>` or the apex returns 404, check the `web` logs for
static-host probes such as:

```text
probe="/app/wwwroot/admin/index.html" exists=False
```

That means the `web` image did not include the built SPA assets or the
old container is still running. Force a clean Dokploy rebuild on the
latest branch commit.

Admin browser traffic on `admin.<domain>` is routed to `web`. The
`moderation` service is still useful when testing the `/api/admin/*`
path family through non-admin hosts.

---

## Local Microservices

For local service-split testing without Dokploy:

```bash
docker compose -f docker-compose.microservices.yml up --build
```

This starts the gateway on host port `8080`, all service slices, the
monolith fallback, Postgres, and Redis. The local compose file does not
start `cloudflared`; direct local testing uses Host headers:

```bash
curl -H 'Host: api.localhost' http://localhost:8080/healthz
curl -H 'Host: admin.localhost' http://localhost:8080/
curl -H 'Host: localhost' http://localhost:8080/
```

Set `DORKNET_S3_*` if you need blob/image features locally. Leaving S3
blank is fine for API smoke tests that do not touch object storage.

---

## Standalone Server Fallback

The root `Dockerfile` builds the standalone `DorkNet.Server` image. Use
it for local debugging, narrow rollback tests, or a temporary
single-container deployment. The current production docs and Dokploy
workflow assume the microservices compose stack instead.

Create a Dokploy **Application** only if you intentionally want the
single-container fallback:

| Field | Value |
|---|---|
| Source | This GitHub repo / branch |
| Build type | Dockerfile |
| Dockerfile path | `Dockerfile` |
| Context | `.` |
| Container port | `8080` |

Standalone environment variables use the direct .NET configuration keys:

```env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
DOTNET_RUNNING_IN_CONTAINER=true

DORKNET_DOMAIN=yourdomain.com
DORKNET_JWT_SECRET=replace-with-at-least-64-random-characters

Database__Provider=postgres
ConnectionStrings__Default=Host=<postgres-host>;Port=5432;Database=dorknet;Username=<user>;Password=<password>
ConnectionStrings__Redis=redis://<redis-host>:6379

S3__Endpoint=https://your-s3-api-endpoint
S3__AccessKey=your-access-key
S3__SecretKey=your-secret-key
S3__Region=garage

Photon__AppId=your-photon-realtime-app-id
Photon__VoiceAppId=your-photon-voice-app-id
Photon__CloudRegion=eu
DorkNet__DefaultClientVersion=december_2020_12_18
DorkNet__SupportedVersions__0=december_2020_12_18
DorkNet__BuildIdToVersionKey__20201210=december_2020_12_18
```

Every public host listed above must route to this single container when
running standalone.

---

## S3 Storage

DorkNet uses S3-compatible storage for room blobs, profile images, camera
photos, and CDN/image pipeline backing data. Create these buckets before
production cutover:

```text
profile-images
camera-photos
room-blobs
```

Garage-style endpoints usually use:

```env
S3__Endpoint=https://your-garage-or-s3-api
S3__Region=garage
S3__AccessKey=...
S3__SecretKey=...
```

Cloudflare R2 uses the S3 API endpoint, not a public bucket URL:

```env
S3__Endpoint=https://<cloudflare-account-id>.r2.cloudflarestorage.com
S3__Region=auto
S3__AccessKey=<r2 access key id>
S3__SecretKey=<r2 secret access key>
S3__MaxErrorRetry=1
S3__TimeoutSeconds=300
```

For the microservices compose files, use the `DORKNET_S3_*`
equivalents; compose maps them into `S3__*` for the service containers.

---

## First-Time Cutover From SQLite

1. Stop the local server so the SQLite source stops accepting writes.
2. Snapshot the SQLite database.
3. Provision Postgres, Redis, and separate S3-compatible storage.
4. Run the migration/import tooling against the new Postgres and S3
   targets.
5. Deploy the microservices compose stack.
6. Verify `/healthz`, `/internal/services/health`, versioncheck, and
   the browser static hosts before sending players to the new host.

The database bootstrap path uses advisory locking so multiple service
containers can start without racing schema/bootstrap work.

Rollback before players write to Postgres is simple: stop the deploy and
return to the SQLite snapshot. After players write to Postgres, rollback
means losing post-cutover writes unless you migrate them back.

---

## Photon Cloud

Use your own Photon AppIds. Rec Room's original Photon AppIds reject
external clients because their dashboard points CustomAuth at the
official backend.

1. Create a Photon Realtime app at <https://dashboard.photonengine.com>.
2. Create a Photon Voice app if voice chat is needed.
3. Set:

```env
Photon__AppId=<your realtime AppId>
Photon__VoiceAppId=<your voice AppId>
Photon__CloudRegion=eu
```

Server-side Photon custom auth exists at `/photon/customauth`, but the
2020 client does not send the needed AuthValues without client patching.
Leave Photon dashboard "Reject if Auth Failed" off unless the client mod
is updated to send `userid` and `LoginLock`.

---

## Future Schema Changes

SQLite dev DBs apply EF migrations. The production Postgres path is still
based on `EnsureCreated()` plus idempotent bootstrap steps. Until that is
changed to `Migrate()` under the same advisory lock, treat production
schema changes as explicit deployment work:

1. Add or update the entity model.
2. Add the migration for SQLite/dev.
3. Add any needed idempotent Postgres bootstrap SQL.
4. Deploy and verify `/healthz` and `/internal/services/health`.

See [`../DorkNet.Server/Data/MIGRATIONS.md`](../DorkNet.Server/Data/MIGRATIONS.md)
for the detailed migration discipline.
