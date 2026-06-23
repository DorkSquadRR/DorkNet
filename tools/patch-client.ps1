<#
.SYNOPSIS
  Applies static DorkNet byte patches to an extracted Rec Room client.

.DESCRIPTION
  RecRoomPath must point at the Unity data folder, usually
  Recroom_Release_Data or RecRoom_Data. The script resolves the install root
  from that folder, then patches the hardcoded Photon Realtime and Voice
  AppIds wherever the build stores them: GameAssembly.dll,
  il2cpp_data\Metadata\global-metadata.dat, and resources.assets.

  The replacement values must be GUID strings, so the byte length stays
  unchanged and the patch can be done in place.

.PARAMETER RecRoomPath
  Path to Recroom_Release_Data.

.PARAMETER PhotonAppId
  Photon Cloud Realtime AppId.

.PARAMETER PhotonVoiceAppId
  Photon Cloud Voice AppId. Defaults to PhotonAppId.

.PARAMETER DisableSignatureChecks
  Deprecated compatibility switch. File-check bypass is handled by
  DorkNet.ClientMod so the embedded build/signature identifiers stay valid.

.PARAMETER Revert
  Restore patched files from backups created by this script.

.PARAMETER DryRun
  Show what would be patched without writing files.
#>

[CmdletBinding(DefaultParameterSetName = 'Patch')]
param(
    [Parameter(Mandatory = $true)]
    [string]$RecRoomPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Patch')]
    [ValidatePattern('(?i)^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')]
    [string]$PhotonAppId,

    [Parameter(ParameterSetName = 'Patch')]
    [ValidatePattern('(?i)^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')]
    [string]$PhotonVoiceAppId,

    [Parameter(ParameterSetName = 'Patch')]
    [switch]$DisableSignatureChecks,

    [Parameter(ParameterSetName = 'Patch')]
    [string]$ImageSigningPublicKeyPath,

    [Parameter(ParameterSetName = 'Patch')]
    [string]$ServerIp,

    [Parameter(ParameterSetName = 'Patch')]
    [string]$CertPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Revert')]
    [switch]$Revert,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$OfficialPhotonAppId = '9372aa8d-d3f4-44a0-986d-419e145a2b83'
$OfficialPhotonVoiceAppId = 'e93ae440-f238-4b6c-848f-1df89faf14f5'

function Write-Step($msg) { Write-Host ('> ' + $msg) -ForegroundColor Cyan }
function Write-OK($msg)   { Write-Host ('OK: ' + $msg) -ForegroundColor Green }
function Write-Warn($msg) { Write-Host ('WARN: ' + $msg) -ForegroundColor Yellow }

function Find-SequenceOffsets {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Haystack,
        [Parameter(Mandatory = $true)][byte[]]$Needle
    )

    $offsets = New-Object System.Collections.Generic.List[int]
    if ($Needle.Length -eq 0 -or $Haystack.Length -lt $Needle.Length) {
        return $offsets
    }

    $last = $Haystack.Length - $Needle.Length
    for ($i = 0; $i -le $last; $i++) {
        if ($Haystack[$i] -ne $Needle[0]) { continue }
        $matched = $true
        for ($j = 1; $j -lt $Needle.Length; $j++) {
            if ($Haystack[$i + $j] -ne $Needle[$j]) {
                $matched = $false
                break
            }
        }
        if ($matched) { $offsets.Add($i) }
    }
    return $offsets
}

function Format-HexOffset([int]$Offset) {
    return '0x{0:X}' -f $Offset
}

function Apply-AsciiStringPatch {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][string]$Original,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $enc = [System.Text.Encoding]::ASCII
    $originalBytes = $enc.GetBytes($Original)
    $replacementBytes = $enc.GetBytes($Replacement)
    if ($originalBytes.Length -ne $replacementBytes.Length) {
        throw "$Label replacement length differs from original. Expected $($originalBytes.Length) bytes, got $($replacementBytes.Length)."
    }

    $offsets = @(Find-SequenceOffsets -Haystack $Bytes -Needle $originalBytes)
    if ($offsets.Count -eq 0) {
        $replacementOffsets = @(Find-SequenceOffsets -Haystack $Bytes -Needle $replacementBytes)
        if ($replacementOffsets.Count -gt 0) {
            Write-OK "$Label already patched at $((@($replacementOffsets) | ForEach-Object { Format-HexOffset $_ }) -join ', ')"
            return $false
        }
        Write-Warn "$Label official AppId '$Original' was not found, and replacement '$Replacement' is not already present."
        return $null
    }

    foreach ($offset in $offsets) {
        [System.Array]::Copy($replacementBytes, 0, $Bytes, $offset, $replacementBytes.Length)
    }
    Write-OK "$Label patched at $((@($offsets) | ForEach-Object { Format-HexOffset $_ }) -join ', ')"
    return $true
}

function Get-ClientPaths {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $resolved) {
        throw "RecRoomPath '$Path' does not exist."
    }

    $dataPath = $resolved.Path
    if (-not (Test-Path -LiteralPath (Join-Path $dataPath 'resources.assets') -PathType Leaf)) {
        throw "resources.assets not found in '$dataPath'. Point RecRoomPath at the Recroom_Release_Data folder."
    }

    $root = Split-Path -Parent $dataPath
    $gameAssembly = Join-Path $root 'GameAssembly.dll'
    if (-not (Test-Path -LiteralPath $gameAssembly -PathType Leaf)) {
        throw "GameAssembly.dll not found next to '$dataPath'."
    }

    [pscustomobject]@{
        DataPath = $dataPath
        Root = $root
        GameAssembly = $gameAssembly
        Resources = Join-Path $dataPath 'resources.assets'
        GlobalMetadata = Join-Path $dataPath 'il2cpp_data\Metadata\global-metadata.dat'
    }
}

$paths = Get-ClientPaths -Path $RecRoomPath
$patchTargets = @(
    [pscustomobject]@{ Label = 'GameAssembly.dll'; Path = $paths.GameAssembly },
    [pscustomobject]@{ Label = 'global-metadata.dat'; Path = $paths.GlobalMetadata },
    [pscustomobject]@{ Label = 'resources.assets'; Path = $paths.Resources }
) | Where-Object { Test-Path -LiteralPath $_.Path -PathType Leaf }

if ($Revert) {
    $restored = 0
    foreach ($target in $patchTargets) {
        $backup = "$($target.Path).dorknet-appid-backup"
        if (-not (Test-Path -LiteralPath $backup -PathType Leaf)) {
            continue
        }
        Write-Step "Reverting $($target.Label) from $backup"
        if ($DryRun) {
            Write-Host "DRY RUN: would restore $($target.Path) from $backup"
            $restored++
            continue
        }
        Copy-Item -LiteralPath $backup -Destination $target.Path -Force
        Write-OK "$($target.Label) restored."
        $restored++
    }
    if ($restored -eq 0) {
        throw "No DorkNet AppId backups found under $($paths.Root)."
    }
    exit 0
}

if (-not $PhotonVoiceAppId) {
    $PhotonVoiceAppId = $PhotonAppId
}

if ($ImageSigningPublicKeyPath) {
    Write-Warn 'ImageSigningPublicKeyPath is accepted for installer compatibility, but this patcher currently only applies Photon AppId byte patches.'
}
if ($ServerIp -or $CertPath) {
    Write-Warn 'ServerIp/CertPath are accepted for legacy installer compatibility, but hosts/cert patching is not applied by this script.'
}
if ($DisableSignatureChecks) {
    Write-Warn 'DisableSignatureChecks is handled by DorkNet.ClientMod; no signature metadata bytepatch is applied.'
}

if ($patchTargets.Count -eq 0) {
    throw "No patchable client files found under $($paths.Root)."
}

$anyChanged = $false
$anyFound = $false
foreach ($target in $patchTargets) {
    Write-Step "Patching Photon AppIds in $($target.Path)"
    $bytes = [System.IO.File]::ReadAllBytes($target.Path)
    $fileChanged = $false

    $realtimeResult = Apply-AsciiStringPatch -Bytes $bytes -Original $OfficialPhotonAppId -Replacement $PhotonAppId.ToLowerInvariant() -Label "$($target.Label) Photon Realtime AppId"
    if ($null -ne $realtimeResult) {
        $anyFound = $true
        $fileChanged = $realtimeResult -or $fileChanged
    }

    $voiceResult = Apply-AsciiStringPatch -Bytes $bytes -Original $OfficialPhotonVoiceAppId -Replacement $PhotonVoiceAppId.ToLowerInvariant() -Label "$($target.Label) Photon Voice AppId"
    if ($null -ne $voiceResult) {
        $anyFound = $true
        $fileChanged = $voiceResult -or $fileChanged
    }

    if (-not $fileChanged) {
        Write-OK "No $($target.Label) changes needed."
        continue
    }

    $anyChanged = $true
    $backup = "$($target.Path).dorknet-appid-backup"
    if ($DryRun) {
        Write-Host "DRY RUN: would write patched $($target.Label)"
        if (-not (Test-Path -LiteralPath $backup -PathType Leaf)) {
            Write-Host "DRY RUN: would create backup $backup"
        }
        continue
    }

    if (-not (Test-Path -LiteralPath $backup -PathType Leaf)) {
        Copy-Item -LiteralPath $target.Path -Destination $backup
        Write-OK "Backup written to $backup"
    }

    [System.IO.File]::WriteAllBytes($target.Path, $bytes)
    Write-OK "$($target.Label) Photon AppIds patched."
}

if (-not $anyFound) {
    throw "Official Photon AppIds were not found in any patch target, and requested replacements were not already present."
}

if (-not $anyChanged) {
    Write-OK 'No Photon AppId changes needed.'
}
