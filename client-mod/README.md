# client-mod/

MelonLoader 0.6.x mod that retargets the 2020 Rec Room client at a
DorkNet server. Compiles to `DorkNet.ClientMod.dll`, which the patcher
drops into the user's `Recroom_Release/Mods/` folder.

## What it does

Harmony patches applied at boot:

- **`Uri ctor`** — rewrites every `.rec.net` URL to your configured
  server host. Catches every hardcoded API endpoint the watch ships with.
- **`PhotonNetwork.ConnectUsingSettings`** — overrides the baked-in
  Photon Cloud AppId with yours.
- **`NetworkingPeer.CallAuthenticate`** — injects `userid` + `LoginLock`
  into Photon's CustomAuth so your server can identify the player.
- **`BouncyCastle.Tls.*NotifyServerCertificate`** — bypasses TLS chain
  validation. The 2020 client only trusts a baked-in cert set; we replace
  the verifier so any cert from your server is accepted. (Mitigated by
  also force-routing only to your configured host.)
- **`PlayerEmotes.RemoveInvalidCharactersFromMessage`** — skips the
  TMP font-validation step that NREs when the font asset isn't loaded
  yet (private servers don't always have it cached at chat-send time).
- **`AccountExtensions.CanLocalPlayerChat`** — forces chat-enabled for
  Junior / restricted accounts (single-tenant private server, no chat
  moderation needed).

See `Mod.cs` for the full patch list with citations to the Cpp2IL
disassembly that justifies each one.

## Config

The mod reads `MelonLoader/UserData/dorknet-clientmod.json`:

```json
{
  "ServerHost": "abc-def.trycloudflare.com",
  "PhotonAppId": "your-photon-realtime-appid",
  "PhotonCloudRegion": "eu",
  "InjectAuthValues": true,
  "EnableTlsTrustBypass": true
}
```

The patcher (desktop app and CLI) writes this file as part of the patch
flow — users don't normally edit it by hand.

## Building

```bash
dotnet build -c Release
# → bin/Release/DorkNet.ClientMod.dll
```

Requires Il2Cpp interop assemblies from the user's MelonLoader install.
Path defaults to `dist/RecRoom-Clean-2020.03.10/MelonLoader/`; override
with `-p:MelonLoaderDir=<path>`.
