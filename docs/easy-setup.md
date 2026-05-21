# Easy mode — host your own server

> Built for non-technical hosts. If you can install Discord, you can do this.

## What you'll need

- A Windows PC (Mac and Linux support coming later)
- A copy of **Rec Room 2020.03.10** — the specific old build DorkNet targets.
  You acquire this yourself; see [Getting the 2020 client](#getting-the-2020-client) below.
- ~5 GB free disk space
- About 10 minutes

## Step 1 — Download DorkNet

Grab `dorknet.exe` from the [latest release](https://github.com/YOUR_GH_USERNAME/dorknet/releases/latest).

> Windows SmartScreen may warn you. The exe is code-signed; click "More info"
> → "Run anyway".

## Step 2 — First launch

When you open DorkNet for the first time, you'll see a welcome screen
asking what you want to do.

![first-run welcome screen](images/first-run-welcome.png)

Click **🏠 Host a server**.

## Step 3 — Patch your Rec Room install

DorkNet needs to redirect Rec Room to talk to your server instead of the
official one. This is a one-time patch.

1. Click the **Patch game** tab.
2. Click **Browse for Rec Room install...** — point it at the folder where
   `Recroom_Release.exe` lives.
3. Click **Patch**. Takes 30 seconds.

![patch tab](images/patch-tab.png)

✅ You're done with patching. You won't need to do this again unless you
reinstall Rec Room.

## Step 4 — Start the server + tunnel

1. Click **Start server** on the Status tab.
2. After ~5 seconds you'll see a public URL like
   `https://abc-def-ghi.trycloudflare.com`. Anyone with that URL + a
   matching join code can connect to you.

![status tab running](images/status-running.png)

## Step 5 — Share the join code with your friends

1. Click the **Share** tab.
2. Click **Copy join code**.
3. Paste it to your friend in Discord / iMessage / wherever.

That's it for hosting. Your friend opens DorkNet, picks "Join a server",
pastes the code, and they're in.

---

## Getting the 2020 client

DorkNet doesn't ship the Rec Room game itself — that's Rec Room Inc.'s
intellectual property. You acquire your own copy through legal means.

Common paths:
- If you bought Rec Room on Steam before 2020, you can use SteamDB depot
  tools to download the old build (manifest `4651244957411961725` for
  `Recroom_Release_Data`).
- Friends who already have a 2020 install can share files with you under
  whatever license terms apply on their end.

DorkNet's authors can't help you obtain the game. Search the web for
"Steam depot 2020.03.10 Rec Room" if you need pointers.

---

## Troubleshooting

See [troubleshooting.md](troubleshooting.md) for common issues.

The most common ones:
- **"This site can't be reached" when joining** — your tunnel URL changed.
  Get the new one from the Status tab and share it again. (Cloudflare
  Quick Tunnels give a new URL each session. See
  [advanced-setup.md](advanced-setup.md) if you want a stable one.)
- **Friend can't connect** — they need to use the same Photon region as
  you. Settings → Photon region. Default is `eu`.
- **Game launches but stays on the loading screen** — your friend isn't
  patched. Make sure they ran the Patch tab in their copy of DorkNet.
