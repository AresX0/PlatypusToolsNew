#!/usr/bin/env bash
# Build a portable single-file binary for the host platform.
# Produces a Full edition and a Multimedia edition variant.
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

RID="$(dotnet --info | awk -F': *' '/RID:/ {print $2; exit}')"

build_variant() {
    local edition="$1"  # Full|Media
    local suffix="$2"   # "" or "-media"
    local out="dist/portable-${RID}${suffix}"
    rm -rf "$out"

    dotnet publish PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj \
        -c Release \
        -r "$RID" \
        --self-contained true \
        /p:PublishSingleFile=true \
        /p:IncludeNativeLibrariesForSelfExtract=true \
        /p:DebugType=None \
        /p:DebugSymbols=false \
        -o "$out"

    echo "$edition" > "$out/edition.txt"
    echo "✓ Portable (${edition}) build for $RID at: $out"
}

build_variant "Full"  ""
build_variant "Media" "-media"
