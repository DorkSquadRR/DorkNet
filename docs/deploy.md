# DorkNet — Coolify deployment

This doc covers the production cutover from the local-machine setup
(SQLite + Kestrel direct + mkcert + per-machine cloudflared) to the
horizontally-scaled Coolify deployment (Postgres + Redis + Garage S3 +
2-replica Kestrel behind cloudflared, all on the same Coolify project).

The plan-of-record is `BUILD_PROPER_PLAN.md` at the repo root and the
phased writeup at `~/.claude/plans/pahese-0-1-horizontal-cheeky-hickey.md`.
Anything that contradicts this doc, that doc wins.

---

## Coolify project layout

One Coolify *project* with these services:

| Service              | Image / source                                 | Notes                                                                        |
| -------------------- | ---------------------------------------------- | ---------------------------------------------------------------------------- |
| `dorknet-server`     | Build from this repo's `Dockerfile`            | Replicas: **2** to start. Internal port 8080. No public domain.              |
| `dorknet-postgres`   | Coolify one-click **Postgres**                 | Persistent volume on Coolify's data store.                                   |
| `dorknet-redis`      | Coolify one-click **Redis**                    | Persistent volume optional; used for ephemeral state + SignalR backplane.    |
| `dorknet-garage`     | Coolify one-click **Garage**                   | S3-compatible. Persistent volume (room blobs are 100s of MB).                |
| `dorknet-cloudflared`| Cloudflare's `cloudflared` image, custom config | Reads `tools/cloudflared-config.coolify.yml.template` rendered with a tunnel ID. |

All five sit on the same project network so the server reaches its
dependencies over Coolify's internal Docker DNS.

## Required environment variables on `dorknet-server`

Set these in Coolify's *Environment Variables* tab. Coolify expands
`${...}` references to other services in the same project, so the
connection-string lookups link automatically.

```
ASPNETCORE_ENVIRONMENT=Production

# Database
ConnectionStrings__Default=Host=dorknet-postgres;Port=5432;Database=dorknet;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}

# Redis
ConnectionStrings__Redis=dorknet-redis:6379

# JWT
DORKNET_JWT_SECRET=<openssl rand -base64 64>

# Object storage (Garage). Bucket names are hard-coded in code; only
# the endpoint + credentials need configuring. Objects are organized
# under owner-scoped keys inside each bucket, e.g.
# players/{playerId}/camera, players/{playerId}/profile,
# players/{playerId}/rooms/{roomId}, players/{playerId}/dorm.
S3__Endpoint=http://dorknet-garage:3900
S3__Region=garage
S3__AccessKey=<garage admin key>
S3__SecretKey=<garage admin secret>

# Image CDN signing. The 2020 client verifies Content-Signature on
# image downloads. Use the private half of the key whose public modulus
# the client knows for p1. Store PEM newlines as \n if your env UI is
# single-line, or use DORKNET_IMAGE_SIGNING_PRIVATE_KEY_BASE64.
DORKNET_IMAGE_SIGNING_KEY_ID=p1
DORKNET_IMAGE_SIGNING_PRIVATE_KEY=<PEM RSA private key>

# Photon (copy from local appsettings.json — same AppId across envs)
Photon__AppId=<your-photon-realtime-app-id>
Photon__VoiceAppId=1917ff4a-b7ad-4ad6-9ea2-9642bc174b70
Photon__CloudRegion=us
```

### Cloudflare R2 instead of Garage

DorkNet can use Cloudflare R2 without code changes because R2 speaks the
S3-compatible API. Create these three R2 buckets first:

```
profile-images
camera-photos
room-blobs
```

Then create an R2 API token with Object Read & Write access to both
buckets and set the server env vars:

```
S3__Endpoint=https://<cloudflare-account-id>.r2.cloudflarestorage.com
S3__Region=auto
S3__AccessKey=<r2 access key id>
S3__SecretKey=<r2 secret access key>
S3__MaxErrorRetry=1
S3__TimeoutSeconds=8
```

Do not use an R2 public bucket URL or custom domain for `S3__Endpoint`.
That value must be the S3 API endpoint ending in
`.r2.cloudflarestorage.com`.
DorkNet disables S3 chunked upload encoding in code because R2 rejects
`STREAMING-AWS4-HMAC-SHA256-PAYLOAD` request bodies.

`appsettings.Production.json` already pins `Database:Provider=postgres`
and binds Kestrel to `http://0.0.0.0:8080`, so those don't need to be
set as env vars.

## First-time cutover (stop-the-world)

1. **Drain the local server.** Tell players, take it down. The cutover
   is non-incremental — once the SQLite source is read, no further
   writes can be accepted there.

2. **Snapshot SQLite.** `tools/snapshot-db.ps1` writes a timestamped
   copy. Keep it indefinitely as the pre-cutover baseline.

3. **Deploy the Coolify project** with the four services above
   (Postgres / Redis / Garage / cloudflared) but the server replicas
   set to 0 — we want the database empty and idle while the migrator
   runs.

4. **Run the migrator** from the operator's laptop, pointed at the
   newly-provisioned Postgres + Garage:

   ```pwsh
   dotnet run --project Tools/MigrateSqliteToPostgres -- `
       --sqlite ./snapshots/dorknet-pre-cutover.db `
       --postgres "Host=<coolify-postgres-public-host>;Port=5432;Database=dorknet;Username=...;Password=...;SSL Mode=Require" `
       --reset `
       --upload-blobs `
       --s3-endpoint http://<coolify-garage-public-host>:3900 `
       --s3-bucket room-blobs `
       --s3-access-key ... `
       --s3-secret-key ...
   ```

   The migrator runs the 9-step verification + an optional 10th step
   that bulk-uploads every `RoomDataBlobs.Bytes` row to S3, idempotent
   on re-run. A non-zero exit means **abort**: leave the destination
   in its dirty state, fix the diff, re-run with `--reset` for a clean
   slate.

5. **Scale `dorknet-server` to 2 replicas.** First boot: one replica
   wins the `pg_advisory_xact_lock` and runs `EnsureCreated` (no-op,
   schema already there from the migrator) + the seed steps; the other
   blocks on the lock until the first commits, sees the schema is
   complete, and proceeds.

6. **Verify `/healthz` is 200** on both replicas (Coolify shows this
   in the Container tab) before pointing the tunnel at the service.

7. **Bring up `dorknet-cloudflared`** with the rendered config (see
   `tools/cloudflared-config.coolify.yml.template`). Confirm `dig` /
   `curl` against `https://api.localhost/api/health/v1` returns 200.

8. **Tell players it's back.** Done.

## Notes on multi-replica behaviour

* **Sticky sessions are no longer required.** Auth's
  `OrphanAccountTracker` writes the pending claim to Redis, so a
  player whose two HTTP requests hit different replicas still sees
  the same account-creation state.

* **SignalR groups fan out across replicas.** The Redis backplane is
  registered under the channel prefix `dorknet-signalr`; both replicas
  publish/subscribe to the same channels, so a notification raised on
  replica B reaches a player connected to replica A.

* **Player presence is shared.** `PlayerPresenceService` writes
  `presence:player:{id}` keys to Redis with a 45 s TTL (3× heartbeat).
  Either replica can answer "where is player N?".

* **Game session ids are allocated by Postgres.** `GameSessionService`
  uses a `bigserial` so two replicas can `JoinOrCreate` simultaneously
  without colliding.

* **Private instances live in Postgres.** Cross-replica visibility is
  automatic — both replicas read from the same `PrivateInstances`
  table.

## Rollback

If the cutover goes sideways before step 5 (no replicas running yet),
just delete the Postgres / Redis / Garage volumes in Coolify, set the
server replica count back to 0, and bring the local SQLite server back
up. The pre-cutover snapshot is still untouched.

After step 5, rolling back means **restoring the SQLite snapshot**
(any data players wrote on Postgres post-cutover will be lost). Be
sure the cutover validation in step 6 is convincing before you tell
players it's safe.

## Photon Cloud — own AppId + CustomAuth callback

The hard-coded RecRoom Photon AppId rejects external clients (NameServer
error code 32736 / "No auth request during expected wait time") because
their dashboard has CustomAuth set to "Reject if Auth Failed" pointing
at RecRoom's own backend. Solution: register your own free AppId.

1. Go to https://dashboard.photonengine.com → sign up → **Create a New App**
   → "Photon Realtime" → save. Repeat for "Photon Voice" if you want voice.
2. Copy each **AppId** (UUID) and set in Coolify env vars:
   ```
   Photon__AppId=<your realtime AppId>
   Photon__VoiceAppId=<your voice AppId>
   Photon__CloudRegion=us
   ```
3. Restart the server.

That alone gets multiplayer working — the Photon NameServer accepts the
client because no CustomAuth is required.

### (Optional) wire up server-side single-session enforcement

Server already exposes `https://auth.localhost/photon/customauth` — see
[PhotonCustomAuthController.cs](DorkNet.Server/Controllers/Auth/PhotonCustomAuthController.cs).
It accepts `userid` + `LoginLock` and validates them against
`PlayerPresenceService` (Redis). Enable it in the dashboard:

1. Photon dashboard → your app → **Manage** → **Custom Authentication** tab.
2. **Enable Custom Authentication**.
3. URL: `https://auth.localhost/photon/customauth?userid={userid}&LoginLock={LoginLock}`
4. **Reject if Auth Failed**: **OFF for now** (see caveat below).
5. Save.

**Caveat — current 2020 watch doesn't send AuthValues**. A sweep of
`Cpp2IL_ISIL/IsilDump/Assembly-CSharp/` turns up no caller of
`PhotonNetwork.set_AuthValues` outside Photon's own library code, so
the unmodified client connects without supplying `{userid, LoginLock}`.
With "Reject if Auth Failed" ON, every Photon connect would fail with
NameServer 32736 — same symptom we tried to solve. With it OFF, the
client connects fine, the endpoint exists for future client patching.

### Patching the watch to send AuthValues — `DorkNet.ClientMod/`

The vanilla 2020 watch never sets `PhotonNetwork.AuthValues` (verified
by an exhaustive grep across both `Assembly-CSharp.dll` and the
Photon `Assembly-CSharp-firstpass.dll` library). To make Photon
forward `userid` + `LoginLock` to our `/photon/customauth` endpoint a
Harmony patch on the watch's Photon auth path has to attach the values
in flight. That hook lives in the **MelonLoader IL2CPP mod**
(`DorkNet.ClientMod`); the AuthValues injector itself is currently
parked (see `DorkNet.ClientMod/attic/AuthValuesInjector.cs.attic` and
the notes in `Mod.cs`) since the `/photon/customauth` endpoint runs in
permissive mode.

Install the mod on a tester's machine with:

```pwsh
.\tools\install-melon.ps1 `
    -RecRoomPath "C:\…\RecRoom\Recroom_Release_Data" `
    -PhotonAppId  cb0880d9-…
```

The script unpacks MelonLoader, builds `DorkNet.ClientMod.dll`, and
drops it into the client's `Mods/` folder. On the first run it prints
"launch the game once" so MelonLoader can generate its IL2CPP
assemblies; relaunch the script with `-ResumeBuild` to finish.

Once AuthValues injection is re-enabled in the mod and in production:

1. Flip [`PhotonCustomAuthController`](../DorkNet.Server/Controllers/Auth/PhotonCustomAuthController.cs)
   from permissive-mode (`ResultCode 1` always) back to strict —
   parse `userid` + `LoginLock`, validate against
   `PlayerPresenceService.ValidateLock`, return `ResultCode 3` on
   mismatch.
2. In the Photon dashboard, flip **"Reject if Auth Failed"** ON.
3. Duplicate-account login from a different LoginLock now fails
   Photon auth at the NameServer — never reaches the dorm.

## Future schema changes

PR 4 keeps `EnsureCreated` for the first deploy (the migrator builds
the schema directly, then EnsureCreated is a no-op the server can do
under advisory-lock to be safe on multi-replica boot). Any
**post-cutover** schema change needs a real Postgres migration:

1. `dotnet ef migrations add <Name> --project DorkNet.Server`
2. Replace the `EnsureCreated` call in `Program.cs` with
   `db.Database.Migrate()` *inside* the same advisory-lock block.
3. Deploy. Both replicas race for the lock, the winner runs the
   migration, the loser sees the new history row and skips.

Until that swap happens, treat the schema as frozen.
