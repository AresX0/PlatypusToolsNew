<#
.SYNOPSIS
    Automated screenshot capture for PlatypusTools — navigates every tab and takes PNG screenshots.
.DESCRIPTION
    Uses Windows UI Automation to launch PlatypusTools, navigate all tabs (including nested tabs
    in Multimedia), switch themes, and capture window screenshots as PNG files.
.NOTES
    Run from: C:\Projects\PlatypusToolsNew\Website Artifacts\
    Output:   C:\Projects\PlatypusToolsNew\Website Artifacts\screenshots\*.png
#>

param(
    [switch]$ThemesOnly,      # Only capture theme screenshots
    [switch]$FeaturesOnly,    # Only capture feature tab screenshots
    [int]$DelayMs = 1200      # Delay in ms after switching tabs before screenshot
)

$ErrorActionPreference = 'Continue'

# ── Load assemblies ──────────────────────────────────────────────
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# For window capture via Win32
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class WindowCapture {
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left, Top, Right, Bottom;
    }

    public static Bitmap CaptureWindow(IntPtr hWnd) {
        RECT rect;
        GetWindowRect(hWnd, out rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;

        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        return bmp;
    }
}
"@ -ReferencedAssemblies System.Drawing, System.Drawing.Primitives, System.Runtime.InteropServices -ErrorAction SilentlyContinue

# ── Paths ────────────────────────────────────────────────────────
$exePath      = "C:\Projects\PlatypusToolsNew\publish\PlatypusTools.UI.exe"
$outputDir    = "C:\Projects\PlatypusToolsNew\Website Artifacts\screenshots"
$settingsDir  = Join-Path $env:APPDATA "PlatypusTools"
$settingsFile = Join-Path $settingsDir "settings.json"

if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }

# ── UI Automation helpers ────────────────────────────────────────
function Find-MainWindow {
    param([int]$TimeoutSeconds = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "PlatypusTools"
    )
    while ((Get-Date) -lt $deadline) {
        $win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition
        )
        if ($win) { return $win }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Find-AllTabItems {
    param($Parent)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem
    )
    return $Parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Select-TabByName {
    param(
        $Parent,
        [string]$TabName
    )
    $tabs = Find-AllTabItems -Parent $Parent
    foreach ($tab in $tabs) {
        $name = $tab.Current.Name
        if ($name -like "*$TabName*") {
            try {
                $selPattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPatternIdentifiers]::Pattern)
                $selPattern.Select()
                Start-Sleep -Milliseconds $DelayMs
                return $true
            } catch {
                # Try invoke pattern as fallback
                try {
                    $invPattern = $tab.GetCurrentPattern([System.Windows.Automation.InvokePatternIdentifiers]::Pattern)
                    $invPattern.Invoke()
                    Start-Sleep -Milliseconds $DelayMs
                    return $true
                } catch {
                    Write-Warning "  Could not select tab '$TabName': $_"
                    return $false
                }
            }
        }
    }
    Write-Warning "  Tab not found: '$TabName'"
    return $false
}

function Capture-Screenshot {
    param(
        [string]$FileName,
        [IntPtr]$hWnd
    )
    [WindowCapture]::SetForegroundWindow($hWnd)
    Start-Sleep -Milliseconds 300
    $bmp = [WindowCapture]::CaptureWindow($hWnd)
    if ($bmp) {
        $outPath = Join-Path $outputDir "$FileName.png"
        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "  OK: $FileName.png" -ForegroundColor Green
        return $true
    } else {
        Write-Warning "  FAILED to capture: $FileName"
        return $false
    }
}

function Close-PopupWindows {
    param([IntPtr]$mainHwnd)
    # Try to close any dependency setup or dialog windows
    Start-Sleep -Milliseconds 1000
    $dialogs = @("Dependency Setup", "First Run", "Setup")
    foreach ($name in $dialogs) {
        $h = [WindowCapture]::FindWindow([NullString]::Value, $name)
        if ($h -ne [IntPtr]::Zero) {
            # Send Alt+F4
            [System.Windows.Forms.SendKeys]::SendWait("%{F4}")
            Start-Sleep -Milliseconds 500
        }
    }
    # Re-focus main window
    [WindowCapture]::SetForegroundWindow($mainHwnd)
}

function Set-AppTheme {
    param([string]$ThemeName)
    if (Test-Path $settingsFile) {
        $json = Get-Content $settingsFile -Raw | ConvertFrom-Json
        $json.Theme = $ThemeName
        $json | ConvertTo-Json -Depth 10 | Set-Content $settingsFile -Encoding UTF8
    } else {
        @{ Theme = $ThemeName } | ConvertTo-Json | Set-Content $settingsFile -Encoding UTF8
    }
}

# ── Screenshot navigation map ───────────────────────────────────
# Each entry: @{ File="output-name"; Path=@("TopTab","SubTab","SubSubTab") }
$featureMap = @(
    # ── File Management ──
    @{ File="file-cleaner";         Path=@("File Management","File Cleaner") }
    @{ File="duplicates";           Path=@("File Management","Duplicates") }
    @{ File="empty-folder-scanner"; Path=@("File Management","Empty Folder Scanner") }
    @{ File="robocopy";             Path=@("File Management","Robocopy") }
    @{ File="cloud-sync";           Path=@("File Management","Cloud Sync") }
    @{ File="file-diff";            Path=@("File Management","File Diff") }
    @{ File="bulk-file-mover";      Path=@("File Management","Bulk File Mover") }
    @{ File="symlink-manager";      Path=@("File Management","Symlink Manager") }

    # ── Multimedia > Media Hub ──
    @{ File="media-hub";        Path=@("Multimedia","Media Hub") }

    # ── Multimedia > Audio ──
    @{ File="audio-player";       Path=@("Multimedia","Audio","Audio Player") }
    @{ File="audio-trim";         Path=@("Multimedia","Audio","Audio Trim") }
    @{ File="audio-transcription"; Path=@("Multimedia","Audio","Audio Transcription") }

    # ── Multimedia > Image ──
    @{ File="image-edit";       Path=@("Multimedia","Image","Image Edit") }
    @{ File="image-converter";  Path=@("Multimedia","Image","Image Converter") }
    @{ File="image-resizer";    Path=@("Multimedia","Image","Image Resizer") }
    @{ File="ico-converter";    Path=@("Multimedia","Image","ICO Converter") }
    @{ File="batch-watermark";  Path=@("Multimedia","Image","Batch Watermark") }
    @{ File="image-scaler";     Path=@("Multimedia","Image","Image Scaler") }
    @{ File="3d-model-editor";  Path=@("Multimedia","Image","3D Model") }

    # ── Multimedia > Video ──
    @{ File="video-player";    Path=@("Multimedia","Video","Video Player") }
    @{ File="video-editor";    Path=@("Multimedia","Video","Video Editor") }
    @{ File="upscaler";        Path=@("Multimedia","Video","Upscaler") }
    @{ File="video-combiner";  Path=@("Multimedia","Video","Video Combiner") }
    @{ File="video-converter"; Path=@("Multimedia","Video","Video Converter") }
    @{ File="export-queue";    Path=@("Multimedia","Video","Export Queue") }
    @{ File="video-metadata";  Path=@("Multimedia","Video","Video Metadata") }
    @{ File="gif-maker";       Path=@("Multimedia","Video","GIF Maker") }

    # ── Multimedia > Media Library & External Tools ──
    @{ File="media-library";   Path=@("Multimedia","Media Library") }
    @{ File="external-tools";  Path=@("Multimedia","External Tools") }

    # ── System ──
    @{ File="disk-cleanup";      Path=@("System","Disk Cleanup") }
    @{ File="privacy-cleaner";   Path=@("System","Privacy Cleaner") }
    @{ File="recent-cleaner";    Path=@("System","Recent Cleaner") }
    @{ File="startup-manager";   Path=@("System","Startup Manager") }
    @{ File="process-manager";   Path=@("System","Process Manager") }
    @{ File="registry-cleaner";  Path=@("System","Registry Cleaner") }
    @{ File="scheduled-tasks";   Path=@("System","Scheduled Tasks") }
    @{ File="system-restore";    Path=@("System","System Restore") }
    @{ File="windows-update";    Path=@("System","Windows Update") }
    @{ File="terminal";          Path=@("System","Terminal") }
    @{ File="job-queue";         Path=@("System","Job Queue") }
    @{ File="scheduled-backup";  Path=@("System","Scheduled Backup") }
    @{ File="env-variables";     Path=@("System","Environment Variables") }
    @{ File="windows-services";  Path=@("System","Windows Services") }
    @{ File="disk-health";       Path=@("System","Disk Health") }
    @{ File="wifi-passwords";    Path=@("System","WiFi Passwords") }
    @{ File="remote-dashboard";  Path=@("System","Remote Dashboard") }
    @{ File="remote-desktop";    Path=@("System","Remote Desktop") }

    # ── Security ──
    @{ File="folder-hider";        Path=@("Security","Folder Hider") }
    @{ File="system-audit";        Path=@("Security","System Audit") }
    @{ File="forensics-analyzer";  Path=@("Security","Forensics Analyzer") }
    @{ File="reboot-analyzer";     Path=@("Security","Reboot Analyzer") }
    @{ File="secure-wipe";         Path=@("Security","Secure Wipe") }
    @{ File="advanced-forensics";  Path=@("Security","Advanced Forensics") }
    @{ File="hash-scanner";        Path=@("Security","Hash Scanner") }
    @{ File="ad-security";         Path=@("Security","AD Security") }
    @{ File="cve-search";          Path=@("Security","CVE Search") }
    @{ File="file-integrity";      Path=@("Security","File Integrity") }
    @{ File="certificate-manager"; Path=@("Security","Certificate Manager") }
    @{ File="security-vault";      Path=@("Security","Security Vault") }

    # ── Metadata ──
    @{ File="metadata-editor"; Path=@("Metadata") }

    # ── Tools ──
    @{ File="website-downloader"; Path=@("Tools","Website Downloader") }
    @{ File="file-analyzer";      Path=@("Tools","File Analyzer") }
    @{ File="disk-space";         Path=@("Tools","Disk Space") }
    @{ File="network-tools";      Path=@("Tools","Network Tools") }
    @{ File="archive-manager";    Path=@("Tools","Archive Manager") }
    @{ File="pdf-tools";          Path=@("Tools","PDF Tools") }
    @{ File="screenshot";         Path=@("Tools","Screenshot") }
    @{ File="bootable-usb";       Path=@("Tools","Bootable USB") }
    @{ File="plugin-manager";     Path=@("Tools","Plugin Manager") }
    @{ File="ftp-client";         Path=@("Tools","FTP") }
    @{ File="terminal-client";    Path=@("Tools","Terminal (SSH") }
    @{ File="web-browser";        Path=@("Tools","Web Browser") }
    @{ File="plex-backup";        Path=@("Tools","Plex Backup") }
    @{ File="screen-recorder";    Path=@("Tools","Screen Recorder") }
    @{ File="intune-packager";    Path=@("Tools","Intune Packager") }
    @{ File="clipboard-history";  Path=@("Tools","Clipboard History") }
    @{ File="qr-code";            Path=@("Tools","QR Code") }
    @{ File="color-picker";       Path=@("Tools","Color Picker") }
    @{ File="bulk-checksum";      Path=@("Tools","Bulk Checksum") }
)

$themes = @(
    @{ File="theme-light";        Name="Light" }
    @{ File="theme-dark";         Name="Dark" }
    @{ File="theme-lcars";        Name="LCARS" }
    @{ File="theme-glass";        Name="Glass" }
    @{ File="theme-pipboy";       Name="PipBoy" }
    @{ File="theme-klingon";      Name="Klingon" }
    @{ File="theme-kpop";         Name="KPopDemonHunters" }
    @{ File="theme-highcontrast"; Name="HighContrast" }
)

# ── Launch app & get window ──────────────────────────────────────
function Start-AppAndGetWindow {
    Write-Host "`n>> Launching PlatypusTools..." -ForegroundColor Cyan
    $proc = Start-Process $exePath -PassThru
    Start-Sleep -Seconds 3

    $mainWin = Find-MainWindow -TimeoutSeconds 30
    if (-not $mainWin) {
        Write-Error "Could not find PlatypusTools main window after 30s!"
        exit 1
    }

    $hWnd = [IntPtr]::new($mainWin.Current.NativeWindowHandle)

    # Resize to consistent 1400x900 and position at (100,50)
    [WindowCapture]::ShowWindow($hWnd, 9)  # SW_RESTORE
    Start-Sleep -Milliseconds 300
    [WindowCapture]::MoveWindow($hWnd, 100, 50, 1400, 900, $true)
    Start-Sleep -Milliseconds 500
    [WindowCapture]::SetForegroundWindow($hWnd)
    Start-Sleep -Milliseconds 500

    # Dismiss any popup dialogs
    Close-PopupWindows -mainHwnd $hWnd

    return [PSCustomObject]@{ Process=$proc; Window=$mainWin; hWnd=$hWnd }
}

function Stop-App {
    param($proc)
    if ($proc -and !$proc.HasExited) {
        $proc.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 1500
        if (!$proc.HasExited) { $proc.Kill() }
    }
}

# ── Feature screenshot capture ───────────────────────────────────
function Capture-FeatureScreenshots {
    param($mainWin, [IntPtr]$hWnd)

    $total = $featureMap.Count
    $captured = 0
    $failed = 0

    Write-Host "`n>> Capturing $total feature screenshots..." -ForegroundColor Cyan

    # Hero overview first (default view after launch)
    Write-Host "`n[Hero] Capturing hero-overview..." -ForegroundColor Yellow
    Capture-Screenshot -FileName "hero-overview" -hWnd $hWnd

    foreach ($entry in $featureMap) {
        $file = $entry.File
        $path = $entry.Path
        Write-Host "`n[$($captured+1)/$total] $file -> $($path -join ' > ')" -ForegroundColor Yellow

        $ok = $true
        foreach ($step in $path) {
            if (!(Select-TabByName -Parent $mainWin -TabName $step)) {
                $ok = $false
                break
            }
        }

        if ($ok) {
            if (Capture-Screenshot -FileName $file -hWnd $hWnd) {
                $captured++
            } else {
                $failed++
            }
        } else {
            $failed++
        }
    }

    Write-Host "`n>> Feature screenshots: $captured captured, $failed failed out of $total" -ForegroundColor Cyan
}

# ── Theme screenshot capture ─────────────────────────────────────
function Capture-ThemeScreenshots {
    Write-Host "`n>> Capturing theme screenshots (will restart app for each theme)..." -ForegroundColor Cyan

    # Back up current settings
    $backupFile = "$settingsFile.bak"
    if (Test-Path $settingsFile) {
        Copy-Item $settingsFile $backupFile -Force
    }

    foreach ($theme in $themes) {
        Write-Host "`n[Theme] $($theme.Name)..." -ForegroundColor Yellow

        Set-AppTheme -ThemeName $theme.Name
        Start-Sleep -Milliseconds 500

        $app = Start-AppAndGetWindow
        Start-Sleep -Milliseconds 1500

        # Navigate to File Cleaner (a representative tab) for theme screenshot
        Select-TabByName -Parent $app.Window -TabName "File Management" | Out-Null
        Start-Sleep -Milliseconds 500
        Select-TabByName -Parent $app.Window -TabName "File Cleaner" | Out-Null
        Start-Sleep -Milliseconds 800

        Capture-Screenshot -FileName $theme.File -hWnd $app.hWnd

        Stop-App -proc $app.Process
        Start-Sleep -Milliseconds 1000
    }

    # Restore original settings
    if (Test-Path $backupFile) {
        Copy-Item $backupFile $settingsFile -Force
        Remove-Item $backupFile -Force
    }

    Write-Host "`n>> All theme screenshots captured!" -ForegroundColor Cyan
}

# ── Main ─────────────────────────────────────────────────────────
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  PlatypusTools Screenshot Automation" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Output: $outputDir"
Write-Host "App:    $exePath"
Write-Host ""

if (-not $ThemesOnly) {
    $app = Start-AppAndGetWindow
    Capture-FeatureScreenshots -mainWin $app.Window -hWnd $app.hWnd
    Stop-App -proc $app.Process
    Start-Sleep -Milliseconds 1000
}

if (-not $FeaturesOnly) {
    Capture-ThemeScreenshots
}

$pngCount = (Get-ChildItem "$outputDir\*.png" -ErrorAction SilentlyContinue).Count
Write-Host "`n============================================" -ForegroundColor Green
Write-Host "  DONE! $pngCount PNG screenshots in:" -ForegroundColor Green
Write-Host "  $outputDir" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
