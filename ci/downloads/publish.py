#!/usr/bin/env python3
"""Publish DorkNet client builds to the downloads page.

  stage    the build output in out/ -> out/downloads/<release>/ under the
           names players see, plus release.json describing those files
  upload   that folder -> the downloads bucket, then merge release.json into
           the bucket's manifest.json and upload the page (index.html)

The build scripts keep their own file names on purpose: the launcher fetches
dorknet-clientpatch-<VersionKey>.zip from GitHub Releases by that exact name.
Only the published copies are renamed:

    dorknet-client-<key>.zip              DorkNet-<release>-PC-Full.zip
    dorknet-clientpatch-<key>.zip         DorkNet-<release>-PC-Patch.zip
    DorkNet-Quest-<ver>/DorkNet-<ver>.xapk  DorkNet-<release>-Quest.xapk
    DorkNet-Quest-<ver>/install.*, README DorkNet-<release>-Quest-Installer.zip

The bucket is served read-only as a static website (Garage's web endpoint).
upload talks to its S3 API with rclone; the endpoint, bucket and key come
from the environment:

    DOWNLOADS_S3_ENDPOINT   S3 API URL, e.g. http://garage-<uuid>:3900
    DOWNLOADS_BUCKET        bucket name, e.g. downloads.example.com
    DOWNLOADS_S3_REGION     optional, default garage
    DOWNLOADS_S3_KEY_ID / DOWNLOADS_S3_SECRET
    DORKNET_SITE_URL        optional: the DorkNet server's website, linked
                            from the page for the browser Quest installer

    python3 ci/downloads/publish.py stage --out out --release 2023.03 \\
        --title "March 2023" --game-version 2023-03-21 --version-key march_2023_03_21
    python3 ci/downloads/publish.py upload --staged out/downloads
"""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path
from typing import NoReturn

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PAGE_FILES = {  # bucket path -> source
    "index.html": HERE / "index.html",
    "dorknet.svg": REPO / "DorkNet.Server" / "site" / "public" / "brand" / "dorknet.svg",
}
NO_CACHE = "Cache-Control: no-cache"

INSTALLER_NOTE = """Unzip these files into the same folder as {xapk}, then
run install.cmd (Windows) or ./install.sh (macOS / Linux).

"""


def fail(msg: str) -> NoReturn:
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(1)


def step(msg: str) -> None:
    print(f"==> {msg}", flush=True)


def human(n: int) -> str:
    return f"{n / 2**20:,.1f} MB" if n >= 2**20 else f"{max(1, round(n / 2**10))} KB"


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        while chunk := f.read(1 << 20):
            h.update(chunk)
    return h.hexdigest()


def link_or_copy(src: Path, dst: Path) -> None:
    """Hard-link where possible: the full client is several GB."""
    dst.unlink(missing_ok=True)
    try:
        os.link(src, dst)
    except OSError:
        shutil.copy2(src, dst)


def game_exe(full_zip: Path) -> str | None:
    """The game's exe: the one with a matching <name>_Data folder at the root."""
    with zipfile.ZipFile(full_zip) as z:
        names = z.namelist()
    tops = {n.split("/", 1)[0] for n in names}
    exes = sorted(t for t in tops if t.lower().endswith(".exe") and f"{t[:-4]}_Data" in tops)
    return exes[0] if exes else None


# ---- stage ----------------------------------------------------------------

def cmd_stage(a: argparse.Namespace) -> None:
    out = Path(a.out)
    dest = Path(a.dest) if a.dest else out / "downloads"
    rel_dir = dest / a.release
    if rel_dir.exists():
        shutil.rmtree(rel_dir)
    rel_dir.mkdir(parents=True)
    prefix = f"DorkNet-{a.release}"

    staged: list[tuple[str, str, Path]] = []  # (id, platform, staged file)
    extra: dict[str, str] = {}

    full = out / "pc" / f"dorknet-client-{a.version_key}.zip"
    patch = out / "pc" / f"dorknet-clientpatch-{a.version_key}.zip"
    if full.is_file():
        dst = rel_dir / f"{prefix}-PC-Full.zip"
        link_or_copy(full, dst)
        staged.append(("pc-full", "pc", dst))
        exe = game_exe(full)
        if exe:
            extra["pc_exe"] = exe
    if patch.is_file():
        dst = rel_dir / f"{prefix}-PC-Patch.zip"
        link_or_copy(patch, dst)
        staged.append(("pc-patch", "pc", dst))

    packages = sorted((out / "quest").glob("DorkNet-Quest-*/"))
    if len(packages) > 1:
        fail(f"more than one Quest package folder in {out / 'quest'}: {', '.join(p.name for p in packages)}")
    if packages:
        pkg = packages[0]
        xapks = sorted(pkg.glob("*.xapk"))
        if len(xapks) != 1:
            fail(f"expected one .xapk in {pkg}, found {len(xapks)}")
        xapk = rel_dir / f"{prefix}-Quest.xapk"
        link_or_copy(xapks[0], xapk)
        staged.append(("quest", "quest", xapk))

        installer = rel_dir / f"{prefix}-Quest-Installer.zip"
        with zipfile.ZipFile(installer, "w", zipfile.ZIP_DEFLATED) as z:
            for name in ("install.cmd", "install.ps1", "install.sh"):
                src = pkg / name
                if not src.is_file():
                    fail(f"{src} missing")
                info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
                info.external_attr = (0o755 if name.endswith(".sh") else 0o644) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                z.writestr(info, src.read_bytes())
            readme = pkg / "README.txt"
            text = INSTALLER_NOTE.format(xapk=xapk.name) + (readme.read_text(encoding="utf-8") if readme.is_file() else "")
            info = zipfile.ZipInfo("README.txt", date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, text)
        staged.append(("quest-installer", "quest", installer))

    if not staged:
        fail(f"nothing to publish in {out}")

    built_at = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat()
    files = []
    for fid, platform, path in staged:
        step(f"{path.name}: hashing")
        files.append({
            "id": fid,
            "platform": platform,
            "name": path.name,
            "path": f"{a.release}/{path.name}",
            "size": path.stat().st_size,
            "sha256": sha256(path),
            "build": a.build,
            "commit": a.commit,
            "built_at": built_at,
        })
    release = {"id": a.release, "title": a.title, "game_version": a.game_version, **extra, "files": files}
    (dest / "release.json").write_text(json.dumps(release, indent=2) + "\n", encoding="utf-8")
    print()
    for f in files:
        print(f"  {f['name']}  {human(f['size'])}  {f['sha256'][:16]}…")
    print(f"Staged {len(files)} file(s) in {rel_dir}")


# ---- upload ---------------------------------------------------------------

def rclone_env() -> dict[str, str]:
    env = dict(os.environ)
    for k in ("DOWNLOADS_S3_ENDPOINT", "DOWNLOADS_BUCKET", "DOWNLOADS_S3_KEY_ID", "DOWNLOADS_S3_SECRET"):
        if not env.get(k):
            fail(f"{k} is not set")
    env.update({
        "RCLONE_CONFIG_DL_TYPE": "s3",
        "RCLONE_CONFIG_DL_PROVIDER": "Other",
        "RCLONE_CONFIG_DL_ENDPOINT": env["DOWNLOADS_S3_ENDPOINT"],
        "RCLONE_CONFIG_DL_REGION": env.get("DOWNLOADS_S3_REGION") or "garage",
        "RCLONE_CONFIG_DL_ACCESS_KEY_ID": env["DOWNLOADS_S3_KEY_ID"],
        "RCLONE_CONFIG_DL_SECRET_ACCESS_KEY": env["DOWNLOADS_S3_SECRET"],
        "RCLONE_CONFIG_DL_FORCE_PATH_STYLE": "true",
        "RCLONE_CONFIG_DL_NO_CHECK_BUCKET": "true",
    })
    return env


def rclone(env: dict[str, str], *args: str, stdin: str | None = None, check: bool = True) -> subprocess.CompletedProcess:
    r = subprocess.run(["rclone", *args], env=env, input=stdin, capture_output=True, text=True)
    if check and r.returncode != 0:
        fail(f"rclone {args[0]} failed ({r.returncode}): {r.stderr.strip()}")
    return r


def merge(manifest: dict, release: dict) -> dict:
    """Replace this release's files that were just built; keep the others
    (a push build may rebuild only one platform)."""
    releases = [r for r in manifest.get("releases", []) if r.get("id") != release["id"]]
    old = next((r for r in manifest.get("releases", []) if r.get("id") == release["id"]), {})
    new_ids = {f["id"] for f in release["files"]}
    kept = [f for f in old.get("files", []) if f.get("id") not in new_ids]
    merged = {**old, **release, "files": kept + release["files"]}
    if "pc_exe" not in release and "pc_exe" in old:
        merged["pc_exe"] = old["pc_exe"]
    order = ["pc-full", "pc-patch", "quest", "quest-installer"]
    merged["files"].sort(key=lambda f: order.index(f["id"]) if f["id"] in order else len(order))
    releases.append(merged)
    releases.sort(key=lambda r: r["id"], reverse=True)
    out = {**manifest, "releases": releases,
           "updated": datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat()}
    site = os.environ.get("DORKNET_SITE_URL", "").rstrip("/")
    if site:
        out["site_url"] = site
    return out


def cmd_upload(a: argparse.Namespace) -> None:
    staged = Path(a.staged)
    release = json.loads((staged / "release.json").read_text(encoding="utf-8"))
    env = rclone_env()
    bucket = f"dl:{env['DOWNLOADS_BUCKET']}"
    rel = release["id"]

    # 1. the files, before the manifest that points at them
    for f in release["files"]:
        step(f"uploading {f['name']} ({human(f['size'])})")
        rclone(env, "copyto", str(staged / f["path"]), f"{bucket}/{f['path']}",
               "--header-upload", NO_CACHE, "--s3-upload-concurrency", "8", "--s3-chunk-size", "64M")

    # 2. merge into the live manifest
    r = rclone(env, "cat", f"{bucket}/manifest.json", check=False)
    manifest = json.loads(r.stdout) if r.returncode == 0 and r.stdout.strip() else {}
    manifest = merge(manifest, release)

    # 3. the page, then the manifest
    for path, src in PAGE_FILES.items():
        step(f"uploading {path}")
        rclone(env, "copyto", str(src), f"{bucket}/{path}", "--header-upload", NO_CACHE)
    step("uploading manifest.json")
    rclone(env, "rcat", f"{bucket}/manifest.json", "--header-upload", NO_CACHE,
           "--header-upload", "Content-Type: application/json", stdin=json.dumps(manifest, indent=2) + "\n")

    # 4. drop files of this release the manifest no longer lists (renames)
    listed = {f["path"] for r in manifest["releases"] if r["id"] == rel for f in r["files"]}
    r = rclone(env, "lsf", f"{bucket}/{rel}/", "--files-only", check=False)
    for name in r.stdout.split():
        if f"{rel}/{name}" not in listed:
            step(f"removing stale {rel}/{name}")
            rclone(env, "deletefile", f"{bucket}/{rel}/{name}")
    print(f"Published {len(release['files'])} file(s) for {release['title']}.")


# ---- cli ------------------------------------------------------------------

def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    s = sub.add_parser("stage", help="out/ -> out/downloads/<release>/ + release.json")
    s.add_argument("--out", default="out", help="the build output folder (default: out)")
    s.add_argument("--dest", help="where to stage (default: <out>/downloads)")
    s.add_argument("--release", required=True, help="release id used in file names and paths, e.g. 2023.03")
    s.add_argument("--title", required=True, help='shown on the page, e.g. "March 2023"')
    s.add_argument("--game-version", required=True, help="the stock build's date, e.g. 2023-03-21")
    s.add_argument("--version-key", required=True, help="the PC build's version key, e.g. march_2023_03_21")
    s.add_argument("--build", default=os.environ.get("BUILD_NUMBER", ""), help="CI build number (default: $BUILD_NUMBER)")
    s.add_argument("--commit", default=os.environ.get("GIT_COMMIT", ""), help="source commit (default: $GIT_COMMIT)")
    s.set_defaults(fn=cmd_stage)

    u = sub.add_parser("upload", help="staged folder -> downloads bucket + manifest.json + page")
    u.add_argument("--staged", default="out/downloads", help="the folder `stage` wrote (default: out/downloads)")
    u.set_defaults(fn=cmd_upload)

    a = p.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
