#!/usr/bin/env bash
# Build Linux AppImages. Requires `appimagetool` in PATH (https://appimage.github.io/appimagetool/).
# Produces TWO AppImages:
#   - dist/PlatypusTools-x86_64.AppImage          (Full edition)
#   - dist/PlatypusTools-Multimedia-x86_64.AppImage (Media edition)
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Step 1: produce both self-contained binaries (Full + Media).
./build-linux.sh

build_appimage() {
    local edition="$1"   # "Full" or "Media"
    local suffix="$2"    # "" or "-media"
    local app_name="$3"  # "PlatypusTools" or "PlatypusTools-Multimedia"
    local desktop_name="$4"

    local appdir="dist/${app_name}.AppDir"
    rm -rf "$appdir"
    mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/256x256/apps"

    cp -r "dist/linux-x64${suffix}"/* "$appdir/usr/bin/"

    cat > "$appdir/${app_name}.desktop" <<EOF
[Desktop Entry]
Name=${desktop_name}
Exec=PlatypusTools
Icon=${app_name,,}
Type=Application
Categories=Utility;AudioVideo;
EOF

    cp "$appdir/${app_name}.desktop" "$appdir/usr/share/applications/"

    local icon_src="PlatypusTools.UI.Avalonia/Assets/app-icon.png"
    local icon="$appdir/${app_name,,}.png"
    if [ -f "$icon_src" ]; then
        cp "$icon_src" "$icon"
    else
        printf '\x89PNG\r\n\x1a\n' > "$icon"
    fi
    cp "$icon" "$appdir/usr/share/icons/hicolor/256x256/apps/"

    cat > "$appdir/AppRun" <<'EOF'
#!/bin/bash
HERE="$(dirname "$(readlink -f "${0}")")"
exec "$HERE/usr/bin/PlatypusTools" "$@"
EOF
    chmod +x "$appdir/AppRun"

    local out="dist/${app_name}-x86_64.AppImage"
    ARCH=x86_64 appimagetool "$appdir" "$out"
    echo "✓ AppImage (${edition}): $out"
}

build_appimage "Full"  ""       "PlatypusTools"            "PlatypusTools"
build_appimage "Media" "-media" "PlatypusTools-Multimedia" "PlatypusTools Multimedia"
