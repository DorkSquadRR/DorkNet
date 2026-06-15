# Cloudflare Tunnel

Cloudflare Tunnel is used in two DorkNet workflows:

- Local standalone testing, where cloudflared forwards `*.localhost`
  traffic to a locally running `DorkNet.Server`. The default origin is
  `http://localhost:8080`; use local HTTPS only when you intentionally
  run the standalone server with a wildcard dev certificate.
- Dokploy production microservices, where the compose sidecar forwards
  the apex and wildcard hostnames to `http://gateway:8080`.

Both avoid opening ports directly on the host and let Cloudflare serve a
public TLS certificate at the edge.

## Local Standalone Tunnel

Use this when you are running the standalone server locally and want
friends to reach it through Cloudflare.

- You want a friend to play the patched 2020 Rec Room client against
  your server, but they're not on your LAN and don't want to install
  your local CA.
- You want `https://admin.localhost` / `https://feed.localhost` reachable
  from any browser, not just yours.

## Prerequisites

1. **`localhost` is on a Cloudflare account you control.** The DNS
   nameservers for the domain need to be Cloudflare's. Check at
   <https://dash.cloudflare.com/> — the zone should be Active.
2. **Local standalone server running.** The current quickstart listens
   on `http://localhost:8080`. Older local TLS setups can still run on
   `0.0.0.0:443` with a wildcard certificate that covers `*.localhost`.
3. **Patched client** with `tools/patch-domain.ps1` so the in-game
   URLs resolve to `*.localhost`. (Players using the public deployment
   need to run this on their own client too.)

## One-shot setup

```powershell
PS C:\…\Recnet> .\tools\setup-cloudflared.ps1
```

The script:
1. Installs `cloudflared` via winget if missing.
2. Runs `cloudflared tunnel login` — opens a browser, you pick the
   `localhost` zone, Cloudflare drops `cert.pem` into
   `%USERPROFILE%\.cloudflared\`.
3. Creates a named tunnel called `dorknet` (or reuses one if it
   already exists).
4. Generates `%USERPROFILE%\.cloudflared\config.yml` from the
   `tools/cloudflared-config.yml.template` template, filling in the
   tunnel id and credentials path.
5. Adds DNS CNAMEs for every localhost subdomain DorkNet uses
   (api, auth, accounts, match, notify, ns, admin, feed, …).

## Run the tunnel

Foreground (for testing — Ctrl+C to stop):

```powershell
cloudflared tunnel run dorknet
```

Or install as a Windows service so it survives reboots:

```powershell
cloudflared service install
```

## Dokploy Microservices Sidecar

For Dokploy microservices, use
`docker-compose.microservices.dokploy.yml`. It starts `cloudflared` in
the same Compose network as `gateway`, so the tunnel origin is:

```text
http://gateway:8080
```

Set `CLOUDFLARE_TUNNEL_TOKEN` in Dokploy from the Cloudflare Zero Trust
tunnel token. In the Cloudflare tunnel's public hostnames, add:

```text
yourdomain.com      -> http://gateway:8080
*.yourdomain.com    -> http://gateway:8080
```

Do not add these domains in Dokploy's domain modal for this compose
stack; Cloudflare owns the public hostname routing.

## What Happens At Request Time

Local standalone tunnel:

```
visitor -> https://api.localhost/api/versioncheck/v4
        -> Cloudflare edge
        -> cloudflared tunnel
        -> cloudflared running on your machine
        -> http://localhost:8080  (Host preserved)
        -> DorkNet.Server
```

Dokploy microservices tunnel:

```
visitor -> https://api.yourdomain.com/api/versioncheck/v4
        -> Cloudflare edge
        -> cloudflared sidecar
        -> http://gateway:8080  (Host preserved)
        -> DorkNet.Gateway reverse proxy
        -> dedicated service slice or monolith fallback
```

For default local testing, point the local cloudflared origin at
`http://localhost:8080`. If you intentionally test a local HTTPS origin,
the leg from cloudflared to localhost can use `noTLSVerify: true`
because that certificate is usually from a private dev CA. The
Cloudflare-edge to cloudflared leg is still TLS-encrypted with
Cloudflare's cert. In the Dokploy microservices stack, the tunnel
sidecar uses plain HTTP on the private Compose network and sends
everything to the gateway.

## Tearing down

```powershell
cloudflared service uninstall   # if you ran service install
cloudflared tunnel delete dorknet
```

DNS records left behind in the Cloudflare dashboard need to be
deleted manually if you want them gone.

## Limits / caveats

- **Photon traffic is NOT proxied.** Cloudflare Tunnel is HTTP-only;
  the realtime Photon connection still hits the public Photon Cloud
  directly, with the AppId from your `appsettings.json`. That's fine
  — Photon doesn't go through your server.
- **WebSocket support** is on by default in modern cloudflared, so
  the SignalR notify hub works through the tunnel.
- **Free tier rate limits** — Cloudflare Free is generous for a
  small private server but watch the dashboard if you scale up.
