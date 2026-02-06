<#
.SYNOPSIS
    Complete release build script for PlatypusTools.
    
.DESCRIPTION
    This script performs a COMPLETE clean build of both the self-contained executable
    and the MSI installer. It handles all the steps required to avoid stale file issues:
    
    1. Auto-increments version number (unless -NoVersionBump is specified)
    2. Updates version in csproj and Product.wxs
    3. Archives existing build artifacts (optional, with -Archive flag)
    4. Cleans ALL bin/obj/publish directories
    5. Restores packages
    6. Publishes to the correct location for MSI source files
    7. Regenerates PublishFiles.wxs from fresh publish
    8. Builds the MSI installer
    9. Publishes the single-file self-contained executable
    10. Verifies version numbers match across all outputs
    
.PARAMETER Archive
    If specified, archives existing build artifacts before cleaning.
    Archives are stored in LocalArchive\OldBuilds_<timestamp>
    
.PARAMETER SkipClean
    If specified, skips the clean step (NOT RECOMMENDED for releases)
    
.PARAMETER NoVersionBump
    If specified, does not auto-increment the version number.
    
.PARAMETER Version
    Specific version number to set. Overrides auto-increment.

.PARAMETER Upload
    If specified, automatically creates a GitHub release and uploads the MSI.
    Requires gh CLI to be installed and authenticated.

.PARAMETER ReleaseNotes
    Release notes for the GitHub release. Only used with -Upload.
    
.EXAMPLE
    .\Build-Release.ps1
    Auto-bumps version and performs a complete clean build.
    
.EXAMPLE
    .\Build-Release.ps1 -NoVersionBump
    Builds without changing the version number.
    
.EXAMPLE
    .\Build-Release.ps1 -Version "3.3.0.7"
    Sets version to 3.3.0.7 and performs a clean build.
    
.EXAMPLE
    .\Build-Release.ps1 -Archive
    Archives existing artifacts, auto-bumps version, then performs build.
    
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
    [switch]$NoVersionBump,
    [string]$Version,
    [switch]$Upload,
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
Set-Location $ProjectRoot

# Console colors
function Write-Step { param([string]$Message) Write-Host "`n=== $Message ===" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Warning { param([string]$Message) Write-Host "[!] $Message" -ForegroundColor Yellow }
function Write-Error { param([string]$Message) Write-Host "[X] $Message" -ForegroundColor Red }

Write-Host "`n" -NoNewline
Write-Host "+===============================================================+" -ForegroundColor Magenta
Write-Host "|        PlatypusTools Release Build Script                     |" -ForegroundColor Magenta
Write-Host "|        Clean Build → MSI + Self-Contained EXE                 |" -ForegroundColor Magenta
Write-Host "+===============================================================+" -ForegroundColor Magenta

# Get version from csproj
$csprojPath = Join-Path $ProjectRoot "PlatypusTools.UI\PlatypusTools.UI.csproj"
$productWxsPath = Join-Path $ProjectRoot "PlatypusTools.Installer\Product.wxs"
$csproj = [xml](Get-Content $csprojPath)
$currentVersion = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Current version in csproj: $currentVersion" -ForegroundColor Gray

# Auto-bump version if not specified and not disabled
if (-not $Version -and -not $NoVersionBump) {
    # Parse current version and increment patch
    $versionParts = $currentVersion.Split('.')
    if ($versionParts.Count -ge 4) {
        $versionParts[3] = [int]$versionParts[3] + 1
    } elseif ($versionParts.Count -eq 3) {
        $versionParts += "1"
    }
    $Version = $versionParts -join '.'
    Write-Host "Auto-incremented version: $currentVersion -> $Version" -ForegroundColor Cyan
    
    # Update csproj
    $csprojContent = Get-Content $csprojPath -Raw
    $csprojContent = $csprojContent -replace "<Version>$currentVersion</Version>", "<Version>$Version</Version>"
    Set-Content -Path $csprojPath -Value $csprojContent -NoNewline
    Write-Success "Updated csproj version to $Version"
    
    # Update Product.wxs
    $wxsContent = Get-Content $productWxsPath -Raw
    $wxsContent = $wxsContent -replace 'Version="[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?"', "Version=`"$Version`""
    Set-Content -Path $productWxsPath -Value $wxsContent -NoNewline
    Write-Success "Updated Product.wxs version to $Version"
} elseif ($Version) {
    Write-Host "Using specified version: $Version" -ForegroundColor Cyan
    
    # Update csproj if different
    if ($currentVersion -ne $Version) {
        $csprojContent = Get-Content $csprojPath -Raw
        $csprojContent = $csprojContent -replace "<Version>$currentVersion</Version>", "<Version>$Version</Version>"
        Set-Content -Path $csprojPath -Value $csprojContent -NoNewline
        Write-Success "Updated csproj version to $Version"
        
        # Update Product.wxs
        $wxsContent = Get-Content $productWxsPath -Raw
        $wxsContent = $wxsContent -replace 'Version="[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?"', "Version=`"$Version`""
        Set-Content -Path $productWxsPath -Value $wxsContent -NoNewline
        Write-Success "Updated Product.wxs version to $Version"
    }
} else {
    $Version = $currentVersion
    Write-Host "Using existing version (NoVersionBump): $Version" -ForegroundColor Gray
}

Write-Host "`nBuild Configuration:" -ForegroundColor White
Write-Host "  Version: $Version" -ForegroundColor Gray
Write-Host "  Archive: $Archive" -ForegroundColor Gray
Write-Host "  SkipClean: $SkipClean" -ForegroundColor Gray
Write-Host "  NoVersionBump: $NoVersionBump" -ForegroundColor Gray

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

# === STEP 7b: Copy Content folders to publish directory ===
# Themes and Assets are Content files that need to be copied separately for single-file publish
Write-Step "Copying Content folders (Themes, Assets) to publish directory"

# Copy Themes
$themesSource = Join-Path $ProjectRoot "PlatypusTools.UI\Themes"
$themesDest = Join-Path $publishDir "Themes"

if (Test-Path $themesSource) {
    if (-not (Test-Path $themesDest)) {
        New-Item -ItemType Directory -Path $themesDest -Force | Out-Null
    }
    Copy-Item -Path "$themesSource\*" -Destination $themesDest -Force
    $themeCount = (Get-ChildItem -Path $themesDest -File).Count
    Write-Success "Copied $themeCount theme files to publish\Themes"
} else {
    Write-Warning "Themes source folder not found: $themesSource"
}

# Copy Assets
$assetsSource = Join-Path $ProjectRoot "PlatypusTools.UI\Assets"
$assetsDest = Join-Path $publishDir "Assets"

if (Test-Path $assetsSource) {
    if (-not (Test-Path $assetsDest)) {
        New-Item -ItemType Directory -Path $assetsDest -Force | Out-Null
    }
    Copy-Item -Path "$assetsSource\*" -Destination $assetsDest -Force -Recurse
    $assetCount = (Get-ChildItem -Path $assetsDest -File -Recurse).Count
    Write-Success "Copied $assetCount asset files to publish\Assets"
} else {
    Write-Warning "Assets source folder not found: $assetsSource"
}

# === STEP 8: Code Signing ===
Write-Step "Code Signing EXE and MSI"

$certThumbprint = "B4FA73AB05DC5AE2D245D1C629D5EC59E4FC0D40"
$timestampServer = "http://timestamp.digicert.com"

# Check if certificate exists
$cert = Get-ChildItem Cert:\CurrentUser\My\$certThumbprint -ErrorAction SilentlyContinue
if (-not $cert) {
    $cert = Get-ChildItem Cert:\LocalMachine\My\$certThumbprint -ErrorAction SilentlyContinue
}

if ($cert) {
    Write-Host "Found certificate: $($cert.Subject)" -ForegroundColor Gray
    
    # Sign the EXE
    try {
        $sigResult = Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -TimestampServer $timestampServer
        if ($sigResult.Status -eq "Valid") {
            Write-Success "EXE signed successfully"
        } else {
            Write-Warning "EXE signing status: $($sigResult.Status) - $($sigResult.StatusMessage)"
        }
    } catch {
        Write-Warning "Failed to sign EXE: $_"
    }
    
    # Sign the MSI
    try {
        $sigResult = Set-AuthenticodeSignature -FilePath $msiPath -Certificate $cert -TimestampServer $timestampServer
        if ($sigResult.Status -eq "Valid") {
            Write-Success "MSI signed successfully"
        } else {
            Write-Warning "MSI signing status: $($sigResult.Status) - $($sigResult.StatusMessage)"
        }
    } catch {
        Write-Warning "Failed to sign MSI: $_"
    }
} else {
    Write-Warning "Code signing certificate not found (thumbprint: $certThumbprint)"
    Write-Warning "Skipping code signing - EXE and MSI will be unsigned"
}

# === STEP 9: Version verification ===
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

# Check MSI version from Product.wxs (look for Package Version specifically)
$productWxsPath = Join-Path $ProjectRoot "PlatypusTools.Installer\Product.wxs"
$productWxsContent = Get-Content $productWxsPath -Raw
if ($productWxsContent -match '<Package[^>]*Version="([^"]+)"') {
    $msiVersion = $Matches[1]
    if ($msiVersion -ne $Version) {
        Write-Warning "MSI version mismatch: Expected $Version, got $msiVersion"
    } else {
        Write-Success "MSI version: $msiVersion"
    }
}

# === Summary ===
Write-Host "`n" -NoNewline
Write-Host "+===============================================================+" -ForegroundColor Green
Write-Host "|                    BUILD COMPLETE                             |" -ForegroundColor Green
Write-Host "+===============================================================+" -ForegroundColor Green

Write-Host "`nBuild Artifacts:" -ForegroundColor White
Write-Host "  Self-contained EXE: $exePath" -ForegroundColor Gray
Write-Host "                      Size: $exeSizeMB MB" -ForegroundColor Gray
Write-Host "  MSI Installer:      $msiPath" -ForegroundColor Gray
Write-Host "                      Size: $msiSizeMB MB" -ForegroundColor Gray

Write-Host "`nVersion: $Version" -ForegroundColor Cyan

Write-Host "`n[!] IMPORTANT: Before uploading to GitHub, verify:" -ForegroundColor Yellow
Write-Host "  1. Test the MSI installer on a clean machine" -ForegroundColor Yellow
Write-Host "  2. Verify installed version matches $Version" -ForegroundColor Yellow
Write-Host "  3. Test the self-contained EXE runs correctly" -ForegroundColor Yellow

# Copy MSI to releases folder with version name
$releasesDir = Join-Path $ProjectRoot "releases"
if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
}
$versionedMsiName = "PlatypusToolsSetup-v$Version.msi"
$versionedMsiPath = Join-Path $releasesDir $versionedMsiName
Copy-Item $msiPath $versionedMsiPath -Force
Write-Success "MSI copied to: $versionedMsiPath"

Write-Host "`n[GitHub Release]" -ForegroundColor Cyan
Write-Host "  To create a GitHub release, run:" -ForegroundColor White
Write-Host ""
Write-Host "  # Step 1: Create release WITHOUT file (fast)" -ForegroundColor Gray
Write-Host "  gh release create v$Version --title `"v$Version`" --notes `"Release notes here`"" -ForegroundColor Yellow
Write-Host ""
Write-Host "  # Step 2: Upload MSI separately (wait for completion)" -ForegroundColor Gray  
Write-Host "  gh release upload v$Version `"releases\$versionedMsiName`" --clobber" -ForegroundColor Yellow
Write-Host ""
Write-Host "  # Step 3: Verify upload succeeded" -ForegroundColor Gray
Write-Host "  gh release view v$Version" -ForegroundColor Yellow
Write-Host ""
Write-Host "  [!] CRITICAL: Wait for Step 2 to fully complete before verifying!" -ForegroundColor Red
Write-Host "      Large files (300+ MB) can take 1-2 minutes to upload." -ForegroundColor Red

# Automatic GitHub upload if -Upload specified
if ($Upload) {
    Write-Step "Uploading to GitHub"
    
    # Check if gh CLI is available
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Error "GitHub CLI (gh) not found. Install from https://cli.github.com/"
        exit 1
    }
    
    # Check authentication
    $authStatus = gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Not authenticated with GitHub. Run: gh auth login"
        exit 1
    }
    
    $tagName = "v$Version"
    $notesContent = if ($ReleaseNotes) { $ReleaseNotes } else { "Release v$Version" }
    
    # Step 1: Create release WITHOUT file
    Write-Host "Creating release $tagName..." -ForegroundColor Cyan
    gh release create $tagName --title $tagName --notes $notesContent 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        # Release might already exist, try to continue
        Write-Warning "Release may already exist, attempting to upload asset..."
    } else {
        Write-Success "Release $tagName created"
    }
    
    # Step 2: Upload MSI separately (this is the critical part)
    Write-Host "Uploading MSI (this may take 1-2 minutes for 300+ MB files)..." -ForegroundColor Cyan
    $uploadResult = gh release upload $tagName $versionedMsiPath --clobber 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to upload MSI: $uploadResult"
        exit 1
    }
    Write-Success "MSI uploaded successfully"
    
    # Step 3: Verify upload
    Write-Host "Verifying upload..." -ForegroundColor Cyan
    $releaseInfo = gh release view $tagName --json assets 2>&1 | ConvertFrom-Json
    if ($releaseInfo.assets.Count -eq 0) {
        Write-Error "Upload verification FAILED - no assets found on release!"
        Write-Host "Check manually: gh release view $tagName" -ForegroundColor Yellow
        exit 1
    }
    
    $uploadedAsset = $releaseInfo.assets | Where-Object { $_.name -eq $versionedMsiName }
    if (-not $uploadedAsset) {
        Write-Error "Upload verification FAILED - MSI not found in release assets!"
        exit 1
    }
    
    $assetSizeMB = [math]::Round($uploadedAsset.size / 1MB, 2)
    Write-Success "Verified: $($uploadedAsset.name) ($assetSizeMB MB)"
    
    Write-Host "`n" -NoNewline
    Write-Host "+===============================================================+" -ForegroundColor Green
    Write-Host "|              GITHUB RELEASE PUBLISHED                         |" -ForegroundColor Green
    Write-Host "+===============================================================+" -ForegroundColor Green
    Write-Host "`nRelease URL: https://github.com/AresX0/PlatypusToolsNew/releases/tag/$tagName" -ForegroundColor Cyan
}

Write-Host "`n"

