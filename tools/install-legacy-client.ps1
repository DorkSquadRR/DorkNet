<#
.SYNOPSIS
  Switch a Rec Room 2020 client to DorkNet without BepInEx.

.DESCRIPTION
  This is the loader-free fallback path. It removes the BepInEx plugin
  install made by tools/install-plugin.ps1, then runs tools/patch-client.ps1
  in its legacy mode:

    - byte-patches Photon Realtime/Voice AppIds in resources.assets;
    - byte-patches the native BestHTTP/BouncyCastle TLS certificate
      verifier return in GameAssembly.dll;
    - points *.rec.net at the DorkNet server through the hosts file;
    - generates/uses a mkcert wildcard certificate for HTTPS.

  No BepInEx, Harmony, Doorstop, or IL2CPP managed plugin remains involved
  unless -KeepBepInEx is supplied.

  Photon identity note: the stock 2020 client does not send userid/LoginLock
  AuthValues. DorkNet's Photon custom-auth endpoint allows anonymous connects,
  but duplicate-login enforcement through Photon is unavailable in this mode.

.PARAMETER RecRoomPath
  Path to Recroom_Release_Data.

.PARAMETER PhotonAppId
  Photon Cloud Realtime AppId.

.PARAMETER PhotonVoiceAppId
  Photon Cloud Voice AppId. Defaults to PhotonAppId.

.PARAMETER ServerIp
  IP address to put in the hosts file for *.rec.net.

.PARAMETER CertPath
  Optional path for the mkcert wildcard certificate.

.PARAMETER KeepBepInEx
  Skip removing BepInEx before applying the legacy patch.

.PARAMETER DryRun
  Show what would happen without changing files.

.EXAMPLE
  .\tools\install-legacy-client.ps1 `
    -RecRoomPath "C:\Users\you\Documents\Recnet\dist\RecRoom-Clean-2020.03.10\Recroom_Release_Data" `
    -PhotonAppId "<your-photon-realtime-app-id>"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RecRoomPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('(?i)^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')]
    [string]$PhotonAppId,

    [ValidatePattern('(?i)^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')]
    [string]$PhotonVoiceAppId,

    [string]$ServerIp = '127.0.0.1',

    [string]$CertPath,

    [switch]$KeepBepInEx,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg) { Write-Host ('> ' + $msg) -ForegroundColor Cyan }
function Write-OK($msg)   { Write-Host ('OK: ' + $msg) -ForegroundColor Green }
function Write-Warn($msg) { Write-Host ('WARN: ' + $msg) -ForegroundColor Yellow }

$TlsBypassOffset = 0xB5EE50
$TlsBypassOriginalByte = 0x48
$TlsBypassPatchedByte = 0xC3

function Apply-TlsBypassPatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DataPath,

        [switch]$DryRun
    )

    $gameDir = Split-Path -Parent $DataPath
    $gameAssembly = Join-Path $gameDir 'GameAssembly.dll'
    if (-not (Test-Path -LiteralPath $gameAssembly -PathType Leaf)) {
        throw "GameAssembly.dll not found next to '$DataPath'."
    }

    Write-Step 'Applying native TLS verifier bypass'
    $bytes = [System.IO.File]::ReadAllBytes($gameAssembly)
    if ($bytes.Length -le $TlsBypassOffset) {
        throw "GameAssembly.dll is too small for TLS bypass offset 0x$($TlsBypassOffset.ToString('X')). Wrong client build?"
    }

    $current = $bytes[$TlsBypassOffset]
    if ($current -eq $TlsBypassPatchedByte) {
        Write-OK "TLS bypass already patched at GameAssembly.dll+0x$($TlsBypassOffset.ToString('X'))."
        return
    }

    if ($current -ne $TlsBypassOriginalByte) {
        $actual = '0x{0:X2}' -f $current
        $expected = '0x{0:X2}' -f $TlsBypassOriginalByte
        throw "Unexpected byte at GameAssembly.dll+0x$($TlsBypassOffset.ToString('X')): $actual, expected $expected. Refusing to patch an unknown build."
    }

    $backup = "$gameAssembly.tls-bypass-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    if ($DryRun) {
        Write-Host "DRY RUN: would backup GameAssembly.dll to $backup"
        Write-Host "DRY RUN: would patch GameAssembly.dll+0x$($TlsBypassOffset.ToString('X')) from 0x48 to 0xC3"
        return
    }

    Copy-Item -LiteralPath $gameAssembly -Destination $backup
    $bytes[$TlsBypassOffset] = [byte]$TlsBypassPatchedByte
    [System.IO.File]::WriteAllBytes($gameAssembly, $bytes)
    Write-OK "TLS bypass patched at GameAssembly.dll+0x$($TlsBypassOffset.ToString('X'))."
    Write-OK "Backup written to $backup"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$installPlugin = Join-Path $PSScriptRoot 'install-plugin.ps1'
$patchClient = Join-Path $PSScriptRoot 'patch-client.ps1'

if (-not (Test-Path -LiteralPath $installPlugin -PathType Leaf)) {
    throw "Missing $installPlugin"
}
if (-not (Test-Path -LiteralPath $patchClient -PathType Leaf)) {
    throw "Missing $patchClient"
}

$resolved = Resolve-Path -LiteralPath $RecRoomPath -ErrorAction SilentlyContinue
if (-not $resolved) {
    throw "RecRoomPath '$RecRoomPath' does not exist."
}
$RecRoomPath = $resolved.Path

if (-not $PhotonVoiceAppId) {
    $PhotonVoiceAppId = $PhotonAppId
}

Write-Host ''
Write-Step 'DorkNet legacy client install'
Write-Host ("  RecRoomPath : {0}" -f $RecRoomPath)
Write-Host ("  PhotonAppId : {0}" -f $PhotonAppId)
Write-Host ("  VoiceAppId  : {0}" -f $PhotonVoiceAppId)
Write-Host ("  ServerIp    : {0}" -f $ServerIp)
Write-Host ("  RepoRoot    : {0}" -f $repoRoot)
Write-Host ''

if (-not $KeepBepInEx) {
    Write-Step 'Removing BepInEx/plugin install'
    if ($DryRun) {
        Write-Host "DRY RUN: would run install-plugin.ps1 -Revert"
    } else {
        & $installPlugin -RecRoomPath $RecRoomPath -Revert
        if (-not $?) {
            throw "install-plugin.ps1 -Revert failed"
        }
    }
} else {
    Write-Warn 'Keeping BepInEx because -KeepBepInEx was supplied.'
}

Write-Step 'Applying legacy resources.assets/hosts/cert patch'
if ($CertPath -and $DryRun) {
    & $patchClient -RecRoomPath $RecRoomPath -PhotonAppId $PhotonAppId -PhotonVoiceAppId $PhotonVoiceAppId -ServerIp $ServerIp -CertPath $CertPath -DryRun
} elseif ($CertPath) {
    & $patchClient -RecRoomPath $RecRoomPath -PhotonAppId $PhotonAppId -PhotonVoiceAppId $PhotonVoiceAppId -ServerIp $ServerIp -CertPath $CertPath
} elseif ($DryRun) {
    & $patchClient -RecRoomPath $RecRoomPath -PhotonAppId $PhotonAppId -PhotonVoiceAppId $PhotonVoiceAppId -ServerIp $ServerIp -DryRun
} else {
    & $patchClient -RecRoomPath $RecRoomPath -PhotonAppId $PhotonAppId -PhotonVoiceAppId $PhotonVoiceAppId -ServerIp $ServerIp
}
if (-not $?) {
    throw "patch-client.ps1 failed"
}

Apply-TlsBypassPatch -DataPath $RecRoomPath -DryRun:$DryRun

Write-Host ''
Write-OK 'Legacy client path installed.'
Write-Warn 'For this mode, Photon Custom Authentication should allow anonymous connects; DorkNet already does this, but Photon dashboard strict rejection should not depend on userid/LoginLock.'
