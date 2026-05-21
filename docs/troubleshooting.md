# Troubleshooting

If your symptom isn't here, check [the FAQ](faq.md) and then open an
issue with: your OS, DorkNet version, server log (Settings → Send logs
to clipboard), and the exact steps that triggered the problem.

## Client / patcher

### "Signature verification failed" on launch

The image-signing public key baked into the patched `global-metadata.dat`
doesn't match the private key on the server. Repair from the Patch tab —
this re-fetches the server's current pubkey before patching.

### "Check your internet connection" loop at startup

The watch can't reach the server URL. Verify:
1. The host string in `dorknet-clientmod.json` matches the server's
   public URL (tunnel URL for Easy mode).
2. The server responds to `https://<host>/healthz` with `200 OK` from
   the same machine the client runs on.
3. No corporate proxy / DNS filter is intercepting `*.trycloudflare.com`.

### Black screen after the loading bar fills

The watch authenticated, joined Photon, but couldn't download the room
data blob. Look in MelonLoader's `Latest.log` for
`[DownloadRoomDataBlobCoroutine]` lines — the URL there tells you which
asset the server failed to serve.

### Save doesn't persist, MakerPen objects disappear after reload

Known issue with stale `LocalRoomScene.DataBlobName`. The client mod's
`SetRoomDataBlobName_Prefix` hijack should handle this — verify it's
firing in MelonLoader's log. If not, the mod failed to load; check that
`Mods\DorkNet.ClientMod.dll` is present and not blocked by Windows.

## Server

### `[boot] migrations failed`

The schema patch SQL errored. Most likely cause: an existing column whose
type doesn't match what we're trying to add. Look at the exact failure
in the log; if it's a Postgres `cannot alter type` error, the prior
column type needs a manual migration before DorkNet starts.

### Photon CustomAuth returns 401

Either:
- The Photon AppId in `appsettings` doesn't match the AppId baked into
  the patched client. They MUST match.
- The user logging in doesn't have a row in the `Players` table — the
  auth flow expects `userid` to be a real account.

### Room chat doesn't send

Run the client with `SaveDebugMod` enabled (or watch MelonLoader's log)
and look for the `[chat-trace]` lines. The pipeline is:

```
SendChatEmote → RemoveInvalidCharactersFromMessage → PurifyString HTTP
              → ProcessNewChatMessageReceived → Photon RPC
```

If any trace line is missing, that's where it died. The two most common
fixes:
- `[chat-fix] CanLocalPlayerChat returned false`: the account is flagged
  as a Junior with chat disabled. Force-true via the client mod (default
  behavior) or fix it server-side in admin.
- `Received malformed RecNet response`: the `/api/sanitize/v1` endpoint
  returned the wrong shape. Should be a bare JSON-quoted string. See
  `SanitizeController.cs`.

## Easy app

### "Cloudflared exited unexpectedly"

The bundled `cloudflared.exe` couldn't start. Most likely a Windows
Defender false-positive — check Defender's quarantine. If you find a
cleaner alternative tunnel provider, open an issue; we've been looking
for one.

### Patch button is greyed out

DorkNet didn't find a Rec Room install at the path you selected. Make
sure you pointed at the *folder* containing `Recroom_Release.exe`, not
the exe itself, and that `Recroom_Release_Data/` exists alongside.

### Mode switch lost my server data

Switching from Host mode → Join mode preserves the SQLite database; it
just stops auto-starting the server. Switch back to Host mode and your
players / rooms / images are still there.

If the data really is gone, check `%APPDATA%\DorkNet\dorknet.db` — that
file is the entire host-side state, easy to copy elsewhere as a backup.
