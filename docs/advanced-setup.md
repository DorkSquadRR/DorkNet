# Advanced setup

Use this path if you are running a community server, deploying to a VPS,
testing the server from source, or working on DorkNet itself.

The important bit: `main` is not the server branch. It only has the
launcher, the CLI, `versions.json`, and these shared docs. The actual
ASP.NET server, admin UI, client mod, Dockerfile, and deploy scripts live
on the version branches.

## Pick the right branch

Use the branch that matches the Rec Room build your players will run.

| Rec Room install | Branch |
|---|---|
| 2020.12.18 | `december-2020-12-18` |
| 2020.03.10 or 2020.03.06 | `march-2020-03-10` |

The same list is in [`../BRANCHES.md`](../BRANCHES.md) and
[`../versions.json`](../versions.json). If the client build is not in
that list, DorkNet will not know how to talk to it.

## Clone the server branch

Example for December:

```bash
git clone --branch december-2020-12-18 https://github.com/DorkSquadRR/DorkNet
cd DorkNet
```

Example for March:

```bash
git clone --branch march-2020-03-10 https://github.com/DorkSquadRR/DorkNet
cd DorkNet
```

From that checkout, read the branch's own `README.md` first. If the
branch has `docs/deploy.md`, use that as the production deploy guide.
Those files are more accurate than any generic command copied from
`main`, because each branch is laid out a little differently.

## What you usually need

For a real hosted server, plan on:

- a domain or tunnel that can route the Rec Room service subdomains,
- a Photon Realtime AppId,
- a Photon Voice AppId if you want in-game voice,
- persistent storage for the server database,
- object/blob storage if the branch's deploy guide calls for it,
- the matching 2020 Rec Room client on each player machine.

For Photon setup, use [`photon-setup.md`](photon-setup.md).

## Running from source

On a version branch, the usual local loop is:

```bash
dotnet run --project DorkNet.Server
```

Then follow that branch's README for the exact environment variables,
database settings, and client patching steps.

For admin UI work on a version branch:

```bash
cd DorkNet.Server/admin-ui
npm install
npm run dev
```

For public site work, use the same pattern under `DorkNet.Server/site`.

## Docker and production deploys

There is no top-level `docker/` folder on the active branches.
Start from the branch README and deploy guide instead.

The version branches contain the current Dockerfile and deploy docs.
December has the newer microservices/Dokploy path in `docs/deploy.md`.
March is the older single-server branch. If you are unsure which one you
are on, run:

```bash
git branch --show-current
```

## Updating

Branches are independent. Pulling `main` updates the launcher docs and
manifest, not the server code you are running.

To update a server checkout:

```bash
git checkout december-2020-12-18   # or march-2020-03-10
git pull
```

Then follow that branch's deploy guide for rebuild/restart steps.

## Backups

Back up the database and any blob/image storage before you upgrade. The
exact paths depend on the branch and deploy mode, so use the branch's
own deploy guide rather than guessing from this page.

For the launcher/easy-mode database, the local SQLite file lives under
the DorkNet app data folder shown in the launcher's Settings view.
