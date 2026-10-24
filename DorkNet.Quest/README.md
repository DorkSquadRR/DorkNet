# DorkNet.Quest

Sideload-patches the **Meta Quest** build of Rec Room **2023.03.21**
(versionName `20230317`, versionCode `2128210`) to connect to a DorkNet server.

It is the Quest counterpart of `DorkNet.ClientMod` (the PC MelonLoader mod) plus
`tools/patch-client.ps1` (the PC Photon byte-patch). Android/IL2CPP has no
MelonLoader, so everything is done in **one injected native library**
(`libdorknet.so`): it is added as a `DT_NEEDED` entry on `libmain.so` so the
loader maps it before Unity, then it installs arm64 inline hooks at addresses
resolved from the build's own `dump.cs`.

This build reports appVersion **`20230317`** — the *same* version the PC client
reports — so a Quest player patched with this tool shares rooms, presence and
Photon shards with PC DorkNet players with no server-side version change.

> This ships **no** Rec Room code or assets. You supply your own extracted store
> APK and OBB. It disables the client's certificate pinning and image-signature
> checks so a self-hosted server works; do not redistribute game binaries.

## What it does

| Hook | Effect | PC equivalent |
| --- | --- | --- |
| `HTTPManager.SendRequest` | rewrite `<svc>.rec.net` → `<svc>.<ServerHost>`, or into a `{service}` template such as `{service}-dev.dork.sh` for hyphen-style hosts (the name server supplies every other URL) | `ClientMod` UriPatches |
| Photon `AppSettings` factory | inject the operator's Realtime/Voice/Chat app ids and fixed region (`PhotonRegion`, must match the PC clients) | `tools/patch-client.ps1` |
| `PlatformSettings.get_AppID` / `get_MobileAppID` | return the operator's Meta app id (only when set) | — |
| `UnityTlsContext.VerifyCallback` + BouncyCastle `NotifyServerCertificate` | accept the self-hosted TLS chain | `ClientMod` TlsPatches |
| three certificate-pin verifiers | accept the chain (pins never match a self-hosted host) | `ClientMod` DisableTLSPinning |
| image RSA/SHA1 verify | accept Content-Signatures the server signs with its own key | PC ImageSigning |
| hile-warning / unknown-DLL / file-hash / ToxMod callbacks | neutralised so a re-signed, hook-loaded client is not warned/quit | `ClientMod` AntiTamper/ToxMod/FileHash |
| integrity report + quit funnels | **log only** — names any new rejection in logcat | — |
| Oculus `ovr_*` platform calls | (no-Meta build only) pass the entitlement / user-proof / logged-in-user requests Meta refuses for the renamed app (including the SDK's second check on the inner native message), answering the logged-in user with the placeholder user id `1` | — |
| title-screen auto-login (`TitleScreenFlowModel`) | a headset with no remembered account opens the **sign-in page** instead of auto-creating an account (`AutoCreateAccount = true` restores Rec Room's behaviour) | — |

### Two builds, selected by `OculusAppId`

- **Set** — the *Meta build*: Meta's real entitlement + login run against the
  app you registered; the platform hooks stay disarmed. Players need a Meta
  account, and your package + signing cert must be registered with that app.
- **Blank** — the *no-Meta-account build*: the platform path is steered past
  Meta's checks to Rec Room's own username/password screen. It authenticates
  nothing — every headset sends the placeholder platform user id `1`. DorkNet's
  server treats platform id `1` (and `0`/blank) as a non-identity and keys
  accounts by `deviceId` instead (`PlayerService.IsPlaceholderPlatformId`), so
  players are **not** merged into one account and are never offered each other's
  "remembered logins". **Never** add a server rule that accepts platform id `1`
  as an identity.

## Prerequisites

- **Android NDK** r27c-class (`ANDROID_NDK`, or `ndk/<ver>` under
  `ANDROID_SDK_ROOT`) — compiles the hook library against bionic.
- **Android SDK build-tools** (`zipalign`, `apksigner`) — `ANDROID_SDK_ROOT`.
- **apktool** `apktool.jar` (`APKTOOL_JAR`, or next to `ANDROID_SDK_ROOT`).
- **JDK** (`keytool` for the debug keystore; apksigner needs a JRE).
- **Python 3** with **LIEF** (`pip install lief`) — the `DT_NEEDED` injection.
- **[Il2CppDumper](https://github.com/Perfare/Il2CppDumper)** — to produce
  `dump.cs` for the target APK (see below).
- Your own **store APK** (`com.AgainstGravity.RecRoom`, versionCode `2128210`)
  and its matching **OBB**.

> On Windows with Controlled Folder Access on, the NDK linker and apksigner
> can't write under `Documents`. `build.py` builds in `%TEMP%` by default;
> keep the work dir outside protected folders.

## Build

```powershell
# 1. Dump the target build (Il2CppDumper), once, into a scratch dir:
dotnet Il2CppDumper.dll `
  <extracted>\lib\arm64-v8a\libil2cpp.so `
  <extracted>\assets\bin\Data\Managed\Metadata\global-metadata.dat `
  <scratch>\dump

# 2. Configure:
copy DorkNet.Quest\dorknet-quest.cfg.example DorkNet.Quest\dorknet-quest.cfg
#   edit ServerHost, Photon app ids, (optional) OculusAppId, PackageName

# 3. Build the signed APK (and, with --obb, the release package):
$env:ANDROID_SDK_ROOT = 'C:\path\to\android-sdk'
$env:ANDROID_NDK      = 'C:\path\to\android-sdk\ndk\27.2.12479018'
$env:APKTOOL_JAR      = 'C:\path\to\apktool.jar'
py -3 DorkNet.Quest\scripts\build.py build `
  --apk     C:\path\to\RecRoom-store-2128210.apk `
  --dump-cs <scratch>\dump\dump.cs `
  --obb     C:\path\to\main.2128210.com.AgainstGravity.RecRoom.obb `
  --keystore C:\path\to\dorknet-debug.keystore
# -> %TEMP%\dorknet-quest-build\dorknet-recroom-20230317.apk
# -> %TEMP%\dorknet-quest-build\DorkNet-Quest-20230317\   (with --obb)
```

The same command works on Linux and macOS (`python3 DorkNet.Quest/scripts/build.py build …`).

For a build whose target table is already committed under `targets/`, pass
`--targets-json DorkNet.Quest/targets/20230317-2128210.json` instead of
`--dump-cs`. No Il2CppDumper is needed then: the build checks the table
against the APK's `libil2cpp.so` and stops on a mismatch. This is what the
Jenkins job does (`ci/jenkins/README.md`).

With `--obb`, the build also writes a release folder ready to hand to players:

| File | Purpose |
| --- | --- |
| `DorkNet-20230317.xapk` | standard XAPK: `manifest.json`, the APK, the OBB at its install path, `icon.png` |
| `install.cmd` / `install.ps1` | one-command install on Windows (double-click `install.cmd`) |
| `install.sh` | one-command install on macOS / Linux |
| `README.txt` | player instructions |

To package an APK you already built, run `build.py package --apk <apk> --obb <obb>`.
The package contains game binaries from your store copy, so only share it with
people who own the game.

`build.py build` chains: resolve targets → generate headers → compile
`libdorknet.so` → apktool decode → package rename + LAUNCHER + manifest →
app name (`AppName`) → drop the library in + `DT_NEEDED` it onto `libmain.so` → apktool build →
zipalign → apksigner (v2+v3). Without `--keystore` it generates a fresh debug
keystore in the work dir on first run. **Keep that keystore** and pass it with
`--keystore` from then on — Meta ties tokens to app id + package
+ signing cert, so re-signing with a different key breaks any Meta registration.
It is never committed.

### Rebasing onto a different build

Obfuscated Assembly-CSharp names are re-randomised each release but shared with
the PC build of the same release, so the PC decompile is the oracle. For a new
build:

1. Dump it with Il2CppDumper.
2. Re-run `scripts/resolve_targets.py --dump dump.cs --il2cpp libil2cpp.so
   --version-name <v> --version-code <c> --objdump <ndk>/llvm-objdump` and
   confirm every target resolves, is ≥ 20 bytes, and has a real frame-setup
   prologue. Adjust `targets/spec.json` signatures if a name/shape changed
   (cross-check against the PC decompile first).
3. Commit the new `targets/<v>-<c>.json`.

The library refuses to arm any target whose 20-byte fingerprint does not match
the running `libil2cpp.so`, so a table can never be applied to the wrong build.

## Install

**From the browser (easiest).** Open the **Quest** page of your DorkNet site
(`https://<apex>/quest`) in Chrome or Edge on a computer, plug the headset in
over USB, pick the `.xapk`, click **Connect**, accept "Allow USB debugging" in
the headset, then **Install**. The page talks to the headset over WebUSB
(Tango, `@yume-chan/adb`): it installs the APK, copies the OBB to its install
path (skipped if an identical one is already there) and launches the app. The
`.xapk` never leaves the player's computer; the server hosts no game files.
Close SideQuest / Meta Quest Developer Hub, or run `adb kill-server`, first:
only one program can hold the headset's USB connection. Firefox, Safari and
phones don't support WebUSB.

**From a release folder**, one command does everything (finds adb and the headset,
installs the APK, copies the OBB to the right path, skips the OBB if it is
already there, and launches the app):

```powershell
.\install.cmd                   # Windows (or double-click it)
./install.sh                    # macOS / Linux
```

Both take an optional package path, `-Serial`/`--serial` for a specific headset
and `-NoLaunch`/`--no-launch`. SideQuest and other XAPK installers can open the
`.xapk` directly. The app shows up as `AppName` (default **DorkNet**) under
Library > Unknown Sources.

By hand:

```sh
PKG=net.dorknet.RecRoom      # your PackageName
VC=2128210
adb uninstall $PKG 2>/dev/null
adb install -r dorknet-recroom-20230317.apk
adb shell mkdir -p /sdcard/Android/obb/$PKG
adb push main.2128210.com.AgainstGravity.RecRoom.obb \
  /sdcard/Android/obb/$PKG/main.$VC.$PKG.obb
```

The on-device OBB filename **must** be `main.<versionCode>.<package>.obb` —
Unity rebuilds that exact path at runtime, so the source file's own name is
irrelevant, only the destination is.

## Check it on the headset

**Wear the headset** — off-head it sleeps within seconds and never finishes
init, which looks like a crash.

```sh
adb logcat -c
adb shell am start -n net.dorknet.RecRoom/com.unity3d.player.UnityPlayerActivity
adb logcat -d -s DORKNET
```

A healthy boot logs: `DorkNet.Quest hooks loaded`, `init: build 20230317/...`,
every `armed <target>`, then `intercepted https://ns.rec.net/ -> https://ns.<host>/`,
`unitytls verify: forcing 0x8 -> SUCCESS`, `[http<-] 200 ...`, and
`photon appsettings: RT=... Voice=... Chat=...`.

| Symptom | Likely cause |
| --- | --- |
| `REFUSING <target>: ... fingerprint mismatch` | wrong APK for this target table — rebuild from the matching store APK, or rebase |
| install fails `-124` / `NO_CERTIFICATES` | signing/align step — rebuild with `build.py`, don't hand-zip |
| boots to an empty world | OBB missing or named for the wrong versionCode |
| `[integrity] report reason=... NetworkCertificateMismatch` then a quit | TLS bypass off, or a pin hook didn't arm |
| `[http<-]` shows a Cloudflare challenge page | edge WAF blocked the request before your origin |
| multiplayer never connects | Photon app ids not set in the cfg |
| Quest and PC players never see each other's rooms | `PhotonRegion` differs from the PC ClientMod `PhotonCloudRegion` |
| "Unable to load Oculus user account" | old build (before the GetLoggedInUser bypass), or a Meta build whose app id the account isn't entitled to |
| game restarts while "creating a new account" | `AutoCreateAccount = true` on a server with sign-ups off — set it to `false` so the sign-in page opens |

## Layout

| Path | Purpose |
| --- | --- |
| `src/dorknet_quest.cpp` | all hooks + init |
| `src/config.h` / `config.cpp` | compile-time defaults + runtime cfg loader |
| `src/third_party/And64InlineHook/` | vendored arm64 inline hooker (MIT, © Rprop) |
| `targets/spec.json` | target signatures (class header + method), build-independent |
| `targets/<ver>-<code>.json` | resolved RVAs + fingerprints for one build |
| `scripts/resolve_targets.py` | dump.cs + libil2cpp.so → target table (+ prologue check) |
| `scripts/gen_targets_header.py` | target table → `targets_generated.h` |
| `scripts/gen_config_header.py` | `dorknet-quest.cfg` → `config_generated.h` |
| `scripts/inject_needed.py` | LIEF `DT_NEEDED` injection |
| `scripts/build.py` | `build`: store APK → signed patched APK (+ release package with `--obb`); `package`: patched APK + OBB → `.xapk` + installers |
| `scripts/installer/` | `install.ps1`, `install.cmd`, `install.sh` copied into each package |
| `dorknet-quest.cfg.example` | config template; copy to `dorknet-quest.cfg` (git-ignored) |

## Credits

- [RecFlare](https://github.com/recflare) by djdevin. Their Quest client
  showed the way this patcher is built: one native hook library loaded
  through a `DT_NEEDED` entry on `libmain.so`, and which client checks need
  hooking. They target a newer build (20230414), so every hook target here
  was re-derived from the 20230317 build; no RecFlare code is included.
- [And64InlineHook](https://github.com/Rprop/And64InlineHook) © Rprop (MIT) —
  vendored under `src/third_party/`.
- [Il2CppDumper](https://github.com/Perfare/Il2CppDumper), [LIEF](https://lief.re).
