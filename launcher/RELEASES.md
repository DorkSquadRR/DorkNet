# Release artifact contract

The DorkNet Launcher (on `main`) reads
[`/versions.json`](../versions.json) to find every supported per-version
branch, then fetches release artifacts from each branch's GitHub
Releases.

Per-version branches must publish releases that follow this contract,
otherwise the launcher reports "no release found" to users on host /
join.

## Tag naming

A release's tag must start with the `release_tag_prefix` declared for
the branch in `versions.json`. Recommended scheme:

```
v1-{month}-{YYYY.MM.DD}
```

Examples (matching the current `release_tag_prefix` values):

| Branch | `release_tag_prefix` | Example full tag |
|---|---|---|
| `december-2020-12-18` | `v1-december` | `v1-december-2026.06.15` |
| `march-2020-03-10` | `v1-march` | `v1-march-2026.06.15` |

The launcher picks the most-recent release (by `published_at`) whose
tag starts with the prefix, then looks for the asset names below.

## Required assets

Each release must attach these two files:

### 1. `dorknet-server-{version_key}-win-x64.zip`

A self-contained .NET 9 publish of `DorkNet.Server` for `win-x64`.
The launcher unpacks this and spawns the executable directly.

Build with:

```pwsh
cd DorkNet.Server
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Then zip the publish dir's contents (NOT the dir itself):
cd bin/Release/net9.0/win-x64/publish
Compress-Archive -Path * -DestinationPath ../../../../../dorknet-server-{version_key}-win-x64.zip
```

The zip's root must contain `DorkNet.Server.exe` (or any `.exe`; the
launcher scans for it). Folder layout below that is preserved as-is.

### 2. `dorknet-clientpatch-{version_key}.zip`

The contents of the per-version branch's `tools/` directory limited
to the patcher scripts the launcher invokes:

- `install-plugin.ps1` (preferred — BepInEx route)
- `install-legacy-client.ps1` (fallback — direct byte-patch route)
- Any helper scripts these reference
- The compiled `DorkNet.ClientPatch.dll` if `install-plugin.ps1`
  expects a prebuilt DLL

The launcher invokes the patcher script with these arguments:

```
powershell -NoProfile -ExecutionPolicy Bypass -File install-plugin.ps1 `
  -RecRoomPath <user's path> `
  -PhotonAppId <user's> `
  -PhotonVoiceAppId <user's> `
  -ServerHost <apex host or join-code host>
```

So the script must accept those four parameters. If your branch's
patcher needs additional config, default to safe values.

## Verifying a release before publishing

```pwsh
# Locally simulate the launcher's fetch.
$tag = "v1-december-2026.06.15"
gh release view $tag --repo DorkSquadRR/DorkNet
gh release download $tag --repo DorkSquadRR/DorkNet `
  --pattern "dorknet-server-december_2020_12_18-win-x64.zip" `
  --pattern "dorknet-clientpatch-december_2020_12_18.zip"
```

Then unzip + boot the server locally to confirm it starts, and
unzip + run the patcher against a clean Rec Room install to confirm
patching works.

## Future: GitHub Actions workflow

Each per-version branch should land a `.github/workflows/release.yml`
that builds + zips + publishes the two artifacts on tag push. Not yet
implemented; releases are manual for now.
