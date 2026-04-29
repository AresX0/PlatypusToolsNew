#!/usr/bin/env bash
# Build a portable single-file binary for the host platform.
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

RID="$(dotnet --info | awk -F': *' '/RID:/ {print $2; exit}')"
OUT="dist/portable-$RID"
rm -rf "$OUT"

dotnet publish PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj \
    -c Release \
    -r "$RID" \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:DebugType=None \
    /p:DebugSymbols=false \
    -o "$OUT"

echo "✓ Portable build for $RID at: $OUT"
