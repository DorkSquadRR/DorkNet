# DorkNet Easy App

The unified desktop app for hosts and guests. One binary, two modes.

## Project layout

```
easy-app/
├── Shell/        PhotinoNET window host, mode router, settings store,
│                 auto-updater, system tray, log viewer.
├── HostMode/     Tunnel manager (cloudflared wrapper), server lifecycle
│                 (start/stop the in-process ASP.NET Core), share-code
│                 generation.
├── JoinMode/     Join-code parser (dorknet:// URL handler), connection
│                 sanity-check, server reachability ping.
├── Shared/       Patcher library (port of tools/patch-client.ps1 to C#),
│                 ConfigManager, RecRoomInstallFinder (Steam library scan),
│                 ImageSigningKeyManager (auto-generate on first run).
└── Resources/    Icons, admin-ui dist (bundled at build time),
                  cloudflared.exe, MelonLoader/, mod DLLs.
```

## Build flow

`Resources/admin-ui-dist/` is the production build output from
`../admin-ui/` — copied in by a pre-build task so the Photino webview can
serve it as the host-mode admin tab.

```pwsh
# One-shot dev build
pwsh tools/build-easy-app.ps1

# Release artifact
dotnet publish easy-app -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=false
# → bin/Release/net9.0/win-x64/publish/dorknet.exe (~80 MB)
```

## Why PhotinoNET

- The admin UI is already a webview-friendly React SPA. Embedding it in a
  Photino window is essentially free.
- Single .NET project. No Node toolchain in the desktop build, no Electron.
- Bundled binary ~50 MB before the runtime, ~80 MB with self-contained
  .NET. Acceptable for one-time downloads.

## Why not Electron / Tauri / Avalonia

- **Electron**: doubles the binary size, adds Node into the supply chain.
- **Tauri**: Rust dependency we don't otherwise need; mixing it with the
  existing .NET server complicates the build matrix.
- **Avalonia**: native, beautiful, but means re-implementing the admin
  UI a second time. The point of unified Host mode is *reuse* the admin
  SPA verbatim.

## Modes — first-run decision

`Shared/ConfigManager` writes `%APPDATA%\DorkNet\mode.json`:

```json
{ "mode": "host" }
```

`Shell` reads this on startup. Missing or `null` → first-run wizard.
Settings → "Switch mode" sets `mode` to the other value, restarts the
process clean (Photino re-init).

## Host mode lifecycle

1. User clicks Start server.
2. `HostMode.ServerLauncher` boots the ASP.NET Core host in-process,
   wired to a SQLite DB at `%APPDATA%\DorkNet\dorknet.db`.
3. `HostMode.TunnelManager` spawns `cloudflared tunnel --url
   http://localhost:5000`, reads the public URL off stderr.
4. UI surfaces the URL + a "Copy join code" button. Join code is
   base64(`{host, photonAppId, photonRegion, name}`).

## Join mode lifecycle

1. User pastes a join code (or clicks a `dorknet://` URL — the installer
   registers the protocol handler).
2. `JoinMode.CodeParser` decodes, ping-checks the host, shows a
   "Connecting to X" preview.
3. User points at their Rec Room install (auto-detected via
   `RecRoomInstallFinder` if Steam owns it).
4. `Shared.Patcher` patches `global-metadata.dat`, drops MelonLoader,
   writes `dorknet-clientmod.json` with the decoded host + Photon AppId.
5. UI offers a "Launch Rec Room" button — runs `Recroom_Release.exe`
   directly, no Steam intermediary needed.

## Auto-updates

[Velopack](https://velopack.io) is the path of least resistance for .NET
desktop. Drop-in: declare a GitHub Release feed, ship updates as deltas.

## Testing

QA matrix to maintain:
- Fresh Windows 10 VM, no Rec Room → install Steam → buy / depot-download
  the 2020 build → DorkNet host mode → patch + start + tunnel → connect
  from a second VM via Join mode. End-to-end smoke test.
- Mac M1 + Linux: deferred. PhotinoNET supports them but we need separate
  cloudflared binaries and a packaging step per OS.

## Open questions

- [ ] Code-signing cert. SmartScreen warning is a major adoption friction.
  EV cert is ~$200/yr; standard cert is cheaper but takes longer to build
  reputation.
- [ ] Crash reporting. Sentry has a generous free tier and a clean .NET SDK.
- [ ] Telemetry — opt-in? Off by default? Right now: no plans, but
  document the position so contributors know.
