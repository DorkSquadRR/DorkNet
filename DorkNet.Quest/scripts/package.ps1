<#
.SYNOPSIS
  Bundle a patched Quest APK and its OBB into one installable package.

.DESCRIPTION
  Writes a release folder holding:
    DorkNet-<ver>.xapk           standard XAPK (manifest.json + APK + OBB + icon)
    install.cmd / install.ps1    one-command installer for Windows
    install.sh                   one-command installer for macOS / Linux
    README.txt                   what to do with it

  The XAPK is the usual APKPure layout, so XAPK-aware installers (SideQuest's
  file drop, SAI, APKPure) can also open it. The OBB is stored uncompressed:
  it is already compressed, and storing it keeps packaging and install fast.

  The output contains game binaries from your own store copy. Share it only
  with people who own the game.

.PARAMETER Apk
  The signed, patched APK produced by build.ps1.

.PARAMETER Obb
  The store OBB matching the APK's versionCode (main.<code>.com.AgainstGravity.RecRoom.obb).

.PARAMETER OutDir
  Release folder to create. Default: <APK folder>\DorkNet-Quest-<ver>.

.PARAMETER BuildToolsDir
  Android SDK build-tools dir holding aapt2. Default: resolved from $env:ANDROID_SDK_ROOT.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [Parameter(Mandatory = $true)][string]$Obb,
    [string]$OutDir,
    [string]$BuildToolsDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Need($path, $what) { if (-not (Test-Path -LiteralPath $path)) { throw "$what not found: $path" } }

Need $Apk 'patched APK'
Need $Obb 'OBB'
$Apk = (Resolve-Path -LiteralPath $Apk).Path
$Obb = (Resolve-Path -LiteralPath $Obb).Path

if (-not $BuildToolsDir) {
    if (-not $env:ANDROID_SDK_ROOT) { throw "Pass -BuildToolsDir or set ANDROID_SDK_ROOT." }
    $bt = Join-Path $env:ANDROID_SDK_ROOT 'build-tools'
    $BuildToolsDir = (Get-ChildItem $bt -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
}
$Aapt2 = Join-Path $BuildToolsDir 'aapt2.exe'
Need $Aapt2 'aapt2'

# ---- read the APK's identity --------------------------------------------
Step 'reading APK metadata'
$badging = & $Aapt2 dump badging $Apk 2>$null
if ($LASTEXITCODE) { throw 'aapt2 dump badging failed' }
function Badge($pattern) { foreach ($l in $badging) { if ($l -match $pattern) { return $Matches[1] } } return $null }
$pkg      = Badge "^package: name='([^']+)'"
$verCode  = Badge "versionCode='([^']+)'"
$verName  = Badge "versionName='([^']+)'"
$minSdk   = Badge "^(?:minSdkVersion|sdkVersion):'([^']+)'"
$tgtSdk   = Badge "^targetSdkVersion:'([^']+)'"
$label    = Badge "^application-label:'([^']*)'"
if (-not $pkg -or -not $verCode) { throw 'could not read package name / versionCode from the APK' }
$perms = @($badging | Where-Object { $_ -match "^uses-permission: name='([^']+)'" } | ForEach-Object { $_ -replace "^uses-permission: name='([^']+)'.*", '$1' })
$iconRef = @($badging | Where-Object { $_ -match "^application-icon-\d+:'([^']+)'" } |
    ForEach-Object { [pscustomobject]@{ Density = [int]($_ -replace "^application-icon-(\d+):.*", '$1'); Path = ($_ -replace "^application-icon-\d+:'([^']+)'.*", '$1') } } |
    Sort-Object Density -Descending | Select-Object -First 1 | ForEach-Object { $_.Path })

# The launcher icon may be an adaptive-icon XML; XAPK installers want a PNG, so
# fall back to the same-named PNG from the densest mipmap/drawable folder.
$iconPath = $null
if ($iconRef) {
    $src = [System.IO.Compression.ZipFile]::OpenRead($Apk)
    try {
        if ($iconRef -like '*.png' -and $src.GetEntry($iconRef)) { $iconPath = $iconRef }
        else {
            $base = [System.IO.Path]::GetFileNameWithoutExtension($iconRef)
            foreach ($d in 'xxxhdpi', 'xxhdpi', 'xhdpi', 'hdpi', 'mdpi', 'ldpi') {
                foreach ($kind in 'mipmap', 'drawable') {
                    if (-not $iconPath -and $src.GetEntry("res/$kind-$d/$base.png")) { $iconPath = "res/$kind-$d/$base.png" }
                }
            }
        }
    } finally { $src.Dispose() }
}
Write-Host "  $pkg  version $verName ($verCode)"

$obbName = "main.$verCode.$pkg.obb"
$obbEntry = "Android/obb/$pkg/$obbName"
$apkEntry = "$pkg.apk"

if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $Apk) "DorkNet-Quest-$verName" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$xapk = Join-Path $OutDir "DorkNet-$verName.xapk"
if (Test-Path $xapk) { Remove-Item -Force $xapk }

# ---- manifest.json -------------------------------------------------------
$manifest = [ordered]@{
    xapk_version       = 2
    package_name       = $pkg
    name               = if ($label) { $label } else { 'DorkNet' }
    version_code       = $verCode
    version_name       = $verName
    min_sdk_version    = $minSdk
    target_sdk_version = $tgtSdk
    permissions        = $perms
    total_size         = (Get-Item $Apk).Length + (Get-Item $Obb).Length
    expansions         = @([ordered]@{ file = $obbEntry; install_location = 'EXTERNAL_STORAGE'; install_path = $obbEntry })
}
if ($iconPath) { $manifest.icon = 'icon.png' }

# ---- write the XAPK ------------------------------------------------------
Step "writing $xapk (the OBB is large; this takes a minute)"
$zip = [System.IO.Compression.ZipFile]::Open($xapk, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $e = $zip.CreateEntry('manifest.json', [System.IO.Compression.CompressionLevel]::Optimal)
    $w = New-Object System.IO.StreamWriter($e.Open(), (New-Object System.Text.UTF8Encoding($false)))
    $w.Write(($manifest | ConvertTo-Json -Depth 5)); $w.Dispose()

    if ($iconPath) {
        $src = [System.IO.Compression.ZipFile]::OpenRead($Apk)
        try {
            $ie = $src.GetEntry($iconPath)
            if ($ie) {
                $dst = $zip.CreateEntry('icon.png', [System.IO.Compression.CompressionLevel]::NoCompression)
                $in = $ie.Open(); $out = $dst.Open(); $in.CopyTo($out); $out.Dispose(); $in.Dispose()
            }
        } finally { $src.Dispose() }
    }

    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $Apk, $apkEntry, [System.IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $Obb, $obbEntry, [System.IO.Compression.CompressionLevel]::NoCompression) | Out-Null
} finally { $zip.Dispose() }

# ---- installers + readme -------------------------------------------------
Step 'adding installers'
foreach ($f in 'install.ps1', 'install.cmd', 'install.sh') {
    Copy-Item (Join-Path $PSScriptRoot "installer\$f") (Join-Path $OutDir $f) -Force
}
$readme = @"
$($manifest.name) $verName - Rec Room for DorkNet (Meta Quest)
============================================================

1. Turn on developer mode for your headset (Meta Horizon phone app), plug it
   in over USB and accept "Allow USB debugging" inside the headset.
2. Install. Easiest: open the Quest page of your DorkNet server's website
   (https://<server>/quest) in Chrome or Edge, pick this .xapk and follow the
   steps. Or use the scripts here:
     Windows:        double-click install.cmd
     macOS / Linux:  ./install.sh
   The installer needs adb (Android platform-tools) on PATH or in
   ANDROID_SDK_ROOT. It installs the app, copies the game data (~1.4 GB) and
   offers to launch it.
3. In the headset: Library > Unknown Sources > $($manifest.name).

The app installs as $pkg alongside any store copy of Rec Room.
SideQuest users can also drop the .xapk onto SideQuest instead.
"@
Set-Content -Path (Join-Path $OutDir 'README.txt') -Value $readme -Encoding utf8

$sizeGb = '{0:N2}' -f ((Get-Item $xapk).Length / 1GB)
Write-Host ""
Write-Host "DONE. Package folder: $OutDir" -ForegroundColor Green
Write-Host "  $(Split-Path -Leaf $xapk)  ($sizeGb GB)" -ForegroundColor Green
Write-Host "Install with: $(Join-Path $OutDir 'install.cmd')" -ForegroundColor Green
