# Download-ExifTool.ps1
# Downloads ExifTool Windows executable for bundling with PlatypusTools

param(
    [string]$DestinationPath = "$PSScriptRoot\..\Tools\exiftool",
    [string]$Version = "13.47"  # Current version as of Jan 2025
)

$ErrorActionPreference = "Stop"

Write-Host "=== ExifTool Download Script ===" -ForegroundColor Cyan

# Create destination directory if it doesn't exist
if (-not (Test-Path $DestinationPath)) {
    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    Write-Host "Created directory: $DestinationPath"
}

# Check if ExifTool already exists
$exiftoolExe = Join-Path $DestinationPath "exiftool.exe"
if (Test-Path $exiftoolExe) {
    Write-Host "ExifTool already exists at $exiftoolExe" -ForegroundColor Yellow
    Write-Host "To re-download, delete the Tools\exiftool folder first."
    return
}

# Download ExifTool from SourceForge (64-bit Windows executable)
$downloadUrl = "https://sourceforge.net/projects/exiftool/files/exiftool-${Version}_64.zip/download"
$zipPath = Join-Path $DestinationPath "exiftool-${Version}_64.zip"

Write-Host "Downloading ExifTool $Version from SourceForge..."
Write-Host "URL: $downloadUrl"

try {
    # Use curl with -L to follow redirects (SourceForge uses redirects)
    $curlPath = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curlPath) {
        & curl.exe -L -o $zipPath $downloadUrl --progress-bar
    } else {
        # Fallback to Invoke-WebRequest (may not handle redirects well)
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing -MaximumRedirection 5
    }
} catch {
    Write-Error "Failed to download ExifTool: $_"
    exit 1
}

# Verify the download
if (-not (Test-Path $zipPath)) {
    Write-Error "Download failed - file not found"
    exit 1
}

$fileSize = (Get-Item $zipPath).Length
if ($fileSize -lt 1000000) {
    # File too small - probably an HTML error page
    Write-Error "Downloaded file is too small ($fileSize bytes) - download may have failed"
    Remove-Item $zipPath -Force
    exit 1
}

Write-Host "Downloaded: $([math]::Round($fileSize / 1MB, 2)) MB"

# Extract the archive
Write-Host "Extracting ExifTool..."
try {
    Expand-Archive -Path $zipPath -DestinationPath $DestinationPath -Force
    
    # ExifTool extracts into a subfolder - move contents up
    $extractedFolder = Get-ChildItem -Path $DestinationPath -Directory | Where-Object { $_.Name -like "exiftool-*" } | Select-Object -First 1
    if ($extractedFolder) {
        Get-ChildItem -Path $extractedFolder.FullName | ForEach-Object {
            Move-Item -Path $_.FullName -Destination $DestinationPath -Force
        }
        Remove-Item $extractedFolder.FullName -Force -Recurse
    }
} catch {
    Write-Error "Failed to extract ExifTool: $_"
    exit 1
}

# Rename the executable for command-line use
$originalExe = Join-Path $DestinationPath "exiftool(-k).exe"
if (Test-Path $originalExe) {
    Rename-Item -Path $originalExe -NewName "exiftool.exe" -Force
    Write-Host "Renamed exiftool(-k).exe to exiftool.exe for command-line use"
}

# Clean up zip file
Remove-Item $zipPath -Force
Write-Host "Cleaned up temporary files"

# Verify installation
$exiftoolExe = Join-Path $DestinationPath "exiftool.exe"
$exiftoolFiles = Join-Path $DestinationPath "exiftool_files"

if ((Test-Path $exiftoolExe) -and (Test-Path $exiftoolFiles)) {
    Write-Host ""
    Write-Host "=== ExifTool Installation Complete ===" -ForegroundColor Green
    Write-Host "Executable: $exiftoolExe"
    Write-Host "Support files: $exiftoolFiles"
    
    $totalSize = (Get-ChildItem $DestinationPath -Recurse | Measure-Object -Property Length -Sum).Sum
    Write-Host "Total size: $([math]::Round($totalSize / 1MB, 2)) MB"
} else {
    Write-Error "ExifTool installation verification failed"
    exit 1
}
