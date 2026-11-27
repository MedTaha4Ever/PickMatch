#!/usr/bin/env bash
# Builds a .deb (Ubuntu, Debian, Mint…) from a published PickMatch folder.
# Usage: build-deb.sh <publish-dir> <version> <x64|arm64> <output-dir>
set -euo pipefail
SRC=$1 VER=$2 ARCH=$3 OUT=$4
case "$ARCH" in x64) DEBARCH=amd64 ;; arm64) DEBARCH=arm64 ;; *) echo "unknown arch $ARCH"; exit 1 ;; esac
HERE=$(cd "$(dirname "$0")" && pwd)
PKG=$(mktemp -d)/pickmatch_${VER}_${DEBARCH}

mkdir -p "$PKG/DEBIAN" "$PKG/opt/pickmatch" "$PKG/usr/bin" "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/256x256/apps"
cp -r "$SRC"/. "$PKG/opt/pickmatch/"
chmod +x "$PKG/opt/pickmatch/PickMatch"
ln -s /opt/pickmatch/PickMatch "$PKG/usr/bin/pickmatch"
install -m 644 "$HERE/../../src/PickMatch/Assets/PickMatch.png" "$PKG/usr/share/icons/hicolor/256x256/apps/pickmatch.png"
cat > "$PKG/usr/share/applications/pickmatch.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=PickMatch
GenericName=Photo matching
Comment=Find the original RAW/JPG of the photos your client picked
Exec=/opt/pickmatch/PickMatch %F
Icon=pickmatch
Categories=Graphics;Photography;
Keywords=photo;raw;culling;client;wedding;
Terminal=false
StartupWMClass=PickMatch
DESKTOP

SIZE=$(du -sk "$PKG/opt" | cut -f1)
cat > "$PKG/DEBIAN/control" <<CONTROL
Package: pickmatch
Version: $VER
Section: graphics
Priority: optional
Architecture: $DEBARCH
Installed-Size: $SIZE
Depends: libx11-6, libfontconfig1, libfreetype6, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67
Recommends: tesseract-ocr
Maintainer: PickMatch <MedTaha4Ever@users.noreply.github.com>
Homepage: https://github.com/MedTaha4Ever/PickMatch
Description: Find the original RAW/JPG of the photos your client picked
 Your client sends back the photos they chose as WhatsApp copies, screenshots
 or small JPGs. PickMatch finds the original RAW/JPG of each one, shows the pairs
 side by side and copies the confirmed originals to a separate folder.
CONTROL

mkdir -p "$OUT"
dpkg-deb --build --root-owner-group "$PKG" "$OUT/PickMatch-linux-${ARCH}.deb"
echo "Built $OUT/PickMatch-linux-${ARCH}.deb"
