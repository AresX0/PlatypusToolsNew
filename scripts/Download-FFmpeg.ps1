<#
.SYNOPSIS
    Downloads and extracts FFmpeg for bundling with the MSI installer.
    
.DESCRIPTION
    This script downloads the latest FFmpeg essentials build from gyan.dev
    and extracts it to a local Tools folder for inclusion in the MSI.
    
.PARAMETER OutputPath
    The directory where FFmpeg binaries will be extracted.
    Default: .\Tools\ffmpeg
    
.EXAMPLE
    .\Download-FFmpeg.ps1
    Downloads FFmpeg to the default Tools\ffmpeg directory.
    
.EXAMPLE
    .\Download-FFmpeg.ps1 -OutputPath "C:\MyTools\ffmpeg"
    Downloads FFmpeg to a custom directory.
#>

param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\Tools\ffmpeg")
)

$ErrorActionPreference = "Stop"

# FFmpeg download URL (gyan.dev essentials build - smaller, contains ffmpeg.exe, ffprobe.exe, ffplay.exe)
$ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$tempZip = Join-Path $env:TEMP "ffmpeg-essentials.zip"

function Write-Step { param([string]$Message) Write-Host "`n=== $Message ===" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Info { param([string]$Message) Write-Host "  $Message" -ForegroundColor Gray }

Write-Host "`n" -NoNewline
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║             FFmpeg Download Script                            ║" -ForegroundColor Magenta
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

# Resolve output path
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
Write-Info "Output path: $OutputPath"

# Check if FFmpeg already exists
$ffmpegExe = Join-Path $OutputPath "ffmpeg.exe"
if (Test-Path $ffmpegExe) {
    $version = & $ffmpegExe -version 2>&1 | Select-Object -First 1
    Write-Success "FFmpeg already exists: $version"
    Write-Host "`nTo re-download, delete the folder: $OutputPath" -ForegroundColor Yellow
    exit 0
}

# Create output directory
Write-Step "Creating output directory"
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}
Write-Success "Directory ready: $OutputPath"

# Download FFmpeg
Write-Step "Downloading FFmpeg"
Write-Info "URL: $ffmpegUrl"
Write-Info "This may take a few minutes..."

try {
    $ProgressPreference = 'SilentlyContinue'  # Speeds up Invoke-WebRequest
    Invoke-WebRequest -Uri $ffmpegUrl -OutFile $tempZip -UseBasicParsing
    $ProgressPreference = 'Continue'
    
    $zipSize = [math]::Round((Get-Item $tempZip).Length / 1MB, 2)
    Write-Success "Downloaded: $zipSize MB"
}
catch {
    throw "Failed to download FFmpeg: $_"
}

# Extract FFmpeg
Write-Step "Extracting FFmpeg"
$tempExtract = Join-Path $env:TEMP "ffmpeg-extract"
if (Test-Path $tempExtract) {
    Remove-Item -Path $tempExtract -Recurse -Force
}

try {
    Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force
    Write-Success "Extracted to temp folder"
    
    # Find the bin folder (structure is ffmpeg-X.X-essentials_build/bin/)
    $binFolder = Get-ChildItem -Path $tempExtract -Recurse -Directory -Filter "bin" | Select-Object -First 1
    if (-not $binFolder) {
        throw "Could not find bin folder in FFmpeg archive"
    }
    
    # Copy executables to output
    $exeFiles = @("ffmpeg.exe", "ffprobe.exe", "ffplay.exe")
    foreach ($exe in $exeFiles) {
        $source = Join-Path $binFolder.FullName $exe
        if (Test-Path $source) {
            Copy-Item -Path $source -Destination $OutputPath -Force
            Write-Success "Copied: $exe"
        }
    }
    
    # Copy license file
    $licenseFiles = Get-ChildItem -Path $tempExtract -Recurse -Filter "LICENSE*" | Select-Object -First 1
    if ($licenseFiles) {
        Copy-Item -Path $licenseFiles.FullName -Destination (Join-Path $OutputPath "LICENSE.txt") -Force
        Write-Success "Copied: LICENSE.txt"
    }
}
finally {
    # Cleanup
    if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
    if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force }
}

# Verify installation
Write-Step "Verifying installation"
$ffmpegExe = Join-Path $OutputPath "ffmpeg.exe"
if (Test-Path $ffmpegExe) {
    $version = & $ffmpegExe -version 2>&1 | Select-Object -First 1
    Write-Success "FFmpeg installed: $version"
    
    $files = Get-ChildItem -Path $OutputPath -File
    Write-Info "Installed files:"
    foreach ($file in $files) {
        $sizeMB = [math]::Round($file.Length / 1MB, 2)
        Write-Info "  - $($file.Name) ($sizeMB MB)"
    }
}
else {
    throw "FFmpeg installation verification failed"
}

Write-Host "`n" -NoNewline
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                    DOWNLOAD COMPLETE                          ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host "`nFFmpeg is ready at: $OutputPath" -ForegroundColor Cyan
Write-Host ""
