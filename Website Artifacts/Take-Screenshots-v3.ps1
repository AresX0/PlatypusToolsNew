# Take-Screenshots-v3.ps1 - Automated screenshot capture for PlatypusTools
# Uses settings.json modification + app restart for each tab
# Uses EnumWindows by title to find window (FindWindow doesn't work with .NET 10 WPF)
# Uses PrintWindow API for capture (captures window content regardless of Z-order)

param(
    [string]$AppPath = "C:\Projects\PlatypusToolsNew\publish\PlatypusTools.UI.exe",
    [string]$OutputDir = "C:\Projects\PlatypusToolsNew\Website Artifacts\screenshots",
    [int]$WaitSeconds = 45,
    [switch]$ThemesOnly,
    [switch]$NestedOnly,
    [switch]$FlatOnly
)

$ErrorActionPreference = 'Stop'
$settingsPath = "$env:APPDATA\PlatypusTools\settings.json"

# ── Load assemblies ──
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ── Win32 interop ──
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

public struct RECT { public int Left, Top, Right, Bottom; }

public class Win32Screenshot {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] 
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    
    [DllImport("user32.dll")] 
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    
    [DllImport("user32.dll")] 
    public static extern bool IsWindowVisible(IntPtr hWnd);
    
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] 
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    
    [DllImport("user32.dll")] 
    public static extern int GetWindowTextLength(IntPtr hWnd);
    
    [DllImport("user32.dll")] 
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [DllImport("user32.dll")] 
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")] 
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    
    [DllImport("user32.dll")] 
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    
    [DllImport("user32.dll")] 
    public static extern bool SetCursorPos(int X, int Y);
    
    [DllImport("user32.dll")] 
    public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    // PrintWindow captures a window's content to a device context (works even if window is behind others)
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // PW_RENDERFULLCONTENT = 2 (better capture for DWM-composed windows)
    public const uint PW_RENDERFULLCONTENT = 2;

    public static IntPtr FindWindowByPid(uint pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, lParam) => {
            uint wpid;
            GetWindowThreadProcessId(hWnd, out wpid);
            if (wpid == pid && IsWindowVisible(hWnd)) {
                int len = GetWindowTextLength(hWnd);
                if (len > 0) {
                    var sb = new StringBuilder(len + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    if (sb.ToString().Contains("PlatypusTools")) {
                        found = hWnd;
                        return false; // stop
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Find the PlatypusTools app window (NOT VS Code, Edge, etc.)
    // Uses EXACT title match first, then falls back to title-starts-with match
    // Excludes windows from known non-target apps (VS Code, Edge, Explorer, etc.)
    public static IntPtr FindWindowByTitle(string titlePart) {
        IntPtr exactMatch = IntPtr.Zero;
        IntPtr startsWithMatch = IntPtr.Zero;
        
        // Titles to EXCLUDE - these are other apps that may contain "PlatypusTools" in title
        string[] excludePatterns = new string[] {
            "Visual Studio Code", "Visual Studio", "Edge", "Chrome", "Firefox",
            "Explorer", "pwsh", "PowerShell", "Terminal", "cmd"
        };
        
        EnumWindows((hWnd, lParam) => {
            if (IsWindowVisible(hWnd)) {
                int len = GetWindowTextLength(hWnd);
                if (len > 0) {
                    var sb = new StringBuilder(len + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    
                    // Skip windows from known non-target apps
                    bool excluded = false;
                    foreach (var pattern in excludePatterns) {
                        if (title.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) return true; // continue enumeration
                    
                    // Exact match is best (title is exactly "PlatypusTools")
                    if (title == titlePart) {
                        exactMatch = hWnd;
                        return false; // stop - perfect match
                    }
                    
                    // Starts-with match as fallback (e.g. "PlatypusTools v4.0.0.9")
                    if (startsWithMatch == IntPtr.Zero && title.StartsWith(titlePart)) {
                        startsWithMatch = hWnd;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        
        return exactMatch != IntPtr.Zero ? exactMatch : startsWithMatch;
    }
}
"@

# ── Helper functions ──

function Update-Settings {
    param(
        [int]$MainTab,
        [hashtable]$SubTabs = @{},
        [hashtable]$SubSubTabs = @{},
        [string]$Theme = "Light"
    )
    $s = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $s.LastSelectedMainTabIndex = $MainTab
    $s.Theme = $Theme
    $s.RestoreLastSession = $true
    $s.RequireAdminRights = $false
    $s.FontScale = 1.0
    $s.WindowWidth = 1400
    $s.WindowHeight = 900
    $s.WindowTop = 50
    $s.WindowLeft = 100
    $s.WindowStateValue = 0
    
    # Update sub-tab indices
    foreach ($key in $SubTabs.Keys) {
        $s.LastSelectedSubTabIndices.$key = $SubTabs[$key]
    }
    
    # Update sub-sub-tab indices (3rd level, e.g. Audio/Image/Video nested tabs)
    # Ensure the property exists
    if (-not $s.PSObject.Properties['LastSelectedSubSubTabIndices']) {
        $s | Add-Member -NotePropertyName 'LastSelectedSubSubTabIndices' -NotePropertyValue @{} -Force
    }
    foreach ($key in $SubSubTabs.Keys) {
        $s.LastSelectedSubSubTabIndices | Add-Member -NotePropertyName $key -NotePropertyValue $SubSubTabs[$key] -Force
    }
    
    $s | ConvertTo-Json -Depth 4 | Set-Content $settingsPath -Encoding UTF8
}

function Kill-AllInstances {
    # Kill ALL PlatypusTools instances and wait until they're truly gone
    $attempts = 0
    do {
        Get-Process PlatypusTools* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $remaining = (Get-Process PlatypusTools* -ErrorAction SilentlyContinue | Measure-Object).Count
        $attempts++
    } while ($remaining -gt 0 -and $attempts -lt 5)
    
    if ($remaining -gt 0) {
        Write-Host "  WARNING: $remaining processes still alive after $attempts kill attempts"
    }
}

function Start-AppAndCapture {
    param(
        [string]$ScreenshotName,
        [int]$ExtraWaitMs = 0
    )
    
    # CRITICAL: Ensure no other instances are running before launching
    Kill-AllInstances
    
    # Launch app
    Start-Process $AppPath | Out-Null
    Write-Host "  Waiting ${WaitSeconds}s for app to load..."
    Start-Sleep -Seconds $WaitSeconds
    
    # Find window by title (not PID - single-file .NET apps may fork)
    $hwnd = [Win32Screenshot]::FindWindowByTitle("PlatypusTools")
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Host "  Window not ready, waiting 10 more seconds..."
        Start-Sleep -Seconds 10
        $hwnd = [Win32Screenshot]::FindWindowByTitle("PlatypusTools")
    }
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Host "  Still not found, final wait 10s..."
        Start-Sleep -Seconds 10
        $hwnd = [Win32Screenshot]::FindWindowByTitle("PlatypusTools")
    }
    
    if ($hwnd -eq [IntPtr]::Zero) {
        Write-Host "  ERROR: Window not found for $ScreenshotName - SKIPPING"
        Kill-AllInstances
        return $null
    }
    
    # Position and focus window
    [Win32Screenshot]::MoveWindow($hwnd, 100, 50, 1400, 900, $true) | Out-Null
    [Win32Screenshot]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds (1000 + $ExtraWaitMs)
    
    # Capture screenshot using PrintWindow API (captures window content regardless of Z-order)
    $rect = New-Object RECT
    [Win32Screenshot]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    
    if ($w -le 0 -or $h -le 0) {
        Write-Host "  ERROR: Invalid window dimensions ${w}x${h} for $ScreenshotName - SKIPPING"
        Kill-AllInstances
        return $null
    }
    
    try {
        # Use PrintWindow to capture the window's own content
        $hdcWindow = [Win32Screenshot]::GetDC($hwnd)
        $hdcMem = [Win32Screenshot]::CreateCompatibleDC($hdcWindow)
        $hBitmap = [Win32Screenshot]::CreateCompatibleBitmap($hdcWindow, $w, $h)
        $hOld = [Win32Screenshot]::SelectObject($hdcMem, $hBitmap)
        
        # PW_RENDERFULLCONTENT = 2 for WPF/DWM windows
        [Win32Screenshot]::PrintWindow($hwnd, $hdcMem, [Win32Screenshot]::PW_RENDERFULLCONTENT) | Out-Null
        
        [Win32Screenshot]::SelectObject($hdcMem, $hOld) | Out-Null
        
        # Convert HBITMAP to .NET Bitmap and save
        $bmp = [System.Drawing.Image]::FromHbitmap($hBitmap)
        $outPath = Join-Path $OutputDir "$ScreenshotName.png"
        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        
        # Cleanup GDI objects
        [Win32Screenshot]::DeleteObject($hBitmap) | Out-Null
        [Win32Screenshot]::DeleteDC($hdcMem) | Out-Null
        [Win32Screenshot]::ReleaseDC($hwnd, $hdcWindow) | Out-Null
        
        $size = (Get-Item $outPath).Length
        Write-Host "  CAPTURED: $ScreenshotName.png ($size bytes)"
    } catch {
        Write-Host "  ERROR: Capture failed for $ScreenshotName - $($_.Exception.Message)"
        Kill-AllInstances
        return $null
    }
    
    return @{ Hwnd = $hwnd }
}

function Stop-App {
    param($AppInfo)
    Kill-AllInstances
}

function Send-RightArrow {
    param([IntPtr]$Hwnd)
    [Win32Screenshot]::SetForegroundWindow($Hwnd) | Out-Null
    Start-Sleep -Milliseconds 100
    # VK_RIGHT = 0x27, KEYEVENTF_KEYDOWN = 0, KEYEVENTF_KEYUP = 2
    [Win32Screenshot]::keybd_event(0x27, 0, 0, [UIntPtr]::Zero)
    [Win32Screenshot]::keybd_event(0x27, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 800
}

function Click-At {
    param([int]$X, [int]$Y)
    [Win32Screenshot]::SetCursorPos($X, $Y) | Out-Null
    Start-Sleep -Milliseconds 50
    # MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4
    [Win32Screenshot]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [Win32Screenshot]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

function Capture-Window {
    param(
        [IntPtr]$Hwnd,
        [string]$ScreenshotName
    )
    [Win32Screenshot]::SetForegroundWindow($Hwnd) | Out-Null
    Start-Sleep -Milliseconds 300
    
    $rect = New-Object RECT
    [Win32Screenshot]::GetWindowRect($Hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    
    if ($w -le 0 -or $h -le 0) {
        Write-Host "  ERROR: Invalid dimensions ${w}x${h} for $ScreenshotName"
        return
    }
    
    try {
        # Use PrintWindow to capture the window's own content
        $hdcWindow = [Win32Screenshot]::GetDC($Hwnd)
        $hdcMem = [Win32Screenshot]::CreateCompatibleDC($hdcWindow)
        $hBitmap = [Win32Screenshot]::CreateCompatibleBitmap($hdcWindow, $w, $h)
        $hOld = [Win32Screenshot]::SelectObject($hdcMem, $hBitmap)
        
        [Win32Screenshot]::PrintWindow($Hwnd, $hdcMem, [Win32Screenshot]::PW_RENDERFULLCONTENT) | Out-Null
        
        [Win32Screenshot]::SelectObject($hdcMem, $hOld) | Out-Null
        
        $bmp = [System.Drawing.Image]::FromHbitmap($hBitmap)
        $outPath = Join-Path $OutputDir "$ScreenshotName.png"
        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        
        [Win32Screenshot]::DeleteObject($hBitmap) | Out-Null
        [Win32Screenshot]::DeleteDC($hdcMem) | Out-Null
        [Win32Screenshot]::ReleaseDC($Hwnd, $hdcWindow) | Out-Null
        
        $size = (Get-Item $outPath).Length
        Write-Host "  CAPTURED: $ScreenshotName.png ($size bytes)"
    } catch {
        Write-Host "  ERROR: Capture failed for $ScreenshotName - $($_.Exception.Message)"
    }
}

# ── Define all screenshot targets ──

# Flat tabs: each entry is [screenshotName, mainTabIndex, subTabCategory, subTabIndex]
$flatScreenshots = @(
    # Dashboard
    @("hero-overview", 0, $null, -1),
    
    # File Management (main=1)
    @("file-cleaner",        1, "`u{1F4C1} File Management", 0),
    @("duplicates",          1, "`u{1F4C1} File Management", 1),
    @("empty-folder-scanner",1, "`u{1F4C1} File Management", 2),
    @("robocopy",            1, "`u{1F4C1} File Management", 3),
    @("cloud-sync",          1, "`u{1F4C1} File Management", 4),
    @("file-diff",           1, "`u{1F4C1} File Management", 5),
    @("bulk-file-mover",     1, "`u{1F4C1} File Management", 6),
    @("symlink-manager",     1, "`u{1F4C1} File Management", 7),
    
    # Multimedia flat tabs (main=2)
    @("media-hub",           2, "`u{1F3AC} Multimedia", 0),
    @("media-library",       2, "`u{1F3AC} Multimedia", 4),
    @("external-tools",      2, "`u{1F3AC} Multimedia", 5),
    
    # System (main=3)
    @("disk-cleanup",        3, "`u{1F5A5}`u{FE0F} System", 0),
    @("privacy-cleaner",     3, "`u{1F5A5}`u{FE0F} System", 1),
    @("recent-cleaner",      3, "`u{1F5A5}`u{FE0F} System", 2),
    @("startup-manager",     3, "`u{1F5A5}`u{FE0F} System", 3),
    @("process-manager",     3, "`u{1F5A5}`u{FE0F} System", 4),
    @("registry-cleaner",    3, "`u{1F5A5}`u{FE0F} System", 5),
    @("scheduled-tasks",     3, "`u{1F5A5}`u{FE0F} System", 6),
    @("system-restore",      3, "`u{1F5A5}`u{FE0F} System", 7),
    @("windows-update",      3, "`u{1F5A5}`u{FE0F} System", 8),
    @("terminal",            3, "`u{1F5A5}`u{FE0F} System", 9),
    @("job-queue",           3, "`u{1F5A5}`u{FE0F} System", 10),
    @("scheduled-backup",    3, "`u{1F5A5}`u{FE0F} System", 11),
    @("env-variables",       3, "`u{1F5A5}`u{FE0F} System", 12),
    @("windows-services",    3, "`u{1F5A5}`u{FE0F} System", 13),
    @("disk-health",         3, "`u{1F5A5}`u{FE0F} System", 14),
    @("wifi-passwords",      3, "`u{1F5A5}`u{FE0F} System", 15),
    @("remote-dashboard",    3, "`u{1F5A5}`u{FE0F} System", 16),
    @("remote-desktop",      3, "`u{1F5A5}`u{FE0F} System", 17),
    
    # Security (main=4)
    @("folder-hider",        4, "`u{1F512} Security", 0),
    @("system-audit",        4, "`u{1F512} Security", 1),
    @("forensics-analyzer",  4, "`u{1F512} Security", 2),
    @("reboot-analyzer",     4, "`u{1F512} Security", 3),
    @("secure-wipe",         4, "`u{1F512} Security", 4),
    @("advanced-forensics",  4, "`u{1F512} Security", 5),
    @("hash-scanner",        4, "`u{1F512} Security", 6),
    @("ad-security",         4, "`u{1F512} Security", 7),
    @("cve-search",          4, "`u{1F512} Security", 8),
    @("file-integrity",      4, "`u{1F512} Security", 9),
    @("certificate-manager", 4, "`u{1F512} Security", 10),
    @("security-vault",      4, "`u{1F512} Security", 11),
    
    # Metadata (main=5, no sub-tabs)
    @("metadata-editor",     5, $null, -1),
    
    # Tools (main=6)
    @("website-downloader",  6, "`u{1F527} Tools", 0),
    @("file-analyzer",       6, "`u{1F527} Tools", 1),
    @("disk-space",          6, "`u{1F527} Tools", 2),
    @("network-tools",       6, "`u{1F527} Tools", 3),
    @("archive-manager",     6, "`u{1F527} Tools", 4),
    @("pdf-tools",           6, "`u{1F527} Tools", 5),
    @("screenshot",          6, "`u{1F527} Tools", 6),
    @("bootable-usb",        6, "`u{1F527} Tools", 7),
    @("plugin-manager",      6, "`u{1F527} Tools", 8),
    @("ftp-client",          6, "`u{1F527} Tools", 9),
    @("terminal-client",     6, "`u{1F527} Tools", 10),
    @("web-browser",         6, "`u{1F527} Tools", 11),
    @("plex-backup",         6, "`u{1F527} Tools", 12),
    @("screen-recorder",     6, "`u{1F527} Tools", 13),
    @("intune-packager",     6, "`u{1F527} Tools", 14),
    @("clipboard-history",   6, "`u{1F527} Tools", 15),
    @("qr-code",             6, "`u{1F527} Tools", 16),
    @("color-picker",        6, "`u{1F527} Tools", 17),
    @("bulk-checksum",       6, "`u{1F527} Tools", 18)
)

# Nested multimedia tabs: each entry is [screenshotName, parentSubIndex, subSubTabHeader, subSubTabIndex]
# parentSubIndex: 1=Audio, 2=Image, 3=Video
# subSubTabHeader: the emoji header of the parent group tab (used as key in LastSelectedSubSubTabIndices)
$nestedMultimedia = @(
    # Audio (parentSub=1, header="🎵 Audio")
    @("audio-player",       1, "`u{1F3B5} Audio", 0),
    @("audio-trim",         1, "`u{1F3B5} Audio", 1),
    @("audio-transcription", 1, "`u{1F3B5} Audio", 2),
    
    # Image (parentSub=2, header="🖼️ Image")
    @("image-edit",          2, "`u{1F5BC}`u{FE0F} Image", 0),
    @("image-converter",     2, "`u{1F5BC}`u{FE0F} Image", 1),
    @("image-resizer",       2, "`u{1F5BC}`u{FE0F} Image", 2),
    @("ico-converter",       2, "`u{1F5BC}`u{FE0F} Image", 3),
    @("batch-watermark",     2, "`u{1F5BC}`u{FE0F} Image", 4),
    @("image-scaler",        2, "`u{1F5BC}`u{FE0F} Image", 5),
    @("3d-model-editor",     2, "`u{1F5BC}`u{FE0F} Image", 6),
    
    # Video (parentSub=3, header="🎬 Video")
    @("video-player",        3, "`u{1F3AC} Video", 0),
    @("video-editor",        3, "`u{1F3AC} Video", 1),
    @("upscaler",            3, "`u{1F3AC} Video", 2),
    @("video-combiner",      3, "`u{1F3AC} Video", 3),
    @("video-converter",     3, "`u{1F3AC} Video", 4),
    @("export-queue",        3, "`u{1F3AC} Video", 5),
    @("video-metadata",      3, "`u{1F3AC} Video", 6),
    @("gif-maker",           3, "`u{1F3AC} Video", 7)
)

# Theme screenshots
$themes = @(
    @("theme-light", "Light"),
    @("theme-dark", "Dark"),
    @("theme-lcars", "LCARS"),
    @("theme-glass", "Glass"),
    @("theme-pipboy", "PipBoy"),
    @("theme-klingon", "Klingon"),
    @("theme-kpop", "KPopDemonHunters"),
    @("theme-highcontrast", "HighContrast")
)

# ── Execution ──

Write-Host "═══════════════════════════════════════════════"
Write-Host " PlatypusTools Screenshot Automation v3"
Write-Host " Output: $OutputDir"
Write-Host " App: $AppPath"
Write-Host " Wait: ${WaitSeconds}s per launch"
Write-Host "═══════════════════════════════════════════════"
Write-Host ""

# Check existing PNGs - skip those already captured
$existingPngs = @()
Get-ChildItem "$OutputDir\*.png" -ErrorAction SilentlyContinue | ForEach-Object { $existingPngs += $_.BaseName }
Write-Host "Existing screenshots: $($existingPngs.Count) - will skip these"
Write-Host ""

$captured = 0
$failed = 0
$startTime = Get-Date

# ── Phase 1: Flat tabs (settings-based, one restart per screenshot) ──
if (-not $ThemesOnly -and -not $NestedOnly) {
    Write-Host "━━━ PHASE 1: Flat tabs ($($flatScreenshots.Count) screenshots) ━━━"
    
    $skipped = 0
    foreach ($entry in $flatScreenshots) {
        $name = $entry[0]
        $mainIdx = $entry[1]
        $subCategory = $entry[2]
        $subIdx = $entry[3]
        
        if ($name -in $existingPngs) {
            $skipped++
            continue
        }
        
        Write-Host "[$($captured+$failed+1)/$($flatScreenshots.Count)] $name (main=$mainIdx, sub=$subIdx) [skipped $skipped]"
        
        # Build sub-tab settings
        $subTabs = @{}
        if ($subCategory -and $subIdx -ge 0) {
            $subTabs[$subCategory] = $subIdx
        }
        
        Update-Settings -MainTab $mainIdx -SubTabs $subTabs -Theme "Light"
        $info = Start-AppAndCapture -ScreenshotName $name
        
        if ($info) { $captured++ } else { $failed++ }
        Stop-App $info
    }
    Write-Host "  (Skipped $skipped existing screenshots)"
    
    Write-Host ""
    Write-Host "Phase 1 complete: $captured captured, $failed failed"
    Write-Host ""
}

# ── Phase 2: Nested multimedia tabs (settings-based, one restart per screenshot) ──
if (-not $ThemesOnly -and -not $FlatOnly) {
    Write-Host "━━━ PHASE 2: Nested multimedia tabs ($($nestedMultimedia.Count) screenshots) ━━━"
    
    $skipped = 0
    foreach ($entry in $nestedMultimedia) {
        $name = $entry[0]
        $parentSub = $entry[1]
        $subSubHeader = $entry[2]
        $subSubIdx = $entry[3]
        
        if ($name -in $existingPngs) {
            $skipped++
            continue
        }
        
        Write-Host "[$($captured+$failed+1)] $name (multimedia, parent=$parentSub, subSub=$subSubIdx)"
        
        # Set main tab to Multimedia (2), sub-tab to Audio/Image/Video group, and sub-sub-tab to specific tool
        $mmKey = "`u{1F3AC} Multimedia"
        Update-Settings -MainTab 2 -SubTabs @{ $mmKey = $parentSub } -SubSubTabs @{ $subSubHeader = $subSubIdx } -Theme "Light"
        $info = Start-AppAndCapture -ScreenshotName $name -ExtraWaitMs 500
        
        if ($info) { $captured++ } else { $failed++ }
        Stop-App $info
    }
    Write-Host "  (Skipped $skipped existing screenshots)"
    
    Write-Host ""
    Write-Host "Phase 2 complete: $captured captured, $failed failed"
    Write-Host ""
}

# ── Phase 3: Theme screenshots ──
if (-not $FlatOnly -and -not $NestedOnly) {
    Write-Host "━━━ PHASE 3: Theme screenshots ($($themes.Count) themes) ━━━"
    
    foreach ($theme in $themes) {
        $name = $theme[0]
        $themeValue = $theme[1]
        
        if ($name -in $existingPngs) {
            Write-Host "Theme: $themeValue (already exists, skipping)"
            continue
        }
        
        Write-Host "Theme: $themeValue"
        
        Update-Settings -MainTab 0 -Theme $themeValue
        $info = Start-AppAndCapture -ScreenshotName $name -ExtraWaitMs 500
        
        if ($info) { $captured++ } else { $failed++ }
        Stop-App $info
    }
    
    Write-Host ""
    Write-Host "Phase 3 complete"
    Write-Host ""
}

# ── Restore original settings ──
Update-Settings -MainTab 0 -Theme "Light"

# ── Summary ──
$elapsed = (Get-Date) - $startTime
Write-Host "═══════════════════════════════════════════════"
Write-Host " COMPLETE"
Write-Host " Captured: $captured"
Write-Host " Failed:   $failed"
Write-Host " Time:     $($elapsed.ToString('mm\:ss'))"
Write-Host " Output:   $OutputDir"
Write-Host "═══════════════════════════════════════════════"

# List all captured PNGs
Write-Host ""
Write-Host "Files:"
Get-ChildItem "$OutputDir\*.png" | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name) ($($_.Length) bytes)"
}
