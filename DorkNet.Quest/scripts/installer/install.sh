#!/usr/bin/env bash
# Install a DorkNet Rec Room .xapk onto a Meta Quest over adb (macOS / Linux).
#
#   ./install.sh [package.xapk] [--serial <id>] [--no-launch]
#
# Needs adb (Android platform-tools) and unzip.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
xapk="" serial="" launch=1
while [ $# -gt 0 ]; do
  case "$1" in
    --serial) serial="$2"; shift 2 ;;
    --no-launch) launch=0; shift ;;
    *) xapk="$1"; shift ;;
  esac
done

say()  { printf '\033[36m==> %s\033[0m\n' "$*"; }
fail() { printf '\033[31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

if [ -z "$xapk" ]; then
  set -- "$here"/*.xapk
  [ -e "$1" ] || fail "no .xapk found next to this script; pass it as an argument."
  [ $# -eq 1 ] || fail "more than one .xapk here; pass the one to install."
  xapk="$1"
fi
[ -f "$xapk" ] || fail "package not found: $xapk"
command -v unzip >/dev/null || fail "unzip is required."

adb="$(command -v adb || true)"
for c in "${ANDROID_SDK_ROOT:-}/platform-tools/adb" "${ANDROID_HOME:-}/platform-tools/adb" \
         "$HOME/Library/Android/sdk/platform-tools/adb" "$HOME/Android/Sdk/platform-tools/adb" \
         "$here/platform-tools/adb"; do
  [ -n "$adb" ] && break
  [ -x "$c" ] && adb="$c"
done
[ -n "$adb" ] || fail "adb not found. Install Android platform-tools: https://developer.android.com/tools/releases/platform-tools"

if [ -z "$serial" ]; then
  ready=$("$adb" devices | awk 'NR>1 && $2=="device"{print $1}')
  count=$(printf '%s' "$ready" | grep -c . || true)
  if [ "$count" -eq 0 ]; then
    if "$adb" devices | grep -q unauthorized; then
      fail 'the headset is connected but not authorised: put it on and accept "Allow USB debugging".'
    fi
    fail 'no headset found. Enable developer mode, connect over USB, and accept "Allow USB debugging".'
  fi
  [ "$count" -eq 1 ] || fail "more than one device connected: $(echo $ready). Pass --serial <id>."
  serial="$ready"
fi
a() { "$adb" -s "$serial" "$@"; }
say "headset: $(a shell getprop ro.product.model | tr -d '\r') ($serial)"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
manifest="$(unzip -p "$xapk" manifest.json)" || fail "not an XAPK: manifest.json missing."
field() { printf '%s' "$manifest" | grep -o "\"$1\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" | head -1 | sed 's/.*:[[:space:]]*"\(.*\)"/\1/'; }
pkg="$(field package_name)"
[ -n "$pkg" ] || fail "manifest.json has no package_name."
say "package: $(field name)  $pkg  $(field version_name) ($(field version_code))"

say "installing the app"
unzip -q -o "$xapk" "$pkg.apk" -d "$tmp"
out="$(a install -r "$tmp/$pkg.apk" 2>&1 || true)"
if printf '%s' "$out" | grep -qE 'INSTALL_FAILED_UPDATE_INCOMPATIBLE|signatures do not match'; then
  echo "An older copy of $pkg is installed with a different signing key; it has to be removed first."
  echo "Your account lives on the server, but local settings on the headset are lost."
  read -r -p "Uninstall it and continue? [y/N] " ans
  case "$ans" in y|Y|yes) ;; *) fail "cancelled." ;; esac
  a uninstall "$pkg" >/dev/null || true
  out="$(a install -r "$tmp/$pkg.apk" 2>&1 || true)"
fi
printf '%s' "$out" | grep -q Success || fail "app install failed: $out"
rm -f "$tmp/$pkg.apk"

# Expansion files: every "install_path" in the manifest.
printf '%s' "$manifest" | grep -o '"install_path"[[:space:]]*:[[:space:]]*"[^"]*"' | sed 's/.*:[[:space:]]*"\(.*\)"/\1/' |
while read -r path; do
  dest="/sdcard/$path"
  size="$(unzip -l "$xapk" "$path" | awk 'NR==4{print $1}')"
  have="$(a shell "stat -c %s '$dest' 2>/dev/null" | tr -d '\r' || true)"
  if [ -n "$size" ] && [ "$have" = "$size" ]; then say "game data already on the headset, skipping"; continue; fi
  say "copying game data - this takes a few minutes"
  unzip -q -o "$xapk" "$path" -d "$tmp"
  a shell "mkdir -p '$(dirname "$dest")'"
  a push "$tmp/$path" "$dest" || fail "copying the game data failed (is there enough free space on the headset?)."
  rm -f "$tmp/$path"
done

echo
printf '\033[32mInstalled. In the headset: Library > Unknown Sources > %s.\033[0m\n' "$(field name)"
if [ "$launch" -eq 1 ]; then
  say "launching (put the headset on)"
  a shell monkey -p "$pkg" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1 || true
fi
