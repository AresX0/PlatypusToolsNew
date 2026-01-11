# PlatypusTools.NET - Build Guide

Complete instructions for building PlatypusTools.NET from source, including the MSI installer.

## Table of Contents
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

If experiencing persistent issues:

```powershell
# Navigate to solution directory
cd C:\Projects\Platypustools\PlatypusTools.Net

# Clean all projects
dotnet clean

# Remove bin and obj folders (PowerShell)
Get-ChildItem -Recurse -Directory -Filter "bin" | Remove-Item -Recurse -Force
Get-ChildItem -Recurse -Directory -Filter "obj" | Remove-Item -Recurse -Force

# Restore packages
dotnet restore

# Rebuild
dotnet build -c Release

# Build installer
dotnet build PlatypusTools.Installer\PlatypusTools.Installer.wixproj -c Release
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
