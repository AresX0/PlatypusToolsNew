# PlatypusTools v4.0.4.5

## What's new

This release introduces **two installable editions** sharing one codebase:

- **Full edition** — every tool: file management, security, system tools, multimedia, AI/remote, etc.
- **Multimedia edition** *(new)* — multimedia server, streaming, audio player, audio visualizer, video tools, FFmpeg-only. Installs side-by-side with the Full edition.

The edition is gated at runtime via:
1. `--edition=Full|Media` command-line flag
2. `edition.txt` sidecar in the install directory
3. `HKLM\Software\PlatypusTools\Edition` registry value (Windows)
4. `PLATYPUS_EDITION` environment variable

Defaults to Full when nothing is specified.

## Downloads

### Windows (recommended)
| File | Size | Description |
|---|---|---|
| `PlatypusToolsSetup-v4.0.4.5.msi` | 326 MB | **Full edition** signed MSI installer |
| `PlatypusToolsMediaSetup-v4.0.4.5.msi` | 326 MB | **Multimedia edition** signed MSI installer |

### Windows (portable, ARM64)
| File | Size | Description |
|---|---|---|
| `PlatypusTools-Full-win-arm64-v4.0.4.5.zip` | 78 MB | ARM64 portable, Full edition (Avalonia UI) |
| `PlatypusTools-Media-win-arm64-v4.0.4.5.zip` | 78 MB | ARM64 portable, Media edition (Avalonia UI) |

### Linux
| File | Size | Description |
|---|---|---|
| `PlatypusTools-Full-linux-x64-v4.0.4.5.tar.gz` | 77 MB | Linux x64, Full edition |
| `PlatypusTools-Media-linux-x64-v4.0.4.5.tar.gz` | 77 MB | Linux x64, Media edition |
| `PlatypusTools-Full-linux-arm64-v4.0.4.5.tar.gz` | 74 MB | Linux ARM64, Full edition |
| `PlatypusTools-Media-linux-arm64-v4.0.4.5.tar.gz` | 74 MB | Linux ARM64, Media edition |

### macOS
| File | Size | Description |
|---|---|---|
| `PlatypusTools-Full-osx-x64-v4.0.4.5.tar.gz` | 81 MB | macOS Intel, Full edition |
| `PlatypusTools-Media-osx-x64-v4.0.4.5.tar.gz` | 81 MB | macOS Intel, Media edition |
| `PlatypusTools-Full-osx-arm64-v4.0.4.5.tar.gz` | 79 MB | macOS Apple Silicon, Full edition |
| `PlatypusTools-Media-osx-arm64-v4.0.4.5.tar.gz` | 79 MB | macOS Apple Silicon, Media edition |

## Notes

- Linux/macOS/Windows-ARM64 builds use the Avalonia cross-platform UI (`cross-platform/PlatypusTools.UI.Avalonia`). Tab visibility is gated on every platform via the new shared `EditionService` in `PlatypusTools.Core`.
- All Windows MSIs are Authenticode-signed and timestamped (DigiCert).
- macOS tarballs ship the unsigned single-file binary; on first launch run `xattr -dr com.apple.quarantine PlatypusTools` if Gatekeeper blocks it.
- Linux portables: `chmod +x PlatypusTools` before launching.
- The `edition.txt` sidecar is shipped pre-set inside each archive, so the binary picks the right edition automatically.

## From here on

All future Windows changes will be mirrored to the Linux/macOS Avalonia build scripts.
