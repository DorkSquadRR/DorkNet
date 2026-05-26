# Release artifact contract

The DorkNet Launcher (on `main`) reads
[`/versions.json`](../versions.json) to find every supported per-version
branch, then fetches release artifacts from each branch's GitHub
Releases.

Per-version branches must publish releases that follow this contract,
otherwise the launcher reports "no release found" / "manifest missing"
to users on host / join.

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
tag starts with the prefix, then looks for the two asset names below.

## Required assets

### 1. `dorknet-server-{version_key}-win-x64.zip`

A self-contained .NET 9 publish of `DorkNet.Server` for `win-x64`.
The launcher unpacks this and spawns the executable directly.

Build with:

```pwsh
cd DorkNet.Server
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
cd bin/Release/net9.0/win-x64/publish
Compress-Archive -Path * -DestinationPath ../../../../../dorknet-server-{version_key}-win-x64.zip
```

The zip's root must contain `DorkNet.Server.exe` (or any `.exe`; the
launcher scans for it).

### 2. `dorknet-clientpatch-{version_key}.zip`

The MelonLoader-based client patcher, packed into a single zip with a
**manifest.json** at the zip root telling the launcher what to do.
The launcher performs every step in C# — no PowerShell scripts run.

**Required layout inside the zip:**

```
manifest.json                       (described below)
MelonLoader.zip                     (optional — the MelonLoader install tree)
DorkNet.ClientMod.dll               (required — the DorkNet MelonLoader plugin)
dorknet-clientmod.json.template     (optional — UserData config template)
```

**`manifest.json` schema (v1):**

```json
{
  "$schema_version": 1,
  "loader_archive": "MelonLoader.zip",
  "plugin_dll": "DorkNet.ClientMod.dll",
  "plugin_dest": "MelonLoader/Mods",
  "config_template": "dorknet-clientmod.json.template",
  "config_dest": "MelonLoader/UserData/dorknet-clientmod.json",
  "old_plugin_paths": [
    "BepInEx/plugins/DorkNet.ClientPatch.dll"
  ]
}
```

All fields except `$schema_version` and `plugin_dll` are optional.
Defaults:

| Field | Default |
|---|---|
| `plugin_dest` | `MelonLoader/Mods` |
| `config_dest` | `MelonLoader/UserData/dorknet-clientmod.json` |
| `loader_archive` | (none — assumes user has MelonLoader installed) |
| `config_template` | (none — no config written) |

**`config_template` placeholders** rendered before write:

| Placeholder | Source |
|---|---|
| `{HOST}` | Host's tunnel hostname (or join code's `host` field) |
| `{PHOTON_APPID}` | Host's Photon Realtime AppId |
| `{PHOTON_VOICE_APPID}` | Host's Photon Voice AppId (falls back to Realtime if empty) |
| `{PHOTON_REGION}` | Photon Cloud region (us/eu/asia/jp/sa/kr/in/au); defaults to `us` |

Example template:

```json
{
  "ServerHost": "{HOST}",
  "PhotonAppId": "{PHOTON_APPID}",
  "PhotonVoiceAppId": "{PHOTON_VOICE_APPID}"
}
```

## What the launcher does on Apply

Given the manifest above, the launcher does (in this order):

1. Extracts `loader_archive` over the user's Rec Room install root
   (the parent of `Recroom_Release_Data`). MelonLoader's `version.dll`
   and `MelonLoader/` tree land in the right places.
2. Deletes any `old_plugin_paths` (cleans up an old BepInEx-era DLL,
   a previous plugin filename, etc.).
3. Copies `plugin_dll` to `<recroom-root>/<plugin_dest>/`.
4. Renders `config_template` (placeholder substitution) and writes
   to `<recroom-root>/<config_dest>`.

No PowerShell, no separate Steamless invocation, no byte-level
metadata patching in v1 — keep the patcher zip declarative. Future
schema versions can add fields for those without breaking older
launchers (they ignore unknown fields).

## Verifying a release before publishing

```pwsh
$tag = "v1-december-2026.06.15"
gh release view $tag --repo DorkSquadRR/DorkNet
gh release download $tag --repo DorkSquadRR/DorkNet `
  --pattern "dorknet-server-december_2020_12_18-win-x64.zip" `
  --pattern "dorknet-clientpatch-december_2020_12_18.zip"
```

Then unzip + inspect: the patcher zip should have `manifest.json` at
root, the named DLL should exist, the `config_template` (if listed)
should exist, etc.

## Future: GitHub Actions workflow

Each per-version branch should land a `.github/workflows/release.yml`
that builds + packages the artifacts on tag push. Not yet implemented;
releases are manual for now.
