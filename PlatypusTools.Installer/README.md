# PlatypusTools MSI Installer

This project creates a Windows Installer (MSI) package for PlatypusTools using the WiX Toolset v6.

## ⚠️ CRITICAL: Building the Installer Correctly

> **WARNING**: The MSI build has a known pitfall that can cause it to package OLD files. **ALWAYS use the Build-Release.ps1 script.**

### The Problem

The MSI sources files from:
```
PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

The `PublishFiles.wxs` references these files. If stale files exist in this directory, or if `PublishFiles.wxs` is not regenerated, the MSI will contain **outdated code**.

### The Solution

**Always use the release build script from the project root:**

```powershell
cd C:\Projects\PlatypusToolsNew
.\Build-Release.ps1
```

This script:
1. Cleans ALL build directories
2. Publishes fresh files to the MSI source location
3. Regenerates `PublishFiles.wxs`
4. Builds the MSI

---

## Prerequisites

- .NET 10 SDK
- WiX Toolset v6.0.1 (installed via dotnet tool)
- Visual Studio 2022 or higher (recommended)

## Building the Installer

### ✅ RECOMMENDED: Use Build-Release.ps1

```powershell
cd C:\Projects\PlatypusToolsNew
.\Build-Release.ps1
```

### Manual Build (NOT RECOMMENDED)

If you must build manually, follow these steps **exactly**:

```powershell
# 1. CLEAN all build directories
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue `
    ..\publish, `
    ..\PlatypusTools.UI\bin, `
    ..\PlatypusTools.UI\obj, `
    .\bin, .\obj

# 2. PUBLISH to MSI source location (NOT single-file)
dotnet publish ..\PlatypusTools.UI\PlatypusTools.UI.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false

# 3. REGENERATE PublishFiles.wxs
powershell -ExecutionPolicy Bypass -File .\GeneratePublishWxs.ps1 -Configuration Release

# 4. BUILD the MSI
dotnet restore .\PlatypusTools.Installer.wixproj
dotnet build .\PlatypusTools.Installer.wixproj -c Release
```

### Output Locations

| Artifact | Location | Approximate Size |
|----------|----------|-----------------|
| MSI Installer | `bin\x64\Release\PlatypusToolsSetup.msi` | ~152 MB |

---

## Key Files

| File | Purpose |
|------|---------|
| `Product.wxs` | Main installer definition, version number |
| `PublishFiles.wxs` | **Auto-generated** - lists all files to package |
| `GeneratePublishWxs.ps1` | Script to regenerate PublishFiles.wxs |
| `Shortcuts.wxs` | Start menu and desktop shortcuts |

### PublishFiles.wxs

This file is **automatically generated** by `GeneratePublishWxs.ps1`. 

**DO NOT EDIT MANUALLY** - it will be overwritten.

The script scans the publish directory and creates WiX component entries for every file.

---

## What Gets Installed

The installer packages:
- **Main Application**: PlatypusTools.UI.exe with JosephThePlatypus.ico
- **Core Libraries**: PlatypusTools.Core.dll and dependencies
- **Support Tools**: 
  - ffmpeg.exe
  - ffprobe.exe
  - ffplay.exe
  - exiftool.exe
- **Configuration**: App settings and runtime configuration files
- **Assets**: Help files and icons

## Installation Details

- **Install Location**: C:\Program Files\PlatypusTools
- **Start Menu Shortcut**: Yes
- **Desktop Shortcut**: Yes (optional during install)
- **Admin Rights**: Required
- **.NET Runtime Check**: Verifies .NET 10.0 Desktop Runtime is installed
- **Upgrade Support**: Yes, preserves settings during upgrades

## Features

- Clean uninstall (removes all files and shortcuts)
- Add/Remove Programs integration
- Proper Windows Installer database
- Rollback support on failed installation
- Per-machine installation (ALLUSERS=1)

## Customization

### Changing Version

Edit `Product.wxs`:
```xml
<Package Version="2.0.0.0" ...>
```

### Adding Files

Edit `Files.wxs` and add new `<Component>` entries to the appropriate `<ComponentGroup>`.

### Modifying UI

The installer uses WixUI_InstallDir which provides:
- Welcome dialog
- License agreement
- Installation directory selection
- Installation progress
- Completion dialog

To customize, modify the `<ui:WixUI>` reference in `Product.wxs`.

## Troubleshooting

### Build Errors

1. **Missing files**: Ensure PlatypusTools.UI is built in Release mode first
2. **WiX not found**: Run `dotnet tool restore` in the solution directory
3. **ICE validation errors**: These are warnings about best practices; most can be ignored during development

### Missing External Tools

The installer expects ffmpeg.exe, ffprobe.exe, ffplay.exe, and exiftool.exe in the parent directory.
Download them if missing:
- FFmpeg: https://ffmpeg.org/download.html
- ExifTool: https://exiftool.org/

### Install Errors

- **Error 1603**: Usually means .NET 10 runtime is missing
- **Error 2203**: Permission issues, run installer as administrator
- **Error 1618**: Another installation is in progress

## Deployment

1. Build in Release mode
2. Test the MSI on a clean machine
3. Distribute PlatypusToolsSetup.msi
4. Users double-click to install (will prompt for admin)

## Notes

- The UpgradeCode should NEVER change between versions
- The ProductCode is auto-generated per build
- Large files (>100MB) should use Binary table or cab files
- For code signing, use signtool.exe after build
