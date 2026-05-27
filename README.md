# DorkNet


> ⚠️ **Not affiliated with Rec Room Inc. or Against Gravity.**
> See [DISCLAIMER.md](DISCLAIMER.md). DorkNet ships no Rec Room game assets
> or modified binaries — you supply your own legally-acquired Rec Room
> install.

---

## Pick your client version

DorkNet maintains a separate branch for each Rec Room client build it
supports. The server, client patcher, and seed data on each branch are
tuned to that build's wire protocol.

| Client build | Branch | Status |
|---|---|---|
| **Rec Room 2020.12.18** (December) | [`december-2020-12-18`](../../tree/december-2020-12-18) | Active |
| **Rec Room 2020.03.10** (March) | [`march-2020-03-10`](../../tree/march-2020-03-10) | Active |

See [BRANCHES.md](BRANCHES.md) for the full per-branch breakdown
(supported features, schema notes, plugin compatibility). Programs that
need the mapping in machine-readable form (the Easy launcher, install
scripts) read it from [versions.json](versions.json).

This `main` branch is **docs-only** — it carries the project README, the
manifest the launcher reads, the per-version setup guides, and the Easy
app source. All actual server / patcher code lives on the per-version
branches above.

---

## Ways to run it

### Windows GUI — `dorknet` launcher

One download, two flavours: an **installer** (Start menu shortcut +
rolling auto-updates) or a **portable `dorknet.exe`** (drop anywhere).
Pick a Rec Room build on first launch, host or join.

- **Host a server** — runs a local server + Tunnelto tunnel for the
  client version you pick, patches your Rec Room install, and gives
  you a shareable join code.
- **Join a server** — paste the code, point at your Rec Room install,
  hit play. The launcher fetches the matching patcher.

→ **[Easy setup guide](docs/easy-setup.md)** — screenshots, ~5 minutes.

[Download the latest launcher →](https://github.com/DorkSquadRR/DorkNet/releases/latest)

### Linux / macOS host — `dorknet-server` CLI

Headless host-side CLI for Linux (x64/arm64), macOS (Intel/Apple
Silicon), and Windows. Same server + tunnel pipeline as the GUI,
suitable for a VPS or a permanent home server. Joiners still use the
Windows launcher to apply the patch to their own Rec Room install.

```sh
dorknet-server --photon-id YOUR-APPID --name "Sunday games"
```

→ **[Server CLI guide](docs/server-cli.md)** — install, systemd, launchd.

### Advanced — Docker / source

For community-server hosts, modders, and contributors. Pick the branch
matching your client build, clone, run.

```bash
# Example: hosting for December 2020.12.18 clients
git clone --branch december-2020-12-18 https://github.com/DorkSquadRR/DorkNet
cd DorkNet/docker
cp .env.example .env  # fill in 2-3 values
docker compose up -d
```

→ **[Advanced setup guide](docs/advanced-setup.md)**

---

## What works / what doesn't

Feature set varies per branch. The table below is the union — see each
branch's `BRANCHES.md` row for the exact deltas.

| Feature                          | Status |
| -------------------------------- | ------ |
| Account creation + login         | ✅     |
| Dorms (per-player save)          | ✅     |
| Rec Room Originals (RecCenter, Paintball, Stunt Runner, etc.) | ✅ |
| Maker Pen (build + save rooms)   | ✅     |
| Photon Cloud matchmaking         | ✅     |
| Room chat                        | ✅     |
| DM / group chat                  | ✅     |
| Friend lists + invites           | ✅     |
| Leaderboards                     | ✅     |
| Player profiles + cheer badges   | ✅     |
| Store catalog + gifting          | ✅     |
| Game invites                     | ✅     |
| Image upload (polaroids, room thumbnails) | ✅ |
| VR (Quest/Index/etc.)            | ⚠️ Photon-dependent; works in dev, untested at scale |
| Mobile / Junior accounts         | ⚠️ Partial — Junior chat-restrictions disabled by default in Easy mode |
| Voice chat                       | ❌ Routes through Photon Voice; needs separate Photon Voice AppId |
| Cross-server federation          | ❌ Out of scope |

---

## License & legal

- **Code**: [AGPL-3.0](LICENSE). Hosted forks must publish their source.
- **"Rec Room"** is a trademark of Rec Room Inc. See [DISCLAIMER.md](DISCLAIMER.md).
- DorkNet is a clean-room reimplementation of the 2020 backend protocol
  for educational and personal use.

---

## Docs

- [Branch chart](BRANCHES.md) — which branch matches your client
- [Easy setup](docs/easy-setup.md) — Windows GUI launcher
- [Server CLI](docs/server-cli.md) — Linux / macOS / Windows headless
- [Advanced setup](docs/advanced-setup.md) — Docker / VPS path
- [Joining a server](docs/joining-a-server.md) — for players
- [Photon setup](docs/photon-setup.md) — getting your AppIds
- [Architecture](docs/architecture.md) — how the code is laid out
- [FAQ](docs/faq.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Contributing](CONTRIBUTING.md)
