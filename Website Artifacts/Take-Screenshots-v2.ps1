<#
.SYNOPSIS
    Automated screenshot capture for PlatypusTools using settings-based tab navigation.
.DESCRIPTION
    Modifies the app's settings.json to restore specific tab indices, launches the app,
    waits for rendering, captures a window screenshot, and closes the app. Repeats for
    every tab and theme.
.NOTES
    Run from any directory. Outputs PNGs to Website Artifacts\screenshots\.
#>

param(
    [switch]$ThemesOnly,
    [switch]$FeaturesOnly,
    [int]$RenderDelayMs = 3500
)

$ErrorActionPreference = 'Continue'

# ── Load assemblies & Win32 helpers ──────────────────────────────
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class WinAPI {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int W, int H, bool repaint);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern void SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, int extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, int extra);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP    = 0x0004;
    public const uint KEYEVENTF_KEYUP       = 0x0002;
    public const byte VK_TAB   = 0x09;
    public const byte VK_RIGHT = 0x27;

    public static Bitmap CaptureWindow(IntPtr hWnd) {
        RECT r;
        GetWindowRect(hWnd, out r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return null;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static void ClickAt(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        System.Threading.Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        System.Threading.Thread.Sleep(200);
    }

    public static void PressKey(byte vk) {
        keybd_event(vk, 0, 0, 0);
        System.Threading.Thread.Sleep(30);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
        System.Threading.Thread.Sleep(150);
    }
}
"@ -ReferencedAssemblies System.Drawing, System.Drawing.Primitives, System.Runtime.InteropServices, System.Threading.Thread -ErrorAction SilentlyContinue

# ── Paths ────────────────────────────────────────────────────────
$exePath      = "C:\Projects\PlatypusToolsNew\publish\PlatypusTools.UI.exe"
$outputDir    = "C:\Projects\PlatypusToolsNew\Website Artifacts\screenshots"
$settingsDir  = Join-Path $env:APPDATA "PlatypusTools"
$settingsFile = Join-Path $settingsDir "settings.json"

if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }

# ── Settings helpers ─────────────────────────────────────────────
function Get-Settings {
    if (Test-Path $settingsFile) {
        return Get-Content $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    return @{} | ConvertTo-Json | ConvertFrom-Json
}

function Save-Settings($obj) {
    $obj | ConvertTo-Json -Depth 10 | Set-Content $settingsFile -Encoding UTF8
}

function Set-TabIndices {
    param([int]$MainIndex, [string]$SubKey, [int]$SubIndex = -1)
    $s = Get-Settings
    $s.RestoreLastSession = $true
    $s.LastSelectedMainTabIndex = $MainIndex

    # Reset window to consistent size/position
    $s.WindowWidth = 1400
    $s.WindowHeight = 900
    $s.WindowTop = 50
    $s.WindowLeft = 100
    $s.WindowStateValue = 0  # Normal (not maximized)

    if ($SubKey -and $SubIndex -ge 0) {
        if (-not $s.LastSelectedSubTabIndices) {
            $s | Add-Member -NotePropertyName LastSelectedSubTabIndices -NotePropertyValue @{} -Force
        }
        # PowerShell's JSON deserialization creates PSCustomObject; handle both
        $subs = $s.LastSelectedSubTabIndices
        if ($subs -is [PSCustomObject]) {
            $subs | Add-Member -NotePropertyName $SubKey -NotePropertyValue $SubIndex -Force
        } else {
            $subs[$SubKey] = $SubIndex
        }
    }
    Save-Settings $s
}

function Set-Theme($ThemeName) {
    $s = Get-Settings
    $s.Theme = $ThemeName
    Save-Settings $s
}

# ── App lifecycle ────────────────────────────────────────────────
function Start-App {
    $proc = Start-Process $exePath -PassThru
    Start-Sleep -Milliseconds $RenderDelayMs

    # Find window
    $hwnd = [WinAPI]::FindWindow([NullString]::Value, "PlatypusTools")
    $attempts = 0
    while ($hwnd -eq [IntPtr]::Zero -and $attempts -lt 20) {
        Start-Sleep -Milliseconds 500
        $hwnd = [WinAPI]::FindWindow([NullString]::Value, "PlatypusTools")
        $attempts++
    }

    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Warning "Could not find PlatypusTools window!"
        return $null
    }

    # Ensure correct size/position and bring to front
    [WinAPI]::ShowWindow($hwnd, 9) | Out-Null  # SW_RESTORE
    Start-Sleep -Milliseconds 200
    [WinAPI]::MoveWindow($hwnd, 100, 50, 1400, 900, $true) | Out-Null
    Start-Sleep -Milliseconds 300
    [WinAPI]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 500

    return [PSCustomObject]@{ Process = $proc; hWnd = $hwnd }
}

function Stop-App($app) {
    if ($app -and $app.Process -and !$app.Process.HasExited) {
        $app.Process.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 1000
        if (!$app.Process.HasExited) {
            $app.Process.Kill()
            Start-Sleep -Milliseconds 500
        }
    }
}

# ── Screenshot capture ───────────────────────────────────────────
function Capture-Screenshot {
    param([string]$FileName, [IntPtr]$hWnd)
    [WinAPI]::SetForegroundWindow($hWnd) | Out-Null
    Start-Sleep -Milliseconds 300
    $bmp = [WinAPI]::CaptureWindow($hWnd)
    if ($bmp) {
        $outPath = Join-Path $outputDir "$FileName.png"
        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "    OK  $FileName.png" -ForegroundColor Green
        return $true
    } else {
        Write-Warning "  FAIL  $FileName"
        return $false
    }
}

# ── Navigate nested tabs via arrow key simulation ────────────────
function Navigate-RightArrow {
    param([int]$Times = 1, [int]$ClickX = 200, [int]$ClickY = 253)
    # Click on the sub-tab header area to give focus
    [WinAPI]::ClickAt($ClickX, $ClickY)
    Start-Sleep -Milliseconds 400
    for ($i = 0; $i -lt $Times; $i++) {
        [WinAPI]::PressKey([WinAPI]::VK_RIGHT)
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 800
}

# ── Screenshot map ───────────────────────────────────────────────
# Main tab indices (with Dashboard at 0):
#   0=Dashboard, 1=File Management, 2=Multimedia, 3=System, 4=Security, 5=Metadata, 6=Tools
#
# Sub-tab keys use the full emoji header: "📁 File Management", "🎬 Multimedia", etc.

$screenshotJobs = @(
    # ── Hero (Dashboard) ──
    @{ File="hero-overview"; Main=0; SubKey=$null; SubIdx=-1 }

    # ── File Management (main=1) ──
    @{ File="file-cleaner";         Main=1; SubKey="📁 File Management"; SubIdx=0 }
    @{ File="duplicates";           Main=1; SubKey="📁 File Management"; SubIdx=1 }
    @{ File="empty-folder-scanner"; Main=1; SubKey="📁 File Management"; SubIdx=2 }
    @{ File="robocopy";             Main=1; SubKey="📁 File Management"; SubIdx=3 }
    @{ File="cloud-sync";           Main=1; SubKey="📁 File Management"; SubIdx=4 }
    @{ File="file-diff";            Main=1; SubKey="📁 File Management"; SubIdx=5 }
    @{ File="bulk-file-mover";      Main=1; SubKey="📁 File Management"; SubIdx=6 }
    @{ File="symlink-manager";      Main=1; SubKey="📁 File Management"; SubIdx=7 }

    # ── Multimedia (main=2) — direct sub-tabs ──
    @{ File="media-hub";       Main=2; SubKey="🎬 Multimedia"; SubIdx=0 }
    @{ File="audio-player";    Main=2; SubKey="🎬 Multimedia"; SubIdx=1 }  # Audio → default first
    @{ File="image-edit";      Main=2; SubKey="🎬 Multimedia"; SubIdx=2 }  # Image → default first
    @{ File="video-player";    Main=2; SubKey="🎬 Multimedia"; SubIdx=3 }  # Video → default first
    @{ File="media-library";   Main=2; SubKey="🎬 Multimedia"; SubIdx=4 }
    @{ File="external-tools";  Main=2; SubKey="🎬 Multimedia"; SubIdx=5 }

    # ── System (main=3) ──
    @{ File="disk-cleanup";      Main=3; SubKey="🖥️ System"; SubIdx=0 }
    @{ File="privacy-cleaner";   Main=3; SubKey="🖥️ System"; SubIdx=1 }
    @{ File="recent-cleaner";    Main=3; SubKey="🖥️ System"; SubIdx=2 }
    @{ File="startup-manager";   Main=3; SubKey="🖥️ System"; SubIdx=3 }
    @{ File="process-manager";   Main=3; SubKey="🖥️ System"; SubIdx=4 }
    @{ File="registry-cleaner";  Main=3; SubKey="🖥️ System"; SubIdx=5 }
    @{ File="scheduled-tasks";   Main=3; SubKey="🖥️ System"; SubIdx=6 }
    @{ File="system-restore";    Main=3; SubKey="🖥️ System"; SubIdx=7 }
    @{ File="windows-update";    Main=3; SubKey="🖥️ System"; SubIdx=8 }
    @{ File="terminal";          Main=3; SubKey="🖥️ System"; SubIdx=9 }
    @{ File="job-queue";         Main=3; SubKey="🖥️ System"; SubIdx=10 }
    @{ File="scheduled-backup";  Main=3; SubKey="🖥️ System"; SubIdx=11 }
    @{ File="env-variables";     Main=3; SubKey="🖥️ System"; SubIdx=12 }
    @{ File="windows-services";  Main=3; SubKey="🖥️ System"; SubIdx=13 }
    @{ File="disk-health";       Main=3; SubKey="🖥️ System"; SubIdx=14 }
    @{ File="wifi-passwords";    Main=3; SubKey="🖥️ System"; SubIdx=15 }
    @{ File="remote-dashboard";  Main=3; SubKey="🖥️ System"; SubIdx=16 }
    @{ File="remote-desktop";    Main=3; SubKey="🖥️ System"; SubIdx=17 }

    # ── Security (main=4) ──
    @{ File="folder-hider";        Main=4; SubKey="🔒 Security"; SubIdx=0 }
    @{ File="system-audit";        Main=4; SubKey="🔒 Security"; SubIdx=1 }
    @{ File="forensics-analyzer";  Main=4; SubKey="🔒 Security"; SubIdx=2 }
    @{ File="reboot-analyzer";     Main=4; SubKey="🔒 Security"; SubIdx=3 }
    @{ File="secure-wipe";         Main=4; SubKey="🔒 Security"; SubIdx=4 }
    @{ File="advanced-forensics";  Main=4; SubKey="🔒 Security"; SubIdx=5 }
    @{ File="hash-scanner";        Main=4; SubKey="🔒 Security"; SubIdx=6 }
    @{ File="ad-security";         Main=4; SubKey="🔒 Security"; SubIdx=7 }
    @{ File="cve-search";          Main=4; SubKey="🔒 Security"; SubIdx=8 }
    @{ File="file-integrity";      Main=4; SubKey="🔒 Security"; SubIdx=9 }
    @{ File="certificate-manager"; Main=4; SubKey="🔒 Security"; SubIdx=10 }
    @{ File="security-vault";      Main=4; SubKey="🔒 Security"; SubIdx=11 }

    # ── Metadata (main=5) — no sub-tabs ──
    @{ File="metadata-editor"; Main=5; SubKey=$null; SubIdx=-1 }

    # ── Tools (main=6) ──
    @{ File="website-downloader"; Main=6; SubKey="🔧 Tools"; SubIdx=0 }
    @{ File="file-analyzer";      Main=6; SubKey="🔧 Tools"; SubIdx=1 }
    @{ File="disk-space";         Main=6; SubKey="🔧 Tools"; SubIdx=2 }
    @{ File="network-tools";      Main=6; SubKey="🔧 Tools"; SubIdx=3 }
    @{ File="archive-manager";    Main=6; SubKey="🔧 Tools"; SubIdx=4 }
    @{ File="pdf-tools";          Main=6; SubKey="🔧 Tools"; SubIdx=5 }
    @{ File="screenshot";         Main=6; SubKey="🔧 Tools"; SubIdx=6 }
    @{ File="bootable-usb";       Main=6; SubKey="🔧 Tools"; SubIdx=7 }
    @{ File="plugin-manager";     Main=6; SubKey="🔧 Tools"; SubIdx=8 }
    @{ File="ftp-client";         Main=6; SubKey="🔧 Tools"; SubIdx=9 }
    @{ File="terminal-client";    Main=6; SubKey="🔧 Tools"; SubIdx=10 }
    @{ File="web-browser";        Main=6; SubKey="🔧 Tools"; SubIdx=11 }
    @{ File="plex-backup";        Main=6; SubKey="🔧 Tools"; SubIdx=12 }
    @{ File="screen-recorder";    Main=6; SubKey="🔧 Tools"; SubIdx=13 }
    @{ File="intune-packager";    Main=6; SubKey="🔧 Tools"; SubIdx=14 }
    @{ File="clipboard-history";  Main=6; SubKey="🔧 Tools"; SubIdx=15 }
    @{ File="qr-code";            Main=6; SubKey="🔧 Tools"; SubIdx=16 }
    @{ File="color-picker";       Main=6; SubKey="🔧 Tools"; SubIdx=17 }
    @{ File="bulk-checksum";      Main=6; SubKey="🔧 Tools"; SubIdx=18 }
)

# Nested Multimedia tabs that need arrow-key navigation after settings restore.
# For each group: restore to the parent sub-tab, screenshot the default, then arrow right through remaining.
$nestedMultimediaJobs = @(
    @{
        # Multimedia → Audio (sub-index 1): Audio Player(default), Audio Trim, Audio Transcription
        SubIdx = 1
        Tabs = @(
            # audio-player is already captured by the settings job above
            @{ File="audio-trim";          ArrowRight=1 }
            @{ File="audio-transcription"; ArrowRight=2 }
        )
    }
    @{
        # Multimedia → Image (sub-index 2): Image Edit(default), Image Converter, Image Resizer ...
        SubIdx = 2
        Tabs = @(
            @{ File="image-converter";  ArrowRight=1 }
            @{ File="image-resizer";    ArrowRight=2 }
            @{ File="ico-converter";    ArrowRight=3 }
            @{ File="batch-watermark";  ArrowRight=4 }
            @{ File="image-scaler";     ArrowRight=5 }
            @{ File="3d-model-editor";  ArrowRight=6 }
        )
    }
    @{
        # Multimedia → Video (sub-index 3): Video Player(default), Video Editor, Upscaler ...
        SubIdx = 3
        Tabs = @(
            @{ File="video-editor";     ArrowRight=1 }
            @{ File="upscaler";         ArrowRight=2 }
            @{ File="video-combiner";   ArrowRight=3 }
            @{ File="video-converter";  ArrowRight=4 }
            @{ File="export-queue";     ArrowRight=5 }
            @{ File="video-metadata";   ArrowRight=6 }
            @{ File="gif-maker";        ArrowRight=7 }
        )
    }
)

# Theme list
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

# ── MAIN ─────────────────────────────────────────────────────────
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  PlatypusTools Screenshot Automation v2" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Output: $outputDir"
Write-Host "Method: Settings-based tab restoration"
Write-Host ""

# Back up settings
$backupFile = "$settingsFile.screenshot-backup"
if (Test-Path $settingsFile) {
    Copy-Item $settingsFile $backupFile -Force
    Write-Host "Settings backed up." -ForegroundColor DarkGray
}

$captured = 0
$failed = 0

# ── Phase 1: Settings-based screenshots ──────────────────────────
if (-not $ThemesOnly) {
    # Group jobs by MainIndex to minimize restarts
    $groups = $screenshotJobs | Group-Object { $_.Main }

    foreach ($group in $groups | Sort-Object Name) {
        $mainIdx = [int]$group.Name
        $jobs = $group.Group

        Write-Host "`n>> Main tab $mainIdx ($($jobs.Count) screenshots)" -ForegroundColor Cyan

        foreach ($job in $jobs) {
            Write-Host "  [$($captured+$failed+1)] $($job.File)..." -NoNewline

            Set-TabIndices -MainIndex $job.Main -SubKey $job.SubKey -SubIndex $job.SubIdx
            $app = Start-App
            if ($app) {
                # Extra wait for heavier tabs
                Start-Sleep -Milliseconds 800
                if (Capture-Screenshot -FileName $job.File -hWnd $app.hWnd) {
                    $captured++
                } else {
                    $failed++
                }
                Stop-App $app
            } else {
                Write-Warning "    App failed to start"
                $failed++
            }
        }
    }

    # ── Phase 2: Nested Multimedia tabs via arrow keys ───────────
    Write-Host "`n>> Nested Multimedia tabs (arrow-key navigation)..." -ForegroundColor Cyan

    foreach ($group in $nestedMultimediaJobs) {
        $subIdx = $group.SubIdx
        $tabs = $group.Tabs

        Write-Host "  Sub-category index $subIdx ($($tabs.Count) nested tabs)" -ForegroundColor Yellow

        # Launch app at the right Multimedia sub-tab
        Set-TabIndices -MainIndex 2 -SubKey "🎬 Multimedia" -SubIndex $subIdx
        $app = Start-App
        if (-not $app) {
            Write-Warning "  App failed to start for nested tabs"
            $failed += $tabs.Count
            continue
        }

        Start-Sleep -Milliseconds 1000

        foreach ($tab in $tabs) {
            Write-Host "  [$($captured+$failed+1)] $($tab.File) (arrow x$($tab.ArrowRight))..." -NoNewline

            # Click on the third-level tab strip area to set focus, then arrow right
            Navigate-RightArrow -Times $tab.ArrowRight -ClickX 200 -ClickY 253
            Start-Sleep -Milliseconds 800

            if (Capture-Screenshot -FileName $tab.File -hWnd $app.hWnd) {
                $captured++
            } else {
                $failed++
            }

            # Reset: click back to the first tab for the next absolute navigation
            # Click first tab header position
            [WinAPI]::ClickAt(200, 253)
            Start-Sleep -Milliseconds 300
            # Go back to first by pressing Home or clicking
        }

        Stop-App $app
    }
}

# ── Phase 3: Theme screenshots ──────────────────────────────────
if (-not $FeaturesOnly) {
    Write-Host "`n>> Theme screenshots ($($themes.Count) themes)..." -ForegroundColor Cyan

    foreach ($theme in $themes) {
        Write-Host "  [$($captured+$failed+1)] $($theme.Name)..." -NoNewline

        Set-Theme $theme.Name
        Set-TabIndices -MainIndex 1 -SubKey "📁 File Management" -SubIndex 0  # Show File Cleaner for theme shots
        $app = Start-App
        if ($app) {
            Start-Sleep -Milliseconds 1500  # Extra time for theme rendering
            if (Capture-Screenshot -FileName $theme.File -hWnd $app.hWnd) {
                $captured++
            } else {
                $failed++
            }
            Stop-App $app
        } else {
            Write-Warning "    App failed to start"
            $failed++
        }
    }
}

# ── Restore original settings ────────────────────────────────────
if (Test-Path $backupFile) {
    Copy-Item $backupFile $settingsFile -Force
    Remove-Item $backupFile -Force
    Write-Host "`nOriginal settings restored." -ForegroundColor DarkGray
}

$pngCount = (Get-ChildItem "$outputDir\*.png" -ErrorAction SilentlyContinue).Count
Write-Host "`n============================================" -ForegroundColor Green
Write-Host "  COMPLETE: $captured captured, $failed failed" -ForegroundColor Green
Write-Host "  Total PNGs in output: $pngCount" -ForegroundColor Green
Write-Host "  $outputDir" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
