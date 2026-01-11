# PlatypusTools MSI Installer

This project creates a Windows Installer (MSI) package for PlatypusTools using the WiX Toolset v5.

## Prerequisites

- .NET 10 SDK
- WiX Toolset v5.0.2 (installed via dotnet tool)
- Visual Studio 2022 or higher (recommended)

## Building the Installer

### From Command Line

1. Build the main application first:
   ```powershell
   cd ..\PlatypusTools.UI
   dotnet build -c Release
   ```

2. Build the installer:
   ```powershell
   cd ..\PlatypusTools.Installer
   dotnet build
   ```

3. The MSI file will be output to:
   ```
   bin\Debug\PlatypusToolsSetup.msi
   ```

### From Visual Studio

1. Set the build configuration to Release
2. Build the PlatypusTools.UI project first
3. Build the PlatypusTools.Installer project
4. The MSI will be in the bin\Release folder

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
