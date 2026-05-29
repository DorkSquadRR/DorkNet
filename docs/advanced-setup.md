# Advanced mode — Docker stack

For hosts running a community server (more than ~10 concurrent players),
doing custom modding work, or contributing to DorkNet itself.

If you just want to play with friends, the [easy-setup](easy-setup.md)
desktop app is what you want.

## What you get

- **server**: the ASP.NET Core backend
- **postgres**: the canonical store (the desktop app uses SQLite; for >10
  concurrent players, Postgres is what you want)
- **redis**: cross-replica presence cache (skip with `REDIS_URL=` empty
  to fall back to in-memory; loses presence on restart but otherwise fine)
- **nginx**: TLS termination + multi-subdomain routing
  (`api.*`, `auth.*`, `accounts.*`, `match.*`, `chat.*`, `cdn.*`, `commerce.*`,
  `img.*`, `leaderboard.*`, `notify.*`)

Behind a proper TLS certificate (Let's Encrypt via Caddy or Cloudflare
Tunnel) this should comfortably run 50+ concurrent players on a $20/mo VPS.

## Prerequisites

- A Linux VPS (Debian 12 / Ubuntu 22.04 / etc.)
- A domain you control with wildcard DNS pointing at the VPS
  (`*.your-server.example.com → 1.2.3.4`)
- Docker + Docker Compose

## Setup

### 1. Pick the branch matching your client build

DorkNet keeps one branch per supported Rec Room build. See
[`../BRANCHES.md`](../BRANCHES.md) for the full chart. Quick map:

| Your Rec Room install | Branch to clone |
|---|---|
| 2020.12.18 (most common DorkNet target) | `december-2020-12-18` |
| 2020.03.10 (or 2020.03.06) | `march-2020-03-10` |

### 2. Clone + configure

```bash
# Replace the branch in --branch with the row that matched above.
git clone --branch december-2020-12-18 https://github.com/DorkSquadRR/DorkNet
cd DorkNet/docker

cp .env.example .env
$EDITOR .env  # fill in the values noted below
```

### Required `.env` values

```env
# Your domain. Server expects to find itself at *.DORKNET_DOMAIN.
DORKNET_DOMAIN=your-server.example.com

# A long random string. Used to sign JWTs.
JWT_SECRET=$(openssl rand -base64 64)

# Postgres password. Random, written once, kept in this file.
POSTGRES_PASSWORD=$(openssl rand -base64 32)

# Photon Cloud AppIds. Free tier at https://dashboard.photonengine.com
PHOTON_APP_ID=your-photon-realtime-appid
PHOTON_VOICE_APP_ID=your-photon-voice-appid  # optional
PHOTON_CLOUD_REGION=eu  # eu, us, asia, jp, sa, kr, in, au, etc.

# Steam Web API key — only needed if you want Steam logins to work.
# Free at https://steamcommunity.com/dev/apikey
STEAM_API_KEY=
```

### TLS

The bundled nginx config terminates TLS but expects certificates to exist
on the host. You have two reasonable paths:

1. **Caddy in front** (recommended for new hosts) — replace the nginx
   service with a Caddyfile; Caddy auto-fetches Let's Encrypt certs.
2. **Cloudflare Tunnel** — DNS your wildcard at Cloudflare, run
   `cloudflared` as another container, never expose port 443 to the
   internet. The repo includes a sample `docker-compose.cloudflared.yml`.

### Run it

```bash
docker compose up -d
docker compose logs -f server
```

First boot does:

1. Schema initialization (`EnsureCreated` from the entity classes)
2. Legacy data upgrades (no-op on a fresh install)
3. Seeds RR-Original rooms + store catalog
4. Applies canonical room overrides (Crescendo, Paintball merge, etc.)
5. Downloads the bundled room thumbnail images

You should see `[boot] migrations complete` within ~3 seconds. Then the
server is ready at `https://api.your-server.example.com/healthz`.

## Patching the client to point here

Same patcher as Easy mode, but you'll run it from the command line. The
CLI installer ships on the version-specific branch (e.g.
`december-2020-12-18`):

```powershell
.\tools\install-melon.ps1 `
  -RecRoomPath "C:\path\to\Recroom_Release_Data" `
  -PhotonAppId "<your-photon-realtime-app-id>" `
  -PhotonVoiceAppId "<your-photon-voice-app-id>"
```

The installer drops the DorkNet MelonLoader mod (`DorkNet.ClientMod`)
into the client's `Mods/` folder and writes its config under
`MelonLoader/UserData/`.

## Admin UI

`https://admin.your-server.example.com` — log in with the first account
you create (it auto-promotes to admin) or with the bootstrap credentials
in your `.env` if you set `BOOTSTRAP_ADMIN_*`.

## Backups

Postgres is the only stateful service.

```bash
docker compose exec postgres pg_dump -U dorknet dorknet > backup.sql
```

`data/images/` on the host is the image blob store. Back that up too.

## Updating

```bash
cd DorkNet
git pull
docker compose pull
docker compose up -d
```

The server runs all idempotent migrations on boot, so version upgrades
are zero-downtime as long as the schema changes are additive.

> ⚠️ `git pull` only fetches updates for **your current branch**.
> DorkNet branches are independent — updates on `december-2020-12-18`
> don't flow to `march-2020-03-10` and vice versa. If you've checked
> out the wrong branch for your client, switching is a fresh
> `git clone --branch ...` (the schema and Photon AppId requirements
> differ).
