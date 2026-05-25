# Branches

DorkNet maintains a long-lived branch per supported Rec Room client
build. Each branch is an independent fork of the server + client patcher
tuned to that build's wire protocol. Pick the one matching your install.

## Active branches

### `december-2020-12-18`

- **Client build:** Rec Room 2020.12.18
- **Status:** active
- **Notes:**
  - Single EF Initial migration; SQLite + Postgres both work out of the
    box.
  - Includes the chat-threads, clubs, playlists, weekly-challenge, and
    room-keys subsystems added late-2020.
  - Server's `Versions/Late2020VersionPlugin` is the active version
    plugin; client header `X-DorkNet-Version: december_2020_12_18`.

### `march-2020-03-10`

- **Client build:** Rec Room 2020.03.10 (also serves 2020.03.06)
- **Status:** active
- **Notes:**
  - Earlier wire shape — no clubs, no playlists, simpler store catalog.
  - Diverges from `december-2020-12-18` by ~170 files; cannot be
    unified with the plugin abstraction alone.

Setup for either is the same — see [docs/advanced-setup.md](docs/advanced-setup.md)
(starts with a "pick your branch" step).

## How to pick

If you don't know which Rec Room build you have, check:

- **Steam**: right-click *Rec Room* → Properties → Local files →
  Browse → look at the `Recroom_Release_Data/StreamingAssets/version.txt`
  if present, or note the install timestamp.
- **Manual install** (the typical DorkNet setup path): the build is in
  the folder name (`RecRoom-2020.12.18`, `RecRoom-2020.03.10`, etc.).
- **Easy launcher**: auto-detects the build and picks the branch.

## Machine-readable manifest

[`versions.json`](versions.json) carries the same information in a
format the Easy launcher and install scripts can consume directly. Add
new versions there too when a new branch lands.

## Adding a new version branch

1. Land the server code on a new orphan branch named `<month>-<build>`
   (e.g. `august-2021-08-04`).
2. Add a row to the active-branches list above.
3. Add the matching entry to `versions.json`.
4. Add a `docs/<month>-setup.md` setup guide.
5. Tag a release on the new branch so the Easy launcher's
   release-fetcher picks it up.

Branches are intentionally long-lived — once a build is supported, its
branch keeps getting patches against the same wire protocol. Don't
rebase across branches; cherry-pick when a fix applies to multiple.
