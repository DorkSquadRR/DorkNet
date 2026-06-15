# DorkNet Deployment

This branch has two deployable shapes:

- **Production/full API:** build the root `Dockerfile`, which runs
  `DorkNet.Server`. This is still the runtime that serves every public
  Rec Room-compatible endpoint.
- **Microservices:** deploy `docker-compose.microservices.yml`
  for local testing or `docker-compose.microservices.dokploy.yml` on
  Dokploy. Both start `DorkNet.Gateway`, the dedicated service slices,
  the `DorkNet.Server` monolith fallback, Postgres, and Redis. The
  gateway routes public traffic to owned service slices and sends
  unknown route families to the fallback.

Object storage is always a **separate S3-compatible instance**. The
microservices compose files do not start MinIO/Garage.

---

## Dokploy: full server

Use this for a real server today.

Create a Dokploy **Application**:

| Field | Value |
|---|---|
| Source | This GitHub repo / branch |
| Build type | Dockerfile |
| Dockerfile path | `Dockerfile` |
| Context | `.` |
| Container port | `8080` |

Set application environment variables:

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
```

If Postgres and Redis are Dokploy-managed services in the same project,
use the internal service hostnames Dokploy gives you. For a Compose-style
network the values usually look like:

```env
ConnectionStrings__Default=Host=postgres;Port=5432;Database=dorknet;Username=dorknet;Password=dorknet_dev
ConnectionStrings__Redis=redis://redis:6379
```

### Domains

Point DNS at the Dokploy server, then add domains to the same
application. Every host routes to container port `8080`.

Add these hosts for full client compatibility:

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

All of these point to the same `DorkNet.Server` application/container
port today. `admin` and `feed` serve static hosts; the rest are the
service URLs returned to the client by `ConfigService`.

---

## Dokploy: microservices

Use this to run the gateway/service-host layout.

Create a Dokploy **Compose** service from one of these files:

| Compose file | Use |
|---|---|
| `docker-compose.microservices.yml` | Local/dev-style compose |
| `docker-compose.microservices.dokploy.yml` | Dokploy compose with a Cloudflare Tunnel sidecar |

Both compose files provide:

| Service | Purpose |
|---|---|
| `gateway` | Edge reverse proxy; service map, route table, and service health probes |
| `identity` | Auth, accounts, and platform-login route slice |
| `rooms` | Rooms, room keys, playlists, matchmaking, and discovery route slice |
| `notify` | Notify, messages/chat, SignalR, and notification route slice |
| `content` | CDN paths, uploads, images, photos, room blobs, and storage route slice |
| `social` | Clubs, groups, announcements, player events, subscriptions route slice |
| `commerce` | Catalog, storefronts, econ, inventory, inventions route slice |
| `platform` | Service directory, config, version checks, geo, strings, and platform route slice |
| `moderation` | Bug reporting, player reporting, sanitize, admin API, and testcase route slice |
| `web` | Apex/www/admin/feed static hosts and site API route slice |
| `monolith` | Fallback full server for unknown route families not assigned to a service yet |
| `postgres` | Internal Postgres for the service network |
| `redis` | Internal Redis for ephemeral state and fan-out |
| `cloudflared` | Cloudflare Tunnel sidecar; Dokploy file only |

### Cloudflare Tunnel domains

Use `docker-compose.microservices.dokploy.yml` with Cloudflare Tunnel.
Do not add Dokploy domain rows for this compose stack; public traffic
enters through the `cloudflared` sidecar instead of Dokploy's Traefik
router.

Set these Dokploy Compose environment variables:

```env
DORKNET_DOMAIN=yourdomain.com
DORKNET_JWT_SECRET=replace-with-at-least-64-random-characters
POSTGRES_PASSWORD=replace-with-a-random-db-password
CLOUDFLARE_TUNNEL_TOKEN=<token from Cloudflare Zero Trust tunnel>
```

By default the service containers connect to the bundled compose
Postgres service:

```env
Host=postgres;Port=5432;Database=dorknet;Username=dorknet;Password=${POSTGRES_PASSWORD}
```

To use a Dokploy-managed or external Postgres instead, set a full
connection string and the compose services will use it instead of the
bundled `postgres` hostname:

```env
DORKNET_POSTGRES_CONNECTION_STRING=Host=<postgres-host>;Port=5432;Database=dorknet;Username=<user>;Password=<password>
```

`docker-compose.microservices.dokploy.yml` attaches every backend
service that opens database connections to both the stack's default
network and Dokploy's external `dokploy-network`. Use the managed
Postgres service's real internal Docker/Dokploy hostname in
`DORKNET_POSTGRES_CONNECTION_STRING`; inside a DorkNet service container,
`getent hosts <postgres-host>` should return a Docker IP, not
`127.0.0.1`.

For hosted Postgres that requires TLS, include the Npgsql SSL options:

```env
DORKNET_POSTGRES_CONNECTION_STRING=Host=<postgres-host>;Port=5432;Database=dorknet;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

If you use the bundled compose Postgres, `POSTGRES_PASSWORD` must match
the password that initialized the existing Postgres volume. Changing
`POSTGRES_PASSWORD` later does not change the already-created `dorknet`
database user; either restore the original password, reset it inside
Postgres, or recreate the Postgres volume.

In Cloudflare Zero Trust, configure the tunnel with two public hostnames:

```text
yourdomain.com      -> http://gateway:8080
*.yourdomain.com    -> http://gateway:8080
```

The wildcard covers `api`, `auth`, `rooms`, `notify`, `cdn`, `clubs`,
`commerce`, and the other client-facing subdomains. The apex rule is
separate because wildcard DNS does not cover the root domain. Let
Cloudflare create the tunnel DNS records, or create CNAME records to the
tunnel target Cloudflare gives you.

Every public DorkNet domain listed above should point to the gateway.
The gateway preserves the original Host header so service code still sees
`auth.yourdomain.com`, `rooms.yourdomain.com`, and the other client
hosts.

Set these Compose environment variables in Dokploy:

```env
DORKNET_S3_ENDPOINT=https://your-s3-api-endpoint
DORKNET_S3_ACCESS_KEY=your-access-key
DORKNET_S3_SECRET_KEY=your-secret-key
DORKNET_S3_REGION=garage
```

The compose services already refer to Postgres as `postgres` and Redis as
`redis` on the internal Compose network. Do not add an `object-storage`
service unless you intentionally want a local dev-only S3 emulator.

Useful smoke checks after deploy:

```bash
curl https://api.yourdomain.com/healthz
curl https://api.yourdomain.com/internal/services
curl https://api.yourdomain.com/internal/services/health
curl https://api.yourdomain.com/internal/routes
```

`/internal/routes` shows which host/path patterns route to each service
slice or the monolith fallback. `/internal/services/health` reports
gateway-visible health for every backend service.

---

## S3 storage

DorkNet uses S3-compatible storage for room blobs, profile images,
camera photos, and CDN/image pipeline backing data. Create these buckets
before production cutover:

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

For the microservices compose file, use the `DORKNET_S3_*` equivalents;
Compose maps them into `S3__*` for the service containers.

---

## First-time cutover from SQLite

1. Stop the local server so the SQLite source stops accepting writes.
2. Snapshot the SQLite database.
3. Provision Postgres, Redis, and the separate S3-compatible storage.
4. Run the migration/import tooling against the new Postgres and S3
   targets.
5. Deploy the full `DorkNet.Server` application.
6. Verify `/healthz` returns 200 before sending players to the new host.

The server uses an advisory-lock guarded startup path for Postgres so
multiple replicas can start without racing schema/bootstrap work.

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

## Future schema changes

SQLite dev DBs apply EF migrations. The production Postgres path is still
based on `EnsureCreated()` plus idempotent bootstrap steps. Until that is
changed to `Migrate()` under the same advisory lock, treat production
schema changes as explicit deployment work:

1. Add or update the entity model.
2. Add the migration for SQLite/dev.
3. Add any needed idempotent Postgres bootstrap SQL.
4. Deploy and verify `/healthz`.

See [`../DorkNet.Server/Data/MIGRATIONS.md`](../DorkNet.Server/Data/MIGRATIONS.md)
for the detailed migration discipline.
