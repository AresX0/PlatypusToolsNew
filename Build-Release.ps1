<#
.SYNOPSIS
    Complete release build script for PlatypusTools.
    
.DESCRIPTION
    THE ONE AND ONLY build script. Performs a complete clean build:
    1. Auto-increments version (patch number)
    2. Updates version in all required files
    3. Cleans all build directories
    4. Builds MSI installer
    5. Copies MSI to releases folder with version in filename
    
.PARAMETER NoVersionBump
    Skip auto-incrementing the version number.
    
.PARAMETER Version
    Override version number (e.g., "3.2.20"). If not specified, auto-increments.
    
.EXAMPLE
    .\Build-Release.ps1
    Auto-increments version and builds.
    
.EXAMPLE
    .\Build-Release.ps1 -Version "3.3.0"
    Sets specific version and builds.
    
.EXAMPLE
    .\Build-Release.ps1 -NoVersionBump
    Builds without changing version.
#>

param(
    [switch]$NoVersionBump,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
Set-Location $ProjectRoot

# === Console Helpers ===
function Write-Step { param([string]$Message) Write-Host "`n=== $Message ===" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "[OK] $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "[!] $Message" -ForegroundColor Yellow }
function Write-Err { param([string]$Message) Write-Host "[X] $Message" -ForegroundColor Red }

Write-Host "`n" -NoNewline
Write-Host "+===============================================================+" -ForegroundColor Magenta
Write-Host "|        PlatypusTools Release Build Script                     |" -ForegroundColor Magenta
Write-Host "|        Auto-Version → Build → Release                         |" -ForegroundColor Magenta
Write-Host "+===============================================================+" -ForegroundColor Magenta

# === STEP 1: Version Management ===
Write-Step "Version Management"

$csprojPath = Join-Path $ProjectRoot "PlatypusTools.UI\PlatypusTools.UI.csproj"
$coreCsprojPath = Join-Path $ProjectRoot "PlatypusTools.Core\PlatypusTools.Core.csproj"
$productWxsPath = Join-Path $ProjectRoot "PlatypusTools.Installer\Product.wxs"

# Read current version from csproj
$csprojContent = Get-Content $csprojPath -Raw
if ($csprojContent -match '<Version>([^<]+)</Version>') {
    $currentVersion = $Matches[1]
} else {
    throw "Could not find Version in csproj"
}

Write-Host "Current version: $currentVersion" -ForegroundColor Gray

if ($Version) {
    # Use specified version
    $newVersion = $Version
    Write-Host "Using specified version: $newVersion" -ForegroundColor Yellow
} elseif ($NoVersionBump) {
    # Keep current version
    $newVersion = $currentVersion
    Write-Host "Keeping current version (no bump)" -ForegroundColor Yellow
} else {
    # Auto-increment patch version
    $parts = $currentVersion -split '\.'
    if ($parts.Count -ge 3) {
        $parts[2] = [int]$parts[2] + 1
        $newVersion = $parts -join '.'
    } else {
        $newVersion = "$currentVersion.1"
    }
    Write-Host "Auto-incremented to: $newVersion" -ForegroundColor Green
}

# Update version in all files
if ($newVersion -ne $currentVersion) {
    Write-Host "Updating version in all files..." -ForegroundColor Gray
    
    # Update PlatypusTools.UI.csproj
    $csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$newVersion</Version>"
    Set-Content $csprojPath $csprojContent -NoNewline
    Write-Success "Updated: PlatypusTools.UI.csproj"
    
    # Update PlatypusTools.Core.csproj
    $coreContent = Get-Content $coreCsprojPath -Raw
    $coreContent = $coreContent -replace '<Version>[^<]+</Version>', "<Version>$newVersion</Version>"
    Set-Content $coreCsprojPath $coreContent -NoNewline
    Write-Success "Updated: PlatypusTools.Core.csproj"
    
    # Update Product.wxs (WiX installer version) - only update the Package Version, not InstallerVersion
    $wxsContent = Get-Content $productWxsPath -Raw
    $wxsContent = $wxsContent -replace '(Package[^>]*\sVersion=")[^"]+(")', "`${1}$newVersion`${2}"
    Set-Content $productWxsPath $wxsContent -NoNewline
    Write-Success "Updated: Product.wxs"
}

$Version = $newVersion
Write-Host "`nBuilding version: $Version" -ForegroundColor Cyan

# === STEP 2: Clean Build Directories ===
Write-Step "Cleaning build directories"

$dirsToClean = @(
    "publish",
    "PlatypusTools.UI\bin",
    "PlatypusTools.UI\obj",
    "PlatypusTools.Core\bin",
    "PlatypusTools.Core\obj",
    "PlatypusTools.Installer\bin",
    "PlatypusTools.Installer\obj"
)

foreach ($dir in $dirsToClean) {
    $fullPath = Join-Path $ProjectRoot $dir
    if (Test-Path $fullPath) {
        Remove-Item -Path $fullPath -Recurse -Force
        Write-Success "Cleaned: $dir"
    }
}

# === STEP 3: Restore Packages ===
Write-Step "Restoring NuGet packages"
dotnet restore
if ($LASTEXITCODE -ne 0) { throw "Package restore failed" }
Write-Success "Packages restored"

# === STEP 4: Publish for MSI Source ===
Write-Step "Publishing application for MSI"

dotnet publish PlatypusTools.UI\PlatypusTools.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$msiSourceDir = Join-Path $ProjectRoot "PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
if (-not (Test-Path $msiSourceDir)) {
    throw "MSI source directory not found: $msiSourceDir"
}

$fileCount = (Get-ChildItem -Path $msiSourceDir -Recurse -File).Count
Write-Success "Published $fileCount files"

# === STEP 5: Generate PublishFiles.wxs ===
Write-Step "Generating PublishFiles.wxs"

Push-Location (Join-Path $ProjectRoot "PlatypusTools.Installer")
try {
    & powershell -ExecutionPolicy Bypass -File .\GeneratePublishWxs.ps1 -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "GeneratePublishWxs.ps1 failed" }
} finally {
    Pop-Location
}
Write-Success "PublishFiles.wxs generated"

# === STEP 6: Build MSI ===
Write-Step "Building MSI installer"

dotnet restore PlatypusTools.Installer\PlatypusTools.Installer.wixproj
dotnet build PlatypusTools.Installer\PlatypusTools.Installer.wixproj -c Release

if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

$msiSource = Join-Path $ProjectRoot "PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi"
if (-not (Test-Path $msiSource)) {
    throw "MSI not found: $msiSource"
}

Write-Success "MSI built successfully"

# === STEP 7: Copy to Releases Folder ===
Write-Step "Copying to releases folder"

$releasesDir = Join-Path $ProjectRoot "releases"
if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
}

$msiDest = Join-Path $releasesDir "PlatypusToolsSetup-v$Version.msi"
Copy-Item $msiSource $msiDest -Force

$msiInfo = Get-Item $msiDest
$msiSizeMB = [math]::Round($msiInfo.Length / 1MB, 2)

Write-Success "Copied to: $msiDest"
Write-Host "  Size: $msiSizeMB MB" -ForegroundColor Gray

# === STEP 8: Build Self-Contained EXE (Optional Portable) ===
Write-Step "Building self-contained EXE"

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

if ($LASTEXITCODE -ne 0) { throw "EXE publish failed" }

$exePath = Join-Path $publishDir "PlatypusTools.UI.exe"
$exeInfo = Get-Item $exePath
$exeSizeMB = [math]::Round($exeInfo.Length / 1MB, 2)

Write-Success "EXE built: $exePath ($exeSizeMB MB)"

# === Summary ===
Write-Host "`n" -NoNewline
Write-Host "+===============================================================+" -ForegroundColor Green
Write-Host "|                    BUILD COMPLETE                             |" -ForegroundColor Green
Write-Host "+===============================================================+" -ForegroundColor Green

Write-Host "`nVersion: $Version" -ForegroundColor Cyan
Write-Host "`nRelease Artifact:" -ForegroundColor White
Write-Host "  $msiDest" -ForegroundColor Green
Write-Host "  Size: $msiSizeMB MB" -ForegroundColor Gray

Write-Host "`nTo upload to GitHub:" -ForegroundColor Yellow
Write-Host "  gh release create v$Version `"$msiDest`" --title `"v$Version`" --notes `"Release notes`"" -ForegroundColor Gray

Write-Host "`n"
