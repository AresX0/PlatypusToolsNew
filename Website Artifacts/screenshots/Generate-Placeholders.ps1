# Generate-Placeholders.ps1
# Creates SVG placeholder images for PlatypusTools website screenshots.
# Replace these with actual screenshots before publishing.
#
# Usage: Run this script, then replace each .svg with a real .png screenshot.

$outputDir = $PSScriptRoot

function New-PlaceholderSvg {
    param(
        [string]$FileName,
        [string]$Title,
        [string]$Subtitle,
        [string]$BgColor = "#1e1e2e",
        [string]$AccentColor = "#e9a820",
        [int]$Width = 1280,
        [int]$Height = 720
    )
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="$Width" height="$Height" viewBox="0 0 $Width $Height">
  <rect width="$Width" height="$Height" fill="$BgColor"/>
  <rect x="0" y="0" width="$Width" height="4" fill="$AccentColor"/>
  <text x="$($Width/2)" y="$($Height/2 - 20)" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="36" font-weight="bold" fill="white">$Title</text>
  <text x="$($Width/2)" y="$($Height/2 + 30)" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="18" fill="#aaa">$Subtitle</text>
  <rect x="40" y="$($Height - 60)" width="200" height="30" rx="4" fill="$AccentColor" opacity="0.3"/>
  <text x="140" y="$($Height - 40)" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="12" fill="$AccentColor">REPLACE WITH SCREENSHOT</text>
</svg>
"@
    $path = Join-Path $outputDir "$FileName.svg"
    Set-Content -Path $path -Value $svg -Encoding UTF8
    Write-Host "Created: $path"
}

# === THEMES ===
New-PlaceholderSvg -FileName "theme-light"    -Title "Light Theme"    -Subtitle "Clean, bright interface for daytime use" -BgColor "#f5f5f5" -AccentColor "#0078d4"
New-PlaceholderSvg -FileName "theme-dark"     -Title "Dark Theme"     -Subtitle "Easy on the eyes for night sessions"     -BgColor "#1e1e2e" -AccentColor "#569cd6"
New-PlaceholderSvg -FileName "theme-lcars"    -Title "LCARS Theme"    -Subtitle "Star Trek LCARS inspired interface"      -BgColor "#000000" -AccentColor "#ff9900"
New-PlaceholderSvg -FileName "theme-glass"    -Title "Glass Theme"    -Subtitle "Translucent glass UI with blur effects"  -BgColor "#1a2332" -AccentColor "#4fc3f7"
New-PlaceholderSvg -FileName "theme-pipboy"   -Title "Pip-Boy Theme"  -Subtitle "Fallout Pip-Boy retro terminal look"     -BgColor "#0a1a0a" -AccentColor "#00ff41"
New-PlaceholderSvg -FileName "theme-klingon"  -Title "Klingon Theme"  -Subtitle "Bold Klingon warrior interface"          -BgColor "#1a0a0a" -AccentColor "#ff3333"
New-PlaceholderSvg -FileName "theme-kpop"     -Title "K-Pop Demon Hunters" -Subtitle "Vibrant neon K-Pop inspired theme" -BgColor "#1a0a2e" -AccentColor "#ff00ff"
New-PlaceholderSvg -FileName "theme-highcontrast" -Title "High Contrast" -Subtitle "Accessibility-focused high contrast"  -BgColor "#000000" -AccentColor "#ffff00"

# === FILE MANAGEMENT ===
New-PlaceholderSvg -FileName "file-cleaner"        -Title "File Cleaner"        -Subtitle "Batch rename, move, and organize files"
New-PlaceholderSvg -FileName "duplicates"           -Title "Duplicate Finder"    -Subtitle "Find and remove duplicate files"
New-PlaceholderSvg -FileName "empty-folder-scanner" -Title "Empty Folder Scanner" -Subtitle "Detect and clean empty directories"
New-PlaceholderSvg -FileName "robocopy"             -Title "Robocopy"            -Subtitle "Windows Robocopy with visual interface"
New-PlaceholderSvg -FileName "cloud-sync"           -Title "Cloud Sync"          -Subtitle "Sync files across cloud providers"
New-PlaceholderSvg -FileName "file-diff"            -Title "File Diff"           -Subtitle "Compare files side by side"
New-PlaceholderSvg -FileName "bulk-file-mover"      -Title "Bulk File Mover"     -Subtitle "Move files in bulk by rules"
New-PlaceholderSvg -FileName "symlink-manager"      -Title "Symlink Manager"     -Subtitle "Create and manage symbolic links"

# === MULTIMEDIA - AUDIO ===
New-PlaceholderSvg -FileName "audio-player"       -Title "Audio Player"       -Subtitle "Full featured music player with visualizer"
New-PlaceholderSvg -FileName "audio-trim"          -Title "Audio Trimmer"      -Subtitle "Trim audio files with waveform display"
New-PlaceholderSvg -FileName "audio-transcription" -Title "Audio Transcription" -Subtitle "AI-powered speech-to-text with Whisper"

# === MULTIMEDIA - IMAGE ===
New-PlaceholderSvg -FileName "image-edit"       -Title "Image Editor"       -Subtitle "Rotate, flip, crop, and apply effects"
New-PlaceholderSvg -FileName "image-converter"  -Title "Image Converter"    -Subtitle "Convert between PNG, JPEG, WebP, TIFF, BMP"
New-PlaceholderSvg -FileName "image-resizer"    -Title "Image Resizer"      -Subtitle "Batch resize images to any dimension"
New-PlaceholderSvg -FileName "ico-converter"    -Title "ICO Converter"      -Subtitle "Create Windows icons from images"
New-PlaceholderSvg -FileName "batch-watermark"  -Title "Batch Watermark"    -Subtitle "Apply text watermarks to image batches"
New-PlaceholderSvg -FileName "image-scaler"     -Title "Image Scaler"       -Subtitle "AI upscale images with Real-ESRGAN"
New-PlaceholderSvg -FileName "3d-model-editor"  -Title "3D Model Editor"    -Subtitle "View and edit 3D model files"

# === MULTIMEDIA - VIDEO ===
New-PlaceholderSvg -FileName "video-player"    -Title "Video Player"    -Subtitle "Native fullscreen video player"
New-PlaceholderSvg -FileName "video-editor"    -Title "Video Editor"    -Subtitle "Shotcut-powered non-linear editor"
New-PlaceholderSvg -FileName "upscaler"        -Title "Video Upscaler"  -Subtitle "AI video upscaling with Real-ESRGAN"
New-PlaceholderSvg -FileName "video-combiner"  -Title "Video Combiner"  -Subtitle "Merge multiple videos into one"
New-PlaceholderSvg -FileName "video-converter" -Title "Video Converter" -Subtitle "Convert video formats with FFmpeg"
New-PlaceholderSvg -FileName "export-queue"    -Title "Export Queue"    -Subtitle "Manage video export jobs"
New-PlaceholderSvg -FileName "video-metadata"  -Title "Video Metadata"  -Subtitle "View and edit video metadata tags"
New-PlaceholderSvg -FileName "gif-maker"       -Title "GIF Maker"       -Subtitle "Create GIFs from video clips"

# === MULTIMEDIA - OTHER ===
New-PlaceholderSvg -FileName "media-hub"       -Title "Media Hub"       -Subtitle "Plex-like media browsing experience"
New-PlaceholderSvg -FileName "media-library"   -Title "Media Library"   -Subtitle "Organize your media collection"
New-PlaceholderSvg -FileName "external-tools"  -Title "External Tools"  -Subtitle "Launch VLC, Audacity, GIMP and more"

# === SYSTEM ===
New-PlaceholderSvg -FileName "disk-cleanup"       -Title "Disk Cleanup"          -Subtitle "Deep clean temporary and junk files"
New-PlaceholderSvg -FileName "privacy-cleaner"    -Title "Privacy Cleaner"       -Subtitle "Remove browser and app tracking data"
New-PlaceholderSvg -FileName "recent-cleaner"     -Title "Recent Cleaner"        -Subtitle "Clear recent file history"
New-PlaceholderSvg -FileName "startup-manager"    -Title "Startup Manager"       -Subtitle "Control auto-start programs"
New-PlaceholderSvg -FileName "process-manager"    -Title "Process Manager"       -Subtitle "Advanced process monitoring"
New-PlaceholderSvg -FileName "registry-cleaner"   -Title "Registry Cleaner"      -Subtitle "Clean orphaned registry entries"
New-PlaceholderSvg -FileName "scheduled-tasks"    -Title "Scheduled Tasks"       -Subtitle "View and manage Windows tasks"
New-PlaceholderSvg -FileName "system-restore"     -Title "System Restore"        -Subtitle "Manage restore points"
New-PlaceholderSvg -FileName "windows-update"     -Title "Windows Update Repair" -Subtitle "Fix Windows Update issues"
New-PlaceholderSvg -FileName "terminal"           -Title "Terminal"              -Subtitle "Built-in PowerShell terminal"
New-PlaceholderSvg -FileName "job-queue"          -Title "Job Queue"             -Subtitle "Batch job scheduling"
New-PlaceholderSvg -FileName "scheduled-backup"   -Title "Scheduled Backup"      -Subtitle "Automated backup scheduler"
New-PlaceholderSvg -FileName "env-variables"      -Title "Environment Variables" -Subtitle "Visual environment variable editor"
New-PlaceholderSvg -FileName "windows-services"   -Title "Windows Services"      -Subtitle "Service manager with status monitoring"
New-PlaceholderSvg -FileName "disk-health"        -Title "Disk Health"           -Subtitle "S.M.A.R.T. disk monitoring"
New-PlaceholderSvg -FileName "wifi-passwords"     -Title "WiFi Passwords"        -Subtitle "Reveal saved WiFi credentials"
New-PlaceholderSvg -FileName "remote-dashboard"   -Title "Remote Dashboard"      -Subtitle "Remote system monitoring"
New-PlaceholderSvg -FileName "remote-desktop"     -Title "Remote Desktop"        -Subtitle "Built-in remote desktop client"

# === SECURITY ===
New-PlaceholderSvg -FileName "folder-hider"       -Title "Folder Hider"           -Subtitle "Hide sensitive folders from view"
New-PlaceholderSvg -FileName "system-audit"       -Title "System Audit"           -Subtitle "Security configuration audit"
New-PlaceholderSvg -FileName "forensics-analyzer" -Title "Forensics Analyzer"     -Subtitle "Digital forensics toolkit"
New-PlaceholderSvg -FileName "reboot-analyzer"    -Title "Reboot Analyzer"        -Subtitle "Analyze system reboot history"
New-PlaceholderSvg -FileName "secure-wipe"        -Title "Secure Wipe"            -Subtitle "Military-grade file shredding"
New-PlaceholderSvg -FileName "advanced-forensics" -Title "Advanced Forensics"     -Subtitle "Deep forensic analysis with Volatility"
New-PlaceholderSvg -FileName "hash-scanner"       -Title "Hash Scanner"           -Subtitle "Scan files against threat databases"
New-PlaceholderSvg -FileName "ad-security"        -Title "AD Security Analyzer"   -Subtitle "Active Directory security assessment"
New-PlaceholderSvg -FileName "cve-search"         -Title "CVE Search"             -Subtitle "Search vulnerability databases"
New-PlaceholderSvg -FileName "file-integrity"     -Title "File Integrity Monitor" -Subtitle "Monitor files for unauthorized changes"
New-PlaceholderSvg -FileName "certificate-manager" -Title "Certificate Manager"   -Subtitle "Manage X.509 certificates"
New-PlaceholderSvg -FileName "security-vault"     -Title "Security Vault"         -Subtitle "Encrypted credential storage"

# === METADATA ===
New-PlaceholderSvg -FileName "metadata-editor" -Title "Metadata Editor" -Subtitle "Edit EXIF, XMP, IPTC metadata with ExifTool"

# === TOOLS ===
New-PlaceholderSvg -FileName "website-downloader" -Title "Website Downloader" -Subtitle "Download entire websites for offline use"
New-PlaceholderSvg -FileName "file-analyzer"      -Title "File Analyzer"      -Subtitle "Deep file type analysis"
New-PlaceholderSvg -FileName "disk-space"         -Title "Disk Space Analyzer" -Subtitle "Visual disk usage breakdown"
New-PlaceholderSvg -FileName "network-tools"      -Title "Network Tools"      -Subtitle "Ping, traceroute, port scan, DNS"
New-PlaceholderSvg -FileName "archive-manager"    -Title "Archive Manager"    -Subtitle "ZIP, 7Z, RAR archive handling"
New-PlaceholderSvg -FileName "pdf-tools"          -Title "PDF Tools"          -Subtitle "Merge, split, and convert PDFs"
New-PlaceholderSvg -FileName "screenshot"         -Title "Screenshot"         -Subtitle "Screen capture with annotations"
New-PlaceholderSvg -FileName "bootable-usb"       -Title "Bootable USB Creator" -Subtitle "Create bootable USB drives"
New-PlaceholderSvg -FileName "plugin-manager"     -Title "Plugin Manager"     -Subtitle "Install and manage plugins"
New-PlaceholderSvg -FileName "ftp-client"         -Title "FTP/SFTP Client"    -Subtitle "Secure file transfer client"
New-PlaceholderSvg -FileName "terminal-client"    -Title "SSH/Telnet Terminal" -Subtitle "Remote terminal connections"
New-PlaceholderSvg -FileName "web-browser"        -Title "Web Browser"        -Subtitle "Built-in Chromium web browser"
New-PlaceholderSvg -FileName "plex-backup"        -Title "Plex Backup"        -Subtitle "Back up Plex Media Server data"
New-PlaceholderSvg -FileName "screen-recorder"    -Title "Screen Recorder"    -Subtitle "Record screen with audio"
New-PlaceholderSvg -FileName "intune-packager"    -Title "Intune Packager"    -Subtitle "Package apps for Microsoft Intune"
New-PlaceholderSvg -FileName "clipboard-history"  -Title "Clipboard History"  -Subtitle "Track and reuse clipboard items"
New-PlaceholderSvg -FileName "qr-code"            -Title "QR Code Generator"  -Subtitle "Generate QR codes from text/URLs"
New-PlaceholderSvg -FileName "color-picker"       -Title "Color Picker"       -Subtitle "Pick colors from screen"
New-PlaceholderSvg -FileName "bulk-checksum"      -Title "Bulk Checksum"      -Subtitle "Generate checksums for file batches"

# === HERO / OVERVIEW ===
New-PlaceholderSvg -FileName "hero-overview" -Title "PlatypusTools" -Subtitle "The Swiss Army Knife for Windows" -Width 1920 -Height 1080 -AccentColor "#e9a820"

Write-Host ""
Write-Host "Done! Generated $(Get-ChildItem $outputDir -Filter '*.svg' | Measure-Object | Select-Object -ExpandProperty Count) placeholder screenshots."
Write-Host "Replace each .svg with an actual .png screenshot before publishing."
