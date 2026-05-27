# Headless host — Linux & macOS

> For hosts running a server from a Linux box, a Mac, or a VPS. Joiners
> on Windows still use the [Windows launcher](easy-setup.md) to apply
> the patch to their copy of Rec Room.

`dorknet-server` is a small command-line tool that does the host side
of what the Windows launcher does: downloads the server build, opens a
[Tunnelto](https://tunnelto.dev) tunnel, runs the server, and prints
a join code. It runs on Linux, macOS, and Windows.

## What you'll need

- A machine running Linux (x64 or arm64), macOS (Intel or Apple
  Silicon), or Windows
- [Tunnelto](https://github.com/agrinman/tunnelto) on PATH (for the
  default mode that exposes your server publicly)
- A Photon Realtime AppId — see [photon-setup.md](photon-setup.md)
- ~500 MB free disk (server binary + SQLite database)

You **do not** need Rec Room installed on the host machine. The server
doesn't run the game; joiners run their own (Windows) copy.

## Install

Grab the matching archive from the
[latest release](https://github.com/DorkSquadRR/DorkNet/releases/latest):

| Platform | Asset |
| --- | --- |
| Linux x64 | `dorknet-server-linux-x64.tar.gz` |
| Linux arm64 | `dorknet-server-linux-arm64.tar.gz` |
| macOS Apple Silicon | `dorknet-server-osx-arm64.tar.gz` |
| macOS Intel | `dorknet-server-osx-x64.tar.gz` |
| Windows x64 | `dorknet-server-win-x64.zip` |

Unpack, then either keep it where it is or move the `dorknet-server`
binary onto your `PATH`.

```sh
# Linux / macOS
curl -L https://github.com/DorkSquadRR/DorkNet/releases/latest/download/dorknet-server-linux-x64.tar.gz | tar xz
chmod +x dorknet-server
./dorknet-server --help
```

```pwsh
# Windows (PowerShell)
Invoke-WebRequest https://github.com/DorkSquadRR/DorkNet/releases/latest/download/dorknet-server-win-x64.zip -OutFile dorknet-server.zip
Expand-Archive dorknet-server.zip
.\dorknet-server\dorknet-server.exe --help
```

### Tunnelto

The CLI shells out to `tunnelto` for the public tunnel. Quickest install:

```sh
# macOS (Homebrew)
brew install tunnelto

# Linux (cargo)
cargo install tunnelto

# Or grab a binary from github.com/agrinman/tunnelto/releases
```

Skip Tunnelto if you're running in `--mode lan` (LAN-only, no public
URL).

## Usage

The minimum invocation is just your Photon AppId:

```sh
dorknet-server --photon-id 12345678-aaaa-bbbb-cccc-1234567890ab
```

The CLI prints progress, then a join code:

```
DorkNet server CLI · v0.1.0
  mode:        Internet
  server:      DorkNet Server
  photon:      ********90ab (us)

[server] downloading…
            54.2%  (27.1 / 50.0 MB)
[server] cached at /home/alex/.local/share/DorkNet/servers/march_2020_03_10
[tunnel] starting tunnelto…
[tunnel] live at https://dorknet-abc123.tunnelto.me (apex=dorknet-abc123.tunnelto.me)
[server] starting…
[server] listening (logs: /home/alex/.local/share/DorkNet/logs/server-…log)

══════════════════════════════════════════════════════
  Server live: DorkNet Server
  Address:     https://dorknet-abc123.tunnelto.me

  Join code (paste this into your friend's launcher):

    eyJob3N0IjoiZG9ya25ldC1hYmMxMjMudHVubmVsdG8ubWUiLCJ2I...

  Ctrl-C to stop.
══════════════════════════════════════════════════════
```

Copy the join code, send it to your friend, they paste it into their
Windows launcher's **Join a friend** view.

Stop the server with `Ctrl-C` — the CLI shuts the server + tunnel
down cleanly.

## Common options

```
--photon-id <guid>     Required. Photon Realtime AppId.
--voice-id <guid>      Optional. Photon Voice AppId (Discord on the side
                       works fine without this).
--region <code>        Photon cloud region — us, eu, asia, jp, sa, kr,
                       in, au. Default: us. All joiners use the same.
--name "<text>"        Server display name in the join code.
                       Default: "DorkNet Server".
--mode <kind>          tunnelto (default)  — public, friends anywhere
                       wildcard            — your own Tunnelto apex
                       lan                 — same WiFi only, no tunnel
--apex <hostname>      Wildcard apex. Required for --mode wildcard.
                       Example: dorknet.example.tunnelto.me
--version <key>        Rec Room version key. Default: march_2020_03_10
                       (the only branch currently supported).
--server-dir <path>    Skip download — use a local build at this path.
                       Useful for dev work on the server itself.
```

Run with `--help` for the canonical list.

## Examples

```sh
# Sunday games server with a custom name, EU region
dorknet-server \
  --photon-id 12345678-aaaa-bbbb-cccc-1234567890ab \
  --name "Sunday games" \
  --region eu

# LAN party — no tunnel, joiners must be on the same WiFi
dorknet-server --photon-id ... --mode lan

# Custom Tunnelto wildcard base
dorknet-server \
  --photon-id ... \
  --mode wildcard \
  --apex dorknet.acme.tunnelto.me

# Voice chat enabled
dorknet-server --photon-id ... --voice-id 87654321-...

# Iterate on a local server build during dev
dorknet-server --photon-id ... \
  --server-dir /home/alex/src/DorkNet/DorkNet.Server/bin/Debug/net9.0
```

## Run as a systemd service (Linux)

Make a service so the server restarts on reboot:

```ini
# /etc/systemd/system/dorknet.service
[Unit]
Description=DorkNet private Rec Room server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=alex
ExecStart=/home/alex/bin/dorknet-server \
  --photon-id 12345678-aaaa-bbbb-cccc-1234567890ab \
  --region eu \
  --name "Sunday games"
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now dorknet
journalctl -fu dorknet           # follow the logs
```

> Heads-up: each new tunnel session gets a fresh `*.tunnelto.me`
> hostname, so the join code rotates on restart. Either re-share
> the new code with your friends, or use `--mode wildcard` with a
> stable apex.

## Run as a launchd agent (macOS)

```xml
<!-- ~/Library/LaunchAgents/com.dorknet.server.plist -->
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
                       "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.dorknet.server</string>
  <key>ProgramArguments</key>
  <array>
    <string>/usr/local/bin/dorknet-server</string>
    <string>--photon-id</string><string>YOUR-APPID</string>
    <string>--region</string><string>us</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>/tmp/dorknet.log</string>
  <key>StandardErrorPath</key><string>/tmp/dorknet.err</string>
</dict>
</plist>
```

```sh
launchctl load ~/Library/LaunchAgents/com.dorknet.server.plist
```

## Files & state

The CLI follows XDG-ish conventions on Linux/macOS and the standard
`%APPDATA%` / `%LOCALAPPDATA%` paths on Windows:

| Path | Contents |
| --- | --- |
| `$XDG_DATA_HOME/DorkNet/` (Linux) <br/> `~/Library/Application Support/DorkNet/` (macOS) <br/> `%APPDATA%\DorkNet\` (Windows) | Server SQLite DB (`dorknet.db`), JWT signing secret |
| same prefix `/servers/<version>/` | Downloaded server binaries (~50 MB each, cached across runs) |
| same prefix `/logs/` | Per-session server stdout / stderr |

The CLI does not read or write the Windows launcher's `state.json` —
it's stateless across runs (all config comes from CLI args).

## Limitations

- **No client patcher.** Joiners need the Windows launcher to patch
  their Rec Room install — there is no patch-from-Linux flow yet
  because Rec Room is a Windows game and the patcher manipulates a
  Windows PE binary.
- **No GUI.** Use the Windows [`dorknet.exe`](easy-setup.md) if you
  want a graphical launcher.
- **No auto-updates.** Pull a new release manually when you want one.
  Watch the GitHub releases feed or run a periodic check in your
  service.

## Troubleshooting

### `tunnelto was not found`

The CLI couldn't find a `tunnelto` binary. Install it (see above) or
drop the binary next to `dorknet-server`.

### `couldn't fetch versions.json`

You're offline or GitHub is blocked. Either fix the connection or
point `DORKNET_LOCAL_MANIFEST` at a local copy of versions.json.

### `No server binary found in …`

The downloaded archive doesn't contain the expected `DorkNet.Server`
file. Likely cause: the release for your platform hasn't been
published yet. Workaround: build the server from source and pass
`--server-dir /path/to/built/server`.

### Same Photon errors as the GUI launcher

Photon issues (CustomAuth 401, region mismatch, voice silent) have
the same fixes as on Windows. See
[photon-setup.md → Troubleshooting](photon-setup.md#troubleshooting).
