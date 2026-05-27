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

    Self-contained: downloads MelonLoader before the dotnet build and
    feeds its DLLs into the build as references, so no local Rec Room
    install is required to compile DorkNet.ClientMod. (The mod resolves
    every game-side type at runtime via AccessTools.TypeByName.)

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
    # ── 1. Fetch MelonLoader from upstream releases ──────────────────────
    # We download it first so the DLLs inside can serve as compile-time
    # references for DorkNet.ClientMod — keeps the build self-contained
    # (no local Rec Room install required to compile the mod).
    # x64 .NET 6 build targets 2020-era Rec Room. The asset name pattern
    # is stable across 0.6.x: MelonLoader.x64.zip.
    $mlAsset = "MelonLoader.x64.zip"
    $mlUrl = "https://github.com/LavaGang/MelonLoader/releases/download/$MelonLoaderVersion/$mlAsset"
    $mlZip = "$stage\MelonLoader.zip"
    $mlExtract = "$stage\melonloader-extracted"
    Write-Host "[fetch] downloading $mlUrl"
    Invoke-WebRequest -Uri $mlUrl -OutFile $mlZip
    if (-not (Test-Path $mlZip) -or (Get-Item $mlZip).Length -lt 1MB) {
        throw "MelonLoader download failed or produced an unexpectedly small file"
    }
    Expand-Archive -Path $mlZip -DestinationPath $mlExtract -Force
    # The 0.6.x release zip layout has a top-level `MelonLoader` folder
    # containing net6, net35, Dependencies, Documentation. Point the
    # csproj at that inner folder.
    $mlRefRoot = Join-Path $mlExtract 'MelonLoader'
    if (-not (Test-Path "$mlRefRoot\net6\MelonLoader.dll")) {
        throw "Extracted MelonLoader is missing net6\MelonLoader.dll — release layout may have changed"
    }

    # ── 2. Build DorkNet.ClientMod (no Rec Room install needed) ─────────
    Write-Host "[build] dotnet build DorkNet.ClientMod (Release)"
    dotnet build "$repoRoot\DorkNet.ClientMod\DorkNet.ClientMod.csproj" `
        -c Release --nologo `
        "-p:MelonLoaderDir=$mlRefRoot" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ClientMod build failed" }
    $dll = Get-ChildItem "$repoRoot\DorkNet.ClientMod\bin\Release" -Recurse `
        -Filter "DorkNet.ClientMod.dll" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $dll) { throw "DorkNet.ClientMod.dll not found post-build" }
    Copy-Item $dll.FullName "$stage\DorkNet.ClientMod.dll"
    Write-Host "[build] copied $($dll.FullName)"

    # ── 3. Copy the config template ──────────────────────────────────────
    $template = "$repoRoot\DorkNet.ClientMod\dorknet-clientmod.json.template"
    if (-not (Test-Path $template)) {
        throw "Config template missing: $template"
    }
    Copy-Item $template "$stage\dorknet-clientmod.json.template"

    # ── 4. Copy the Steam stub (Goldberg) + appid ────────────────────────
    # Replaces the Valve steam_api64.dll so the unwrapped exe can run
    # without Steam loaded. App ID 471710 is Rec Room — same across all
    # versions we ship for.
    $stubDll = "$repoRoot\DorkNet.ClientMod\steam-stub\steam_api64.dll"
    $stubAppid = "$repoRoot\DorkNet.ClientMod\steam-stub\steam_appid.txt"
    if (-not (Test-Path $stubDll)) {
        throw "Steam stub DLL missing: $stubDll"
    }
    if (-not (Test-Path $stubAppid)) {
        throw "Steam appid file missing: $stubAppid"
    }
    Copy-Item $stubDll "$stage\steam_api64.dll"
    Copy-Item $stubAppid "$stage\steam_appid.txt"

    # ── 5. Write manifest.json ──────────────────────────────────────────
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
        'steam_stub' = @{
            'api_dll' = 'steam_api64.dll'
            'api_dest' = 'Recroom_Release_Data/Plugins/steam_api64.dll'
            'api_backup_suffix' = '.steam-original'
            'appid_file' = 'steam_appid.txt'
            'appid_dest' = 'steam_appid.txt'
        }
    } | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath "$stage\manifest.json" -Value $manifest

    # ── 6. Bundle into the final zip ────────────────────────────────────
    # Strip the extracted MelonLoader scratch dir before zipping so it
    # doesn't bloat the artifact. The MelonLoader.zip itself stays.
    Remove-Item -Recurse -Force $mlExtract
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
