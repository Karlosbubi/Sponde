#!/usr/bin/env bash
set -euo pipefail
if [[ $# != 1 ]]; then echo 'Usage: test-rpm.sh <rpm-file>' >&2; exit 2; fi
package=$(realpath "$1")
dnf install -y "$package"
rpm -q sponde
test -x /usr/bin/sponde
test -x /usr/lib64/sponde/Sponde
desktop-file-validate /usr/share/applications/sponde.desktop
work=$(mktemp -d /tmp/sponde-rpm-smoke.XXXXXXXX)
mkdir -p "$work/data"
printf 'Preserve user data\n' > "$work/data/keep.txt"
Xvfb :99 -screen 0 1280x900x24 > "$work/xvfb.log" 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true; rm -rf -- "$work"' EXIT
for attempt in {1..50}; do [[ -S /tmp/.X11-unix/X99 ]] && break; sleep 0.1; done
DISPLAY=:99 sponde --data-dir "$work/data" > "$work/app.log" 2>&1 &
app=$!
ready=false
for attempt in {1..180}; do
  if ! kill -0 "$app" 2>/dev/null; then cat "$work/app.log"; echo 'Installed app failed startup' >&2; exit 1; fi
  if [[ -f "$work/data/finance.sqlite" ]]; then ready=true; break; fi
  sleep 0.25
done
[[ $ready == true ]] || { cat "$work/app.log"; echo 'Dataset initialization timed out' >&2; exit 1; }
sleep 1
kill -0 "$app"
kill "$app"
wait "$app" || true
test -f "$work/data/finance.sqlite"
dnf remove -y sponde
test ! -e /usr/bin/sponde
test -f "$work/data/keep.txt"
test -f "$work/data/finance.sqlite"
echo 'RPM install, native X11 startup, uninstall and user-data preservation passed.'
