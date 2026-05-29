<#
.SYNOPSIS
  MelonLoader-based setup for the DorkNet 2020 client.

.DESCRIPTION
  One command that:
    1. Downloads MelonLoader (IL2CPP build, x64) from GitHub releases
       and extracts it into the Rec Room install dir.
    2. Builds DorkNet.ClientMod (the MelonLoader IL2CPP client mod)
       against MelonLoader's generated Il2CppAssemblies.
    3. Drops the built DLL into <RecRoomInstall>\Mods\ and writes
       <RecRoomInstall>\MelonLoader\UserData\dorknet-clientmod.json
       with the user-supplied Photon AppId / server host / etc.
    4. Calls tools\patch-client.ps1 to apply the static byte patches
       (Photon AppId in resources.assets, image signing public key,
       image signature verifier, and the CodeStage Anti-Cheat Toolkit
       neutraliser).

  Idempotent — re-running re-applies any missing pieces but won't
  re-download MelonLoader if it's already cached, and the byte patches
  detect already-patched bytes and skip.

  First-run gotcha: MelonLoader needs ONE successful game launch to
  generate Il2CppAssemblies\ before the mod can build against it. This
  script auto-detects that state. If Il2CppAssemblies\ is missing it
  installs MelonLoader, writes the JSON config, runs the byte patches,
  then prints "launch the game once, then re-run this script with
  -ResumeBuild" instead of failing the build.

.PARAMETER RecRoomPath
  Path to the Recroom_Release_Data folder (the one with
  resources.assets). The script uses this folder's parent as the install
  root for MelonLoader extraction.

.PARAMETER PhotonAppId
  Photon Cloud Realtime AppId (GUID, no curly braces). Required on
  the first install run.

.PARAMETER PhotonVoiceAppId
  Photon Cloud Voice AppId. Defaults to PhotonAppId.

.PARAMETER ServerHost
  Domain that replaces .rec.net in every URL the watch builds.
  Default: localhost.

.PARAMETER MelonLoaderZip
  Optional local path to a MelonLoader.x64.zip. Skips the download.

.PARAMETER ForceMelonLoader
  Re-extract MelonLoader even if it's already present.

.PARAMETER ResumeBuild
  Skip the MelonLoader install + patch-client.ps1 step and only do
  the mod build/copy step. Use after the first run has prompted you to
  launch the game once.

.PARAMETER SkipBytePatches
  Skip the tools\patch-client.ps1 call. Use if you've already run it
  separately or want to manage byte patches by hand.

.PARAMETER Revert
  Uninstall the mod + MelonLoader. Does not touch resources.assets
  byte patches (use patch-client.ps1 -Revert for those).

.PARAMETER ImageSigningPublicKeyPath
  Forwarded to patch-client.ps1 unchanged — see that script's help.

.EXAMPLE
  # First-time setup. After the script prints "launch the game once",
  # double-click Recroom_Release.exe, wait for the watch UI, quit, then
  # re-run with -ResumeBuild to finish the mod build.
  .\tools\install-melon.ps1 `
    -RecRoomPath "C:\Users\you\Documents\Recnet\dist\RecRoom-Clean-2020.03.10\Recroom_Release_Data" `
    -PhotonAppId  <your-photon-realtime-app-id>

.EXAMPLE
  # Second pass after the first game launch generated Il2CppAssemblies.
  .\tools\install-melon.ps1 `
    -RecRoomPath "C:\Users\you\Documents\Recnet\dist\RecRoom-Clean-2020.03.10\Recroom_Release_Data" `
    -PhotonAppId  <your-photon-realtime-app-id> `
    -ResumeBuild

.EXAMPLE
  # Uninstall the mod (keeps the byte patches; use patch-client.ps1 -Revert
  # for those).
  .\tools\install-melon.ps1 -RecRoomPath D:\RecRoom\Recroom_Release_Data -Revert
#>

[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true)]
    [string]$RecRoomPath,

    # Mandatory only when -FromServerConfig is not used. Validation
    # happens after FromServerConfig fills it in below.
    [Parameter(ParameterSetName = 'Install')]
    [string]$PhotonAppId,

    [Parameter(ParameterSetName = 'Install')]
    [string]$PhotonVoiceAppId,

    [Parameter(ParameterSetName = 'Install')]
    [ValidateSet('us','eu','asia','jp','au','usw','sa','cae','kr','in','ru','rue')]
    [string]$PhotonCloudRegion = 'us',

    # Read PhotonAppId / VoiceAppId / CloudRegion from the DorkNet.Server
    # appsettings file at this path. Use the value from
    # `DorkNet.Server\appsettings.Local.json` (falls back to
    # `appsettings.json`) so you don't have to retype the GUIDs you
    # already configured on the server side. Any explicit -PhotonAppId /
    # -PhotonVoiceAppId / -PhotonCloudRegion override the file values.
    [Parameter(ParameterSetName = 'Install')]
    [string]$FromServerConfig,

    [Parameter(ParameterSetName = 'Install')]
    [string]$ServerHost = 'localhost',

    [Parameter(ParameterSetName = 'Install')]
    [string]$MelonLoaderZip,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$ForceMelonLoader,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$ResumeBuild,

    # Auto-bootstrap MelonLoader by launching Recroom_Release.exe in the
    # background, polling for Il2CppAssemblies\Assembly-CSharp.dll to
    # appear, then killing the process. Makes the install a single
    # script run instead of the install -> launch -> ResumeBuild dance.
    # On by default; pass -AutoBootstrap:$false to keep the two-step flow.
    [Parameter(ParameterSetName = 'Install')]
    [bool]$AutoBootstrap = $true,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$SkipBytePatches,

    [Parameter(ParameterSetName = 'Install')]
    [string]$ImageSigningPublicKeyPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Revert')]
    [switch]$Revert
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Seed $LASTEXITCODE so every later "did the native call succeed" check
# has something to read under StrictMode. PowerShell only populates this
# automatic variable AFTER a native executable runs, so a cold-shell
# invocation of this script can otherwise hit a VariableIsUndefined
# error the first time we test it.
$global:LASTEXITCODE = 0

# Pinned MelonLoader release. "latest" would be more convenient but
# pinning means a regression upstream doesn't silently land on tester
# machines — a regression upstream doesn't silently land on testers.
# Bump when there's a known-good newer build.
$Script:MelonLoaderUrl = 'https://github.com/LavaGang/MelonLoader/releases/download/v0.6.6/MelonLoader.x64.zip'
$Script:MelonLoaderCacheName = 'MelonLoader.x64-0.6.6.zip'

# Steamless pin — same release as install-plugin.ps1 so the cache hits
# between the two scripts. The 2020 Rec Room build is wrapped with
# Valve's SteamStub DRM; if the .exe is launched outside Steam the
# stub aborts with exit code 1, which is what kills MelonLoader's
# auto-bootstrap before it can generate Il2CppAssemblies. Stripping
# the wrapper is the cleanest fix.
$Script:SteamlessUrl = 'https://github.com/atom0s/Steamless/releases/download/v3.1.0.5/Steamless.v3.1.0.5.-.by.atom0s.zip'
$Script:SteamlessCacheName = 'Steamless.v3.1.0.5.zip'

function Write-Step($msg)  { Write-Host ('> ' + $msg) -ForegroundColor Cyan }
function Write-OK($msg)    { Write-Host ('+ ' + $msg) -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host ('! ' + $msg) -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host ('x ' + $msg) -ForegroundColor Red }

# ── Steam DRM detection + stripping ──────────────────────────────────
# These three helpers are mechanically identical to the ones in
# install-plugin.ps1 (they predate this script). Kept locally here
# rather than dot-sourced so install-melon.ps1 stays a single file you
# can drop on a fresh machine.

function Test-SteamDrmWrapped {
    # Walks the PE header to see whether the AddressOfEntryPoint lands
    # inside a `.bind` section — Valve's drm_wrap injects `.bind`
    # carrying the SteamStub trampoline and rewrites the entry point
    # to land there. After Steamless strips, the entry point points
    # back into `.text` and the leftover `.bind` section is harmless.
    param([string]$ExePath)
    try {
        $fs = [System.IO.File]::OpenRead($ExePath)
        try {
            $br = New-Object System.IO.BinaryReader($fs)
            $fs.Seek(0x3C, 'Begin') | Out-Null
            $peOff = $br.ReadInt32()
            if ($peOff -lt 0 -or $peOff + 24 -gt $fs.Length) { return $false }
            $fs.Seek($peOff + 6, 'Begin') | Out-Null
            $sections = $br.ReadUInt16()
            $fs.Seek($peOff + 20, 'Begin') | Out-Null
            $optSize = $br.ReadUInt16()
            $fs.Seek($peOff + 24 + 16, 'Begin') | Out-Null
            $entryRva = $br.ReadUInt32()
            $secStart = $peOff + 24 + $optSize
            for ($i = 0; $i -lt $sections; $i++) {
                $fs.Seek($secStart + $i * 40, 'Begin') | Out-Null
                $name = [System.Text.Encoding]::ASCII.GetString($br.ReadBytes(8)).TrimEnd([char]0)
                $vSize = $br.ReadUInt32()
                $vAddr = $br.ReadUInt32()
                if ($name -eq '.bind' -and $entryRva -ge $vAddr -and $entryRva -lt ($vAddr + $vSize)) {
                    return $true
                }
            }
            return $false
        } finally { $fs.Close() }
    } catch {
        return $false
    }
}

function Get-Steamless {
    # Returns the path to a usable Steamless.CLI.exe. Downloads + extracts
    # the pinned release on first run, caches under %LOCALAPPDATA%\DorkNet.
    $cacheDir   = Join-Path $env:LOCALAPPDATA 'DorkNet'
    $extractDir = Join-Path $cacheDir 'Steamless'
    $cli        = Join-Path $extractDir 'Steamless.CLI.exe'
    if (Test-Path -LiteralPath $cli -PathType Leaf) {
        return $cli
    }
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $zipPath = Join-Path $cacheDir $Script:SteamlessCacheName
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        Write-Step "Downloading Steamless -> $zipPath"
        Write-Host "  $Script:SteamlessUrl" -ForegroundColor DarkGray
        Invoke-WebRequest -Uri $Script:SteamlessUrl -OutFile $zipPath `
            -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
    }
    Write-Step "Extracting Steamless -> $extractDir"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
        # Some Steamless releases nest the CLI in a subfolder.
        $found = Get-ChildItem -LiteralPath $extractDir -Recurse -Filter 'Steamless.CLI.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $cli = $found.FullName }
    }
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
        throw "Steamless.CLI.exe not found after extracting $zipPath"
    }
    return $cli
}

function Invoke-DrmStrip {
    # Runs Steamless against the wrapped exe in-place. Steamless writes
    # an unpacked copy alongside the original; we move the original to
    # .drm-original.exe and rename the unpacked file in place so the
    # auto-bootstrap step doesn't have to know which is which.
    param([string]$ExePath)
    $cli = Get-Steamless
    Write-Step "Stripping Steam DRM from $ExePath"
    $exeDir = Split-Path -Parent $ExePath
    $proc = Start-Process -FilePath $cli `
        -ArgumentList @('--quiet', '--keepbind', '0', $ExePath) `
        -NoNewWindow -PassThru -Wait
    if ($proc.ExitCode -ne 0) {
        throw "Steamless exited with code $($proc.ExitCode). The exe may not be a recognised Steam DRM build."
    }
    $unpacked = "$ExePath.unpacked.exe"
    if (-not (Test-Path -LiteralPath $unpacked -PathType Leaf)) {
        throw "Steamless ran but didn't produce $unpacked. Check the tool's output."
    }
    $bak = Join-Path $exeDir 'Recroom_Release.drm-original.exe'
    if (-not (Test-Path -LiteralPath $bak -PathType Leaf)) {
        Move-Item -LiteralPath $ExePath -Destination $bak -Force
    } else {
        Remove-Item -LiteralPath $ExePath -Force
    }
    Move-Item -LiteralPath $unpacked -Destination $ExePath -Force
    Write-OK "DRM stripped. Original kept as $bak"
}

# ── Path resolution ──────────────────────────────────────────────────
$resolved = Resolve-Path -LiteralPath $RecRoomPath -ErrorAction SilentlyContinue
if ($resolved) { $RecRoomPath = $resolved.Path } else { $RecRoomPath = $null }
if (-not $RecRoomPath -or -not (Test-Path -LiteralPath $RecRoomPath -PathType Container)) {
    Write-Err "RecRoomPath '$RecRoomPath' does not exist."
    exit 1
}
if (-not (Test-Path -LiteralPath (Join-Path $RecRoomPath 'resources.assets') -PathType Leaf)) {
    Write-Err "resources.assets not found in '$RecRoomPath'. Point at the Recroom_Release_Data folder."
    exit 1
}

$RecRoomRoot    = Split-Path -Parent $RecRoomPath
$RecRoomExe     = Join-Path $RecRoomRoot 'Recroom_Release.exe'
$MelonLoaderDir = Join-Path $RecRoomRoot 'MelonLoader'
$ModsDir        = Join-Path $RecRoomRoot 'Mods'
$UserDataDir    = Join-Path $MelonLoaderDir 'UserData'
$Il2CppAsmsDir  = Join-Path $MelonLoaderDir 'Il2CppAssemblies'
$RepoRoot       = Split-Path -Parent $PSScriptRoot
$ModProj        = Join-Path $RepoRoot    'DorkNet.ClientMod'
$ModCsproj      = Join-Path $ModProj     'DorkNet.ClientMod.csproj'
$PatchClient    = Join-Path $PSScriptRoot 'patch-client.ps1'

# ── Pull Photon values from the server's appsettings when -FromServerConfig
#    is set OR when a sibling DorkNet.Server\appsettings.Local.json exists.
#    Explicit -Photon* params still win over file values so you can patch
#    a one-off client against a different Photon app without touching the
#    server config. Both Local and the base file get a look — Local first.
function Read-ServerPhotonConfig {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if (-not $json.PSObject.Properties['Photon']) { return $null }
        $photon = $json.Photon
        $appId  = if ($photon.PSObject.Properties['AppId'])       { [string]$photon.AppId }       else { $null }
        $voice  = if ($photon.PSObject.Properties['VoiceAppId'])  { [string]$photon.VoiceAppId }  else { $null }
        $region = if ($photon.PSObject.Properties['CloudRegion']) { [string]$photon.CloudRegion } else { $null }
        return [pscustomobject]@{ AppId = $appId; VoiceAppId = $voice; CloudRegion = $region; Source = $Path }
    } catch {
        Write-Warn2 "Could not parse Photon section from $Path : $($_.Exception.Message)"
        return $null
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Install') {
    if ($FromServerConfig) {
        $candidates = @($FromServerConfig)
    } else {
        $candidates = @(
            (Join-Path $RepoRoot 'DorkNet.Server\appsettings.Local.json'),
            (Join-Path $RepoRoot 'DorkNet.Server\appsettings.json')
        )
    }
    foreach ($cand in $candidates) {
        $cfg = Read-ServerPhotonConfig -Path $cand
        if (-not $cfg) { continue }
        if (-not $PhotonAppId        -and $cfg.AppId)       { $PhotonAppId        = $cfg.AppId }
        if (-not $PhotonVoiceAppId   -and $cfg.VoiceAppId)  { $PhotonVoiceAppId   = $cfg.VoiceAppId }
        if (-not $PSBoundParameters.ContainsKey('PhotonCloudRegion') -and $cfg.CloudRegion) {
            $PhotonCloudRegion = $cfg.CloudRegion
        }
        Write-OK ("Loaded Photon config from $($cfg.Source) " +
                  "(AppId=$(if($cfg.AppId){'<set>'}else{'<unset>'}), " +
                  "VoiceAppId=$(if($cfg.VoiceAppId){'<set>'}else{'<unset>'}), " +
                  "CloudRegion=$(if($cfg.CloudRegion){$cfg.CloudRegion}else{'<unset>'}))")
        break
    }

    if (-not $PhotonAppId) {
        Write-Err 'PhotonAppId is required (pass -PhotonAppId <guid> or -FromServerConfig <appsettings.json>).'
        exit 1
    }
    $guidRe = '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
    if ($PhotonAppId -notmatch $guidRe) {
        Write-Err "PhotonAppId '$PhotonAppId' is not a GUID."
        exit 1
    }
    if ($PhotonVoiceAppId -and $PhotonVoiceAppId -notmatch $guidRe) {
        Write-Err "PhotonVoiceAppId '$PhotonVoiceAppId' is not a GUID."
        exit 1
    }
}

# ── Revert ───────────────────────────────────────────────────────────
if ($Revert) {
    Write-Step 'REVERT MODE — removing MelonLoader + DorkNet.ClientMod'

    foreach ($p in @(
        (Join-Path $RecRoomRoot 'MelonLoader'),
        (Join-Path $RecRoomRoot 'Mods'),
        (Join-Path $RecRoomRoot 'version.dll'),
        (Join-Path $RecRoomRoot 'dobby.dll'),
        (Join-Path $RecRoomRoot 'NOTICE.txt')
    )) {
        if (Test-Path -LiteralPath $p) {
            Write-Step "Removing $p"
            Remove-Item -LiteralPath $p -Recurse -Force
            Write-OK "Removed $p"
        }
    }

    Write-OK 'MelonLoader and ClientMod removed.'
    Write-Host '  resources.assets / image signing / anti-cheat byte patches are still applied.' -ForegroundColor DarkGray
    Write-Host '  Use tools\patch-client.ps1 -Revert -RecRoomPath ... to roll those back too.' -ForegroundColor DarkGray
    exit 0
}

# ── 1. MelonLoader install ───────────────────────────────────────────
function Get-MelonLoaderZip {
    if ($MelonLoaderZip) {
        if (-not (Test-Path -LiteralPath $MelonLoaderZip -PathType Leaf)) {
            Write-Err "MelonLoaderZip '$MelonLoaderZip' not found."
            exit 1
        }
        return $MelonLoaderZip
    }
    $cacheDir = Join-Path $env:LOCALAPPDATA 'DorkNet'
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $cached = Join-Path $cacheDir $Script:MelonLoaderCacheName
    if (Test-Path -LiteralPath $cached -PathType Leaf) {
        Write-OK "Using cached MelonLoader zip -> $cached"
        return $cached
    }
    Write-Step "Downloading MelonLoader -> $cached"
    Write-Host "  $Script:MelonLoaderUrl" -ForegroundColor DarkGray
    Invoke-WebRequest -Uri $Script:MelonLoaderUrl -OutFile $cached `
        -UseBasicParsing -TimeoutSec 120 -ErrorAction Stop
    return $cached
}

$melonAlreadyInstalled = (Test-Path -LiteralPath (Join-Path $RecRoomRoot 'version.dll') -PathType Leaf) `
    -and (Test-Path -LiteralPath $MelonLoaderDir -PathType Container)

if ($ResumeBuild) {
    if (-not $melonAlreadyInstalled) {
        Write-Err 'ResumeBuild requested but MelonLoader is not installed. Run without -ResumeBuild first.'
        exit 1
    }
    Write-OK 'Resuming after game launch — skipping MelonLoader extract.'
} elseif ($melonAlreadyInstalled -and -not $ForceMelonLoader) {
    Write-OK "MelonLoader already installed at $MelonLoaderDir"
} else {
    $zipPath = Get-MelonLoaderZip
    Write-Step "Extracting MelonLoader -> $RecRoomRoot"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $RecRoomRoot -Force
    if (-not (Test-Path -LiteralPath (Join-Path $RecRoomRoot 'version.dll'))) {
        Write-Err 'MelonLoader extracted but version.dll proxy missing. The downloaded zip may be the wrong shape.'
        exit 1
    }
    Write-OK 'MelonLoader extracted.'
}

# ── 2. Make sure UserData + Mods exist, write mod config ────────────
New-Item -ItemType Directory -Path $UserDataDir -Force | Out-Null
New-Item -ItemType Directory -Path $ModsDir     -Force | Out-Null

$cfgPath = Join-Path $UserDataDir 'dorknet-clientmod.json'
$cfg = [ordered]@{
    ServerHost           = $ServerHost
    PhotonAppId          = $PhotonAppId
    PhotonVoiceAppId     = if ($PhotonVoiceAppId) { $PhotonVoiceAppId } else { '' }
    PhotonCloudRegion    = $PhotonCloudRegion
    InjectAuthValues     = $true
    EnableTlsTrustBypass = $true
}
Write-Step "Writing mod config -> $cfgPath"
[System.IO.File]::WriteAllText($cfgPath, ($cfg | ConvertTo-Json -Depth 4))
Write-OK 'dorknet-clientmod.json written.'

# ── 3. Byte patches via patch-client.ps1 ─────────────────────────────
if (-not $SkipBytePatches) {
    if (-not (Test-Path -LiteralPath $PatchClient -PathType Leaf)) {
        Write-Warn2 "patch-client.ps1 not found at $PatchClient — skipping byte patches."
    } else {
        Write-Step 'Running patch-client.ps1 (Photon AppIds, image signing, anti-cheat)'
        $patchArgs = @{
            RecRoomPath = $RecRoomPath
            PhotonAppId = $PhotonAppId
        }
        if ($PhotonVoiceAppId)            { $patchArgs.PhotonVoiceAppId = $PhotonVoiceAppId }
        if ($ImageSigningPublicKeyPath)   { $patchArgs.ImageSigningPublicKeyPath = $ImageSigningPublicKeyPath }
        # Seed $LASTEXITCODE so the post-call check works under StrictMode.
        # PowerShell only populates $LASTEXITCODE after a native exe runs;
        # a pure-cmdlet path through patch-client.ps1 leaves it undefined,
        # and reading an undefined variable throws under
        # Set-StrictMode -Version Latest.
        $global:LASTEXITCODE = 0
        & $PatchClient @patchArgs
        $patchExit = $LASTEXITCODE
        if ($patchExit -ne 0) {
            Write-Err "patch-client.ps1 returned exit code $patchExit"
            exit $patchExit
        }
    }
}

# ── 4a. Auto-bootstrap MelonLoader to generate Il2CppAssemblies ──────
#
#   When MelonLoader extracts for the first time it ships no interop
#   assemblies — they only appear once the game boots with the loader
#   attached and Cpp2IL + Il2CppInterop.Generator run against
#   GameAssembly.dll + il2cpp_data\Metadata\global-metadata.dat.
#   Generation finishes a few seconds into startup (long before the
#   game's own bootloader gets anywhere interesting), so we can launch
#   the .exe, poll for the marker DLL, then kill the process.
#
#   Anchor marker: Assembly-CSharp.dll inside Il2CppAssemblies\. That's
#   the last assembly the generator writes (after Il2Cppmscorlib +
#   Il2CppSystem + all the Unity proxies), so seeing it on disk means
#   the rest are already in place.
#
#   Caveats called out in the user-facing log line on failure:
#     * if Recroom_Release.exe is wrapped with Steam DRM (.bind section)
#       it'll silently refuse to launch outside Steam; the user has
#       either to keep Steam running OR strip DRM via Steamless first
#       (see install-plugin.ps1's Invoke-DrmStrip for that path).
#     * anti-cheat WOULD interfere here, but patch-client.ps1 above
#       just neutralised it before this step runs.
function Invoke-MelonBootstrap {
    param([string]$ExePath, [string]$Il2CppAsmsDir)

    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        Write-Err "Recroom_Release.exe not found at $ExePath"
        return $false
    }
    $exeDir = Split-Path -Parent $ExePath
    foreach ($required in @(
        (Join-Path $exeDir 'version.dll'),
        (Join-Path $exeDir 'MelonLoader\Dependencies\Bootstrap.dll'),
        (Join-Path $exeDir 'MelonLoader\Dependencies\dobby.dll')
    )) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            Write-Err "MelonLoader native dependency missing: $required"
            Write-Host '  Re-run this script without -ResumeBuild, or use -ForceMelonLoader to re-extract MelonLoader.' -ForegroundColor DarkGray
            return $false
        }
    }

    Write-Step "Auto-bootstrap: launching Recroom_Release.exe to generate Il2CppAssemblies"
    Write-Host '  (the game window will open briefly, then get killed once the proxy' -ForegroundColor DarkGray
    Write-Host '   assemblies are on disk. Boot to the watch is NOT required.)'        -ForegroundColor DarkGray

    $proc = $null
    try {
        $proc = Start-Process -FilePath $ExePath -WorkingDirectory $exeDir -PassThru -ErrorAction Stop
    } catch {
        Write-Err "Failed to launch the game: $($_.Exception.Message)"
        return $false
    }

    $marker      = Join-Path $Il2CppAsmsDir 'Assembly-CSharp.dll'
    $timeoutSec  = 180
    $pollSec     = 2
    $start       = Get-Date

    while ($true) {
        if ($proc.HasExited) {
            Write-Err "Game process exited (code $($proc.ExitCode)) before generating assemblies."
            Write-Host '  Common causes: Steam DRM wrapper rejects standalone launch, MelonLoader proxy' -ForegroundColor DarkGray
            Write-Host '  DLL got quarantined by Defender, or the game crashed on boot.'                  -ForegroundColor DarkGray
            return $false
        }
        if (Test-Path -LiteralPath $marker -PathType Leaf) {
            # Give Cpp2IL/Il2CppInterop a few extra seconds to finish
            # writing the last of the proxy DLLs before we yank the process.
            # The marker shows up partway through the generator run; the
            # rest of the writes are quick but not instant.
            Start-Sleep -Seconds 6
            break
        }
        $elapsed = (Get-Date) - $start
        if ($elapsed.TotalSeconds -gt $timeoutSec) {
            Write-Warn2 ("Timed out after {0:n0}s waiting for {1}." -f $timeoutSec, $marker)
            try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
            return $false
        }
        Start-Sleep -Seconds $pollSec
    }

    try {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        # Wait a beat for filesystem handles to release.
        Start-Sleep -Seconds 1
    } catch {}
    Write-OK "Il2CppAssemblies generated; game process closed."
    return $true
}

if (-not (Test-Path -LiteralPath $Il2CppAsmsDir -PathType Container) -and $AutoBootstrap -and -not $ResumeBuild) {
    # Strip Steam DRM first if the .exe is wrapped. The launch in
    # Invoke-MelonBootstrap is detached from Steam, so the SteamStub
    # trampoline would reject with exit code 1 (same symptom the user
    # gets if they double-click outside Steam too — MelonLoader's
    # proxy DLL never gets loaded, no Il2CppAssemblies generated, and
    # MelonLoader's first-launch state stays incomplete).
    if (Test-SteamDrmWrapped -ExePath $RecRoomExe) {
        Write-Warn2 "Recroom_Release.exe is wrapped with Steam DRM."
        Write-Step "Stripping DRM so MelonLoader can launch the .exe directly."
        try {
            Invoke-DrmStrip -ExePath $RecRoomExe
        } catch {
            Write-Err "DRM strip failed: $($_.Exception.Message)"
            Write-Host '  Falling back to the manual flow: launch the game once' -ForegroundColor DarkGray
            Write-Host '  (e.g. from Steam) so it self-strips the DRM, then re-run' -ForegroundColor DarkGray
            Write-Host '  this script with -ResumeBuild.' -ForegroundColor DarkGray
        }
    } else {
        Write-OK "Recroom_Release.exe is already DRM-stripped."
    }

    if (-not (Invoke-MelonBootstrap -ExePath $RecRoomExe -Il2CppAsmsDir $Il2CppAsmsDir)) {
        Write-Warn2 'Auto-bootstrap failed. Falling through to the manual two-step flow.'
    }
}

# ── 4b. Build DorkNet.ClientMod against Il2CppAssemblies ─────────────
if (-not (Test-Path -LiteralPath $Il2CppAsmsDir -PathType Container)) {
    Write-Warn2 "Il2CppAssemblies not generated yet at $Il2CppAsmsDir"
    Write-Host ''
    Write-Host '  MelonLoader needs ONE successful launch to generate the IL2CPP proxy' -ForegroundColor Yellow
    Write-Host '  assemblies the mod builds against. Steps:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '    1. Launch Recroom_Release.exe (just open the game and let it boot' -ForegroundColor Yellow
    Write-Host '       to the watch UI; MelonLoader will dump assemblies in the' -ForegroundColor Yellow
    Write-Host '       background).' -ForegroundColor Yellow
    Write-Host '    2. Close the game.' -ForegroundColor Yellow
    Write-Host '    3. Re-run this script with -ResumeBuild to finish the mod build.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  (The byte patches and mod config are already in place; only the' -ForegroundColor DarkGray
    Write-Host '   compiled mod DLL is missing.)' -ForegroundColor DarkGray
    exit 0
}

if (-not (Test-Path -LiteralPath $ModCsproj -PathType Leaf)) {
    Write-Err "DorkNet.ClientMod csproj not found at $ModCsproj"
    exit 1
}

Write-Step "Building DorkNet.ClientMod (interop=$Il2CppAsmsDir)"
$buildOutput = & dotnet build $ModCsproj -c Release --nologo `
    -p:MelonLoaderDir=$MelonLoaderDir `
    -p:Il2CppAssembliesDir=$Il2CppAsmsDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err 'DorkNet.ClientMod build failed. Build output:'
    Write-Host ($buildOutput -join "`n") -ForegroundColor DarkGray
    exit 1
}
$builtDll = Join-Path $ModProj 'bin\Release\DorkNet.ClientMod.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    Write-Err "Build succeeded but $builtDll not found."
    exit 1
}

# ── 5. Copy mod DLL into Mods/ ───────────────────────────────────────
Copy-Item -LiteralPath $builtDll -Destination $ModsDir -Force
Write-OK ("Mod DLL copied -> " + (Join-Path $ModsDir 'DorkNet.ClientMod.dll'))

# ── 6. Summary ───────────────────────────────────────────────────────
Write-Host ''
Write-OK 'Install complete.'
Write-Host ('  MelonLoader   : ' + $MelonLoaderDir) -ForegroundColor DarkGray
Write-Host ('  Mods folder   : ' + $ModsDir)        -ForegroundColor DarkGray
Write-Host ('  Mod config    : ' + $cfgPath)        -ForegroundColor DarkGray
Write-Host ('  Photon AppId  : ' + $PhotonAppId)    -ForegroundColor DarkGray
Write-Host ('  Server host   : ' + $ServerHost)     -ForegroundColor DarkGray
Write-Host ''
Write-Host '  Launch Recroom_Release.exe. MelonLoader log appears in:' -ForegroundColor DarkGray
Write-Host ('    ' + (Join-Path $MelonLoaderDir 'Latest.log')) -ForegroundColor DarkGray
