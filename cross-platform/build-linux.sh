#!/usr/bin/env bash
# Build self-contained Linux x64 binary.
# Run from repo root or from cross-platform/.
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

OUT="dist/linux-x64"
rm -rf "$OUT"
mkdir -p "$OUT"

dotnet publish PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:DebugType=None \
    /p:DebugSymbols=false \
    -o "$OUT"

# Tarball
TAR="dist/PlatypusTools-linux-x64.tar.gz"
tar -czf "$TAR" -C dist linux-x64
echo "✓ Built: $OUT"
echo "✓ Archive: $TAR"
