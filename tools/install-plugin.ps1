<#
.SYNOPSIS
  BepInEx-only setup for DorkNet ClientPatch.

.DESCRIPTION
  Replaces the legacy patch-client.ps1 (asset-byte-edit + hosts file +
  mkcert wildcard). Now everything lives in the
  DorkNet.ClientPatch BepInEx plugin: Photon AppId override, *.rec.net
  → user-configured-host URL rewrite, and Photon CustomAuth identity
  injection.

  After this script runs once successfully, all you need to do on a
  client version update is download the new build and re-run
  this script — it'll re-extract BepInEx and rebuild the plugin
  against the new game's interop assemblies. No more hardcoded
  resources.assets offsets, no more hosts file, no more wildcard
  cert.

.PARAMETER RecRoomPath
  Path to the Recroom_Release_Data folder (the one containing
  resources.assets). The patcher uses this to locate the actual
  install root (its parent directory).

.PARAMETER BepInExZip
  Optional path to a BepInEx 6.x IL2CPP zip. Only needed if you want
  to override the pinned build the script auto-downloads. On first
  install (or when -ForceBepInEx is set), the script fetches a
  pinned BepInEx build from builds.bepinex.dev and caches it under
  %LOCALAPPDATA%\DorkNet so repeated runs don't re-download. Pass
  this only if you need a specific (newer/older) build.

.PARAMETER ServerHost
  Domain that replaces *.rec.net in every URL the watch builds.
  Default "localhost" — set to your own apex if hosting elsewhere.

.PARAMETER PhotonAppId
  Photon Cloud Realtime AppId (GUID without curly braces).

.PARAMETER PhotonVoiceAppId
  Photon Cloud Voice AppId. Defaults to PhotonAppId.

.PARAMETER ForceBepInEx
  Re-extract BepInEx even if it's already installed. Use after a client
  update.

.PARAMETER Revert
  Remove BepInEx + the plugin from the client install. Doesn't
  touch the game's own files.

.EXAMPLE
  # First-time install — script auto-downloads BepInEx
  .\tools\install-plugin.ps1 `
    -RecRoomPath "C:\Program Files (x86)\Steam\steamapps\common\RecRoom\Recroom_Release_Data" `
    -PhotonAppId  <your-photon-realtime-app-id>

.EXAMPLE
  # Override the pinned BepInEx build with your own zip
  .\tools\install-plugin.ps1 `
    -RecRoomPath "C:\…\Recroom_Release_Data" `
    -BepInExZip   "C:\Downloads\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.NNN.zip" `
    -PhotonAppId  cb0880d9-…

.EXAMPLE
  # After a client update (interop changed) — re-run with
  # the same args, optionally adding -ForceBepInEx if BepInEx itself
  # got clobbered.
  .\tools\install-plugin.ps1 `
    -RecRoomPath "C:\…\Recroom_Release_Data" `
    -PhotonAppId cb0880d9-…

.EXAMPLE
  # Remove the plugin
  .\tools\install-plugin.ps1 -RecRoomPath D:\RecRoom\Recroom_Release_Data -Revert
#>

[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true)]
    [string]$RecRoomPath,

    [Parameter(ParameterSetName = 'Install')]
    [string]$BepInExZip,

    [Parameter(ParameterSetName = 'Install')]
    [string]$ServerHost = 'localhost',

    [Parameter(Mandatory = $true, ParameterSetName = 'Install')]
    [ValidatePattern('^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')]
    [string]$PhotonAppId,

    [Parameter(ParameterSetName = 'Install')]
    [ValidatePattern('^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')]
    [string]$PhotonVoiceAppId,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$ForceBepInEx,

    # Tell the plugin to fake the Steamworks SDK so the watch boots
    # without Steam.exe running. Pure C#-level Harmony patches inside
    # the plugin DLL — no replacement steam_api64.dll, no Goldberg /
    # SmartSteamEmu (which Defender flags as PUA). Default off so
    # Steam-launched runs behave normally.
    [Parameter(ParameterSetName = 'Install')]
    [switch]$FakeSteamApi,

    # SteamID64 the plugin should report from SteamUser.GetSteamID
    # when -FakeSteamApi is on. Empty = deterministic per-machine ID
    # derived from OS user + machine name. Pin an explicit value to
    # keep the same DorkNet account across machines.
    [Parameter(ParameterSetName = 'Install')]
    [string]$FakeSteamId,

    # Display name returned from SteamFriends.GetPersonaName when
    # -FakeSteamApi is on. Empty = OS user name.
    [Parameter(ParameterSetName = 'Install')]
    [string]$FakeAccountName,

    # RSA public modulus, base64 encoded, that replaces the client's
    # embedded image-signing key. Defaults to the public key derived
    # from DorkNet.Server/appsettings.Local.json when that file has
    # ImageSigning:PrivateKeyPem.
    [Parameter(ParameterSetName = 'Install')]
    [string]$ImageSigningPublicKeyBase64,

    # PEM private key to derive the image-signing public modulus from.
    # Use this when Dokploy/prod signs images with a key that differs
    # from appsettings.Local.json.
    [Parameter(ParameterSetName = 'Install')]
    [string]$ImageSigningPrivateKeyPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Revert')]
    [switch]$Revert
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Pinned BepInEx 6 IL2CPP win-x64 build. Auto-downloaded when neither
# BepInEx is already installed nor -BepInExZip is provided. Pin a
# specific build rather than 'latest' so a regression upstream
# doesn't silently land on tester machines.
$Script:BepInExUrl = 'https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip'
$Script:BepInExCacheName = 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755.zip'

# Pinned Steamless release. atom0s ships it as a single zip with a
# CLI exe inside. Cached alongside BepInEx under %LOCALAPPDATA%\DorkNet
# so a one-time download per machine handles both DRM-stripping and
# mod loading.
$Script:SteamlessUrl = 'https://github.com/atom0s/Steamless/releases/download/v3.1.0.5/Steamless.v3.1.0.5.-.by.atom0s.zip'
$Script:SteamlessCacheName = 'Steamless.v3.1.0.5.zip'

# 2020.12 image-signing public keys embedded in OHDHPENHDAP. The watch
# chooses p1 for non-localhost hosts and d1 for localhost, then expects
# Content-Signature: key-id=KEY:RSA:<p1|d1>.<domain>; data=<rsa-sha1>.
# DorkNet signs with its own private key, so stock clients must have both
# embedded moduli replaced with the server key's public modulus.
$Script:OriginalImageSigningP1Modulus = 'X07yXkxaaLcZ1wVXfkWjgFkkqdoLhDFm0GPODsF+Q47pSUlbLvtXGqStnyEJEIrQmgDiicAvCdGRq4lovr2l5sIPMaoyizsbVHBdwLUrCsji0RvSBnmvN+8KqQ8STnB4DP4pAsPilfD35def4WuX/xMCXB5+hQUVhv27HPV8Dj9XzHuJAijIM9UwDZmvUcECyiO4wv+TaZi2+ELBtaLCQR8Gm1ZPeDEwP62Ch6MJy0jx5pkvvD0KdF9Wye+3/Wx31Zn/Trdo9HL4sGFWPDM9H9kQhZd5wkTHuxpwGIIhlzIwvY2/pBGdZKP6fi1D2jROEmVkBDyhmYY9nO+s3/bndQ=='
$Script:OriginalImageSigningD1Modulus = 'htW8AsNuy5E+nBkukGpKGInje8YotC7yHekeuotwMzDiCdR5jU69H9MN9r+Dplp/g0Pz1cXNPD+PX/aGgtiXaKTBjPzTcedQCsnkDIA4V6Y5OKMbrM84x7VNUaFWC1GoCLOraTy5RA3jnMUa2bPX1CAOaRYAKbBd65T76b3GR+oI4LRuua6zxe8tbZ+RKGS9ktNuZonPcKrcAQNrEN2E+z0ig9ls+z0EE3H6ufl48N1ix53ROwTOG3DPNwqp+w+n5oE2KbdL2V3MgS6TD+C1wUaPJI1//8UEzJs6ItNoFuez5PaBr3y9P/CyKVyGQRzTS82A/8gDEvOdEC4ZKCbKQQ=='

# NOTE: Goldberg Steam Emu integration was tried and removed.
# Windows Defender flags every Steam emulator as PUA and
# quarantines mid-download. The "boot without Steam running"
# capability now lives entirely inside DorkNet.ClientPatch —
# Harmony patches on the Steamworks.NET C# wrappers
# (SteamAPI.Init, SteamUser.GetSteamID, GetAuthSessionTicket, …)
# so the watch's SteamPlatformManager.Initialize succeeds without
# Steam.exe running. Toggled via the -FakeSteamApi switch on this
# script (writes the [Steam] FakeSteamApi=true config the plugin
# reads at load).

function Write-Step($msg)  { Write-Host ('▸ ' + $msg) -ForegroundColor Cyan }
function Write-OK($msg)    { Write-Host ('✓ ' + $msg) -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host ('! ' + $msg) -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host ('✗ ' + $msg) -ForegroundColor Red }

function Find-AsciiOffsets {
    param([byte[]]$Bytes, [string]$Needle)

    $hits = New-Object System.Collections.Generic.List[int]
    if ([string]::IsNullOrEmpty($Needle)) { return @() }
    $pattern = [System.Text.Encoding]::ASCII.GetBytes($Needle)
    for ($i = 0; $i -le $Bytes.Length - $pattern.Length; $i++) {
        if ($Bytes[$i] -ne $pattern[0]) { continue }
        $ok = $true
        for ($j = 1; $j -lt $pattern.Length; $j++) {
            if ($Bytes[$i + $j] -ne $pattern[$j]) { $ok = $false; break }
        }
        if ($ok) { $hits.Add($i) }
    }
    return @($hits)
}

function Get-ImageSigningPublicModulus {
    param(
        [string]$RepoRoot,
        [string]$PublicKeyBase64,
        [string]$PrivateKeyPath
    )

    if ($PublicKeyBase64 -and $PrivateKeyPath) {
        Write-Err 'Use only one of -ImageSigningPublicKeyBase64 or -ImageSigningPrivateKeyPath.'
        exit 1
    }

    if ($PublicKeyBase64) { return $PublicKeyBase64.Trim() }

    $privatePem = $null
    if ($PrivateKeyPath) {
        $resolvedKey = Resolve-Path -LiteralPath $PrivateKeyPath -ErrorAction SilentlyContinue
        if (-not $resolvedKey) {
            Write-Err "Image signing private key '$PrivateKeyPath' not found."
            exit 1
        }
        $privatePem = Get-Content -LiteralPath $resolvedKey.Path -Raw
    } else {
        $localSettings = Join-Path $RepoRoot 'DorkNet.Server\appsettings.Local.json'
        if (Test-Path -LiteralPath $localSettings -PathType Leaf) {
            try {
                $json = Get-Content -LiteralPath $localSettings -Raw | ConvertFrom-Json
                if ($json.PSObject.Properties['ImageSigning'] -and
                    $json.ImageSigning.PSObject.Properties['PrivateKeyPem']) {
                    $privatePem = [string]$json.ImageSigning.PrivateKeyPem
                }
            } catch {
                Write-Warn2 "Could not read ImageSigning:PrivateKeyPem from $localSettings: $($_.Exception.Message)"
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($privatePem)) { return $null }

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem($privatePem)
        return [Convert]::ToBase64String($rsa.ExportParameters($false).Modulus)
    } catch {
        Write-Warn2 "Could not derive image-signing public key from configured private key: $($_.Exception.Message)"
        return $null
    } finally {
        $rsa.Dispose()
    }
}

function Invoke-ImageSigningPublicKeyPatch {
    param(
        [string]$DataDir,
        [string]$RecRoomRoot,
        [string]$PublicModulusBase64
    )

    if ([string]::IsNullOrWhiteSpace($PublicModulusBase64)) {
        Write-Warn2 'No image-signing public key configured; image Content-Signature verification may fail in-game.'
        return
    }
    $PublicModulusBase64 = $PublicModulusBase64.Trim()
    if ($PublicModulusBase64.Length -ne $Script:OriginalImageSigningP1Modulus.Length) {
        Write-Err "Image signing public modulus must be $($Script:OriginalImageSigningP1Modulus.Length) base64 chars for the in-place client patch."
        exit 1
    }

    $meta = Join-Path $DataDir 'il2cpp_data\Metadata\global-metadata.dat'
    $gameAssembly = Join-Path $RecRoomRoot 'GameAssembly.dll'
    $candidates = @($meta, $gameAssembly) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    if (-not $candidates) {
        Write-Warn2 'No IL2CPP metadata or GameAssembly file found for image-signing key patch.'
        return
    }

    $replacement = [System.Text.Encoding]::ASCII.GetBytes($PublicModulusBase64)
    $patchedFiles = 0
    $alreadyPatched = $false
    foreach ($path in $candidates) {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $desiredHits = @(Find-AsciiOffsets -Bytes $bytes -Needle $PublicModulusBase64)
        if ($desiredHits.Count -gt 0) {
            $alreadyPatched = $true
        }

        $offsets = New-Object System.Collections.Generic.List[int]
        foreach ($needle in @($Script:OriginalImageSigningP1Modulus, $Script:OriginalImageSigningD1Modulus)) {
            foreach ($hit in @(Find-AsciiOffsets -Bytes $bytes -Needle $needle)) {
                if (-not $offsets.Contains($hit)) { $offsets.Add($hit) }
            }
        }
        if ($offsets.Count -eq 0) { continue }

        Write-Step "Patching image signing public key in $path"
        if ($path -ne $meta) {
            $backup = "$path.dorknet-image-signing-original"
            if (-not (Test-Path -LiteralPath $backup -PathType Leaf)) {
                Copy-Item -LiteralPath $path -Destination $backup
            }
        }

        foreach ($offset in $offsets) {
            [Array]::Copy($replacement, 0, $bytes, $offset, $replacement.Length)
        }
        [System.IO.File]::WriteAllBytes($path, $bytes)
        $formatted = @($offsets | ForEach-Object { ('0x{0:x}' -f $_) }) -join ', '
        Write-OK "Image signing public key patched at $formatted"
        $patchedFiles++
    }

    if ($patchedFiles -eq 0) {
        if ($alreadyPatched) {
            Write-OK 'Image signing public key already matches requested key.'
        } else {
            Write-Warn2 'Original image signing key was not found. This client may already be patched or may not be the 2020.12 IL2CPP build.'
        }
    }
}

function Test-SteamDrmWrapped {
    # Detects the Steamworks DRM wrapper by checking whether the PE's
    # AddressOfEntryPoint falls inside a '.bind' section. Valve's
    # drm_wrap tool injects .bind carrying the DRM stub and rewrites
    # the entry point to land there; SteamStub jumps to the real
    # .text once it's done. After Steamless strips, the entry point
    # is rewritten back into .text and .bind is left as dead weight
    # (the section header survives but execution never visits it),
    # so we'd false-positive if we just looked at .bind's size.
    #
    # PE layout walked here (offsets relative to start of file):
    #   e_lfanew @ 0x3C → 4-byte offset to PE header
    #   PE+0  "PE\0\0" + COFF header (Sections @ +6, OptHeader size @ +20)
    #   OptionalHeader+0x10 = AddressOfEntryPoint (RVA)
    #   Section table starts at PE+24+OptHeaderSize, 40 bytes/entry:
    #     Name(8) VirtSize(4) VirtAddr(4) RawSize(4) RawPtr(4) ...
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
    param()
    $cacheDir   = Join-Path $env:LOCALAPPDATA 'DorkNet'
    $extractDir = Join-Path $cacheDir 'Steamless'
    $cli        = Join-Path $extractDir 'Steamless.CLI.exe'
    if (Test-Path -LiteralPath $cli -PathType Leaf) {
        return $cli
    }
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $zipPath = Join-Path $cacheDir $Script:SteamlessCacheName
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        Write-Step "Downloading Steamless → $zipPath"
        Write-Host "  $Script:SteamlessUrl" -ForegroundColor DarkGray
        try {
            Invoke-WebRequest -Uri $Script:SteamlessUrl -OutFile $zipPath `
                -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
        } catch {
            Write-Err "Steamless download failed: $($_.Exception.Message)"
            Write-Host '  Manually download from https://github.com/atom0s/Steamless/releases' -ForegroundColor DarkGray
            Write-Host "  and extract to $extractDir, then re-run." -ForegroundColor DarkGray
            throw
        }
    }
    Write-Step "Extracting Steamless → $extractDir"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
        # Some Steamless zips put the CLI inside a subfolder; locate it.
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
    # rest of the script doesn't have to know which is which.
    param([string]$ExePath)
    $cli = Get-Steamless
    Write-Step "Stripping Steam DRM from $ExePath"
    $exeDir = Split-Path -Parent $ExePath
    # Steamless emits <input>.exe.unpacked.exe by default. Run with
    # --quiet to keep our log clean; --keepbind 0 lets it strip the
    # .bind section so subsequent runs don't re-trigger the detector.
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
    Move-Item -LiteralPath $ExePath -Destination $bak -Force
    Move-Item -LiteralPath $unpacked -Destination $ExePath -Force
    Write-OK "DRM stripped. Original kept as $bak"
}

function Get-BepInExZip {
    # Returns a path to a usable BepInEx zip. Reuses a cached download
    # under %LOCALAPPDATA%\DorkNet so a repeated install on the same
    # box doesn't re-download the ~9 MB blob each run.
    param()
    $cacheDir = Join-Path $env:LOCALAPPDATA 'DorkNet'
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $cached = Join-Path $cacheDir $Script:BepInExCacheName
    if (Test-Path -LiteralPath $cached -PathType Leaf) {
        Write-OK "Using cached BepInEx zip → $cached"
        return $cached
    }
    Write-Step "Downloading BepInEx → $cached"
    Write-Host "  $Script:BepInExUrl" -ForegroundColor DarkGray
    try {
        # -UseBasicParsing for compatibility with Windows PowerShell 5.1
        # (PS7 ignores it). 90-second timeout — the file is small but a
        # cold builds.bepinex.dev cdn can be sluggish from some regions.
        Invoke-WebRequest -Uri $Script:BepInExUrl -OutFile $cached `
            -UseBasicParsing -TimeoutSec 90 -ErrorAction Stop
    } catch {
        Write-Err "Download failed: $($_.Exception.Message)"
        Write-Host '  Manually download the zip and pass it via -BepInExZip <path>.' -ForegroundColor DarkGray
        Write-Host "  Releases page: https://github.com/BepInEx/BepInEx/releases" -ForegroundColor DarkGray
        throw
    }
    $size = (Get-Item -LiteralPath $cached).Length
    Write-OK ("Downloaded {0:N1} MB" -f ($size / 1MB))
    return $cached
}

# Resolve via a two-step assignment instead of the ?. null-conditional
# operator — that's PS 7+ only and we want to support Windows
# PowerShell 5.1 (still the default on stock Win10/11).
$resolved = Resolve-Path -LiteralPath $RecRoomPath -ErrorAction SilentlyContinue
if ($resolved) { $RecRoomPath = $resolved.Path } else { $RecRoomPath = $null }
if (-not $RecRoomPath -or -not (Test-Path -LiteralPath $RecRoomPath -PathType Container)) {
    Write-Err "RecRoomPath '$RecRoomPath' does not exist."
    exit 1
}

# RecRoomPath = …\Recroom_Release_Data, BepInEx lives one level up
$RecRoomRoot = Split-Path -Parent $RecRoomPath
$BepInExDir  = Join-Path $RecRoomRoot 'BepInEx'
$ConfigDir   = Join-Path $BepInExDir  'config'
$PluginsDir  = Join-Path $BepInExDir  'plugins'
$InteropDir  = Join-Path $BepInExDir  'interop'
$RepoRoot    = Split-Path -Parent $PSScriptRoot
$PluginProj  = Join-Path $RepoRoot 'DorkNet.ClientPatch'

if ($Revert) {
    Write-Step "Removing BepInEx + plugin from $RecRoomRoot"
    if (Test-Path -LiteralPath $BepInExDir -PathType Container) {
        Remove-Item -LiteralPath $BepInExDir -Recurse -Force
        Write-OK "Removed $BepInExDir"
    }
    foreach ($leaf in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt')) {
        $p = Join-Path $RecRoomRoot $leaf
        if (Test-Path -LiteralPath $p -PathType Leaf) {
            Remove-Item -LiteralPath $p -Force
            Write-OK "Removed $leaf"
        }
    }
    # Restore the pristine global-metadata.dat from the backup we made
    # before host-patching, so the watch points at *.rec.net again.
    $meta   = Join-Path $RecRoomPath 'il2cpp_data\Metadata\global-metadata.dat'
    $backup = $meta + '.dorknet-original'
    if (Test-Path -LiteralPath $backup -PathType Leaf) {
        Copy-Item -LiteralPath $backup -Destination $meta -Force
        Remove-Item -LiteralPath $backup -Force
        Write-OK 'Restored stock global-metadata.dat from backup.'
    }
    Write-OK 'Plugin reverted. Rec Room is now stock.'
    exit 0
}

if (-not (Test-Path -LiteralPath $PluginProj -PathType Container)) {
    Write-Err "Plugin project missing at '$PluginProj'. Run from a DorkNet repo checkout."
    exit 1
}

# steam_appid.txt — Steamworks SDK uses this file as "app id is
# pre-configured, don't relaunch via Steam." Without it, even a
# DRM-stripped binary still calls SteamAPI.RestartAppIfNecessary
# at startup and gets handed back to Steam.exe before the .NET
# payload (and therefore our plugin) has a chance to run. Drop it
# next to the exe before we test the DRM-strip path.
$AppIdFile = Join-Path $RecRoomRoot 'steam_appid.txt'
if (-not (Test-Path -LiteralPath $AppIdFile -PathType Leaf)) {
    '471710' | Out-File -LiteralPath $AppIdFile -Encoding ASCII -NoNewline
    Write-OK "Wrote $AppIdFile (Rec Room AppID 471710 — skips Steam relaunch on boot)"
}

# DRM check + auto-strip — Steamworks DRM stub runs before BepInEx's
# winhttp.dll doorstop fires, so a stock Steam-wrapped exe would just
# exit and ask Steam to relaunch (BepInEx + plugin never get a chance).
# Detect via .bind entry-point check, then run Steamless against it.
# Original kept as Recroom_Release.drm-original.exe.
$RecRoomExe = Join-Path $RecRoomRoot 'Recroom_Release.exe'
if (Test-Path -LiteralPath $RecRoomExe -PathType Leaf) {
    if (Test-SteamDrmWrapped -ExePath $RecRoomExe) {
        Write-Step 'Steam DRM wrapper detected on Recroom_Release.exe — stripping with Steamless'
        Invoke-DrmStrip -ExePath $RecRoomExe
        # Sanity-check: if Steamless somehow left .bind in place, bail.
        if (Test-SteamDrmWrapped -ExePath $RecRoomExe) {
            Write-Err 'Stripped exe still reports a .bind section — Steamless may not support this build.'
            Write-Host '  Manually strip with the Steamless GUI and re-run this script.' -ForegroundColor DarkGray
            exit 1
        }
        Write-OK 'Recroom_Release.exe is now DRM-stripped.'
    } else {
        Write-OK 'Recroom_Release.exe is DRM-stripped (no .bind section).'
    }
}

# Steam-without-Steam.exe handling now lives inside the plugin via
# the [Steam] FakeSteamApi=true config — no DLL replacement, no
# Goldberg AV trip. We just write the config in step 4 below.

# ── 0b. Patch ns.rec.net / www.rec.net in IL2CPP global metadata ──────────
#    The watch hard-codes the bootstrap URL "https://ns.rec.net/?v=2" as a
#    string literal in IL2CPP global-metadata.dat. It builds the URL via
#    BestHTTP.HTTPRequest directly with the raw string — never goes through
#    `new System.Uri(string)` — so the plugin's URL-rewrite Harmony patch
#    can't see it. Same for the WWW link "www.rec.net". Both literals are
#    the same length as their localhost equivalents, so we can byte-replace
#    in place without shifting offsets.
#
#    This is the ONLY hardcoded-byte edit the install does, and it's
#    auto-detected & re-applied on every install run. The original file
#    is preserved as `global-metadata.dat.dorknet-original` so -Revert
#    can put it back.
function Invoke-MetadataHostPatch {
    param([string]$DataDir, [string]$ServerHost)
    $meta = Join-Path $DataDir 'il2cpp_data\Metadata\global-metadata.dat'
    if (-not (Test-Path -LiteralPath $meta -PathType Leaf)) {
        Write-Warn2 "global-metadata.dat not found at $meta — skipping host byte-patch."
        return
    }
    $backup = $meta + '.dorknet-original'
    if (-not (Test-Path -LiteralPath $backup -PathType Leaf)) {
        Copy-Item -LiteralPath $meta -Destination $backup
    }
    # Always patch from the pristine backup so re-runs are idempotent and
    # don't accidentally compound substitutions if the host changes.
    $bytes = [System.IO.File]::ReadAllBytes($backup)
    $subs = @(
        @{ Old = 'ns.rec.net';  New = "ns.$ServerHost"  },
        @{ Old = 'www.rec.net'; New = "www.$ServerHost" }
    )
    $totalReplacements = 0
    foreach ($s in $subs) {
        $oldB = [System.Text.Encoding]::ASCII.GetBytes($s.Old)
        $newB = [System.Text.Encoding]::ASCII.GetBytes($s.New)
        if ($oldB.Length -ne $newB.Length) {
            Write-Err "Host '$($s.Old)' (len $($oldB.Length)) and '$($s.New)' (len $($newB.Length)) differ in length — refusing to patch."
            return
        }
        $i = 0
        while ($i -le $bytes.Length - $oldB.Length) {
            $match = $true
            for ($k = 0; $k -lt $oldB.Length; $k++) {
                if ($bytes[$i + $k] -ne $oldB[$k]) { $match = $false; break }
            }
            if ($match) {
                [Array]::Copy($newB, 0, $bytes, $i, $newB.Length)
                $totalReplacements++
                $i += $newB.Length
            } else {
                $i++
            }
        }
    }
    [System.IO.File]::WriteAllBytes($meta, $bytes)
    Write-OK "global-metadata.dat host-patched ($totalReplacements substitution$( if ($totalReplacements -ne 1) { 's' }))."
}
Invoke-MetadataHostPatch -DataDir $RecRoomPath -ServerHost $ServerHost

# ── 1. BepInEx install ────────────────────────────────────────────────────
$bepInExPresent = Test-Path -LiteralPath $BepInExDir -PathType Container
if (-not $bepInExPresent -or $ForceBepInEx) {
    # Auto-fetch the pinned build when the operator didn't supply one.
    # First-run UX: just `install-plugin.ps1 -RecRoomPath ... -PhotonAppId ...`
    # works without the operator having to manually download a zip.
    if (-not $BepInExZip) {
        $BepInExZip = Get-BepInExZip
    }
    if (-not (Test-Path -LiteralPath $BepInExZip -PathType Leaf)) {
        Write-Err "BepInExZip '$BepInExZip' not found."
        exit 1
    }
    if ($ForceBepInEx -and $bepInExPresent) {
        Write-Step "Removing existing BepInEx install (-ForceBepInEx)"
        Remove-Item -LiteralPath $BepInExDir -Recurse -Force
    }
    Write-Step "Extracting BepInEx → $RecRoomRoot"
    Expand-Archive -LiteralPath $BepInExZip -DestinationPath $RecRoomRoot -Force
    Write-OK 'BepInEx extracted.'
} else {
    Write-OK "BepInEx already installed at $BepInExDir"
}

# ── 2. Interop assemblies ─────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $InteropDir -PathType Container)) {
    Write-Warn2 'Interop assemblies not found.'
    Write-Host '  Launch the client ONCE so BepInEx generates them, then re-run this script.' -ForegroundColor DarkGray
    Write-Host '  (One-time step per game version — takes ~30s on first launch.)' -ForegroundColor DarkGray
    exit 0
}
Write-OK "Interop assemblies present at $InteropDir"

# ── 3. Build plugin ───────────────────────────────────────────────────────
$env:BepInExInterop = $InteropDir
$env:BepInExCore    = Join-Path $BepInExDir 'core'
Write-Step 'Building DorkNet.ClientPatch'
$build = & dotnet build $PluginProj -c Release --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err 'Plugin build failed:'
    Write-Host ($build -join "`n") -ForegroundColor DarkGray
    exit 1
}
$pluginDll = Join-Path $PluginProj 'bin\Release\DorkNet.ClientPatch.dll'
if (-not (Test-Path -LiteralPath $pluginDll -PathType Leaf)) {
    Write-Err "Plugin DLL not produced at $pluginDll"
    exit 1
}
New-Item -ItemType Directory -Path $PluginsDir -Force | Out-Null
$oldPluginDll = Join-Path $PluginsDir ('DorkNet.' + 'Rec' + 'RoomPatch.dll')
if (Test-Path -LiteralPath $oldPluginDll -PathType Leaf) {
    Remove-Item -LiteralPath $oldPluginDll -Force
    Write-OK 'Removed old client patch plugin.'
}
Copy-Item -LiteralPath $pluginDll -Destination $PluginsDir -Force
Write-OK ('Plugin DLL → ' + (Join-Path $PluginsDir 'DorkNet.ClientPatch.dll'))

# ── 4. Plugin config ──────────────────────────────────────────────────────
# Write the BepInEx config file with the operator's chosen values.
# Format mirrors what the plugin's Config.Bind defaults produce on first
# run, so editing afterwards via BepInEx config manager works as expected.
$cfgFile = Join-Path $ConfigDir 'sh.dork.clientpatch.cfg'
New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null

$voiceId = if ($PhotonVoiceAppId) { $PhotonVoiceAppId } else { '' }
$cfg = @"
## Settings file was created by DorkNet install-plugin.ps1
## Plugin GUID: sh.dork.clientpatch

[Server]

## Domain that replaces *.rec.net in every URL the watch builds.
## Empty disables the rewrite (use only if pointing at the official servers).
# Setting type: String
# Default value: localhost
Host = $ServerHost

[Photon]

## Photon Cloud Realtime AppId (UUID, no curly braces).
## Empty leaves the AppId baked into the build untouched.
# Setting type: String
# Default value:
AppId = $PhotonAppId

## Photon Cloud Voice AppId. Empty falls back to AppId.
# Setting type: String
# Default value:
VoiceAppId = $voiceId

## Attach userid + LoginLock to PhotonNetwork.AuthValues right before
## OpAuthenticate so DorkNet's /photon/customauth endpoint can identify
## the player. Disable to debug pure Photon connectivity issues.
# Setting type: Boolean
# Default value: true
InjectAuthValues = true

[UI]

## Replacement branding string. Any UI label containing 'RecNet'
## gets that substring replaced with this value at runtime, so error
## dialogs and the loading screen show the private-server brand.
## Empty disables the rewrite.
# Setting type: String
# Default value: DorkNet
BrandName = DorkNet

[Steam]

## Bypass Steamworks.SteamAPI.RestartAppIfNecessary so the watch
## doesn't relaunch itself through Steam on boot. No effect when
## launched normally from the Steam library; useful when running the
## game directly via Recroom_Release.exe (BepInEx mod-loader shims
## often need this to avoid an infinite restart loop).
# Setting type: Boolean
# Default value: true
SkipRestartAppIfNecessary = true

## Pretend the Steam SDK initialised even when Steam.exe isn't
## running. Patches Steamworks.NET wrappers to return canned success
## values so the watch's SteamPlatformManager.Initialize succeeds
## without a live Steam process. DorkNet's auth backend uses SteamID
## as a lookup key only — no real ticket validation — so the fake
## ticket is accepted. Default off so launches via Steam itself
## behave normally.
# Setting type: Boolean
# Default value: false
FakeSteamApi = $($FakeSteamApi.IsPresent.ToString().ToLower())

## SteamID64 returned by SteamUser.GetSteamID when FakeSteamApi=true.
## Empty = deterministic value derived from OS user + machine name.
## Pin an explicit ID to keep the same DorkNet account across
## different machines.
# Setting type: String
# Default value:
FakeSteamId = $FakeSteamId

## Display name returned by SteamFriends.GetPersonaName when
## FakeSteamApi=true. Empty = OS user name.
# Setting type: String
# Default value:
FakeAccountName = $FakeAccountName
"@
$cfg | Out-File -LiteralPath $cfgFile -Encoding utf8 -Force
Write-OK "Plugin config → $cfgFile"

Write-Host ''
Write-OK 'All done. Launch the client from Steam.'
Write-Host '  Tail BepInEx/LogOutput.log for `[auth-injector] set Photon AuthValues …`' -ForegroundColor DarkGray
Write-Host '  to confirm the plugin loaded and ran.' -ForegroundColor DarkGray
