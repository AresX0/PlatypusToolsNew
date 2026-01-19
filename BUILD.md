# PlatypusTools.NET - Build Guide

Complete instructions for building PlatypusTools.NET from source, including the MSI installer.

## ⚠️ CRITICAL: Avoiding Stale Build Issues

> **WARNING**: The MSI installer build system has a known pitfall that can cause the installer to package OLD files instead of your latest changes. **ALWAYS follow the release build process below.**

### The Problem
The MSI build sources files from:
```
PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

The `PublishFiles.wxs` file references these files by path. If you:
- Build without cleaning first
- Use cached files from a previous build
- Forget to regenerate `PublishFiles.wxs`

...the MSI will contain **old files** even though you made code changes!

### The Solution: Use Build-Release.ps1

**For ALL release builds, use the automated script:**

```powershell
cd C:\Projects\PlatypusToolsNew
.\Build-Release.ps1
```

This script:
1. ✅ Cleans ALL bin/obj/publish directories
2. ✅ Publishes fresh files to the correct MSI source location
3. ✅ Regenerates `PublishFiles.wxs` with current files
4. ✅ Builds the MSI from clean state
5. ✅ Creates the self-contained single-file EXE
6. ✅ Verifies version numbers match

**Optional flags:**
```powershell
# Archive existing builds before cleaning
.\Build-Release.ps1 -Archive

# Verify specific version
.\Build-Release.ps1 -Version "3.2.3.0"
```

### Build Outputs
After running `Build-Release.ps1`:
- **Self-contained EXE**: `publish\PlatypusTools.UI.exe` (~191 MB)
- **MSI Installer**: `PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi` (~152 MB)

---

## Table of Contents
- [Critical Build Warning](#️-critical-avoiding-stale-build-issues)
- [Prerequisites](#prerequisites)
- [Getting the Source Code](#getting-the-source-code)
- [Building the Application](#building-the-application)
- [Building the MSI Installer](#building-the-msi-installer)
- [Running Tests](#running-tests)
- [Troubleshooting](#troubleshooting)

## Prerequisites

### Required Software

1. **Visual Studio 2022** (Community, Professional, or Enterprise)
   - Download: https://visualstudio.microsoft.com/downloads/
   - Required workloads:
     - `.NET desktop development`
     - `Desktop development with C++` (for WiX)
   - Individual components:
     - `.NET 10.0 SDK` (or latest)
     - `Windows 10/11 SDK`

2. **.NET 10.0 SDK**
   - Download: https://dotnet.microsoft.com/download/dotnet/10.0
   - Verify installation:
     ```powershell
     dotnet --version
     # Should show: 10.0.101 or higher
     ```

3. **WiX Toolset 6.0** (for MSI installer)
   - Install via .NET tool:
     ```powershell
     dotnet tool install --global wix --version 6.0.1
     ```
   - Verify installation:
     ```powershell
     wix --version
     # Should show: 6.0.1 or higher
     ```

4. **Git** (optional, for cloning repository)
   - Download: https://git-scm.com/downloads

### Optional Tools

- **Visual Studio Code** - Lightweight alternative editor
- **PowerShell 7+** - Enhanced terminal experience
- **Windows Terminal** - Modern terminal application

## Getting the Source Code

### Option 1: Clone via Git
```powershell
# HTTPS
git clone https://github.com/AresX0/PlatypusToolsNew.git

# SSH
git clone git@github.com:AresX0/PlatypusToolsNew.git

# Navigate to .NET project
cd PlatypusToolsNew\PlatypusTools.Net
```

### Option 2: Download ZIP
1. Go to https://github.com/AresX0/PlatypusToolsNew
2. Click **Code** → **Download ZIP**
3. Extract to `C:\Projects\PlatypusTools`
4. Navigate to `PlatypusTools.Net` folder

## Building the Application

### Method 1: Visual Studio (Recommended for Development)

1. **Open Solution**
   - Double-click `PlatypusTools.sln`
   - Or: File → Open → Project/Solution → Select `PlatypusTools.sln`

2. **Restore NuGet Packages**
   - Visual Studio should auto-restore on open
   - Or: Right-click solution → Restore NuGet Packages
   - Or: Tools → NuGet Package Manager → Restore

3. **Select Build Configuration**
   - Debug (for development): Includes debugging symbols
   - Release (for production): Optimized, no debug symbols
   - Select from dropdown: `Release` | `x64`

4. **Build Solution**
   - Press `Ctrl+Shift+B`
   - Or: Build → Build Solution
   - Or: Right-click solution → Build

5. **Run Application**
   - Press `F5` (with debugging)
   - Or: `Ctrl+F5` (without debugging)
   - Or: Debug → Start Debugging / Start Without Debugging

### Method 2: Command Line (.NET CLI)

```powershell
# Navigate to solution directory
cd C:\Projects\Platypustools\PlatypusTools.Net

# Restore dependencies
dotnet restore

# Build Debug version
dotnet build -c Debug

# Build Release version (recommended for distribution)
dotnet build -c Release

# Run application (Debug)
dotnet run --project PlatypusTools.UI\PlatypusTools.UI.csproj

# Run application (Release)
dotnet run --project PlatypusTools.UI\PlatypusTools.UI.csproj -c Release
```

### Build Output Locations

**Debug Build:**
```
PlatypusTools.UI\bin\Debug\net10.0-windows\
├── PlatypusTools.UI.exe          # Main executable
├── PlatypusTools.Core.dll        # Core library
├── *.dll                         # Dependencies
└── [External Tools]              # FFmpeg, ExifTool, etc.
```

**Release Build:**
```
PlatypusTools.UI\bin\Release\net10.0-windows\
├── PlatypusTools.UI.exe
├── PlatypusTools.Core.dll
└── [Dependencies and Tools]
```

## Building the MSI Installer

### ⚠️ IMPORTANT: MSI Build Process

**The MSI build is the most error-prone part of the release process.** The installer packages files from a SPECIFIC location, and if those files are stale, the MSI will contain old code.

### Recommended: Use Build-Release.ps1

```powershell
cd C:\Projects\PlatypusToolsNew
.\Build-Release.ps1
```

This handles everything automatically. See [Critical Build Warning](#️-critical-avoiding-stale-build-issues).

### Manual Build Process (NOT recommended for releases)

If you must build manually, follow these steps **EXACTLY**:

#### Step 1: Clean ALL build directories

```powershell
cd C:\Projects\PlatypusToolsNew

# Remove ALL cached builds - DO NOT SKIP THIS
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue `
    publish, `
    PlatypusTools.UI\bin, `
    PlatypusTools.UI\obj, `
    PlatypusTools.Core\bin, `
    PlatypusTools.Core\obj, `
    PlatypusTools.Installer\bin, `
    PlatypusTools.Installer\obj
```

#### Step 2: Publish to MSI source location

```powershell
# This creates the files the MSI will package
dotnet publish PlatypusTools.UI\PlatypusTools.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false

# Verify the publish directory exists with your files
$msiSource = "PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
Get-ChildItem $msiSource -Recurse | Measure-Object | Select-Object Count
```

#### Step 3: Regenerate PublishFiles.wxs

> **CRITICAL**: This step MUST be done after every publish!

```powershell
cd PlatypusTools.Installer
powershell -ExecutionPolicy Bypass -File .\GeneratePublishWxs.ps1 -Configuration Release
cd ..

# Verify component count matches file count
# Should show "Generated PublishFiles.wxs with XXXX components"
```

#### Step 4: Build the MSI

```powershell
dotnet restore PlatypusTools.Installer\PlatypusTools.Installer.wixproj
dotnet build PlatypusTools.Installer\PlatypusTools.Installer.wixproj -c Release

# Verify MSI was created with current timestamp
Get-Item PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi | 
    Select-Object Name, Length, LastWriteTime
```

#### Step 5: Build self-contained EXE

```powershell
dotnet publish PlatypusTools.UI\PlatypusTools.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o publish

# Verify EXE version
(Get-Item publish\PlatypusTools.UI.exe).VersionInfo.FileVersion
```

### Prerequisites for MSI Build

1. **WiX Toolset 6.0** must be installed (see Prerequisites)
2. **Release build** must be successful first
3. **Administrator privileges** may be required

### Build Steps

#### Method 1: Visual Studio

1. **Build Application First**
   - Set configuration to `Release | x64`
   - Build → Build Solution (or Ctrl+Shift+B)
   - Wait for successful build

2. **Build Installer Project**
   - Right-click `PlatypusTools.Installer` project
   - Select `Build`
   - Monitor Output window for progress

3. **Locate MSI**
   - Path: `PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi`
   - Size: ~93 MB
   - Build time: ~60-90 seconds

#### Method 2: Command Line

```powershell
# Navigate to solution directory
cd C:\Projects\Platypustools\PlatypusTools.Net

# Build Release version of application first
dotnet build -c Release

# Build installer
dotnet build PlatypusTools.Installer\PlatypusTools.Installer.wixproj -c Release

# Check installer details
$msi = Get-Item "PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi"
Write-Host "Installer: $($msi.FullName)"
Write-Host "Size: $([math]::Round($msi.Length / 1MB, 2)) MB"
Write-Host "Created: $($msi.LastWriteTime)"
```

### Expected Build Output

```
Build succeeded in 71.6s
  PlatypusTools.Core succeeded (7.5s)
  PlatypusTools.UI succeeded (2.5s)
  PlatypusTools.Installer succeeded (60.5s) → PlatypusToolsSetup.msi

Warnings: 4 (benign - analyzer version notifications)
Errors: 0
```

### Installer Details

- **File**: `PlatypusToolsSetup.msi`
- **Size**: ~93 MB
- **Platform**: x64 (64-bit Windows)
- **Install Location**: `C:\Program Files\PlatypusTools`
- **Uninstall**: Via Windows Settings or Control Panel

## Running Tests

### All Tests

```powershell
# Run all tests
dotnet test

# Minimal verbosity
dotnet test -v minimal

# With detailed output
dotnet test -v normal
```

### Specific Test Project

```powershell
# Core tests only
dotnet test PlatypusTools.Core.Tests\PlatypusTools.Core.Tests.csproj -v minimal

# UI tests only
dotnet test tests\PlatypusTools.UI.Tests\PlatypusTools.UI.Tests.csproj -v minimal
```

### Specific Test Class

```powershell
# Run only FileRenamerServiceTests
dotnet test --filter "FullyQualifiedName~FileRenamerServiceTests"

# Run only DuplicatesScannerTests
dotnet test --filter "FullyQualifiedName~DuplicatesScannerTests"
```

### Expected Test Results

```
Passed: 88
Failed: 0
Skipped: 2 (ParityTests - require archived scripts)
Total: 90
Coverage: 98%
```

## Project Structure

```
PlatypusTools.Net/
├── PlatypusTools.sln                    # Main solution file
├── PlatypusTools.Core/                  # Core library
│   ├── PlatypusTools.Core.csproj       # Project file
│   ├── Services/                        # Business logic
│   ├── Models/                          # Data models
│   └── Utilities/                       # Helpers (ElevationHelper, etc.)
├── PlatypusTools.UI/                    # WPF application
│   ├── PlatypusTools.UI.csproj         # Project file
│   ├── ViewModels/                      # MVVM ViewModels
│   ├── Views/                           # XAML Views
│   ├── Converters/                      # Value converters
│   ├── Services/                        # UI services
│   └── MainWindow.xaml                  # Main window
├── PlatypusTools.Core.Tests/            # Core unit tests
│   └── PlatypusTools.Core.Tests.csproj
├── PlatypusTools.UI.Tests/              # UI tests
│   └── PlatypusTools.UI.Tests.csproj
└── PlatypusTools.Installer/             # WiX installer
    ├── PlatypusTools.Installer.wixproj  # Installer project
    └── Product.wxs                      # WiX configuration
```

## Troubleshooting

### Build Errors

#### "SDK not found" or ".NET 10.0 not found"
**Solution**: Install .NET 10.0 SDK
```powershell
# Check installed SDKs
dotnet --list-sdks

# Download from: https://dotnet.microsoft.com/download/dotnet/10.0
```

#### "WiX not found" when building installer
**Solution**: Install WiX Toolset
```powershell
# Install globally
dotnet tool install --global wix --version 6.0.1

# Verify
wix --version
```

#### "The project file may be invalid" or "TargetFramework not found"
**Solution**: Clean and restore
```powershell
dotnet clean
dotnet restore
dotnet build
```

#### Build warnings about analyzer versions
```
warning: The .NET SDK has newer analyzers with version '10.0.101' 
than what version '8.0.0' of 'Microsoft.CodeAnalysis.NetAnalyzers' provides.
```
**Status**: Benign - can be ignored or suppressed
**Solution** (optional): Update package
```powershell
dotnet add package Microsoft.CodeAnalysis.NetAnalyzers --version 10.0.0
```

### Runtime Errors

#### "PlatypusTools.UI.exe - Entry Point Not Found"
**Solution**: Rebuild Release configuration
```powershell
dotnet clean -c Release
dotnet build -c Release
```

#### "Could not load file or assembly 'PlatypusTools.Core'"
**Solution**: Ensure Core library is built
```powershell
dotnet build PlatypusTools.Core\PlatypusTools.Core.csproj
dotnet build PlatypusTools.UI\PlatypusTools.UI.csproj
```

### Installer Build Errors

#### "heat.exe not found" or "candle.exe not found"
**Solution**: WiX tools not in PATH
```powershell
# Reinstall WiX
dotnet tool uninstall --global wix
dotnet tool install --global wix --version 6.0.1
```

#### Installer build takes very long (>5 minutes)
**Cause**: Large number of files being processed
**Solution**: Normal for first build. Subsequent builds are faster (~60s)

#### "Access denied" when building installer
**Solution**: Run Visual Studio or terminal as Administrator
```powershell
# PowerShell as Admin
Start-Process pwsh -Verb RunAs

# Then build
cd C:\Projects\Platypustools\PlatypusTools.Net
dotnet build PlatypusTools.Installer\PlatypusTools.Installer.wixproj -c Release
```

## Clean Build

If experiencing persistent issues, or **before ANY release build**:

### Recommended: Use Build-Release.ps1 with Archive

```powershell
cd C:\Projects\PlatypusToolsNew

# Archive existing builds, then clean and rebuild
.\Build-Release.ps1 -Archive
```

This archives all existing builds to `LocalArchive\OldBuilds_<timestamp>\` before cleaning.

### Manual Clean Build

```powershell
# Navigate to solution directory
cd C:\Projects\PlatypusToolsNew

# Clean all projects
dotnet clean

# Remove ALL bin, obj, and publish folders (PowerShell)
$foldersToRemove = @(
    "publish",
    "releases",
    "PlatypusTools.UI\bin",
    "PlatypusTools.UI\obj", 
    "PlatypusTools.Core\bin",
    "PlatypusTools.Core\obj",
    "PlatypusTools.Installer\bin",
    "PlatypusTools.Installer\obj",
    "PlatypusTools.Installer\publish",
    "PlatypusTools.Core.Tests\bin",
    "PlatypusTools.Core.Tests\obj",
    "tests\PlatypusTools.Core.Tests\bin",
    "tests\PlatypusTools.Core.Tests\obj",
    "tests\PlatypusTools.UI.Tests\bin",
    "tests\PlatypusTools.UI.Tests\obj"
)

foreach ($folder in $foldersToRemove) {
    if (Test-Path $folder) {
        Remove-Item -Path $folder -Recurse -Force
        Write-Host "Removed: $folder"
    }
}

# Restore packages
dotnet restore

# Rebuild (see MSI section for full release build)
dotnet build -c Release
```

### Archive Instead of Delete

To preserve old builds for debugging or comparison:

```powershell
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$archiveDir = "LocalArchive\OldBuilds_$timestamp"
New-Item -ItemType Directory -Path $archiveDir -Force

# Copy before removing
Copy-Item -Path "publish" -Destination "$archiveDir\publish" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Path "PlatypusTools.Installer\bin" -Destination "$archiveDir\Installer_bin" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Path "PlatypusTools.UI\bin" -Destination "$archiveDir\UI_bin" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Archived to: $archiveDir"
```

## Performance Notes

### Build Times (Typical)
- **Restore**: ~5-15 seconds (first time: 30-60 seconds)
- **Core Library**: ~5-10 seconds
- **UI Project**: ~10-20 seconds
- **Tests**: ~5-10 seconds
- **Installer**: ~60-90 seconds
- **Total Clean Build**: ~2-3 minutes

### Optimization Tips
1. Use Release configuration for production builds
2. Close other applications during installer build
3. Use SSD for faster file I/O
4. Disable antivirus scanning of build directories temporarily

## Continuous Integration

### GitHub Actions Example

```yaml
name: Build and Test

on: [push, pull_request]

jobs:
  build:
    runs-on: windows-latest
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    
    - name: Install WiX
      run: dotnet tool install --global wix --version 6.0.1
    
    - name: Restore dependencies
      run: dotnet restore
      working-directory: ./PlatypusTools.Net
    
    - name: Build
      run: dotnet build -c Release --no-restore
      working-directory: ./PlatypusTools.Net
    
    - name: Test
      run: dotnet test -c Release --no-build --verbosity minimal
      working-directory: ./PlatypusTools.Net
    
    - name: Build Installer
      run: dotnet build PlatypusTools.Installer/PlatypusTools.Installer.wixproj -c Release
      working-directory: ./PlatypusTools.Net
    
    - name: Upload MSI
      uses: actions/upload-artifact@v3
      with:
        name: PlatypusToolsSetup
        path: ./PlatypusTools.Net/PlatypusTools.Installer/bin/x64/Release/PlatypusToolsSetup.msi
```

## Support

For build issues:
1. Check this guide first
2. Review [Troubleshooting](#troubleshooting) section
3. Open an [Issue](https://github.com/yourusername/PlatypusTools/issues) with:
   - Build command used
   - Complete error message
   - .NET SDK version (`dotnet --version`)
   - Visual Studio version (if applicable)

---

**Last Updated**: January 10, 2026
