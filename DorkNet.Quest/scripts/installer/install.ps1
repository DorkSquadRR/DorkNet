<#
.SYNOPSIS
  Install a DorkNet Rec Room .xapk onto a Meta Quest over adb.

.DESCRIPTION
  Installs the APK, copies the OBB to the path the game expects, then launches
  the app. Works in Windows PowerShell 5.1 and PowerShell 7. Run it with no
  arguments from the package folder, or double-click install.cmd.

.PARAMETER Xapk
  The package to install. Default: the only .xapk next to this script.

.PARAMETER Serial
  adb serial of the headset when more than one device is connected.

.PARAMETER NoLaunch
  Install only; don't start the app afterwards.
#>
[CmdletBinding()]
param(
    [string]$Xapk,
    [string]$Serial,
    [switch]$NoLaunch
)

# adb writes progress to stderr; under 'Stop' Windows PowerShell would treat
# that as a failure, so exit codes are checked by hand instead.
$ErrorActionPreference = 'Continue'
Set-StrictMode -Version 2
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Say($m)  { Write-Host "==> $m" -ForegroundColor Cyan }
function Fail($m) { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

# ---- find the package ----------------------------------------------------
if (-not $Xapk) {
    $found = @(Get-ChildItem -Path $PSScriptRoot -Filter *.xapk -File)
    if ($found.Count -eq 0) { Fail "no .xapk found next to $PSCommandPath; pass -Xapk <file>." }
    if ($found.Count -gt 1) { Fail "more than one .xapk here; pass -Xapk <file>." }
    $Xapk = $found[0].FullName
}
if (-not (Test-Path -LiteralPath $Xapk)) { Fail "package not found: $Xapk" }
$Xapk = (Resolve-Path -LiteralPath $Xapk).Path

# ---- find adb -----------------------------------------------------------
$adb = $null
$cmd = Get-Command adb -ErrorAction SilentlyContinue
if ($cmd) { $adb = $cmd.Source }
if (-not $adb) {
    $roots = @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'), $PSScriptRoot) | Where-Object { $_ }
    foreach ($r in $roots) {
        foreach ($c in @((Join-Path $r 'platform-tools\adb.exe'), (Join-Path $r 'adb.exe'))) {
            if (Test-Path $c) { $adb = $c; break }
        }
        if ($adb) { break }
    }
}
if (-not $adb) {
    Fail ("adb not found. Install Android platform-tools from " +
          "https://developer.android.com/tools/releases/platform-tools and put adb on PATH " +
          "(or unzip platform-tools next to this script).")
}

# ---- pick the headset ----------------------------------------------------
$lines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' }
$ready = @($lines | Where-Object { $_ -match "^(\S+)\s+device$" } | ForEach-Object { ($_ -split '\s+')[0] })
$unauth = @($lines | Where-Object { $_ -match '\sunauthorized$' })
if ($Serial) {
    if ($ready -notcontains $Serial) { Fail "device $Serial is not connected and authorised (adb devices)." }
} elseif ($ready.Count -eq 1) {
    $Serial = $ready[0]
} elseif ($ready.Count -gt 1) {
    Fail ("more than one device connected: " + ($ready -join ', ') + ". Pass -Serial <id>.")
} elseif ($unauth.Count -gt 0) {
    Fail 'the headset is connected but not authorised: put it on and accept "Allow USB debugging".'
} else {
    Fail 'no headset found. Enable developer mode, connect it over USB, and accept "Allow USB debugging" in the headset.'
}
function Adb { & $adb -s $Serial @args }
$model = (Adb shell getprop ro.product.model | Out-String).Trim()
Say "headset: $model ($Serial)"

# ---- read the package ----------------------------------------------------
$zip = [System.IO.Compression.ZipFile]::OpenRead($Xapk)
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("dorknet-install-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $me = $zip.GetEntry('manifest.json')
    if (-not $me) { Fail 'not an XAPK: manifest.json missing.' }
    $sr = New-Object System.IO.StreamReader($me.Open())
    $manifest = $sr.ReadToEnd() | ConvertFrom-Json
    $sr.Dispose()
    $pkg = $manifest.package_name
    Say "package: $($manifest.name)  $pkg  $($manifest.version_name) ($($manifest.version_code))"

    function Extract($entry) {
        $out = Join-Path $tmp ([System.IO.Path]::GetFileName($entry.FullName))
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $out, $true)
        return $out
    }

    # ---- APK -------------------------------------------------------------
    $apkEntry = $zip.GetEntry("$pkg.apk")
    if (-not $apkEntry) { $apkEntry = @($zip.Entries | Where-Object { $_.FullName -like '*.apk' })[0] }
    if (-not $apkEntry) { Fail 'no APK inside the package.' }
    Say 'installing the app'
    $apkFile = Extract $apkEntry
    $res = (Adb install -r $apkFile 2>&1 | Out-String)
    if ($res -match 'INSTALL_FAILED_UPDATE_INCOMPATIBLE|signatures do not match') {
        Write-Host "An older copy of $pkg is installed with a different signing key." -ForegroundColor Yellow
        Write-Host "It has to be removed first. Your account lives on the server, but local settings on the headset are lost." -ForegroundColor Yellow
        $ans = Read-Host 'Uninstall it and continue? [y/N]'
        if ($ans -notmatch '^(y|yes)$') { Fail 'cancelled.' }
        Adb uninstall $pkg | Out-Null
        $res = (Adb install -r $apkFile 2>&1 | Out-String)
    }
    if ($res -notmatch 'Success') { Fail ("app install failed:`n" + $res.Trim()) }
    Remove-Item -Force $apkFile

    # ---- OBB (expansion files) ------------------------------------------
    foreach ($x in @($manifest.expansions)) {
        if (-not $x) { continue }
        $entry = $zip.GetEntry($x.file)
        if (-not $entry) { Fail "package is missing $($x.file)." }
        $dest = '/sdcard/' + $x.install_path
        $have = (Adb shell "stat -c %s '$dest' 2>/dev/null" | Out-String).Trim()
        if ($have -eq [string]$entry.Length) { Say "game data already on the headset, skipping ($([System.IO.Path]::GetFileName($dest)))"; continue }
        Say ("copying game data ({0:N2} GB) - this takes a few minutes" -f ($entry.Length / 1GB))
        $f = Extract $entry
        Adb shell "mkdir -p '$([System.IO.Path]::GetDirectoryName($dest).Replace('\','/'))'" | Out-Null
        Adb push $f $dest
        if ($LASTEXITCODE) { Fail 'copying the game data failed (is there enough free space on the headset?).' }
        Remove-Item -Force $f
    }
} finally {
    $zip.Dispose()
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Installed. In the headset: Library > Unknown Sources > $($manifest.name)." -ForegroundColor Green
if (-not $NoLaunch) {
    Say 'launching (put the headset on)'
    Adb shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 2>&1 | Out-Null
}
