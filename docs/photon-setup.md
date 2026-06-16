# Getting your Photon AppIds

DorkNet uses Photon Cloud for in-room realtime sync (positions, voice,
matchmaking). It's free for small private servers and the launcher
never ships one — you bring your own. This guide walks through getting
one if the in-app walkthrough doesn't work for you (e.g. you're using
the Linux/macOS [`dorknet-server`](server-cli.md) CLI).

You only need to do this **once**. The AppIds are saved on your host
machine and embedded into the join code so your friends never see them.

## What you're getting

- **Realtime AppId** — required. Powers position sync, room state,
  RPCs. Free Photon plan covers 20 concurrent users which is enough
  for a friends-only server.
- **Voice AppId** — optional. Powers in-game voice chat. Skip it if
  your group uses Discord on the side.

Both are GUIDs that look like `abc12345-6789-...`. Treat them like
loose credentials: they're not secret-secret (the join code carries
them to joiners), but don't paste them in public chat.

---

## Step 1 — Sign up

Go to **[dashboard.photonengine.com](https://dashboard.photonengine.com/)**
and click "Sign up". Email + password; the free tier doesn't ask for a
card.

> If you already have an account from another project, log in instead.
> Multiple DorkNet servers can share the same Photon account, but each
> server needs its own AppId pair so concurrent player counts don't
> collide.

## Step 2 — Create a Realtime app

After login, click **CREATE A NEW APP** on the dashboard. The form asks
for:

- **Photon Type** — pick **Photon SDK**. Not Fusion, not Quantum, not
  Bolt.
- **Name** — anything you'll recognise later ("DorkNet Realtime",
  "Sunday Server", etc.). Joiners don't see this.
- **Description** — optional.

Click **CREATE**.

## Step 3 — Copy the Realtime AppId

You land back on the dashboard with your new app at the top. The AppId
is the long string under the app name — click it to copy.

That's your **Realtime AppId**. Paste it into:

- the launcher's first-run wizard step 4, **REALTIME APPID** field, OR
- the launcher's Host view → Photon Cloud panel → **REALTIME APPID**, OR
- the CLI: `dorknet-server --photon-id <paste-here>`

## Step 4 — (Optional) Create a Voice app

If you want in-game voice chat, repeat Step 2 with **Photon Voice**
selected instead of Realtime. Same flow.

Copy the Voice AppId from the dashboard the same way as Step 3 and
paste it into:

- the wizard's **VOICE APPID** field, OR
- the launcher's Host view → Photon Cloud panel → **VOICE APPID · OPTIONAL**, OR
- the CLI: `--voice-id <paste-here>`

## Step 5 — Pick a region

Both apps default to a global routing layer, but for the lowest latency
you want all your players hitting the same Photon datacenter.

In the launcher, **CLOUD REGION** sits below the AppId fields:

| Code | Region |
| --- | --- |
| `us` | US East (default) |
| `eu` | EU Amsterdam |
| `asia` | Asia Singapore |
| `jp` | Japan Tokyo |
| `sa` | South America São Paulo |
| `kr` | Korea Seoul |
| `in` | India Chennai |
| `au` | Australia Sydney |

Pick the region closest to most of your players. The CLI flag is
`--region <code>`.

> **All joiners must use the same region as the host.** This is baked
> into the join code, so as long as everyone uses the same code,
> they'll match. If you change the region later, regenerate the code.

## Step 6 — Paste into DorkNet

That's it. In the launcher:

1. Open the Host view.
2. Paste **Realtime AppId** under Photon Cloud.
3. Paste **Voice AppId** (if you made one).
4. Pick the **Cloud Region**.
5. Click **START HOSTING** when ready.

For the CLI:

```
dorknet-server \
  --photon-id <realtime-appid> \
  --voice-id  <voice-appid>      # optional \
  --region    eu                 # whatever region you picked \
  --name      "Sunday games"
```

---

## What's safe to share

| Value | Share with friends? | Why |
| --- | --- | --- |
| Realtime AppId | Yes (via join code) | The launcher embeds it into the join code so joiners can connect. |
| Voice AppId | Yes (via join code) | Same. |
| Photon dashboard login | **No** | Anyone with your login can delete your apps + see usage logs. |
| Photon billing details | **No** | Self-explanatory. |

If you accidentally publish an AppId somewhere public, you can rotate
it: delete the app in the Photon dashboard, create a new one, paste
the new AppId into DorkNet. Existing join codes stop working; share
new ones.

## Free tier limits

At time of writing the Photon free tier covers:

- 20 concurrent users (CCU) — i.e. 20 people simultaneously connected
  through your Realtime app
- 60 messages/second/user
- No card required, no expiry

For a friends-and-family server this is enough. You'll see a warning on
the Photon dashboard if you start hitting limits; the paid plans bump
the CCU cap.

## Troubleshooting

### "Photon CustomAuth 401" in the server log

The Realtime AppId on the server doesn't match what got embedded in
your patched client. Either:
- You changed the AppId in the launcher after applying the patch —
  re-share the join code and have joiners re-patch.
- You're running an older patched client from a previous join code —
  re-patch with the current one.

### Joiner can't reach the room (game stuck loading)

Region mismatch. Both sides must use the same region. Confirm in your
launcher's **CLOUD REGION** dropdown; ask your friend to re-patch with
your latest join code.

### Voice chat is silent

You either didn't set a Voice AppId, or you set one but the joiner's
patched client doesn't have it. Re-patch with a fresh join code that
includes both AppIds.
