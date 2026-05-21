# Joining someone's DorkNet server

> Built for players whose friend already runs a server and gave them a
> code. If you want to *host* instead, see [easy-setup.md](easy-setup.md).

## What you'll need

- A Windows PC
- A copy of **Rec Room 2020.03.10** (your own — see
  [Getting the 2020 client](easy-setup.md#getting-the-2020-client))
- The join code your friend gave you (looks like `dorknet://join?...`)
- About 2 minutes

## Step 1 — Download DorkNet

Grab `dorknet.exe` from the [latest release](https://github.com/YOUR_GH_USERNAME/dorknet/releases/latest).

## Step 2 — First launch

On the welcome screen, click **🎮 Join a server**.

![first-run welcome screen — Join selected](images/first-run-welcome-join.png)

## Step 3 — Paste the join code

Your friend's code goes in the big text box. DorkNet decodes it and shows
you what you're about to connect to so you can sanity-check:

![join code preview](images/join-preview.png)

Make sure the server name matches what your friend told you. Codes from
strangers can point anywhere — only paste codes from people you trust.

## Step 4 — Patch your Rec Room install

1. Click **Browse for Rec Room install...** — point at the folder with
   `Recroom_Release.exe`.
2. Click **Patch & launch**.

DorkNet patches the game (one-time), then launches it pointed at your
friend's server. You'll see the normal Rec Room login screen, log in with
the account you made on your friend's server, and you're in.

---

## I already patched for a different server. Can I switch?

Yes — DorkNet's patcher is reversible. Click **Repair / unpatch** in the
Patch tab to undo, then re-patch with a new join code. Or just re-run
**Patch & launch** with the new code; the patcher detects an existing
patch and updates the destination.

## My account doesn't exist on the new server

Each DorkNet server runs its own account database. You'll need to create
a new account the first time you join a server. Your friend can sign you
up via their admin panel, or you can use the in-game "Create account"
flow if the server has signups enabled.

## Voice chat doesn't work

Voice routes through Photon Voice, which requires a separate Photon Voice
AppId. If your friend hasn't set one up, voice chat is text-only for now.

## More troubleshooting

See [troubleshooting.md](troubleshooting.md).
