# DorkNet

A self-hostable reimplementation of the 2020 Rec Room backend. Bring your own
client; run a private server for yourself and your friends.

> ⚠️ **Not affiliated with Rec Room Inc. or Against Gravity.**
> See [DISCLAIMER.md](DISCLAIMER.md). DorkNet ships no Rec Room game assets or
> modified binaries — you supply your own legally-acquired Rec Room 2020.12.18
> install.

---

## Two ways to run it

### 🎮 Easy mode — desktop app

One download. Double-click. Choose "host" or "join" on first launch.

- **Host a server** for you and your friends — runs a local server + tunnel,
  patches your Rec Room install, and gives you a shareable join code.
- **Join a server** someone shared with you — paste the code, point at your
  Rec Room install, hit play.

→ **[Easy setup guide](docs/easy-setup.md)** — screenshots, takes 5 minutes.

[Download the latest Easy app →](https://github.com/YOUR_GH_USERNAME/dorknet/releases/latest)

### 🛠️ Advanced mode — Docker

For people running a community server, doing custom modding, or contributing
back. Postgres + Redis + the ASP.NET Core server behind an nginx reverse proxy.

```bash
git clone https://github.com/YOUR_GH_USERNAME/dorknet
cd dorknet/docker
cp .env.example .env  # fill in 2-3 values
docker compose up -d
```

→ **[Advanced setup guide](docs/advanced-setup.md)**

---

## What works / what doesn't

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
- DorkNet is a clean-room reimplementation of the 2020 backend protocol for
  educational and personal use.

---

## Docs

- [Easy setup](docs/easy-setup.md)
- [Advanced setup](docs/advanced-setup.md)
- [Joining a server](docs/joining-a-server.md)
- [Architecture](docs/architecture.md)
- [FAQ](docs/faq.md)
- [Contributing](CONTRIBUTING.md)
