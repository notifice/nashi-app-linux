#!/usr/bin/env bash
# Installs or updates Nashi for Linux x64 from the latest GitHub release of this fork, per user (~/.local).
#   curl -fsSL https://raw.githubusercontent.com/notifice/nashi-app-linux/main/scripts/install-linux.sh | bash
#   bash install-linux.sh --uninstall
# Set NASHI_RELEASE=v0.7.17.1 to install a specific release instead of the latest.
set -euo pipefail

REPO="notifice/nashi-app-linux"
ASSET="Nashi-Linux-x64.tar.gz"
OPT="$HOME/.local/opt/nashi"

if [ "${1:-}" = "--uninstall" ]; then
  if [ -x "$OPT/install.sh" ]; then exec "$OPT/install.sh" --uninstall; fi
  echo "Nashi is not installed in $OPT." >&2; exit 1
fi
[ "$(uname -m)" = x86_64 ] || { echo "Nashi for Linux is built for x86_64 only." >&2; exit 1; }
command -v curl >/dev/null || { echo "curl is required." >&2; exit 1; }

if [ -n "${NASHI_RELEASE:-}" ]; then BASE="https://github.com/$REPO/releases/download/$NASHI_RELEASE"
else BASE="https://github.com/$REPO/releases/latest/download"; fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
echo "Downloading $ASSET from $REPO..."
curl -fL --proto '=https' --retry 3 -o "$WORK/$ASSET" "$BASE/$ASSET"
curl -fsSL --proto '=https' --retry 3 -o "$WORK/$ASSET.sha256" "$BASE/$ASSET.sha256"
(cd "$WORK" && sha256sum -c --quiet "$ASSET.sha256") || { echo "Checksum mismatch; nothing was installed." >&2; exit 1; }

# A running Nashi keeps its files open; ask it to finish first.
if pgrep -x Nashi >/dev/null 2>&1; then echo "Nashi is running. Quit it (tray → Quit Nashi) and run this again." >&2; exit 1; fi

mkdir "$WORK/x"
tar -xzf "$WORK/$ASSET" -C "$WORK/x"
"$WORK/x/Nashi/install.sh"

command -v wl-paste >/dev/null || echo "Tip: install wl-clipboard for the global wheel shortcut (nashi --wheel)."
echo "Done. Start Nashi from your app launcher or run: nashi"
