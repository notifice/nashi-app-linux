#!/usr/bin/env bash
# Builds build/Nashi-Linux-x64.tar.gz (+ .sha256) and, with --appimage,
# build/Nashi-Linux-x86_64.AppImage from windows/Nashi.Linux.
set -euo pipefail

APPIMAGETOOL_VERSION="1.9.1"
APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_VERSION}/appimagetool-x86_64.AppImage"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/windows/Nashi.Linux"
PKG="$PROJECT/packaging"
BUILD="$ROOT/build"
PUBLISH="$BUILD/linux/Nashi"
TARBALL="$BUILD/Nashi-Linux-x64.tar.gz"
APPIMAGE="$BUILD/Nashi-Linux-x86_64.AppImage"

want_appimage=0
for arg in "$@"; do
  case "$arg" in
    --appimage) want_appimage=1 ;;
    *) echo "usage: $0 [--appimage]" >&2; exit 2 ;;
  esac
done

SOURCE_DATE_EPOCH="$(git -C "$ROOT" log -1 --format=%ct)"
export SOURCE_DATE_EPOCH

# MimeType= in nashi.desktop must match Resources/FeatureCatalog.json.
python3 "$PKG/gen-mimetypes.py" --check

rm -rf "$BUILD/linux"
mkdir -p "$BUILD/linux"
dotnet restore "$PROJECT" --locked-mode
# NASHI_VERSION (e.g. 0.7.17.1, set by the release workflow) stamps the assembly version the update check compares against.
VERSION_ARGS=()
if [ -n "${NASHI_VERSION:-}" ]; then
  [[ "$NASHI_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]] || { echo "NASHI_VERSION must look like 0.7.17.1" >&2; exit 2; }
  VERSION_ARGS=(-p:Version="$NASHI_VERSION")
fi
dotnet publish "$PROJECT" -c Release --no-restore -o "$PUBLISH" "${VERSION_ARGS[@]}"

# Packaging files shipped inside the tarball (icons: 256 pre-sized, 1024 source for install.sh resizing).
cp "$PKG/install.sh" "$PKG/nashi.desktop" "$PKG/nashi-256.png" "$PUBLISH/"
cp "$ROOT/Resources/Brand/NashiIcon.png" "$PUBLISH/nashi.png"
chmod 755 "$PUBLISH/install.sh"

rm -f "$TARBALL" "$TARBALL.sha256"
tar -C "$BUILD/linux" --sort=name --owner=0 --group=0 --numeric-owner \
  --mtime="@$SOURCE_DATE_EPOCH" -cf - Nashi | gzip -n -9 > "$TARBALL"
(cd "$BUILD" && sha256sum "$(basename "$TARBALL")" > "$(basename "$TARBALL").sha256")
echo "Built $TARBALL"

if [ "$want_appimage" -eq 1 ]; then
  TOOLS="$BUILD/tools"
  TOOL="$TOOLS/appimagetool-$APPIMAGETOOL_VERSION-x86_64.AppImage"
  mkdir -p "$TOOLS"
  if [ ! -f "$TOOL" ] || ! echo "$APPIMAGETOOL_SHA256  $TOOL" | sha256sum -c --status; then
    curl -fsSL -o "$TOOL" "$APPIMAGETOOL_URL"
  fi
  echo "$APPIMAGETOOL_SHA256  $TOOL" | sha256sum -c --quiet \
    || { echo "appimagetool checksum mismatch" >&2; rm -f "$TOOL"; exit 1; }
  chmod 755 "$TOOL"

  APPDIR="$BUILD/linux/Nashi.AppDir"
  rm -rf "$APPDIR"
  mkdir -p "$APPDIR/usr/lib"
  cp -a "$PUBLISH" "$APPDIR/usr/lib/nashi"
  rm -f "$APPDIR/usr/lib/nashi/install.sh" "$APPDIR/usr/lib/nashi/nashi.desktop" \
        "$APPDIR/usr/lib/nashi/nashi-256.png" "$APPDIR/usr/lib/nashi/nashi.png"
  cp "$PKG/AppRun" "$APPDIR/AppRun"
  chmod 755 "$APPDIR/AppRun"
  cp "$PKG/nashi.desktop" "$APPDIR/nashi.desktop"
  cp "$PKG/nashi-256.png" "$APPDIR/nashi.png"
  cp "$PKG/nashi-256.png" "$APPDIR/.DirIcon"

  # appimagetool is itself an AppImage; without FUSE 2 it must self-extract.
  tool_args=()
  if ! { [ -e /dev/fuse ] && command -v fusermount >/dev/null 2>&1 && ldconfig -p 2>/dev/null | grep -q 'libfuse\.so\.2'; }; then
    tool_args+=(--appimage-extract-and-run)
  fi
  rm -f "$APPIMAGE"
  ARCH=x86_64 "$TOOL" "${tool_args[@]}" "$APPDIR" "$APPIMAGE"
  echo "Built $APPIMAGE"
fi
