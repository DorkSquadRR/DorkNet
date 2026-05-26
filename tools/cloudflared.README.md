# Cloudflare Tunnel for `localhost`

Stand up a Cloudflare Tunnel that exposes your local DorkNet server
on `*.localhost` for public testing — without opening port 443 on your
firewall, and using Cloudflare's real Let's-Encrypt-equivalent cert
edge-side so visitors don't need your mkcert CA installed.

## When to use this

- You want a friend to play the patched 2020 Rec Room client against
  your server, but they're not on your LAN and don't want to install
  your local CA.
- You want `https://admin.localhost` / `https://feed.localhost` reachable
  from any browser, not just yours.

## Prerequisites

1. **`localhost` is on a Cloudflare account you control.** The DNS
   nameservers for the domain need to be Cloudflare's. Check at
   <https://dash.cloudflare.com/> — the zone should be Active.
2. **Local server running** on `0.0.0.0:443` with the wildcard cert
   that covers `*.localhost` (re-run `tools/patch-client.ps1` if your
   cert is still the `+1` rec.net-only one — it'll regenerate as
   `+3` covering both domains).
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

## What happens at request time

```
visitor → https://api.localhost/api/versioncheck/v4
        → Cloudflare edge (real LE cert, validated)
        → cloudflared tunnel
        → cloudflared (running on your machine)
        → https://localhost:443  (Host: api.localhost, mkcert cert)
        → DorkNet.Server matches [Host("api.localhost")] → handler runs
```

The leg from cloudflared → localhost has `noTLSVerify: true` because
the local server's cert is from mkcert (private CA only trusted on
your machine). The Cloudflare-edge → cloudflared leg is still
TLS-encrypted with their cert, so this isn't a security weakening
— it's just acknowledging that "127.0.0.1 with self-trusted cert" is
where the trust chain ends.

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
