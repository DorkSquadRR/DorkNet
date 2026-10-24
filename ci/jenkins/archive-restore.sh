#!/usr/bin/env bash
# archive-restore — pull a Rec Room build out of the restic build archive.
#
#   archive-restore list    <repo-path> [tag[,tag…]]
#   archive-restore restore <repo-path> <tag[,tag…]> <cache-dir> [restic restore args…]
#
# <repo-path> is the restic repository's folder on the archive FTP, e.g.
# "Rec Room Versions/steam-v2". Snapshots are picked by tag, e.g.
# "manifest=7490748483298966814" (Steam) or "version-code=2128210" (Quest);
# several tags separated by commas must all match. The newest match wins.
#
# `restore` writes the snapshot to <cache-dir>/<snapshot-id>/ and prints that
# path. A snapshot already restored there is reused, so a multi-GB build is
# only downloaded once. Extra arguments go to `restic restore` (e.g.
# --exclude /.DepotDownloader).
#
# restic reaches the FTP through its rclone backend. Everything secret comes
# from the environment (Jenkins injects it from credentials):
#   ARCHIVE_FTP_HOST      FTP host name
#   ARCHIVE_FTP_USER      FTP user
#   ARCHIVE_FTP_PASSWORD  FTP password
#   RESTIC_PASSWORD       restic repository password
# Optional: ARCHIVE_FTP_PORT (21), ARCHIVE_FTP_TLS (none | explicit | implicit,
# default explicit), RESTIC_CACHE_DIR.
set -euo pipefail

mode="${1:?usage: archive-restore list|restore <repo-path> …}"
repo_path="${2:?repo path required}"
tags="${3:-}"

: "${ARCHIVE_FTP_HOST:?ARCHIVE_FTP_HOST is not set}"
: "${ARCHIVE_FTP_USER:?ARCHIVE_FTP_USER is not set}"
: "${ARCHIVE_FTP_PASSWORD:?ARCHIVE_FTP_PASSWORD is not set}"
: "${RESTIC_PASSWORD:?RESTIC_PASSWORD is not set}"

# rclone remote "rrarchive", configured purely through its env-var config.
export RCLONE_CONFIG_RRARCHIVE_TYPE=ftp
export RCLONE_CONFIG_RRARCHIVE_HOST="$ARCHIVE_FTP_HOST"
export RCLONE_CONFIG_RRARCHIVE_USER="$ARCHIVE_FTP_USER"
export RCLONE_CONFIG_RRARCHIVE_PORT="${ARCHIVE_FTP_PORT:-21}"
RCLONE_CONFIG_RRARCHIVE_PASS="$(printf '%s' "$ARCHIVE_FTP_PASSWORD" | rclone obscure -)"
export RCLONE_CONFIG_RRARCHIVE_PASS
case "${ARCHIVE_FTP_TLS:-explicit}" in
  explicit) export RCLONE_CONFIG_RRARCHIVE_EXPLICIT_TLS=true ;;
  implicit) export RCLONE_CONFIG_RRARCHIVE_TLS=true ;;
  none) ;;
  *) echo "archive-restore: ARCHIVE_FTP_TLS must be none, explicit or implicit" >&2; exit 2 ;;
esac
export RESTIC_REPOSITORY="rclone:rrarchive:${repo_path}"

tag_args=()
[ -n "$tags" ] && tag_args=(--tag "$tags")

case "$mode" in
  list)
    restic snapshots --no-lock "${tag_args[@]}"
    ;;
  restore)
    cache="${4:?cache dir required}"
    [ -n "$tags" ] || { echo "archive-restore: restore needs a tag filter" >&2; exit 2; }
    shift 4
    id="$(restic snapshots --no-lock --json "${tag_args[@]}" \
      | python3 -c 'import json,sys; s=sorted(json.load(sys.stdin), key=lambda x: x["time"]); print(s[-1]["short_id"] if s else "")')"
    if [ -z "$id" ]; then
      echo "archive-restore: no snapshot in '$repo_path' has tags '$tags'" >&2
      exit 1
    fi
    dest="$cache/$id"
    if [ -f "$dest.restored" ]; then
      echo "archive-restore: reusing $id from $dest" >&2
    else
      echo "archive-restore: restoring $id ($tags) from '$repo_path'" >&2
      rm -rf "$dest" "$dest.restored"
      mkdir -p "$dest"
      restic restore --no-lock "$id" --target "$dest" "$@" >&2
      touch "$dest.restored"
    fi
    echo "$dest"
    ;;
  *)
    echo "archive-restore: unknown mode '$mode'" >&2
    exit 2
    ;;
esac
