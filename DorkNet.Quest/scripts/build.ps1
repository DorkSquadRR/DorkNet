<#
.SYNOPSIS
  Build a DorkNet-patched Rec Room Quest APK from a pristine store APK.

.DESCRIPTION
  End to end:
    1. resolve_targets.py      -> per-build target table (RVA + fingerprint)
    2. gen_targets_header.py   -> targets_generated.h
    3. gen_config_header.py    -> config_generated.h (from your cfg)
    4. NDK clang++             -> libdorknet.so (arm64)
    5. apktool decode          -> working project
    6. package rename + manifest/version edits
    7. drop libdorknet.so in, DT_NEEDED it into libmain.so (LIEF)
    8. apktool build -> zipalign -> apksigner  -> the signed output APK

  Nothing game-derived is written into the repo. The work directory defaults to
  a temp path because Windows Controlled Folder Access blocks the NDK linker and
  apksigner from writing under Documents; override with -WorkDir if needed.

  You supply the store APK and (for install) the matching OBB. No game binaries
  ship with this tool.

.PARAMETER Apk
  Path to the pristine store APK (com.AgainstGravity.RecRoom, versionCode
  2128210 / version 20230317).

.PARAMETER Config
  dorknet-quest.cfg with your ServerHost / Photon ids / package name. Defaults
  to dorknet-quest.cfg next to the repo folder; falls back to the .example.

.PARAMETER WorkDir
  Scratch build directory (must be outside Controlled-Folder-Access-protected
  locations). Defaults to $env:TEMP\dorknet-quest-build.

.PARAMETER OutApk
  Where to write the finished APK. Default: <WorkDir>\dorknet-recroom-<ver>.apk.

.PARAMETER NdkRoot
  Android NDK root. Default: $env:ANDROID_NDK, else the NDK under $env:ANDROID_SDK_ROOT\ndk.

.PARAMETER BuildToolsDir
  Android SDK build-tools dir holding zipalign + apksigner. Default: resolved
  from $env:ANDROID_SDK_ROOT.

.PARAMETER Obb
  The store OBB matching the APK. When given, package.ps1 also bundles the
  result into an installable .xapk with one-command installers.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [string]$Config,
    [string]$WorkDir = (Join-Path $env:TEMP 'dorknet-quest-build'),
    [string]$OutApk,
    [string]$NdkRoot,
    [string]$BuildToolsDir,
    [string]$ApktoolJar,
    [string]$DumpCs,
    [string]$ApiLevel = '24',
    [string]$Obb
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoDir   = Split-Path -Parent $PSScriptRoot
$Scripts   = $PSScriptRoot
$OldPkg    = 'com.AgainstGravity.RecRoom'
$SoName    = 'libdorknet.so'

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Need($path, $what) { if (-not (Test-Path -LiteralPath $path)) { throw "$what not found: $path" } }

# ---- resolve tools ------------------------------------------------------
if (-not $NdkRoot) {
    $NdkRoot = $env:ANDROID_NDK
    if (-not $NdkRoot -and $env:ANDROID_SDK_ROOT) {
        $cand = Join-Path $env:ANDROID_SDK_ROOT 'ndk'
        if (Test-Path $cand) { $NdkRoot = (Get-ChildItem $cand -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName }
    }
}
if (-not $NdkRoot) { throw "Android NDK not found. Pass -NdkRoot or set ANDROID_NDK / ANDROID_SDK_ROOT." }
$Cxx = Join-Path $NdkRoot "toolchains\llvm\prebuilt\windows-x86_64\bin\aarch64-linux-android$ApiLevel-clang++.cmd"
$ObjDump = Join-Path $NdkRoot 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-objdump.exe'
Need $Cxx 'NDK aarch64 clang++'

if (-not $BuildToolsDir) {
    if (-not $env:ANDROID_SDK_ROOT) { throw "Pass -BuildToolsDir or set ANDROID_SDK_ROOT." }
    $bt = Join-Path $env:ANDROID_SDK_ROOT 'build-tools'
    $BuildToolsDir = (Get-ChildItem $bt -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
}
$ZipAlign  = Join-Path $BuildToolsDir 'zipalign.exe'
$ApkSigner = Join-Path $BuildToolsDir 'apksigner.bat'
Need $ZipAlign 'zipalign'
Need $ApkSigner 'apksigner'

if (-not $ApktoolJar) {
    $ApktoolJar = if ($env:APKTOOL_JAR) { $env:APKTOOL_JAR } elseif ($env:ANDROID_SDK_ROOT) { Join-Path $env:ANDROID_SDK_ROOT 'apktool.jar' } else { '' }
}
Need $ApktoolJar 'apktool.jar'
Need $Apk 'store APK'

if (-not $Config) {
    $c = Join-Path $RepoDir 'dorknet-quest.cfg'
    $Config = if (Test-Path $c) { $c } else { Join-Path $RepoDir 'dorknet-quest.cfg.example' }
}
Need $Config 'config'
Write-Host "Using config: $Config"

$PkgName = ((Get-Content $Config) | Where-Object { $_ -match '^\s*PackageName\s*=' } | Select-Object -First 1)
$PkgName = if ($PkgName) { ($PkgName -split '=', 2)[1].Trim() } else { 'net.dorknet.RecRoom' }
$AppName = ((Get-Content $Config) | Where-Object { $_ -match '^\s*AppName\s*=' } | Select-Object -First 1)
$AppName = if ($AppName) { ($AppName -split '=', 2)[1].Split('#')[0].Trim() } else { '' }
if (-not $AppName) { $AppName = 'DorkNet' }

# Resolve the launcher to its FULL exe path -- "& py ..." would otherwise match
# the (case-insensitive) function name and recurse. Prefer py -3.14, else python.
$PyCmd = (Get-Command py.exe -ErrorAction SilentlyContinue)
if ($PyCmd) { $PyExe = $PyCmd.Source; $PyPre = @('-3.14', '-I') }
else { $PyExe = (Get-Command python.exe -ErrorAction Stop).Source; $PyPre = @('-I') }
function Invoke-Py { param([Parameter(ValueFromRemainingArguments = $true)][string[]]$a)
    $pa = $PyPre + $a
    & $PyExe @pa
    if ($LASTEXITCODE) { throw "python failed: $($a -join ' ')" } }

# ---- work dirs ----------------------------------------------------------
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$Gen     = Join-Path $WorkDir 'gen'
$Project = Join-Path $WorkDir 'project'
$Extract = Join-Path $WorkDir 'extract'
New-Item -ItemType Directory -Force -Path $Gen, $Extract | Out-Null

# ---- 1. extract + dump + resolve targets --------------------------------
Step 'extracting libil2cpp.so + global-metadata.dat'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Apk))
try {
    foreach ($e in @('lib/arm64-v8a/libil2cpp.so', 'assets/bin/Data/Managed/Metadata/global-metadata.dat', 'lib/arm64-v8a/libmain.so')) {
        $entry = $zip.GetEntry($e); if (-not $entry) { throw "APK missing $e" }
        $dest = Join-Path $Extract (Split-Path $e -Leaf)
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
    }
} finally { $zip.Dispose() }
$Il2Cpp   = Join-Path $Extract 'libil2cpp.so'
$LibMain  = Join-Path $Extract 'libmain.so'

if (-not $DumpCs) {
    # Prefer a dump.cs the operator points at; otherwise require one. Dumping
    # is left to Il2CppDumper (see README) to keep this script toolchain-light.
    throw "Pass -DumpCs <dump.cs> from Il2CppDumper for this APK (see DorkNet.Quest/README.md)."
}
Need $DumpCs 'dump.cs'

Step 'resolving hook targets'
$verName = '20230317'; $verCode = '2128210'
# Write the resolved table into the work dir (Controlled Folder Access can
# block writes under Documents). The repo keeps a committed copy under targets/.
$table = Join-Path $WorkDir "$verName-$verCode.json"
Invoke-Py @((Join-Path $Scripts 'resolve_targets.py'), '--dump', $DumpCs, '--il2cpp', $Il2Cpp,
     '--version-name', $verName, '--version-code', $verCode, '--objdump', $ObjDump, '--out', $table)

Step 'generating headers'
Invoke-Py @((Join-Path $Scripts 'gen_targets_header.py'), $table, (Join-Path $Gen 'targets_generated.h'))
Invoke-Py @((Join-Path $Scripts 'gen_config_header.py'), $Config, $PkgName, (Join-Path $Gen 'config_generated.h'))

# ---- 4. compile libdorknet.so -------------------------------------------
Step 'compiling libdorknet.so (arm64)'
$src = @(
    (Join-Path $RepoDir 'src\dorknet_quest.cpp'),
    (Join-Path $RepoDir 'src\config.cpp'),
    (Join-Path $RepoDir 'src\third_party\And64InlineHook\And64InlineHook.cpp')
)
$So = Join-Path $WorkDir $SoName
& $Cxx '-std=c++17' '-fPIC' '-shared' '-O2' '-fvisibility=hidden' '-static-libstdc++' `
    '-ffunction-sections' '-fdata-sections' '-Wl,--gc-sections' `
    "-I$Gen" "-I$(Join-Path $RepoDir 'src')" $src '-llog' '-o' $So
if ($LASTEXITCODE) { throw 'compile failed' }
Need $So 'libdorknet.so'

# ---- 5. apktool decode --------------------------------------------------
Step 'apktool decode'
if (Test-Path $Project) { Remove-Item -Recurse -Force $Project }
& java -jar $ApktoolJar decode -f -o $Project (Resolve-Path $Apk)
if ($LASTEXITCODE) { throw 'apktool decode failed' }

# ---- 6. package rename + manifest -------------------------------------
Step "renaming package $OldPkg -> $PkgName"
$manifest = Join-Path $Project 'AndroidManifest.xml'
$m = Get-Content -Raw $manifest
$m = $m.Replace($OldPkg, $PkgName)
if ($m -notmatch 'android.intent.category.LAUNCHER') {
    $m = $m.Replace('<category android:name="android.intent.category.INFO"/>',
        '<category android:name="android.intent.category.INFO"/><category android:name="android.intent.category.LAUNCHER"/>')
}
Set-Content -NoNewline -Path $manifest -Value $m

# The launcher label comes from @string/app_name; set it in every locale.
Step "naming the app '$AppName'"
$labelXml = [System.Security.SecurityElement]::Escape($AppName).Replace("'", "\'")
foreach ($sx in Get-ChildItem (Join-Path $Project 'res') -Filter strings.xml -Recurse) {
    $s = Get-Content -Raw -Encoding utf8 $sx.FullName
    $n = [regex]::Replace($s, '<string name="app_name">[^<]*</string>', "<string name=`"app_name`">$labelXml</string>")
    if ($n -ne $s) { Set-Content -NoNewline -Encoding utf8 -Path $sx.FullName -Value $n }
}

# ---- 7. inject the hook library ----------------------------------------
Step 'injecting libdorknet.so'
Copy-Item $So (Join-Path $Project 'lib\arm64-v8a' $SoName) -Force
$projMain = Join-Path $Project 'lib\arm64-v8a\libmain.so'
Invoke-Py @((Join-Path $Scripts 'inject_needed.py'), $projMain, $SoName)

# ---- 8. rebuild + align + sign -----------------------------------------
Step 'apktool build'
$unsigned = Join-Path $WorkDir 'unsigned.apk'
& java -jar $ApktoolJar build -f -o $unsigned $Project
if ($LASTEXITCODE) { throw 'apktool build failed' }

$Keystore = Join-Path $WorkDir 'dorknet-debug.keystore'
if (-not (Test-Path $Keystore)) {
    Step 'generating a fresh debug keystore (kept in the work dir, never committed)'
    & keytool -genkeypair -v -keystore $Keystore -alias dorknet -keyalg RSA -keysize 2048 `
        -validity 10000 -storepass dorknet -keypass dorknet -dname 'CN=DorkNet Quest, O=DorkNet'
    if ($LASTEXITCODE) { throw 'keytool failed' }
}

Step 'zipalign'
$aligned = Join-Path $WorkDir 'aligned.apk'
& $ZipAlign -f -p 4 $unsigned $aligned
if ($LASTEXITCODE) { throw 'zipalign failed' }

if (-not $OutApk) { $OutApk = Join-Path $WorkDir "dorknet-recroom-$verName.apk" }
Step "apksigner -> $OutApk"
& $ApkSigner sign --ks $Keystore --ks-key-alias dorknet --ks-pass pass:dorknet --key-pass pass:dorknet `
    --v2-signing-enabled true --v3-signing-enabled true --out $OutApk $aligned
if ($LASTEXITCODE) { throw 'apksigner failed' }
& $ApkSigner verify --print-certs $OutApk | Out-Null

Write-Host ""
Write-Host "DONE. Patched APK: $OutApk" -ForegroundColor Green
Write-Host "Package: $PkgName   versionCode: $verCode" -ForegroundColor Green
if ($Obb) {
    Write-Host ""
    & (Join-Path $Scripts 'package.ps1') -Apk $OutApk -Obb $Obb -BuildToolsDir $BuildToolsDir
} else {
    Write-Host "Install it, then push the matching OBB to:" -ForegroundColor Green
    Write-Host "  /sdcard/Android/obb/$PkgName/main.$verCode.$PkgName.obb" -ForegroundColor Green
    Write-Host "(or pass -Obb to bundle both into an installable .xapk)" -ForegroundColor Green
}
