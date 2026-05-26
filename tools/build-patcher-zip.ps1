<#
.SYNOPSIS
    Builds the per-version dorknet-clientpatch-{VersionKey}.zip the
    DorkNet launcher expects to find as a GitHub Release asset.

.DESCRIPTION
    Output zip layout (see launcher/RELEASES.md on the public main
    branch for the full contract):

      manifest.json                       (schema v1, describes the install)
      MelonLoader.zip                     (downloaded from melonloader github)
      DorkNet.ClientMod.dll               (built from this repo)
      dorknet-clientmod.json.template     (copied from DorkNet.ClientMod/)

    The launcher reads manifest.json, unzips MelonLoader on top of the
    user's Rec Room install root, copies the DLL into Mods/, then
    renders the config template into UserData/.

.PARAMETER VersionKey
    The version key matching versions.json on the public main branch
    (e.g. december_2020_12_18 or march_2020_03_10).

.PARAMETER MelonLoaderVersion
    MelonLoader release tag to bundle. Defaults to the 0.6.x stable
    target — that's the loader DorkNet.ClientMod compiled against.

.PARAMETER OutputDir
    Where the final zip lands. Defaults to repo-root/dist.

.EXAMPLE
    .\tools\build-patcher-zip.ps1 -VersionKey march_2020_03_10
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$VersionKey,
    [string]$MelonLoaderVersion = "v0.6.6",
    [string]$OutputDir = $null
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path "$PSScriptRoot\.."
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'dist' }
New-Item -ItemType Directory -Force $OutputDir | Out-Null

$stage = Join-Path $env:TEMP "dorknet-patcher-$VersionKey-$([guid]::NewGuid().Guid.Substring(0,8))"
New-Item -ItemType Directory -Force $stage | Out-Null
try {
    # ── 1. Build DorkNet.ClientMod ───────────────────────────────────────
    Write-Host "[build] dotnet build DorkNet.ClientMod (Release)"
    dotnet build "$repoRoot\DorkNet.ClientMod\DorkNet.ClientMod.csproj" `
        -c Release --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ClientMod build failed" }
    $dll = Get-ChildItem "$repoRoot\DorkNet.ClientMod\bin\Release" -Recurse `
        -Filter "DorkNet.ClientMod.dll" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $dll) { throw "DorkNet.ClientMod.dll not found post-build" }
    Copy-Item $dll.FullName "$stage\DorkNet.ClientMod.dll"
    Write-Host "[build] copied $($dll.FullName)"

    # ── 2. Fetch MelonLoader from upstream releases ──────────────────────
    # The x64 .NET 6 build is what targets 2020-era Rec Room. The asset
    # name pattern is stable across 0.6.x: MelonLoader.x64.zip.
    $mlAsset = "MelonLoader.x64.zip"
    $mlUrl = "https://github.com/LavaGang/MelonLoader/releases/download/$MelonLoaderVersion/$mlAsset"
    $mlOut = "$stage\MelonLoader.zip"
    Write-Host "[build] downloading $mlUrl"
    Invoke-WebRequest -Uri $mlUrl -OutFile $mlOut
    if (-not (Test-Path $mlOut) -or (Get-Item $mlOut).Length -lt 1MB) {
        throw "MelonLoader download failed or produced an unexpectedly small file"
    }

    # ── 3. Copy the config template ──────────────────────────────────────
    $template = "$repoRoot\DorkNet.ClientMod\dorknet-clientmod.json.template"
    if (-not (Test-Path $template)) {
        throw "Config template missing: $template"
    }
    Copy-Item $template "$stage\dorknet-clientmod.json.template"

    # ── 4. Write manifest.json ──────────────────────────────────────────
    $manifest = @{
        '$schema_version' = 1
        'loader_archive' = 'MelonLoader.zip'
        'plugin_dll' = 'DorkNet.ClientMod.dll'
        'plugin_dest' = 'MelonLoader/Mods'
        'config_template' = 'dorknet-clientmod.json.template'
        'config_dest' = 'MelonLoader/UserData/dorknet-clientmod.json'
        'old_plugin_paths' = @(
            'BepInEx/plugins/DorkNet.ClientPatch.dll'
        )
    } | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath "$stage\manifest.json" -Value $manifest

    # ── 5. Bundle into the final zip ────────────────────────────────────
    $outZip = Join-Path $OutputDir "dorknet-clientpatch-$VersionKey.zip"
    if (Test-Path $outZip) { Remove-Item $outZip -Force }
    Compress-Archive -Path "$stage\*" -DestinationPath $outZip
    $sizeMb = [math]::Round((Get-Item $outZip).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Built $outZip ($sizeMb MB)" -ForegroundColor Green
    Write-Host "  Upload this to the v1-* GitHub Release on this branch."
}
finally {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
}
