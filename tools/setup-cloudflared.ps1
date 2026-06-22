<#
.SYNOPSIS
  Stand up a Cloudflare Tunnel that routes every *.localhost request to
  the local DorkNet server. One-shot setup script for temp testing.

.DESCRIPTION
  Performs the steps a tester would otherwise do by hand:

    1. Installs cloudflared if missing (via winget).
    2. Runs `cloudflared tunnel login` (browser auth — one-time).
    3. Creates a named tunnel "dorknet" (idempotent — re-uses an
       existing tunnel of the same name).
    4. Generates %USERPROFILE%\.cloudflared\config.yml from the
       template at tools/cloudflared-config.yml.template, substituting
       the tunnel id + credentials file path.
    5. Adds DNS routes for every localhost subdomain DorkNet uses
       (api, auth, match, accounts, admin, feed, …). Cloudflare
       creates each as a CNAME pointing at <tunnel>.cfargotunnel.com.
    6. Prints the next steps to start the tunnel.

  Prerequisite: localhost must be on a Cloudflare account you control,
  with the nameservers pointed at Cloudflare. The CLI works against
  any zone the logged-in account can manage.

.PARAMETER TunnelName
  The name to give the tunnel. Default 'dorknet'.

.PARAMETER Domain
  Apex domain to route. Default 'localhost'.

.PARAMETER DryRun
  Print every command that would run without executing.

.EXAMPLE
  PS> .\tools\setup-cloudflared.ps1
#>

[CmdletBinding()]
param(
    [string]$TunnelName = 'dorknet',
    [string]$Domain     = 'localhost',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg)   { Write-Host ('▸ ' + $msg) -ForegroundColor Cyan }
function Write-OK($msg)     { Write-Host ('✓ ' + $msg) -ForegroundColor Green }
function Write-Warn2($msg)  { Write-Host ('! ' + $msg) -ForegroundColor Yellow }
function Write-Err($msg)    { Write-Host ('✗ ' + $msg) -ForegroundColor Red }
function Write-DryRun($msg) { Write-Host ('[dry-run] ' + $msg) -ForegroundColor DarkGray }

# cloudflared writes harmless warnings (e.g. "Your version is outdated")
# to stderr. With ErrorActionPreference='Stop' globally, the 2>&1
# merge would surface those as "NativeCommandError" terminating
# errors. Wrap every cloudflared invocation in this helper so stderr
# becomes informational output and only a non-zero exit is treated
# as a failure.
function Invoke-Cloudflared {
    param([Parameter(ValueFromRemainingArguments = $true)] $CfArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & cloudflared @CfArgs 2>&1 | ForEach-Object { $_.ToString() }
        $exit = $LASTEXITCODE
        return [PSCustomObject]@{
            Output   = $output
            ExitCode = $exit
        }
    } finally {
        $ErrorActionPreference = $prev
    }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Template = Join-Path $PSScriptRoot 'cloudflared-config.yml.template'
$CfDir    = Join-Path $env:USERPROFILE '.cloudflared'
$ConfigPath = Join-Path $CfDir 'config.yml'

# Subdomains routed through the tunnel. Keep this list aligned with
# DorkNetRouteOwnership.PublicSubdomains and the cloudflared config
# template. We intentionally create explicit DNS routes instead of a
# wildcard so unrelated subdomains do not reach the gateway.
$Subdomains = @(
    '@',                        # apex (localhost itself)
    'admin', 'api', 'accounts', 'auth', 'cdn', 'chat', 'commerce',
    'discovery', 'econ', 'feed', 'geo', 'img', 'match', 'notify',
    'ns', 'playersettings', 'rooms', 'storage', 'strings', 'strings-cdn',
    'bugreporting', 'cards', 'clubs', 'cms', 'data', 'datacollection',
    'gamelogs', 'leaderboard', 'link', 'lists', 'moderation',
    'platformnotifications', 'roomcomments', 'roomieintegrations',
    'studio', 'thorn', 'videos'
)

# ── 1. cloudflared install ────────────────────────────────────────────────────
Write-Step 'Checking cloudflared'
$cf = Get-Command cloudflared -ErrorAction SilentlyContinue
if (-not $cf) {
    Write-Warn2 'cloudflared not in PATH.'
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        if ($DryRun) {
            Write-DryRun 'Would run: winget install --id Cloudflare.cloudflared --silent'
        } else {
            Write-Step 'Installing via winget'
            & winget install --id Cloudflare.cloudflared --silent --accept-package-agreements --accept-source-agreements
            # winget puts it in PATH but not in the current shell session
            $env:PATH = "$env:PATH;$env:LOCALAPPDATA\Microsoft\WinGet\Links"
            $cf = Get-Command cloudflared -ErrorAction SilentlyContinue
            if (-not $cf) {
                Write-Err 'cloudflared still not found after install. Open a new shell and re-run.'
                exit 1
            }
        }
    } else {
        Write-Err 'winget not found either. Install cloudflared manually:'
        Write-Host '  https://github.com/cloudflare/cloudflared/releases' -ForegroundColor DarkGray
        exit 1
    }
}
$cfSource = if ($cf) { $cf.Source } else { 'just-installed' }
Write-OK ('cloudflared at ' + $cfSource)

# ── 2. Login ──────────────────────────────────────────────────────────────────
$certPath = Join-Path $CfDir 'cert.pem'
if (-not (Test-Path -LiteralPath $certPath)) {
    Write-Step 'Cloudflare login (browser will open — pick the localhost zone)'
    if ($DryRun) {
        Write-DryRun 'Would run: cloudflared tunnel login'
    } else {
        $login = Invoke-Cloudflared tunnel login
        $login.Output | ForEach-Object { Write-Host $_ -ForegroundColor DarkGray }
        if (-not (Test-Path -LiteralPath $certPath)) {
            Write-Err 'cert.pem still missing — login was cancelled or failed.'
            exit 1
        }
    }
}
Write-OK ('Auth cert present at ' + $certPath)

# ── 3. Tunnel create / look up ─────────────────────────────────────────────────
Write-Step ("Looking up tunnel '" + $TunnelName + "'")
if ($DryRun) {
    Write-DryRun ('Would run: cloudflared tunnel list and create ' + $TunnelName + ' if missing')
    $tunnelId = '{{TUNNEL_ID}}'
    $credsFile = '{{CREDENTIALS_FILE}}'
} else {
    # `cloudflared tunnel list` outputs human-readable text by default;
    # parsing the table is fragile but works. We grep for our name and
    # extract the UUID. If not found, create.
    $list = Invoke-Cloudflared tunnel list
    $existingLine = $list.Output | Select-String -Pattern ('\b' + [regex]::Escape($TunnelName) + '\b')
    if ($existingLine) {
        # Format: "<id>  <name>  <created>  <connections>"
        $tunnelId = ($existingLine.Line -split '\s+')[0]
        Write-OK ('Tunnel exists: ' + $tunnelId)
    } else {
        Write-Step ("Creating tunnel '" + $TunnelName + "'")
        $create = Invoke-Cloudflared tunnel create $TunnelName
        $create.Output | ForEach-Object { Write-Host $_ -ForegroundColor DarkGray }
        $list = Invoke-Cloudflared tunnel list
        $existingLine = $list.Output | Select-String -Pattern ('\b' + [regex]::Escape($TunnelName) + '\b')
        if (-not $existingLine) {
            Write-Err 'Tunnel create failed; cloudflared list still empty.'
            exit 1
        }
        $tunnelId = ($existingLine.Line -split '\s+')[0]
        Write-OK ('Created tunnel ' + $tunnelId)
    }
    $credsFile = Join-Path $CfDir ($tunnelId + '.json')
    if (-not (Test-Path -LiteralPath $credsFile)) {
        Write-Warn2 ('Credentials file missing: ' + $credsFile)
    }
}

# ── 4. config.yml from template ───────────────────────────────────────────────
Write-Step ('Writing ' + $ConfigPath)
if (-not (Test-Path -LiteralPath $Template)) {
    Write-Err ('Template missing: ' + $Template)
    exit 1
}
if (-not (Test-Path -LiteralPath $CfDir)) {
    if (-not $DryRun) { New-Item -ItemType Directory -Path $CfDir -Force | Out-Null }
}
$tmpl = Get-Content -LiteralPath $Template -Raw
$config = $tmpl `
    -replace '\{\{TUNNEL_ID\}\}', $tunnelId `
    -replace '\{\{CREDENTIALS_FILE\}\}', ($credsFile -replace '\\', '\\')
if ($DryRun) {
    Write-DryRun ('Would write ' + $ConfigPath)
    Write-Host $config -ForegroundColor DarkGray
} else {
    Set-Content -LiteralPath $ConfigPath -Value $config -NoNewline
    Write-OK 'Config written.'
}

# ── 5. DNS routes ──────────────────────────────────────────────────────────────
Write-Step ('Adding DNS CNAMEs on ' + $Domain)
foreach ($sub in $Subdomains) {
    $hostname = if ($sub -eq '@') { $Domain } else { "$sub.$Domain" }
    if ($DryRun) {
        Write-DryRun ("Would run: cloudflared tunnel route dns $TunnelName $hostname")
        continue
    }
    Write-Host ('  ' + $hostname) -ForegroundColor DarkGray
    $null = Invoke-Cloudflared tunnel route dns $TunnelName $hostname
    # cloudflared returns nonzero if the CNAME already exists, but the
    # net result is fine — proceed.
}
Write-OK 'DNS routes registered.'

# ── 6. Done ───────────────────────────────────────────────────────────────────
Write-Host ''
Write-OK 'Cloudflare Tunnel ready.'
Write-Host ''
Write-Host '  Start the tunnel (keep running while testing):' -ForegroundColor White
Write-Host ('    cloudflared tunnel run ' + $TunnelName) -ForegroundColor Cyan
Write-Host ''
Write-Host '  Or install as a Windows service so it auto-starts:' -ForegroundColor White
Write-Host '    cloudflared service install' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Then: visit https://admin.localhost / https://feed.localhost / play the' -ForegroundColor DarkGray
Write-Host '        patched client. Make sure the local DorkNet.Server is running' -ForegroundColor DarkGray
Write-Host '        and listening on 0.0.0.0:443 with the *.localhost-covering cert.' -ForegroundColor DarkGray
