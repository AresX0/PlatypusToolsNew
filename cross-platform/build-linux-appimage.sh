#!/usr/bin/env bash
# Build a Linux AppImage. Requires `appimagetool` in PATH (https://appimage.github.io/appimagetool/).
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Step 1: produce the self-contained binary.
./build-linux.sh

APPDIR="dist/PlatypusTools.AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp -r dist/linux-x64/* "$APPDIR/usr/bin/"

cat > "$APPDIR/PlatypusTools.desktop" <<'EOF'
[Desktop Entry]
Name=PlatypusTools
Exec=PlatypusTools
Icon=platypustools
Type=Application
Categories=Utility;
EOF

cp "$APPDIR/PlatypusTools.desktop" "$APPDIR/usr/share/applications/"

# Real app icon — sourced from PlatypusTools.UI.Avalonia/Assets/app-icon.png
ICON_SRC="PlatypusTools.UI.Avalonia/Assets/app-icon.png"
ICON="$APPDIR/platypustools.png"
if [ -f "$ICON_SRC" ]; then
    cp "$ICON_SRC" "$ICON"
else
    # Transparent fallback
    printf '\x89PNG\r\n\x1a\n' > "$ICON"
fi
cp "$ICON" "$APPDIR/usr/share/icons/hicolor/256x256/apps/"

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/bash
HERE="$(dirname "$(readlink -f "${0}")")"
exec "$HERE/usr/bin/PlatypusTools" "$@"
EOF
chmod +x "$APPDIR/AppRun"

OUT="dist/PlatypusTools-x86_64.AppImage"
ARCH=x86_64 appimagetool "$APPDIR" "$OUT"
echo "✓ AppImage: $OUT"
