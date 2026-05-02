#!/usr/bin/env bash
# Build self-contained Linux x64 binary.
# Produces TWO variants:
#   - dist/linux-x64       (Full edition)
#   - dist/linux-x64-media (Multimedia & Streaming edition)
# Run from repo root or from cross-platform/.
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

build_variant() {
    local edition="$1"   # "Full" or "Media"
    local suffix="$2"    # "" or "-media"
    local out="dist/linux-x64${suffix}"
    rm -rf "$out"
    mkdir -p "$out"

    dotnet publish PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        /p:PublishSingleFile=true \
        /p:IncludeNativeLibrariesForSelfExtract=true \
        /p:DebugType=None \
        /p:DebugSymbols=false \
        -o "$out"

    # Drop edition.txt sidecar so EditionService picks the right edition.
    echo "$edition" > "$out/edition.txt"

    local tar="dist/PlatypusTools${suffix}-linux-x64.tar.gz"
    tar -czf "$tar" -C dist "linux-x64${suffix}"
    echo "✓ ${edition} build: $out"
    echo "✓ Archive: $tar"
}

build_variant "Full"  ""
build_variant "Media" "-media"
