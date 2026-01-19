# Build & Packaging

## ⚠️ CRITICAL: Use the Build Script for Releases

**DO NOT manually build releases.** Use the automated script:

```powershell
cd C:\Projects\PlatypusToolsNew
.\Build-Release.ps1
```

This script handles all the steps required to produce correct builds. See [BUILD.md](BUILD.md) for details on why this is necessary.

### The Stale Build Problem

The MSI installer sources files from:
```
PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

If you don't:
1. Clean all build directories
2. Publish fresh files to the correct location
3. Regenerate `PublishFiles.wxs`

...the MSI will contain **OLD FILES** from previous builds!

---

## Requirements

- **[.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)** — verify with `dotnet --list-sdks`
- **[WiX Toolset 6.0](https://wixtoolset.org)** — install via `dotnet tool install --global wix --version 6.0.1`

## Quick Build Commands

### Release Build (RECOMMENDED)

```powershell
.\Build-Release.ps1           # Standard clean build
.\Build-Release.ps1 -Archive  # Archive old builds first
```

### Development Build

```powershell
dotnet build .\PlatypusTools.sln
```

### Run UI Locally

```powershell
dotnet run --project PlatypusTools.UI\PlatypusTools.UI.csproj
```

---

## Build Outputs

| Artifact | Location | Size |
|----------|----------|------|
| Self-contained EXE | `publish\PlatypusTools.UI.exe` | ~191 MB |
| MSI Installer | `PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi` | ~152 MB |

---

## Release Checklist

Before uploading to GitHub:

1. ✅ Update version in ALL files:
   - `PlatypusTools.UI\PlatypusTools.UI.csproj`
   - `PlatypusTools.Core\PlatypusTools.Core.csproj`
   - `PlatypusTools.Installer\Product.wxs`
   - `PlatypusTools.UI\MainWindow.xaml.cs`
   - `PlatypusTools.UI\Views\SettingsWindow.xaml`
   - `PlatypusTools.UI\Views\AboutWindow.xaml`
   - `PlatypusTools.UI\Assets\PlatypusTools_Help.html`

2. ✅ Run `Build-Release.ps1`

3. ✅ Verify build outputs:
   ```powershell
   # Check EXE version
   (Get-Item publish\PlatypusTools.UI.exe).VersionInfo.FileVersion
   
   # Check MSI timestamp (should be recent)
   Get-Item PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi | Select-Object LastWriteTime
   ```

4. ✅ Test the MSI on a clean machine

5. ✅ Create GitHub release:
   ```powershell
   git add -A
   git commit -m "Release vX.Y.Z.W"
   git tag -a vX.Y.Z.W -m "Version X.Y.Z.W"
   git push origin main --tags
   
   gh release create vX.Y.Z.W `
       --title "PlatypusTools vX.Y.Z.W" `
       --notes "Release notes here" `
       "publish\PlatypusTools.UI.exe" `
       "PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi"
   ```

---

## Directory Structure

### Build Directories (cleaned on release build)

| Directory | Purpose |
|-----------|---------|
| `publish\` | Single-file EXE output |
| `PlatypusTools.UI\bin\` | UI project build output |
| `PlatypusTools.UI\bin\Release\...\publish\` | **MSI source files** |
| `PlatypusTools.Installer\bin\` | MSI output |

### Archive Directories (preserved)

| Directory | Purpose |
|-----------|---------|
| `LocalArchive\` | Archived old builds |
| `archive\` | Archived source references |

---

## Logging Configuration

The app reads `appsettings.json` from the application directory:

```json
{
  "LogFile": "C:\\Users\\<you>\\AppData\\Local\\PlatypusTools\\logs\\platypustools.log",
  "LogLevel": "Debug"
}
```

| Key | Description | Default |
|-----|-------------|---------|
| `LogFile` | Path to log file | `%LOCALAPPDATA%\PlatypusTools\logs\platypustools.log` |
| `LogLevel` | Minimum level (Trace, Debug, Info, Warn, Error) | `Info` |

---

## Troubleshooting

### dotnet not found

```powershell
# Add to PATH temporarily
$env:Path = "C:\Program Files\dotnet;$env:Path"

# Add to PATH permanently
$u = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($u -notmatch 'dotnet') { 
    [Environment]::SetEnvironmentVariable('Path', "$u;C:\Program Files\dotnet", 'User') 
}
```

### MSI contains old files

1. Did you use `Build-Release.ps1`? If not, use it.
2. Check `PublishFiles.wxs` was regenerated (look for component count message)
3. Verify MSI timestamp is after your code changes

### WiX build fails

```powershell
# Reinstall WiX
dotnet tool uninstall --global wix
dotnet tool install --global wix --version 6.0.1
```

---

**Last Updated**: January 19, 2026

