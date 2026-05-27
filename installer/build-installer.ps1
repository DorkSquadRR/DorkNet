# Build the DorkNet Windows installer.
#
# Usage:
#   pwsh -File installer\build-installer.ps1               # uses default 0.1.0
#   pwsh -File installer\build-installer.ps1 -Version 0.2.0
#
# Output:
#   installer\out\dorknet-setup-<version>.exe
#
# Requirements:
#   - .NET 9 SDK (for `dotnet publish`)
#   - Inno Setup 6 installed; ISCC must be on PATH or at the default
#     install location (C:\Program Files (x86)\Inno Setup 6\ISCC.exe).
#     Grab it free from https://jrsoftware.org/isinfo.php

[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $repo "launcher"
$installer = $PSScriptRoot

Write-Host "==> Publishing launcher (self-contained, single file)..." -ForegroundColor Cyan
& dotnet publish $launcher `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:Version=$Version `
    /p:AssemblyVersion="$Version.0" `
    /p:FileVersion="$Version.0" `
    /p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$publishDir = Join-Path $launcher "bin\$Configuration\net9.0-windows\win-x64\publish"
$exe = Join-Path $publishDir "dorknet.exe"
if (-not (Test-Path $exe)) { throw "Expected $exe not found after publish" }

$exeSize = "{0:N1} MB" -f ((Get-Item $exe).Length / 1MB)
Write-Host "==> Published: $exe ($exeSize)" -ForegroundColor Green

# Resolve ISCC.exe — try PATH first, then common machine/user install dirs.
$iscc = (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) {
    $iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $iscc)) { $iscc = "C:\Program Files\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) {
    throw "ISCC.exe not found. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php"
}

Write-Host "==> Compiling installer..." -ForegroundColor Cyan
$iss = Join-Path $installer "dorknet.iss"
$outDir = Join-Path $installer "out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& $iscc "/DAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$installerExe = Join-Path $outDir "dorknet-setup-$Version.exe"
if (-not (Test-Path $installerExe)) { throw "Installer not produced at $installerExe" }

$instSize = "{0:N1} MB" -f ((Get-Item $installerExe).Length / 1MB)
Write-Host "==> Built: $installerExe ($instSize)" -ForegroundColor Green
