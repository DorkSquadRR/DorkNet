#!/usr/bin/env python3
"""Build a DorkNet-patched Rec Room Quest APK (and optionally an .xapk release).

From a pristine store APK, end to end:

  1. hook targets   resolve them from an Il2CppDumper dump.cs
                    (resolve_targets.py), or use a committed table checked
                    against the APK's libil2cpp.so
  2. headers        targets_generated.h + config_generated.h
  3. compile        libdorknet.so (arm64) with the Android NDK
  4. apktool decode
  5. rename         package rename, launcher category, app label
  6. inject         libdorknet.so in, DT_NEEDED into libmain.so (LIEF)
  7. sign           apktool build -> zipalign -> apksigner
  8. package        with --obb: .xapk + one-command installers (or run
                    `package` on an APK you already built)

Nothing game-derived is written into the repo. You supply the store APK and,
for a release package, the matching OBB. The output contains game binaries
from your own store copy: share it only with people who own the game.

Runs on Windows, Linux and macOS. Needs Python 3.9+ with LIEF, a JDK (java,
keytool), the Android NDK, Android build-tools and apktool.

    py -3 build.py build --apk recroom.apk --obb main.2128210.com.AgainstGravity.RecRoom.obb \\
        --targets-json ../targets/20230317-2128210.json --keystore dorknet.keystore
    py -3 build.py package --apk dorknet-recroom-20230317.apk --obb main.2128210....obb
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape as xml_escape

SCRIPTS = Path(__file__).resolve().parent
REPO = SCRIPTS.parent  # DorkNet.Quest/
OLD_PKG = "com.AgainstGravity.RecRoom"
SO_NAME = "libdorknet.so"
ON_WINDOWS = os.name == "nt"


def step(msg: str) -> None:
    print(f"==> {msg}", flush=True)


def fail(msg: str) -> None:
    raise SystemExit(f"error: {msg}")


def need(path: Path | str, what: str) -> Path:
    p = Path(path)
    if not p.exists():
        fail(f"{what} not found: {p}")
    return p


def run(*cmd: object, cwd: Path | None = None) -> None:
    args = [str(c) for c in cmd]
    r = subprocess.run(args, cwd=cwd)
    if r.returncode:
        fail(f"{Path(args[0]).name} failed (exit {r.returncode})")


def capture(*cmd: object) -> str:
    args = [str(c) for c in cmd]
    r = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode:
        sys.stderr.write(r.stderr)
        fail(f"{Path(args[0]).name} failed (exit {r.returncode})")
    return r.stdout


def run_py(script: str, *args: object) -> None:
    """Run a sibling helper script with this interpreter (isolated mode)."""
    run(sys.executable, "-I", SCRIPTS / script, *args)


def exe(name: str) -> str:
    return name + (".exe" if ON_WINDOWS else "")


# ---- tool resolution ------------------------------------------------------

def sdk_root() -> Path | None:
    v = os.environ.get("ANDROID_SDK_ROOT") or os.environ.get("ANDROID_HOME")
    return Path(v) if v else None


def newest_subdir(parent: Path) -> Path | None:
    if not parent.is_dir():
        return None
    dirs = sorted((d for d in parent.iterdir() if d.is_dir()), key=lambda d: d.name, reverse=True)
    return dirs[0] if dirs else None


def resolve_build_tools(arg: str | None) -> Path:
    if arg:
        return need(arg, "build-tools dir")
    root = sdk_root()
    found = newest_subdir(root / "build-tools") if root else None
    if not found:
        fail("Android build-tools not found. Pass --build-tools-dir or set ANDROID_SDK_ROOT.")
    return found


def resolve_ndk(arg: str | None) -> Path:
    if arg:
        return need(arg, "NDK")
    if os.environ.get("ANDROID_NDK"):
        return need(os.environ["ANDROID_NDK"], "NDK (ANDROID_NDK)")
    root = sdk_root()
    found = newest_subdir(root / "ndk") if root else None
    if not found:
        fail("Android NDK not found. Pass --ndk-root or set ANDROID_NDK / ANDROID_SDK_ROOT.")
    return found


def ndk_bin(ndk: Path) -> Path:
    if ON_WINDOWS:
        tag = "windows-x86_64"
    elif sys.platform == "darwin":
        tag = "darwin-x86_64"
    else:
        tag = "linux-x86_64"
    return ndk / "toolchains" / "llvm" / "prebuilt" / tag / "bin"


def resolve_apktool(arg: str | None) -> Path:
    if arg:
        return need(arg, "apktool.jar")
    if os.environ.get("APKTOOL_JAR"):
        return need(os.environ["APKTOOL_JAR"], "apktool.jar (APKTOOL_JAR)")
    root = sdk_root()
    if root and (root / "apktool.jar").exists():
        return root / "apktool.jar"
    fail("apktool.jar not found. Pass --apktool-jar or set APKTOOL_JAR.")


def cfg_value(config: Path, key: str) -> str | None:
    for line in config.read_text(encoding="utf-8").splitlines():
        m = re.match(rf"^\s*{re.escape(key)}\s*=(.*)$", line)
        if m:
            return m.group(1).split("#", 1)[0].strip()
    return None


# ---- build ----------------------------------------------------------------

def cmd_build(a: argparse.Namespace) -> None:
    ndk = resolve_ndk(a.ndk_root)
    bin_dir = ndk_bin(ndk)
    cxx = need(bin_dir / (f"aarch64-linux-android{a.api_level}-clang++" + (".cmd" if ON_WINDOWS else "")),
               "NDK aarch64 clang++")
    objdump = bin_dir / exe("llvm-objdump")
    build_tools = resolve_build_tools(a.build_tools_dir)
    zipalign = need(build_tools / exe("zipalign"), "zipalign")
    apksigner = need(build_tools / ("apksigner.bat" if ON_WINDOWS else "apksigner"), "apksigner")
    apktool = resolve_apktool(a.apktool_jar)
    apk = need(a.apk, "store APK").resolve()

    config = Path(a.config) if a.config else (
        REPO / "dorknet-quest.cfg" if (REPO / "dorknet-quest.cfg").exists() else REPO / "dorknet-quest.cfg.example")
    need(config, "config")
    print(f"Using config: {config}")
    pkg_name = cfg_value(config, "PackageName") or "net.dorknet.RecRoom"
    app_name = cfg_value(config, "AppName") or "DorkNet"

    work = Path(a.work_dir)
    gen, project, extract = work / "gen", work / "project", work / "extract"
    for d in (work, gen, extract):
        d.mkdir(parents=True, exist_ok=True)

    # 1. extract + targets
    step("extracting libil2cpp.so + global-metadata.dat")
    with zipfile.ZipFile(apk) as z:
        for entry in ("lib/arm64-v8a/libil2cpp.so",
                      "assets/bin/Data/Managed/Metadata/global-metadata.dat",
                      "lib/arm64-v8a/libmain.so"):
            try:
                data = z.read(entry)
            except KeyError:
                fail(f"APK missing {entry}")
            (extract / Path(entry).name).write_bytes(data)
    il2cpp = extract / "libil2cpp.so"

    ver_name, ver_code = "20230317", "2128210"
    if a.targets_json:
        # A committed, already-resolved table. It is only valid for the exact
        # libil2cpp.so it was resolved against, so check that first.
        table = need(a.targets_json, "target table")
        step(f"using target table {table}")
        data = json.loads(table.read_text(encoding="utf-8"))
        sha = hashlib.sha256(il2cpp.read_bytes()).hexdigest()
        want = str(data.get("il2cppSha256") or "").lower()
        if want and sha != want:
            fail(f"libil2cpp.so in this APK ({sha}) is not the build the target table was resolved "
                 f"for ({want}). Resolve a table for it with --dump-cs.")
        ver_name, ver_code = str(data["versionName"]), str(data["versionCode"])
    else:
        if not a.dump_cs:
            # Dumping is left to Il2CppDumper (see README) to keep this
            # script toolchain-light.
            fail("Pass --dump-cs <dump.cs> from Il2CppDumper for this APK, or --targets-json with a "
                 "resolved table (see DorkNet.Quest/README.md).")
        need(a.dump_cs, "dump.cs")
        step("resolving hook targets")
        # Written into the work dir (Controlled Folder Access can block writes
        # under Documents). The repo keeps a committed copy under targets/.
        table = work / f"{ver_name}-{ver_code}.json"
        run_py("resolve_targets.py", "--dump", a.dump_cs, "--il2cpp", il2cpp,
               "--version-name", ver_name, "--version-code", ver_code,
               "--objdump", objdump, "--out", table)

    # 2. headers
    step("generating headers")
    run_py("gen_targets_header.py", table, gen / "targets_generated.h")
    run_py("gen_config_header.py", config, pkg_name, gen / "config_generated.h")

    # 3. compile
    step("compiling libdorknet.so (arm64)")
    src = REPO / "src"
    so = work / SO_NAME
    run(cxx, "-std=c++17", "-fPIC", "-shared", "-O2", "-fvisibility=hidden", "-static-libstdc++",
        "-ffunction-sections", "-fdata-sections", "-Wl,--gc-sections",
        f"-I{gen}", f"-I{src}",
        src / "dorknet_quest.cpp", src / "config.cpp",
        src / "third_party" / "And64InlineHook" / "And64InlineHook.cpp",
        "-llog", "-o", so)
    need(so, "libdorknet.so")

    # 4. decode
    step("apktool decode")
    if project.exists():
        shutil.rmtree(project)
    run("java", "-jar", apktool, "decode", "-f", "-o", project, apk)

    # 5. package rename + manifest + label
    step(f"renaming package {OLD_PKG} -> {pkg_name}")
    manifest = project / "AndroidManifest.xml"
    m = manifest.read_text(encoding="utf-8").replace(OLD_PKG, pkg_name)
    if "android.intent.category.LAUNCHER" not in m:
        m = m.replace('<category android:name="android.intent.category.INFO"/>',
                      '<category android:name="android.intent.category.INFO"/>'
                      '<category android:name="android.intent.category.LAUNCHER"/>')
    manifest.write_text(m, encoding="utf-8", newline="")

    # The launcher label comes from @string/app_name; set it in every locale.
    step(f"naming the app '{app_name}'")
    # XML entities for & < > " ' (the PowerShell version used
    # SecurityElement.Escape), so no raw quote reaches Android's string parser.
    label = xml_escape(app_name, {'"': "&quot;", "'": "&apos;"})
    for sx in (project / "res").rglob("strings.xml"):
        s = sx.read_text(encoding="utf-8")
        n = re.sub(r'<string name="app_name">[^<]*</string>', lambda _: f'<string name="app_name">{label}</string>', s)
        if n != s:
            sx.write_text(n, encoding="utf-8", newline="")

    # 6. inject
    step(f"injecting {SO_NAME}")
    shutil.copy2(so, project / "lib" / "arm64-v8a" / SO_NAME)
    run_py("inject_needed.py", project / "lib" / "arm64-v8a" / "libmain.so", SO_NAME)

    # 7. build + align + sign
    step("apktool build")
    unsigned = work / "unsigned.apk"
    run("java", "-jar", apktool, "build", "-f", "-o", unsigned, project)

    if a.keystore:
        keystore = need(a.keystore, "keystore")
    else:
        keystore = work / "dorknet-debug.keystore"
        if not keystore.exists():
            step("generating a fresh debug keystore (kept in the work dir, never committed)")
            run("keytool", "-genkeypair", "-v", "-keystore", keystore, "-alias", a.key_alias,
                "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000",
                "-storepass", a.keystore_pass, "-keypass", a.keystore_pass,
                "-dname", "CN=DorkNet Quest, O=DorkNet")

    step("zipalign")
    aligned = work / "aligned.apk"
    run(zipalign, "-f", "-p", "4", unsigned, aligned)

    out_apk = Path(a.out_apk) if a.out_apk else work / f"dorknet-recroom-{ver_name}.apk"
    out_apk.parent.mkdir(parents=True, exist_ok=True)
    step(f"apksigner -> {out_apk}")
    run(apksigner, "sign", "--ks", keystore, "--ks-key-alias", a.key_alias,
        "--ks-pass", f"pass:{a.keystore_pass}", "--key-pass", f"pass:{a.keystore_pass}",
        "--v2-signing-enabled", "true", "--v3-signing-enabled", "true",
        "--out", out_apk, aligned)
    capture(apksigner, "verify", "--print-certs", out_apk)

    print()
    print(f"DONE. Patched APK: {out_apk}")
    print(f"Package: {pkg_name}   versionCode: {ver_code}")
    if a.obb:
        print()
        package(out_apk, Path(a.obb), Path(a.package_dir) if a.package_dir else None, build_tools)
    else:
        print("Install it, then push the matching OBB to:")
        print(f"  /sdcard/Android/obb/{pkg_name}/main.{ver_code}.{pkg_name}.obb")
        print("(or pass --obb to bundle both into an installable .xapk)")


# ---- package --------------------------------------------------------------

README_TXT = """{name} {ver} - Rec Room for DorkNet (Meta Quest)
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
3. In the headset: Library > Unknown Sources > {name}.

The app installs as {pkg} alongside any store copy of Rec Room.
SideQuest users can also drop the .xapk onto SideQuest instead.
"""


def package(apk: Path, obb: Path, out_dir: Path | None, build_tools: Path) -> Path:
    """Bundle a patched APK and its OBB into an .xapk plus installers.

    The XAPK is the usual APKPure layout (manifest.json + APK + OBB + icon), so
    XAPK-aware installers (SideQuest's file drop, SAI, APKPure) can open it too.
    The APK and OBB are stored uncompressed: they are already compressed, and
    storing them keeps packaging and install fast."""
    apk, obb = need(apk, "patched APK").resolve(), need(obb, "OBB").resolve()
    aapt2 = need(build_tools / exe("aapt2"), "aapt2")

    step("reading APK metadata")
    badging = capture(aapt2, "dump", "badging", apk).splitlines()

    def badge(pattern: str) -> str | None:
        for line in badging:
            mm = re.search(pattern, line)
            if mm:
                return mm.group(1)
        return None

    pkg = badge(r"^package: name='([^']+)'")
    ver_code = badge(r"versionCode='([^']+)'")
    ver_name = badge(r"versionName='([^']+)'")
    min_sdk = badge(r"^(?:minSdkVersion|sdkVersion):'([^']+)'")
    tgt_sdk = badge(r"^targetSdkVersion:'([^']+)'")
    label = badge(r"^application-label:'([^']*)'")
    if not pkg or not ver_code:
        fail("could not read package name / versionCode from the APK")
    perms = [mm.group(1) for line in badging if (mm := re.match(r"^uses-permission: name='([^']+)'", line))]
    icons = sorted(((int(mm.group(1)), mm.group(2)) for line in badging
                    if (mm := re.match(r"^application-icon-(\d+):'([^']+)'", line))), reverse=True)

    # The launcher icon may be an adaptive-icon XML; XAPK installers want a
    # PNG, so fall back to the same-named PNG from the densest mipmap/drawable.
    icon_path = None
    with zipfile.ZipFile(apk) as z:
        names = set(z.namelist())
        if icons:
            ref = icons[0][1]
            if ref.endswith(".png") and ref in names:
                icon_path = ref
            else:
                base = Path(ref).stem
                for d in ("xxxhdpi", "xxhdpi", "xhdpi", "hdpi", "mdpi", "ldpi"):
                    for kind in ("mipmap", "drawable"):
                        cand = f"res/{kind}-{d}/{base}.png"
                        if icon_path is None and cand in names:
                            icon_path = cand
        icon_bytes = z.read(icon_path) if icon_path else None
    print(f"  {pkg}  version {ver_name} ({ver_code})")

    obb_entry = f"Android/obb/{pkg}/main.{ver_code}.{pkg}.obb"
    out_dir = out_dir or apk.parent / f"DorkNet-Quest-{ver_name}"
    out_dir.mkdir(parents=True, exist_ok=True)
    xapk = out_dir / f"DorkNet-{ver_name}.xapk"
    xapk.unlink(missing_ok=True)

    manifest = {
        "xapk_version": 2,
        "package_name": pkg,
        "name": label or "DorkNet",
        "version_code": ver_code,
        "version_name": ver_name,
        "min_sdk_version": min_sdk,
        "target_sdk_version": tgt_sdk,
        "permissions": perms,
        "total_size": apk.stat().st_size + obb.stat().st_size,
        "expansions": [{"file": obb_entry, "install_location": "EXTERNAL_STORAGE", "install_path": obb_entry}],
    }
    if icon_bytes:
        manifest["icon"] = "icon.png"

    step(f"writing {xapk} (the OBB is large; this takes a minute)")
    with zipfile.ZipFile(xapk, "w", allowZip64=True) as z:
        z.writestr(zipfile.ZipInfo("manifest.json"), json.dumps(manifest, indent=2), compress_type=zipfile.ZIP_DEFLATED)
        if icon_bytes:
            z.writestr("icon.png", icon_bytes, compress_type=zipfile.ZIP_STORED)
        z.write(apk, f"{pkg}.apk", compress_type=zipfile.ZIP_STORED)
        z.write(obb, obb_entry, compress_type=zipfile.ZIP_STORED)

    step("adding installers")
    for f in ("install.ps1", "install.cmd", "install.sh"):
        shutil.copy2(SCRIPTS / "installer" / f, out_dir / f)
    (out_dir / "install.sh").chmod(0o755)
    (out_dir / "README.txt").write_text(
        README_TXT.format(name=manifest["name"], ver=ver_name, pkg=pkg), encoding="utf-8")

    print()
    print(f"DONE. Package folder: {out_dir}")
    print(f"  {xapk.name}  ({xapk.stat().st_size / 2**30:.2f} GB)")
    print(f"Install with: {out_dir / 'install.cmd'}")
    return out_dir


def cmd_package(a: argparse.Namespace) -> None:
    package(Path(a.apk), Path(a.obb), Path(a.out_dir) if a.out_dir else None,
            resolve_build_tools(a.build_tools_dir))


# ---- cli ------------------------------------------------------------------

def main() -> None:
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = p.add_subparsers(dest="cmd", required=True)

    b = sub.add_parser("build", help="store APK -> signed patched APK (+ .xapk with --obb)")
    b.add_argument("--apk", required=True, help="pristine store APK (com.AgainstGravity.RecRoom)")
    b.add_argument("--obb", help="matching store OBB; also writes the .xapk release package")
    b.add_argument("--config", help="dorknet-quest.cfg (default: DorkNet.Quest/dorknet-quest.cfg, else the .example)")
    b.add_argument("--dump-cs", help="Il2CppDumper dump.cs for this APK, to resolve the hook targets")
    b.add_argument("--targets-json", help="a resolved target table to use instead of --dump-cs; must match the APK's libil2cpp.so")
    b.add_argument("--keystore", help="signing keystore; reuse the same one for every build so updates install in place")
    b.add_argument("--keystore-pass", default="dorknet", help="store and key password (default: dorknet)")
    b.add_argument("--key-alias", default="dorknet", help="key alias (default: dorknet)")
    b.add_argument("--work-dir", default=str(Path(tempfile.gettempdir()) / "dorknet-quest-build"),
                   help="scratch directory (on Windows keep it outside Controlled Folder Access, e.g. %%TEMP%%)")
    b.add_argument("--out-apk", help="where to write the APK (default: <work-dir>/dorknet-recroom-<ver>.apk)")
    b.add_argument("--package-dir", help="release folder for the .xapk (default: <apk folder>/DorkNet-Quest-<ver>)")
    b.add_argument("--ndk-root", help="Android NDK root (default: ANDROID_NDK, else newest under ANDROID_SDK_ROOT/ndk)")
    b.add_argument("--build-tools-dir", help="Android build-tools dir (default: newest under ANDROID_SDK_ROOT/build-tools)")
    b.add_argument("--apktool-jar", help="apktool.jar (default: APKTOOL_JAR, else ANDROID_SDK_ROOT/apktool.jar)")
    b.add_argument("--api-level", default="24", help="NDK API level (default: 24)")
    b.set_defaults(func=cmd_build)

    k = sub.add_parser("package", help="patched APK + OBB -> .xapk + installers")
    k.add_argument("--apk", required=True)
    k.add_argument("--obb", required=True)
    k.add_argument("--out-dir", help="release folder (default: <apk folder>/DorkNet-Quest-<ver>)")
    k.add_argument("--build-tools-dir")
    k.set_defaults(func=cmd_package)

    a = p.parse_args()
    a.func(a)


if __name__ == "__main__":
    main()
