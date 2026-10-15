# Meta Quest client

DorkNet can be played on a standalone Meta Quest headset using a
sideload-patched Rec Room **2023.03.21** client. The patcher and the full build
and install guide live in [`../DorkNet.Quest/`](../DorkNet.Quest/README.md);
this page is the server operator's view.

## Which build

| | |
| --- | --- |
| Rec Room version | `2023.03.21` (versionName `20230317`, versionCode `2128210`) |
| Package (store) | `com.AgainstGravity.RecRoom`, arm64-v8a, IL2CPP |
| Ships | single APK + a ~1.4 GB OBB |

This build reports appVersion **`20230317`** — identical to the PC
`march-2023-03-21` client. The server hardcodes `"20230317"` in presence /
match / notification responses, so **Quest and PC players interoperate with no
server change**: the version gate (`GET api/versioncheck/v4`), the presence
`appVersion` string, and Photon room versioning all line up. Do **not** patch a
different Quest build (e.g. `20230414`) against this branch — a different
appVersion reads as "version mismatch, can't join".

## How it connects

There is no MelonLoader on Android, so `DorkNet.Quest` does the ClientMod +
Photon-byte-patch jobs with one native library (`libdorknet.so`) injected via a
`DT_NEEDED` entry on `libmain.so`. At runtime it:

- rewrites `<service>.rec.net` → `<service>.<ServerHost>` (for a server on
  `Domain:HostStyle = hyphen`, set `ServerHost = {service}-<env>.<apex>`) (the name server then
  hands out every other service URL — same bootstrap as the PC client);
- injects the operator's Photon app ids and fixed region (`PhotonRegion`; it
  must match the PC clients' `PhotonCloudRegion` or the two never meet);
- accepts the self-hosted TLS chain and image signatures;
- neutralises the integrity callbacks that would otherwise reject a re-signed,
  hook-loaded sideload.

So the **same DNS, TLS, Photon and image-signing setup from
[`deploy.md`](deploy.md) serves Quest** — every `*.<apex>` subdomain the PC
client uses must resolve and be covered by the cert.

## Accounts without a Meta account

If the build is made with a blank `OculusAppId` (the no-Meta-account build), the
client can't produce a real Oculus platform identity, so **every headset sends
the placeholder platform user id `1`**. The server must not treat that as an
identity, or all those players collapse into one shared account and are offered
each other's "remembered logins".

DorkNet handles this server-side in `PlayerService`
(`IsPlaceholderPlatformId`): platform ids `0`, blank and `1` are never stored on
an account, never matched by the cached-login lookup
(`cachedlogin/forplatformid`), and never used to synthesise a device id — those
players are keyed by `deviceId` alone, exactly like the PC Steam-emulator path.
A real Meta platform id (the Meta build) still tags and lists normally.

Thanks to [RecFlare](https://github.com/recflare) for documenting this
pitfall in their Quest client notes.

> Covered by `DorkNet.Server.Tests/QuestPlaceholderPlatformIdTests.cs`. **Never
> add a rule that accepts platform id `1` as a real identity** — the whole
> no-Meta fleet sends the same value.

## Operator checklist

1. DNS + TLS for every `*.<apex>` service subdomain (see `deploy.md`).
2. Your own Photon Realtime/Voice app ids (see `deploy.md` → Photon Cloud).
3. Image signing key configured (`ImageSignatureService`), as for PC.
4. Build with `ServerHost=<apex>`, the Photon ids and region baked in, passing
   `-Obb` so you get a release folder (`DorkNet-<ver>.xapk` plus
   `install.cmd`/`install.sh`). Players install it from your site's
   **Quest** page (`https://<apex>/quest`, Chrome or Edge over USB) or with the
   installer scripts; the app appears as **DorkNet** under Unknown Sources.
   Share the `.xapk` however you like: the page reads it from the player's own
   disk, so your server never hosts game files.

No server component is Quest-specific beyond the placeholder-platform-id
handling above.
