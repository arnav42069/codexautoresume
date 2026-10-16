#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if ! node -e 'const m=require("./launcher/release.json"); process.exit(m.platforms?.[process.platform+"-"+process.arch]?0:1)'; then
  echo 'Native build is checked; npm asset smoke test starts when this platform has published checksum pins.'
  exit 0
fi
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export AUTORESUME_DATA_HOME="$work/state" AUTORESUME_DESKTOP_DIR="$work/Desktop" XDG_DATA_HOME="$work/data"
source_url="https://github.com/${GITHUB_REPOSITORY:?}/archive/${GITHUB_SHA:?}.tar.gz"
npm install --global --foreground-scripts --prefix "$work/npm" "$source_url"
command="$work/npm/bin/codexautoresume"
"$command" --version
if [ "$(uname -s)" = Linux ]; then xvfb-run -a "$command" --self-test; else "$command" --self-test; fi
"$command" install --probe
"$command" shortcut
test -f "$AUTORESUME_DATA_HOME/desktop-shortcut.json"
npm uninstall --global --prefix "$work/npm" codexautoresume
test ! -e "$command"
echo 'PASS npm install, verified download, desktop shortcut, native startup, diagnostic and uninstall'
