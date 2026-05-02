#!/usr/bin/env bash
# Build macOS .app bundles for both Intel (osx-x64) and Apple Silicon (osx-arm64).
# For each architecture, produces a Full edition AND a Multimedia edition bundle.
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

build_arch() {
    local rid="$1"
    local edition="$2"   # "Full" or "Media"
    local suffix="$3"    # "" or "-media"
    local bundle_name
    if [ "$edition" = "Media" ]; then
        bundle_name="PlatypusTools Multimedia"
    else
        bundle_name="PlatypusTools"
    fi

    local out="dist/${rid}${suffix}"
    local app="dist/${bundle_name}-${rid}.app"

    rm -rf "$out" "$app"
    mkdir -p "$out"

    dotnet publish PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj \
        -c Release \
        -r "$rid" \
        --self-contained true \
        /p:PublishSingleFile=false \
        /p:DebugType=None \
        /p:DebugSymbols=false \
        -o "$out"

    # Edition sidecar
    echo "$edition" > "$out/edition.txt"

    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -r "$out"/* "$app/Contents/MacOS/"

    # Copy app icon (PNG; macOS prefers .icns but accepts PNG with CFBundleIconFile)
    if [ -f "PlatypusTools.UI.Avalonia/Assets/app-icon.png" ]; then
        cp "PlatypusTools.UI.Avalonia/Assets/app-icon.png" "$app/Contents/Resources/AppIcon.png"
    fi

    local bundle_id="com.platypustools.app"
    if [ "$edition" = "Media" ]; then
        bundle_id="com.platypustools.media"
    fi

    cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>             <string>${bundle_name}</string>
    <key>CFBundleDisplayName</key>      <string>${bundle_name}</string>
    <key>CFBundleIdentifier</key>       <string>${bundle_id}</string>
    <key>CFBundleVersion</key>          <string>4.0.4.3</string>
    <key>CFBundleShortVersionString</key><string>4.0.4</string>
    <key>CFBundleExecutable</key>       <string>PlatypusTools</string>
    <key>CFBundlePackageType</key>      <string>APPL</string>
    <key>LSMinimumSystemVersion</key>   <string>11.0</string>
    <key>NSHighResolutionCapable</key>  <true/>
    <key>CFBundleIconFile</key>         <string>AppIcon</string>
</dict>
</plist>
EOF

    chmod +x "$app/Contents/MacOS/PlatypusTools"
    echo "✓ Built bundle ($edition): $app"
}

for rid in osx-x64 osx-arm64; do
    build_arch "$rid" "Full"  ""
    build_arch "$rid" "Media" "-media"
done
echo "Done. Sign with: codesign --deep --sign \"Developer ID Application: ...\" dist/PlatypusTools-osx-arm64.app"
