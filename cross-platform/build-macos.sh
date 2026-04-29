#!/usr/bin/env bash
# Build macOS .app bundles for both Intel (osx-x64) and Apple Silicon (osx-arm64).
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

build_arch() {
    local rid="$1"
    local out="dist/$rid"
    local app="dist/PlatypusTools-$rid.app"

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

    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -r "$out"/* "$app/Contents/MacOS/"

    # Copy app icon (PNG; macOS prefers .icns but accepts PNG with CFBundleIconFile)
    if [ -f "PlatypusTools.UI.Avalonia/Assets/app-icon.png" ]; then
        cp "PlatypusTools.UI.Avalonia/Assets/app-icon.png" "$app/Contents/Resources/AppIcon.png"
    fi

    cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>             <string>PlatypusTools</string>
    <key>CFBundleDisplayName</key>      <string>PlatypusTools</string>
    <key>CFBundleIdentifier</key>       <string>com.platypustools.app</string>
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
    echo "✓ Built bundle: $app"
}

build_arch osx-x64
build_arch osx-arm64
echo "Done. Sign with: codesign --deep --sign \"Developer ID Application: ...\" dist/PlatypusTools-osx-arm64.app"
