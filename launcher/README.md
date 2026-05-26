# DorkNet Launcher

Single-download desktop launcher for self-hosting / joining DorkNet
servers. PhotinoNET-wrapped WebView2 with the orchestration logic in
C#.

This README is for **developers** of the launcher. End-users see
[the main README](../README.md) and download a prebuilt `.exe` from
[Releases](https://github.com/DorkSquadRR/DorkNet/releases).

## What it does

- **Host mode:** fetches the matching server binary from GitHub
  Releases for the user's picked Rec Room version, runs it as a
  subprocess, opens a Cloudflare quick-tunnel, patches the user's
  Rec Room install, hands the user a shareable join code.
- **Join mode:** parses the join code, fetches the matching client
  patcher from GitHub Releases, patches the user's Rec Room install
  to point at the host.

## How it's wired

```
launcher/
├── DorkNet.Launcher.csproj    .NET 9 single-file self-contained exe (Photino.NET dep)
├── Program.cs                  PhotinoNET window + message-bridge wiring
├── app.manifest                Windows DPI-awareness manifest
├── Backend/                    Orchestration (C#)
│   ├── AppPaths.cs            %APPDATA%\DorkNet layout
│   ├── AppState.cs            Persisted user choices (mode, paths, Photon AppId)
│   ├── VersionsManifest.cs    Fetches versions.json from main
│   ├── ReleaseDownloader.cs   GitHub Releases API + artifact unpack
│   ├── ServerProcess.cs       Spawns the downloaded server binary
│   ├── ClientPatcher.cs       Invokes install-plugin.ps1 from the unpacked patcher zip
│   ├── Tunnel.cs              Wraps `cloudflared tunnel --url …`
│   ├── RecRoomPicker.cs       Windows folder dialog
│   ├── JoinCode.cs            base64url-encoded {host, version, photonAppId, …}
│   └── MessageBridge.cs       JSON envelope router between JS ↔ C#
└── ui/                         Single-file frontend (no build step)
    ├── index.html
    ├── style.css
    └── app.js
```

Message protocol: both directions use
`{ "type": "command-or-event-name", "payload": { ... } }`. New
commands go in `MessageBridge.HandleAsync`'s switch; new outbound
events use `bridge.SendEvent(name, payload)`.

## Build + run

```pwsh
cd launcher
dotnet run
```

Watch the launcher window open. C# stderr lands in the console;
the WebView2 devtools open with F12 (PhotinoNET passes them through).

## Publish a self-contained .exe

```pwsh
dotnet publish -c Release
# Output: bin\Release\net9.0-windows\win-x64\publish\dorknet.exe (~80 MB)
```

The published exe is what ships in GitHub Releases on `main` (use the
`Easy launcher release` workflow when it exists).

## Dependencies on per-version branches

The launcher fetches release artifacts from GitHub Releases tagged on
the per-version branches. See [RELEASES.md](RELEASES.md) for the
exact naming convention each branch must follow.

## Adding a new command

1. Add a new `case` in `MessageBridge.HandleAsync`.
2. Implement the work in a `Backend/*` class.
3. In `ui/app.js`, call `bridge.send({ type: 'your-command', payload: {...} })`
   from wherever the user triggers it.
4. If the command needs to push events back, call
   `bridge.SendEvent('your-event-name', { ... })` from the handler
   and add a case in `app.js`'s `handleEvent` switch.

Keep the bridge handlers tight — they're routing + state mutation;
long work goes in `Backend/*`.
