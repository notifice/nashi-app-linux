#!/usr/bin/env bash
# Per-user installer for the Nashi tarball. Run from the extracted Nashi/ directory.
#   ./install.sh              install
#   ./install.sh --uninstall  remove exactly what install put in place
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
OPT="$HOME/.local/opt/nashi"
LINK="$HOME/.local/bin/nashi"
DESKTOP="$DATA/applications/nashi.desktop"
ICON_SIZES=(256 128 64)

refresh_caches() {
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA/applications" || true
  command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$DATA/icons/hicolor" 2>/dev/null || true
}

uninstall() {
  if [ -L "$LINK" ] && [ "$(readlink "$LINK")" = "$OPT/Nashi" ]; then rm -f "$LINK"; fi
  rm -rf "$OPT"
  rm -f "$DESKTOP"
  for s in "${ICON_SIZES[@]}"; do rm -f "$DATA/icons/hicolor/${s}x${s}/apps/nashi.png"; done
  refresh_caches
  echo "Nashi uninstalled."
}

if [ "${1:-}" = "--uninstall" ]; then uninstall; exit 0; fi

[ -x "$SRC/Nashi" ] || { echo "install.sh must run from the extracted Nashi directory (Nashi executable not found)." >&2; exit 1; }

rm -rf "$OPT"
mkdir -p "$OPT" "$HOME/.local/bin" "$DATA/applications"
cp -a "$SRC/." "$OPT/"
ln -sfn "$OPT/Nashi" "$LINK"

# Absolute Exec so the launcher works even when ~/.local/bin is not on the desktop session PATH.
sed "s|^Exec=nashi\\b|Exec=$OPT/Nashi|" "$SRC/nashi.desktop" > "$DESKTOP"

mkdir -p "$DATA/icons/hicolor/256x256/apps"
cp "$SRC/nashi-256.png" "$DATA/icons/hicolor/256x256/apps/nashi.png"
if command -v magick >/dev/null 2>&1 && [ -f "$SRC/nashi.png" ]; then
  for s in 128 64; do
    mkdir -p "$DATA/icons/hicolor/${s}x${s}/apps"
    magick "$SRC/nashi.png" -resize "${s}x${s}" "$DATA/icons/hicolor/${s}x${s}/apps/nashi.png"
  done
fi

refresh_caches
echo "Nashi installed to $OPT (command: $LINK)."
case ":$PATH:" in *":$HOME/.local/bin:"*) ;; *) echo "Note: $HOME/.local/bin is not on your PATH." ;; esac
