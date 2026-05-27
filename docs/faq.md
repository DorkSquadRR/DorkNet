# FAQ

### Is this legal?

The DorkNet code itself is original and AGPL-licensed. The Rec Room game
client is not ours — you supply your own copy that you've legally
acquired. The patcher runs locally on your machine and never uploads
modified game files anywhere.

DorkNet's authors don't ship Rec Room IP and don't help users obtain it.
See [DISCLAIMER.md](../DISCLAIMER.md) for the full position.

### Will Rec Room Inc. shut this down?

Possibly. Fan-server projects exist in a grey zone. AGPL doesn't shield
from DMCA. We minimize friction by never distributing Rec Room IP, never
interfering with their live servers, and being explicit that this is for
educational / private use only.

### Why the 2020 build specifically? Will newer versions work?

2020.03.10 is the last build before Rec Room shipped server-side
ObscuredInt anti-cheat changes that would require deeper client patching.
It's old enough to be effectively abandonware on Rec Room's roadmap, new
enough to have most of the features people remember.

Newer builds (any Rec Room from late 2020 onward) aren't supported and
won't be without significant additional reverse-engineering. PRs that
add support for newer builds are welcome but expect scope creep.

### Can I run a public server that anyone can join?

Technically yes (just disable signup-disabled in admin settings). Legally
risky — see the C&D answer above. Realistically, "public" private
servers attract attention and shorten the project's lifespan.

We recommend keeping servers small (friends + community), private
(invite-only signups), and not advertised.

### Will mobile / Junior accounts work?

Mobile binaries from 2020 are platform-locked to App Store / Play Store
servers and can't easily be retargeted. Quest standalone is possible in
principle but hasn't been validated.

Junior accounts work but DorkNet patches out the chat-permission gate by
default since on a private server with friends, the chat moderation
plumbing is overkill. Toggle in admin → Server settings if you need it.

### Does this work with Quest VR?

Desktop VR via SteamVR works. Standalone Quest builds aren't easily
patchable (sideloaded APK signing is its own world). Quest Link / Air
Link puts you back in the SteamVR case, which works.

### How many players can one server handle?

- **Easy mode (SQLite, single-instance)**: ~30 concurrent players in
  practice. Photon does the heavy realtime lifting; the bottleneck is the
  server's SignalR fanout + DB writes, both of which SQLite struggles
  with past ~50.
- **Advanced mode (Postgres + Redis + multi-replica)**: tested at 200+
  in dev. Real bottleneck becomes Photon AppId quotas at that point.

### Can I federate multiple DorkNet servers?

Not supported and not planned. Each server has its own account database;
players are server-local. Same as classic Minecraft realms.

### Is there a Discord?

No official one yet. If you start a community, link it from your fork.

### What about voice chat?

Routes through Photon Voice. You'll need a (free) Photon Voice AppId
separate from your Photon Realtime AppId. Set `PHOTON_VOICE_APP_ID` in
the server config; the client mod handles the rest.

### My friend can't connect

Most common causes, in order:
1. **Stale join code.** Localtunnel hands out a fresh `*.loca.lt` URL
   on every host restart. Hit Start hosting, copy the new join code
   from the launcher, send the new code to your friend.
2. **Photon region mismatch.** Both clients must use the same Photon
   region — set in Settings.
3. **Friend didn't patch.** They need to open the Join view in their
   launcher, paste your join code, and click APPLY PATCH before
   launching Rec Room.
4. **Localtunnel interstitial.** First time a joiner's machine hits a
   given `*.loca.lt` URL, Localtunnel may show a one-time "Click to
   Continue" page. Open the URL once in any browser and retry.
4. **Junior account / accessibility settings.** Server settings → relax
   moderation gates if friends are on accounts with parental controls.
