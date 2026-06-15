# Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  Supported 2020 Rec Room client (patched)                         │
│  ┌────────────────────┐    ┌──────────────────────────────────┐  │
│  │  IL2CPP game code  │    │  MelonLoader + client-mod        │  │
│  │  • set_BaseUri     │←───│  • rewrites *.rec.net hosts      │  │
│  │  • Photon client   │    │  • injects Photon AppId          │  │
│  │  • BouncyCastle TLS│    │  • bypasses TLS trust check      │  │
│  └─────────┬──────────┘    └──────────────────────────────────┘  │
└────────────┼─────────────────────────────────────────────────────┘
             │ HTTPS to *.your-server.example.com
             ▼
┌──────────────────────────────────────────────────────────────────┐
│  DorkNet server from the matching version branch (ASP.NET Core)   │
│                                                                  │
│  Subdomain-multiplexed routing:                                  │
│    api.*       → REST endpoints (rooms, players, store, etc.)    │
│    auth.*      → OAuth-style /connect/token, /photon/customauth  │
│    accounts.*  → /account/bulk, /account/me, /account/{id}/bio   │
│    match.*     → /goto/room/*, /player/heartbeat, /roominstance/*│
│    chat.*      → /thread, /thread/{id}/message                   │
│    cdn.*       → static asset serving (room blobs, images)       │
│    notify.*    → SignalR hub for push notifications              │
│    img.*       → signed image fetch + on-the-fly resize          │
│    leaderboard.* → /leaderboard/SetStat, GetPlayerRank, etc.     │
│    admin.*     → React SPA + /api/admin/v1/*                     │
│                                                                  │
│  Storage:                                                        │
│    • Postgres (advanced mode) / SQLite (easy mode)               │
│    • Redis presence cache (advanced) / in-memory (easy)          │
│    • data/images/ — room thumbnails + player polaroids           │
└──────────────┬───────────────────────────────────────────────────┘
               │
               │   The server does NOT host Photon. Players' clients
               │   connect to Photon Cloud directly via the AppId
               │   the server hands them.
               ▼
┌──────────────────────────────────────────────────────────────────┐
│  Photon Cloud (external — Exit Games)                            │
│  Each Rec Room instance = one Photon room. Photon handles        │
│  realtime sync between clients in the same instance.             │
│  Server only touches Photon to authenticate connections via      │
│  /photon/customauth.                                             │
└──────────────────────────────────────────────────────────────────┘
```

## Why subdomains?

The 2020 Rec Room client routes every request through a hardcoded
service map (`RecNet.Core.SendRequest(method, Service.Match, …)`) that
picks a host per service. We mirror the official `*.rec.net` layout so
the watch's URI synthesis works unmodified after a single root-host
rewrite by the client mod.

## Why SignalR?

The 2020 client expects WebSocket push notifications from a long-poll-or-
WebSocket "Notifications" service. SignalR's transport negotiation
matches what the client expects after a small DTO-shape wrap; see
`Services/NotificationService.cs`.

## Why a separate Photon Cloud?

The 2020 build is tightly coupled to Photon Realtime SDK 4 — replacing it
would mean shipping a custom game client. Using Photon Cloud as-is means
clients talk peer-to-peer through Photon's relay; our server only
witnesses the auth handshake. That's also why voice chat needs a Photon
Voice AppId — voice runs through Photon Voice (a separate service from
Photon Realtime).
