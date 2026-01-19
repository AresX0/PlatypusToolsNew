<#
.SYNOPSIS
    Complete release build script for PlatypusTools.
    
.DESCRIPTION
    This script performs a COMPLETE clean build of both the self-contained executable
    and the MSI installer. It handles all the steps required to avoid stale file issues:
    
    1. Archives existing build artifacts (optional, with -Archive flag)
    2. Cleans ALL bin/obj/publish directories
    3. Restores packages
    4. Publishes to the correct location for MSI source files
    5. Regenerates PublishFiles.wxs from fresh publish
    6. Builds the MSI installer
    7. Publishes the single-file self-contained executable
    8. Verifies version numbers match across all outputs
    
.PARAMETER Archive
    If specified, archives existing build artifacts before cleaning.
    Archives are stored in LocalArchive\OldBuilds_<timestamp>
    
.PARAMETER SkipClean
    If specified, skips the clean step (NOT RECOMMENDED for releases)
    
.PARAMETER Version
    Expected version number to verify in outputs. If not specified, reads from csproj.
    
.EXAMPLE
    .\Build-Release.ps1
    Performs a complete clean build.
    
.EXAMPLE
    .\Build-Release.ps1 -Archive
    Archives existing artifacts, then performs a complete clean build.
    
.EXAMPLE
    .\Build-Release.ps1 -Version "3.2.2.5"
    Performs a clean build and verifies all outputs are version 3.2.2.5
    
.NOTES
    CRITICAL: Always use this script for release builds to avoid stale file issues.
    
    The MSI build sources files from:
    PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
    
    The PublishFiles.wxs MUST be regenerated after each publish to capture:
    - New files added to the project
    - Files removed from the project
    - Updated file contents
    
    Build artifacts:
    - Self-contained EXE: publish\PlatypusTools.UI.exe
    - MSI Installer: PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi
#>

param(
    [switch]$Archive,
    [switch]$SkipClean,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
Set-Location $ProjectRoot

# Console colors
function Write-Step { param([string]$Message) Write-Host "`n=== $Message ===" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Warning { param([string]$Message) Write-Host "⚠ $Message" -ForegroundColor Yellow }
function Write-Error { param([string]$Message) Write-Host "✗ $Message" -ForegroundColor Red }

Write-Host "`n" -NoNewline
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║        PlatypusTools Release Build Script                     ║" -ForegroundColor Magenta
Write-Host "║        Clean Build → MSI + Self-Contained EXE                 ║" -ForegroundColor Magenta
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

# Get version from csproj if not specified
if (-not $Version) {
    $csprojPath = Join-Path $ProjectRoot "PlatypusTools.UI\PlatypusTools.UI.csproj"
    $csproj = [xml](Get-Content $csprojPath)
    $Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    Write-Host "Detected version from csproj: $Version" -ForegroundColor Gray
}

Write-Host "`nBuild Configuration:" -ForegroundColor White
Write-Host "  Version: $Version" -ForegroundColor Gray
Write-Host "  Archive: $Archive" -ForegroundColor Gray
Write-Host "  SkipClean: $SkipClean" -ForegroundColor Gray

# === STEP 1: Archive existing artifacts ===
if ($Archive) {
    Write-Step "Archiving existing build artifacts"
    
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $archiveDir = Join-Path $ProjectRoot "LocalArchive\OldBuilds_$timestamp"
    New-Item -ItemType Directory -Path $archiveDir -Force | Out-Null
    
    $dirsToArchive = @(
        @{ Source = "publish"; Dest = "publish" },
        @{ Source = "releases"; Dest = "releases" },
        @{ Source = "PlatypusTools.Installer\bin"; Dest = "Installer_bin" },
        @{ Source = "PlatypusTools.Installer\publish"; Dest = "Installer_publish" },
        @{ Source = "PlatypusTools.UI\bin"; Dest = "UI_bin" }
    )
    
    foreach ($dir in $dirsToArchive) {
        $sourcePath = Join-Path $ProjectRoot $dir.Source
        if (Test-Path $sourcePath) {
            $destPath = Join-Path $archiveDir $dir.Dest
            Copy-Item -Path $sourcePath -Destination $destPath -Recurse -Force
            Write-Success "Archived: $($dir.Source)"
        }
    }
    
    Write-Success "Archive created: $archiveDir"
}

# === STEP 2: Clean build directories ===
if (-not $SkipClean) {
    Write-Step "Cleaning ALL build directories"
    
    # Critical directories that must be cleaned for MSI builds
    $criticalDirs = @(
        "publish",
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
    
    foreach ($dir in $criticalDirs) {
        $fullPath = Join-Path $ProjectRoot $dir
        if (Test-Path $fullPath) {
            Remove-Item -Path $fullPath -Recurse -Force
            Write-Success "Removed: $dir"
        }
    }
    
    Write-Success "All build directories cleaned"
} else {
    Write-Warning "Skipping clean step (NOT RECOMMENDED for release builds)"
}

# === STEP 3: Restore packages ===
Write-Step "Restoring NuGet packages"
dotnet restore
if ($LASTEXITCODE -ne 0) { throw "Package restore failed" }
Write-Success "Packages restored"

# === STEP 4: Publish for MSI source ===
Write-Step "Publishing application for MSI source files"
Write-Host "Target: PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\" -ForegroundColor Gray

# This publish is NOT single-file - it's the source for the MSI
dotnet publish PlatypusTools.UI\PlatypusTools.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) { throw "Publish for MSI source failed" }

# Verify publish directory exists
$msiSourceDir = Join-Path $ProjectRoot "PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
if (-not (Test-Path $msiSourceDir)) {
    throw "MSI source directory not found: $msiSourceDir"
}

$fileCount = (Get-ChildItem -Path $msiSourceDir -Recurse -File).Count
Write-Success "Published $fileCount files for MSI source"

# === STEP 5: Regenerate PublishFiles.wxs ===
Write-Step "Regenerating PublishFiles.wxs"
Write-Host "CRITICAL: This must be done after every publish!" -ForegroundColor Yellow

Push-Location (Join-Path $ProjectRoot "PlatypusTools.Installer")
try {
    & powershell -ExecutionPolicy Bypass -File .\GeneratePublishWxs.ps1 -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "GeneratePublishWxs.ps1 failed" }
} finally {
    Pop-Location
}

Write-Success "PublishFiles.wxs regenerated"

# === STEP 6: Build MSI installer ===
Write-Step "Building MSI installer"

dotnet restore PlatypusTools.Installer\PlatypusTools.Installer.wixproj
dotnet build PlatypusTools.Installer\PlatypusTools.Installer.wixproj -c Release

if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

$msiPath = Join-Path $ProjectRoot "PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi"
if (-not (Test-Path $msiPath)) {
    throw "MSI not found at expected location: $msiPath"
}

$msiInfo = Get-Item $msiPath
$msiSizeMB = [math]::Round($msiInfo.Length / 1MB, 2)
Write-Success "MSI built: $msiPath"
Write-Host "  Size: $msiSizeMB MB" -ForegroundColor Gray
Write-Host "  Created: $($msiInfo.LastWriteTime)" -ForegroundColor Gray

# === STEP 7: Publish self-contained single-file EXE ===
Write-Step "Publishing self-contained single-file executable"

# Create publish directory
$publishDir = Join-Path $ProjectRoot "publish"
if (-not (Test-Path $publishDir)) {
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
}

dotnet publish PlatypusTools.UI\PlatypusTools.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "Single-file publish failed" }

$exePath = Join-Path $publishDir "PlatypusTools.UI.exe"
if (-not (Test-Path $exePath)) {
    throw "EXE not found at expected location: $exePath"
}

$exeInfo = Get-Item $exePath
$exeSizeMB = [math]::Round($exeInfo.Length / 1MB, 2)
Write-Success "EXE built: $exePath"
Write-Host "  Size: $exeSizeMB MB" -ForegroundColor Gray
Write-Host "  Created: $($exeInfo.LastWriteTime)" -ForegroundColor Gray

# === STEP 8: Version verification ===
Write-Step "Verifying version numbers"

# Check EXE version
$exeVersion = (Get-Item $exePath).VersionInfo.FileVersion
# Trim any extra .0 from the end
$exeVersionClean = $exeVersion -replace '\.0$', ''
if ($exeVersionClean -ne $Version) {
    Write-Warning "EXE version mismatch: Expected $Version, got $exeVersionClean"
} else {
    Write-Success "EXE version: $exeVersionClean"
}

# Check MSI version from Product.wxs
$productWxsPath = Join-Path $ProjectRoot "PlatypusTools.Installer\Product.wxs"
$productWxsContent = Get-Content $productWxsPath -Raw
if ($productWxsContent -match 'Version="([^"]+)"') {
    $msiVersion = $Matches[1]
    if ($msiVersion -ne $Version) {
        Write-Warning "MSI version mismatch: Expected $Version, got $msiVersion"
    } else {
        Write-Success "MSI version: $msiVersion"
    }
}

# === Summary ===
Write-Host "`n" -NoNewline
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                    BUILD COMPLETE                             ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Green

Write-Host "`nBuild Artifacts:" -ForegroundColor White
Write-Host "  Self-contained EXE: $exePath" -ForegroundColor Gray
Write-Host "                      Size: $exeSizeMB MB" -ForegroundColor Gray
Write-Host "  MSI Installer:      $msiPath" -ForegroundColor Gray
Write-Host "                      Size: $msiSizeMB MB" -ForegroundColor Gray

Write-Host "`nVersion: $Version" -ForegroundColor Cyan

Write-Host "`n⚠ IMPORTANT: Before uploading to GitHub, verify:" -ForegroundColor Yellow
Write-Host "  1. Test the MSI installer on a clean machine" -ForegroundColor Yellow
Write-Host "  2. Verify installed version matches $Version" -ForegroundColor Yellow
Write-Host "  3. Test the self-contained EXE runs correctly" -ForegroundColor Yellow

Write-Host "`n"
