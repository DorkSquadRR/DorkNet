#!/usr/bin/env python3
"""Build the DorkNet PC client packages.

  patcher   dorknet-clientpatch-<VersionKey>.zip, what the DorkNet launcher
            installs: MelonLoader.zip, DorkNet.ClientMod.dll (built from this
            repo), the config template, the Goldberg Steam stub and a
            manifest.json saying where each piece goes. No game files.

  client    a ready-to-run client: a stock Rec Room build with a patcher zip
            applied the way the launcher applies it, plus your config. It
            contains the game's own files, so keep it private.

Runs on Windows, Linux and macOS. `patcher` needs the .NET SDK (the mod
builds against MelonLoader's own DLLs, so no Rec Room install is needed).

    py -3 tools/build_pc.py patcher --version-key march_2023_03_21
    py -3 tools/build_pc.py client --base-client RecRoom-2023-03-21.zip \\
        --patcher-zip dist/dorknet-clientpatch-march_2023_03_21.zip \\
        --config dorknet-clientmod.json
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CLIENT_MOD = REPO / "DorkNet.ClientMod"

# Where each build expects the mod's config and the Steam DLL the game actually
# loads (see the per-branch build layout notes in the README):
#   2020.03  Recroom_Release.exe is the game; Steam DLL in its Data/Plugins.
#   2020.12  RecRoom.exe is the game (Recroom_Release.exe is the EAC wrapper);
#            MelonLoader keeps UserData under MelonLoader/.
#   2023.03  RecRoom.exe + RecRoom_Data; MelonLoader 0.6 keeps UserData at the
#            install root.
LAYOUTS = {
    "march_2020_03_10": ("MelonLoader/UserData/dorknet-clientmod.json", "Recroom_Release_Data/Plugins/steam_api64.dll"),
    "december_2020_12_18": ("MelonLoader/UserData/dorknet-clientmod.json", "RecRoom_Data/Plugins/x86_64/steam_api64.dll"),
    "march_2023_03_21": ("UserData/dorknet-clientmod.json", "RecRoom_Data/Plugins/x86_64/steam_api64.dll"),
}


def step(msg: str) -> None:
    print(f"==> {msg}", flush=True)


def fail(msg: str) -> None:
    raise SystemExit(f"error: {msg}")


def need(path: Path | str, what: str) -> Path:
    p = Path(path)
    if not p.exists():
        fail(f"{what} not found: {p}")
    return p


def zip_dir(src: Path, out: Path) -> None:
    """Zip the contents of src (not src itself) into out."""
    out.parent.mkdir(parents=True, exist_ok=True)
    out.unlink(missing_ok=True)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, allowZip64=True) as z:
        for f in sorted(src.rglob("*")):
            if f.is_file():
                z.write(f, f.relative_to(src).as_posix())


# ---- patcher --------------------------------------------------------------

def cmd_patcher(a: argparse.Namespace) -> None:
    if a.version_key not in LAYOUTS:
        fail(f"unknown version key '{a.version_key}'. Known: {', '.join(LAYOUTS)}")
    config_dest, steam_dest = LAYOUTS[a.version_key]
    out_dir = Path(a.output_dir) if a.output_dir else REPO / "dist"

    with tempfile.TemporaryDirectory(prefix=f"dorknet-patcher-{a.version_key}-") as tmp:
        stage = Path(tmp) / "stage"
        stage.mkdir()

        # 1. MelonLoader. Downloaded first so its DLLs can serve as the mod's
        #    compile-time references: no local Rec Room install needed.
        url = f"https://github.com/LavaGang/MelonLoader/releases/download/{a.melonloader_version}/MelonLoader.x64.zip"
        ml_zip = stage / "MelonLoader.zip"
        step(f"downloading {url}")
        with urllib.request.urlopen(url) as r, open(ml_zip, "wb") as f:
            shutil.copyfileobj(r, f)
        if ml_zip.stat().st_size < 1 << 20:
            fail("MelonLoader download failed or produced an unexpectedly small file")
        ml_extract = Path(tmp) / "melonloader"
        with zipfile.ZipFile(ml_zip) as z:
            z.extractall(ml_extract)
        # The 0.6.x release zip has a top-level MelonLoader/ folder holding
        # net6, net35, Dependencies, Documentation.
        ml_ref = ml_extract / "MelonLoader"
        if not (ml_ref / "net6" / "MelonLoader.dll").exists():
            fail("extracted MelonLoader is missing net6/MelonLoader.dll; the release layout may have changed")

        # 2. the mod
        step("dotnet build DorkNet.ClientMod (Release)")
        r = subprocess.run(["dotnet", "build", str(CLIENT_MOD / "DorkNet.ClientMod.csproj"),
                            "-c", "Release", "--nologo", f"-p:MelonLoaderDir={ml_ref}"])
        if r.returncode:
            fail("ClientMod build failed")
        dlls = sorted((CLIENT_MOD / "bin" / "Release").rglob("DorkNet.ClientMod.dll"),
                      key=lambda p: p.stat().st_mtime, reverse=True)
        if not dlls:
            fail("DorkNet.ClientMod.dll not found after the build")
        shutil.copy2(dlls[0], stage / "DorkNet.ClientMod.dll")
        print(f"    built {dlls[0]}")

        # 3. config template + 4. Goldberg Steam stub (App ID 471710, the same
        #    for every build). The stub replaces Valve's steam_api64.dll so the
        #    game runs without Steam.
        for src, what in ((CLIENT_MOD / "dorknet-clientmod.json.template", "config template"),
                          (CLIENT_MOD / "steam-stub" / "steam_api64.dll", "Steam stub DLL"),
                          (CLIENT_MOD / "steam-stub" / "steam_appid.txt", "Steam appid file")):
            shutil.copy2(need(src, what), stage / src.name)

        # 5. manifest.json, read by the launcher and by `client` below.
        manifest = {
            "$schema_version": 1,
            "loader_archive": "MelonLoader.zip",
            "plugin_dll": "DorkNet.ClientMod.dll",
            "plugin_dest": "Mods",
            "config_template": "dorknet-clientmod.json.template",
            "config_dest": config_dest,
            "old_plugin_paths": ["BepInEx/plugins/DorkNet.ClientPatch.dll"],
            "steam_stub": {
                # 2020.12+ is an EAC build: the real Unity binary is RecRoom.exe
                # (it loads RecRoom_Data/Plugins/x86_64/steam_api64.dll), NOT
                # Recroom_Release.exe (the EAC launcher wrapper, whose data dir
                # the real game never opens).
                "api_dll": "steam_api64.dll",
                "api_dest": steam_dest,
                "api_backup_suffix": ".steam-original",
                "appid_file": "steam_appid.txt",
                "appid_dest": "steam_appid.txt",
            },
        }
        (stage / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")

        # 6. bundle
        out = out_dir / f"dorknet-clientpatch-{a.version_key}.zip"
        zip_dir(stage, out)
    print()
    print(f"Built {out} ({out.stat().st_size / 2**20:.1f} MB)")


# ---- client ---------------------------------------------------------------

def cmd_client(a: argparse.Namespace) -> None:
    base = need(a.base_client, "stock client")
    patcher_zip = need(a.patcher_zip, "patcher zip")
    config = need(a.config, "client config") if a.config else None

    with tempfile.TemporaryDirectory(prefix="dorknet-pc-client-") as tmp:
        tmp = Path(tmp)
        install, patcher = tmp / "install", tmp / "patcher"
        patcher.mkdir()

        # 1. stock client: a zip or a folder, the install at its root or
        #    inside one wrapping folder.
        step(f"unpacking stock client {base}")
        if base.is_dir():
            unpacked = base
        else:
            unpacked = tmp / "base"
            with zipfile.ZipFile(base) as z:
                z.extractall(unpacked)
        root = unpacked
        top = list(root.iterdir())
        if len(top) == 1 and top[0].is_dir():
            root = top[0]
        games = [p.name for p in root.glob("*.exe") if (root / f"{p.stem}_Data").is_dir()]
        if not games:
            fail(f"no game exe with a matching _Data folder at the root of {base}")
        print(f"    game: {', '.join(games)}")
        shutil.copytree(root, install, ignore=shutil.ignore_patterns(".DepotDownloader"))

        # 2. apply the patcher the way the launcher does, from its manifest.
        step(f"applying {patcher_zip}")
        with zipfile.ZipFile(patcher_zip) as z:
            z.extractall(patcher)
        m = json.loads(need(patcher / "manifest.json", "patcher manifest.json").read_text(encoding="utf-8"))

        with zipfile.ZipFile(need(patcher / m["loader_archive"], "MelonLoader archive")) as z:
            z.extractall(install)
        print("    MelonLoader -> install root")

        plugin_dir = install / m["plugin_dest"]
        plugin_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(patcher / m["plugin_dll"], plugin_dir / m["plugin_dll"])
        print(f"    {m['plugin_dll']} -> {m['plugin_dest']}/")

        config_dest = install / m["config_dest"]
        config_dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(config or patcher / m["config_template"], config_dest)
        print(f"    config -> {m['config_dest']}{'' if config else ' (template, unedited)'}")

        stub = m.get("steam_stub")
        if stub:
            api_dest = install / stub["api_dest"]
            if api_dest.exists():
                backup = api_dest.with_name(api_dest.name + stub["api_backup_suffix"])
                if not backup.exists():
                    shutil.copy2(api_dest, backup)
            api_dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(patcher / stub["api_dll"], api_dest)
            shutil.copy2(patcher / stub["appid_file"], install / stub["appid_dest"])
            print(f"    Steam stub -> {stub['api_dest']}")

        for old in m.get("old_plugin_paths") or []:
            p = install / old
            if p.exists():
                p.unlink()
                print(f"    removed old {old}")

        # 3. zip
        key = patcher_zip.stem.removeprefix("dorknet-clientpatch-")
        out = Path(a.output_zip) if a.output_zip else REPO / "dist" / f"dorknet-client-{key}.zip"
        step(f"zipping -> {out}")
        zip_dir(install, out)
    print()
    print(f"Built {out} ({out.stat().st_size / 2**20:.1f} MB). It contains game files: keep it private.")


# ---- cli ------------------------------------------------------------------

def main() -> None:
    p = argparse.ArgumentParser(description="Build the DorkNet PC client packages.")
    sub = p.add_subparsers(dest="cmd", required=True)

    pz = sub.add_parser("patcher", help="dorknet-clientpatch-<VersionKey>.zip (no game files)")
    pz.add_argument("--version-key", required=True, choices=sorted(LAYOUTS),
                    help="the build the zip is for; picks the install layout")
    pz.add_argument("--melonloader-version", default="v0.6.6",
                    help="MelonLoader release tag to bundle (default: v0.6.6, what the mod compiles against)")
    pz.add_argument("--output-dir", help="where the zip goes (default: <repo>/dist)")
    pz.set_defaults(func=cmd_patcher)

    c = sub.add_parser("client", help="stock build + patcher zip + config -> ready-to-run client zip")
    c.add_argument("--base-client", required=True, help="the stock client, as a .zip or a folder")
    c.add_argument("--patcher-zip", required=True, help="dorknet-clientpatch-<VersionKey>.zip from `patcher`")
    c.add_argument("--config", help="dorknet-clientmod.json to install (default: the unedited template)")
    c.add_argument("--output-zip", help="default: <repo>/dist/dorknet-client-<VersionKey>.zip")
    c.set_defaults(func=cmd_client)

    a = p.parse_args()
    a.func(a)


if __name__ == "__main__":
    main()
