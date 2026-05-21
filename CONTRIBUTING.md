# Contributing to DorkNet

Thanks for your interest. A few ground rules before you open a PR.

## Code style

- **C#**: nullable enabled. Match the file you're editing. No regions, no
  multi-paragraph XML docs, no async-without-await.
- **TypeScript / React**: existing patterns — function components, named
  exports, tailwind classes, no CSS modules. Lint with `npm run lint` from
  `admin-ui/`.
- **No AI-co-author trailers in commit messages.** Use a real attribution
  line or none at all.

## Branches and commits

- `main` is protected; PR from a feature branch.
- Conventional commits welcome but not required. One-line summary should
  describe the *why*, not the *what* — the diff already shows what.

## Scope of contributions we welcome

- Bug fixes against documented behavior
- Feature parity with the 2020 client (anything reverse-engineered cleanly)
- Performance work
- Better docs, screenshots, video walkthroughs
- New translations of the admin UI

## Scope of contributions we don't accept

- **Rec Room game assets, decompiled source, or modified game binaries.**
  We never ship Rec Room IP. See [DISCLAIMER.md](DISCLAIMER.md). PRs that
  add these files will be closed.
- **Cheats, exploits, or features that would harm other public Rec Room
  servers.** This project exists to let people self-host the 2020 backend
  for themselves; it isn't a vehicle for griefing live games.
- **Crypto / NFT / monetization plugins.** Hard no.

## Reporting security issues

Don't open a public issue. Email the maintainer at the address in the repo
metadata, or use GitHub's private vulnerability reporting.

## Setting up a dev environment

See [docs/advanced-setup.md](docs/advanced-setup.md) for the full server
stack. For just hacking on the admin UI:

```bash
cd admin-ui
npm install
npm run dev
```

Points at `http://localhost:5000` by default — start the server first.
