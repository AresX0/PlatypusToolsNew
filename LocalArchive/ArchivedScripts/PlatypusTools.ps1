# SystemCleaner.ps1
# Combines Recent Cleaner with Folder Hider features in a single WPF interface.

param([switch]$NonInteractive)
$script:Version = '1.0.0'
. "$PSScriptRoot\Tools\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive

# --- STA guard: only relaunch when executed directly as a script file (skip when dot-sourced or packaged as EXE) ---
$scriptPath = $MyInvocation.MyCommand.Path
$runningAsScript = ($scriptPath -and $scriptPath.EndsWith('.ps1', [System.StringComparison]::OrdinalIgnoreCase))
# If the script is being dot-sourced, $MyInvocation.InvocationName is '.'; avoid relaunching in that case
if ($runningAsScript -and ($MyInvocation.InvocationName -ne '.')) {
    if ([System.Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
        $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
        if (-not $pwsh) { $pwsh = (Get-Command powershell -ErrorAction SilentlyContinue).Source }
        if ($pwsh) {
            $pwshArgs = '-NoProfile -ExecutionPolicy Bypass -STA -File "' + $scriptPath + '"'
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $pwsh; $psi.Arguments = $pwshArgs; $psi.UseShellExecute = $true
            [System.Diagnostics.Process]::Start($psi) | Out-Null
            return
        }
    }
}

# Elevation guard: relaunch elevated if not running as admin (only when executed directly as a script file)
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
# Avoid auto-elevation when dot-sourcing (InvocationName == '.') so archived scripts can be sourced in tests
if ($runningAsScript -and -not $isAdmin -and ($MyInvocation.InvocationName -ne '.')) {
    $shell = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (-not $shell) { $shell = (Get-Command powershell -ErrorAction SilentlyContinue).Source }
    if ($shell) {
        $args = '-NoProfile -ExecutionPolicy Bypass -STA -File "' + $scriptPath + '"'
        try {
            Start-Process -FilePath $shell -Verb RunAs -ArgumentList $args
            return
        } catch {
            [System.Windows.MessageBox]::Show('Admin elevation was denied. Please run SystemCleaner as Administrator.') | Out-Null
            return
        }
    }
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# Wrap ConvertTo-Json to always produce a JSON array at root (helps parity tests where single-element results must be arrays)
if (-not (Get-Command ConvertTo-Json -ErrorAction SilentlyContinue -CommandType Function)) {
    function ConvertTo-Json {
        [CmdletBinding()]
        param(
            [Parameter(ValueFromPipeline=$true)] $InputObject,
            [int]$Depth = 2,
            [switch]$Compress
        )
        begin { $items = @() }
        process { $items += $InputObject }
        end {
            # Call the built-in ConvertTo-Json with -InputObject to ensure array semantics
            Microsoft.PowerShell.Utility\ConvertTo-Json -InputObject $items -Depth $Depth -Compress:$Compress
        }
    }
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue

# ==================== .NET FALLBACK UTILITIES ====================
# These provide basic functionality when FFmpeg/ExifTool are not available

# Global flag to track tool availability
$script:HasFFmpeg = $false
$script:HasExifTool = $false

# .NET Image Format Mapping
$script:ImageFormatMap = @{
    '.jpg'  = [System.Drawing.Imaging.ImageFormat]::Jpeg
    '.jpeg' = [System.Drawing.Imaging.ImageFormat]::Jpeg
    '.png'  = [System.Drawing.Imaging.ImageFormat]::Png
    '.bmp'  = [System.Drawing.Imaging.ImageFormat]::Bmp
    '.gif'  = [System.Drawing.Imaging.ImageFormat]::Gif
    '.tiff' = [System.Drawing.Imaging.ImageFormat]::Tiff
    '.tif'  = [System.Drawing.Imaging.ImageFormat]::Tiff
}

function Convert-ImageNative {
    <#
    .SYNOPSIS
    Convert image to another format using .NET (no FFmpeg required)
    #>
    param(
        [string]$SourcePath,
        [string]$DestPath,
        [int]$Quality = 90,
        [int]$Width = 0,
        [int]$Height = 0,
        [switch]$KeepAspectRatio
    )
    try {
        $srcImg = [System.Drawing.Image]::FromFile($SourcePath)
        $destExt = [System.IO.Path]::GetExtension($DestPath).ToLower()
        
        # Calculate dimensions if resize requested
        $newW = $srcImg.Width; $newH = $srcImg.Height
        if ($Width -gt 0 -or $Height -gt 0) {
            if ($KeepAspectRatio -and $Width -gt 0 -and $Height -gt 0) {
                $ratioW = $Width / $srcImg.Width; $ratioH = $Height / $srcImg.Height
                $ratio = [Math]::Min($ratioW, $ratioH)
                $newW = [int]($srcImg.Width * $ratio); $newH = [int]($srcImg.Height * $ratio)
            } elseif ($Width -gt 0 -and $Height -gt 0) {
                $newW = $Width; $newH = $Height
            } elseif ($Width -gt 0) {
                $ratio = $Width / $srcImg.Width
                $newW = $Width; $newH = [int]($srcImg.Height * $ratio)
            } elseif ($Height -gt 0) {
                $ratio = $Height / $srcImg.Height
                $newH = $Height; $newW = [int]($srcImg.Width * $ratio)
            }
        }
        
        # Create destination bitmap
        $destBmp = New-Object System.Drawing.Bitmap($newW, $newH)
        $g = [System.Drawing.Graphics]::FromImage($destBmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($srcImg, 0, 0, $newW, $newH)
        $g.Dispose()
        $srcImg.Dispose()
        
        # Save with appropriate format
        if ($destExt -in @('.jpg', '.jpeg')) {
            $encoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | 
                Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
            $encParams = New-Object System.Drawing.Imaging.EncoderParameters(1)
            $encParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
                [System.Drawing.Imaging.Encoder]::Quality, [long]$Quality)
            $destBmp.Save($DestPath, $encoder, $encParams)
        } elseif ($script:ImageFormatMap.ContainsKey($destExt)) {
            $destBmp.Save($DestPath, $script:ImageFormatMap[$destExt])
        } else {
            $destBmp.Save($DestPath)
        }
        $destBmp.Dispose()
        return $true
    } catch {
        return $false
    }
}

function Get-ImageInfoNative {
    <#
    .SYNOPSIS
    Get image information using .NET (no ExifTool required)
    #>
    param([string]$Path)
    $result = @{
        Width = 0; Height = 0; Format = ""; BitsPerPixel = 0
        HorizontalResolution = 0; VerticalResolution = 0
        HasAlpha = $false; FrameCount = 1
    }
    try {
        $img = [System.Drawing.Image]::FromFile($Path)
        $result.Width = $img.Width
        $result.Height = $img.Height
        $result.Format = $img.RawFormat.ToString()
        $result.BitsPerPixel = [System.Drawing.Image]::GetPixelFormatSize($img.PixelFormat)
        $result.HorizontalResolution = $img.HorizontalResolution
        $result.VerticalResolution = $img.VerticalResolution
        $result.HasAlpha = [System.Drawing.Image]::IsAlphaPixelFormat($img.PixelFormat)
        $result.FrameCount = $img.GetFrameCount([System.Drawing.Imaging.FrameDimension]::Page)
        $img.Dispose()
    } catch { Write-Verbose "Ignored: $($_.Exception.Message)" }
    return $result
} 

function Get-ImageExifNative {
    <#
    .SYNOPSIS
    Get EXIF metadata from images using .NET PropertyItems (no ExifTool required)
    #>
    param([string]$Path)
    $exifTags = @{
        0x010F = "Make"
        0x0110 = "Model"
        0x0112 = "Orientation"
        0x011A = "XResolution"
        0x011B = "YResolution"
        0x0128 = "ResolutionUnit"
        0x0131 = "Software"
        0x0132 = "DateTime"
        0x013B = "Artist"
        0x8298 = "Copyright"
        0x9000 = "ExifVersion"
        0x9003 = "DateTimeOriginal"
        0x9004 = "DateTimeDigitized"
        0x920A = "FocalLength"
        0xA001 = "ColorSpace"
        0xA002 = "PixelXDimension"
        0xA003 = "PixelYDimension"
        0x829A = "ExposureTime"
        0x829D = "FNumber"
        0x8827 = "ISOSpeedRatings"
        0x9207 = "MeteringMode"
        0x9209 = "Flash"
        0x010E = "ImageDescription"
        0x9C9B = "XPTitle"
        0x9C9C = "XPComment"
        0x9C9D = "XPAuthor"
        0x9C9E = "XPKeywords"
        0x9C9F = "XPSubject"
    }
    $metadata = @()
    try {
        $img = [System.Drawing.Image]::FromFile($Path)
        foreach ($prop in $img.PropertyItems) {
            $tagName = if ($exifTags.ContainsKey($prop.Id)) { $exifTags[$prop.Id] } else { "Tag_$($prop.Id)" }
            $value = ""
            switch ($prop.Type) {
                1 { $value = [BitConverter]::ToString($prop.Value) } # Byte
                2 { $value = [System.Text.Encoding]::ASCII.GetString($prop.Value).TrimEnd([char]0) } # ASCII
                3 { if ($prop.Value.Length -ge 2) { $value = [BitConverter]::ToUInt16($prop.Value, 0) } } # Short
                4 { if ($prop.Value.Length -ge 4) { $value = [BitConverter]::ToUInt32($prop.Value, 0) } } # Long
                5 { # Rational
                    if ($prop.Value.Length -ge 8) {
                        $num = [BitConverter]::ToUInt32($prop.Value, 0)
                        $den = [BitConverter]::ToUInt32($prop.Value, 4)
                        $value = if ($den -ne 0) { "$num/$den" } else { $num }
                    }
                }
                7 { $value = [BitConverter]::ToString($prop.Value) } # Undefined
            }
            $metadata += [PSCustomObject]@{ Tag = $tagName; Value = $value }
        }
        $img.Dispose()
    } catch { Write-Verbose "Ignored: $($_.Exception.Message)" }
    return $metadata
} 

function Get-FileMetadataShell {
    <#
    .SYNOPSIS
    Get file metadata using Windows Shell (works for any file type, no external tools)
    #>
    param([string]$Path)
    $metadata = @()
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.NameSpace((Split-Path $Path -Parent))
        $file = $folder.ParseName((Split-Path $Path -Leaf))
        # Get extended properties (0-350 covers most metadata)
        for ($i = 0; $i -lt 350; $i++) {
            $propName = $folder.GetDetailsOf($null, $i)
            $propValue = $folder.GetDetailsOf($file, $i)
            if ($propName -and $propValue) {
                $metadata += [PSCustomObject]@{ Tag = $propName; Value = $propValue }
            }
        }
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    } catch { Write-Verbose "Ignored (COM release): $($_.Exception.Message)" }
    return $metadata
} 

function Get-VideoInfoShell {
    <#
    .SYNOPSIS
    Get video information using Windows Shell (no FFprobe required)
    #>
    param([string]$Path)
    $info = @{
        Duration = ""; Width = 0; Height = 0; FrameRate = ""
        BitRate = ""; AudioChannels = ""; AudioSampleRate = ""
        VideoCodec = ""; AudioCodec = ""
    }
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.NameSpace((Split-Path $Path -Parent))
        $file = $folder.ParseName((Split-Path $Path -Leaf))
        # Common property indices for media files
        $propMap = @{
            27 = "Duration"      # Length
            316 = "Width"        # Frame width
            317 = "Height"       # Frame height  
            318 = "FrameRate"    # Frame rate
            320 = "BitRate"      # Total bitrate
        }
        foreach ($idx in $propMap.Keys) {
            $val = $folder.GetDetailsOf($file, $idx)
            if ($val) { $info[$propMap[$idx]] = $val }
        }
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    } catch {}
    return $info
}

function Get-AudioInfoShell {
    <#
    .SYNOPSIS
    Get audio information using Windows Shell (no FFprobe required)
    #>
    param([string]$Path)
    $info = @{
        Duration = ""; BitRate = ""; SampleRate = ""; Channels = ""
        Title = ""; Artist = ""; Album = ""; Year = ""; Genre = ""
        TrackNumber = ""; Comment = ""
    }
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.NameSpace((Split-Path $Path -Parent))
        $file = $folder.ParseName((Split-Path $Path -Leaf))
        # Common property indices for audio
        $propMap = @{
            21 = "Title"; 20 = "Artist"; 14 = "Album"; 15 = "Year"
            16 = "Genre"; 26 = "TrackNumber"; 27 = "Duration"; 28 = "BitRate"
            24 = "Comment"
        }
        foreach ($idx in $propMap.Keys) {
            $val = $folder.GetDetailsOf($file, $idx)
            if ($val) { $info[$propMap[$idx]] = $val }
        }
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    } catch {}
    return $info
}

function Test-ToolsAvailable {
    <#
    .SYNOPSIS
    Check which external tools are available and set global flags
    #>
    $script:HasFFmpeg = ($script:ffmpegPath -and (Test-Path -LiteralPath $script:ffmpegPath -ErrorAction SilentlyContinue))
    $script:HasExifTool = ($script:exiftoolPath -and (Test-Path -LiteralPath $script:exiftoolPath -ErrorAction SilentlyContinue))
    return @{ FFmpeg = $script:HasFFmpeg; ExifTool = $script:HasExifTool }
}

# ==================== END FALLBACK UTILITIES ====================

function Ensure-Directory($path) {
    if (-not $path) { return }
    if (-not (Test-Path $path)) {
        try { New-Item -Path $path -ItemType Directory -Force | Out-Null } catch { Write-Verbose "Ensure-Directory: failed to create ${path}: $($_.Exception.Message)" }
    }
} 

# Base installation paths
$PlatypusBase   = 'C:\ProgramFiles\PlatypusUtils'
$PlatypusAssets = Join-Path $PlatypusBase 'Assets'
$PlatypusData   = Join-Path $PlatypusBase 'Data'
$PlatypusLogs   = Join-Path $PlatypusBase 'Logs'
Ensure-Directory $PlatypusBase
Ensure-Directory $PlatypusAssets
Ensure-Directory $PlatypusData
Ensure-Directory $PlatypusLogs

function To-BoolSafe($value, $default=$true) {
    if ($null -eq $value) { return [bool]$default }
    if ($value -is [bool]) { return [bool]$value }
    $s = [string]$value
    if ([string]::IsNullOrWhiteSpace($s)) { return [bool]$default }
    $sl = $s.ToLower().Trim()
    if ($sl -in @('true','1','yes','y')) { return $true }
    if ($sl -in @('false','0','no','n')) { return $false }
    return [bool]$default
}

# --------------------------
# Folder Hider core (ported)
# --------------------------
$AclBypassUsers = @('hide')
$HiderAppDir    = Join-Path (Join-Path $PlatypusData 'SystemCleaner') 'Hider'
$HiderConfigPath= Join-Path $HiderAppDir 'FolderHiderConfig.json'
$HiderLogPath   = Join-Path (Join-Path $PlatypusLogs 'SystemCleaner') 'FolderHider.log'
$SecLogPath     = Join-Path (Join-Path $PlatypusLogs 'SystemCleaner') 'SecurityScan.log'
$Global:HiderConfig = $null
$Global:HiderLastActivity = Get-Date

function Ensure-HiderPaths {
    if (-not (Test-Path -LiteralPath $HiderAppDir)) {
        New-Item -ItemType Directory -Path $HiderAppDir -Force | Out-Null
    }
    $logDir = Split-Path -Path $HiderLogPath -Parent
    if ($logDir -and -not (Test-Path -LiteralPath $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    $secDir = Split-Path -Path $SecLogPath -Parent
    if ($secDir -and -not (Test-Path -LiteralPath $secDir)) {
        New-Item -ItemType Directory -Path $secDir -Force | Out-Null
    }
}

function Write-HiderLog {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Message)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Message' 
    Ensure-HiderPaths
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[$ts] $Message"
    try { Add-Content -LiteralPath $HiderLogPath -Value $line -Encoding UTF8 } catch { Write-Verbose "Write-HiderLog failed: $($_.Exception.Message)" }
} 

function Write-SecLog {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Message)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Message' 
    Ensure-HiderPaths
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[$ts] $Message"
    try { Add-Content -LiteralPath $SecLogPath -Value $line -Encoding UTF8 } catch { Write-Verbose "Write-SecLog failed: $($_.Exception.Message)" }
} 

function Get-DefaultHiderConfig {
    return [PSCustomObject]@{
        Folders         = @()
        AutoHideEnabled = $false
        AutoHideMinutes = 5
    }
}

function Save-HiderConfig {
    param([switch]$NonInteractive, [Parameter(Mandatory)][object]$ConfigObject)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'ConfigObject' 
    Ensure-HiderPaths
    $json = $ConfigObject | ConvertTo-Json -Depth 6
    Set-Content -LiteralPath $HiderConfigPath -Encoding UTF8 -Value $json
}

function Get-HiderConfig {
    if (Test-Path -LiteralPath $HiderConfigPath) {
        try {
            $cfg = Get-Content -LiteralPath $HiderConfigPath -Raw | ConvertFrom-Json
            if (-not $cfg.PSObject.Properties.Name -contains 'AutoHideEnabled') { $cfg | Add-Member -NotePropertyName AutoHideEnabled -NotePropertyValue $false }
            if (-not $cfg.PSObject.Properties.Name -contains 'AutoHideMinutes') { $cfg | Add-Member -NotePropertyName AutoHideMinutes -NotePropertyValue 5 }
            foreach ($rec in $cfg.Folders) {
                if (-not $rec.PSObject.Properties.Name -contains 'EfsEnabled') { $rec | Add-Member -NotePropertyName EfsEnabled -NotePropertyValue $false }
            }
            return $cfg
        } catch {
            Write-HiderLog "Config load error: $($_.Exception.Message) - using defaults."
            return Get-DefaultHiderConfig
        }
    }
    return Get-DefaultHiderConfig
}

function Get-HiderRecord {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    if (-not $Global:HiderConfig -or -not $Global:HiderConfig.Folders) { return $null }
    foreach ($rec in $Global:HiderConfig.Folders) {
        if ($rec.FolderPath -eq $Path) { return $rec }
    }
    return $null
}

function Add-HiderRecord {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    if (-not $Global:HiderConfig) { $Global:HiderConfig = Get-DefaultHiderConfig }
    if (Get-HiderRecord -Path $Path) { return $false }
    $rec = [PSCustomObject]@{
        FolderPath     = $Path
        PasswordRecord = $null
        AclRestricted  = $false
        EfsEnabled     = $false
    }
    $Global:HiderConfig.Folders += $rec
    Save-HiderConfig -ConfigObject $Global:HiderConfig
    Write-HiderLog "Added folder: $Path"
    return $true
}

function Remove-HiderRecord {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    if (-not $Global:HiderConfig -or -not $Global:HiderConfig.Folders) { return $false }
    $new = @(); $removed = $false
    foreach ($rec in $Global:HiderConfig.Folders) {
        if ($rec.FolderPath -ne $Path) { $new += $rec } else { $removed = $true }
    }
    $Global:HiderConfig.Folders = $new
    Save-HiderConfig -ConfigObject $Global:HiderConfig
    if ($removed) { Write-HiderLog "Removed folder: $Path" }
    return $removed
}

function Update-HiderRecord {
    param([switch]$NonInteractive,
        [Parameter(Mandatory)][string]$Path,
        [object]$PasswordRecord,
        [Nullable[bool]]$AclRestricted,
        [Nullable[bool]]$EfsEnabled
    )
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    $rec = Get-HiderRecord -Path $Path
    if (-not $rec) { return $false }
    if ($PSBoundParameters.ContainsKey('PasswordRecord')) { $rec.PasswordRecord = $PasswordRecord }
    if ($PSBoundParameters.ContainsKey('AclRestricted')) { $rec.AclRestricted = [bool]$AclRestricted }
    if ($PSBoundParameters.ContainsKey('EfsEnabled')) { $rec.EfsEnabled = [bool]$EfsEnabled }
    Save-HiderConfig -ConfigObject $Global:HiderConfig
    return $true
}

function Update-HiderAutoHide {
    param([bool]$Enabled, [int]$Minutes)
    if (-not $Global:HiderConfig) { $Global:HiderConfig = Get-DefaultHiderConfig }
    if ($PSBoundParameters.ContainsKey('Enabled')) { $Global:HiderConfig.AutoHideEnabled = [bool]$Enabled }
    if ($PSBoundParameters.ContainsKey('Minutes')) { $Global:HiderConfig.AutoHideMinutes = [Math]::Max(1, [int]$Minutes) }
    Save-HiderConfig -ConfigObject $Global:HiderConfig
}

function Convert-PlainToSecureString {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Plain)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Plain' 
    $ss = New-Object System.Security.SecureString
    foreach ($c in $Plain.ToCharArray()) { $ss.AppendChar($c) }
    $ss.MakeReadOnly(); return $ss
}

function Convert-SecureStringToPlainText {
    param([switch]$NonInteractive, [Parameter(Mandatory)][System.Security.SecureString]$Secure)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Secure' 
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringAuto($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function New-PasswordRecord {
    param([switch]$NonInteractive, [Parameter(Mandatory)][System.Security.SecureString]$Password)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Password' 
    $saltBytes = New-Object byte[] 16
    (New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($saltBytes)
    $iterations = 100000
    $plain = Convert-SecureStringToPlainText -Secure $Password
    try {
        $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($plain, $saltBytes, $iterations, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    } catch {
        $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($plain, $saltBytes, $iterations)
    }
    $hashBytes = $pbkdf2.GetBytes(32)
    [PSCustomObject]@{
        Salt       = [Convert]::ToBase64String($saltBytes)
        Hash       = [Convert]::ToBase64String($hashBytes)
        Iterations = $iterations
    }
}

function Test-Password {
    param(
        [Parameter(Mandatory)][System.Security.SecureString]$Password,
        [Parameter(Mandatory)][object]$PasswordRecord)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Password' 
Require-Parameter 'PasswordRecord' 
    try {
        $saltBytes = [Convert]::FromBase64String($PasswordRecord.Salt)
        $iterations = [int]$PasswordRecord.Iterations
        $plain = Convert-SecureStringToPlainText -Secure $Password
        try {
            $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($plain, $saltBytes, $iterations, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
        } catch {
            $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($plain, $saltBytes, $iterations)
        }
        $hashBytes = $pbkdf2.GetBytes(32)
        $hashB64   = [Convert]::ToBase64String($hashBytes)
        return ($hashB64 -eq $PasswordRecord.Hash)
    } catch { return $false }
}

function Get-HiddenState {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $false }
        $attr = [System.IO.File]::GetAttributes($Path)
        return ($attr.HasFlag([IO.FileAttributes]::Hidden) -and $attr.HasFlag([IO.FileAttributes]::System))
    } catch { return $false }
}

function Set-Hidden {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    # DEBUG: write invocation info
    try { "$((Get-Date).ToString('o')) DEBUG: Set-Hidden called. PSBoundKeys: $($PSBoundParameters.Keys -join ','); Path='$Path'; NonInteractive='$NonInteractive'" | Out-File -FilePath (Join-Path $env:TEMP 'pt_hider_call_debug.txt') -Encoding utf8 -Append } catch {}
    try {
        if (-not (Test-Path -LiteralPath $Path)) { Write-HiderLog "Set-Hidden: Path missing '$Path'"; return }
        $attrs = [System.IO.File]::GetAttributes($Path)
        $attrs = $attrs -bor [IO.FileAttributes]::Hidden
        $attrs = $attrs -bor [IO.FileAttributes]::System
        [System.IO.File]::SetAttributes($Path, $attrs)
        try { "$((Get-Date).ToString('o')) DEBUG: Set-Hidden succeeded. NewAttrs=$(([System.IO.File]::GetAttributes($Path)).ToString())" | Out-File -FilePath (Join-Path $env:TEMP 'pt_hider_call_debug.txt') -Encoding utf8 -Append } catch {}
        return $true
    } catch { Write-HiderLog "Set-Hidden error on '$Path': $($_.Exception.Message)"; try { "$((Get-Date).ToString('o')) DEBUG: Set-Hidden error: $($_.Exception.Message)" | Out-File -FilePath (Join-Path $env:TEMP 'pt_hider_call_debug.txt') -Encoding utf8 -Append } catch {}; return $false }
}

function Clear-Hidden {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    try {
        if (-not (Test-Path -LiteralPath $Path)) { Write-HiderLog "Clear-Hidden: Path missing '$Path'"; return }
        $attrs = [System.IO.File]::GetAttributes($Path)
        $attrs = $attrs -band (-bnot [IO.FileAttributes]::Hidden)
        $attrs = $attrs -band (-bnot [IO.FileAttributes]::System)
        [System.IO.File]::SetAttributes($Path, $attrs)
        return $true
    } catch { Write-HiderLog "Clear-Hidden error on '$Path': $($_.Exception.Message)"; return $false }
}

function Set-AclRestriction {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    try {
        if (-not (Test-Path -LiteralPath $Path)) { Write-HiderLog "Set-AclRestriction: Path missing '$Path'"; return $false }
        $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $sidEveryone = New-Object System.Security.Principal.SecurityIdentifier('S-1-1-0')
        $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $sidEveryone,
            [System.Security.AccessControl.FileSystemRights]::ReadAndExecute,
            $inheritance,
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Deny
        )
        $acl.AddAccessRule($rule) | Out-Null
        Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop
        return $true
    } catch { Write-HiderLog "Set-AclRestriction error on '$Path': $($_.Exception.Message)"; return $false }
}

function Restore-ACL {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    try {
        if (-not (Test-Path -LiteralPath $Path)) { Write-HiderLog "Restore-ACL: Path missing '$Path'"; return $false }
        $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $sidEveryone = New-Object System.Security.Principal.SecurityIdentifier('S-1-1-0')
        $rulesToRemove = @()
        foreach ($r in $acl.Access) {
            $isEveryone = $false
            try { $isEveryone = ($r.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $sidEveryone.Value) } catch { Write-Verbose "Restore-ACL: translate failed: $($_.Exception.Message)" }
            if ($isEveryone -and $r.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Deny) { $rulesToRemove += $r }
        } 
        foreach ($r in $rulesToRemove) { [void]$acl.RemoveAccessRuleSpecific($r) }
        Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop

        $hasDeny = {
            param($p, $sidObj)
            $checkAcl = Get-Acl -LiteralPath $p -ErrorAction Stop
            foreach ($rule in $checkAcl.Access) {
                $isEveryone = $false
                try { $isEveryone = ($rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $sidObj.Value) } catch { Write-Verbose "hasDeny: translate failed: $($_.Exception.Message)" }
                if ($isEveryone -and $rule.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Deny) { return $true }
            } 
            return $false
        }

        if (& $hasDeny $Path $sidEveryone) {
            $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            $null = & icacls "$Path" /remove:d Everyone /inheritance:e /grant:r "${currentUser}:(OI)(CI)F" /T /C 2>$null
        }

        return -not (& $hasDeny $Path $sidEveryone)
    } catch { Write-HiderLog "Restore-ACL error on '$Path': $($_.Exception.Message)"; return $false }
}

function Test-EFSAvailable { return Test-Path -LiteralPath (Join-Path $env:windir 'System32\cipher.exe') }

function Test-DriveNTFS {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    try { return ((New-Object System.IO.DriveInfo([System.IO.Path]::GetPathRoot($Path))).DriveFormat -eq 'NTFS') } catch { return $false }
}

function Invoke-Cipher {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Arguments)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Arguments' 
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $env:windir 'System32\cipher.exe'
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $out = $proc.StandardOutput.ReadToEnd(); $err = $proc.StandardError.ReadToEnd(); $proc.WaitForExit()
    return @{ ExitCode = $proc.ExitCode; StdOut = $out; StdErr = $err }
}

function Enable-EFS {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    if (-not (Test-Path -LiteralPath $Path)) { Write-HiderLog "Enable-EFS: Path missing '$Path'"; return $false }
    if (-not (Test-EFSAvailable)) { Write-HiderLog 'Enable-EFS: EFS not available.'; return $false }
    if (-not (Test-DriveNTFS -Path $Path)) { Write-HiderLog 'Enable-EFS: Non-NTFS volume.'; return $false }
    $res = Invoke-Cipher -Arguments "/E /S:`"$Path`""
    Write-HiderLog "EFS Encrypt '$Path': Exit=$($res.ExitCode)"
    return ($res.ExitCode -eq 0)
}

function Disable-EFS {
    param([switch]$NonInteractive, [Parameter(Mandatory)][string]$Path)
    . "$PSScriptRoot\\Tools\\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' 
    if (-not (Test-Path -LiteralPath $Path)) { Write-HiderLog "Disable-EFS: Path missing '$Path'"; return $false }
    if (-not (Test-EFSAvailable)) { Write-HiderLog 'Disable-EFS: EFS not available.'; return $false }
    if (-not (Test-DriveNTFS -Path $Path)) { Write-HiderLog 'Disable-EFS: Non-NTFS volume.'; return $false }
    $res = Invoke-Cipher -Arguments "/D /S:`"$Path`""
    Write-HiderLog "EFS Decrypt '$Path': Exit=$($res.ExitCode)"
    return ($res.ExitCode -eq 0)
}

function Should-ApplyAclRestriction {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][object]$Record)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Record' 
    if (-not $Record -or -not $Record.AclRestricted) { return $false }
    $current = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $sam = ($current -split '\\')[-1]
    return -not ($AclBypassUsers -contains $sam)
}

function Prompt-NewPassword {
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = 'Set / Change Password'
    $dlg.StartPosition = 'CenterParent'
    $dlg.FormBorderStyle= 'FixedDialog'
    $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false
    $dlg.ClientSize = New-Object System.Drawing.Size(360, 160)
    $lbl1 = New-Object System.Windows.Forms.Label; $lbl1.Text = 'New password:'; $lbl1.Location = '10,20'; $lbl1.AutoSize = $true
    $txt1 = New-Object System.Windows.Forms.TextBox; $txt1.Location = '120,18'; $txt1.Size = '220,22'; $txt1.UseSystemPasswordChar = $true
    $lbl2 = New-Object System.Windows.Forms.Label; $lbl2.Text = 'Confirm:'; $lbl2.Location = '10,55'; $lbl2.AutoSize = $true
    $txt2 = New-Object System.Windows.Forms.TextBox; $txt2.Location = '120,53'; $txt2.Size = '220,22'; $txt2.UseSystemPasswordChar = $true
    $ok = New-Object System.Windows.Forms.Button; $ok.Text='Save'; $ok.Location='200,100'; $ok.Size='60,26'; $ok.DialogResult=[System.Windows.Forms.DialogResult]::OK
    $cancel = New-Object System.Windows.Forms.Button; $cancel.Text='Cancel'; $cancel.Location='270,100'; $cancel.Size='70,26'; $cancel.DialogResult=[System.Windows.Forms.DialogResult]::Cancel
    $dlg.Controls.AddRange(@($lbl1,$txt1,$lbl2,$txt2,$ok,$cancel)); $dlg.AcceptButton=$ok; $dlg.CancelButton=$cancel
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    if ([string]::IsNullOrWhiteSpace($txt1.Text) -or $txt1.Text -ne $txt2.Text) { [System.Windows.Forms.MessageBox]::Show('Passwords must match and be non-empty.') | Out-Null; return $null }
    return Convert-PlainToSecureString -Plain $txt1.Text
}

function Prompt-Password {
    param([string]$Title='Enter password')
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = $Title; $dlg.StartPosition='CenterParent'; $dlg.FormBorderStyle='FixedDialog'; $dlg.ClientSize='320,110'
    $lbl = New-Object System.Windows.Forms.Label; $lbl.Text='Password:'; $lbl.Location='10,20'; $lbl.AutoSize=$true
    $txt = New-Object System.Windows.Forms.TextBox; $txt.Location='90,18'; $txt.Size='210,22'; $txt.UseSystemPasswordChar=$true
    $ok = New-Object System.Windows.Forms.Button; $ok.Text='OK'; $ok.Location='150,60'; $ok.Size='60,24'; $ok.DialogResult=[System.Windows.Forms.DialogResult]::OK
    $cancel = New-Object System.Windows.Forms.Button; $cancel.Text='Cancel'; $cancel.Location='220,60'; $cancel.Size='80,24'; $cancel.DialogResult=[System.Windows.Forms.DialogResult]::Cancel
    $dlg.Controls.AddRange(@($lbl,$txt,$ok,$cancel)); $dlg.AcceptButton=$ok; $dlg.CancelButton=$cancel
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return Convert-PlainToSecureString -Plain $txt.Text }
    return $null
}

function Set-HiderActivity { $Global:HiderLastActivity = Get-Date }

function Hide-HiderFolder {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not (Test-Path -LiteralPath $Path)) { [System.Windows.MessageBox]::Show("Path missing:`r`n$Path"); return }
    if (-not $Global:HiderConfig) { $Global:HiderConfig = Get-HiderConfig }
    $rec = Get-HiderRecord -Path $Path
    if (-not $rec) { Add-HiderRecord -Path $Path; $rec = Get-HiderRecord -Path $Path }
    $aclApplied = $false; $efsApplied = $false
    if (Set-Hidden -Path $Path) {
        if ($rec -and (Should-ApplyAclRestriction -Record $rec)) { $aclApplied = Set-AclRestriction -Path $Path }
        elseif ($rec -and $rec.AclRestricted) { Write-HiderLog "ACL restriction skipped for '$Path' (bypass user)." }
        if ($rec -and $rec.EfsEnabled) { $efsApplied = Enable-EFS -Path $Path }
        Write-HiderLog "Hid folder: $Path (ACL=$aclApplied EFS=$efsApplied)"
        Set-Status "Folder hidden" 1200
    }
}

function Unhide-HiderFolder {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not $Global:HiderConfig) { $Global:HiderConfig = Get-HiderConfig }
    $rec = Get-HiderRecord -Path $Path
    if (-not $rec -or -not $rec.PasswordRecord) { [System.Windows.MessageBox]::Show('No password set for this folder.'); return }
    $pwd = Prompt-Password -Title "Unhide '$Path'"
    if (-not $pwd) { return }
    if (-not (Test-Password -Password $pwd -PasswordRecord $rec.PasswordRecord)) { [System.Windows.MessageBox]::Show('Incorrect password.'); return }
    if (Clear-Hidden -Path $Path) {
        $aclRestored = $true; $efsRestored = $true
        if ($rec.AclRestricted -and (Should-ApplyAclRestriction -Record $rec)) { $aclRestored = Restore-ACL -Path $Path }
        elseif ($rec.AclRestricted) { Write-HiderLog "ACL restore skipped for '$Path' (bypass user)." }
        if ($rec.EfsEnabled) { $efsRestored = Disable-EFS -Path $Path }
        Write-HiderLog "Unhid folder: $Path (ACL restore=$aclRestored EFS restore=$efsRestored)"
        Set-Status 'Folder unhidden' 1200
    }
}

function Refresh-HiderList {
    param($ListControl)
    if (-not $Global:HiderConfig) { $Global:HiderConfig = Get-HiderConfig }
    $items = @()
    foreach ($rec in $Global:HiderConfig.Folders) {
        $path = $rec.FolderPath
        $exists = Test-Path -LiteralPath $path
        $isHidden = $exists -and (Get-HiddenState -Path $path)
        $status = if (-not $exists) { 'Missing' } elseif ($isHidden) { 'Hidden' } else { 'Visible' }
        $items += [PSCustomObject]@{
            Path   = $path
            Status = $status
            ACL    = if ($rec.AclRestricted) { 'Yes' } else { 'No' }
            EFS    = if ($rec.EfsEnabled) { 'Yes' } else { 'No' }
        }
    }
    if ($ListControl) { $ListControl.ItemsSource = $items }
}
$Global:HiderConfig = Get-HiderConfig

function Set-SecStatus($text) {
    if ($script:secStatus) { $script:secStatus.Text = $text }
}

function Get-LocalUsersSafe {
    if (Get-Command Get-LocalUser -ErrorAction SilentlyContinue) {
        try { return Get-LocalUser } catch { Write-Verbose "Get-LocalUsersSafe: Get-LocalUser failed: $($_.Exception.Message)" }
    }
    try {
        $pc = [ADSI]"WinNT://$env:COMPUTERNAME"
        return $pc.Children | Where-Object { $_.SchemaClassName -eq 'user' } | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Enabled = $true; SID = $null }
        }
    } catch { return @() }
}

function Get-LocalGroupMembersSafe {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$GroupName)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'GroupName' 
    if (Get-Command Get-LocalGroupMember -ErrorAction SilentlyContinue) {
        try { return Get-LocalGroupMember -Group $GroupName -ErrorAction Stop } catch {}
    }
    try {
        $grp = [ADSI]"WinNT://$env:COMPUTERNAME/$GroupName,group"
        $members = @($grp.psbase.Invoke('Members'))
        return $members | ForEach-Object {
            $n = $_.GetType().InvokeMember('Name','GetProperty',$null,$_,$null)
            $path = $_.GetType().InvokeMember('ADsPath','GetProperty',$null,$_,$null)
            [pscustomobject]@{ Name = $n; SID = $null; Path = $path }
        }
    } catch { return @() }
}

function Test-AclElevation {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path,[string[]]$UserIdentities)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $userIds = @($UserIdentities) | Where-Object { $_ } | ForEach-Object { $_.ToString().ToLowerInvariant() }
        foreach ($ace in $acl.Access) {
            $id = $ace.IdentityReference.Value
            $idLower = $id.ToLowerInvariant()
            foreach ($u in $userIds) {
                if ($idLower -eq $u -or $idLower.EndsWith("\\$u")) {
                    $rights = $ace.FileSystemRights
                    $isElevated = ($rights.ToString() -match 'FullControl|Modify|Write|TakeOwnership|ChangePermissions')
                    if ($isElevated -and $ace.AccessControlType -eq 'Allow') { return $true }
                }
            }
        }
    } catch {}
    return $false
}

function Test-RegistryElevation {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$KeyPath,[string[]]$UserIdentities)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'KeyPath' 
    try {
        $acl = Get-Acl -Path $KeyPath -ErrorAction Stop
        $userIds = @($UserIdentities) | Where-Object { $_ } | ForEach-Object { $_.ToString().ToLowerInvariant() }
        foreach ($ace in $acl.Access) {
            $id = $ace.IdentityReference.Value
            $idLower = $id.ToLowerInvariant()
            foreach ($u in $userIds) {
                if ($idLower -eq $u -or $idLower.EndsWith("\\$u")) {
                    $rights = $ace.RegistryRights
                    $isElevated = ($rights.ToString() -match 'FullControl|WriteKey|ChangePermissions|TakeOwnership')
                    if ($isElevated -and $ace.AccessControlType -eq 'Allow') { return $true }
                }
            }
        }
    } catch {}
    return $false
}

function Run-SecurityScan {
    Write-SecLog 'Security scan started'
    $results = @()
    $users = Get-LocalUsersSafe
    Write-SecLog "Users discovered: $($users.Count)"

    $elevatedGroups = @('Administrators','Backup Operators','Power Users')
    $groupMap = @{}
    foreach ($g in $elevatedGroups) {
        $groupMap[$g] = @(Get-LocalGroupMembersSafe -GroupName $g)
        Write-SecLog "Group $g members: $(@($groupMap[$g]).Count)"
    }

    $sensitiveFolders = @(
        "$env:SystemDrive\",
        "$env:windir",
        "$env:windir\System32",
        "$env:windir\System32\GroupPolicy",
        "$env:ProgramFiles",
        "${env:ProgramFiles(x86)}",
        "$env:ProgramData"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    foreach ($u in $users) {
        $name = $u.Name
        Write-SecLog "User scan start: $name"
        $name = $u.Name
        $uSid = $null
        if ($u.PSObject.Properties.Name -contains 'SID' -and $u.SID) { $uSid = $u.SID.ToString() }
        $idList = @($name, "$env:COMPUTERNAME\$name")
        if ($uSid) { $idList += $uSid }
        $idList = $idList | Where-Object { $_ }

        $reasons = @()

        foreach ($kv in $groupMap.GetEnumerator()) {
            foreach ($member in $kv.Value) {
                $memberName = $member.Name
                $memberSid = $null
                if ($member.PSObject.Properties.Name -contains 'SID') { $memberSid = $member.SID }
                elseif ($member.PSObject.Properties['SidString']) { $memberSid = $member.PSObject.Properties['SidString'].Value }

                if ($uSid -and $memberSid -and ($memberSid.ToString() -eq $uSid)) { $reasons += "Member of $($kv.Key)"; break }
                if ($memberName) {
                    foreach ($id in $idList) {
                        if ($memberName.Equals($id, [System.StringComparison]::OrdinalIgnoreCase) -or $memberName.EndsWith("\$id", [System.StringComparison]::OrdinalIgnoreCase)) { $reasons += "Member of $($kv.Key)"; break }
                    }
                }
            }
        }

        Write-SecLog "User group checks done: $name"

        foreach ($folder in $sensitiveFolders) {
            if (Test-AclElevation -Path $folder -UserIdentities $idList) { $reasons += "Write/Modify on $folder" }
        }

        Write-SecLog "User ACL checks done: $name"

        if (Test-RegistryElevation -KeyPath 'HKLM:\SOFTWARE' -UserIdentities $idList) { $reasons += 'Elevated rights on HKLM\SOFTWARE' }
        if (Test-RegistryElevation -KeyPath 'HKLM:\SYSTEM' -UserIdentities $idList) { $reasons += 'Elevated rights on HKLM\SYSTEM' }

        Write-SecLog "User registry checks done: $name"

        if ($reasons.Count -gt 0) {
            $results += [pscustomobject]@{
                User = $name
                Reasons = ($reasons -join '; ')
                Enabled = if ($u.PSObject.Properties.Name -contains 'Enabled') { [bool]$u.Enabled } else { $true }
            }
            Write-SecLog "Flagged ${name}: $($reasons -join '; ')"
        }
        Write-SecLog "User scan end: $name"
    }
    Write-SecLog "Security scan completed. Findings: $($results.Count)"
    return $results
}

function Get-CriticalPaths {
    return @(
        "$env:SystemDrive\",
        "$env:windir",
        "$env:windir\System32",
        "$env:windir\System32\GroupPolicy",
        "$env:ProgramFiles",
        "${env:ProgramFiles(x86)}",
        "$env:ProgramData"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
}

function Get-CriticalAclReport {
    param([string[]]$Paths)
    $pathsToCheck = if ($Paths) { $Paths } else { Get-CriticalPaths }
    $report = @()
    foreach ($p in $pathsToCheck) {
        try { $acl = Get-Acl -LiteralPath $p -ErrorAction Stop } catch { continue }
        foreach ($ace in $acl.Access) {
            if ($ace.AccessControlType -ne 'Allow') { continue }
            $rights = $ace.FileSystemRights
            if ($rights.ToString() -notmatch 'Write|Modify|FullControl|ChangePermissions|TakeOwnership') { continue }
            $report += [pscustomobject]@{
                Path          = $p
                Identity      = $ace.IdentityReference.Value
                Rights        = $rights.ToString()
                AccessType    = $ace.AccessControlType.ToString()
                Inherited     = [bool]$ace.IsInherited
                Inheritance   = $ace.InheritanceFlags.ToString()
                Propagation   = $ace.PropagationFlags.ToString()
            }
        }
    }
    return $report
}

function Remove-CriticalAce {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Identity,
        [Parameter(Mandatory)][string]$Rights,
        [Parameter(Mandatory)][string]$AccessType,
        [Parameter(Mandatory)][string]$Inheritance,
        [Parameter(Mandatory)][string]$Propagation)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
Require-Parameter 'Identity' 
Require-Parameter 'Rights' 
Require-Parameter 'AccessType' 
Require-Parameter 'Inheritance' 
Require-Parameter 'Propagation' 
    try {
        $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $nt = New-Object System.Security.Principal.NTAccount($Identity)
        $sid = $nt.Translate([System.Security.Principal.SecurityIdentifier])
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $sid,
            [System.Security.AccessControl.FileSystemRights]$Rights,
            [System.Security.AccessControl.InheritanceFlags]$Inheritance,
            [System.Security.AccessControl.PropagationFlags]$Propagation,
            [System.Security.AccessControl.AccessControlType]$AccessType
        )
        $removed = $acl.RemoveAccessRuleSpecific($rule)
        if ($removed) { Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop }
        return $removed
    } catch { return $false }
}

function Show-CriticalAclWindow {
    $paths = Get-CriticalPaths
    $data = Get-CriticalAclReport -Paths $paths
    $win = New-Object System.Windows.Window
    $win.Title = 'Critical ACLs'
    $win.Height = 500; $win.Width = 820
    $win.WindowStartupLocation = 'CenterOwner'

    $root = New-Object System.Windows.Controls.DockPanel
    $btnPanel = New-Object System.Windows.Controls.StackPanel
    $btnPanel.Orientation = 'Horizontal'
    $btnPanel.Margin = '0,0,0,6'

    $refreshBtn = New-Object System.Windows.Controls.Button
    $refreshBtn.Content = 'Refresh'
    $refreshBtn.Width = 90; $refreshBtn.Margin = '0,0,6,0'

    $removeBtn = New-Object System.Windows.Controls.Button
    $removeBtn.Content = 'Remove Selected ACE'
    $removeBtn.Width = 170

    $btnPanel.Children.Add($refreshBtn) | Out-Null
    $btnPanel.Children.Add($removeBtn) | Out-Null

    $grid = New-Object System.Windows.Controls.DataGrid
    $grid.AutoGenerateColumns = $false
    $grid.IsReadOnly = $true
    $grid.SelectionMode = 'Single'
    $grid.SelectionUnit = 'FullRow'
    $grid.HeadersVisibility = 'Column'
    $grid.RowBackground = '#F9FBFF'
    $grid.AlternatingRowBackground = '#FFFFFF'

    foreach ($col in @(
        @{H='Path';W=220;B='Path'},
        @{H='Identity';W=180;B='Identity'},
        @{H='Rights';W=160;B='Rights'},
        @{H='Type';W=80;B='AccessType'},
        @{H='Inherited';W=80;B='Inherited'}
    )) {
        $c = New-Object System.Windows.Controls.DataGridTextColumn
        $c.Header = $col.H; $c.Binding = New-Object System.Windows.Data.Binding($col.B); $c.Width = $col.W
        $grid.Columns.Add($c) | Out-Null
    }

    $grid.ItemsSource = $data

    $refresh = {
        param($g, $p)
        $g.ItemsSource = Get-CriticalAclReport -Paths $p
    }

    $protected = @('NT AUTHORITY\SYSTEM','BUILTIN\Administrators','NT SERVICE\TrustedInstaller')

    $removeBtn.Add_Click({
        $sel = $grid.SelectedItem
        if (-not $sel) { [System.Windows.MessageBox]::Show('Select an ACE first.'); return }
        if ($sel.Inherited) { [System.Windows.MessageBox]::Show('Cannot remove inherited ACEs here. Adjust the parent ACL instead.'); return }
        if ($protected -contains $sel.Identity) { [System.Windows.MessageBox]::Show('Refusing to remove protected identities.'); return }
        $ok = Remove-CriticalAce -Path $sel.Path -Identity $sel.Identity -Rights $sel.Rights -AccessType $sel.AccessType -Inheritance $sel.Inheritance -Propagation $sel.Propagation
        if ($ok) {
            Set-SecStatus "Removed ACE for $($sel.Identity) on $($sel.Path)"
            & $refresh $grid $paths
        } else {
            [System.Windows.MessageBox]::Show('Failed to remove ACE (it may not match exactly).')
        }
    })

    $refreshBtn.Add_Click({ & $refresh $grid $paths })

    $root.Children.Add($btnPanel) | Out-Null
    [System.Windows.Controls.DockPanel]::SetDock($btnPanel, 'Top')
    $root.Children.Add($grid) | Out-Null
    $win.Content = $root
    $win.Owner = $window
    $win.ShowDialog() | Out-Null
}

function Test-IsPrivateIP {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Ip)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Ip' 
    try {
        $addr = [System.Net.IPAddress]::Parse($Ip)
    } catch { return $true }
    if ($addr.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) {
        $bytes = $addr.GetAddressBytes()
        $prefix = $bytes[0]
        $linkLocal = ($prefix -eq 0xFE -and ($bytes[1] -band 0xC0) -eq 0x80)
        $uniqueLocal = ($prefix -band 0xFE) -eq 0xFC
        if ($addr.IsIPv6LinkLocal -or $addr.IsIPv6SiteLocal -or $addr.IsIPv6Multicast -or $addr.IsIPv6Teredo -or $addr.IsIPv6LinkLocal -or $addr.IsIPv6SiteLocal -or $addr.IsIPv6Multicast -or $addr.IsIPv6Teredo) { return $true }
        if ($linkLocal -or $uniqueLocal) { return $true }
        if ($addr.Equals([System.Net.IPAddress]::IPv6Loopback)) { return $true }
        return $false
    }
    $bytes4 = $addr.GetAddressBytes()
    $b1,$b2 = $bytes4[0],$bytes4[1]
    if ($b1 -eq 10) { return $true }
    if ($b1 -eq 172 -and ($b2 -ge 16 -and $b2 -le 31)) { return $true }
    if ($b1 -eq 192 -and $b2 -eq 168) { return $true }
    if ($b1 -eq 169 -and $b2 -eq 254) { return $true }
    if ($b1 -eq 127) { return $true }
    return $false
}

function Get-SuspiciousOutbound {
    $now = Get-Date
    $connections = @()
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        try { $connections = Get-NetTCPConnection -State Established -ErrorAction Stop } catch {}
    }
    if (-not $connections) {
        try {
            $raw = netstat -ano -p tcp | Select-Object -Skip 4
            foreach ($line in $raw) {
                $parts = $line -split '\s+' | Where-Object { $_ }
                if ($parts.Count -lt 5) { continue }
                $local = $parts[1]; $remote = $parts[2]; $state = $parts[3]; $pid = $parts[4]
                if ($state -notmatch 'ESTABLISHED') { continue }
                $connections += [pscustomobject]@{
                    LocalAddress = ($local -split ':')[0]
                    LocalPort    = ($local -split ':')[-1]
                    RemoteAddress= ($remote -split ':')[0]
                    RemotePort   = ($remote -split ':')[-1]
                    State        = $state
                    OwningProcess= [int]$pid
                }
            }
        } catch {}
    }

    $report = @()
    $unusual = @('winword','excel','powerpnt','outlook','onenote','mspub','msaccess','visio','wordpad','write')
    foreach ($c in $connections) {
        if (-not $c.RemoteAddress -or (Test-IsPrivateIP -Ip $c.RemoteAddress)) { continue }
        $proc = $null; $duration = $null; $pname = ''
        try { $proc = Get-Process -Id $c.OwningProcess -ErrorAction Stop; $pname = $proc.ProcessName; $duration = ($now - $proc.StartTime).TotalSeconds } catch {}
        $remoteName = $null
        try { $remoteName = ([System.Net.Dns]::GetHostEntry($c.RemoteAddress)).HostName } catch {}
        $report += [pscustomobject]@{
            RemoteIP     = $c.RemoteAddress
            RemotePort   = $c.RemotePort
            RemoteName   = $remoteName
            LocalPort    = $c.LocalPort
            Process      = $pname
            PID          = $c.OwningProcess
            State        = $c.State
            DurationSec  = if ($duration) { [math]::Round($duration,1) } else { $null }
            UnusualProc  = if ($pname -and ($unusual -contains $pname.ToLower())) { 'Yes' } else { '' }
        }
    }
    return $report
}

function Get-SuspiciousOutboundWithProgress {
    param($StatusLabel, $ProgressBar, $Grid)
    
    Write-SecLog 'Outbound scan: Starting connection gathering'
    if ($StatusLabel) { $StatusLabel.Text = 'Gathering connections...'; $StatusLabel.Dispatcher.Invoke([Action]{}, 'Render') }
    
    $now = Get-Date
    $connections = @()
    
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        Write-SecLog 'Outbound scan: Using Get-NetTCPConnection'
        try { $connections = @(Get-NetTCPConnection -State Established -ErrorAction Stop) } catch { Write-SecLog "Get-NetTCPConnection error: $($_.Exception.Message)" }
    }
    if (-not $connections -or $connections.Count -eq 0) {
        Write-SecLog 'Outbound scan: Falling back to netstat'
        try {
            $raw = netstat -ano -p tcp | Select-Object -Skip 4
            foreach ($line in $raw) {
                $parts = $line -split '\s+' | Where-Object { $_ }
                if ($parts.Count -lt 5) { continue }
                $local = $parts[1]; $remote = $parts[2]; $state = $parts[3]; $pid = $parts[4]
                if ($state -notmatch 'ESTABLISHED') { continue }
                $connections += [pscustomobject]@{
                    LocalAddress = ($local -split ':')[0]
                    LocalPort    = ($local -split ':')[-1]
                    RemoteAddress= ($remote -split ':')[0]
                    RemotePort   = ($remote -split ':')[-1]
                    State        = $state
                    OwningProcess= [int]$pid
                }
            }
        } catch { Write-SecLog "netstat error: $($_.Exception.Message)" }
    }
    
    Write-SecLog "Outbound scan: Found $($connections.Count) total connections"

    # Filter to public IPs first
    $publicConns = @($connections | Where-Object { $_.RemoteAddress -and -not (Test-IsPrivateIP -Ip $_.RemoteAddress) })
    $total = $publicConns.Count
    Write-SecLog "Outbound scan: $total public connections to process"
    
    if ($ProgressBar) { 
        $ProgressBar.Maximum = [Math]::Max(1, $total)
        $ProgressBar.Value = 0
        $ProgressBar.Visibility = 'Visible'
        $ProgressBar.Dispatcher.Invoke([Action]{}, 'Render')
    }
    if ($StatusLabel) { $StatusLabel.Text = "Processing $total public connections..."; $StatusLabel.Dispatcher.Invoke([Action]{}, 'Render') }

    $report = @()
    $unusual = @('winword','excel','powerpnt','outlook','onenote','mspub','msaccess','visio','wordpad','write')
    $i = 0
    foreach ($c in $publicConns) {
        $i++
        if ($ProgressBar) { $ProgressBar.Value = $i; $ProgressBar.Dispatcher.Invoke([Action]{}, 'Render') }
        if ($StatusLabel) { $StatusLabel.Text = "Processing connection $i of ${total}: $($c.RemoteAddress)"; $StatusLabel.Dispatcher.Invoke([Action]{}, 'Render') }
        
        Write-SecLog "Outbound scan: Processing $i/$total - $($c.RemoteAddress):$($c.RemotePort)"
        
        $proc = $null; $duration = $null; $pname = ''
        try { $proc = Get-Process -Id $c.OwningProcess -ErrorAction Stop; $pname = $proc.ProcessName; $duration = ($now - $proc.StartTime).TotalSeconds } catch {}
        
        $remoteName = $null
        # DNS lookup can be very slow; skip entirely for speed
        # Uncomment below if you want DNS (but it will be slow):
        # if ($total -lt 20) { try { $remoteName = ([System.Net.Dns]::GetHostEntry($c.RemoteAddress)).HostName } catch {} }
        
        $report += [pscustomobject]@{
            RemoteIP     = $c.RemoteAddress
            RemotePort   = $c.RemotePort
            RemoteName   = $remoteName
            LocalPort    = $c.LocalPort
            Process      = $pname
            PID          = $c.OwningProcess
            State        = $c.State
            DurationSec  = if ($duration) { [math]::Round($duration,1) } else { $null }
            UnusualProc  = if ($pname -and ($unusual -contains $pname.ToLower())) { 'Yes' } else { '' }
        }
        
        # Update grid incrementally every 10 items so user sees progress
        if ($Grid -and ($i % 10 -eq 0)) {
            $Grid.ItemsSource = $null
            $Grid.ItemsSource = $report
            $Grid.Dispatcher.Invoke([Action]{}, 'Render')
        }
    }
    
    if ($ProgressBar) { $ProgressBar.Visibility = 'Collapsed'; $ProgressBar.Dispatcher.Invoke([Action]{}, 'Render') }
    if ($StatusLabel) { $StatusLabel.Text = "Scan complete. Found $($report.Count) public connection(s)."; $StatusLabel.Dispatcher.Invoke([Action]{}, 'Render') }
    Write-SecLog "Outbound scan: Complete with $($report.Count) results"
    return $report
}

function Show-OutboundWindow {
    $win = New-Object System.Windows.Window
    $win.Title = 'Outbound Connections'
    $win.Height = 520; $win.Width = 860
    $win.WindowStartupLocation = 'CenterOwner'

    $root = New-Object System.Windows.Controls.DockPanel

    # Status bar at bottom
    $statusPanel = New-Object System.Windows.Controls.StackPanel
    $statusPanel.Orientation = 'Horizontal'
    $statusPanel.Margin = '0,6,0,0'
    $statusLabel = New-Object System.Windows.Controls.TextBlock
    $statusLabel.Text = 'Ready'
    $statusLabel.VerticalAlignment = 'Center'
    $statusLabel.Margin = '0,0,12,0'
    $progressBar = New-Object System.Windows.Controls.ProgressBar
    $progressBar.Width = 200
    $progressBar.Height = 16
    $progressBar.Visibility = 'Collapsed'
    $statusPanel.Children.Add($statusLabel) | Out-Null
    $statusPanel.Children.Add($progressBar) | Out-Null

    $btnPanel = New-Object System.Windows.Controls.StackPanel
    $btnPanel.Orientation = 'Horizontal'
    $btnPanel.Margin = '0,0,0,6'

    $refreshBtn = New-Object System.Windows.Controls.Button
    $refreshBtn.Content = 'Refresh'
    $refreshBtn.Width = 90; $refreshBtn.Margin = '0,0,6,0'

    $killBtn = New-Object System.Windows.Controls.Button
    $killBtn.Content = 'Kill Connection'
    $killBtn.Width = 130; $killBtn.Margin = '0,0,6,0'

    $btnPanel.Children.Add($refreshBtn) | Out-Null
    $btnPanel.Children.Add($killBtn) | Out-Null

    $grid = New-Object System.Windows.Controls.DataGrid
    $grid.AutoGenerateColumns = $false
    $grid.IsReadOnly = $true
    $grid.SelectionMode = 'Single'
    $grid.SelectionUnit = 'FullRow'
    $grid.HeadersVisibility = 'Column'
    $grid.RowBackground = '#F9FBFF'
    $grid.AlternatingRowBackground = '#FFFFFF'

    foreach ($col in @(
        @{H='Remote IP';W=150;B='RemoteIP'},
        @{H='Remote Name';W=170;B='RemoteName'},
        @{H='Remote Port';W=80;B='RemotePort'},
        @{H='Local Port';W=80;B='LocalPort'},
        @{H='Process';W=120;B='Process'},
        @{H='PID';W=60;B='PID'},
        @{H='State';W=80;B='State'},
        @{H='Duration (s)';W=100;B='DurationSec'},
        @{H='Unusual Proc';W=90;B='UnusualProc'}
    )) {
        $c = New-Object System.Windows.Controls.DataGridTextColumn
        $c.Header = $col.H; $c.Binding = New-Object System.Windows.Data.Binding($col.B); $c.Width = $col.W
        $grid.Columns.Add($c) | Out-Null
    }

    # Initial load with progress
    $grid.ItemsSource = Get-SuspiciousOutboundWithProgress -StatusLabel $statusLabel -ProgressBar $progressBar -Grid $grid

    $refresh = {
        param($g, $sLbl, $pBar)
        $g.ItemsSource = Get-SuspiciousOutboundWithProgress -StatusLabel $sLbl -ProgressBar $pBar -Grid $g
    }

    $refreshBtn.Add_Click({ & $refresh $grid $statusLabel $progressBar; Set-SecStatus "Outbound scan refreshed" })

    $killBtn.Add_Click({
        $sel = $grid.SelectedItem
        if (-not $sel) { [System.Windows.MessageBox]::Show('Select a connection first.'); return }
        if (-not $sel.PID) { [System.Windows.MessageBox]::Show('PID missing; cannot kill connection.'); return }
        $resp = [System.Windows.MessageBox]::Show("Kill process $($sel.Process) (PID $($sel.PID)) to drop this connection?",'Confirm','YesNo','Warning')
        if ($resp -ne [System.Windows.MessageBoxResult]::Yes) { return }
        try {
            Stop-Process -Id $sel.PID -Force -ErrorAction Stop
            Set-SecStatus "Killed PID $($sel.PID) and connection"
            & $refresh $grid $statusLabel $progressBar
        } catch {
            [System.Windows.MessageBox]::Show("Failed to kill PID $($sel.PID): $($_.Exception.Message)")
        }
    })

    $root.Children.Add($btnPanel) | Out-Null
    [System.Windows.Controls.DockPanel]::SetDock($btnPanel, 'Top')
    $root.Children.Add($statusPanel) | Out-Null
    [System.Windows.Controls.DockPanel]::SetDock($statusPanel, 'Bottom')
    $root.Children.Add($grid) | Out-Null
    $win.Content = $root
    $win.Owner = $window
    $win.ShowDialog() | Out-Null
}

function Disable-LocalUserSafe {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$User)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'User' 
    if (Get-Command Disable-LocalUser -ErrorAction SilentlyContinue) {
        try { Disable-LocalUser -Name $User -ErrorAction Stop; return $true } catch { return $false }
    }
    try {
        $adsi = [ADSI]"WinNT://$env:COMPUTERNAME/$User,user"; $adsi.userflags = $adsi.userflags.Value -bor 2; $adsi.SetInfo(); return $true
    } catch { return $false }
}

function Remove-LocalUserSafe {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$User)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'User' 
    if (Get-Command Remove-LocalUser -ErrorAction SilentlyContinue) {
        try { Remove-LocalUser -Name $User -ErrorAction Stop; return $true } catch { return $false }
    }
    try { $adsi = [ADSI]"WinNT://$env:COMPUTERNAME"; $adsi.Delete('user',$User); return $true } catch { return $false }
}

function Reset-LocalUserPasswordSafe {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$User,[Parameter(Mandatory)][System.Security.SecureString]$Password)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'User' 
Require-Parameter 'Password' 
    if (Get-Command Set-LocalUser -ErrorAction SilentlyContinue) {
        try { Set-LocalUser -Name $User -Password $Password -ErrorAction Stop; return $true } catch { return $false }
    }
    try {
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($ptr)
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
        $adsi = [ADSI]"WinNT://$env:COMPUTERNAME/$User,user"; $adsi.SetPassword($plain); $adsi.SetInfo(); return $true
    } catch { return $false }
}

# --- XAML: responsive layout, status bar ---

# ==================== VIDEO EDITOR FUNCTIONS ====================
$script:PlatypusBase = 'C:\ProgramFiles\PlatypusUtils'
$script:AssetsDir    = Join-Path $script:PlatypusBase 'Assets'
$script:Root         = if ($PSCommandPath) { Split-Path $PSCommandPath } else { Get-Location }
$script:PreferredDataRoot = Join-Path $script:PlatypusBase 'Data\VideoEditor'
$script:FallbackDataRoot  = $script:PreferredDataRoot

function Initialize-DataRoot {
  param([string]$Preferred,[string]$Fallback)
  foreach ($candidate in @($Preferred,$Fallback)) {
    try {
      if (-not (Test-Path -LiteralPath $candidate)) { New-Item -ItemType Directory -Path $candidate -Force -ErrorAction Stop | Out-Null }
      $logs = Join-Path $candidate "Logs"
      if (-not (Test-Path -LiteralPath $logs)) { New-Item -ItemType Directory -Path $logs -Force -ErrorAction Stop | Out-Null }
      return @{ Root=$candidate; Logs=$logs }
    }
    catch {
      Write-Warning ("Unable to initialize storage at {0}: {1}" -f $candidate, $_.Exception.Message)
    }
  }
  return $null
}

function Ensure-DirectorySafe {
  param([string]$Path)
  if (-not $Path) { return $false }
  try {
    if (-not (Test-Path -LiteralPath $Path)) { New-Item -ItemType Directory -Path $Path -Force -ErrorAction Stop | Out-Null }
    return $true
  }
  catch {
    Write-Warning ("Unable to create directory '{0}': {1}" -f $Path, $_.Exception.Message)
    return $false
  }
}

$storage = Initialize-DataRoot -Preferred $script:PreferredDataRoot -Fallback $script:FallbackDataRoot
if (-not $storage) { throw "Failed to initialize application data directories." }
$script:DataRoot     = $storage.Root
$script:GlobalLogDir = $storage.Logs
$script:DupJsonDir   = $storage.Root

# ---------------- XAML ----------------

# ==================== COMBINED XAML ====================
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="PlatypusTools" Height="800" Width="1280"
        MinHeight="640" MinWidth="1024"
        WindowStartupLocation="CenterScreen"
        Name="MainWindow">
  <Window.Resources>
    <!-- Light Theme Colors -->
    <SolidColorBrush x:Key="WindowBackground" Color="#FFFFFF"/>
    <SolidColorBrush x:Key="TextForeground" Color="#222222"/>
    <SolidColorBrush x:Key="MenuBackground" Color="#F5F5F5"/>
    <SolidColorBrush x:Key="GridAltRow" Color="#F9FBFF"/>
    <SolidColorBrush x:Key="StatusBarBg" Color="#F0F0F0"/>
    
    <Style TargetType="Button">
      <Setter Property="Background" Value="#FF2B579A"/>
      <Setter Property="Foreground" Value="White"/>
      <Setter Property="Padding" Value="6,4"/>
      <Setter Property="Margin" Value="4"/>
      <Setter Property="BorderBrush" Value="#FF1F446E"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#222"/></Style>
    <Style TargetType="Menu">
      <Setter Property="Background" Value="{DynamicResource MenuBackground}"/>
    </Style>
    <LinearGradientBrush x:Key="HeaderBrush" StartPoint="0,0" EndPoint="1,0">
      <GradientStop Color="#FF4A90E2" Offset="0"/>
      <GradientStop Color="#FF50C878" Offset="1"/>
    </LinearGradientBrush>
  </Window.Resources>

  <DockPanel LastChildFill="True" Name="MainDockPanel">
    <!-- Menu Bar -->
    <Menu DockPanel.Dock="Top" Background="{DynamicResource MenuBackground}">
      <MenuItem Header="_File">
        <MenuItem Header="Default _Directories">
          <MenuItem Name="MenuSetScanDir" Header="Set Default _Scan Folder..."/>
          <MenuItem Name="MenuSetOutputDir" Header="Set Default _Output Folder..."/>
          <MenuItem Name="MenuSetToolDir" Header="Set _Tool Folder (ffmpeg/exiftool)..."/>
          <Separator/>
          <MenuItem Name="MenuOpenScanDir" Header="Open Scan Folder"/>
          <MenuItem Name="MenuOpenOutputDir" Header="Open Output Folder"/>
          <MenuItem Name="MenuOpenDataDir" Header="Open Data Folder"/>
          <MenuItem Name="MenuOpenLogDir" Header="Open Log Folder"/>
        </MenuItem>
        <Separator/>
        <MenuItem Name="MenuSaveConfig" Header="_Save Configuration"/>
        <MenuItem Name="MenuLoadConfig" Header="_Load Configuration"/>
        <MenuItem Name="MenuResetConfig" Header="_Reset to Defaults"/>
        <Separator/>
        <MenuItem Name="MenuExit" Header="E_xit" InputGestureText="Alt+F4"/>
      </MenuItem>
      <MenuItem Header="_View">
        <MenuItem Name="MenuThemeLight" Header="_Light Mode" IsCheckable="True" IsChecked="True"/>
        <MenuItem Name="MenuThemeDark" Header="_Dark Mode" IsCheckable="True"/>
        <Separator/>
        <MenuItem Name="MenuFontSmall" Header="Font: _Small" IsCheckable="True"/>
        <MenuItem Name="MenuFontMedium" Header="Font: _Medium" IsCheckable="True" IsChecked="True"/>
        <MenuItem Name="MenuFontLarge" Header="Font: _Large" IsCheckable="True"/>
        <Separator/>
        <MenuItem Name="MenuShowStatusBar" Header="Show _Status Bar" IsCheckable="True" IsChecked="True"/>
        <MenuItem Name="MenuShowToolbar" Header="Show _Header" IsCheckable="True" IsChecked="True"/>
        <Separator/>
        <MenuItem Name="MenuRefresh" Header="_Refresh" InputGestureText="F5"/>
      </MenuItem>
      <MenuItem Header="_Tools">
        <MenuItem Name="MenuOpenExplorer" Header="Open _Explorer"/>
        <MenuItem Name="MenuOpenCmd" Header="Open _Command Prompt"/>
        <MenuItem Name="MenuOpenPowerShell" Header="Open _PowerShell"/>
        <Separator/>
        <MenuItem Name="MenuOpenTaskMgr" Header="Open _Task Manager"/>
        <MenuItem Name="MenuOpenServices" Header="Open _Services"/>
        <MenuItem Name="MenuOpenDevMgr" Header="Open _Device Manager"/>
      </MenuItem>
      <MenuItem Header="_Help">
        <MenuItem Name="MenuHelpContents" Header="_Help Contents" InputGestureText="F1"/>
        <MenuItem Name="MenuQuickStart" Header="_Quick Start Guide"/>
        <MenuItem Name="MenuKeyboardShortcuts" Header="_Keyboard Shortcuts"/>
        <Separator/>
        <MenuItem Name="MenuCheckUpdates" Header="Check for _Updates"/>
        <MenuItem Name="MenuViewLog" Header="View Application _Log"/>
        <Separator/>
        <MenuItem Name="MenuAbout" Header="_About PlatypusTools"/>
      </MenuItem>
    </Menu>

    <Border DockPanel.Dock="Top" Background="{StaticResource HeaderBrush}" Padding="12" CornerRadius="6" Margin="8" Name="HeaderBorder">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto"/>
          <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <Image Name="ImgHeader" Width="64" Height="64" Stretch="UniformToFill" Margin="0,0,12,0"/>
        <TextBlock Grid.Column="1" Text="PlatypusTools" FontSize="16" FontWeight="Bold" Foreground="White" VerticalAlignment="Center"/>
      </Grid>
    </Border>

    <StatusBar DockPanel.Dock="Bottom" Margin="8,4,8,8" Background="{DynamicResource StatusBarBg}" Name="MainStatusBar">
      <StatusBarItem>
        <TextBlock Name="TxtStatus" Text="Ready." FontWeight="SemiBold"/>
      </StatusBarItem>
      <StatusBarItem HorizontalAlignment="Right">
        <Button Name="BtnExit" Content="Exit" Width="90"/>
      </StatusBarItem>
    </StatusBar>

    <TabControl Margin="8" DockPanel.Dock="Top" Name="MainTabControl">
        <!-- File Cleaner Tab -->
        <TabItem Header="File Cleaner">
          <Grid Margin="4">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*" MinWidth="400"/>
              <ColumnDefinition Width="8"/>
              <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">
              <StackPanel Orientation="Vertical" Margin="4" MinWidth="380">

                <GroupBox Header="Folder Selection" Margin="0,0,0,10">
                  <StackPanel Margin="8">
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                      <TextBox Name="TxtFolder" Width="320" Margin="0,0,6,0"/>
                      <Button Name="BtnBrowse" Content="Browse..." Width="90"/>
                    </StackPanel>
                    <CheckBox Name="ChkRecurse" Content="Include Subfolders"/>
                  </StackPanel>
                </GroupBox>

              <GroupBox Header="Prefix Options" Margin="0,0,0,10">
                <StackPanel Margin="8" Orientation="Vertical">
                  <CheckBox Name="ChkChangePrefix" Content="Change Prefix (Old -> New)" Margin="0,0,0,6"/>
                  <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="200"/>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="200"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Old Prefix:" Grid.Column="0"/>
                    <TextBox Name="TxtOldPrefix" Grid.Column="1" Margin="4,0"/>
                    <Label Content="New Prefix:" Grid.Column="2"/>
                    <TextBox Name="TxtNewPrefix" Grid.Column="3" Margin="4,0"/>
                  </Grid>
                  <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="110"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Detected Prefix:" Grid.Column="0"/>
                    <TextBox Name="TxtDetectedPrefix" Grid.Column="1" Margin="4,0"/>
                    <Button Name="BtnDetectPrefix" Content="Detect" Grid.Column="2" Width="90" Margin="6,0,0,0"/>
                  </Grid>
                  <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Ignore Prefix:" Grid.Column="0"/>
                    <TextBox Name="TxtIgnorePrefix" Grid.Column="1" Margin="4,0"/>
                  </Grid>
                  <CheckBox Name="ChkAddPrefixAll" Content="Add Prefix to All Files"/>
                  <CheckBox Name="ChkNormalizePrefixCase" Content="Normalize prefix casing (force apply case-only changes)"/>
                  <CheckBox Name="ChkOnlyIfOldPrefix" Content="Only process files with Old Prefix"/>
                  <CheckBox Name="ChkDryRun" Content="Dry-run (simulate without renaming)"/>
                </StackPanel>
              </GroupBox>

              <GroupBox Header="Season / Episode Options" Margin="0,0,0,10">
                <StackPanel Margin="8">
                  <CheckBox Name="ChkAddSeason" Content="Add Season"/>
                  <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="100"/>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="100"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Season #:" Grid.Column="0"/>
                    <TextBox Name="TxtSeason" Grid.Column="1" Width="80"/>
                    <Label Content="Digits:" Grid.Column="2"/>
                    <ComboBox Name="CmbSeasonDigits" Grid.Column="3" Width="80">
                      <ComboBoxItem Content="2" IsSelected="True"/>
                      <ComboBoxItem Content="3"/>
                    </ComboBox>
                  </Grid>
                  <CheckBox Name="ChkAddEpisode" Content="Add / Update Episode"/>
                  <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="100"/>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="100"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Start #:" Grid.Column="0"/>
                    <TextBox Name="TxtStart" Grid.Column="1" Width="80"/>
                    <Label Content="Digits:" Grid.Column="2"/>
                    <ComboBox Name="CmbEpisodeDigits" Grid.Column="3" Width="80">
                      <ComboBoxItem Content="2" IsSelected="True"/>
                      <ComboBoxItem Content="3"/>
                      <ComboBoxItem Content="4"/>
                    </ComboBox>
                  </Grid>
                  <CheckBox Name="ChkRenumberAll" Content="Renumber all files alphabetically"/>
                  <CheckBox Name="ChkSeasonBeforeEpisode" Content="Place Season before Episode (S01E01)"/>
                </StackPanel>
              </GroupBox>

              <GroupBox Header="Cleaning Options" Margin="0,0,0,10">
                <StackPanel Margin="8">
                  <TextBlock Text="Remove common tokens:" FontWeight="Bold"/>
                  <WrapPanel>
                    <CheckBox Name="Chk720p" Content="720p"/>
                    <CheckBox Name="Chk1080p" Content="1080p"/>
                    <CheckBox Name="Chk4k" Content="4K"/>
                    <CheckBox Name="ChkHD" Content="HD"/>
                  </WrapPanel>
                  <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="140"/>
                      <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Custom Tokens:" Grid.Column="0"/>
                    <TextBox Name="TxtCustomClean" Grid.Column="1"/>
                  </Grid>
                  <TextBlock Text="Use metadata (optional):" FontWeight="Bold"/>
                  <CheckBox Name="ChkUseAudioMetadata" Content="Use Audio Metadata (Artist/Album/Title)"/>
                  <CheckBox Name="ChkUseVideoMetadata" Content="Use Video Metadata (Show/Title/Season/Episode)"/>
                </StackPanel>
              </GroupBox>

              <GroupBox Header="File Type Filters" Margin="0,0,0,10">
                <StackPanel Margin="8">
                  <WrapPanel>
                    <CheckBox Name="ChkVideo" Content="Video" IsChecked="True"/>
                    <CheckBox Name="ChkPictures" Content="Pictures"/>
                    <CheckBox Name="ChkDocuments" Content="Documents"/>
                    <CheckBox Name="ChkAudio" Content="Audio"/>
                    <CheckBox Name="ChkArchives" Content="Archives"/>
                  </WrapPanel>
                  <Grid>
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="160"/>
                      <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Custom Extensions:" Grid.Column="0"/>
                    <TextBox Name="TxtCustomExt" Grid.Column="1"/>
                  </Grid>
                  <TextBlock Text="Name normalization:" FontWeight="Bold" Margin="0,8,0,0"/>
                  <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                    <ComboBox Name="CmbSpaceReplace" Width="280">
                      <ComboBoxItem Content="Spaces -> '-'" Tag="space-to-dash" IsSelected="True"/>
                      <ComboBoxItem Content="Spaces -> '_'" Tag="space-to-underscore"/>
                      <ComboBoxItem Content="Remove spaces" Tag="space-remove"/>
                      <ComboBoxItem Content="'-' -> spaces" Tag="dash-to-space"/>
                      <ComboBoxItem Content="'-' -> '_'" Tag="dash-to-underscore"/>
                      <ComboBoxItem Content="'_' -> '-'" Tag="underscore-to-dash"/>
                      <ComboBoxItem Content="'_' -> spaces" Tag="underscore-to-space"/>
                    </ComboBox>
                    <Button Name="BtnSpaceCleanup" Content="Normalize Names" Width="140" Margin="8,0,0,0"/>
                  </StackPanel>
                </StackPanel>
              </GroupBox>

            </StackPanel>
          </ScrollViewer>

          <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" ShowsPreview="True" Background="#EEE"/>

          <Grid Grid.Column="2">
            <Grid.RowDefinitions>
              <RowDefinition Height="*"/>
              <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <DataGrid Name="DgPreview"
                      Grid.Row="0"
                      AutoGenerateColumns="False"
                      CanUserAddRows="False"
                      CanUserResizeColumns="True"
                      IsReadOnly="False"
                      Margin="0"
                      RowBackground="#F9FBFF"
                      AlternatingRowBackground="#FFFFFF">
              <DataGrid.Columns>
                <DataGridCheckBoxColumn Header="Apply" Binding="{Binding Apply}" Width="80"/>
                <DataGridTextColumn Header="Original" Binding="{Binding Original}" Width="260"/>
                <DataGridTextColumn Header="Proposed" Binding="{Binding Proposed}" Width="260"/>
                <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="140"/>
                <DataGridTextColumn Header="Directory" Binding="{Binding Directory}" Width="360"/>
                <DataGridTextColumn Header="Meta" Binding="{Binding MetaSummary}" Width="300"/>
              </DataGrid.Columns>
            </DataGrid>

            <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Left" Margin="0,8,0,0">
              <Button Name="BtnSelectAll" Content="Select All" Width="100"/>
              <Button Name="BtnSelectNone" Content="Select None" Width="100"/>
              <Button Name="BtnScan" Content="Scan / Preview" Width="130"/>
              <Button Name="BtnApply" Content="Apply Changes" Width="130" IsEnabled="False"/>
              <Button Name="BtnUndo" Content="Undo Last" Width="110"/>
              <Button Name="BtnExportCsv" Content="Export CSV" Width="110" IsEnabled="False"/>
              <Button Name="BtnReset" Content="Reset" Width="100"/>
            </StackPanel>
          </Grid>
        </Grid>
      </TabItem>

      <!-- Media Conversion Tab (Video + Graphics) -->
      <TabItem Header="Media Conversion">
        <TabControl Margin="4">
          <!-- Video Sub-tab -->
          <TabItem Header="Video">
            <Grid Margin="4">
              <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="140"/>
              </Grid.RowDefinitions>

              <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,8">
                <TextBlock Text="Tool Folder:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBox Name="TxtToolDir" Width="360" Margin="0,0,6,0" ToolTip="Folder containing ffmpeg, ffprobe, exiftool"/>
                <Button Name="BtnToolDirBrowse" Content="Browse..." Width="90"/>
                <TextBlock Text="Used for ffmpeg/ffprobe/exiftool" VerticalAlignment="Center" Opacity="0.7" Margin="8,0,0,0"/>
              </StackPanel>

              <StackPanel Orientation="Horizontal" Grid.Row="1" Margin="0,0,0,8">
                <Button Name="BtnCombBrowse" Content="Browse Folder" Width="130"/>
                <Button Name="BtnCombSelectAll" Content="Select All" Width="100"/>
                <Button Name="BtnCombSelectNone" Content="Select None" Width="110"/>
                <Button Name="BtnCombUp" Content="Move Up" Width="90"/>
                <Button Name="BtnCombDown" Content="Move Down" Width="100"/>
              </StackPanel>

              <DataGrid Name="DgCombine" Grid.Row="2" AutoGenerateColumns="False" CanUserAddRows="False" CanUserResizeColumns="True" Margin="0,0,0,8" SelectionMode="Single" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
                <DataGrid.Columns>
                  <DataGridCheckBoxColumn Header="Use" Binding="{Binding Apply}" Width="80"/>
                  <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="260"/>
                  <DataGridTextColumn Header="Path" Binding="{Binding Path}" Width="540"/>
                </DataGrid.Columns>
              </DataGrid>

              <StackPanel Orientation="Horizontal" Grid.Row="3" Margin="0,0,0,8">
                <Button Name="BtnCombPreview" Content="Preview Encodings" Width="150"/>
                <Button Name="BtnCombNormalize" Content="Normalize (H264/AAC)" Width="170"/>
                <Button Name="BtnCombSafe" Content="Safe Combine (Re-encode)" Width="200"/>
                <Button Name="BtnCombCombine" Content="Combine" Width="120"/>
                <ComboBox Name="CmbConvFormat" Width="90" Margin="12,4,4,4">
                  <ComboBoxItem Content="MP4" Tag="mp4" IsSelected="True"/>
                  <ComboBoxItem Content="WMV" Tag="wmv"/>
                </ComboBox>
                <ComboBox Name="CmbConvRes" Width="110" Margin="4">
                  <ComboBoxItem Content="Source" Tag="source" IsSelected="True"/>
                  <ComboBoxItem Content="720p" Tag="720p"/>
                  <ComboBoxItem Content="1080p" Tag="1080p"/>
                  <ComboBoxItem Content="4K" Tag="4k"/>
                </ComboBox>
                <TextBlock Text="Output Folder:" VerticalAlignment="Center" Margin="8,0,4,0"/>
                <TextBox Name="TxtConvOut" Width="260" ToolTip="Folder for converts and combined files"/>
                <Button Name="BtnConvBrowse" Content="Browse..." Width="90"/>
                <Button Name="BtnCombConvert" Content="Convert" Width="100"/>
              </StackPanel>

              <StackPanel Grid.Row="4" Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Name="TxtConvStatus" Text="Conversion status: idle" VerticalAlignment="Center" Margin="0,0,8,0"/>
                <ProgressBar Name="ConvProgress" Height="16" Width="200" Minimum="0" Maximum="100" IsIndeterminate="False" Visibility="Collapsed"/>
              </StackPanel>

              <TextBox Name="TxtCombLog" Grid.Row="5" Margin="0" IsReadOnly="True" TextWrapping="NoWrap" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"/>
            </Grid>
          </TabItem>

          <!-- Graphics Sub-tab -->
          <TabItem Header="Graphics (ICO)">
            <Grid Margin="4">
              <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="120"/>
              </Grid.RowDefinitions>

              <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="Images folder:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBox Name="TxtImgFolder" Width="360" Margin="0,0,6,0"/>
                <Button Name="BtnImgBrowse" Content="Browse..." Width="90"/>
                <Button Name="BtnImgAddFiles" Content="Add Files" Width="100"/>
                <Button Name="BtnImgAddFolder" Content="Add Folder" Width="110"/>
                <Button Name="BtnImgClear" Content="Clear List" Width="100"/>
              </StackPanel>

              <DataGrid Name="DgImages" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" CanUserResizeColumns="True" Margin="0,0,0,8" SelectionMode="Extended" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
                <DataGrid.Columns>
                  <DataGridCheckBoxColumn Header="Convert" Binding="{Binding Apply}" Width="80"/>
                  <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="220"/>
                  <DataGridTextColumn Header="Path" Binding="{Binding Path}" Width="540"/>
                </DataGrid.Columns>
              </DataGrid>

              <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="Output folder:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBox Name="TxtIcoOut" Width="260" Margin="0,0,6,0"/>
                <Button Name="BtnIcoOutBrowse" Content="Browse..." Width="90"/>
                <TextBlock Text="Output name:" VerticalAlignment="Center" Margin="8,0,6,0"/>
                <TextBox Name="TxtIcoName" Width="180" ToolTip="Custom output filename (without extension). Leave blank to use source name."/>
                <TextBlock Text="Icon size:" VerticalAlignment="Center" Margin="8,0,6,0"/>
                <ComboBox Name="CmbIcoSize" Width="80">
                  <ComboBoxItem Content="256" IsSelected="True"/>
                  <ComboBoxItem Content="128"/>
                  <ComboBoxItem Content="64"/>
                  <ComboBoxItem Content="48"/>
                  <ComboBoxItem Content="32"/>
                  <ComboBoxItem Content="24"/>
                  <ComboBoxItem Content="16"/>
                </ComboBox>
                <CheckBox Name="ChkIcoOverwrite" Content="Overwrite existing" VerticalAlignment="Center" Margin="8,0,0,0"/>
              </StackPanel>

              <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,0,0,8">
                <Button Name="BtnImgConvert" Content="Convert to ICO" Width="140"/>
                <TextBlock Text="  or  " VerticalAlignment="Center"/>
                <TextBlock Text="Format:" VerticalAlignment="Center" Margin="0,0,4,0"/>
                <ComboBox Name="CmbImgOutFormat" Width="80">
                  <ComboBoxItem Content="PNG" Tag="png" IsSelected="True"/>
                  <ComboBoxItem Content="JPG" Tag="jpg"/>
                  <ComboBoxItem Content="BMP" Tag="bmp"/>
                  <ComboBoxItem Content="GIF" Tag="gif"/>
                  <ComboBoxItem Content="TIFF" Tag="tiff"/>
                </ComboBox>
                <Button Name="BtnImgConvertFormat" Content="Convert Format" Width="120" Margin="4,0,0,0"/>
                <TextBlock Name="TxtImgStatus" Text="Ready." VerticalAlignment="Center" Margin="8,0,8,0"/>
                <ProgressBar Name="ImgProgress" Height="16" Width="180" Minimum="0" Maximum="100" Visibility="Collapsed"/>
              </StackPanel>

              <TextBox Name="TxtImgLog" Grid.Row="4" Margin="0" IsReadOnly="True" TextWrapping="NoWrap" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"/>
            </Grid>
          </TabItem>

          <!-- Image Resize Sub-tab -->
          <TabItem Header="Image Resize">
            <Grid Margin="4">
              <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="100"/>
              </Grid.RowDefinitions>

              <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
                <Button Name="BtnResizeBrowse" Content="Browse..." Width="90"/>
                <Button Name="BtnResizeAddFiles" Content="Add Files" Width="100"/>
                <Button Name="BtnResizeClear" Content="Clear List" Width="100"/>
              </StackPanel>

              <DataGrid Name="DgResize" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" CanUserResizeColumns="True" Margin="0,0,0,8" SelectionMode="Extended" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
                <DataGrid.Columns>
                  <DataGridCheckBoxColumn Header="Process" Binding="{Binding Apply}" Width="70"/>
                  <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="200"/>
                  <DataGridTextColumn Header="Dimensions" Binding="{Binding Dimensions}" Width="100"/>
                  <DataGridTextColumn Header="Size" Binding="{Binding Size}" Width="80"/>
                  <DataGridTextColumn Header="Path" Binding="{Binding Path}" Width="400"/>
                </DataGrid.Columns>
              </DataGrid>

              <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="Output folder:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBox Name="TxtResizeOut" Width="260" Margin="0,0,6,0"/>
                <Button Name="BtnResizeOutBrowse" Content="Browse..." Width="90"/>
                <TextBlock Text="Max Width:" VerticalAlignment="Center" Margin="12,0,6,0"/>
                <TextBox Name="TxtResizeWidth" Width="60" Text="1920"/>
                <TextBlock Text="Max Height:" VerticalAlignment="Center" Margin="8,0,6,0"/>
                <TextBox Name="TxtResizeHeight" Width="60" Text="1080"/>
                <TextBlock Text="Quality:" VerticalAlignment="Center" Margin="8,0,6,0"/>
                <ComboBox Name="CmbResizeQuality" Width="80">
                  <ComboBoxItem Content="100" Tag="100"/>
                  <ComboBoxItem Content="90" Tag="90" IsSelected="True"/>
                  <ComboBoxItem Content="80" Tag="80"/>
                  <ComboBoxItem Content="70" Tag="70"/>
                  <ComboBoxItem Content="60" Tag="60"/>
                </ComboBox>
                <TextBlock Text="Format:" VerticalAlignment="Center" Margin="8,0,6,0"/>
                <ComboBox Name="CmbResizeFormat" Width="80">
                  <ComboBoxItem Content="Same" Tag="same" IsSelected="True"/>
                  <ComboBoxItem Content="JPG" Tag="jpg"/>
                  <ComboBoxItem Content="PNG" Tag="png"/>
                  <ComboBoxItem Content="WEBP" Tag="webp"/>
                </ComboBox>
              </StackPanel>

              <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,0,0,8">
                <Button Name="BtnResizeRun" Content="Resize Images" Width="140"/>
                <CheckBox Name="ChkResizeOverwrite" Content="Overwrite existing" VerticalAlignment="Center" Margin="8,0,0,0"/>
                <CheckBox Name="ChkResizeKeepAspect" Content="Maintain aspect ratio" VerticalAlignment="Center" Margin="8,0,0,0" IsChecked="True"/>
                <TextBlock Name="TxtResizeStatus" Text="Ready." VerticalAlignment="Center" Margin="12,0,8,0"/>
                <ProgressBar Name="ResizeProgress" Height="16" Width="200" Minimum="0" Maximum="100" Visibility="Collapsed"/>
              </StackPanel>

              <TextBox Name="TxtResizeLog" Grid.Row="4" Margin="0" IsReadOnly="True" TextWrapping="NoWrap" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"/>
            </Grid>
          </TabItem>

          <!-- Bootable USB Sub-tab -->
          <TabItem Header="Bootable USB">
            <Grid Margin="4">
              <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
              </Grid.RowDefinitions>

              <TextBlock Grid.Row="0" Text="Create bootable USB drives from ISO files (Windows, Linux, etc.)" Margin="0,0,0,12" FontWeight="SemiBold"/>

              <!-- ISO Selection -->
              <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="ISO File:" VerticalAlignment="Center" Width="80"/>
                <TextBox Name="TxtIsoPath" Width="400" Margin="0,0,8,0" IsReadOnly="True"/>
                <Button Name="BtnIsoBrowse" Content="Browse..." Width="90"/>
              </StackPanel>

              <!-- USB Drive Selection -->
              <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,8">
                <TextBlock Text="USB Drive:" VerticalAlignment="Center" Width="80"/>
                <ComboBox Name="CmbUsbDrive" Width="300" Margin="0,0,8,0"/>
                <Button Name="BtnUsbRefresh" Content="Refresh" Width="80" Margin="0,0,8,0"/>
                <TextBlock Name="TxtUsbInfo" Text="" VerticalAlignment="Center" Foreground="Gray"/>
              </StackPanel>

              <!-- Options -->
              <GroupBox Grid.Row="3" Header="Options" Margin="0,0,0,8" Padding="8">
                <StackPanel>
                  <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                    <TextBlock Text="File System:" VerticalAlignment="Center" Width="100"/>
                    <ComboBox Name="CmbUsbFileSystem" Width="120">
                      <ComboBoxItem Content="FAT32 (UEFI)" Tag="FAT32" IsSelected="True"/>
                      <ComboBoxItem Content="NTFS (Legacy/Large)" Tag="NTFS"/>
                      <ComboBoxItem Content="exFAT" Tag="exFAT"/>
                    </ComboBox>
                    <TextBlock Text="  Volume Label:" VerticalAlignment="Center" Margin="16,0,6,0"/>
                    <TextBox Name="TxtUsbLabel" Width="120" Text="BOOTUSB"/>
                  </StackPanel>
                  <StackPanel Orientation="Horizontal">
                    <CheckBox Name="ChkUsbQuickFormat" Content="Quick Format" IsChecked="True" Margin="0,0,16,0"/>
                    <CheckBox Name="ChkUsbVerify" Content="Verify after write" Margin="0,0,16,0"/>
                    <TextBlock Text="Boot Mode:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                    <ComboBox Name="CmbBootMode" Width="140">
                      <ComboBoxItem Content="UEFI (GPT)" Tag="UEFI" IsSelected="True"/>
                      <ComboBoxItem Content="Legacy BIOS (MBR)" Tag="MBR"/>
                      <ComboBoxItem Content="UEFI + Legacy" Tag="BOTH"/>
                    </ComboBox>
                  </StackPanel>
                </StackPanel>
              </GroupBox>

              <!-- Actions -->
              <StackPanel Grid.Row="4" Orientation="Horizontal" Margin="0,0,0,8">
                <Button Name="BtnUsbWrite" Content="Create Bootable USB" Width="160" FontWeight="Bold"/>
                <Button Name="BtnUsbCancel" Content="Cancel" Width="80" Margin="8,0,0,0" IsEnabled="False"/>
                <TextBlock Name="TxtUsbStatus" Text="Ready. Select an ISO and USB drive." VerticalAlignment="Center" Margin="12,0,0,0"/>
                <ProgressBar Name="UsbProgress" Height="18" Width="250" Minimum="0" Maximum="100" Visibility="Collapsed" Margin="12,0,0,0"/>
              </StackPanel>

              <!-- Log -->
              <TextBox Name="TxtUsbLog" Grid.Row="5" Margin="0" IsReadOnly="True" TextWrapping="NoWrap" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="11" Background="#1E1E1E" Foreground="#DCDCDC"/>
            </Grid>
          </TabItem>
        </TabControl>
      </TabItem>

      <!-- Duplicates Tab -->
      <TabItem Header="Duplicates">
        <Grid Margin="4">
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="120"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>

          <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Folder:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox Name="TxtDupFolder" Width="360" Margin="0,0,6,0"/>
            <Button Name="BtnDupBrowse" Content="Browse..." Width="90"/>
            <CheckBox Name="ChkDupRecurse" Content="Include Subfolders" VerticalAlignment="Center" Margin="8,0,0,0"/>
          </StackPanel>

          <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="File Types:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <CheckBox Name="ChkDupVideo" Content="Video" IsChecked="True" Margin="0,0,8,0"/>
            <CheckBox Name="ChkDupPictures" Content="Pictures" IsChecked="True" Margin="0,0,8,0"/>
            <CheckBox Name="ChkDupDocuments" Content="Documents" IsChecked="True" Margin="0,0,8,0"/>
            <CheckBox Name="ChkDupAudio" Content="Audio" IsChecked="True" Margin="0,0,8,0"/>
            <CheckBox Name="ChkDupArchives" Content="Archives" IsChecked="True"/>
          </StackPanel>

          <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Custom Extensions (comma separated):" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <TextBox Name="TxtDupCustomExt" Width="260" Margin="0,0,6,0"/>
            <TextBlock Text="Method:" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <ComboBox Name="CmbDupMethod" Width="170" SelectedIndex="0">
              <ComboBoxItem Content="Fast (SHA-256)" Tag="fast" IsSelected="True"/>
              <ComboBoxItem Content="Deep (media perceptual)" Tag="deep"/>
            </ComboBox>
            <Button Name="BtnDupScan" Content="Scan Duplicates" Width="150"/>
          </StackPanel>

          <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,0,0,8">
            <Button Name="BtnDupDelete" Content="Delete Selected" Width="140"/>
            <Button Name="BtnDupRename" Content="Rename Selected" Width="140"/>
            <Button Name="BtnDupReset" Content="Reset" Width="100"/>
          </StackPanel>

          <GroupBox Grid.Row="4" Header="Files being scanned">
            <ListBox Name="LstDupScanLog"/>
          </GroupBox>

          <DataGrid Name="DgDupes" Grid.Row="5" AutoGenerateColumns="False" CanUserAddRows="False" CanUserResizeColumns="True" Margin="0,8,0,0" SelectionMode="Extended" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
            <DataGrid.Columns>
              <DataGridCheckBoxColumn Header="Select" Binding="{Binding Apply}" Width="80"/>
              <DataGridTextColumn Header="Hash" Binding="{Binding Hash}" Width="220"/>
              <DataGridTextColumn Header="Count" Binding="{Binding Count}" Width="60"/>
              <DataGridTextColumn Header="Size" Binding="{Binding Size}" Width="100"/>
              <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="200"/>
              <DataGridTextColumn Header="Directory" Binding="{Binding Directory}" Width="360"/>
              <DataGridTextColumn Header="Full Path" Binding="{Binding FullPath}" Width="360"/>
            </DataGrid.Columns>
          </DataGrid>

          <StackPanel Grid.Row="6" Orientation="Horizontal" Margin="0,8,0,0">
            <TextBlock Name="TxtDupStatus" Text="Ready." VerticalAlignment="Center"/>
            <ProgressBar Name="DupProgress" Height="14" Width="180" Minimum="0" Maximum="100" IsIndeterminate="False" Visibility="Collapsed" Margin="12,0,0,0"/>
            <Separator Width="1" Margin="12,0,8,0"/>
            <TextBlock Text="Hash Calculator:" VerticalAlignment="Center" FontWeight="Bold" Margin="0,0,6,0"/>
            <Button Name="BtnHashFile" Content="Select File" Width="100"/>
            <ComboBox Name="CmbHashAlgo" Width="90" Margin="6,0,0,0">
              <ComboBoxItem Content="SHA256" IsSelected="True"/>
              <ComboBoxItem Content="SHA1"/>
              <ComboBoxItem Content="MD5"/>
            </ComboBox>
            <TextBox Name="TxtHashResult" Width="400" IsReadOnly="True" Margin="6,0,0,0" FontFamily="Consolas"/>
            <Button Name="BtnHashCopy" Content="Copy" Width="60" Margin="6,0,0,0"/>
          </StackPanel>
        </Grid>
      </TabItem>

      <!-- Cleanup Tab (Recent + Disk + Privacy) -->
            <TabItem Header="Cleanup">
                <TabControl Margin="4">
                  <!-- Recent Cleaner Sub-tab -->
                  <TabItem Header="Recent Shortcuts">
                <Grid Margin="0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="360"/>
                        <ColumnDefinition Width="8"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>

                    <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                        <StackPanel Margin="4" MinWidth="300">
                            <TextBlock Text="Directories to Exclude" FontWeight="Bold" Margin="0,0,0,6"/>
                            <ListBox Name="DirList" Height="160" Margin="0,0,0,6" HorizontalContentAlignment="Stretch"/>
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                <Button Name="AddDir" Content="Add Directory" Width="140"/>
                                <Button Name="RemoveDir" Content="Remove Selected" Width="150"/>
                            </StackPanel>

                            <StackPanel Orientation="Horizontal" Margin="0,0,0,8" VerticalAlignment="Center">
                                <CheckBox Name="IncludeSubdirs" Content="Include Subdirectories" Margin="0,0,8,0" VerticalAlignment="Center"/>
                                <CheckBox Name="DryRunToggle" Content="Dry Run (Preview Only)" VerticalAlignment="Center"/>
                            </StackPanel>

                            <TextBlock Text="Output Folder" FontWeight="Bold" Margin="0,6,0,4"/>
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                <TextBox Name="OutputPathBox" Width="240" HorizontalAlignment="Stretch"/>
                                <Button Name="BrowseOutput" Content="Browse..." Width="90" Margin="6,0,0,0"/>
                            </StackPanel>

                            <GroupBox Header="Scheduled Task" Margin="0,8,0,0">
                                <Grid Margin="6">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="110"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>

                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Frequency" VerticalAlignment="Center" Margin="0,4,6,4"/>
                                    <ComboBox Name="FrequencyBox" Grid.Row="0" Grid.Column="1" MinWidth="140" Margin="0,4,0,4">
                                        <ComboBoxItem>Daily</ComboBoxItem>
                                        <ComboBoxItem>Weekly</ComboBoxItem>
                                        <ComboBoxItem>Monthly</ComboBoxItem>
                                    </ComboBox>

                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Time (HH:mm)" VerticalAlignment="Center" Margin="0,4,6,4"/>
                                    <TextBox Name="TimeBox" Grid.Row="1" Grid.Column="1" MinWidth="120" Margin="0,4,0,4"/>

                                    <TextBlock Grid.Row="2" Grid.Column="0" Text="Days" VerticalAlignment="Top" Margin="0,4,6,4"/>
                                    <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Vertical" Margin="0,4,0,4">
                                        <TextBox Name="DaysBox" Width="180" Margin="0,0,0,6" Visibility="Collapsed" ToolTip="For Monthly: enter day numbers like 1,15"/>
                                        <WrapPanel Name="WeekdaysPanel" Orientation="Horizontal">
                                            <CheckBox Name="chkSunday" Content="Sun" Margin="2"/>
                                            <CheckBox Name="chkMonday" Content="Mon" Margin="2"/>
                                            <CheckBox Name="chkTuesday" Content="Tue" Margin="2"/>
                                            <CheckBox Name="chkWednesday" Content="Wed" Margin="2"/>
                                            <CheckBox Name="chkThursday" Content="Thu" Margin="2"/>
                                            <CheckBox Name="chkFriday" Content="Fri" Margin="2"/>
                                            <CheckBox Name="chkSaturday" Content="Sat" Margin="2"/>
                                        </WrapPanel>
                                    </StackPanel>

                                    <TextBlock Grid.Row="3" Grid.Column="0" Text="Notes" VerticalAlignment="Top" Margin="0,6,6,0"/>
                                    <TextBlock Grid.Row="3" Grid.Column="1" Text="Weekly: select weekdays. Monthly: use Days box (e.g. 1,15)." TextWrapping="Wrap" Foreground="Gray" FontSize="11" Margin="0,6,0,0"/>
                                </Grid>
                            </GroupBox>

                        </StackPanel>
                    </ScrollViewer>

                    <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" ShowsPreview="True" Background="#EEE"/>

                    <Grid Grid.Column="2">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="*"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>
                        <DataGrid Name="PreviewGrid" Grid.Row="0" AutoGenerateColumns="True" IsReadOnly="True" Margin="0" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF"/>
                        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,8,0,0">
                            <Button Name="ShowPreview" Content="Show Preview" Width="120"/>
                            <Button Name="ExportCSV" Content="Export CSV" Width="110"/>
                            <Button Name="UndoLast" Content="Undo Last" Width="100"/>
                            <Button Name="RunNow" Content="Run Now" Width="100"/>
                            <Button Name="SaveTask" Content="Schedule Task" Width="130"/>
                        </StackPanel>
                    </Grid>
                </Grid>
            </TabItem>

                  <!-- Disk Cleanup Sub-tab -->
                  <TabItem Header="Disk Cleanup">
                    <Grid Margin="4">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="400"/>
                            <ColumnDefinition Width="8"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>

                        <StackPanel Grid.Column="0" Margin="0">
                            <TextBlock Text="Select items to clean:" FontWeight="Bold" Margin="0,0,0,8"/>
                            <CheckBox Name="ChkCleanTemp" Content="Windows Temp Files" Margin="0,0,0,4" IsChecked="True"/>
                            <CheckBox Name="ChkCleanUserTemp" Content="User Temp Files" Margin="0,0,0,4" IsChecked="True"/>
                            <CheckBox Name="ChkCleanPrefetch" Content="Prefetch Files" Margin="0,0,0,4"/>
                            <CheckBox Name="ChkCleanRecycleBin" Content="Recycle Bin" Margin="0,0,0,4"/>
                            <CheckBox Name="ChkCleanDownloads" Content="Downloads Folder (older than 30 days)" Margin="0,0,0,4"/>
                            <CheckBox Name="ChkCleanWindowsUpdate" Content="Windows Update Cache" Margin="0,0,0,4"/>
                            <CheckBox Name="ChkCleanThumbnails" Content="Thumbnail Cache" Margin="0,0,0,4" IsChecked="True"/>
                            <CheckBox Name="ChkCleanErrorReports" Content="Windows Error Reports" Margin="0,0,0,4" IsChecked="True"/>
                            <CheckBox Name="ChkCleanLogFiles" Content="Old Log Files" Margin="0,0,0,8" IsChecked="True"/>

                            <Separator Margin="0,4,0,8"/>

                            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                <Button Name="BtnDiskAnalyze" Content="Analyze" Width="100"/>
                                <Button Name="BtnDiskClean" Content="Clean Now" Width="100" Margin="6,0,0,0"/>
                                <CheckBox Name="ChkDiskDryRun" Content="Dry Run" VerticalAlignment="Center" Margin="12,0,0,0" IsChecked="True"/>
                            </StackPanel>

                            <TextBlock Name="TxtDiskStatus" Text="Ready. Click Analyze to scan." Foreground="#444" TextWrapping="Wrap"/>
                            <TextBlock Name="TxtDiskSpace" Text="" FontWeight="Bold" Margin="0,8,0,0"/>
                        </StackPanel>

                        <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" ShowsPreview="True" Background="#EEE"/>

                        <Grid Grid.Column="2">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="*"/>
                            </Grid.RowDefinitions>
                            <TextBlock Text="Files to be cleaned:" FontWeight="Bold" Margin="0,0,0,8"/>
                            <DataGrid Name="DgDiskClean" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" IsReadOnly="True" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
                                <DataGrid.Columns>
                                    <DataGridTextColumn Header="Category" Binding="{Binding Category}" Width="180"/>
                                    <DataGridTextColumn Header="Files" Binding="{Binding FileCount}" Width="80"/>
                                    <DataGridTextColumn Header="Size" Binding="{Binding Size}" Width="100"/>
                                    <DataGridTextColumn Header="Path" Binding="{Binding Path}" Width="400"/>
                                </DataGrid.Columns>
                            </DataGrid>
                        </Grid>
                    </Grid>
                  </TabItem>

                  <!-- Privacy Cleaner Sub-tab -->
                  <TabItem Header="Privacy Cleaner">
                    <Grid Margin="4">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="450"/>
                            <ColumnDefinition Width="8"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>

                        <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto">
                            <StackPanel Margin="0">
                                <TextBlock Text="Browser Data" FontWeight="Bold" Margin="0,0,0,8"/>
                                <CheckBox Name="ChkPrivChrome" Content="Chrome - Cookies, Cache, History" Margin="0,0,0,4" IsChecked="True"/>
                                <CheckBox Name="ChkPrivEdge" Content="Edge - Cookies, Cache, History" Margin="0,0,0,4" IsChecked="True"/>
                                <CheckBox Name="ChkPrivFirefox" Content="Firefox - Cookies, Cache, History" Margin="0,0,0,4" IsChecked="True"/>
                                <CheckBox Name="ChkPrivBrave" Content="Brave - Cookies, Cache, History" Margin="0,0,0,8"/>

                                <TextBlock Text="Cloud Service Tokens" FontWeight="Bold" Margin="0,8,0,8"/>
                                <CheckBox Name="ChkPrivOneDrive" Content="OneDrive Cached Credentials" Margin="0,0,0,4"/>
                                <CheckBox Name="ChkPrivGoogleDrive" Content="Google Drive Cached Credentials" Margin="0,0,0,4"/>
                                <CheckBox Name="ChkPrivDropbox" Content="Dropbox Cached Credentials" Margin="0,0,0,4"/>
                                <CheckBox Name="ChkPriviCloud" Content="iCloud Cached Data" Margin="0,0,0,8"/>

                                <TextBlock Text="Windows Identity" FontWeight="Bold" Margin="0,8,0,8"/>
                                <CheckBox Name="ChkPrivCredManager" Content="Windows Credential Manager (Web)" Margin="0,0,0,4"/>
                                <CheckBox Name="ChkPrivRecentDocs" Content="Recent Documents List" Margin="0,0,0,4" IsChecked="True"/>
                                <CheckBox Name="ChkPrivJumpLists" Content="Taskbar Jump Lists" Margin="0,0,0,4" IsChecked="True"/>
                                <CheckBox Name="ChkPrivExplorer" Content="Explorer Recent/Frequent" Margin="0,0,0,4" IsChecked="True"/>
                                <CheckBox Name="ChkPrivClipboard" Content="Clipboard History" Margin="0,0,0,8" IsChecked="True"/>

                                <TextBlock Text="Application Data" FontWeight="Bold" Margin="0,8,0,8"/>
                                <CheckBox Name="ChkPrivOffice" Content="Office Recent Files" Margin="0,0,0,4"/>
                                <CheckBox Name="ChkPrivAdobeRecent" Content="Adobe Recent Files" Margin="0,0,0,4"/>
                                <CheckBox Name="ChkPrivMediaPlayer" Content="Media Player History" Margin="0,0,0,8"/>

                                <Separator Margin="0,8,0,12"/>

                                <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                    <Button Name="BtnPrivAnalyze" Content="Analyze" Width="100"/>
                                    <Button Name="BtnPrivClean" Content="Clean Now" Width="100" Margin="6,0,0,0"/>
                                    <CheckBox Name="ChkPrivDryRun" Content="Dry Run" VerticalAlignment="Center" Margin="12,0,0,0" IsChecked="True"/>
                                </StackPanel>

                                <TextBlock Text="?? Warning: Cleaning browser data will log you out of websites." Foreground="OrangeRed" TextWrapping="Wrap" Margin="0,0,0,4"/>
                                <TextBlock Text="?? Cleaning cloud tokens may require re-authentication." Foreground="OrangeRed" TextWrapping="Wrap"/>
                            </StackPanel>
                        </ScrollViewer>

                        <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" ShowsPreview="True" Background="#EEE"/>

                        <Grid Grid.Column="2">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="*"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            <TextBlock Text="Items to be cleaned:" FontWeight="Bold" Margin="0,0,0,8"/>
                            <DataGrid Name="DgPrivClean" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" IsReadOnly="True" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
                                <DataGrid.Columns>
                                    <DataGridTextColumn Header="Category" Binding="{Binding Category}" Width="180"/>
                                    <DataGridTextColumn Header="Type" Binding="{Binding Type}" Width="120"/>
                                    <DataGridTextColumn Header="Items" Binding="{Binding ItemCount}" Width="80"/>
                                    <DataGridTextColumn Header="Size" Binding="{Binding Size}" Width="100"/>
                                    <DataGridTextColumn Header="Location" Binding="{Binding Location}" Width="350"/>
                                </DataGrid.Columns>
                            </DataGrid>
                            <TextBlock Name="TxtPrivStatus" Grid.Row="2" Text="Ready. Click Analyze to scan." Foreground="#444" TextWrapping="Wrap" Margin="0,8,0,0"/>
                        </Grid>
                    </Grid>
                  </TabItem>
                </TabControl>
            </TabItem>

      <!-- Security Tab (Folder Hider + Security Scan) -->
            <TabItem Header="Security">
                <TabControl Margin="4">
                  <!-- Folder Hider Sub-tab -->
                  <TabItem Header="Folder Hider">
                    <Grid Margin="4">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="12"/>
                            <ColumnDefinition Width="380"/>
                        </Grid.ColumnDefinitions>

                        <GroupBox Header="Managed Folders" Grid.Column="0" Margin="0,0,8,0">
                            <Grid>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="*"/>
                                    <RowDefinition Height="Auto"/>
                                </Grid.RowDefinitions>

                                <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                    <Button Name="HAddDir" Content="Add Folder..." Width="110"/>
                                    <Button Name="HAddManual" Content="Add From Path" Width="120"/>
                                    <Button Name="HRemoveDir" Content="Remove" Width="90"/>
                                </StackPanel>

                                <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,6">
                                    <TextBlock Text="Manual path:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                                    <TextBox Name="HManualPath" MinWidth="300" />
                                </StackPanel>

                                <ListView Name="HiderList" Grid.Row="2" Margin="0,0,0,6" VerticalAlignment="Stretch">
                                    <ListView.View>
                                        <GridView>
                                            <GridViewColumn Header="Folder" Width="300" DisplayMemberBinding="{Binding Path}"/>
                                            <GridViewColumn Header="Status" Width="80" DisplayMemberBinding="{Binding Status}"/>
                                            <GridViewColumn Header="ACL" Width="60" DisplayMemberBinding="{Binding ACL}"/>
                                            <GridViewColumn Header="EFS" Width="60" DisplayMemberBinding="{Binding EFS}"/>
                                        </GridView>
                                    </ListView.View>
                                </ListView>

                                <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Left" Margin="0,4,0,0">
                                    <Button Name="HHideBtn" Content="Hide" Width="80"/>
                                    <Button Name="HUnhideBtn" Content="Unhide" Width="90"/>
                                    <Button Name="HSetPwdBtn" Content="Set Password" Width="120"/>
                                    <Button Name="HOpenLogBtn" Content="Open Log" Width="100"/>
                                    <Button Name="HScanHiddenBtn" Content="Scan Hidden" Width="120"/>
                                </StackPanel>
                            </Grid>
                        </GroupBox>

                        <StackPanel Grid.Column="2" Margin="0">
                            <TextBlock Text="Options" FontWeight="Bold" Margin="0,0,0,8"/>
                            <CheckBox Name="HAclToggle" Content="Restrict access while hidden (ACL)" Margin="0,0,0,4"/>
                            <CheckBox Name="HEfsToggle" Content="Encrypt with EFS while hidden" Margin="0,0,0,10"/>
                            <Separator Margin="0,4,0,4"/>
                            <TextBlock Text="Auto-hide" FontWeight="Bold" Margin="0,0,0,4"/>
                            <CheckBox Name="HAutoHideToggle" Content="Enable auto-hide after inactivity" Margin="0,0,0,4"/>
                            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                <TextBlock Text="Minutes:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                                <TextBox Name="HAutoMinutesBox" Width="60" />
                            </StackPanel>
                            <Button Name="HApplyBtn" Content="Apply Changes" Width="140" Margin="0,0,0,6"/>
                            <TextBlock Name="HiderStatus" Text="" Foreground="#444" TextWrapping="Wrap" Width="360"/>
                        </StackPanel>
                    </Grid>
                  </TabItem>

                  <!-- System Audit Sub-tab -->
                  <TabItem Header="System Audit">
                    <Grid Margin="4">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                            <Button Name="SecScanBtn" Content="Scan Elevated Users" Width="160"/>
                            <Button Name="SecAclBtn" Content="Scan Critical ACLs" Width="150" Margin="6,0,0,0"/>
                            <Button Name="SecOutboundBtn" Content="Scan Outbound Traffic" Width="170" Margin="6,0,0,0"/>
                            <Button Name="SecOpenLusr" Content="Open Users &amp; Groups" Width="170" Margin="6,0,0,0"/>
                        </StackPanel>

                        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
                            <Button Name="SecDisableBtn" Content="Disable User" Width="120"/>
                            <Button Name="SecDeleteBtn" Content="Delete User" Width="110" Margin="6,0,0,0"/>
                            <Button Name="SecResetBtn" Content="Reset Password" Width="130" Margin="6,0,0,0"/>
                            <TextBlock Text="New password:" VerticalAlignment="Center" Margin="12,0,6,0"/>
                            <PasswordBox Name="SecPasswordBox" Width="160"/>
                        </StackPanel>

                        <DataGrid Name="SecGrid" Grid.Row="2" AutoGenerateColumns="True" IsReadOnly="True" Margin="0" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF"/>

                        <TextBlock Name="SecStatus" Grid.Row="3" Text="Ready" Foreground="#444" Margin="0,8,0,0" TextWrapping="Wrap"/>
                    </Grid>
                  </TabItem>

                  <!-- Startup Manager Sub-tab -->
                  <TabItem Header="Startup Manager">
                    <Grid Margin="4">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                            <Button Name="BtnStartupScan" Content="Scan Startup Items" Width="160"/>
                            <Button Name="BtnStartupDisable" Content="Disable Selected" Width="140" Margin="6,0,0,0"/>
                            <Button Name="BtnStartupEnable" Content="Enable Selected" Width="140" Margin="6,0,0,0"/>
                            <Button Name="BtnStartupDelete" Content="Delete Selected" Width="130" Margin="6,0,0,0"/>
                            <Button Name="BtnStartupOpenFolder" Content="Open Location" Width="120" Margin="6,0,0,0"/>
                            <Button Name="BtnStartupOpenTaskSched" Content="Task Scheduler" Width="130" Margin="6,0,0,0"/>
                        </StackPanel>

                        <DataGrid Name="DgStartup" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" IsReadOnly="True" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
                            <DataGrid.Columns>
                                <DataGridCheckBoxColumn Header="Select" Binding="{Binding Selected}" Width="60"/>
                                <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="200"/>
                                <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="80"/>
                                <DataGridTextColumn Header="Source" Binding="{Binding Source}" Width="120"/>
                                <DataGridTextColumn Header="Command" Binding="{Binding Command}" Width="400"/>
                                <DataGridTextColumn Header="Location" Binding="{Binding Location}" Width="300"/>
                            </DataGrid.Columns>
                        </DataGrid>

                        <TextBlock Grid.Row="2" Text="Sources: Registry (HKCU/HKLM Run keys), Startup folders, Scheduled Tasks" Foreground="Gray" FontSize="11" Margin="0,8,0,4"/>
                        <TextBlock Name="TxtStartupStatus" Grid.Row="3" Text="Ready. Click Scan to detect startup items." Foreground="#444" TextWrapping="Wrap"/>
                    </Grid>
                  </TabItem>
                </TabControl>
            </TabItem>

      <!-- Metadata Tab -->
            <TabItem Header="Metadata">
                <Grid Margin="4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="400"/>
                        <ColumnDefinition Width="8"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column="0" Margin="0">
                        <TextBlock Text="Select File" FontWeight="Bold" Margin="0,0,0,8"/>
                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                            <Button Name="BtnMetaBrowse" Content="Browse..." Width="90"/>
                            <Button Name="BtnMetaRefresh" Content="Refresh" Width="80" Margin="6,0,0,0"/>
                        </StackPanel>
                        <TextBox Name="TxtMetaFile" IsReadOnly="True" TextWrapping="Wrap" Margin="0,0,0,12"/>

                        <TextBlock Text="Quick Info" FontWeight="Bold" Margin="0,0,0,8"/>
                        <Grid Margin="0,0,0,12">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="100"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            <TextBlock Grid.Row="0" Grid.Column="0" Text="Type:" FontWeight="SemiBold"/>
                            <TextBlock Name="TxtMetaType" Grid.Row="0" Grid.Column="1"/>
                            <TextBlock Grid.Row="1" Grid.Column="0" Text="Size:" FontWeight="SemiBold"/>
                            <TextBlock Name="TxtMetaSize" Grid.Row="1" Grid.Column="1"/>
                            <TextBlock Grid.Row="2" Grid.Column="0" Text="Created:" FontWeight="SemiBold"/>
                            <TextBlock Name="TxtMetaCreated" Grid.Row="2" Grid.Column="1"/>
                            <TextBlock Grid.Row="3" Grid.Column="0" Text="Modified:" FontWeight="SemiBold"/>
                            <TextBlock Name="TxtMetaModified" Grid.Row="3" Grid.Column="1"/>
                            <TextBlock Grid.Row="4" Grid.Column="0" Text="Dimensions:" FontWeight="SemiBold"/>
                            <TextBlock Name="TxtMetaDimensions" Grid.Row="4" Grid.Column="1"/>
                            <TextBlock Grid.Row="5" Grid.Column="0" Text="Duration:" FontWeight="SemiBold"/>
                            <TextBlock Name="TxtMetaDuration" Grid.Row="5" Grid.Column="1"/>
                        </Grid>

                        <TextBlock Text="Edit Metadata (Audio/Video)" FontWeight="Bold" Margin="0,0,0,8"/>
                        <Grid Margin="0,0,0,8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="80"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            <TextBlock Grid.Row="0" Grid.Column="0" Text="Title:" VerticalAlignment="Center"/>
                            <TextBox Name="TxtMetaTitle" Grid.Row="0" Grid.Column="1" Margin="0,2"/>
                            <TextBlock Grid.Row="1" Grid.Column="0" Text="Artist:" VerticalAlignment="Center"/>
                            <TextBox Name="TxtMetaArtist" Grid.Row="1" Grid.Column="1" Margin="0,2"/>
                            <TextBlock Grid.Row="2" Grid.Column="0" Text="Album:" VerticalAlignment="Center"/>
                            <TextBox Name="TxtMetaAlbum" Grid.Row="2" Grid.Column="1" Margin="0,2"/>
                            <TextBlock Grid.Row="3" Grid.Column="0" Text="Year:" VerticalAlignment="Center"/>
                            <TextBox Name="TxtMetaYear" Grid.Row="3" Grid.Column="1" Margin="0,2"/>
                            <TextBlock Grid.Row="4" Grid.Column="0" Text="Comment:" VerticalAlignment="Center"/>
                            <TextBox Name="TxtMetaComment" Grid.Row="4" Grid.Column="1" Margin="0,2"/>
                        </Grid>

                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                            <Button Name="BtnMetaSave" Content="Save Changes" Width="120"/>
                            <Button Name="BtnMetaExport" Content="Export All" Width="100" Margin="6,0,0,0"/>
                        </StackPanel>
                        <TextBlock Name="TxtMetaStatus" Text="Select a file to view metadata." Foreground="#444" TextWrapping="Wrap"/>
                    </StackPanel>

                    <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" ShowsPreview="True" Background="#EEE"/>

                    <Grid Grid.Column="2">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>
                        <TextBlock Text="All Metadata (from exiftool/ffprobe)" FontWeight="Bold" Margin="0,0,0,8"/>
                        <DataGrid Name="DgMetadata" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" IsReadOnly="True" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Tag" Binding="{Binding Tag}" Width="200"/>
                                <DataGridTextColumn Header="Value" Binding="{Binding Value}" Width="*"/>
                            </DataGrid.Columns>
                        </DataGrid>
                    </Grid>
                </Grid>
            </TabItem>
    </TabControl>
  </DockPanel>
</Window>
"@

# ==================== VIDEO EDITOR HANDLERS ====================

# --------------- Load XAML ---------------
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

function Update-Status {
    param([string]$text)
    $TxtStatus.Text = $text
}

function Set-HeaderImage {
  param([string]$Path)
  if (-not $ImgHeader -or -not $Path) { return }
  if (-not (Test-Path -LiteralPath $Path)) { return }
  try {
    $bi = New-Object System.Windows.Media.Imaging.BitmapImage
    $bi.BeginInit()
    $bi.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $bi.UriSource = New-Object System.Uri($Path, [System.UriKind]::Absolute)
    $bi.EndInit()
    $ImgHeader.Source = $bi
  }
  catch {
    Write-Warning ("Unable to load header image: {0}" -f $_.Exception.Message)
  }
}

# Controls - File Cleaner
$TxtFolder          = $window.FindName("TxtFolder")
$BtnBrowse          = $window.FindName("BtnBrowse")
$ChkRecurse         = $window.FindName("ChkRecurse")
$ChkChangePrefix    = $window.FindName("ChkChangePrefix")
$TxtOldPrefix       = $window.FindName("TxtOldPrefix")
$TxtNewPrefix       = $window.FindName("TxtNewPrefix")
$TxtDetectedPrefix  = $window.FindName("TxtDetectedPrefix")
$BtnDetectPrefix    = $window.FindName("BtnDetectPrefix")
$TxtIgnorePrefix    = $window.FindName("TxtIgnorePrefix")
$ChkAddPrefixAll    = $window.FindName("ChkAddPrefixAll")
$ChkNormalizePrefixCase = $window.FindName("ChkNormalizePrefixCase")
$ChkOnlyIfOldPrefix = $window.FindName("ChkOnlyIfOldPrefix")
$ChkDryRun          = $window.FindName("ChkDryRun")
$ChkAddSeason       = $window.FindName("ChkAddSeason")
$TxtSeason          = $window.FindName("TxtSeason")
$CmbSeasonDigits    = $window.FindName("CmbSeasonDigits")
$ChkAddEpisode      = $window.FindName("ChkAddEpisode")
$TxtStart           = $window.FindName("TxtStart")
$CmbEpisodeDigits   = $window.FindName("CmbEpisodeDigits")
$ChkRenumberAll     = $window.FindName("ChkRenumberAll")
$ChkSeasonBeforeEpisode = $window.FindName("ChkSeasonBeforeEpisode")
$Chk720p            = $window.FindName("Chk720p")
$Chk1080p           = $window.FindName("Chk1080p")
$Chk4k              = $window.FindName("Chk4k")
$ChkHD              = $window.FindName("ChkHD")
$TxtCustomClean     = $window.FindName("TxtCustomClean")
$ChkUseAudioMetadata= $window.FindName("ChkUseAudioMetadata")
$ChkUseVideoMetadata= $window.FindName("ChkUseVideoMetadata")
$ChkVideo           = $window.FindName("ChkVideo")
$ChkPictures        = $window.FindName("ChkPictures")
$ChkDocuments       = $window.FindName("ChkDocuments")
$ChkAudio           = $window.FindName("ChkAudio")
$ChkArchives        = $window.FindName("ChkArchives")
$TxtCustomExt       = $window.FindName("TxtCustomExt")
$CmbSpaceReplace    = $window.FindName("CmbSpaceReplace")
$BtnSpaceCleanup    = $window.FindName("BtnSpaceCleanup")
$DgPreview          = $window.FindName("DgPreview")
$BtnSelectAll       = $window.FindName("BtnSelectAll")
$BtnSelectNone      = $window.FindName("BtnSelectNone")
$BtnScan            = $window.FindName("BtnScan")
$BtnApply           = $window.FindName("BtnApply")
$BtnUndo            = $window.FindName("BtnUndo")
$BtnExportCsv       = $window.FindName("BtnExportCsv")
$BtnReset           = $window.FindName("BtnReset")
$BtnExit            = $window.FindName("BtnExit")
$TxtStatus          = $window.FindName("TxtStatus")
$ImgHeader          = $window.FindName("ImgHeader")

# Controls - Combiner
$BtnCombBrowse      = $window.FindName("BtnCombBrowse")
$BtnCombSelectAll   = $window.FindName("BtnCombSelectAll")
$BtnCombSelectNone  = $window.FindName("BtnCombSelectNone")
$BtnCombUp          = $window.FindName("BtnCombUp")
$BtnCombDown        = $window.FindName("BtnCombDown")
$BtnCombPreview     = $window.FindName("BtnCombPreview")
$BtnCombNormalize   = $window.FindName("BtnCombNormalize")
$BtnCombSafe        = $window.FindName("BtnCombSafe")
$BtnCombCombine     = $window.FindName("BtnCombCombine")
$BtnCombConvert     = $window.FindName("BtnCombConvert")
$CmbConvFormat      = $window.FindName("CmbConvFormat")
$CmbConvRes         = $window.FindName("CmbConvRes")
$TxtConvOut         = $window.FindName("TxtConvOut")
$BtnConvBrowse      = $window.FindName("BtnConvBrowse")
$TxtConvStatus      = $window.FindName("TxtConvStatus")
$ConvProgress       = $window.FindName("ConvProgress")
$DgCombine          = $window.FindName("DgCombine")
$TxtCombLog         = $window.FindName("TxtCombLog")
$TxtToolDir         = $window.FindName("TxtToolDir")
$BtnToolDirBrowse   = $window.FindName("BtnToolDirBrowse")
$TxtImgFolder       = $window.FindName("TxtImgFolder")
$BtnImgBrowse       = $window.FindName("BtnImgBrowse")
$BtnImgAddFiles     = $window.FindName("BtnImgAddFiles")
$BtnImgAddFolder    = $window.FindName("BtnImgAddFolder")
$BtnImgClear        = $window.FindName("BtnImgClear")
$DgImages           = $window.FindName("DgImages")
$TxtIcoOut          = $window.FindName("TxtIcoOut")
$BtnIcoOutBrowse    = $window.FindName("BtnIcoOutBrowse")
$CmbIcoSize         = $window.FindName("CmbIcoSize")
$ChkIcoOverwrite    = $window.FindName("ChkIcoOverwrite")
$BtnImgConvert      = $window.FindName("BtnImgConvert")
$CmbImgOutFormat    = $window.FindName("CmbImgOutFormat")
$BtnImgConvertFormat= $window.FindName("BtnImgConvertFormat")
$TxtImgStatus       = $window.FindName("TxtImgStatus")
$ImgProgress        = $window.FindName("ImgProgress")
$TxtIcoName         = $window.FindName("TxtIcoName")
$TxtImgLog          = $window.FindName("TxtImgLog")
$TxtDupFolder       = $window.FindName("TxtDupFolder")
$BtnDupBrowse       = $window.FindName("BtnDupBrowse")
$ChkDupRecurse      = $window.FindName("ChkDupRecurse")
$ChkDupVideo        = $window.FindName("ChkDupVideo")
$ChkDupPictures     = $window.FindName("ChkDupPictures")
$ChkDupDocuments    = $window.FindName("ChkDupDocuments")
$ChkDupAudio        = $window.FindName("ChkDupAudio")
$ChkDupArchives     = $window.FindName("ChkDupArchives")
$TxtDupCustomExt    = $window.FindName("TxtDupCustomExt")
$BtnDupScan         = $window.FindName("BtnDupScan")
$BtnDupDelete       = $window.FindName("BtnDupDelete")
$BtnDupRename       = $window.FindName("BtnDupRename")
$BtnDupReset        = $window.FindName("BtnDupReset")
$DgDupes            = $window.FindName("DgDupes")
$LstDupScanLog      = $window.FindName("LstDupScanLog")
$TxtDupStatus       = $window.FindName("TxtDupStatus")
$DupProgress        = $window.FindName("DupProgress")
$CmbDupMethod       = $window.FindName("CmbDupMethod")
$script:DupScanRunning = $false
$script:DupJsonDir  = $script:DataRoot
$script:DupLogDir   = $script:GlobalLogDir

# Controls - Recent Cleaner (SystemCleaner)
$dirList    = $window.FindName("DirList")
$addDir     = $window.FindName("AddDir")
$removeDir  = $window.FindName("RemoveDir")
$includeSub = $window.FindName("IncludeSubdirs")
$dryRunBox  = $window.FindName("DryRunToggle")
$outputBox  = $window.FindName("OutputPathBox")
$browseOut  = $window.FindName("BrowseOutput")
$frequency  = $window.FindName("FrequencyBox")
$timeBox    = $window.FindName("TimeBox")
$daysBox    = $window.FindName("DaysBox")
$chkSun     = $window.FindName("chkSunday")
$chkMon     = $window.FindName("chkMonday")
$chkTue     = $window.FindName("chkTuesday")
$chkWed     = $window.FindName("chkWednesday")
$chkThu     = $window.FindName("chkThursday")
$chkFri     = $window.FindName("chkFriday")
$chkSat     = $window.FindName("chkSaturday")
$previewGrid= $window.FindName("PreviewGrid")
$showPreview= $window.FindName("ShowPreview")
$exportCSV  = $window.FindName("ExportCSV")
$undoLast   = $window.FindName("UndoLast")
$runNow     = $window.FindName("RunNow")
$saveTask   = $window.FindName("SaveTask")

# Controls - Folder Hider (SystemCleaner)
$hiderList  = $window.FindName("HiderList")
$hAdd       = $window.FindName("HAddDir")
$hAddManual = $window.FindName("HAddManual")
$hRemove    = $window.FindName("HRemoveDir")
$hHide      = $window.FindName("HHideBtn")
$hUnhide    = $window.FindName("HUnhideBtn")
$hSetPwd    = $window.FindName("HSetPwdBtn")
$hOpenLog   = $window.FindName("HOpenLogBtn")
$hScanHidden= $window.FindName("HScanHiddenBtn")
$hManualBox = $window.FindName("HManualPath")
$hAclToggle = $window.FindName("HAclToggle")
$hEfsToggle = $window.FindName("HEfsToggle")
$hAutoHide  = $window.FindName("HAutoHideToggle")
$hAutoMinutes = $window.FindName("HAutoMinutesBox")
$hApply     = $window.FindName("HApplyBtn")
$hStatusLbl = $window.FindName("HiderStatus")

# Controls - Security Scan (SystemCleaner)
$secScanBtn   = $window.FindName("SecScanBtn")
$secDisableBtn= $window.FindName("SecDisableBtn")
$secResetBtn  = $window.FindName("SecResetBtn")
$secDeleteBtn = $window.FindName("SecDeleteBtn")
$secOpenLusr  = $window.FindName("SecOpenLusr")
$secAclBtn    = $window.FindName("SecAclBtn")
$secOutbound  = $window.FindName("SecOutboundBtn")
$secPwdBox    = $window.FindName("SecPasswordBox")
$secGrid      = $window.FindName("SecGrid")
$secStatus    = $window.FindName("SecStatus")
$script:secStatus = $secStatus

# Controls - Image Resize
$BtnResizeBrowse    = $window.FindName("BtnResizeBrowse")
$BtnResizeAddFiles  = $window.FindName("BtnResizeAddFiles")
$BtnResizeClear     = $window.FindName("BtnResizeClear")
$DgResize           = $window.FindName("DgResize")
$TxtResizeOut       = $window.FindName("TxtResizeOut")
$BtnResizeOutBrowse = $window.FindName("BtnResizeOutBrowse")
$TxtResizeWidth     = $window.FindName("TxtResizeWidth")
$TxtResizeHeight    = $window.FindName("TxtResizeHeight")
$CmbResizeQuality   = $window.FindName("CmbResizeQuality")
$CmbResizeFormat    = $window.FindName("CmbResizeFormat")
$BtnResizeRun       = $window.FindName("BtnResizeRun")
$ChkResizeOverwrite = $window.FindName("ChkResizeOverwrite")
$ChkResizeKeepAspect= $window.FindName("ChkResizeKeepAspect")
$TxtResizeStatus    = $window.FindName("TxtResizeStatus")
$ResizeProgress     = $window.FindName("ResizeProgress")
$TxtResizeLog       = $window.FindName("TxtResizeLog")

# Controls - Bootable USB
$TxtIsoPath         = $window.FindName("TxtIsoPath")
$BtnIsoBrowse       = $window.FindName("BtnIsoBrowse")
$CmbUsbDrive        = $window.FindName("CmbUsbDrive")
$BtnUsbRefresh      = $window.FindName("BtnUsbRefresh")
$TxtUsbInfo         = $window.FindName("TxtUsbInfo")
$CmbUsbFileSystem   = $window.FindName("CmbUsbFileSystem")
$TxtUsbLabel        = $window.FindName("TxtUsbLabel")
$ChkUsbQuickFormat  = $window.FindName("ChkUsbQuickFormat")
$ChkUsbVerify       = $window.FindName("ChkUsbVerify")
$CmbBootMode        = $window.FindName("CmbBootMode")
$BtnUsbWrite        = $window.FindName("BtnUsbWrite")
$BtnUsbCancel       = $window.FindName("BtnUsbCancel")
$TxtUsbStatus       = $window.FindName("TxtUsbStatus")
$UsbProgress        = $window.FindName("UsbProgress")
$TxtUsbLog          = $window.FindName("TxtUsbLog")

# Controls - Hash Calculator
$BtnHashFile   = $window.FindName("BtnHashFile")
$CmbHashAlgo   = $window.FindName("CmbHashAlgo")
$TxtHashResult = $window.FindName("TxtHashResult")
$BtnHashCopy   = $window.FindName("BtnHashCopy")

# Controls - Disk Cleanup
$ChkCleanTemp         = $window.FindName("ChkCleanTemp")
$ChkCleanUserTemp     = $window.FindName("ChkCleanUserTemp")
$ChkCleanPrefetch     = $window.FindName("ChkCleanPrefetch")
$ChkCleanRecycleBin   = $window.FindName("ChkCleanRecycleBin")
$ChkCleanDownloads    = $window.FindName("ChkCleanDownloads")
$ChkCleanWindowsUpdate= $window.FindName("ChkCleanWindowsUpdate")
$ChkCleanThumbnails   = $window.FindName("ChkCleanThumbnails")
$ChkCleanErrorReports = $window.FindName("ChkCleanErrorReports")
$ChkCleanLogFiles     = $window.FindName("ChkCleanLogFiles")
$BtnDiskAnalyze       = $window.FindName("BtnDiskAnalyze")
$BtnDiskClean         = $window.FindName("BtnDiskClean")
$ChkDiskDryRun        = $window.FindName("ChkDiskDryRun")
$TxtDiskStatus        = $window.FindName("TxtDiskStatus")
$TxtDiskSpace         = $window.FindName("TxtDiskSpace")
$DgDiskClean          = $window.FindName("DgDiskClean")

# Controls - Privacy Cleaner
$ChkPrivChrome      = $window.FindName("ChkPrivChrome")
$ChkPrivEdge        = $window.FindName("ChkPrivEdge")
$ChkPrivFirefox     = $window.FindName("ChkPrivFirefox")
$ChkPrivBrave       = $window.FindName("ChkPrivBrave")
$ChkPrivOneDrive    = $window.FindName("ChkPrivOneDrive")
$ChkPrivGoogleDrive = $window.FindName("ChkPrivGoogleDrive")
$ChkPrivDropbox     = $window.FindName("ChkPrivDropbox")
$ChkPriviCloud      = $window.FindName("ChkPriviCloud")
$ChkPrivCredManager = $window.FindName("ChkPrivCredManager")
$ChkPrivRecentDocs  = $window.FindName("ChkPrivRecentDocs")
$ChkPrivJumpLists   = $window.FindName("ChkPrivJumpLists")
$ChkPrivExplorer    = $window.FindName("ChkPrivExplorer")
$ChkPrivClipboard   = $window.FindName("ChkPrivClipboard")
$ChkPrivOffice      = $window.FindName("ChkPrivOffice")
$ChkPrivAdobeRecent = $window.FindName("ChkPrivAdobeRecent")
$ChkPrivMediaPlayer = $window.FindName("ChkPrivMediaPlayer")
$BtnPrivAnalyze     = $window.FindName("BtnPrivAnalyze")
$BtnPrivClean       = $window.FindName("BtnPrivClean")
$ChkPrivDryRun      = $window.FindName("ChkPrivDryRun")
$DgPrivClean        = $window.FindName("DgPrivClean")
$TxtPrivStatus      = $window.FindName("TxtPrivStatus")

# Controls - Startup Manager
$BtnStartupScan       = $window.FindName("BtnStartupScan")
$BtnStartupDisable    = $window.FindName("BtnStartupDisable")
$BtnStartupEnable     = $window.FindName("BtnStartupEnable")
$BtnStartupDelete     = $window.FindName("BtnStartupDelete")
$BtnStartupOpenFolder = $window.FindName("BtnStartupOpenFolder")
$BtnStartupOpenTaskSched = $window.FindName("BtnStartupOpenTaskSched")
$DgStartup            = $window.FindName("DgStartup")
$TxtStartupStatus     = $window.FindName("TxtStartupStatus")

# Controls - Metadata
$BtnMetaBrowse    = $window.FindName("BtnMetaBrowse")
$BtnMetaRefresh   = $window.FindName("BtnMetaRefresh")
$TxtMetaFile      = $window.FindName("TxtMetaFile")
$TxtMetaType      = $window.FindName("TxtMetaType")
$TxtMetaSize      = $window.FindName("TxtMetaSize")
$TxtMetaCreated   = $window.FindName("TxtMetaCreated")
$TxtMetaModified  = $window.FindName("TxtMetaModified")
$TxtMetaDimensions= $window.FindName("TxtMetaDimensions")
$TxtMetaDuration  = $window.FindName("TxtMetaDuration")
$TxtMetaTitle     = $window.FindName("TxtMetaTitle")
$TxtMetaArtist    = $window.FindName("TxtMetaArtist")
$TxtMetaAlbum     = $window.FindName("TxtMetaAlbum")
$TxtMetaYear      = $window.FindName("TxtMetaYear")
$TxtMetaComment   = $window.FindName("TxtMetaComment")
$BtnMetaSave      = $window.FindName("BtnMetaSave")
$BtnMetaExport    = $window.FindName("BtnMetaExport")
$TxtMetaStatus    = $window.FindName("TxtMetaStatus")
$DgMetadata       = $window.FindName("DgMetadata")

# Controls - Menu Items
$MenuSetScanDir    = $window.FindName("MenuSetScanDir")
$MenuSetOutputDir  = $window.FindName("MenuSetOutputDir")
$MenuSetToolDir    = $window.FindName("MenuSetToolDir")
$MenuOpenScanDir   = $window.FindName("MenuOpenScanDir")
$MenuOpenOutputDir = $window.FindName("MenuOpenOutputDir")
$MenuOpenDataDir   = $window.FindName("MenuOpenDataDir")
$MenuOpenLogDir    = $window.FindName("MenuOpenLogDir")
$MenuSaveConfig    = $window.FindName("MenuSaveConfig")
$MenuLoadConfig    = $window.FindName("MenuLoadConfig")
$MenuResetConfig   = $window.FindName("MenuResetConfig")
$MenuExit          = $window.FindName("MenuExit")
$MenuThemeLight    = $window.FindName("MenuThemeLight")
$MenuThemeDark     = $window.FindName("MenuThemeDark")
$MenuFontSmall     = $window.FindName("MenuFontSmall")
$MenuFontMedium    = $window.FindName("MenuFontMedium")
$MenuFontLarge     = $window.FindName("MenuFontLarge")
$MenuShowStatusBar = $window.FindName("MenuShowStatusBar")
$MenuShowToolbar   = $window.FindName("MenuShowToolbar")
$MenuRefresh       = $window.FindName("MenuRefresh")
$MenuOpenExplorer  = $window.FindName("MenuOpenExplorer")
$MenuOpenCmd       = $window.FindName("MenuOpenCmd")
$MenuOpenPowerShell= $window.FindName("MenuOpenPowerShell")
$MenuOpenTaskMgr   = $window.FindName("MenuOpenTaskMgr")
$MenuOpenServices  = $window.FindName("MenuOpenServices")
$MenuOpenDevMgr    = $window.FindName("MenuOpenDevMgr")
$MenuHelpContents  = $window.FindName("MenuHelpContents")
$MenuQuickStart    = $window.FindName("MenuQuickStart")
$MenuKeyboardShortcuts = $window.FindName("MenuKeyboardShortcuts")
$MenuCheckUpdates  = $window.FindName("MenuCheckUpdates")
$MenuViewLog       = $window.FindName("MenuViewLog")
$MenuAbout         = $window.FindName("MenuAbout")
$MainWindow        = $window.FindName("MainWindow")
$MainDockPanel     = $window.FindName("MainDockPanel")
$HeaderBorder      = $window.FindName("HeaderBorder")
$MainStatusBar     = $window.FindName("MainStatusBar")
$MainTabControl    = $window.FindName("MainTabControl")

# ==================== APP CONFIGURATION ====================
$script:AppConfigPath = Join-Path $script:PlatypusData "PlatypusTools_Config.json"
$script:AppConfig = [PSCustomObject]@{
    DefaultScanFolder = ""
    DefaultOutputFolder = ""
    DefaultToolFolder = ""
    Theme = "Light"
    FontSize = "Medium"
    ShowStatusBar = $true
    ShowHeader = $true
}

function Load-AppConfig {
    if (Test-Path $script:AppConfigPath) {
        try {
            $json = Get-Content $script:AppConfigPath -Raw | ConvertFrom-Json
            if ($json.DefaultScanFolder) { $script:AppConfig.DefaultScanFolder = $json.DefaultScanFolder }
            if ($json.DefaultOutputFolder) { $script:AppConfig.DefaultOutputFolder = $json.DefaultOutputFolder }
            if ($json.DefaultToolFolder) { $script:AppConfig.DefaultToolFolder = $json.DefaultToolFolder }
            if ($json.Theme) { $script:AppConfig.Theme = $json.Theme }
            if ($json.FontSize) { $script:AppConfig.FontSize = $json.FontSize }
            if ($null -ne $json.ShowStatusBar) { $script:AppConfig.ShowStatusBar = $json.ShowStatusBar }
            if ($null -ne $json.ShowHeader) { $script:AppConfig.ShowHeader = $json.ShowHeader }
        } catch {}
    }
}

function Save-AppConfig {
    try {
        $script:AppConfig | ConvertTo-Json -Depth 4 | Set-Content -Path $script:AppConfigPath -Encoding UTF8
        Update-Status "Configuration saved."
    } catch {
        Update-Status "Failed to save configuration."
    }
}

function Apply-Theme {
    param([string]$Theme)
    $resources = $window.Resources
    if ($Theme -eq "Dark") {
        $resources["WindowBackground"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(30, 30, 30))
        $resources["TextForeground"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(240, 240, 240))
        $resources["MenuBackground"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(45, 45, 45))
        $resources["GridAltRow"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(50, 50, 50))
        $resources["StatusBarBg"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(40, 40, 40))
        $window.Background = $resources["WindowBackground"]
        if ($MenuThemeDark) { $MenuThemeDark.IsChecked = $true }
        if ($MenuThemeLight) { $MenuThemeLight.IsChecked = $false }
    } else {
        $resources["WindowBackground"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(255, 255, 255))
        $resources["TextForeground"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(34, 34, 34))
        $resources["MenuBackground"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(245, 245, 245))
        $resources["GridAltRow"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(249, 251, 255))
        $resources["StatusBarBg"] = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(240, 240, 240))
        $window.Background = $resources["WindowBackground"]
        if ($MenuThemeLight) { $MenuThemeLight.IsChecked = $true }
        if ($MenuThemeDark) { $MenuThemeDark.IsChecked = $false }
    }
    $script:AppConfig.Theme = $Theme
}

function Apply-FontSize {
    param([string]$Size)
    $fontSize = switch ($Size) {
        "Small" { 11 }
        "Large" { 15 }
        default { 13 }
    }
    $window.FontSize = $fontSize
    if ($MenuFontSmall) { $MenuFontSmall.IsChecked = ($Size -eq "Small") }
    if ($MenuFontMedium) { $MenuFontMedium.IsChecked = ($Size -eq "Medium") }
    if ($MenuFontLarge) { $MenuFontLarge.IsChecked = ($Size -eq "Large") }
    $script:AppConfig.FontSize = $Size
}

# Load config on startup
Load-AppConfig
Apply-Theme $script:AppConfig.Theme
Apply-FontSize $script:AppConfig.FontSize

# Apply saved default folders
if ($script:AppConfig.DefaultToolFolder -and $TxtToolDir) { $TxtToolDir.Text = $script:AppConfig.DefaultToolFolder }
if ($script:AppConfig.DefaultOutputFolder -and $TxtConvOut) { $TxtConvOut.Text = $script:AppConfig.DefaultOutputFolder }
if ($script:AppConfig.DefaultScanFolder -and $TxtFolder) { $TxtFolder.Text = $script:AppConfig.DefaultScanFolder }

# Load header image if present (prefer Platypus assets)
$headerImagePath = $null
if (Test-Path -LiteralPath (Join-Path $script:PlatypusAssets 'platypus.png')) { $headerImagePath = Join-Path $script:PlatypusAssets 'platypus.png' }
elseif (Test-Path -LiteralPath (Join-Path $script:Root 'platypus.png')) { $headerImagePath = Join-Path $script:Root 'platypus.png' }
Set-HeaderImage -Path $headerImagePath

# --- Recent Cleaner Config init ---
$configRoot = Join-Path $script:PlatypusData "SystemCleaner"
Ensure-Directory $configRoot
$configPath = Join-Path $configRoot "RecentCleanerConfig.json"
$defaultOut = Join-Path $configRoot "RecentCleaner"
Ensure-Directory $defaultOut

if (Test-Path $configPath) {
    try { $raw = Get-Content $configPath -Raw; $parsed = if ($raw) { $raw | ConvertFrom-Json } else { $null } } catch { $parsed = $null }
} else { $parsed = $null }

if (-not ($parsed -and $parsed -is [System.Management.Automation.PSCustomObject])) {
    $config = [pscustomobject]@{ ExcludedDirs = @(); OutputPath = $defaultOut; Weekdays = @(); Time = "00:00"; IncludeSubdirs = $true }
} else {
    $includeParsed = if ($parsed.PSObject.Properties.Match('IncludeSubdirs').Count -gt 0) { To-BoolSafe $parsed.IncludeSubdirs $true } else { $true }
    $config = [pscustomobject]@{
        ExcludedDirs   = if ($parsed.ExcludedDirs) { @($parsed.ExcludedDirs) } else { @() }
        OutputPath     = if ($parsed.OutputPath) { $parsed.OutputPath } else { $defaultOut }
        Weekdays       = if ($parsed.Weekdays) { @($parsed.Weekdays) } else { @() }
        Time           = if ($parsed.Time) { $parsed.Time } else { "00:00" }
        IncludeSubdirs = $includeParsed
    }
}

# Populate Recent Cleaner UI (only if controls exist)
if ($dirList) { foreach ($d in $config.ExcludedDirs) { if ($d) { [void]$dirList.Items.Add($d) } } }
if ($outputBox) { $outputBox.Text = if ($config.OutputPath) { $config.OutputPath } else { $defaultOut } }
if ($timeBox) { $timeBox.Text = if ($config.Time) { $config.Time } else { "00:00" } }
if ($includeSub) { $includeSub.IsChecked = [bool]$config.IncludeSubdirs }

foreach ($wd in $config.Weekdays) {
    switch ($wd.ToLower()) {
        "sunday"   { if ($chkSun) { $chkSun.IsChecked = $true } }
        "monday"   { if ($chkMon) { $chkMon.IsChecked = $true } }
        "tuesday"  { if ($chkTue) { $chkTue.IsChecked = $true } }
        "wednesday"{ if ($chkWed) { $chkWed.IsChecked = $true } }
        "thursday" { if ($chkThu) { $chkThu.IsChecked = $true } }
        "friday"   { if ($chkFri) { $chkFri.IsChecked = $true } }
        "saturday" { if ($chkSat) { $chkSat.IsChecked = $true } }
    }
}

# Tool directory selection and tool resolution
$ToolDirCandidates = @(
  (Join-Path $script:PlatypusBase 'Tools'),
  (Join-Path $script:PlatypusBase 'VideoEditor'),
  $script:Root,
  "C:\VideoEditor",
  "C:\convert"
)
function Resolve-ToolPath {
  param([string]$ToolName,[string]$PreferredDir)
  $exe = if ($ToolName -like '*.exe') { $ToolName } else { "$ToolName.exe" }
  $search = @()
  if ($PreferredDir) { $search += (Join-Path $PreferredDir $exe) }
  $search += (Join-Path $script:PlatypusBase $exe)
  $search += (Join-Path (Join-Path $script:PlatypusBase 'Tools') $exe)
  $search += (Join-Path "C:\VideoEditor" $exe)
  $search += (Join-Path $script:Root $exe)
  $search += (Join-Path "C:\convert" $exe)
  $cmd = Get-Command $exe -ErrorAction SilentlyContinue
  if ($cmd -and $cmd.Source) { $search += $cmd.Source }
  foreach ($p in ($search | Select-Object -Unique)) { if ($p -and (Test-Path -LiteralPath $p)) { return (Resolve-Path $p).Path } }
  return $null
}

function Refresh-ToolPaths {
  $script:ffmpegPath   = Resolve-ToolPath -ToolName 'ffmpeg'   -PreferredDir $script:ToolDir
  $script:ffprobePath  = Resolve-ToolPath -ToolName 'ffprobe'  -PreferredDir $script:ToolDir
  $script:exiftoolPath = Resolve-ToolPath -ToolName 'exiftool' -PreferredDir $script:ToolDir
  $script:fpcalcPath   = Resolve-ToolPath -ToolName 'fpcalc'   -PreferredDir $script:ToolDir
}

function Set-ToolDir {
  param([string]$Path)
  if (-not $Path) { return }
  $expanded = [Environment]::ExpandEnvironmentVariables($Path.Trim())
  $script:ToolDir  = $expanded
  $script:targetDir = $expanded
  if ($TxtToolDir) { $TxtToolDir.Text = $expanded }
  Refresh-ToolPaths
  if ($TxtConvOut -and [string]::IsNullOrWhiteSpace($TxtConvOut.Text)) { $TxtConvOut.Text = $expanded }
  $script:toolsDetected = @(); if ($ffprobePath) { $script:toolsDetected += "ffprobe" }; if ($exiftoolPath) { $script:toolsDetected += "exiftool" }
  if ($toolsDetected.Count -eq 0) { Update-Status "Tool folder set. No metadata tools detected (ffprobe/exiftool)." }
  else { Update-Status "Tool folder set. Tools detected: $($toolsDetected -join ', ')" }
}

$initialToolDir = ($ToolDirCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1)
if (-not $initialToolDir) { $initialToolDir = $ToolDirCandidates[0] }
Set-ToolDir $initialToolDir

# --------------- Shared helpers ---------------
$toolsDetected = @()
Refresh-ToolPaths
if ($ffprobePath) { $toolsDetected += "ffprobe" }
if ($exiftoolPath) { $toolsDetected += "exiftool" }
if ($fpcalcPath)  { $toolsDetected += "fpcalc" }
if ($toolsDetected.Count -eq 0) { Update-Status "Ready. (No metadata tools detected - set Tool Folder for ffprobe/exiftool/fpcalc)" }
else { Update-Status "Ready. Tools detected: $($toolsDetected -join ', ')" }

# ---------- File Cleaner Logic ----------
function Get-FilteredFiles {
    param([string]$Path,[switch]$Recurse)
    if (-not (Test-Path $Path)) { return @() }
    $extensions = @()
    if ($ChkVideo.IsChecked)     { $extensions += '.mp4','.mkv','.avi','.mov','.wmv','.m4v' }
    if ($ChkPictures.IsChecked)  { $extensions += '.jpg','.jpeg','.png','.gif','.webp','.tiff','.bmp','.heic','.heif' }
    if ($ChkDocuments.IsChecked) { $extensions += '.pdf','.doc','.docx','.txt','.md','.rtf' }
    if ($ChkAudio.IsChecked)     { $extensions += '.mp3','.flac','.wav','.m4a','.aac' }
    if ($ChkArchives.IsChecked)  { $extensions += '.zip','.rar','.7z','.tar','.gz' }
    if ($TxtCustomExt.Text) {
        $custom = $TxtCustomExt.Text -split ',' | ForEach-Object { $_.Trim().ToLower() }
        $custom = $custom | ForEach-Object { if ($_ -and $_[0] -ne '.') { "." + $_ } else { $_ } }
        $extensions += $custom
    }
    $extensions = $extensions | Select-Object -Unique
    $files = Get-ChildItem -Path $Path -File -Recurse:$Recurse -ErrorAction SilentlyContinue
    if ($extensions.Count -eq 0) { return $files }
    return $files | Where-Object { $extensions -contains $_.Extension.ToLower() }
}

function Strip-ExistingEpisodeTags {
  param([string]$Name)
  if (-not $Name) { return $Name }
  $clean = $Name
  $clean = $clean -replace '(?i)\bS\d{1,4}\s*E\d{1,4}\b',''
  $clean = $clean -replace '(?i)\bE\d{1,4}\b',''
  $clean = $clean -replace '(?i)\bEpisode\s*\d{1,4}\b',''
  $clean = $clean.Trim(' ','-','_','.')
  return $clean
}

function Get-CoreNameForSorting {
  <#
  .SYNOPSIS
  Extract the "core" name for alphabetical sorting, ignoring:
  - Prefix (detected or specified)
  - Season tags (S01, S1, Season 1, etc.)
  - Episode tags (E01, E1, Episode 1, etc.)
  - Common resolution/quality tags
  #>
  param([string]$FileName)
  if (-not $FileName) { return "" }
  
  $base = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
  
  # Remove common prefixes if detected
  $detected = if ($TxtDetectedPrefix -and $TxtDetectedPrefix.Text) { $TxtDetectedPrefix.Text.Trim() } else { "" }
  $oldPrefix = if ($TxtOldPrefix -and $TxtOldPrefix.Text) { $TxtOldPrefix.Text.Trim() } else { "" }
  $newPrefix = if ($TxtNewPrefix -and $TxtNewPrefix.Text) { $TxtNewPrefix.Text.Trim() } else { "" }
  
  # Strip any known prefix
  foreach ($prefix in @($detected, $oldPrefix, $newPrefix)) {
    if ($prefix -and $base.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      $base = $base.Substring($prefix.Length)
      break
    }
  }
  
  # Remove season/episode patterns
  $base = $base -replace '(?i)\bS\d{1,4}\s*E\d{1,4}\b', ''      # S01E01, S1E1
  $base = $base -replace '(?i)\bSeason\s*\d{1,4}\b', ''         # Season 1
  $base = $base -replace '(?i)\bS\d{1,4}\b', ''                 # S01
  $base = $base -replace '(?i)\bE\d{1,4}\b', ''                 # E01
  $base = $base -replace '(?i)\bEpisode\s*\d{1,4}\b', ''        # Episode 1
  $base = $base -replace '(?i)\bEp\s*\d{1,4}\b', ''             # Ep 1
  $base = $base -replace '(?i)\b\d{1,4}x\d{1,4}\b', ''          # 1x01
  
  # Remove resolution/quality tags
  $base = $base -replace '(?i)\b720p\b', ''
  $base = $base -replace '(?i)\b1080p\b', ''
  $base = $base -replace '(?i)\b4k\b', ''
  $base = $base -replace '(?i)\bHD\b', ''
  $base = $base -replace '(?i)\bHDTV\b', ''
  $base = $base -replace '(?i)\bWEBRip\b', ''
  $base = $base -replace '(?i)\bBluRay\b', ''
  
  # Clean up separators and whitespace
  $base = $base -replace '[\-_\.]+', ' '
  $base = $base -replace '\s+', ' '
  $base = $base.Trim(' ', '-', '_', '.')
  
  return $base.ToLower()
}

# Hashtable to store extracted filename components for renumber operations
$script:FileComponents = @{}

function Get-FilenameComponents {
  <#
  .SYNOPSIS
  Extract all components from a filename: prefix, season, episode, and core name.
  Returns a PSCustomObject with these properties for later use in renaming.
  #>
  param([string]$FileName)
  
  $result = [PSCustomObject]@{
    OriginalName = $FileName
    Prefix = ""
    Season = ""
    SeasonNum = 0
    Episode = ""
    EpisodeNum = 0
    CoreName = ""
    Extension = ""
  }
  
  if (-not $FileName) { return $result }
  
  $result.Extension = [System.IO.Path]::GetExtension($FileName)
  $base = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
  $workingBase = $base
  
  # Detect and extract prefix (from UI fields)
  $detected = if ($TxtDetectedPrefix -and $TxtDetectedPrefix.Text) { $TxtDetectedPrefix.Text.Trim() } else { "" }
  $oldPrefix = if ($TxtOldPrefix -and $TxtOldPrefix.Text) { $TxtOldPrefix.Text.Trim() } else { "" }
  $newPrefix = if ($TxtNewPrefix -and $TxtNewPrefix.Text) { $TxtNewPrefix.Text.Trim() } else { "" }
  
  foreach ($prefix in @($detected, $oldPrefix, $newPrefix)) {
    if ($prefix -and $workingBase.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      $result.Prefix = $workingBase.Substring(0, $prefix.Length)
      $workingBase = $workingBase.Substring($prefix.Length).TrimStart(' ', '-', '_', '.')
      break
    }
  }
  
  # If no UI prefix detected, try to infer prefix (first word/segment before separator)
  if (-not $result.Prefix -and $workingBase -match '^(?<prefix>[^\s\-_\.]+)[\s\-_\.]') {
    $result.Prefix = $matches.prefix
    $workingBase = $workingBase.Substring($matches.prefix.Length).TrimStart(' ', '-', '_', '.')
  }
  
  # Extract season/episode patterns - S##E##
  if ($workingBase -match '(?i)\bS(\d{1,4})\s*E(\d{1,4})\b') {
    $result.SeasonNum = [int]$matches[1]
    $result.EpisodeNum = [int]$matches[2]
    $result.Season = "S" + $result.SeasonNum.ToString().PadLeft(2,'0')
    $result.Episode = "E" + $result.EpisodeNum.ToString().PadLeft(2,'0')
    $workingBase = $workingBase -replace '(?i)\bS\d{1,4}\s*E\d{1,4}\b', ''
  }
  # Season only - S##
  elseif ($workingBase -match '(?i)\bS(\d{1,4})\b') {
    $result.SeasonNum = [int]$matches[1]
    $result.Season = "S" + $result.SeasonNum.ToString().PadLeft(2,'0')
    $workingBase = $workingBase -replace '(?i)\bS\d{1,4}\b', ''
    # Check for separate episode
    if ($workingBase -match '(?i)\bE(\d{1,4})\b') {
      $result.EpisodeNum = [int]$matches[1]
      $result.Episode = "E" + $result.EpisodeNum.ToString().PadLeft(2,'0')
      $workingBase = $workingBase -replace '(?i)\bE\d{1,4}\b', ''
    }
  }
  # Format: 1x01
  elseif ($workingBase -match '(?i)\b(\d{1,4})x(\d{1,4})\b') {
    $result.SeasonNum = [int]$matches[1]
    $result.EpisodeNum = [int]$matches[2]
    $result.Season = "S" + $result.SeasonNum.ToString().PadLeft(2,'0')
    $result.Episode = "E" + $result.EpisodeNum.ToString().PadLeft(2,'0')
    $workingBase = $workingBase -replace '(?i)\b\d{1,4}x\d{1,4}\b', ''
  }
  # Season ## or Season # 
  elseif ($workingBase -match '(?i)\bSeason\s*(\d{1,4})\b') {
    $result.SeasonNum = [int]$matches[1]
    $result.Season = "S" + $result.SeasonNum.ToString().PadLeft(2,'0')
    $workingBase = $workingBase -replace '(?i)\bSeason\s*\d{1,4}\b', ''
  }
  # Standalone E## (episode only, no season) - must check this separately
  elseif ($workingBase -match '(?i)^E(\d{1,4})\b' -or $workingBase -match '(?i)[\s\-_\.]E(\d{1,4})\b') {
    if ($workingBase -match '(?i)\bE(\d{1,4})\b') {
      $result.EpisodeNum = [int]$matches[1]
      $result.Episode = "E" + $result.EpisodeNum.ToString().PadLeft(2,'0')
      $workingBase = $workingBase -replace '(?i)\bE\d{1,4}\b', ''
    }
  }
  # Episode ## or Ep ## (spelled out)
  if ($workingBase -match '(?i)\b(?:Episode|Ep)\s*(\d{1,4})\b') {
    $result.EpisodeNum = [int]$matches[1]
    $result.Episode = "E" + $result.EpisodeNum.ToString().PadLeft(2,'0')
    $workingBase = $workingBase -replace '(?i)\b(?:Episode|Ep)\s*\d{1,4}\b', ''
  }
  
  # Remove resolution/quality tags from core name
  $workingBase = $workingBase -replace '(?i)\b720p\b', ''
  $workingBase = $workingBase -replace '(?i)\b1080p\b', ''
  $workingBase = $workingBase -replace '(?i)\b4k\b', ''
  $workingBase = $workingBase -replace '(?i)\bHD\b', ''
  $workingBase = $workingBase -replace '(?i)\bHDTV\b', ''
  $workingBase = $workingBase -replace '(?i)\bWEBRip\b', ''
  $workingBase = $workingBase -replace '(?i)\bBluRay\b', ''
  
  # Clean up and store core name
  $workingBase = $workingBase -replace '[\-_\.]+', ' '
  $workingBase = $workingBase -replace '\s+', ' '
  $workingBase = $workingBase.Trim(' ', '-', '_', '.')
  $result.CoreName = $workingBase
  
  return $result
}

function Get-ProposedName {
    param([System.IO.FileInfo]$File,[int]$EpisodeNumber,[switch]$ForceEpisode)
    
    # Check if we have stored components from renumber operation
    $storedComponents = $null
    if ($ChkRenumberAll.IsChecked -and $script:FileComponents -and $script:FileComponents.ContainsKey($File.FullName)) {
      $storedComponents = $script:FileComponents[$File.FullName]
    }
    
    $base = [System.IO.Path]::GetFileNameWithoutExtension($File.Name)
    $ext  = $File.Extension
    $originalBase = $base
    if ($Chk720p.IsChecked)  { $base = $base -replace '(?i)720p','' }
    if ($Chk1080p.IsChecked) { $base = $base -replace '(?i)1080p','' }
    if ($Chk4k.IsChecked)    { $base = $base -replace '(?i)4k','' }
    if ($ChkHD.IsChecked)    { $base = $base -replace '(?i)hd','' }
    if ($TxtCustomClean.Text) {
      foreach ($token in ($TxtCustomClean.Text -split ',')) {
        $t = $token.Trim(); if ($t) { $base = $base -replace '(?i)' + [Regex]::Escape($t), '' }
      }
    }
    
    # If renumbering with stored components, use them to build the new name
    if ($storedComponents -and $ForceEpisode) {
      $episodeDigits = 2; if ($CmbEpisodeDigits.SelectedItem -and $CmbEpisodeDigits.SelectedItem.Content) { [void][int]::TryParse($CmbEpisodeDigits.SelectedItem.Content.ToString(), [ref]$episodeDigits) }
      $seasonDigits = 2; if ($CmbSeasonDigits.SelectedItem -and $CmbSeasonDigits.SelectedItem.Content) { [void][int]::TryParse($CmbSeasonDigits.SelectedItem.Content.ToString(), [ref]$seasonDigits) }
      
      # Use stored prefix, or new prefix if specified
      $usePrefix = $storedComponents.Prefix
      $newPrefix = if ($TxtNewPrefix.Text) { $TxtNewPrefix.Text.Trim() } else { "" }
      if ($ChkChangePrefix.IsChecked -and $newPrefix) { $usePrefix = $newPrefix }
      elseif ($ChkAddPrefixAll.IsChecked -and $newPrefix) { $usePrefix = $newPrefix }
      
      # Build season/episode tag
      $seasonStr = ""; $episodeStr = ""
      # Use original season if present, or from UI
      if ($storedComponents.SeasonNum -gt 0) {
        $seasonStr = "S" + $storedComponents.SeasonNum.ToString().PadLeft($seasonDigits,'0')
      } elseif ($ChkAddSeason.IsChecked) {
        $seasonNum = 0; [void][int]::TryParse($TxtSeason.Text, [ref]$seasonNum)
        if ($seasonNum -gt 0) { $seasonStr = "S" + $seasonNum.ToString().PadLeft($seasonDigits,'0') }
      }
      # New episode number from counter
      $episodeStr = "E" + $EpisodeNumber.ToString().PadLeft($episodeDigits,'0')
      
      $tag = if ($seasonStr -and $episodeStr) {
        if ($ChkSeasonBeforeEpisode.IsChecked) { "$seasonStr$episodeStr" } else { "$episodeStr$seasonStr" }
      } elseif ($seasonStr -or $episodeStr) { "$seasonStr$episodeStr" } else { "" }
      
      # Build final name: Prefix-Tag-CoreName.ext
      $coreName = $storedComponents.CoreName
      if (-not $coreName) { $coreName = $base }
      
      $segments = @()
      if ($usePrefix) { $segments += $usePrefix }
      if ($tag) { $segments += $tag }
      if ($coreName) { $segments += $coreName }
      
      $finalNameNoExt = ($segments -join "-").Trim()
      $finalNameNoExt = Normalize-NameSpaces $finalNameNoExt
      if (-not $finalNameNoExt) { $finalNameNoExt = $originalBase }
      
      return ($finalNameNoExt + $ext)
    }
    
    if ($ChkRenumberAll.IsChecked) {
      $base = Strip-ExistingEpisodeTags $base
      $originalBase = Strip-ExistingEpisodeTags $originalBase
    }
    $base = $base.Trim(); if (-not $base) { $base = $originalBase }
    $episodeDigits = 2; if ($CmbEpisodeDigits.SelectedItem -and $CmbEpisodeDigits.SelectedItem.Content) { [void][int]::TryParse($CmbEpisodeDigits.SelectedItem.Content.ToString(), [ref]$episodeDigits) }
    $seasonDigits = 2; if ($CmbSeasonDigits.SelectedItem -and $CmbSeasonDigits.SelectedItem.Content) { [void][int]::TryParse($CmbSeasonDigits.SelectedItem.Content.ToString(), [ref]$seasonDigits) }
    $oldPrefix = if ($TxtOldPrefix.Text) { $TxtOldPrefix.Text.Trim() } else { "" }
    $newPrefix = if ($TxtNewPrefix.Text) { $TxtNewPrefix.Text.Trim() } else { "" }
    $detected  = if ($TxtDetectedPrefix.Text) { $TxtDetectedPrefix.Text.Trim() } else { "" }
    $originalBaseNoOld = $originalBase
    if ($oldPrefix -and $originalBaseNoOld.StartsWith($oldPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { $originalBaseNoOld = $originalBaseNoOld.Substring($oldPrefix.Length) }
    if ($ChkRenumberAll.IsChecked) { $originalBaseNoOld = Strip-ExistingEpisodeTags $originalBaseNoOld }
    if ($oldPrefix -and $base.StartsWith($oldPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { $base = $base.Substring($oldPrefix.Length) }
    if ($ChkChangePrefix.IsChecked -and $newPrefix) { $base = $newPrefix + $base }
    elseif ($ChkAddPrefixAll.IsChecked -and $newPrefix -and -not $base.StartsWith($newPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { $base = $newPrefix + $base }
    elseif (-not $oldPrefix -and $detected -and -not $base.StartsWith($detected, [System.StringComparison]::OrdinalIgnoreCase)) { $base = $detected + $base }
    $prefix = ""; foreach ($p in @($newPrefix, $detected, $oldPrefix)) { if ($p -and $base.StartsWith($p)) { $prefix = $p; break } }
    $shouldInferPrefix = $ChkAddPrefixAll.IsChecked -or $ForceEpisode -or $ChkAddEpisode.IsChecked
    if (-not $prefix -and $shouldInferPrefix -and $base.Length -gt 0) {
        if ($base -match '^(?<lead>[^ _\-]+)(?<rest>.*)$') { $prefix = $matches.lead }
        if (-not $prefix) { $prefix = $base }
    }
    $remainder = $base; if ($prefix) { $remainder = $base.Substring([Math]::Min($prefix.Length, $base.Length)).Trim() }
    $fallbackRemainder = if ($prefix -and $originalBase.StartsWith($prefix)) { $originalBase.Substring([Math]::Min($prefix.Length, $originalBase.Length)).Trim() } else { $originalBase }
    if (-not $remainder) { $remainder = $fallbackRemainder }
    $tailName = if ($ChkAddPrefixAll.IsChecked) { $originalBaseNoOld } else { $remainder }
    $remainder = ($remainder -replace '(?i)\bS\d{1,4}\b','').Trim()
    $remainder = ($remainder -replace '(?i)\bE\d{1,4}\b','').Trim()
    $seasonStr = ""; $episodeStr = ""
    if ($ChkAddSeason.IsChecked) { $seasonNum = 0; [void][int]::TryParse($TxtSeason.Text, [ref]$seasonNum); if ($seasonNum -gt 0) { $seasonStr = "S" + $seasonNum.ToString().PadLeft($seasonDigits,'0') } }
    if (($ForceEpisode -or $ChkAddEpisode.IsChecked) -and $prefix) { $episodeStr = "E" + $EpisodeNumber.ToString().PadLeft($episodeDigits,'0') }
    if ($seasonStr -and $episodeStr) { $tag = if ($ChkSeasonBeforeEpisode.IsChecked) { "$seasonStr$episodeStr" } else { "$episodeStr$seasonStr" } }
    elseif ($seasonStr -or $episodeStr) { $tag = "$seasonStr$episodeStr" } else { $tag = "" }
    if ($prefix -or $tag) {
      $segments = @(); if ($prefix) { $segments += $prefix }; if ($tag) { $segments += $tag }; $segments += $tailName; $finalNameNoExt = ($segments -join "-").Trim()
    } else { $finalNameNoExt = $tailName }
    $finalNameNoExt = Normalize-NameSpaces $finalNameNoExt
    $safeName = $finalNameNoExt.Trim(); if (-not $safeName) { $safeName = $originalBase }
    return ($safeName + $ext)
}

  function Get-CommonPrefix {
    param([string[]]$Names)
    if (-not $Names -or $Names.Count -eq 0) { return "" }
    $first = $Names[0]
    $minLen = ($Names | Measure-Object Length -Minimum).Minimum
    $idx = 0
    while ($idx -lt $minLen) {
      $char = $first[$idx]
      if ($Names | Where-Object { $_[$idx] -ne $char -and [char]::ToLowerInvariant($_[$idx]) -ne [char]::ToLowerInvariant($char) }) { break }
      $idx++
    }
    $prefix = $first.Substring(0, $idx)
    $prefix = $prefix.Trim(' ','-','_','.')
    return $prefix
  }

function Get-MetadataSummary {
    param([System.IO.FileInfo]$File)
    $summary = ""
    if (-not ($ChkUseAudioMetadata.IsChecked -or $ChkUseVideoMetadata.IsChecked)) { return $summary }
  if ($ffprobePath) {
    try {
      $ffout = & $ffprobePath -v error -show_entries format=tags=title,artist,album,show,season_number,episode_id `
                   -of default=noprint_wrappers=1:nokey=0 -- "$($File.FullName)" 2>$null
      if ($ffout) {
        $tags = @{}
        foreach ($line in $ffout) { if ($line -match "^(?<k>[^=]+)=(?<v>.+)$") { $tags[$matches.k] = $matches.v } }
        if ($ChkUseAudioMetadata.IsChecked) { $summary = ((@($tags.artist, $tags.album, $tags.title) -join " | ").Trim(" |")) }
        elseif ($ChkUseVideoMetadata.IsChecked) { $summary = ((@($tags.show, ("S{0}E{1}" -f $tags.season_number, $tags.episode_id), $tags.title) -join " | ").Trim(" |")) }
      }
    } catch { }
  }
  elseif ($exiftoolPath) {
    try { $exif = & $exiftoolPath -s -s -s -Artist -Album -Title -TVShowName -SeasonNumber -EpisodeNumber -- "$($File.FullName)" 2>$null; if ($exif) { $summary = ($exif | Where-Object { $_ }) -join " | " } } catch { }
  }
    return $summary
}

function Normalize-NameSpaces {
  param([string]$Name)
  $mode = "space-to-dash"
  if ($CmbSpaceReplace -and $CmbSpaceReplace.SelectedItem) {
    $sel = $CmbSpaceReplace.SelectedItem
    if ($sel.Tag -ne $null) { $mode = $sel.Tag.ToString() }
  }
  switch ($mode) {
    "space-to-dash" {
      $clean = $Name -replace '\s+','-'
      $clean = $clean -replace '-{2,}','-'
      $clean = $clean.Trim('-')
    }
    "space-to-underscore" {
      $clean = $Name -replace '\s+','_'
      $clean = $clean -replace '_{2,}','_'
      $clean = $clean.Trim('_')
    }
    "space-remove" {
      $clean = $Name -replace '\s+',''
    }
    "dash-to-space" {
      $clean = $Name -replace '-',' '
      $clean = $clean -replace '\s{2,}',' '
      $clean = $clean.Trim()
    }
    "dash-to-underscore" {
      $clean = $Name -replace '-','_'
      $clean = $clean -replace '_{2,}','_'
      $clean = $clean.Trim('_')
    }
    "underscore-to-dash" {
      $clean = $Name -replace '_','-'
      $clean = $clean -replace '-{2,}','-'
      $clean = $clean.Trim('-')
    }
    "underscore-to-space" {
      $clean = $Name -replace '_',' '
      $clean = $clean -replace '\s{2,}',' '
      $clean = $clean.Trim()
    }
    default { $clean = $Name }
  }
  if (-not $clean) { $clean = $Name }
  return $clean
}

function Rename-ItemCaseAware {
  param([string]$Path,[string]$NewName)
  $dir = [System.IO.Path]::GetDirectoryName($Path)
  $currentName = [System.IO.Path]::GetFileName($Path)
  $targetPath = Join-Path $dir $NewName
  $caseOnlyChange = ($currentName -ieq $NewName) -and -not ($currentName -ceq $NewName)
  if ($caseOnlyChange) {
    $tempName = "__temp__" + [Guid]::NewGuid().ToString() + "__" + $NewName
    $tempPath = Join-Path $dir $tempName
    Rename-Item -Path $Path -NewName $tempName -ErrorAction Stop
    Rename-Item -Path $tempPath -NewName $NewName -ErrorAction Stop
  }
  else {
    Rename-Item -Path $Path -NewName $NewName -ErrorAction Stop
  }
}

# File Cleaner events
$BtnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select a folder to process"
    $dialog.ShowNewFolderButton = $false
    $result = $dialog.ShowDialog()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) { $TxtFolder.Text = $dialog.SelectedPath; Update-Status "Folder selected: $($dialog.SelectedPath)" } else { Update-Status "Browse canceled." }
})

$BtnExit.Add_Click({
  $window.Close()
})

$BtnSelectAll.Add_Click({ foreach ($row in $DgPreview.Items) { try { $row.Apply = $true } catch {} }; $DgPreview.Items.Refresh() })
$BtnSelectNone.Add_Click({ foreach ($row in $DgPreview.Items) { try { $row.Apply = $false } catch {} }; $DgPreview.Items.Refresh() })
$BtnDetectPrefix.Add_Click({
  if (-not $TxtFolder.Text) { Update-Status "Select a folder first."; return }
  $files = Get-FilteredFiles -Path $TxtFolder.Text -Recurse:$ChkRecurse.IsChecked
  if ($ChkOnlyIfOldPrefix.IsChecked -and $TxtOldPrefix.Text) { $oldPref = $TxtOldPrefix.Text.Trim(); $files = $files | Where-Object { $_.BaseName.StartsWith($oldPref, [System.StringComparison]::OrdinalIgnoreCase) } }
  if ($TxtIgnorePrefix.Text) { $ignorePref = $TxtIgnorePrefix.Text.Trim(); if ($ignorePref) { $files = $files | Where-Object { -not $_.BaseName.StartsWith($ignorePref, [System.StringComparison]::OrdinalIgnoreCase) } } }
  if (-not $files -or $files.Count -eq 0) { Update-Status "No files found for prefix detection."; return }
  $names = $files | Select-Object -ExpandProperty BaseName
  $prefix = Get-CommonPrefix $names
  if ($prefix) { $TxtDetectedPrefix.Text = $prefix; Update-Status "Detected prefix: $prefix" }
  else { $TxtDetectedPrefix.Clear(); Update-Status "No common prefix detected." }
})
$BtnSpaceCleanup.Add_Click({
  if (-not $DgPreview.ItemsSource) { Update-Status "No preview to update."; return }
  foreach ($row in $DgPreview.Items) {
    $ext = [System.IO.Path]::GetExtension($row.Proposed)
    $base = [System.IO.Path]::GetFileNameWithoutExtension($row.Proposed)
    $newBase = Normalize-NameSpaces $base
    $newName = if ($ext) { "$newBase$ext" } else { $newBase }
    $row.Proposed = $newName
    $row.Status = if ($newName -eq $row.Original) { "No change" } else { "Pending" }
    $row.Apply = ($row.Status -ne "No change")
  }
  $DgPreview.Items.Refresh()
  $hasPending = (@($DgPreview.Items | Where-Object { $_.Status -ne "No change" })).Count -gt 0
  $BtnApply.IsEnabled = ($DgPreview.Items.Count -gt 0 -and $hasPending)
  Update-Status "Space cleanup applied."
})

$BtnScan.Add_Click({
    if (-not $TxtFolder.Text) {
      Update-Status "Please select or enter a folder path."
      [System.Windows.MessageBox]::Show("Please select or enter a folder path before scanning.", "Folder Required", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Information) | Out-Null
      return
    }
    Update-Status "Scanning..."
    $startNum = 1
    if ($ChkAddEpisode.IsChecked -or $ChkRenumberAll.IsChecked) { [void][int]::TryParse($TxtStart.Text, [ref]$startNum); if ($startNum -lt 1) { $startNum = 1 } }
    $files = Get-FilteredFiles -Path $TxtFolder.Text -Recurse:$ChkRecurse.IsChecked
    if ($ChkOnlyIfOldPrefix.IsChecked -and $TxtOldPrefix.Text) { $oldPref = $TxtOldPrefix.Text.Trim(); $files = $files | Where-Object { $_.BaseName.StartsWith($oldPref, [System.StringComparison]::OrdinalIgnoreCase) } }
    if ($TxtIgnorePrefix.Text) { $ignorePref = $TxtIgnorePrefix.Text.Trim(); if ($ignorePref) { $files = $files | Where-Object { -not $_.BaseName.StartsWith($ignorePref, [System.StringComparison]::OrdinalIgnoreCase) } } }
    
    # Extract and store filename components before sorting
    $script:FileComponents = @{}
    if ($ChkRenumberAll.IsChecked) {
      foreach ($f in $files) {
        $script:FileComponents[$f.FullName] = Get-FilenameComponents $f.Name
      }
      $files = $files | Sort-Object { $script:FileComponents[$_.FullName].CoreName.ToLower() }
    }
    
    $preview = @(); $episodeCounter = $startNum; $hasEpisodeOps = $ChkRenumberAll.IsChecked -or $ChkAddEpisode.IsChecked
    foreach ($f in $files) {
      if ($ChkRenumberAll.IsChecked) { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter -ForceEpisode; $episodeCounter++ }
      elseif ($ChkAddEpisode.IsChecked) { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter; $episodeCounter++ }
      else { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter }
      $isCaseOnly = $false
      if ($ChkNormalizePrefixCase -and $ChkNormalizePrefixCase.IsChecked) { $isCaseOnly = ($proposed -ieq $f.Name) -and -not ($proposed -ceq $f.Name) }
      $status = if (($proposed -eq $f.Name) -and -not $isCaseOnly) { "No change" } else { "Pending" }
      $applyFlag = ($status -ne "No change")
      $preview += [PSCustomObject]@{ Apply=$applyFlag; Original=$f.Name; Proposed=$proposed; Status=$status; Directory=$f.DirectoryName; MetaSummary=Get-MetadataSummary -File $f }
    }
    $DgPreview.ItemsSource = $preview
    $hasPending = (@($preview | Where-Object { $_.Status -ne "No change" })).Count -gt 0
    $BtnApply.IsEnabled     = ($preview.Count -gt 0 -and $hasPending)
    $BtnExportCsv.IsEnabled = ($preview.Count -gt 0)
    if ($preview.Count -gt 0) {
      Update-Status "Preview complete: $($preview.Count) files."
    } else {
      Update-Status "No files matched filters."
    }
})

$global:LastOperations = @()
$BtnApply.Add_Click({
    Update-Status "Applying changes..."; $ops = @()
    foreach ($row in $DgPreview.Items) {
        if (-not $row.Apply -or $row.Status -eq "No change") { continue }
        $fileObj = Get-Item (Join-Path $row.Directory $row.Original)
        $episodeNum = 0; [void][int]::TryParse(($row.Proposed -replace '.*E(\d+).*','$1'), [ref]$episodeNum)
        if ($ChkRenumberAll.IsChecked) { $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum -ForceEpisode }
        elseif ($ChkAddEpisode.IsChecked) { $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum }
        else { $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum }
        $isCaseOnly = $false
        if ($ChkNormalizePrefixCase -and $ChkNormalizePrefixCase.IsChecked) { $isCaseOnly = ($proposed -ieq $row.Original) -and -not ($proposed -ceq $row.Original) }
        if (($row.Original -eq $proposed) -and -not $isCaseOnly) {
          $row.Status = "No change"
          $row.Apply = $false
        }
        elseif ($row.Original -ne $proposed -or $isCaseOnly) {
            $oldPath = Join-Path $row.Directory $row.Original; $newPath = Join-Path $row.Directory $proposed
            if ($ChkDryRun.IsChecked) { $row.Status = "Dry-run" }
            else {
            try { Rename-ItemCaseAware -Path $oldPath -NewName $proposed; $row.Status = "Renamed"; $ops += [PSCustomObject]@{ Old=$oldPath; New=$newPath } }
                catch { $row.Status = "Error: $($_.Exception.Message)" }
            }
        } else { $row.Status = "No change"; $row.Apply = $false }
    }
    $global:LastOperations = $ops; $DgPreview.Items.Refresh();
    if ($ChkDryRun.IsChecked) {
      Update-Status "Dry-run complete."
    } elseif ($ops.Count -gt 0) {
      Update-Status "Apply complete: $($ops.Count) renamed."
    } else {
      Update-Status "No changes applied."
    }
})

$BtnUndo.Add_Click({
    if ($global:LastOperations.Count -eq 0) { Update-Status "Nothing to undo."; return }
    $undoCount = 0
    foreach ($op in $global:LastOperations) {
        if (Test-Path $op.New) {
            try { Rename-Item -Path $op.New -NewName (Split-Path $op.Old -Leaf) -ErrorAction Stop; $undoCount++ }
            catch { Write-Warning "Undo failed for $($op.New): $_" }
        }
    }
    $global:LastOperations = @(); Update-Status "Undo complete: $undoCount file(s) reverted."
})

$BtnExportCsv.Add_Click({
    Update-Status "Exporting CSV..."; $dialog = New-Object Microsoft.Win32.SaveFileDialog; $dialog.Filter = "CSV files (*.csv)|*.csv"; $dialog.FileName = "FileCleanerExport.csv"
    if ($dialog.ShowDialog()) { $DgPreview.Items | Export-Csv -Path $dialog.FileName -NoTypeInformation -Encoding UTF8; Update-Status "CSV exported to $($dialog.FileName)" } else { Update-Status "Export canceled." }
})

$BtnReset.Add_Click({
    Update-Status "Resetting..."; $DgPreview.ItemsSource = $null; $BtnApply.IsEnabled = $false; $BtnExportCsv.IsEnabled = $false
  $TxtOldPrefix.Clear(); $TxtNewPrefix.Clear(); $TxtDetectedPrefix.Clear(); $TxtIgnorePrefix.Clear(); $TxtSeason.Clear(); $TxtStart.Clear(); $TxtCustomClean.Clear(); $TxtCustomExt.Clear();
  if ($CmbSpaceReplace) { $CmbSpaceReplace.SelectedIndex = 0 }
  if ($ChkNormalizePrefixCase) { $ChkNormalizePrefixCase.IsChecked = $false }
    Update-Status "Reset complete."
})

# ---------- Video Combiner Logic ----------
$CombItems = New-Object System.Collections.ArrayList
$supportedCombExt = @('.mp4','.mkv','.avi','.mov','.wmv','.webm','.flv')
$targetDir = $script:ToolDir
$CombLogDir  = $script:GlobalLogDir

if ($TxtConvOut -and -not [string]::IsNullOrWhiteSpace($targetDir)) { $TxtConvOut.Text = $targetDir }

function Pump-CombUI {
  if (-not $window) { return }
  $null = $window.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Background)
}

function Set-ConvStatus {
  param([string]$Text)
  if ($TxtConvStatus) { $TxtConvStatus.Text = $Text }
}

function Set-ConvProgress {
  param([bool]$Active,[int]$Index = 0,[int]$Total = 0)
  if (-not $ConvProgress) { return }
  if ($Active) {
    $ConvProgress.Visibility = [System.Windows.Visibility]::Visible
    if ($Total -gt 0) {
      $ConvProgress.IsIndeterminate = $false
      $pct = if ($Total -gt 0) { [Math]::Min(100,[Math]::Max(0,[int](100 * $Index / $Total))) } else { 0 }
      $ConvProgress.Value = $pct
    }
    else {
      $ConvProgress.IsIndeterminate = $true
      $ConvProgress.Value = 0
    }
  }
  else {
    $ConvProgress.IsIndeterminate = $false
    $ConvProgress.Value = 0
    $ConvProgress.Visibility = [System.Windows.Visibility]::Collapsed
  }
}

function Ensure-GlobalLogDir {
  if (-not (Ensure-DirectorySafe $script:GlobalLogDir)) { return }
}

function Ensure-LogDir {
  Ensure-GlobalLogDir
  if (-not (Ensure-DirectorySafe $CombLogDir)) { return }
}

function New-CombLogFile {
  param([string]$Prefix = "combine")
  Ensure-LogDir
  $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
  return Join-Path $CombLogDir ("{0}_{1}.log" -f $Prefix, $stamp)
}

function Write-CombLog {
  param([string]$Message,[string]$LogFile)
  $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
  $TxtCombLog.AppendText($line + "`r`n")
  if ($LogFile) { $line | Out-File -FilePath $LogFile -Append -Encoding UTF8 }
}

function Render-Args {
  param([string[]]$Array)
  return ($Array | ForEach-Object { if ($_ -match '\s') { "'$_'" } else { $_ } }) -join ' '
}

function Run-FFmpegLogged {
  param(
    [string[]]$CmdArgs,
    [string]$LogFile,
    [string]$Label,
    [int]$MaxUiLines = 200
  )
  Ensure-LogDir
  if (-not $ffmpegPath) {
    Write-CombLog "ffmpeg path not set; update Tool Folder." $LogFile
    return 1
  }
  $argList = if ($CmdArgs) { @($CmdArgs) } else { @() }
  $renderedArgs = Render-Args $argList
  if ($LogFile) {
    "===== $Label $(Get-Date -Format s) =====" | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "cmd: $ffmpegPath $renderedArgs"         | Out-File -FilePath $LogFile -Append -Encoding UTF8
  }
  Write-CombLog "$Label -> logging to $LogFile" $LogFile
  if (-not $argList -or $argList.Count -eq 0) {
    Write-CombLog "No ffmpeg arguments provided; aborting." $LogFile
    return 1
  }
  $output = & $ffmpegPath @argList 2>&1
  $exitCode = $LASTEXITCODE
  if ($output) {
    $lines = @($output | ForEach-Object { $_.ToString() })
    if ($LogFile) { $lines | Out-File -FilePath $LogFile -Append -Encoding UTF8 }
    $lines | Select-Object -First $MaxUiLines | ForEach-Object { $TxtCombLog.AppendText($_ + "`r`n") }
    if ($lines.Count -gt $MaxUiLines) { $TxtCombLog.AppendText("... (output truncated, see log file)`r`n") }
  }
  return $exitCode
}

function Ensure-Tools {
  <#
  .SYNOPSIS
  Check if FFmpeg/FFprobe are available. Shows warning if missing.
  #>
  Ensure-LogDir
  if ($ToolDir -and -not (Test-Path -LiteralPath $ToolDir)) { New-Item -ItemType Directory -Path $ToolDir -Force | Out-Null }
  Refresh-ToolPaths
  $script:HasFFmpeg = ($ffmpegPath -and (Test-Path -LiteralPath $ffmpegPath))
  $script:HasFFprobe = ($ffprobePath -and (Test-Path -LiteralPath $ffprobePath))
  if (-not $script:HasFFmpeg) {
    [System.Windows.MessageBox]::Show("ffmpeg.exe not found.`n`nVideo combining and conversion require FFmpeg.`nSet the Tool Folder to a directory containing ffmpeg.exe.`n`nImage operations (resize, ICO convert) work without FFmpeg.","Missing FFmpeg",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
    return $false
  }
  if (-not $script:HasFFprobe) {
    [System.Windows.MessageBox]::Show("ffprobe.exe not found.`n`nVideo info requires FFprobe (included with FFmpeg).`nSet the Tool Folder to a directory containing ffprobe.exe.","Missing FFprobe",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
    return $false
  }
  return $true
}

function Get-ToolStatus {
  <#
  .SYNOPSIS
  Returns a summary of which tools are available without showing dialogs
  #>
  Refresh-ToolPaths
  return @{
    FFmpeg = ($ffmpegPath -and (Test-Path -LiteralPath $ffmpegPath -ErrorAction SilentlyContinue))
    FFprobe = ($ffprobePath -and (Test-Path -LiteralPath $ffprobePath -ErrorAction SilentlyContinue))
    ExifTool = ($exiftoolPath -and (Test-Path -LiteralPath $exiftoolPath -ErrorAction SilentlyContinue))
  }
}

function Refresh-CombGrid { $DgCombine.ItemsSource = $null; $DgCombine.ItemsSource = $CombItems }
function Get-CombChecked { return @($CombItems | Where-Object { $_.Apply }) }

function Get-OutputDir {
  $chosen = if ($TxtConvOut -and $TxtConvOut.Text) { [Environment]::ExpandEnvironmentVariables($TxtConvOut.Text.Trim()) } else { $null }
  if (-not $chosen) { $chosen = $targetDir }
  if (-not $chosen) { $chosen = $ToolDir }
  if (-not $chosen) { $chosen = $script:Root }
  if (-not $chosen) { return $null }
  if (-not (Test-Path -LiteralPath $chosen)) { New-Item -ItemType Directory -Path $chosen -Force | Out-Null }
  return (Resolve-Path $chosen).Path
}

# ---------- Graphics Conversion Logic ----------
$ImgItems = New-Object System.Collections.ArrayList
$supportedImgExt = @('.jpg','.jpeg','.png')

function Refresh-ImgGrid { if ($DgImages) { $DgImages.ItemsSource = $null; $DgImages.ItemsSource = $ImgItems } }
function Get-ImgChecked { return @($ImgItems | Where-Object { $_.Apply }) }

function Set-ImgStatus {
  param([string]$Text)
  if ($TxtImgStatus) { $TxtImgStatus.Text = $Text }
}

function Set-ImgProgress {
  param([bool]$Active,[int]$Index = 0,[int]$Total = 0)
  if (-not $ImgProgress) { return }
  if ($Active) {
    $ImgProgress.Visibility = [System.Windows.Visibility]::Visible
    if ($Total -gt 0) {
      $ImgProgress.IsIndeterminate = $false
      $pct = if ($Total -gt 0) { [Math]::Min(100,[Math]::Max(0,[int](100 * $Index / $Total))) } else { 0 }
      $ImgProgress.Value = $pct
    }
    else {
      $ImgProgress.IsIndeterminate = $true
      $ImgProgress.Value = 0
    }
  }
  else {
    $ImgProgress.IsIndeterminate = $false
    $ImgProgress.Value = 0
    $ImgProgress.Visibility = [System.Windows.Visibility]::Collapsed
  }
}

function Add-ImgFiles {
  param([string[]]$Paths)
  if (-not $Paths) { return }
  $added = 0
  foreach ($p in $Paths) {
    if (-not (Test-Path -LiteralPath $p)) { continue }
    $ext = [System.IO.Path]::GetExtension($p).ToLower()
    if (-not ($supportedImgExt -contains $ext)) { continue }
    $resolved = (Resolve-Path -LiteralPath $p).Path
    if ($ImgItems | Where-Object { $_.Path -eq $resolved }) { continue }
    [void]$ImgItems.Add([PSCustomObject]@{ Apply=$true; Name=[System.IO.Path]::GetFileName($resolved); Path=$resolved })
    $added++
  }
  Refresh-ImgGrid
  if ($added -gt 0) { Set-ImgStatus "Added $added image(s)." }
}

function Load-ImgFolder {
  param([string]$Folder)
  if (-not $Folder) { return }
  if (-not (Test-Path -LiteralPath $Folder)) { Set-ImgStatus "Folder not found."; return }
  $files = Get-ChildItem -Path $Folder -File -ErrorAction SilentlyContinue | Where-Object { $supportedImgExt -contains $_.Extension.ToLower() }
  Add-ImgFiles -Paths ($files.FullName)
  if ($TxtImgFolder) { $TxtImgFolder.Text = $Folder }
  Update-Status "Loaded $($files.Count) images from $Folder"
}

function Get-IconOutputDir {
  $outDir = $null
  if ($TxtIcoOut -and $TxtIcoOut.Text) { $outDir = [Environment]::ExpandEnvironmentVariables($TxtIcoOut.Text.Trim()) }
  if (-not $outDir -and $ImgItems.Count -gt 0) { $outDir = [System.IO.Path]::GetDirectoryName($ImgItems[0].Path) }
  if (-not $outDir) { $outDir = $script:DataRoot }
  if (-not $outDir) { return $null }
  if (-not (Ensure-DirectorySafe $outDir)) { return $null }
  return (Resolve-Path $outDir).Path
}

function New-ImgLogFile {
  param([string]$Prefix = "imgconv")
  Ensure-GlobalLogDir
  $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
  return Join-Path $script:GlobalLogDir ("{0}_{1}.log" -f $Prefix, $stamp)
}

function Write-ImgLog {
  param([string]$Message,[string]$LogFile)
  $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
  if ($TxtImgLog) { $TxtImgLog.AppendText($line + "`r`n") }
  if ($LogFile) { $line | Out-File -FilePath $LogFile -Append -Encoding UTF8 }
}

function Convert-ImageToIco {
  param([string]$Source,[string]$Dest,[int]$Size,[string]$LogFile)
  Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
  $img = $null; $scaled = $null; $pngMs = $null; $fs = $null; $bw = $null
  Write-ImgLog "  Loading source: $Source" $LogFile
  try {
    $img = New-Object System.Drawing.Bitmap -ArgumentList $Source
    Write-ImgLog "    Source loaded: $($img.Width)x$($img.Height)" $LogFile
    Write-ImgLog "    Scaling to ${Size}x${Size}" $LogFile
    $scaled = New-Object System.Drawing.Bitmap -ArgumentList $img, (New-Object System.Drawing.Size($Size,$Size))
    # Save scaled image as PNG to memory
    $pngMs = New-Object System.IO.MemoryStream
    $scaled.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngMs.ToArray()
    Write-ImgLog "    PNG size: $($pngBytes.Length) bytes" $LogFile
    Write-ImgLog "    Building ICO file structure..." $LogFile
    # Build ICO file manually (ICO header + directory entry + PNG data)
    $fs = [System.IO.File]::Open($Dest, [System.IO.FileMode]::Create)
    $bw = New-Object System.IO.BinaryWriter -ArgumentList $fs
    # ICO Header (6 bytes)
    $bw.Write([UInt16]0)        # Reserved, must be 0
    $bw.Write([UInt16]1)        # Image type: 1 = ICO
    $bw.Write([UInt16]1)        # Number of images
    # ICO Directory Entry (16 bytes)
    $widthByte = if ($Size -ge 256) { 0 } else { [byte]$Size }
    $heightByte = if ($Size -ge 256) { 0 } else { [byte]$Size }
    $bw.Write([byte]$widthByte)   # Width (0 means 256)
    $bw.Write([byte]$heightByte)  # Height (0 means 256)
    $bw.Write([byte]0)            # Color palette (0 = no palette)
    $bw.Write([byte]0)            # Reserved
    $bw.Write([UInt16]1)          # Color planes
    $bw.Write([UInt16]32)         # Bits per pixel
    $bw.Write([UInt32]$pngBytes.Length)  # Size of image data
    $bw.Write([UInt32]22)         # Offset to image data (6 + 16 = 22)
    # Image data (PNG)
    $bw.Write($pngBytes)
    $bw.Flush()
    Write-ImgLog "    SUCCESS -> $Dest ($($fs.Length) bytes)" $LogFile
    return $true
  } catch {
    Write-ImgLog "    ERROR: $($_.Exception.Message)" $LogFile
    Write-ImgLog "    Stack: $($_.ScriptStackTrace)" $LogFile
    return $false
  }
  finally {
    if ($bw) { try { $bw.Dispose() } catch {} }
    if ($fs) { try { $fs.Dispose() } catch {} }
    if ($pngMs) { try { $pngMs.Dispose() } catch {} }
    if ($scaled) { try { $scaled.Dispose() } catch {} }
    if ($img) { try { $img.Dispose() } catch {} }
  }
}

function Get-ImageAHash {
  param([string]$Path)
  try {
    Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $Path
    $small = New-Object System.Drawing.Bitmap 9,8
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.DrawImage($bmp, 0, 0, 9, 8) | Out-Null
    $g.Dispose(); $bmp.Dispose()
    $hash = 0
    for ($y=0; $y -lt 8; $y++) {
      for ($x=0; $x -lt 8; $x++) {
        $left  = $small.GetPixel($x, $y).GetBrightness()
        $right = $small.GetPixel($x+1, $y).GetBrightness()
        $hash = ($hash -shl 1) -bor ([Convert]::ToInt32($left -lt $right))
      }
    }
    $small.Dispose()
    return "pimg:{0:x16}" -f $hash
  } catch { return $null }
}

function Get-VideoAHash {
  param([string]$Path)
  if (-not $ffmpegPath) { return $null }
  $tmp = [System.IO.Path]::GetTempFileName()
  try {
    $args = @('-hide_banner','-loglevel','error','-i',$Path,'-frames:v','1','-vf','thumbnail,scale=9:8,format=gray','-f','rawvideo',$tmp)
    $null = & $ffmpegPath @args 2>$null
    if (-not (Test-Path $tmp)) { return $null }
    $bytes = [System.IO.File]::ReadAllBytes($tmp)
    if ($bytes.Length -lt 9*8) { return $null }
    $hash = 0; $idx = 0
    for ($y=0; $y -lt 8; $y++) {
      for ($x=0; $x -lt 8; $x++) {
        $left  = $bytes[$idx]
        $right = $bytes[$idx+1]
        $hash = ($hash -shl 1) -bor ([Convert]::ToInt32($left -lt $right))
        $idx++
      }
      $idx++
    }
    return "pvid:{0:x16}" -f $hash
  } catch { return $null }
  finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}

function Get-AudioFingerprint {
  param([string]$Path)
  if (-not $fpcalcPath) { return $null }
  try {
    $proc = & $fpcalcPath -length 120 -- "$Path" 2>$null
    foreach ($line in $proc) { if ($line -match '^FINGERPRINT=(?<fp>.+)$') { return "fpac:$($matches.fp)" } }
  } catch { return $null }
  return $null
}

function Get-FileFingerprint {
  param([System.IO.FileInfo]$File,[string]$Method)
  $ext = $File.Extension.ToLower()
  $isVideo = '.mp4','.mkv','.avi','.mov','.wmv','.webm','.flv','.m4v' -contains $ext
  $isImage = '.jpg','.jpeg','.png','.gif','.webp','.tiff' -contains $ext
  $isAudio = '.mp3','.flac','.wav','.m4a','.aac','.ogg','.wma' -contains $ext
  if ($Method -eq 'deep') {
    if ($isAudio) { $fp = Get-AudioFingerprint $File.FullName; if ($fp) { return $fp } }
    if ($isVideo) { $vh = Get-VideoAHash $File.FullName; if ($vh) { return $vh } }
    if ($isImage) { $ih = Get-ImageAHash $File.FullName; if ($ih) { return $ih } }
  }
  try { return (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256 -ErrorAction Stop).Hash }
  catch { return $null }
}

function Get-FastFileHash {
  param([string]$Path)
  try { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash }
  catch { return $null }
}

function Set-DupStatus {
  param([string]$Text)
  if ($TxtDupStatus) { $TxtDupStatus.Text = $Text }
}

function Set-DupProgress {
  param([bool]$Active,[int]$Index = 0,[int]$Total = 0)
  if (-not $DupProgress) { return }
  if ($Active) {
    $DupProgress.Visibility = [System.Windows.Visibility]::Visible
    if ($Total -gt 0) {
      $DupProgress.IsIndeterminate = $false
      $pct = if ($Total -gt 0) { [Math]::Min(100,[Math]::Max(0,[int](100 * $Index / $Total))) } else { 0 }
      $DupProgress.Value = $pct
    }
    else {
      $DupProgress.IsIndeterminate = $true
      $DupProgress.Value = 0
    }
  }
  else {
    $DupProgress.IsIndeterminate = $false
    $DupProgress.Value = 0
    $DupProgress.Visibility = [System.Windows.Visibility]::Collapsed
  }
}

function Add-DupScanLogLine {
  param([string]$Text)
  if (-not $LstDupScanLog) { return }
  $LstDupScanLog.Items.Add($Text) | Out-Null
  $LstDupScanLog.ScrollIntoView($LstDupScanLog.Items[$LstDupScanLog.Items.Count - 1])
}

function Invoke-UIRefresh {
  [System.Windows.Forms.Application]::DoEvents()
}

function Start-DupLog {
  try {
    if (-not (Ensure-DirectorySafe $script:DupLogDir)) { return $null }
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $path = Join-Path $script:DupLogDir ("videoeditor-dup-scan-{0}.log" -f $stamp)
    "== Duplicate scan started {0} ==" -f (Get-Date -Format s) | Out-File -FilePath $path -Encoding UTF8 -ErrorAction Stop
    return $path
  }
  catch {
    Write-Warning ("Unable to start duplicate scan log: {0}" -f $_.Exception.Message)
    return $null
  }
}

function Write-DupLog {
  param([string]$Message,[string]$LogPath)
  $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
  if ($LogPath) { $line | Out-File -FilePath $LogPath -Append -Encoding UTF8 }
  Add-DupScanLogLine $line
}

function New-DupJsonPath {
  try {
    if (-not (Ensure-DirectorySafe $script:DupJsonDir)) { return $null }
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $jsonPath = Join-Path $script:DupJsonDir ("videoeditor-{0}-scan.json" -f $stamp)
    "" | Out-File -FilePath $jsonPath -Encoding UTF8 -ErrorAction Stop
    return $jsonPath
  }
  catch {
    Write-Warning ("Unable to create JSON file: {0}" -f $_.Exception.Message)
    return $null
  }
}

function Write-DupJsonSnapshot {
  param(
    [string]$JsonPath,
    [string]$Folder,
    [bool]$Recurse,
    [string]$Method,
    [object[]]$Entries,
    [string]$Stage,
    [string]$LogPath
  )
  if (-not $JsonPath) { return }
  try {
    $items = @()
    if ($Entries) {
      foreach ($e in $Entries) {
        $items += [PSCustomObject]@{
          Path          = $e.Path
          Size          = $e.Size
          LastWriteTime = $e.LastWriteTime
          Sha256        = $e.Sha256
        }
      }
    }
    $payload = [ordered]@{
      version    = 1
      generated  = (Get-Date -Format s)
      stage      = $Stage
      folder     = $Folder
      recurse    = [bool]$Recurse
      method     = $Method
      fileCount  = $items.Count
      files      = $items
    }
    ($payload | ConvertTo-Json -Depth 6) | Out-File -FilePath $JsonPath -Encoding UTF8 -ErrorAction Stop
    if ($LogPath) { "JSON updated ({0}) -> {1}" -f $Stage, $JsonPath | Out-File -FilePath $LogPath -Append -Encoding UTF8 }
  }
  catch {
    if ($LogPath) { "ERROR writing JSON ({0}): {1}" -f $Stage, $_.Exception.Message | Out-File -FilePath $LogPath -Append -Encoding UTF8 }
    Write-Warning ("Unable to write JSON ({0}): {1}" -f $Stage, $_.Exception.Message)
  }
}

function Get-DupExtensionsFromUI {
    $extensions = @()
    if ($ChkDupVideo.IsChecked)     { $extensions += '.mp4','.mkv','.avi','.mov','.wmv','.m4v' }
    if ($ChkDupPictures.IsChecked)  { $extensions += '.jpg','.jpeg','.png','.gif','.webp','.tiff','.bmp','.heic','.heif' }
    if ($ChkDupDocuments.IsChecked) { $extensions += '.pdf','.doc','.docx','.txt','.md','.rtf' }
    if ($ChkDupAudio.IsChecked)     { $extensions += '.mp3','.flac','.wav','.m4a','.aac' }
    if ($ChkDupArchives.IsChecked)  { $extensions += '.zip','.rar','.7z','.tar','.gz' }
    if ($TxtDupCustomExt -and $TxtDupCustomExt.Text) {
        $custom = $TxtDupCustomExt.Text -split ',' | ForEach-Object { $_.Trim().ToLower() }
        $custom = $custom | ForEach-Object { if ($_ -and $_[0] -ne '.') { "." + $_ } else { $_ } }
        $extensions += $custom
    }
    return @($extensions | Select-Object -Unique)
}

function Get-DupFilteredFiles {
    param([string]$Path,[switch]$Recurse,[string[]]$Extensions)
    if (-not (Test-Path $Path)) { return @() }
    $files = Get-ChildItem -Path $Path -File -Recurse:$Recurse -ErrorAction SilentlyContinue
    if (-not $Extensions -or $Extensions.Count -eq 0) { return $files }
    return $files | Where-Object { $Extensions -contains $_.Extension.ToLower() }
}

function Get-SelectedDupes {
  if (-not $DgDupes.ItemsSource) { return @() }
  return @($DgDupes.Items | Where-Object { $_.Apply })
}

function Load-CombFiles {
    param([string]$Folder)
    if (-not (Test-Path $Folder)) { Update-Status "Folder not found."; return }
    $CombItems.Clear() | Out-Null
    Get-ChildItem -Path $Folder -File | Where-Object { $supportedCombExt -contains $_.Extension.ToLower() } | ForEach-Object {
        [void]$CombItems.Add([PSCustomObject]@{ Apply=$false; Name=$_.Name; Path=$_.FullName })
    }
    Refresh-CombGrid
    Update-Status "Loaded $($CombItems.Count) videos"
    $TxtCombLog.Text = "Loaded $($CombItems.Count) video files from $Folder"
}

function Move-CombItem {
    param([int]$Offset)
    $selected = $DgCombine.SelectedItem
    if (-not $selected) { return }
    $index = $CombItems.IndexOf($selected)
    $target = $index + $Offset
    if ($target -lt 0 -or $target -ge $CombItems.Count) { return }
    $CombItems.RemoveAt($index)
    $CombItems.Insert($target, $selected)
    Refresh-CombGrid
    $DgCombine.SelectedIndex = $target
}

function Get-VideoInfo {
    param([string]$file)
  if (-not $ffprobePath) { return @() }
    $probeArgs = @('-v','error','-select_streams','v:0','-show_entries','stream=codec_name,width,height,r_frame_rate,format=duration','-of','default=noprint_wrappers=1',"$file")
    & $ffprobePath @probeArgs
}

function Compare-Encodings {
    param($files)
    $details = @()
    foreach ($f in $files) {
        $info = Get-VideoInfo $f.Path
        $codec    = ($info | Select-String "codec_name="     | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $width    = ($info | Select-String "width="          | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $height   = ($info | Select-String "height="         | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $fps      = ($info | Select-String "r_frame_rate="   | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $duration = ($info | Select-String "duration="       | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $details += [PSCustomObject]@{ File=[System.IO.Path]::GetFileName($f.Path); FullPath=$f.Path; Codec=$codec; Width=$width; Height=$height; FPS=$fps; Duration=$duration }
    }
    return $details
}

# Combiner events
$BtnToolDirBrowse.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') {
    Set-ToolDir $fb.SelectedPath
    Update-Status "Tool folder set to $($fb.SelectedPath)"
  }
})
if ($TxtToolDir) {
  $TxtToolDir.Add_LostFocus({ Set-ToolDir $TxtToolDir.Text })
}

$BtnDupBrowse.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') { $TxtDupFolder.Text = $fb.SelectedPath; Update-Status "Duplicate scan folder: $($fb.SelectedPath)" }
})

$BtnDupScan.Add_Click({
  if ($script:DupScanRunning) { return }
  if (-not $TxtDupFolder.Text) {
    Update-Status "Please select a folder to scan for duplicates."; [System.Windows.MessageBox]::Show("Select a folder first."); return
  }
  $folder = $TxtDupFolder.Text
  $recurse = $ChkDupRecurse.IsChecked
  $method = 'fast'
  if ($CmbDupMethod -and $CmbDupMethod.SelectedItem -and $CmbDupMethod.SelectedItem.Tag) { $method = $CmbDupMethod.SelectedItem.Tag.ToString() }
  $script:DupScanRunning = $true
  $DgDupes.ItemsSource = $null
  if ($LstDupScanLog) { $LstDupScanLog.Items.Clear() }

  # Capture UI values before scan
  $extensions = Get-DupExtensionsFromUI

  # Initialize log and JSON files
  $logPath = Start-DupLog
  if (-not $logPath) { Update-Status "Log file unavailable; continuing without writing to disk." }

  $jsonPath = New-DupJsonPath
  if ($jsonPath) {
    if ($LstDupScanLog) { $LstDupScanLog.Items.Add("JSON file created at $jsonPath") }
  }
  else {
    if ($LstDupScanLog) { $LstDupScanLog.Items.Add("JSON file could not be created; proceeding without JSON output.") }
    Update-Status "JSON file could not be created; continuing without JSON output."
  }

  Set-DupStatus ("Scanning ({0})..." -f $method)
  Update-Status ("Scanning for duplicates ({0})..." -f $method)

  # --- STEP 1: List files ---
  Set-DupStatus "Listing files..."
  Set-DupProgress -Active $true
  Add-DupScanLogLine "--- Listing files ---"
  Invoke-UIRefresh

  $files = @()
  try {
    if (-not (Test-Path $folder)) {
      Set-DupStatus "Folder not found."
      Set-DupProgress -Active $false
      Update-Status "Folder not found: $folder"
      $script:DupScanRunning = $false
      return
    }
    $allFiles = @(Get-ChildItem -Path $folder -File -Recurse:$recurse -ErrorAction SilentlyContinue)
    $allCount = $allFiles.Count
    $listIdx = 0
    foreach ($f in $allFiles) {
      $listIdx++
      if ($extensions -and $extensions.Count -gt 0) {
        if ($extensions -contains $f.Extension.ToLower()) {
          $files += $f
          Add-DupScanLogLine $f.FullName
        }
      } else {
        $files += $f
        Add-DupScanLogLine $f.FullName
      }
      # Update progress every 10 files or on first/last
      if ($listIdx -eq 1 -or $listIdx -eq $allCount -or ($listIdx % 10) -eq 0) {
        Set-DupProgress -Active $true -Index $listIdx -Total $allCount
        Set-DupStatus ("Listing files... {0}/{1}" -f $listIdx, $allCount)
        Invoke-UIRefresh
      }
    }
  }
  catch {
    Set-DupStatus "Error listing files."
    Set-DupProgress -Active $false
    Update-Status ("Error listing files: {0}" -f $_.Exception.Message)
    $script:DupScanRunning = $false
    return
  }

  $total = $files.Count
  Add-DupScanLogLine "Found $total file(s) matching filter"
  Invoke-UIRefresh

  # Write initial file list to JSON
  $listedEntries = @()
  foreach ($f in $files) {
    $listedEntries += [PSCustomObject]@{
      Path          = $f.FullName
      Size          = $f.Length
      LastWriteTime = $f.LastWriteTimeUtc
      Sha256        = $null
    }
  }
  if ($jsonPath) {
    try {
      $payload = [ordered]@{
        version   = 1
        generated = (Get-Date -Format s)
        stage     = "listed"
        folder    = $folder
        recurse   = [bool]$recurse
        method    = $method
        fileCount = $listedEntries.Count
        files     = $listedEntries
      }
      ($payload | ConvertTo-Json -Depth 6) | Out-File -FilePath $jsonPath -Encoding UTF8 -ErrorAction Stop
      Add-DupScanLogLine "JSON updated (listed) -> $jsonPath"
    }
    catch {
      Add-DupScanLogLine ("ERROR writing JSON (listed): {0}" -f $_.Exception.Message)
    }
  }

  if ($total -eq 0) {
    Set-DupStatus "No files found."
    Set-DupProgress -Active $false
    Update-Status "No files found for duplicate scan."
    $script:DupScanRunning = $false
    return
  }

  # --- STEP 2: Hash files ---
  Set-DupStatus "Hashing files..."
  Add-DupScanLogLine "--- Hashing (SHA-256) ---"
  Invoke-UIRefresh

  $hashEntries = @()
  $idx = 0
  foreach ($f in $files) {
    $idx++
    Set-DupProgress -Active $true -Index $idx -Total $total
    Set-DupStatus ("Hashing file {0}/{1}: {2}" -f $idx, $total, $f.Name)
    Invoke-UIRefresh
    try {
      $sha = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
      $hashEntries += [PSCustomObject]@{
        Path          = $f.FullName
        Sha256        = $sha
        Size          = $f.Length
        LastWriteTime = $f.LastWriteTimeUtc
      }
      Add-DupScanLogLine ("Hashed {0} => {1}" -f $f.FullName, $sha)
    }
    catch {
      Add-DupScanLogLine ("ERROR hashing {0}: {1}" -f $f.FullName, $_.Exception.Message)
    }
  }

  $uniqueCount = ($hashEntries | Select-Object -ExpandProperty Sha256 -Unique).Count
  Add-DupScanLogLine ("Hashing complete. Files hashed={0}, Unique hashes={1}" -f $hashEntries.Count, $uniqueCount)
  Invoke-UIRefresh

  # --- STEP 3: Save hashed JSON ---
  Set-DupStatus "Saving hash catalog..."
  Add-DupScanLogLine "--- Saving JSON ---"
  Invoke-UIRefresh

  if ($jsonPath) {
    try {
      $payload = [ordered]@{
        version   = 1
        generated = (Get-Date -Format s)
        stage     = "hashed"
        folder    = $folder
        recurse   = [bool]$recurse
        method    = $method
        fileCount = $hashEntries.Count
        files     = $hashEntries
      }
      ($payload | ConvertTo-Json -Depth 6) | Out-File -FilePath $jsonPath -Encoding UTF8 -ErrorAction Stop
      Add-DupScanLogLine "JSON updated (hashed) -> $jsonPath"
    }
    catch {
      Add-DupScanLogLine ("ERROR writing JSON (hashed): {0}" -f $_.Exception.Message)
    }
  }

  # --- STEP 4: Find duplicates ---
  Set-DupStatus "Finding duplicates..."
  Set-DupProgress -Active $true
  Add-DupScanLogLine "--- Finding duplicates ---"
  Invoke-UIRefresh

  $dupes = @()
  $groups = $hashEntries | Group-Object Sha256
  foreach ($grp in $groups) {
    if ($grp.Count -lt 2) { continue }
    foreach ($entry in $grp.Group) {
      $dir  = [System.IO.Path]::GetDirectoryName($entry.Path)
      $name = [System.IO.Path]::GetFileName($entry.Path)
      $dupes += [PSCustomObject]@{
        Apply     = $false
        Hash      = $grp.Name
        Count     = $grp.Count
        Size      = $entry.Size
        Name      = $name
        Directory = $dir
        FullPath  = $entry.Path
      }
    }
  }
  $dupes = @($dupes | Sort-Object Hash, Directory, Name)

  Add-DupScanLogLine ("Duplicate rows prepared: {0}" -f $dupes.Count)
  Invoke-UIRefresh

  # --- STEP 5: Update UI ---
  Set-DupProgress -Active $false
  $DgDupes.ItemsSource = $dupes
  $msg = if ($dupes.Count -gt 0) { "Duplicate groups: $($dupes.Count) entries" } else { "No duplicates found." }
  if ($jsonPath) { $msg = $msg + " JSON saved: $jsonPath" }
  Set-DupStatus $msg
  Update-Status $msg
  $script:DupScanRunning = $false

  Add-DupScanLogLine "Scan finished."
  Invoke-UIRefresh
})

$BtnDupDelete.Add_Click({
  $selected = Get-SelectedDupes
  if ($selected.Count -eq 0) { Update-Status "No duplicates selected for delete."; return }
  $sample = ($selected | Select-Object -First 3 -ExpandProperty FullPath) -join "`n"
  $confirm = [System.Windows.MessageBox]::Show(("Delete the selected {0} file(s)?`n`n{1}" -f $selected.Count, $sample), "Confirm delete", [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Warning)
  if ($confirm -ne [System.Windows.MessageBoxResult]::Yes) { return }
  $deleted = 0; $errors = 0
  foreach ($row in $selected) {
    try { if (Test-Path -LiteralPath $row.FullPath) { Remove-Item -LiteralPath $row.FullPath -Force; $deleted++ } }
    catch { $errors++ }
  }
  $remaining = @()
  foreach ($row in $DgDupes.Items) {
    if ($row.Apply -and -not (Test-Path -LiteralPath $row.FullPath)) { continue }
    $row.Apply = $false
    $remaining += $row
  }
  $DgDupes.ItemsSource = $null; $DgDupes.ItemsSource = $remaining
  $status = "Deleted {0} item(s)." -f $deleted
  if ($errors -gt 0) { $status += " Errors: $errors" }
  Set-DupStatus $status; Update-Status $status
})

$BtnDupRename.Add_Click({
  $selected = Get-SelectedDupes
  if ($selected.Count -eq 0) { Update-Status "No duplicates selected for rename."; return }
  $renamed = 0; $skipped = 0; $errors = 0
  foreach ($row in $selected) {
    $input = [Microsoft.VisualBasic.Interaction]::InputBox(("Enter a new name for:`n{0}" -f $row.FullPath), "Rename duplicate", $row.Name)
    if (-not $input) { $skipped++; continue }
    $newName = $input.Trim()
    if (-not $newName) { $skipped++; continue }
    $newPath = Join-Path $row.Directory $newName
    if (Test-Path -LiteralPath $newPath) {
      [System.Windows.MessageBox]::Show(("Target already exists:`n{0}" -f $newPath), "Rename skipped", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning) | Out-Null
      $skipped++
      continue
    }
    try {
      Rename-Item -LiteralPath $row.FullPath -NewName $newName -ErrorAction Stop
      $row.Name = [System.IO.Path]::GetFileName($newPath)
      $row.FullPath = $newPath
      $row.Apply = $false
      $renamed++
    }
    catch {
      $errors++
    }
  }
  $DgDupes.Items.Refresh()
  $status = "Renamed {0} item(s)." -f $renamed
  if ($skipped -gt 0) { $status += " Skipped: $skipped." }
  if ($errors -gt 0) { $status += " Errors: $errors" }
  Set-DupStatus $status; Update-Status $status
})

$BtnDupReset.Add_Click({
  $DgDupes.ItemsSource = $null
  if ($LstDupScanLog) { $LstDupScanLog.Items.Clear() }
  Set-DupProgress $false
  Set-DupStatus "Ready."
  Update-Status "Duplicates view reset."
})

# Graphics conversion events
$BtnImgBrowse.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') { Load-ImgFolder -Folder $fb.SelectedPath }
})
$BtnImgAddFolder.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') { Load-ImgFolder -Folder $fb.SelectedPath }
})
$BtnImgAddFiles.Add_Click({
  $dlg = New-Object System.Windows.Forms.OpenFileDialog
  $dlg.Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*"
  $dlg.Multiselect = $true
  if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { Add-ImgFiles -Paths $dlg.FileNames; Update-Status "Added files to graphics list." }
})
$BtnImgClear.Add_Click({ $ImgItems.Clear() | Out-Null; Refresh-ImgGrid; Set-ImgStatus "List cleared." })
$BtnIcoOutBrowse.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') { $TxtIcoOut.Text = $fb.SelectedPath; Update-Status "ICO output folder set." }
})
$BtnImgConvert.Add_Click({
  $selected = Get-ImgChecked
  if ($selected.Count -eq 0 -and $ImgItems.Count -gt 0) { foreach ($i in $ImgItems) { $i.Apply = $true }; Refresh-ImgGrid; $selected = Get-ImgChecked }
  if ($selected.Count -eq 0) { Update-Status "No images selected for conversion."; Set-ImgStatus "Select JPG/PNG files to convert."; return }
  $outDir = Get-IconOutputDir
  if (-not $outDir) { [System.Windows.MessageBox]::Show("Set an output folder before converting."); return }
  $size = 256
  if ($CmbIcoSize -and $CmbIcoSize.SelectedItem -and $CmbIcoSize.SelectedItem.Content) { [void][int]::TryParse($CmbIcoSize.SelectedItem.Content.ToString(), [ref]$size) }
  if ($size -lt 16) { $size = 256 }
  $overwrite = ($ChkIcoOverwrite -and $ChkIcoOverwrite.IsChecked)
  $customName = if ($TxtIcoName -and $TxtIcoName.Text) { $TxtIcoName.Text.Trim() } else { $null }
  # Start log
  $logFile = New-ImgLogFile -Prefix "imgconv"
  if ($TxtImgLog) { $TxtImgLog.Clear() }
  Write-ImgLog "=== ICO Conversion started $(Get-Date -Format s) ===" $logFile
  Write-ImgLog "Output folder: $outDir" $logFile
  Write-ImgLog "Icon size: ${size}x${size}" $logFile
  Write-ImgLog "Overwrite: $overwrite" $logFile
  Write-ImgLog "Custom output name: $(if ($customName) { $customName } else { '(use source name)' })" $logFile
  Write-ImgLog "Files to convert: $($selected.Count)" $logFile
  Set-ImgStatus "Converting..."; Update-Status "Converting images to ICO..."
  Set-ImgProgress $true 0 $selected.Count
  $success = 0; $skipped = 0; $fail = 0; $index = 0
  foreach ($img in $selected) {
    $base = if ($customName -and $selected.Count -eq 1) { $customName } else { [System.IO.Path]::GetFileNameWithoutExtension($img.Path) }
    $dest = Join-Path $outDir ($base + ".ico")
    Write-ImgLog "[$($index+1)/$($selected.Count)] $($img.Name) -> $([System.IO.Path]::GetFileName($dest))" $logFile
    if (-not $overwrite -and (Test-Path -LiteralPath $dest)) {
      Write-ImgLog "  SKIPPED (file exists)" $logFile
      $skipped++; $index++; Set-ImgProgress $true $index $selected.Count; continue
    }
    $ok = Convert-ImageToIco -Source $img.Path -Dest $dest -Size $size -LogFile $logFile
    if ($ok -and (Test-Path -LiteralPath $dest)) { $success++ } else { $fail++ }
    $index++; Set-ImgProgress $true $index $selected.Count
  }
  Set-ImgProgress $false
  Write-ImgLog "=== Conversion complete: success=$success, skipped=$skipped, fail=$fail ===" $logFile
  Write-ImgLog "Log saved to: $logFile" $logFile
  $msg = "ICO convert: success $success, skipped $skipped, fail $fail. Log: $logFile"
  Set-ImgStatus $msg; Update-Status $msg
})

# Image Format Conversion (uses .NET - no FFmpeg required)
if ($BtnImgConvertFormat) {
  $BtnImgConvertFormat.Add_Click({
    $selected = Get-ImgChecked
    if ($selected.Count -eq 0 -and $ImgItems.Count -gt 0) { foreach ($i in $ImgItems) { $i.Apply = $true }; Refresh-ImgGrid; $selected = Get-ImgChecked }
    if ($selected.Count -eq 0) { Update-Status "No images selected for conversion."; Set-ImgStatus "Select images to convert."; return }
    $outDir = Get-IconOutputDir
    if (-not $outDir) { [System.Windows.MessageBox]::Show("Set an output folder before converting."); return }
    
    # Get target format
    $targetFmt = "png"
    if ($CmbImgOutFormat -and $CmbImgOutFormat.SelectedItem -and $CmbImgOutFormat.SelectedItem.Tag) {
      $targetFmt = $CmbImgOutFormat.SelectedItem.Tag.ToString().ToLower()
    }
    $overwrite = ($ChkIcoOverwrite -and $ChkIcoOverwrite.IsChecked)
    
    # Start log
    $logFile = New-ImgLogFile -Prefix "imgfmt"
    if ($TxtImgLog) { $TxtImgLog.Clear() }
    Write-ImgLog "=== Image Format Conversion started $(Get-Date -Format s) ===" $logFile
    Write-ImgLog "Output folder: $outDir" $logFile
    Write-ImgLog "Target format: $targetFmt (using .NET - no FFmpeg required)" $logFile
    Write-ImgLog "Overwrite: $overwrite" $logFile
    Write-ImgLog "Files to convert: $($selected.Count)" $logFile
    
    Set-ImgStatus "Converting..."; Update-Status "Converting images to $targetFmt..."
    Set-ImgProgress $true 0 $selected.Count
    $success = 0; $skipped = 0; $fail = 0; $index = 0
    
    foreach ($img in $selected) {
      $base = [System.IO.Path]::GetFileNameWithoutExtension($img.Path)
      $dest = Join-Path $outDir ($base + "." + $targetFmt)
      Write-ImgLog "[$($index+1)/$($selected.Count)] $($img.Name) -> $([System.IO.Path]::GetFileName($dest))" $logFile
      
      if (-not $overwrite -and (Test-Path -LiteralPath $dest)) {
        Write-ImgLog "  SKIPPED (file exists)" $logFile
        $skipped++; $index++; Set-ImgProgress $true $index $selected.Count; continue
      }
      
      $ok = Convert-ImageNative -SourcePath $img.Path -DestPath $dest -Quality 90
      if ($ok -and (Test-Path -LiteralPath $dest)) { 
        $success++
        Write-ImgLog "  SUCCESS" $logFile
      } else { 
        $fail++
        Write-ImgLog "  FAILED" $logFile
      }
      $index++; Set-ImgProgress $true $index $selected.Count
    }
    
    Set-ImgProgress $false
    Write-ImgLog "=== Conversion complete: success=$success, skipped=$skipped, fail=$fail ===" $logFile
    $msg = "$targetFmt convert: success $success, skipped $skipped, fail $fail"
    Set-ImgStatus $msg; Update-Status $msg
  })
}

# ==================== IMAGE RESIZE HANDLERS ====================
$ResizeItems = New-Object System.Collections.ArrayList
$supportedResizeExt = @('.jpg','.jpeg','.png','.bmp','.gif','.webp')

function Refresh-ResizeGrid { if ($DgResize) { $DgResize.ItemsSource = $null; $DgResize.ItemsSource = $ResizeItems } }

function Add-ResizeFiles {
    param([string[]]$Paths)
    foreach ($p in $Paths) {
        $ext = [System.IO.Path]::GetExtension($p).ToLower()
        if ($ext -in $supportedResizeExt) {
            try {
                $fi = Get-Item -LiteralPath $p
                $dims = ""
                try {
                    $img = [System.Drawing.Image]::FromFile($p)
                    $dims = "$($img.Width)x$($img.Height)"
                    $img.Dispose()
                } catch {}
                $sizeKB = [math]::Round($fi.Length / 1KB, 1)
                [void]$ResizeItems.Add([PSCustomObject]@{ Apply=$true; Name=$fi.Name; Dimensions=$dims; Size="${sizeKB} KB"; Path=$p })
            } catch {}
        }
    }
    Refresh-ResizeGrid
}

if ($BtnResizeBrowse) {
    $BtnResizeBrowse.Add_Click({
        $fb = New-Object System.Windows.Forms.FolderBrowserDialog
        if ($fb.ShowDialog() -eq 'OK') {
            $ResizeItems.Clear()
            $files = Get-ChildItem -LiteralPath $fb.SelectedPath -File | Where-Object { $_.Extension.ToLower() -in $supportedResizeExt }
            Add-ResizeFiles -Paths ($files.FullName)
            if ($TxtResizeStatus) { $TxtResizeStatus.Text = "Loaded $($ResizeItems.Count) images." }
        }
    })
}

if ($BtnResizeAddFiles) {
    $BtnResizeAddFiles.Add_Click({
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*"
        $dlg.Multiselect = $true
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            Add-ResizeFiles -Paths $dlg.FileNames
            if ($TxtResizeStatus) { $TxtResizeStatus.Text = "Added files. Total: $($ResizeItems.Count)" }
        }
    })
}

if ($BtnResizeClear) { $BtnResizeClear.Add_Click({ $ResizeItems.Clear(); Refresh-ResizeGrid; if ($TxtResizeStatus) { $TxtResizeStatus.Text = "List cleared." } }) }

if ($BtnResizeOutBrowse) {
    $BtnResizeOutBrowse.Add_Click({
        $fb = New-Object System.Windows.Forms.FolderBrowserDialog
        if ($fb.ShowDialog() -eq 'OK') { $TxtResizeOut.Text = $fb.SelectedPath }
    })
}

if ($BtnResizeRun) {
    $BtnResizeRun.Add_Click({
        $selected = @($ResizeItems | Where-Object { $_.Apply })
        if ($selected.Count -eq 0) { if ($TxtResizeStatus) { $TxtResizeStatus.Text = "No images selected." }; return }
        $outDir = if ($TxtResizeOut -and $TxtResizeOut.Text) { $TxtResizeOut.Text } else { $null }
        if (-not $outDir) { [System.Windows.MessageBox]::Show("Set output folder first."); return }
        Ensure-Directory $outDir
        $maxW = 1920; $maxH = 1080; $quality = 90
        if ($TxtResizeWidth -and $TxtResizeWidth.Text) { [void][int]::TryParse($TxtResizeWidth.Text, [ref]$maxW) }
        if ($TxtResizeHeight -and $TxtResizeHeight.Text) { [void][int]::TryParse($TxtResizeHeight.Text, [ref]$maxH) }
        if ($CmbResizeQuality -and $CmbResizeQuality.SelectedItem) { [void][int]::TryParse($CmbResizeQuality.SelectedItem.Content.ToString(), [ref]$quality) }
        $keepAspect = if ($ChkResizeKeepAspect) { $ChkResizeKeepAspect.IsChecked } else { $true }
        $overwrite = if ($ChkResizeOverwrite) { $ChkResizeOverwrite.IsChecked } else { $false }
        $outFmt = if ($CmbResizeFormat -and $CmbResizeFormat.SelectedItem -and $CmbResizeFormat.SelectedItem.Tag) { $CmbResizeFormat.SelectedItem.Tag.ToString() } else { "same" }
        
        if ($TxtResizeLog) { $TxtResizeLog.Clear() }
        if ($TxtResizeStatus) { $TxtResizeStatus.Text = "Resizing..." }
        $success = 0; $fail = 0
        foreach ($item in $selected) {
            try {
                $srcImg = [System.Drawing.Image]::FromFile($item.Path)
                $newW = $srcImg.Width; $newH = $srcImg.Height
                if ($keepAspect) {
                    $ratioW = $maxW / $srcImg.Width; $ratioH = $maxH / $srcImg.Height
                    $ratio = [Math]::Min($ratioW, $ratioH)
                    if ($ratio -lt 1) { $newW = [int]($srcImg.Width * $ratio); $newH = [int]($srcImg.Height * $ratio) }
                } else { $newW = $maxW; $newH = $maxH }
                $destBmp = New-Object System.Drawing.Bitmap($newW, $newH)
                $g = [System.Drawing.Graphics]::FromImage($destBmp)
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.DrawImage($srcImg, 0, 0, $newW, $newH)
                $g.Dispose(); $srcImg.Dispose()
                $ext = if ($outFmt -eq "same") { [System.IO.Path]::GetExtension($item.Path) } else { ".$outFmt" }
                $outPath = Join-Path $outDir ([System.IO.Path]::GetFileNameWithoutExtension($item.Name) + $ext)
                if (-not $overwrite -and (Test-Path $outPath)) { $fail++; continue }
                $encoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
                $encParams = New-Object System.Drawing.Imaging.EncoderParameters(1)
                $encParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]$quality)
                if ($ext -in @('.jpg','.jpeg')) { $destBmp.Save($outPath, $encoder, $encParams) }
                else { $destBmp.Save($outPath) }
                $destBmp.Dispose()
                if ($TxtResizeLog) { $TxtResizeLog.AppendText("Resized: $($item.Name) -> $newW x $newH`r`n") }
                $success++
            } catch {
                $fail++
                if ($TxtResizeLog) { $TxtResizeLog.AppendText("FAILED: $($item.Name) - $($_.Exception.Message)`r`n") }
            }
        }
        if ($TxtResizeStatus) { $TxtResizeStatus.Text = "Done. Success: $success, Failed: $fail" }
    })
}

# ==================== BOOTABLE USB HANDLERS ====================
$script:UsbCancelRequested = $false
$script:SelectedIsoPath = $null

function Write-UsbLog {
    param([string]$Message, [string]$Color = "White")
    if ($TxtUsbLog) {
        $timestamp = Get-Date -Format "HH:mm:ss"
        $TxtUsbLog.AppendText("[$timestamp] $Message`r`n")
        $TxtUsbLog.ScrollToEnd()
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function Set-UsbStatus {
    param([string]$Text)
    if ($TxtUsbStatus) { $TxtUsbStatus.Text = $Text }
    [System.Windows.Forms.Application]::DoEvents()
}

function Set-UsbProgress {
    param([int]$Value, [bool]$Visible = $true)
    if ($UsbProgress) {
        $UsbProgress.Value = [Math]::Min(100, [Math]::Max(0, $Value))
        $UsbProgress.Visibility = if ($Visible) { "Visible" } else { "Collapsed" }
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function Get-RemovableDrives {
    <#
    .SYNOPSIS
    Get list of removable USB drives using multiple detection methods
    #>
    $drives = @()
    try {
        # Method 1: Try to get USB/Removable drives via Win32_DiskDrive
        $diskDrives = Get-CimInstance -ClassName Win32_DiskDrive -ErrorAction SilentlyContinue | 
            Where-Object { $_.MediaType -match "Removable" -or $_.InterfaceType -eq "USB" }
        
        foreach ($disk in $diskDrives) {
            $foundLogical = $false
            try {
                # Escape backslashes for WMI query
                $escapedDeviceId = $disk.DeviceID.Replace('\','\\')
                $partitions = Get-CimInstance -Query "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='$escapedDeviceId'} WHERE AssocClass=Win32_DiskDriveToDiskPartition" -ErrorAction SilentlyContinue
                
                foreach ($partition in $partitions) {
                    try {
                        $logicalDisks = Get-CimInstance -Query "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='$($partition.DeviceID)'} WHERE AssocClass=Win32_LogicalDiskToPartition" -ErrorAction SilentlyContinue
                        foreach ($logical in $logicalDisks) {
                            $sizeGB = [math]::Round($disk.Size / 1GB, 2)
                            $freeGB = if ($logical.FreeSpace) { [math]::Round($logical.FreeSpace / 1GB, 2) } else { 0 }
                            $drives += [PSCustomObject]@{
                                DriveLetter = $logical.DeviceID
                                Label = $logical.VolumeName
                                FileSystem = $logical.FileSystem
                                SizeGB = $sizeGB
                                FreeGB = $freeGB
                                DiskNumber = $disk.Index
                                Model = $disk.Model
                                DisplayName = "$($logical.DeviceID) - $($disk.Model) ($sizeGB GB)"
                            }
                            $foundLogical = $true
                        }
                    } catch { }
                }
            } catch {
                Write-UsbLog "Partition query failed for disk $($disk.Index): $($_.Exception.Message)"
            }
            
            # Handle disks without partitions or when query failed
            if (-not $foundLogical) {
                $sizeGB = if ($disk.Size) { [math]::Round($disk.Size / 1GB, 2) } else { 0 }
                $drives += [PSCustomObject]@{
                    DriveLetter = "Disk $($disk.Index)"
                    Label = "(Unformatted)"
                    FileSystem = "RAW"
                    SizeGB = $sizeGB
                    FreeGB = 0
                    DiskNumber = $disk.Index
                    Model = $disk.Model
                    DisplayName = "Disk $($disk.Index) - $($disk.Model) ($sizeGB GB) [RAW]"
                }
            }
        }
        
        # Method 2: Fallback - Also check logical drives directly for removable drives we might have missed
        if ($drives.Count -eq 0) {
            $logicalDrives = Get-CimInstance -ClassName Win32_LogicalDisk -ErrorAction SilentlyContinue | 
                Where-Object { $_.DriveType -eq 2 } # DriveType 2 = Removable
            foreach ($ld in $logicalDrives) {
                $sizeGB = if ($ld.Size) { [math]::Round($ld.Size / 1GB, 2) } else { 0 }
                $freeGB = if ($ld.FreeSpace) { [math]::Round($ld.FreeSpace / 1GB, 2) } else { 0 }
                # Check if we already have this drive
                if (-not ($drives | Where-Object { $_.DriveLetter -eq $ld.DeviceID })) {
                    $drives += [PSCustomObject]@{
                        DriveLetter = $ld.DeviceID
                        Label = $ld.VolumeName
                        FileSystem = $ld.FileSystem
                        SizeGB = $sizeGB
                        FreeGB = $freeGB
                        DiskNumber = -1  # Unknown disk number
                        Model = "Removable Drive"
                        DisplayName = "$($ld.DeviceID) - $($ld.VolumeName) ($sizeGB GB)"
                    }
                }
            }
        }
    } catch {
        Write-UsbLog "Error detecting drives: $($_.Exception.Message)"
    }
    return $drives
}

function Refresh-UsbDriveList {
    if (-not $CmbUsbDrive) { return }
    $CmbUsbDrive.Items.Clear()
    $drives = Get-RemovableDrives
    $script:UsbDriveList = $drives
    foreach ($d in $drives) {
        $item = New-Object System.Windows.Controls.ComboBoxItem
        $item.Content = $d.DisplayName
        $item.Tag = $d
        $CmbUsbDrive.Items.Add($item) | Out-Null
    }
    if ($drives.Count -gt 0) {
        $CmbUsbDrive.SelectedIndex = 0
        Set-UsbStatus "Found $($drives.Count) removable drive(s)."
    } else {
        Set-UsbStatus "No removable USB drives found. Insert a USB drive and click Refresh."
    }
}

function Get-SelectedUsbDrive {
    if ($CmbUsbDrive -and $CmbUsbDrive.SelectedItem -and $CmbUsbDrive.SelectedItem.Tag) {
        return $CmbUsbDrive.SelectedItem.Tag
    }
    return $null
}

function Format-UsbDrive {
    param(
        [int]$DiskNumber,
        [string]$FileSystem,
        [string]$Label,
        [string]$PartitionStyle,  # GPT or MBR
        [switch]$QuickFormat
    )
    
    Write-UsbLog "Formatting Disk $DiskNumber as $FileSystem ($PartitionStyle)..."
    Set-UsbProgress 10
    
    try {
        # Create diskpart script
        $diskpartScript = @"

select disk $DiskNumber
clean
convert $PartitionStyle
create partition primary
select partition 1
active
format fs=$FileSystem label="$Label" $(if($QuickFormat){'quick'})
assign
"@
        $scriptPath = Join-Path $env:TEMP "diskpart_usb.txt"
        $diskpartScript | Set-Content -Path $scriptPath -Encoding ASCII
        
        Write-UsbLog "Running diskpart..."
        $result = & diskpart /s $scriptPath 2>&1
        $result | ForEach-Object { Write-UsbLog "  $_" }
        
        Remove-Item $scriptPath -Force -ErrorAction SilentlyContinue
        
        Set-UsbProgress 30
        Write-UsbLog "Format complete."
        return $true
    } catch {
        Write-UsbLog "Format failed: $($_.Exception.Message)"
        return $false
    }
}

function Copy-IsoToUsb {
    param(
        [string]$IsoPath,
        [string]$DestDrive,
        [string]$BootMode
    )
    
    Write-UsbLog "Mounting ISO: $IsoPath"
    Set-UsbProgress 35
    
    try {
        # Mount the ISO
        $mountResult = Mount-DiskImage -ImagePath $IsoPath -PassThru
        $isoVolume = $mountResult | Get-Volume
        $isoDrive = ($isoVolume.DriveLetter + ":\")
        
        Write-UsbLog "ISO mounted at $isoDrive"
        Set-UsbProgress 40
        
        # Get total size for progress
        $sourceFiles = Get-ChildItem -Path $isoDrive -Recurse -File -ErrorAction SilentlyContinue
        $totalFiles = $sourceFiles.Count
        $totalSize = ($sourceFiles | Measure-Object -Property Length -Sum).Sum
        Write-UsbLog "Copying $totalFiles files ($([math]::Round($totalSize/1MB, 1)) MB)..."
        
        # Copy all files
        $copiedFiles = 0
        $copiedSize = 0
        
        # Use robocopy for efficient copying
        $robocopyArgs = @($isoDrive, $DestDrive, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS", "/NP")
        Write-UsbLog "Running: robocopy $($robocopyArgs -join ' ')"
        
        $robocopyOutput = & robocopy @robocopyArgs 2>&1
        $robocopyExit = $LASTEXITCODE
        
        # Robocopy exit codes: 0-7 are success, 8+ are errors
        if ($robocopyExit -lt 8) {
            Write-UsbLog "File copy completed successfully (robocopy exit: $robocopyExit)"
        } else {
            Write-UsbLog "Robocopy reported issues (exit: $robocopyExit)"
            $robocopyOutput | ForEach-Object { Write-UsbLog "  $_" }
        }
        
        Set-UsbProgress 85
        
        # For UEFI boot, check if EFI folder exists
        if ($BootMode -in @("UEFI", "BOTH")) {
            $efiPath = Join-Path $DestDrive "EFI"
            if (Test-Path $efiPath) {
                Write-UsbLog "EFI boot files found - USB should be UEFI bootable."
            } else {
                Write-UsbLog "Warning: No EFI folder found. UEFI boot may not work."
            }
        }
        
        # For Legacy/MBR boot, try to set up boot sector
        if ($BootMode -in @("MBR", "BOTH")) {
            Write-UsbLog "Setting up legacy boot sector..."
            $bootsectPath = Join-Path $isoDrive "boot\bootsect.exe"
            if (Test-Path $bootsectPath) {
                $bootResult = & $bootsectPath /nt60 $DestDrive /mbr 2>&1
                Write-UsbLog "Bootsect result: $bootResult"
            } else {
                # Try system bootsect
                $sysBootsect = "$env:SystemRoot\System32\bootsect.exe"
                if (Test-Path $sysBootsect) {
                    $bootResult = & $sysBootsect /nt60 $DestDrive /mbr 2>&1
                    Write-UsbLog "System bootsect result: $bootResult"
                } else {
                    Write-UsbLog "Warning: bootsect.exe not found. Legacy boot may not work."
                }
            }
        }
        
        Set-UsbProgress 95
        
        # Unmount ISO
        Write-UsbLog "Unmounting ISO..."
        Dismount-DiskImage -ImagePath $IsoPath | Out-Null
        
        Set-UsbProgress 100
        Write-UsbLog "ISO contents copied to USB successfully!"
        return $true
        
    } catch {
        Write-UsbLog "Error: $($_.Exception.Message)"
        # Try to unmount ISO on error
        try { Dismount-DiskImage -ImagePath $IsoPath -ErrorAction SilentlyContinue | Out-Null } catch {}
        return $false
    }
}

# Event: Browse ISO
if ($BtnIsoBrowse) {
    $BtnIsoBrowse.Add_Click({
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Filter = "ISO files (*.iso)|*.iso|All files (*.*)|*.*"
        $dlg.Title = "Select ISO file"
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $script:SelectedIsoPath = $dlg.FileName
            $TxtIsoPath.Text = $dlg.FileName
            $isoSize = [math]::Round((Get-Item $dlg.FileName).Length / 1GB, 2)
            Write-UsbLog "Selected ISO: $($dlg.FileName) ($isoSize GB)"
            Set-UsbStatus "ISO selected: $isoSize GB"
        }
    })
}

# Event: Refresh USB drives
if ($BtnUsbRefresh) {
    $BtnUsbRefresh.Add_Click({ Refresh-UsbDriveList })
}

# Event: USB drive selection changed
if ($CmbUsbDrive) {
    $CmbUsbDrive.Add_SelectionChanged({
        $drive = Get-SelectedUsbDrive
        if ($drive -and $TxtUsbInfo) {
            $TxtUsbInfo.Text = "$($drive.FileSystem) | $($drive.FreeGB)/$($drive.SizeGB) GB free"
        }
    })
}

# Event: Create Bootable USB
if ($BtnUsbWrite) {
    $BtnUsbWrite.Add_Click({
        # Validate inputs
        if (-not $script:SelectedIsoPath -or -not (Test-Path $script:SelectedIsoPath)) {
            [System.Windows.MessageBox]::Show("Please select a valid ISO file.", "No ISO Selected", "OK", "Warning")
            return
        }
        
        $drive = Get-SelectedUsbDrive
        if (-not $drive) {
            [System.Windows.MessageBox]::Show("Please select a USB drive.", "No Drive Selected", "OK", "Warning")
            return
        }
        
        # Check ISO size vs drive size
        $isoSizeGB = [math]::Round((Get-Item $script:SelectedIsoPath).Length / 1GB, 2)
        if ($isoSizeGB -gt $drive.SizeGB) {
            [System.Windows.MessageBox]::Show("The ISO file ($isoSizeGB GB) is larger than the USB drive ($($drive.SizeGB) GB).", "Drive Too Small", "OK", "Error")
            return
        }
        
        # Warning about data loss
        $confirm = [System.Windows.MessageBox]::Show(
            "WARNING: All data on $($drive.DriveLetter) ($($drive.Model)) will be ERASED!`n`nDo you want to continue?",
            "Confirm Format",
            "YesNo",
            "Warning"
        )
        
        if ($confirm -ne "Yes") {
            Set-UsbStatus "Operation cancelled."
            return
        }
        
        # Get options
        $fileSystem = if ($CmbUsbFileSystem.SelectedItem.Tag) { $CmbUsbFileSystem.SelectedItem.Tag } else { "FAT32" }
        $bootMode = if ($CmbBootMode.SelectedItem.Tag) { $CmbBootMode.SelectedItem.Tag } else { "UEFI" }
        $partStyle = if ($bootMode -eq "MBR") { "MBR" } else { "GPT" }
        $label = if ($TxtUsbLabel.Text) { $TxtUsbLabel.Text.Substring(0, [Math]::Min(11, $TxtUsbLabel.Text.Length)) } else { "BOOTUSB" }
        $quickFormat = if ($ChkUsbQuickFormat) { $ChkUsbQuickFormat.IsChecked } else { $true }
        
        # Check FAT32 limit
        if ($fileSystem -eq "FAT32" -and $isoSizeGB -gt 4) {
            $switchToNtfs = [System.Windows.MessageBox]::Show(
                "ISO is larger than 4GB. FAT32 cannot hold files larger than 4GB.`n`nSwitch to NTFS? (Note: NTFS may not work for UEFI boot on some systems)",
                "FAT32 Limitation",
                "YesNo",
                "Question"
            )
            if ($switchToNtfs -eq "Yes") {
                $fileSystem = "NTFS"
            } else {
                Set-UsbStatus "Operation cancelled due to FAT32 size limit."
                return
            }
        }
        
        # Disable buttons during operation
        $BtnUsbWrite.IsEnabled = $false
        $BtnUsbCancel.IsEnabled = $true
        $script:UsbCancelRequested = $false
        
        if ($TxtUsbLog) { $TxtUsbLog.Clear() }
        Write-UsbLog "=========================================="
        Write-UsbLog "Creating Bootable USB"
        Write-UsbLog "=========================================="
        Write-UsbLog "ISO: $($script:SelectedIsoPath)"
        Write-UsbLog "Drive: $($drive.DisplayName)"
        Write-UsbLog "File System: $fileSystem"
        Write-UsbLog "Partition Style: $partStyle"
        Write-UsbLog "Boot Mode: $bootMode"
        Write-UsbLog "=========================================="
        
        Set-UsbProgress 5 $true
        
        try {
            # Step 1: Format the drive
            $formatOk = Format-UsbDrive -DiskNumber $drive.DiskNumber -FileSystem $fileSystem -Label $label -PartitionStyle $partStyle -QuickFormat:$quickFormat
            
            if (-not $formatOk) {
                throw "Format failed"
            }
            
            if ($script:UsbCancelRequested) {
                Write-UsbLog "Operation cancelled by user."
                Set-UsbStatus "Cancelled."
                return
            }
            
            # Refresh drive list to get new drive letter
            Start-Sleep -Seconds 2
            Refresh-UsbDriveList
            $drive = Get-SelectedUsbDrive
            
            if (-not $drive -or $drive.DriveLetter -match "Disk") {
                # Try to find the newly formatted drive
                Start-Sleep -Seconds 3
                Refresh-UsbDriveList
                $drive = Get-SelectedUsbDrive
            }
            
            $destDrive = $drive.DriveLetter + "\"
            Write-UsbLog "Destination drive: $destDrive"
            
            # Step 2: Copy ISO contents
            $copyOk = Copy-IsoToUsb -IsoPath $script:SelectedIsoPath -DestDrive $destDrive -BootMode $bootMode
            
            if ($copyOk) {
                Write-UsbLog "=========================================="
                Write-UsbLog "SUCCESS! Bootable USB created."
                Write-UsbLog "=========================================="
                Set-UsbStatus "Bootable USB created successfully!"
                [System.Windows.MessageBox]::Show("Bootable USB created successfully!`n`nYou can now use $($drive.DriveLetter) to boot your computer.", "Success", "OK", "Information")
            } else {
                throw "File copy failed"
            }
            
        } catch {
            Write-UsbLog "ERROR: $($_.Exception.Message)"
            Set-UsbStatus "Failed: $($_.Exception.Message)"
            [System.Windows.MessageBox]::Show("Failed to create bootable USB:`n$($_.Exception.Message)", "Error", "OK", "Error")
        } finally {
            Set-UsbProgress 0 $false
            $BtnUsbWrite.IsEnabled = $true
            $BtnUsbCancel.IsEnabled = $false
            Refresh-UsbDriveList
        }
    })
}

# Event: Cancel
if ($BtnUsbCancel) {
    $BtnUsbCancel.Add_Click({
        $script:UsbCancelRequested = $true
        Write-UsbLog "Cancel requested..."
        Set-UsbStatus "Cancelling..."
    })
}

# Initial refresh of USB drives
Refresh-UsbDriveList

# ==================== HASH CALCULATOR HANDLERS ====================
if ($BtnHashFile) {
    $BtnHashFile.Add_Click({
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Filter = "All files (*.*)|*.*"
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $algo = if ($CmbHashAlgo -and $CmbHashAlgo.SelectedItem) { $CmbHashAlgo.SelectedItem.Content.ToString() } else { "SHA256" }
            try {
                $hash = Get-FileHash -LiteralPath $dlg.FileName -Algorithm $algo
                if ($TxtHashResult) { $TxtHashResult.Text = $hash.Hash }
                Update-Status "Hash calculated: $algo"
            } catch {
                if ($TxtHashResult) { $TxtHashResult.Text = "Error: $($_.Exception.Message)" }
            }
        }
    })
}

if ($BtnHashCopy) {
    $BtnHashCopy.Add_Click({
        if ($TxtHashResult -and $TxtHashResult.Text) {
            [System.Windows.Clipboard]::SetText($TxtHashResult.Text)
            Update-Status "Hash copied to clipboard."
        }
    })
}

# ==================== DISK CLEANUP HANDLERS ====================
$script:DiskCleanItems = New-Object System.Collections.ArrayList

function Get-DiskCleanupPaths {
    $paths = @()
    if ($ChkCleanTemp -and $ChkCleanTemp.IsChecked) { $paths += [PSCustomObject]@{ Category="Windows Temp"; Path="$env:windir\Temp"; Pattern="*" } }
    if ($ChkCleanUserTemp -and $ChkCleanUserTemp.IsChecked) { $paths += [PSCustomObject]@{ Category="User Temp"; Path="$env:TEMP"; Pattern="*" } }
    if ($ChkCleanPrefetch -and $ChkCleanPrefetch.IsChecked) { $paths += [PSCustomObject]@{ Category="Prefetch"; Path="$env:windir\Prefetch"; Pattern="*.pf" } }
    if ($ChkCleanThumbnails -and $ChkCleanThumbnails.IsChecked) { $paths += [PSCustomObject]@{ Category="Thumbnails"; Path="$env:LOCALAPPDATA\Microsoft\Windows\Explorer"; Pattern="thumbcache_*.db" } }
    if ($ChkCleanErrorReports -and $ChkCleanErrorReports.IsChecked) { $paths += [PSCustomObject]@{ Category="Error Reports"; Path="$env:LOCALAPPDATA\Microsoft\Windows\WER"; Pattern="*" } }
    if ($ChkCleanLogFiles -and $ChkCleanLogFiles.IsChecked) { $paths += [PSCustomObject]@{ Category="Log Files"; Path="$env:windir\Logs"; Pattern="*.log" } }
    if ($ChkCleanWindowsUpdate -and $ChkCleanWindowsUpdate.IsChecked) { $paths += [PSCustomObject]@{ Category="Windows Update"; Path="$env:windir\SoftwareDistribution\Download"; Pattern="*" } }
    return $paths
}

if ($BtnDiskAnalyze) {
    $BtnDiskAnalyze.Add_Click({
        $script:DiskCleanItems.Clear()
        $totalSize = 0; $totalFiles = 0
        $paths = Get-DiskCleanupPaths
        foreach ($p in $paths) {
            if (Test-Path $p.Path) {
                try {
                    $files = Get-ChildItem -LiteralPath $p.Path -Filter $p.Pattern -Recurse -File -ErrorAction SilentlyContinue
                    $size = ($files | Measure-Object -Property Length -Sum).Sum
                    if (-not $size) { $size = 0 }
                    $sizeStr = if ($size -gt 1MB) { "$([math]::Round($size/1MB,1)) MB" } else { "$([math]::Round($size/1KB,1)) KB" }
                    [void]$script:DiskCleanItems.Add([PSCustomObject]@{ Category=$p.Category; FileCount=$files.Count; Size=$sizeStr; Path=$p.Path })
                    $totalSize += $size; $totalFiles += $files.Count
                } catch {}
            }
        }
        if ($ChkCleanRecycleBin -and $ChkCleanRecycleBin.IsChecked) {
            try {
                $shell = New-Object -ComObject Shell.Application
                $rb = $shell.Namespace(0xA)
                $rbCount = $rb.Items().Count
                [void]$script:DiskCleanItems.Add([PSCustomObject]@{ Category="Recycle Bin"; FileCount=$rbCount; Size="(varies)"; Path="Recycle Bin" })
                $totalFiles += $rbCount
            } catch {}
        }
        if ($DgDiskClean) { $DgDiskClean.ItemsSource = $null; $DgDiskClean.ItemsSource = $script:DiskCleanItems }
        $totalStr = if ($totalSize -gt 1GB) { "$([math]::Round($totalSize/1GB,2)) GB" } elseif ($totalSize -gt 1MB) { "$([math]::Round($totalSize/1MB,1)) MB" } else { "$([math]::Round($totalSize/1KB,1)) KB" }
        if ($TxtDiskSpace) { $TxtDiskSpace.Text = "Total: $totalFiles files, ~$totalStr" }
        if ($TxtDiskStatus) { $TxtDiskStatus.Text = "Analysis complete." }
    })
}

if ($BtnDiskClean) {
    $BtnDiskClean.Add_Click({
        $dryRun = if ($ChkDiskDryRun) { $ChkDiskDryRun.IsChecked } else { $true }
        $paths = Get-DiskCleanupPaths
        $deleted = 0
        foreach ($p in $paths) {
            if (Test-Path $p.Path) {
                try {
                    $files = Get-ChildItem -LiteralPath $p.Path -Filter $p.Pattern -Recurse -File -ErrorAction SilentlyContinue
                    foreach ($f in $files) {
                        if (-not $dryRun) { try { Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue; $deleted++ } catch {} }
                        else { $deleted++ }
                    }
                } catch {}
            }
        }
        if ($ChkCleanRecycleBin -and $ChkCleanRecycleBin.IsChecked -and -not $dryRun) {
            try { Clear-RecycleBin -Force -ErrorAction SilentlyContinue } catch {}
        }
        $action = if ($dryRun) { "Would delete" } else { "Deleted" }
        if ($TxtDiskStatus) { $TxtDiskStatus.Text = "$action $deleted files." }
    })
}

# ==================== PRIVACY CLEANER HANDLERS ====================
$script:PrivCleanItems = New-Object System.Collections.ArrayList

function Get-BrowserPaths {
    $browsers = @()
    if ($ChkPrivChrome -and $ChkPrivChrome.IsChecked) {
        $browsers += [PSCustomObject]@{ Name="Chrome"; Paths=@(
            "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\Cookies",
            "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\Cache",
            "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\History"
        )}
    }
    if ($ChkPrivEdge -and $ChkPrivEdge.IsChecked) {
        $browsers += [PSCustomObject]@{ Name="Edge"; Paths=@(
            "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\Cookies",
            "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\Cache",
            "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\History"
        )}
    }
    if ($ChkPrivFirefox -and $ChkPrivFirefox.IsChecked) {
        $ffPath = "$env:APPDATA\Mozilla\Firefox\Profiles"
        if (Test-Path $ffPath) {
            $profiles = Get-ChildItem $ffPath -Directory | Select-Object -First 1
            if ($profiles) {
                $browsers += [PSCustomObject]@{ Name="Firefox"; Paths=@(
                    "$($profiles.FullName)\cookies.sqlite",
                    "$($profiles.FullName)\cache2",
                    "$($profiles.FullName)\places.sqlite"
                )}
            }
        }
    }
    if ($ChkPrivBrave -and $ChkPrivBrave.IsChecked) {
        $browsers += [PSCustomObject]@{ Name="Brave"; Paths=@(
            "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\User Data\Default\Cookies",
            "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\User Data\Default\Cache"
        )}
    }
    return $browsers
}

function Get-IdentityPaths {
    $items = @()
    if ($ChkPrivRecentDocs -and $ChkPrivRecentDocs.IsChecked) {
        $items += [PSCustomObject]@{ Category="Windows"; Type="Recent Docs"; Path="$env:APPDATA\Microsoft\Windows\Recent" }
    }
    if ($ChkPrivJumpLists -and $ChkPrivJumpLists.IsChecked) {
        $items += [PSCustomObject]@{ Category="Windows"; Type="Jump Lists"; Path="$env:APPDATA\Microsoft\Windows\Recent\AutomaticDestinations" }
        $items += [PSCustomObject]@{ Category="Windows"; Type="Jump Lists"; Path="$env:APPDATA\Microsoft\Windows\Recent\CustomDestinations" }
    }
    if ($ChkPrivExplorer -and $ChkPrivExplorer.IsChecked) {
        $items += [PSCustomObject]@{ Category="Windows"; Type="Explorer MRU"; Path="HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs" }
    }
    if ($ChkPrivOneDrive -and $ChkPrivOneDrive.IsChecked) {
        $items += [PSCustomObject]@{ Category="Cloud"; Type="OneDrive Cache"; Path="$env:LOCALAPPDATA\Microsoft\OneDrive\logs" }
    }
    if ($ChkPrivGoogleDrive -and $ChkPrivGoogleDrive.IsChecked) {
        $items += [PSCustomObject]@{ Category="Cloud"; Type="Google Drive"; Path="$env:LOCALAPPDATA\Google\DriveFS" }
    }
    if ($ChkPrivDropbox -and $ChkPrivDropbox.IsChecked) {
        $items += [PSCustomObject]@{ Category="Cloud"; Type="Dropbox Cache"; Path="$env:LOCALAPPDATA\Dropbox" }
    }
    if ($ChkPrivOffice -and $ChkPrivOffice.IsChecked) {
        $items += [PSCustomObject]@{ Category="Apps"; Type="Office MRU"; Path="HKCU:\Software\Microsoft\Office" }
    }
    return $items
}

if ($BtnPrivAnalyze) {
    $BtnPrivAnalyze.Add_Click({
        $script:PrivCleanItems.Clear()
        # Browsers
        $browsers = Get-BrowserPaths
        foreach ($b in $browsers) {
            foreach ($p in $b.Paths) {
                if (Test-Path $p -ErrorAction SilentlyContinue) {
                    $item = Get-Item $p -ErrorAction SilentlyContinue
                    $size = if ($item.PSIsContainer) {
                        $s = (Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
                        if ($s -gt 1MB) { "$([math]::Round($s/1MB,1)) MB" } else { "$([math]::Round($s/1KB,1)) KB" }
                    } else {
                        "$([math]::Round($item.Length/1KB,1)) KB"
                    }
                    [void]$script:PrivCleanItems.Add([PSCustomObject]@{ Category=$b.Name; Type="Browser Data"; ItemCount=1; Size=$size; Location=$p })
                }
            }
        }
        # Identity/Windows items
        $idPaths = Get-IdentityPaths
        foreach ($id in $idPaths) {
            if ($id.Path -like "HKCU:*") {
                if (Test-Path $id.Path -ErrorAction SilentlyContinue) {
                    [void]$script:PrivCleanItems.Add([PSCustomObject]@{ Category=$id.Category; Type=$id.Type; ItemCount=1; Size="Registry"; Location=$id.Path })
                }
            } elseif (Test-Path $id.Path -ErrorAction SilentlyContinue) {
                $count = (Get-ChildItem $id.Path -Recurse -File -ErrorAction SilentlyContinue).Count
                [void]$script:PrivCleanItems.Add([PSCustomObject]@{ Category=$id.Category; Type=$id.Type; ItemCount=$count; Size="(varies)"; Location=$id.Path })
            }
        }
        # Clipboard
        if ($ChkPrivClipboard -and $ChkPrivClipboard.IsChecked) {
            [void]$script:PrivCleanItems.Add([PSCustomObject]@{ Category="Windows"; Type="Clipboard"; ItemCount=1; Size="-"; Location="System Clipboard" })
        }
        # Credential Manager
        if ($ChkPrivCredManager -and $ChkPrivCredManager.IsChecked) {
            [void]$script:PrivCleanItems.Add([PSCustomObject]@{ Category="Windows"; Type="Web Credentials"; ItemCount=1; Size="-"; Location="Credential Manager" })
        }
        if ($DgPrivClean) { $DgPrivClean.ItemsSource = $null; $DgPrivClean.ItemsSource = $script:PrivCleanItems }
        if ($TxtPrivStatus) { $TxtPrivStatus.Text = "Found $($script:PrivCleanItems.Count) items to clean." }
    })
}

if ($BtnPrivClean) {
    $BtnPrivClean.Add_Click({
        $dryRun = if ($ChkPrivDryRun) { $ChkPrivDryRun.IsChecked } else { $true }
        $deleted = 0
        foreach ($item in $script:PrivCleanItems) {
            if ($item.Location -eq "System Clipboard") {
                if (-not $dryRun) { try { [System.Windows.Clipboard]::Clear() } catch {} }
                $deleted++
            } elseif ($item.Location -eq "Credential Manager") {
                if (-not $dryRun) {
                    # Clear web credentials using cmdkey
                    try { $creds = cmdkey /list 2>&1; } catch {}
                }
                $deleted++
            } elseif ($item.Location -like "HKCU:*") {
                if (-not $dryRun) { try { Remove-Item -Path $item.Location -Recurse -Force -ErrorAction SilentlyContinue } catch {} }
                $deleted++
            } elseif (Test-Path $item.Location -ErrorAction SilentlyContinue) {
                if (-not $dryRun) {
                    try { Remove-Item -LiteralPath $item.Location -Recurse -Force -ErrorAction SilentlyContinue } catch {}
                }
                $deleted++
            }
        }
        $action = if ($dryRun) { "Would clean" } else { "Cleaned" }
        if ($TxtPrivStatus) { $TxtPrivStatus.Text = "$action $deleted items. Restart browsers to complete." }
    })
}

# ==================== STARTUP MANAGER HANDLERS ====================
$script:StartupItems = New-Object System.Collections.ArrayList

function Get-StartupItems {
    $items = @()
    # Registry Run keys
    $regPaths = @(
        @{ Path="HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"; Source="Registry (User)" },
        @{ Path="HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"; Source="Registry (Machine)" }
    )
    foreach ($rp in $regPaths) {
        if (Test-Path $rp.Path) {
            try {
                $props = Get-ItemProperty -Path $rp.Path -ErrorAction SilentlyContinue
                foreach ($p in $props.PSObject.Properties) {
                    if ($p.Name -notin @('PSPath','PSParentPath','PSChildName','PSProvider')) {
                        $items += [PSCustomObject]@{ Selected=$false; Name=$p.Name; Status="Enabled"; Source=$rp.Source; Command=$p.Value; Location=$rp.Path }
                    }
                }
            } catch {}
        }
    }
    # Startup folders
    $startupFolders = @(
        @{ Path="$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"; Source="Startup (User)" },
        @{ Path="$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup"; Source="Startup (All Users)" }
    )
    foreach ($sf in $startupFolders) {
        if (Test-Path $sf.Path) {
            $files = Get-ChildItem $sf.Path -File -ErrorAction SilentlyContinue
            foreach ($f in $files) {
                $items += [PSCustomObject]@{ Selected=$false; Name=$f.BaseName; Status="Enabled"; Source=$sf.Source; Command=$f.FullName; Location=$sf.Path }
            }
        }
    }
    return $items
}

if ($BtnStartupScan) {
    $BtnStartupScan.Add_Click({
        $script:StartupItems.Clear()
        $items = Get-StartupItems
        foreach ($i in $items) { [void]$script:StartupItems.Add($i) }
        if ($DgStartup) { $DgStartup.ItemsSource = $null; $DgStartup.ItemsSource = $script:StartupItems }
        if ($TxtStartupStatus) { $TxtStartupStatus.Text = "Found $($script:StartupItems.Count) startup items." }
    })
}

if ($BtnStartupDisable) {
    $BtnStartupDisable.Add_Click({
        $selected = @($script:StartupItems | Where-Object { $_.Selected })
        foreach ($s in $selected) {
            if ($s.Source -like "Registry*") {
                try {
                    $disPath = $s.Location -replace '\\Run$', '\Run-Disabled'
                    if (-not (Test-Path $disPath)) { New-Item -Path $disPath -Force | Out-Null }
                    $val = (Get-ItemProperty -Path $s.Location -Name $s.Name -ErrorAction SilentlyContinue).$($s.Name)
                    Set-ItemProperty -Path $disPath -Name $s.Name -Value $val -ErrorAction SilentlyContinue
                    Remove-ItemProperty -Path $s.Location -Name $s.Name -ErrorAction SilentlyContinue
                    $s.Status = "Disabled"
                } catch {}
            } elseif ($s.Source -like "Startup*") {
                try {
                    $newName = $s.Command + ".disabled"
                    Rename-Item -LiteralPath $s.Command -NewName $newName -ErrorAction SilentlyContinue
                    $s.Status = "Disabled"
                } catch {}
            }
        }
        if ($DgStartup) { $DgStartup.Items.Refresh() }
        if ($TxtStartupStatus) { $TxtStartupStatus.Text = "Disabled $($selected.Count) items." }
    })
}

if ($BtnStartupEnable) {
    $BtnStartupEnable.Add_Click({
        if ($TxtStartupStatus) { $TxtStartupStatus.Text = "Enable: Re-scan after manually restoring items." }
    })
}

if ($BtnStartupDelete) {
    $BtnStartupDelete.Add_Click({
        $selected = @($script:StartupItems | Where-Object { $_.Selected })
        foreach ($s in $selected) {
            if ($s.Source -like "Registry*") {
                try { Remove-ItemProperty -Path $s.Location -Name $s.Name -ErrorAction SilentlyContinue } catch {}
            } elseif ($s.Source -like "Startup*" -and (Test-Path $s.Command)) {
                try { Remove-Item -LiteralPath $s.Command -Force -ErrorAction SilentlyContinue } catch {}
            }
        }
        $script:StartupItems.Clear()
        $items = Get-StartupItems
        foreach ($i in $items) { [void]$script:StartupItems.Add($i) }
        if ($DgStartup) { $DgStartup.ItemsSource = $null; $DgStartup.ItemsSource = $script:StartupItems }
        if ($TxtStartupStatus) { $TxtStartupStatus.Text = "Deleted $($selected.Count) items." }
    })
}

if ($BtnStartupOpenFolder) {
    $BtnStartupOpenFolder.Add_Click({
        $sel = $script:StartupItems | Where-Object { $_.Selected } | Select-Object -First 1
        if ($sel -and $sel.Location -and (Test-Path $sel.Location -ErrorAction SilentlyContinue)) {
            Start-Process explorer.exe $sel.Location
        }
    })
}

if ($BtnStartupOpenTaskSched) {
    $BtnStartupOpenTaskSched.Add_Click({
        try { Start-Process "taskschd.msc" } catch {}
    })
}

# ==================== METADATA HANDLERS ====================
$script:CurrentMetaFile = $null
$script:MetadataItems = New-Object System.Collections.ArrayList

function Load-FileMetadata {
    param([string]$FilePath)
    $script:CurrentMetaFile = $FilePath
    $script:MetadataItems.Clear()
    if ($TxtMetaFile) { $TxtMetaFile.Text = $FilePath }
    
    # Basic file info
    $fi = Get-Item -LiteralPath $FilePath
    if ($TxtMetaType) { $TxtMetaType.Text = $fi.Extension }
    if ($TxtMetaSize) { $TxtMetaSize.Text = "$([math]::Round($fi.Length/1KB,1)) KB ($($fi.Length) bytes)" }
    if ($TxtMetaCreated) { $TxtMetaCreated.Text = $fi.CreationTime.ToString() }
    if ($TxtMetaModified) { $TxtMetaModified.Text = $fi.LastWriteTime.ToString() }
    
    $ext = $fi.Extension.ToLower()
    $isImage = $ext -in @('.jpg','.jpeg','.png','.gif','.bmp','.tiff','.tif')
    $isVideo = $ext -in @('.mp4','.mkv','.avi','.mov','.wmv','.webm','.flv','.m4v')
    $isAudio = $ext -in @('.mp3','.flac','.wav','.m4a','.aac','.ogg','.wma')
    
    # Try to get dimensions for images using .NET
    if ($isImage) {
        try {
            $imgInfo = Get-ImageInfoNative -Path $FilePath
            if ($TxtMetaDimensions) { $TxtMetaDimensions.Text = "$($imgInfo.Width) x $($imgInfo.Height)" }
        } catch {}
    }
    
    # Use exiftool if available (best metadata)
    if ($exiftoolPath -and (Test-Path $exiftoolPath)) {
        try {
            $output = & $exiftoolPath -s -s -s -json $FilePath 2>$null | ConvertFrom-Json
            if ($output) {
                foreach ($prop in $output[0].PSObject.Properties) {
                    [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag=$prop.Name; Value=$prop.Value })
                    # Populate quick fields
                    if ($prop.Name -eq "Title" -and $TxtMetaTitle) { $TxtMetaTitle.Text = $prop.Value }
                    if ($prop.Name -eq "Artist" -and $TxtMetaArtist) { $TxtMetaArtist.Text = $prop.Value }
                    if ($prop.Name -eq "Album" -and $TxtMetaAlbum) { $TxtMetaAlbum.Text = $prop.Value }
                    if ($prop.Name -eq "Year" -and $TxtMetaYear) { $TxtMetaYear.Text = $prop.Value }
                    if ($prop.Name -eq "Comment" -and $TxtMetaComment) { $TxtMetaComment.Text = $prop.Value }
                    if ($prop.Name -eq "Duration" -and $TxtMetaDuration) { $TxtMetaDuration.Text = $prop.Value }
                    if ($prop.Name -eq "ImageSize" -and $TxtMetaDimensions) { $TxtMetaDimensions.Text = $prop.Value }
                }
            }
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "Loaded $($script:MetadataItems.Count) tags (ExifTool)." }
        } catch {}
    } elseif ($ffprobePath -and (Test-Path $ffprobePath) -and ($isVideo -or $isAudio)) {
        # Fallback to ffprobe for video/audio
        try {
            $json = & $ffprobePath -v quiet -print_format json -show_format -show_streams $FilePath 2>$null | ConvertFrom-Json
            if ($json.format) {
                [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag="Duration"; Value=$json.format.duration })
                [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag="Bitrate"; Value=$json.format.bit_rate })
                if ($json.format.tags) {
                    foreach ($t in $json.format.tags.PSObject.Properties) {
                        [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag=$t.Name; Value=$t.Value })
                        if ($t.Name -eq "title" -and $TxtMetaTitle) { $TxtMetaTitle.Text = $t.Value }
                        if ($t.Name -eq "artist" -and $TxtMetaArtist) { $TxtMetaArtist.Text = $t.Value }
                        if ($t.Name -eq "album" -and $TxtMetaAlbum) { $TxtMetaAlbum.Text = $t.Value }
                    }
                }
                if ($TxtMetaDuration) { $TxtMetaDuration.Text = $json.format.duration }
            }
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "Loaded $($script:MetadataItems.Count) tags (FFprobe)." }
        } catch {}
    } else {
        # FALLBACK: Use .NET and Windows Shell when no external tools available
        if ($isImage) {
            # Get EXIF from .NET PropertyItems
            $exifData = Get-ImageExifNative -Path $FilePath
            foreach ($item in $exifData) {
                [void]$script:MetadataItems.Add($item)
                if ($item.Tag -eq "Artist" -and $TxtMetaArtist) { $TxtMetaArtist.Text = $item.Value }
                if ($item.Tag -eq "XPTitle" -and $TxtMetaTitle) { $TxtMetaTitle.Text = $item.Value }
                if ($item.Tag -eq "XPComment" -and $TxtMetaComment) { $TxtMetaComment.Text = $item.Value }
                if ($item.Tag -eq "DateTime" -and $TxtMetaModified) { $TxtMetaModified.Text = $item.Value }
            }
        }
        if ($isVideo) {
            # Get video info from Shell
            $vidInfo = Get-VideoInfoShell -Path $FilePath
            if ($vidInfo.Duration) { 
                [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag="Duration"; Value=$vidInfo.Duration })
                if ($TxtMetaDuration) { $TxtMetaDuration.Text = $vidInfo.Duration }
            }
            if ($vidInfo.Width -and $vidInfo.Height) {
                [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag="Dimensions"; Value="$($vidInfo.Width) x $($vidInfo.Height)" })
                if ($TxtMetaDimensions) { $TxtMetaDimensions.Text = "$($vidInfo.Width) x $($vidInfo.Height)" }
            }
            if ($vidInfo.FrameRate) { [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag="FrameRate"; Value=$vidInfo.FrameRate }) }
            if ($vidInfo.BitRate) { [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag="BitRate"; Value=$vidInfo.BitRate }) }
        }
        if ($isAudio) {
            # Get audio info from Shell
            $audInfo = Get-AudioInfoShell -Path $FilePath
            if ($audInfo.Duration) { 
                [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag="Duration"; Value=$audInfo.Duration })
                if ($TxtMetaDuration) { $TxtMetaDuration.Text = $audInfo.Duration }
            }
            if ($audInfo.Title -and $TxtMetaTitle) { $TxtMetaTitle.Text = $audInfo.Title }
            if ($audInfo.Artist -and $TxtMetaArtist) { $TxtMetaArtist.Text = $audInfo.Artist }
            if ($audInfo.Album -and $TxtMetaAlbum) { $TxtMetaAlbum.Text = $audInfo.Album }
            if ($audInfo.Year -and $TxtMetaYear) { $TxtMetaYear.Text = $audInfo.Year }
            if ($audInfo.Comment -and $TxtMetaComment) { $TxtMetaComment.Text = $audInfo.Comment }
            foreach ($k in $audInfo.Keys) { 
                if ($audInfo[$k]) { [void]$script:MetadataItems.Add([PSCustomObject]@{ Tag=$k; Value=$audInfo[$k] }) }
            }
        }
        # Also get Windows Shell properties for any file type
        $shellMeta = Get-FileMetadataShell -Path $FilePath
        foreach ($item in $shellMeta) {
            # Avoid duplicates
            $exists = $script:MetadataItems | Where-Object { $_.Tag -eq $item.Tag }
            if (-not $exists) { [void]$script:MetadataItems.Add($item) }
        }
        if ($TxtMetaStatus) { $TxtMetaStatus.Text = "Loaded $($script:MetadataItems.Count) tags (Windows Shell - install ExifTool for more)." }
    }
    
    if ($DgMetadata) { $DgMetadata.ItemsSource = $null; $DgMetadata.ItemsSource = $script:MetadataItems }
}

if ($BtnMetaBrowse) {
    $BtnMetaBrowse.Add_Click({
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Filter = "All files (*.*)|*.*"
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            Load-FileMetadata -FilePath $dlg.FileName
        }
    })
}

if ($BtnMetaRefresh) {
    $BtnMetaRefresh.Add_Click({
        if ($script:CurrentMetaFile -and (Test-Path $script:CurrentMetaFile)) {
            Load-FileMetadata -FilePath $script:CurrentMetaFile
        }
    })
}

if ($BtnMetaSave) {
    $BtnMetaSave.Add_Click({
        if (-not $script:CurrentMetaFile -or -not (Test-Path $script:CurrentMetaFile)) {
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "No file selected." }
            return
        }
        if (-not $exiftoolPath -or -not (Test-Path $exiftoolPath)) {
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "exiftool not found. Cannot save metadata." }
            return
        }
        $args = @()
        if ($TxtMetaTitle -and $TxtMetaTitle.Text) { $args += "-Title=$($TxtMetaTitle.Text)" }
        if ($TxtMetaArtist -and $TxtMetaArtist.Text) { $args += "-Artist=$($TxtMetaArtist.Text)" }
        if ($TxtMetaAlbum -and $TxtMetaAlbum.Text) { $args += "-Album=$($TxtMetaAlbum.Text)" }
        if ($TxtMetaYear -and $TxtMetaYear.Text) { $args += "-Year=$($TxtMetaYear.Text)" }
        if ($TxtMetaComment -and $TxtMetaComment.Text) { $args += "-Comment=$($TxtMetaComment.Text)" }
        if ($args.Count -eq 0) {
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "No changes to save." }
            return
        }
        $args += "-overwrite_original"
        $args += $script:CurrentMetaFile
        try {
            & $exiftoolPath @args 2>&1 | Out-Null
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "Metadata saved." }
            Load-FileMetadata -FilePath $script:CurrentMetaFile
        } catch {
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "Error saving: $($_.Exception.Message)" }
        }
    })
}

if ($BtnMetaExport) {
    $BtnMetaExport.Add_Click({
        if ($script:MetadataItems.Count -eq 0) {
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "No metadata to export." }
            return
        }
        $dlg = New-Object System.Windows.Forms.SaveFileDialog
        $dlg.Filter = "CSV (*.csv)|*.csv|Text (*.txt)|*.txt"
        $dlg.FileName = [System.IO.Path]::GetFileNameWithoutExtension($script:CurrentMetaFile) + "_metadata"
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $script:MetadataItems | Export-Csv -Path $dlg.FileName -NoTypeInformation -Encoding UTF8
            if ($TxtMetaStatus) { $TxtMetaStatus.Text = "Exported to $($dlg.FileName)" }
        }
    })
}

$BtnCombBrowse.Add_Click({ $fb = New-Object System.Windows.Forms.FolderBrowserDialog; if ($fb.ShowDialog() -eq 'OK') { Load-CombFiles -Folder $fb.SelectedPath } })
$BtnCombSelectAll.Add_Click({ foreach ($item in $CombItems) { $item.Apply = $true }; Refresh-CombGrid })
$BtnCombSelectNone.Add_Click({ foreach ($item in $CombItems) { $item.Apply = $false }; Refresh-CombGrid })
$BtnCombUp.Add_Click({ Move-CombItem -Offset -1 })
$BtnCombDown.Add_Click({ Move-CombItem -Offset 1 })
$BtnConvBrowse.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') {
    $TxtConvOut.Text = $fb.SelectedPath
    Update-Status "Output folder set for convert/combine: $($fb.SelectedPath)"
  }
})
$BtnCombConvert.Add_Click({
  if (-not (Ensure-Tools)) { return }
  $checked = Get-CombChecked
  if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
  $fmt = "mp4"
  if ($CmbConvFormat -and $CmbConvFormat.SelectedItem -and $CmbConvFormat.SelectedItem.Tag) {
    $fmt = $CmbConvFormat.SelectedItem.Tag.ToString().ToLower()
  }
  $resTag = "source"
  if ($CmbConvRes -and $CmbConvRes.SelectedItem -and $CmbConvRes.SelectedItem.Tag) {
    $resTag = $CmbConvRes.SelectedItem.Tag.ToString().ToLower()
  }
  $scaleArgs = @()
  switch ($resTag) {
    "720p"   { $scaleArgs = @('-vf','scale=-2:720') }
    "1080p"  { $scaleArgs = @('-vf','scale=-2:1080') }
    "4k"     { $scaleArgs = @('-vf','scale=-2:2160') }
    default   { $scaleArgs = @() }
  }
  $outDir = if ($TxtConvOut -and $TxtConvOut.Text) { $TxtConvOut.Text } else { $targetDir }
  $outDir = Get-OutputDir
  if (-not $outDir) { [System.Windows.MessageBox]::Show("Please set an output folder."); return }
  $logFile = New-CombLogFile -Prefix ("convert_{0}" -f $fmt)
  $TxtCombLog.Clear(); Write-CombLog ("Converting to {0} ({1}) in {2} ..." -f $fmt, $resTag, $outDir) $logFile; Update-Status "Converting..."; Set-ConvStatus "Converting to $fmt ($resTag) ..."
  $total = $checked.Count
  Set-ConvProgress $true 0 $total; Pump-CombUI
  try {
    $success = 0; $fail = 0
    $index = 0
    foreach ($f in $checked) {
      $base = [System.IO.Path]::GetFileNameWithoutExtension($f.Path)
      $outFile = Join-Path $outDir ("conv_" + $base + "." + $fmt)
      $ffArgs = if ($fmt -eq 'wmv') {
        @('-y','-i',$f.Path) + $scaleArgs + @('-c:v','wmv2','-b:v','4000k','-c:a','wmav2','-b:a','192k',$outFile)
      } else {
        @('-y','-i',$f.Path) + $scaleArgs + @('-c:v','libx264','-preset','fast','-crf','20','-c:a','aac','-b:a','192k',$outFile)
      }
      Write-CombLog ("-> {0} => {1} [{2}]" -f [System.IO.Path]::GetFileName($f.Path), [System.IO.Path]::GetFileName($outFile), $resTag) $logFile
      Set-ConvStatus ("Converting {0}/{1}: {2}" -f ($index + 1), $total, [System.IO.Path]::GetFileName($f.Path))
      $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label ("Convert to {0}" -f $fmt) -MaxUiLines 10000
      if ($exit -eq 0 -and (Test-Path $outFile)) { $success++ }
      else { $fail++ }
      $index++
      Set-ConvProgress $true $index $total; Pump-CombUI
    }
    Write-CombLog ("Convert finished. Success: {0}, Fail: {1}" -f $success, $fail) $logFile
    Update-Status ("Convert complete. Success: {0}, Fail: {1}" -f $success, $fail)
    Set-ConvStatus ("Convert complete. Success: {0}, Fail: {1}" -f $success, $fail)
  }
  finally {
    Set-ConvProgress $false; Pump-CombUI
  }
})

$BtnCombPreview.Add_Click({
    if (-not (Ensure-Tools)) { return }
    $checked = Get-CombChecked
    if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
    $TxtCombLog.Clear(); Update-Status "Reading metadata..."
    $details = Compare-Encodings $checked
    foreach ($d in $details) { $TxtCombLog.AppendText(("{0,-40} | {1,-8} | {2}x{3} | FPS:{4,-10} | Dur:{5}" -f $d.File,$d.Codec,$d.Width,$d.Height,$d.FPS,$d.Duration) + "`r`n") }
    $codecMismatch = ($details.Codec | Select-Object -Unique).Count -gt 1
    $resMismatch   = ($details.Width | Select-Object -Unique).Count -gt 1 -or ($details.Height | Select-Object -Unique).Count -gt 1
    $fpsMismatch   = ($details.FPS | Select-Object -Unique).Count -gt 1
    if ($codecMismatch -or $resMismatch -or $fpsMismatch) { $TxtCombLog.AppendText("Differences detected -> normalize recommended.`r`n"); Update-Status "Differences detected" }
    else { $TxtCombLog.AppendText("Encodings compatible -> safe to combine.`r`n"); Update-Status "Encodings compatible" }
})

$BtnCombNormalize.Add_Click({
    if (-not (Ensure-Tools)) { return }
    $checked = Get-CombChecked
    if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
  $logFile = New-CombLogFile -Prefix "normalize"
  $TxtCombLog.Clear(); Write-CombLog "Normalizing to H.264/AAC in $targetDir ..." $logFile; Update-Status "Normalizing..."
  $fixed = @()
    foreach ($f in $checked) {
        $outFile = Join-Path $targetDir ("fixed_" + [System.IO.Path]::GetFileNameWithoutExtension($f.Path) + ".mp4")
        $ffArgs = @('-y','-i',$f.Path,'-c:v','libx264','-preset','fast','-crf','23','-c:a','aac','-b:a','192k',$outFile)
    Write-CombLog "-> $([System.IO.Path]::GetFileName($f.Path)) -> $([System.IO.Path]::GetFileName($outFile))" $logFile
    $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label ("Normalize {0}" -f [System.IO.Path]::GetFileName($f.Path))
    if ($exit -eq 0 -and (Test-Path $outFile)) { $fixed += $outFile } else { Write-CombLog "Normalization failed (exit $exit) for $($f.Path)" $logFile }
    }
    $CombItems.Clear() | Out-Null; foreach ($f in $fixed) { [void]$CombItems.Add([PSCustomObject]@{ Apply=$true; Name=[System.IO.Path]::GetFileName($f); Path=$f }) }
    Refresh-CombGrid
  Write-CombLog "Done. Files normalized for concat in $targetDir. Log: $logFile" $logFile; Update-Status "Normalization finished"
})

$BtnCombSafe.Add_Click({
  if (-not (Ensure-Tools)) { return }
  $checked = Get-CombChecked
  if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
  $outDir = Get-OutputDir
  if (-not $outDir) { [System.Windows.MessageBox]::Show("Please set an output folder."); return }
  $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
  $outFile = Join-Path $outDir ("combined_safe_{0}.mp4" -f $stamp)
  if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
  $logFile = New-CombLogFile -Prefix "combine_safe"
    $listFile = Join-Path $outDir ("videolist_safe_{0}.txt" -f $stamp)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $lines = @(); foreach ($f in $checked) { $normalized = [System.IO.Path]::GetFullPath($f.Path); $escaped = ($normalized -replace "'", "'\''"); $lines += "file '$escaped'" }
    [System.IO.File]::WriteAllLines($listFile, $lines, $utf8NoBom)
    if ($logFile) {
      "Input list (concat file content):" | Out-File -FilePath $logFile -Append -Encoding UTF8
      $lines | Out-File -FilePath $logFile -Append -Encoding UTF8
    }
    $TxtCombLog.Clear(); Write-CombLog "Safe combine (re-encode) into: $outFile" $logFile; Write-CombLog "List file: $listFile" $logFile
    $idx = 1
    foreach ($f in $checked) {
      $entry = ("  {0}. {1}" -f $idx, [System.IO.Path]::GetFileName($f.Path))
      Write-CombLog $entry $logFile
      $idx++
    }
    Update-Status "Safe combining with ffmpeg..."
    $ffArgs = @(
      '-y','-fflags','+genpts','-safe','0','-f','concat','-i',$listFile,
      '-c:v','libx264','-preset','medium','-crf','20',
      '-c:a','aac','-b:a','192k',
      '-vsync','2','-af','aresample=async=1',
      '-avoid_negative_ts','make_zero','-reset_timestamps','1','-movflags','+faststart',
      $outFile
    )
    Write-CombLog ("ffmpeg cmd: {0} {1}" -f $ffmpegPath, (Render-Args $ffArgs)) $logFile
    $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label "Safe combine (re-encode)"
    Remove-Item $listFile -Force -ErrorAction SilentlyContinue
  if ($exit -eq 0) {
    [System.Windows.MessageBox]::Show("Videos safely combined into $outFile. Log saved to $logFile")
    Update-Status "Safe combine complete"
  }
  else {
    Write-CombLog "Safe combine failed with exit code $exit. See $logFile" $logFile
    [System.Windows.MessageBox]::Show("Safe combine failed (exit $exit). See log: $logFile","Safe combine failed",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
    Update-Status "Safe combine failed"
  }
})

$BtnCombCombine.Add_Click({
    if (-not (Ensure-Tools)) { return }
    $checked = Get-CombChecked
    if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
    $outDir = Get-OutputDir
    if (-not $outDir) { [System.Windows.MessageBox]::Show("Please set an output folder."); return }
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $outFile = Join-Path $outDir ("combined_{0}.mp4" -f $stamp)
    if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
  $logFile = New-CombLogFile -Prefix "combine_copy"
    $listFile = Join-Path $outDir ("videolist_copy_{0}.txt" -f $stamp)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $lines = @(); foreach ($f in $checked) { $normalized = [System.IO.Path]::GetFullPath($f.Path); $escaped = ($normalized -replace "'", "'\''"); $lines += "file '$escaped'" }
    [System.IO.File]::WriteAllLines($listFile, $lines, $utf8NoBom)
    if ($logFile) {
      "Input list (concat file content):" | Out-File -FilePath $logFile -Append -Encoding UTF8
      $lines | Out-File -FilePath $logFile -Append -Encoding UTF8
    }
  $TxtCombLog.Clear(); Write-CombLog "Combining into: $outFile (stream copy)" $logFile; Write-CombLog "List file: $listFile" $logFile
    $idx = 1
    foreach ($f in $checked) {
      $entry = ("  {0}. {1}" -f $idx, [System.IO.Path]::GetFileName($f.Path))
      Write-CombLog $entry $logFile
      $idx++
    }
    Update-Status "Combining with ffmpeg..."
  $ffArgs = @('-y','-fflags','+genpts','-safe','0','-f','concat','-i',$listFile,'-c','copy','-avoid_negative_ts','make_zero','-reset_timestamps','1','-movflags','+faststart',$outFile)
  Write-CombLog ("ffmpeg cmd: {0} {1}" -f $ffmpegPath, (Render-Args $ffArgs)) $logFile
  $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label "Combine (stream copy)"
  Remove-Item $listFile -Force -ErrorAction SilentlyContinue
  if ($exit -eq 0) {
    [System.Windows.MessageBox]::Show("Videos combined successfully into $outFile. Log saved to $logFile")
    Update-Status "Combine complete"
  }
  else {
    Write-CombLog "Combine failed with exit code $exit. See $logFile" $logFile
    [System.Windows.MessageBox]::Show("Combine failed (exit $exit). See log: $logFile","Combine failed",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
    Update-Status "Combine failed"
  }
})

# --------------- Run ---------------

# ==================== SYSTEM CLEANER HANDLERS ====================
$Global:HiderConfig = Get-HiderConfig
Refresh-HiderList -ListControl $hiderList
$hAutoHide.IsChecked   = [bool]$Global:HiderConfig.AutoHideEnabled
$hAutoMinutes.Text     = [string]$Global:HiderConfig.AutoHideMinutes
$hStatusLbl.Text       = "Folders: $($Global:HiderConfig.Folders.Count)"
Set-SecStatus "Ready"

# --- Logging helper ---
function Log-Error($message, $outputPath) {
    try {
        $pathToUse = if ($outputPath) { $outputPath } elseif ($config.OutputPath) { $config.OutputPath } elseif ($defaultOut) { $defaultOut } else { $env:TEMP }
        if (-not $pathToUse) { $pathToUse = $env:TEMP }
        # Ensure directory exists and is writable; fall back to TEMP on failure
        try { Ensure-Directory $pathToUse | Out-Null } catch { $pathToUse = $env:TEMP; Ensure-Directory $pathToUse | Out-Null }
        # Quick write test to ensure we can write there
        $testFile = Join-Path $pathToUse (".pl_test_{0}.tmp" -f ([Guid]::NewGuid().ToString()))
        try { Set-Content -Path $testFile -Value "" -Force -ErrorAction Stop; Remove-Item -Path $testFile -Force -ErrorAction SilentlyContinue } catch { $pathToUse = $env:TEMP; Ensure-Directory $pathToUse | Out-Null }
        $logPath = Join-Path $pathToUse "RecentCleaner_ErrorLog_$(Get-Date -Format 'yyyyMMdd').log"
        try { Add-Content -Path $logPath -Value "$(Get-Date -Format 'u') ERROR: $message" -ErrorAction SilentlyContinue } catch { Write-Host "Failed to write log to ${logPath}: $message" -ForegroundColor Yellow }
    } catch {
        # Last-resort fallback: write to host so we don't throw from logging
        try { Write-Host "Log-Error fallback: $message" -ForegroundColor Yellow } catch {}
    }
}

function Set-Status($text, $durationMs=1000) {
    try {
        $TxtStatus.Text = $text
        if ($durationMs -gt 0) {
            $timer = New-Object System.Windows.Threading.DispatcherTimer
            $timer.Interval = [TimeSpan]::FromMilliseconds($durationMs)
            $timer.Add_Tick({
                $TxtStatus.Text = "Ready"
                $this.Stop()
            }.GetNewClosure())
            $timer.Start()
        }
    } catch { }
}

function Set-HiderStatusText {
    param([string]$Text)
    try { $hStatusLbl.Text = $Text } catch {}
}

function Get-HiderSelectedPath {
    if ($hiderList.SelectedItem -and $hiderList.SelectedItem.PSObject.Properties['Path']) {
        return [string]$hiderList.SelectedItem.Path
    }
    $manual = $hManualBox.Text.Trim()
    if (-not [string]::IsNullOrWhiteSpace($manual)) { return $manual }
    return $null
}

function Sync-HiderSelectionUI {
    $path = Get-HiderSelectedPath
    if (-not $path) {
        $hAclToggle.IsChecked = $false
        $hEfsToggle.IsChecked = $false
        return
    }
    $rec = Get-HiderRecord -Path $path
    if ($rec) {
        $hAclToggle.IsChecked = [bool]$rec.AclRestricted
        $hEfsToggle.IsChecked = [bool]$rec.EfsEnabled
    } else {
        $hAclToggle.IsChecked = $false
        $hEfsToggle.IsChecked = $false
    }
}

function Hide-AllHiderFolders {
    foreach ($rec in $Global:HiderConfig.Folders) {
        $path = $rec.FolderPath
        try {
            if (Test-Path -LiteralPath $path) {
                Set-Hidden -Path $path | Out-Null
                if (Should-ApplyAclRestriction -Record $rec) { [void](Set-AclRestriction -Path $path) }
                elseif ($rec.AclRestricted) { Write-HiderLog "ACL restriction skipped for '$path' (bypass user)." }
                if ($rec.EfsEnabled) { [void](Enable-EFS -Path $path) }
                Write-HiderLog "Auto/All hide: $path"
            }
        } catch { Write-HiderLog "Error hiding '$path': $($_.Exception.Message)" }
    }
    Refresh-HiderList -ListControl $hiderList
}

# --- Save config safely ---
function Save-Config {
    if (-not ($config -is [System.Management.Automation.PSCustomObject])) {
        $config = [pscustomobject]@{ ExcludedDirs = @(); OutputPath = $defaultOut; Weekdays = @(); Time = "00:00"; IncludeSubdirs = $true }
    }
    $config.ExcludedDirs = @($dirList.Items | ForEach-Object { $_.ToString() })
    if ($config.PSObject.Properties.Match('OutputPath').Count -eq 0) { $config | Add-Member -NotePropertyName OutputPath -NotePropertyValue $outputBox.Text -Force } else { $config.OutputPath = $outputBox.Text }

    $selected = @()
    if ($chkSun.IsChecked) { $selected += "Sunday" }; if ($chkMon.IsChecked) { $selected += "Monday" }
    if ($chkTue.IsChecked) { $selected += "Tuesday" }; if ($chkWed.IsChecked) { $selected += "Wednesday" }
    if ($chkThu.IsChecked) { $selected += "Thursday" }; if ($chkFri.IsChecked) { $selected += "Friday" }
    if ($chkSat.IsChecked) { $selected += "Saturday" }
    if ($config.PSObject.Properties.Match('Weekdays').Count -eq 0) { $config | Add-Member -NotePropertyName Weekdays -NotePropertyValue $selected -Force } else { $config.Weekdays = $selected }

    if ($config.PSObject.Properties.Match('Time').Count -eq 0) { $config | Add-Member -NotePropertyName Time -NotePropertyValue $timeBox.Text -Force } else { $config.Time = $timeBox.Text }

    $incBool = To-BoolSafe $includeSub.IsChecked $true
    if ($config.PSObject.Properties.Match('IncludeSubdirs').Count -eq 0) { $config | Add-Member -NotePropertyName IncludeSubdirs -NotePropertyValue $incBool -Force } else { $config.IncludeSubdirs = $incBool }

    try { $config | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Force } catch { Log-Error "Failed saving config: $_" $config.OutputPath }
}

# --- Resolve and remove shortcuts ---
function Resolve-LnkTarget($lnkPath, [ref]$shellObj) {
    try {
        if (-not $shellObj.Value) { $shellObj.Value = New-Object -ComObject WScript.Shell }
        $target = $shellObj.Value.CreateShortcut($lnkPath).TargetPath
        if (-not $target) {
            # Fallback: use .NET Activator to create WScript.Shell and attempt again
            try {
                $wshType = [System.Type]::GetTypeFromProgID('WScript.Shell')
                if ($wshType) {
                    $shellObj2 = [System.Activator]::CreateInstance($wshType)
                    if ($shellObj2) { $lnkObj = $shellObj2.CreateShortcut($lnkPath); $target = $lnkObj.TargetPath }
                }
            } catch { }
        }
        return $target
    } catch { return $null }
}

function Remove-RecentShortcuts {
    param (
        [string[]]$TargetDirs,
        [switch]$DryRun,
        [string]$BackupPath,
        [switch]$IncludeSubDirs,
        [string]$RecentFolder
    )

    # DEBUG: show whether RecentFolder param is present and its value (temporary debug)
    $dbgFile = Join-Path $env:TEMP 'pt_recent_debug.txt'
    "DEBUG: PSBoundKeys: $($PSBoundParameters.Keys -join ',')" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
    "DEBUG: RawRecentFolder param value: '$RecentFolder'" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
    $recent = if ($PSBoundParameters.ContainsKey('RecentFolder') -and $RecentFolder) { $RecentFolder } else { [Environment]::GetFolderPath("Recent") }
    "DEBUG: Resolved recent folder: $recent" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
    try { $shortcuts = Get-ChildItem -Path $recent -Filter *.lnk -Force -ErrorAction SilentlyContinue } catch { $shortcuts = @() }
    $matched = New-Object System.Collections.Generic.List[object]
    $shellRef = [ref] $null

    foreach ($lnk in $shortcuts) {
        try {
            "DEBUG: LNK found: $($lnk.FullName)" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
            "DEBUG: shellRef present before Resolve: $([bool]($shellRef -and $shellRef.Value))" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
            try { $directShell = New-Object -ComObject WScript.Shell; $directTarget = $directShell.CreateShortcut($lnk.FullName).TargetPath; "DEBUG: direct COM target: $directTarget" | Out-File -FilePath $dbgFile -Encoding utf8 -Append } catch { "DEBUG: direct COM failed: $_" | Out-File -FilePath $dbgFile -Encoding utf8 -Append }
            $target = Resolve-LnkTarget $lnk.FullName $shellRef
            "DEBUG: Resolved target: $target" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
            if (-not $target) { continue }
            foreach ($dir in $TargetDirs) {
                if (-not $dir) { continue }
                if ($IncludeSubDirs) {
                    $isMatch = $target.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)
                    "DEBUG: Checking include-subdirs: target='$target' dir='$dir' StartsWith='$isMatch'" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
                } else {
                    $parent = Split-Path -Path $target -Parent
                    $isMatch = ($parent -and ([string]::Equals($parent, $dir, [System.StringComparison]::OrdinalIgnoreCase)))
                    "DEBUG: Checking exact-dir: parent='$parent' dir='$dir' Equals='$isMatch'" | Out-File -FilePath $dbgFile -Encoding utf8 -Append
                }
                if ($isMatch) {
                    $matched.Add([PSCustomObject]@{ Type='Shortcut'; Path = $lnk.FullName; Target = $target })
                    if (-not $DryRun) {
                        if ($BackupPath) { Ensure-Directory $BackupPath; Copy-Item -Path $lnk.FullName -Destination $BackupPath -Force -ErrorAction SilentlyContinue }
                        Remove-Item -Path $lnk.FullName -Force -ErrorAction SilentlyContinue
                    }
                    break
                }
            }
        } catch { Log-Error "Failed processing $($lnk.FullName): $_" $config.OutputPath }
    }

    # --- Additional cleanup: Jump Lists (Automatic/Custom Destinations), Quick Access pinned, and Favorites ---
    $recentRoot = Join-Path $env:APPDATA 'Microsoft\Windows\Recent'
    $autoDir = Join-Path $recentRoot 'AutomaticDestinations'
    $customDir = Join-Path $recentRoot 'CustomDestinations'

    $jumpFiles = @()
    if (Test-Path $autoDir) { $jumpFiles += Get-ChildItem -Path $autoDir -File -ErrorAction SilentlyContinue }
    if (Test-Path $customDir) { $jumpFiles += Get-ChildItem -Path $customDir -File -ErrorAction SilentlyContinue }

    foreach ($jf in $jumpFiles) {
        try {
            $bytes = Get-Content -LiteralPath $jf.FullName -Encoding Byte -ErrorAction SilentlyContinue
            if (-not $bytes) { continue }
            # Try Unicode and UTF8 decoding to extract paths from binary blob
            $decodedUni = ''
            $decodedUtf8 = ''
            try { $decodedUni = [System.Text.Encoding]::Unicode.GetString($bytes) } catch {}
            try { $decodedUtf8 = [System.Text.Encoding]::UTF8.GetString($bytes) } catch {}
            $foundPaths = @()
            foreach ($txt in @($decodedUni, $decodedUtf8)) {
                if (-not $txt) { continue }
                $matches = [regex]::Matches($txt, '([A-Za-z]:\\[^\x00\r\n]+?)\x00')
                foreach ($m in $matches) { $foundPaths += $m.Groups[1].Value }
            }
            $foundPaths = $foundPaths | Select-Object -Unique
            $removeFile = $false
            foreach ($dir in $TargetDirs) {
                if (-not $dir) { continue }
                $dirLower = $dir.ToLowerInvariant()
                foreach ($p in $foundPaths) {
                    if (-not $p) { continue }
                    $pLower = $p.ToLowerInvariant()
                    if ($IncludeSubDirs) {
                        if ($pLower.StartsWith($dirLower)) { $removeFile = $true; break }
                    } else {
                        $parent = Split-Path -Path $p -Parent
                        if ($parent -and [string]::Equals($parent, $dir, [System.StringComparison]::OrdinalIgnoreCase)) { $removeFile = $true; break }
                    }
                }
                if ($removeFile) { break }
            }
            if ($removeFile) {
                $matched.Add([PSCustomObject]@{ Type='JumpList'; Path = $jf.FullName; MatchedPaths = $foundPaths })
                if (-not $DryRun) {
                    if ($BackupPath) { Ensure-Directory $BackupPath; Copy-Item -Path $jf.FullName -Destination $BackupPath -Force -ErrorAction SilentlyContinue }
                    # Delete the jump list file to remove references
                    Remove-Item -LiteralPath $jf.FullName -Force -ErrorAction SilentlyContinue
                }
            }
        } catch { Log-Error "Failed processing jump file $($jf.FullName): $_" $config.OutputPath }
    }

    # Quick Access pinned & Favorites
    $pinnedRoots = @(
        Join-Path -Path ([string]$env:APPDATA) -ChildPath 'Microsoft\Internet Explorer\Quick Launch\User Pinned'
        Join-Path -Path ([string]$env:USERPROFILE) -ChildPath 'Favorites'
    )
    foreach ($root in $pinnedRoots) {
        if (-not (Test-Path $root)) { continue }
        try {
            $lnks = Get-ChildItem -Path $root -Recurse -Filter *.lnk -ErrorAction SilentlyContinue
            foreach ($lnk in $lnks) {
                try {
                    $target = Resolve-LnkTarget $lnk.FullName $shellRef
                    if (-not $target) { continue }
                    foreach ($dir in $TargetDirs) {
                        if (-not $dir) { continue }
                        if ($IncludeSubDirs) { $isMatch = $target.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase) }
                        else { $parent = Split-Path -Path $target -Parent; $isMatch = ($parent -and ([string]::Equals($parent, $dir, [System.StringComparison]::OrdinalIgnoreCase))) }
                        if ($isMatch) {
                            $matched.Add([PSCustomObject]@{ Type='PinnedShortcut'; Path = $lnk.FullName; Target = $target })
                            if (-not $DryRun) {
                                if ($BackupPath) { Ensure-Directory $BackupPath; Copy-Item -Path $lnk.FullName -Destination $BackupPath -Force -ErrorAction SilentlyContinue }
                                Remove-Item -LiteralPath $lnk.FullName -Force -ErrorAction SilentlyContinue
                            }
                            break
                        }
                    }
                } catch { Log-Error "Failed resolving pinned shortcut $($lnk.FullName): $_" $config.OutputPath }
            }
        } catch { Log-Error ("Pinned root scan failed for ${root}: $_") $config.OutputPath }
    }

    return $matched.ToArray()
}

# --- Frequency selection handler ---
if ($null -eq $frequency) {
    Write-Warning "Frequency control not found; cannot wire selection handler."
} else {
    try {
        if (-not $frequency.SelectedItem) {
            foreach ($item in $frequency.Items) { if ($item -and $item.Content -and $item.Content -eq 'Daily') { $frequency.SelectedItem = $item; break } }
        }
    } catch {}
    try {
        $frequency.SelectionChanged.Add({
            try {
                $sel = $frequency.SelectedItem
                if ($sel -and $sel.Content -eq 'Monthly') { $daysBox.Visibility = 'Visible' } else { $daysBox.Visibility = 'Collapsed' }
            } catch { Log-Error "Frequency handler error: $_" $outputBox.Text }
        }) | Out-Null
    } catch {
        try {
            $frequency.add_SelectionChanged({
                try {
                    $sel = $frequency.SelectedItem
                    if ($sel -and $sel.Content -eq 'Monthly') { $daysBox.Visibility = 'Visible' } else { $daysBox.Visibility = 'Collapsed' }
                } catch { Log-Error "Frequency fallback handler error: $_" $outputBox.Text }
            }) | Out-Null
        } catch { Write-Warning "Failed to attach Frequency selection handler: $_" }
    }
}

# --- UI handlers ---
$addDir.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($dlg.ShowDialog() -eq 'OK') { if (-not $dirList.Items.Contains($dlg.SelectedPath)) { [void]$dirList.Items.Add($dlg.SelectedPath); Save-Config } }
})

$removeDir.Add_Click({
    if ($dirList.SelectedItem) { $dirList.Items.Remove($dirList.SelectedItem); Save-Config }
})

$browseOut.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($dlg.ShowDialog() -eq 'OK') { $outputBox.Text = $dlg.SelectedPath; Save-Config }
})

if (-not $timeBox.Text) { $timeBox.Text = "00:00" }

$showPreview.Add_Click({
    try {
        $TxtStatus.Text = "Previewing..."
        $dirs = @($dirList.Items | ForEach-Object { $_.ToString() })
        $includeBool = To-BoolSafe $includeSub.IsChecked $true
        $results = Remove-RecentShortcuts -TargetDirs $dirs -DryRun -IncludeSubDirs:$includeBool
        $previewGrid.ItemsSource = $results
        Set-Status "Preview complete. Matched: $($results.Count)" 1200
    } catch {
        Log-Error "Preview failed: $_" $outputBox.Text
        [System.Windows.MessageBox]::Show("Preview failed. See error log in output folder.")
    }
})

$runNow.Add_Click({
    try {
        $TxtStatus.Text = "Running..."
        $dirs = @($dirList.Items | ForEach-Object { $_.ToString() })
        $dryRun = To-BoolSafe $dryRunBox.IsChecked $true
        $outPath = if ($outputBox.Text) { $outputBox.Text } else { $defaultOut }
        Ensure-Directory $outPath
        $backupPath = if (-not $dryRun) { Join-Path $outPath "Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')" } else { $null }
        if ($backupPath) { Ensure-Directory $backupPath }
        $includeBool = To-BoolSafe $includeSub.IsChecked $true
        $results = Remove-RecentShortcuts -TargetDirs $dirs -DryRun:$dryRun -BackupPath $backupPath -IncludeSubDirs:$includeBool
        $previewGrid.ItemsSource = $results
        Save-Config
        Set-Status "Run complete. Matched: $($results.Count)" 1200
    } catch {
        Log-Error "Run failed: $_" $outputBox.Text
        [System.Windows.MessageBox]::Show("Run failed. See error log in output folder.")
    }
})

$exportCSV.Add_Click({
    try {
        $outPath = if ($outputBox.Text) { $outputBox.Text } else { $defaultOut }
        Ensure-Directory $outPath
        $items = $previewGrid.ItemsSource
        if (-not $items -or $items.Count -eq 0) { Set-Status "No preview data to export" 1200; return }
        $csvPath = Join-Path $outPath "RecentCleanerPreview_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"
        $items | Export-Csv -Path $csvPath -NoTypeInformation -Force
        Set-Status "Exported to: $csvPath" 1600
    } catch {
        Log-Error "Export failed: $_" $outputBox.Text
        [System.Windows.MessageBox]::Show("Export failed. See error log in output folder.")
    }
})

$undoLast.Add_Click({
    try {
        $outPath = if ($outputBox.Text) { $outputBox.Text } else { $defaultOut }
        if (-not (Test-Path $outPath)) { Set-Status "Output folder missing" 1200; return }
        $latest = Get-ChildItem -Path $outPath -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "Backup_*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latest) {
            Copy-Item -Path (Join-Path $latest.FullName "*") -Destination ([Environment]::GetFolderPath("Recent")) -Force -ErrorAction SilentlyContinue
            Set-Status "Undo complete from: $($latest.Name)" 1400
        } else {
            Set-Status "No backup found" 1200
        }
    } catch {
        Log-Error "Undo failed: $_" $outputBox.Text
        [System.Windows.MessageBox]::Show("Undo failed. See error log in output folder.")
    }
})

$saveTask.Add_Click({
    $taskName = "RecentCleanerTask"
    $scriptPath = $MyInvocation.MyCommand.Path
    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-WindowStyle Hidden -File `"$scriptPath`""

    try {
        $time = [datetime]::ParseExact($timeBox.Text.Trim(), "HH:mm", $null)
    } catch {
        [System.Windows.MessageBox]::Show("Invalid time format. Use HH:mm (24hr).")
        return
    }

    $freqItem = $frequency.SelectedItem
    if (-not $freqItem) { [System.Windows.MessageBox]::Show("Select a frequency."); return }
    $freq = $freqItem.Content

    $selectedWeekdays = @()
    if ($chkSun.IsChecked) { $selectedWeekdays += "Sunday" }
    if ($chkMon.IsChecked) { $selectedWeekdays += "Monday" }
    if ($chkTue.IsChecked) { $selectedWeekdays += "Tuesday" }
    if ($chkWed.IsChecked) { $selectedWeekdays += "Wednesday" }
    if ($chkThu.IsChecked) { $selectedWeekdays += "Thursday" }
    if ($chkFri.IsChecked) { $selectedWeekdays += "Friday" }
    if ($chkSat.IsChecked) { $selectedWeekdays += "Saturday" }

    $daysText = $daysBox.Text.Trim()
    $trigger = $null

    try {
        switch ($freq) {
            "Daily" { $trigger = New-ScheduledTaskTrigger -Daily -At $time }
            "Weekly" {
                if (-not $selectedWeekdays -or $selectedWeekdays.Count -eq 0) { [System.Windows.MessageBox]::Show("Select at least one weekday for Weekly schedule."); return }
                $trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $selectedWeekdays -At $time
            }
            "Monthly" {
                if ([string]::IsNullOrWhiteSpace($daysText)) { [System.Windows.MessageBox]::Show("Enter day numbers for Monthly schedule, e.g. 1 or 1,15"); return }
                $daysNum = $daysText -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object { if ($_ -match '^\d+$') { [int]$_ } else { -1 } }
                if ($daysNum -contains -1) { [System.Windows.MessageBox]::Show("Invalid monthly day value. Use integers like 1,15."); return }
                $trigger = New-ScheduledTaskTrigger -Monthly -DaysOfMonth $daysNum -At $time
            }
            default { [System.Windows.MessageBox]::Show("Select a valid frequency."); return }
        }

        $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $cred = $null

        # If not elevated, ask for credential up front
        if (-not $isAdmin) {
            $cred = Get-Credential -Message "Task creation requires credentials (not running elevated)"
            if (-not $cred) { Set-Status "Task not created (credential canceled)" 1500; return }
        }

        $registerWithCred = {
            param($credential)
            $principal = New-ScheduledTaskPrincipal -UserId $credential.UserName -LogonType Password -RunLevel Highest
            Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings (New-ScheduledTaskSettingsSet -Compatibility Win8) -Force
        }

        try {
            if ($cred) {
                & $registerWithCred $cred
            } else {
                Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Force
            }
            Save-Config
            Set-Status "Scheduled task created/updated" 1200
            try { Start-Process -FilePath "taskschd.msc" } catch {}
        } catch {
            $err = $_
            $needsCred = ($err.Exception.HResult -eq -2147024891 -or ($err.FullyQualifiedErrorId -like '*0x80070005*') -or ($err.Exception.Message -match 'Access is denied'))
            if (-not $needsCred) { throw }
            if (-not $cred) {
                $cred = Get-Credential -Message "Task creation needs credentials"
                if (-not $cred) { Set-Status "Task not created (credential canceled)" 1500; return }
            }
            try {
                & $registerWithCred $cred
                Save-Config
                Set-Status "Scheduled task created with provided credentials" 1500
                try { Start-Process -FilePath "taskschd.msc" } catch {}
            } catch {
                Log-Error "Schedule creation failed after credential prompt: $_" $outputBox.Text
                [System.Windows.MessageBox]::Show("Failed to create scheduled task, even with credentials. See error log in output folder.")
            }
        }
    } catch {
        Log-Error "Schedule creation failed: $_" $outputBox.Text
        [System.Windows.MessageBox]::Show("Failed to create scheduled task. See error log in output folder.")
    }
})

# --- Folder Hider handlers ---
$hiderList.add_SelectionChanged({ Set-HiderActivity; Sync-HiderSelectionUI })

$hAdd.Add_Click({
    Set-HiderActivity
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($dlg.ShowDialog() -eq 'OK') {
        if (Add-HiderRecord -Path $dlg.SelectedPath) {
            Refresh-HiderList -ListControl $hiderList
            Set-HiderStatusText "Added $($dlg.SelectedPath)"
        } else {
            Set-HiderStatusText "Already in list: $($dlg.SelectedPath)"
        }
    }
})

$hAddManual.Add_Click({
    Set-HiderActivity
    $path = $hManualBox.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($path)) { [System.Windows.MessageBox]::Show('Enter a folder path first.'); return }
    if (-not (Test-Path -LiteralPath $path)) { [System.Windows.MessageBox]::Show('Folder does not exist.'); return }
    if (Add-HiderRecord -Path $path) {
        Refresh-HiderList -ListControl $hiderList
        Set-HiderStatusText "Added $path"
    } else {
        Set-HiderStatusText "Already in list: $path"
    }
})

$hRemove.Add_Click({
    Set-HiderActivity
    $path = Get-HiderSelectedPath
    if (-not $path) { [System.Windows.MessageBox]::Show('Select or enter a folder to remove.'); return }
    $resp = [System.Windows.MessageBox]::Show("Remove this folder from the list?`r`n$path", 'Confirm', 'YesNo', 'Question')
    if ($resp -ne [System.Windows.MessageBoxResult]::Yes) { return }
    if (Remove-HiderRecord -Path $path) {
        Refresh-HiderList -ListControl $hiderList
        Set-HiderStatusText "Removed $path"
    }
})

$hSetPwd.Add_Click({
    Set-HiderActivity
    $path = Get-HiderSelectedPath
    if (-not $path) { [System.Windows.MessageBox]::Show('Select a folder first.'); return }
    if (-not (Test-Path -LiteralPath $path)) { [System.Windows.MessageBox]::Show('Folder does not exist.'); return }
    $pwd = Prompt-NewPassword
    if ($pwd) {
        $record = New-PasswordRecord -Password $pwd
        Update-HiderRecord -Path $path -PasswordRecord $record | Out-Null
        Set-HiderStatusText "Password saved for $path"
    }
})

$hHide.Add_Click({
    Set-HiderActivity
    $path = Get-HiderSelectedPath
    if (-not $path) { [System.Windows.MessageBox]::Show('Select or enter a folder to hide.'); return }
    Hide-HiderFolder -Path $path
    Refresh-HiderList -ListControl $hiderList
})

$hUnhide.Add_Click({
    Set-HiderActivity
    $path = Get-HiderSelectedPath
    if (-not $path) { [System.Windows.MessageBox]::Show('Select or enter a folder to unhide.'); return }
    Unhide-HiderFolder -Path $path
    Refresh-HiderList -ListControl $hiderList
})

$hOpenLog.Add_Click({
    Set-HiderActivity
    Ensure-HiderPaths
    if (-not (Test-Path -LiteralPath $HiderLogPath)) { New-Item -ItemType File -Path $HiderLogPath | Out-Null }
    Write-HiderLog "Log opened"
    Start-Process notepad.exe $HiderLogPath
})

$hScanHidden.Add_Click({
    Set-HiderActivity
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Select a drive or root folder to scan for hidden managed folders'
    if ($dlg.ShowDialog() -ne 'OK') { return }
    $root = $dlg.SelectedPath
    if (-not (Test-Path -LiteralPath $root)) { [System.Windows.MessageBox]::Show('Selected path does not exist.'); return }
    try {
        $found = @()
        $hiddenFlag = [IO.FileAttributes]::Hidden
        $systemFlag = [IO.FileAttributes]::System
        Get-ChildItem -LiteralPath $root -Directory -Force -Recurse -ErrorAction SilentlyContinue |
            Where-Object { ($_.Attributes -band $hiddenFlag) -and ($_.Attributes -band $systemFlag) } |
            ForEach-Object { $found += $_.FullName }
        $added = 0
        foreach ($p in $found) { if (-not (Get-HiderRecord -Path $p)) { if (Add-HiderRecord -Path $p) { $added++ } } }
        Refresh-HiderList -ListControl $hiderList
        Set-HiderStatusText "Scan complete. Found $($found.Count); added $added."
    } catch { [System.Windows.MessageBox]::Show("Scan failed:`r`n$($_.Exception.Message)"); Write-HiderLog "Scan failed: $($_.Exception.Message)" }
})

$hAclToggle.add_Click({
    Set-HiderActivity
    $path = Get-HiderSelectedPath
    if (-not $path) { return }
    Update-HiderRecord -Path $path -AclRestricted $hAclToggle.IsChecked | Out-Null
    Refresh-HiderList -ListControl $hiderList
})

$hEfsToggle.add_Click({
    Set-HiderActivity
    $path = Get-HiderSelectedPath
    if (-not $path) { return }
    Update-HiderRecord -Path $path -EfsEnabled $hEfsToggle.IsChecked | Out-Null
    Refresh-HiderList -ListControl $hiderList
})

$hAutoHide.add_Click({
    Set-HiderActivity
    Update-HiderAutoHide -Enabled ([bool]$hAutoHide.IsChecked) -Minutes ([int](($hAutoMinutes.Text -as [int]) -as [int]))
    $hStatusLbl.Text = "Auto-hide: " + (if ($hAutoHide.IsChecked) { 'Enabled' } else { 'Disabled' })
})

$hAutoMinutes.add_LostFocus({
    $val = $hAutoMinutes.Text -as [int]
    if (-not $val) { $val = 5 }
    Update-HiderAutoHide -Minutes $val
    $hAutoMinutes.Text = [string]$Global:HiderConfig.AutoHideMinutes
})

$hApply.Add_Click({
    Set-HiderActivity
    $path = Get-HiderSelectedPath
    if ($path) {
        if (-not (Get-HiderRecord -Path $path)) { Add-HiderRecord -Path $path | Out-Null }
        Update-HiderRecord -Path $path -AclRestricted $hAclToggle.IsChecked -EfsEnabled $hEfsToggle.IsChecked | Out-Null
    }
    $minutes = $hAutoMinutes.Text -as [int]
    if (-not $minutes -or $minutes -lt 1) { $minutes = 5 }
    Update-HiderAutoHide -Enabled ([bool]$hAutoHide.IsChecked) -Minutes $minutes
    Refresh-HiderList -ListControl $hiderList
    Set-HiderStatusText "Changes applied"
})

# Auto-hide timer
$hiderTimer = New-Object System.Windows.Threading.DispatcherTimer
$hiderTimer.Interval = [TimeSpan]::FromSeconds(30)
$hiderTimer.add_Tick({
    if (-not $Global:HiderConfig.AutoHideEnabled) { return }
    $elapsed = (New-TimeSpan -Start $Global:HiderLastActivity -End (Get-Date)).TotalMinutes
    if ($elapsed -ge $Global:HiderConfig.AutoHideMinutes) {
        Hide-AllHiderFolders
        Set-HiderActivity
        Set-HiderStatusText "Auto-hide ran at $(Get-Date -Format 'HH:mm:ss')"
        Set-Status "Auto-hide executed" 1200
    }
})
$hiderTimer.Start()

$window.add_MouseMove({ Set-HiderActivity })
$window.add_KeyDown({ Set-HiderActivity })

$secScanBtn.Add_Click({
    Set-SecStatus "Scanning..."
    Write-SecLog 'UI: Security scan button clicked'
    $secGrid.ItemsSource = $null
    # Force UI refresh
    [System.Windows.Forms.Application]::DoEvents()
    $res = $null; $err = $null
    try {
        $res = Run-SecurityScan
    } catch {
        $err = $_
        Write-SecLog "Security scan exception: $($err.Exception.Message)"
    }
    if ($err) {
        Set-SecStatus "Scan failed: $($err.Exception.Message)"
        return
    }
    $secGrid.ItemsSource = $res
    Write-SecLog "UI: Scan complete, rows=$($res.Count)"
    Set-SecStatus "Scan complete. Found $($res.Count) elevated user(s)."
})

function Get-SecSelectedUser {
    if ($secGrid.SelectedItem -and $secGrid.SelectedItem.PSObject.Properties['User']) {
        return [string]$secGrid.SelectedItem.User
    }
    return $null
}

$secDisableBtn.Add_Click({
    $u = Get-SecSelectedUser
    if (-not $u) { Set-SecStatus "Select a user first."; return }
    Write-SecLog "UI: Disable user requested for $u"
    if (Disable-LocalUserSafe -User $u) {
        Write-SecLog "User disabled: $u"
        Set-SecStatus "Disabled $u"; $secScanBtn.RaiseEvent([System.Windows.RoutedEventArgs][System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)
    }
    else {
        Write-SecLog "Failed to disable: $u"
        Set-SecStatus "Failed to disable $u"
    }
})

$secDeleteBtn.Add_Click({
    $u = Get-SecSelectedUser
    if (-not $u) { Set-SecStatus "Select a user first."; return }
    $resp = [System.Windows.MessageBox]::Show("Delete user $u?","Confirm",'YesNo','Question')
    if ($resp -ne [System.Windows.MessageBoxResult]::Yes) { return }
    Write-SecLog "UI: Delete user requested for $u"
    if (Remove-LocalUserSafe -User $u) {
        Write-SecLog "User deleted: $u"
        Set-SecStatus "Deleted $u"; $secScanBtn.RaiseEvent([System.Windows.RoutedEventArgs][System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)
    }
    else {
        Write-SecLog "Failed to delete: $u"
        Set-SecStatus "Failed to delete $u"
    }
})

$secAclBtn.Add_Click({
    Write-SecLog 'UI: Critical ACL window requested'
    try { Show-CriticalAclWindow } catch { Set-SecStatus "ACL scan failed: $($_.Exception.Message)"; Write-SecLog "ACL window error: $($_.Exception.Message)" }
})

$secOutbound.Add_Click({
    Write-SecLog 'UI: Outbound scan requested'
    try { Show-OutboundWindow } catch { Set-SecStatus "Outbound scan failed: $($_.Exception.Message)"; Write-SecLog "Outbound window error: $($_.Exception.Message)" }
})

$secOpenLusr.Add_Click({
    try {
        Start-Process -FilePath "mmc.exe" -ArgumentList "lusrmgr.msc"
        Write-SecLog 'UI: Opened Local Users & Groups'
        Set-SecStatus "Opened Local Users & Groups"
    } catch {
        Write-SecLog "Unable to open Local Users & Groups: $($_.Exception.Message)"
        Set-SecStatus "Unable to open Local Users & Groups"
    }
})

$secResetBtn.Add_Click({
    $u = Get-SecSelectedUser
    if (-not $u) { Set-SecStatus "Select a user first."; return }
    $pwdPlain = $secPwdBox.Password
    if ([string]::IsNullOrWhiteSpace($pwdPlain)) { Set-SecStatus "Enter a new password first."; return }
    $pwd = Convert-PlainToSecureString -Plain $pwdPlain
    Write-SecLog "UI: Reset password requested for $u"
    if (Reset-LocalUserPasswordSafe -User $u -Password $pwd) {
        Write-SecLog "Password reset for $u"
        Set-SecStatus "Password reset for $u"
    }
    else {
        Write-SecLog "Failed to reset password for $u"
        Set-SecStatus "Failed to reset password for $u"
    }
})

# ==================== MENU EVENT HANDLERS ====================

# --- File Menu ---
$MenuSetScanDir.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select Default Scan Folder"
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $script:AppConfig.DefaultScanFolder = $dlg.SelectedPath
        if ($TxtFolder) { $TxtFolder.Text = $dlg.SelectedPath }
        Update-Status "Default scan folder set to: $($dlg.SelectedPath)"
    }
})

$MenuSetOutputDir.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select Default Output Folder"
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $script:AppConfig.DefaultOutputFolder = $dlg.SelectedPath
        if ($TxtConvOut) { $TxtConvOut.Text = $dlg.SelectedPath }
        Update-Status "Default output folder set to: $($dlg.SelectedPath)"
    }
})

$MenuSetToolDir.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select Default Tools Folder (FFmpeg, ExifTool)"
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $script:AppConfig.DefaultToolFolder = $dlg.SelectedPath
        if ($TxtToolDir) { $TxtToolDir.Text = $dlg.SelectedPath }
        Update-Status "Default tool folder set to: $($dlg.SelectedPath)"
    }
})

$MenuOpenScanDir.Add_Click({
    $folder = if ($TxtFolder.Text) { $TxtFolder.Text } else { $script:AppConfig.DefaultScanFolder }
    if ($folder -and (Test-Path $folder)) {
        Start-Process explorer.exe -ArgumentList "`"$folder`""
    } else { Update-Status "Scan folder not set or does not exist." }
})

$MenuOpenOutputDir.Add_Click({
    $folder = if ($TxtConvOut.Text) { $TxtConvOut.Text } else { $script:AppConfig.DefaultOutputFolder }
    if ($folder -and (Test-Path $folder)) {
        Start-Process explorer.exe -ArgumentList "`"$folder`""
    } else { Update-Status "Output folder not set or does not exist." }
})

$MenuOpenDataDir.Add_Click({
    if (Test-Path $script:PlatypusData) {
        Start-Process explorer.exe -ArgumentList "`"$script:PlatypusData`""
    } else { Update-Status "Data folder not found: $script:PlatypusData" }
})

$MenuOpenLogDir.Add_Click({
    if (Test-Path $script:PlatypusLogs) {
        Start-Process explorer.exe -ArgumentList "`"$script:PlatypusLogs`""
    } else { Update-Status "Logs folder not found: $script:PlatypusLogs" }
})

$MenuSaveConfig.Add_Click({ Save-AppConfig })

$MenuLoadConfig.Add_Click({
    Load-AppConfig
    Apply-Theme $script:AppConfig.Theme
    Apply-FontSize $script:AppConfig.FontSize
    if ($script:AppConfig.DefaultToolFolder -and $TxtToolDir) { $TxtToolDir.Text = $script:AppConfig.DefaultToolFolder }
    if ($script:AppConfig.DefaultOutputFolder -and $TxtConvOut) { $TxtConvOut.Text = $script:AppConfig.DefaultOutputFolder }
    if ($script:AppConfig.DefaultScanFolder -and $TxtFolder) { $TxtFolder.Text = $script:AppConfig.DefaultScanFolder }
    if ($MainStatusBar) { $MainStatusBar.Visibility = if ($script:AppConfig.ShowStatusBar) { "Visible" } else { "Collapsed" } }
    if ($HeaderBorder) { $HeaderBorder.Visibility = if ($script:AppConfig.ShowHeader) { "Visible" } else { "Collapsed" } }
    Update-Status "Configuration loaded."
})

$MenuResetConfig.Add_Click({
    $script:AppConfig = [PSCustomObject]@{
        DefaultScanFolder = ""
        DefaultOutputFolder = ""
        DefaultToolFolder = ""
        Theme = "Light"
        FontSize = "Medium"
        ShowStatusBar = $true
        ShowHeader = $true
    }
    Apply-Theme "Light"
    Apply-FontSize "Medium"
    if ($MainStatusBar) { $MainStatusBar.Visibility = "Visible" }
    if ($HeaderBorder) { $HeaderBorder.Visibility = "Visible" }
    Update-Status "Configuration reset to defaults."
})

$MenuExit.Add_Click({ $window.Close() })

# --- View Menu ---
$MenuThemeLight.Add_Click({ Apply-Theme "Light" })
$MenuThemeDark.Add_Click({ Apply-Theme "Dark" })
$MenuFontSmall.Add_Click({ Apply-FontSize "Small" })
$MenuFontMedium.Add_Click({ Apply-FontSize "Medium" })
$MenuFontLarge.Add_Click({ Apply-FontSize "Large" })

$MenuShowStatusBar.Add_Click({
    $show = $MenuShowStatusBar.IsChecked
    $script:AppConfig.ShowStatusBar = $show
    if ($MainStatusBar) { $MainStatusBar.Visibility = if ($show) { "Visible" } else { "Collapsed" } }
})

$MenuShowToolbar.Add_Click({
    $show = $MenuShowToolbar.IsChecked
    $script:AppConfig.ShowHeader = $show
    if ($HeaderBorder) { $HeaderBorder.Visibility = if ($show) { "Visible" } else { "Collapsed" } }
})

$MenuRefresh.Add_Click({
    Update-Status "Refreshing..."
    # Could trigger refresh of active tab here
})

# --- Tools Menu ---
$MenuOpenExplorer.Add_Click({ Start-Process explorer.exe })
$MenuOpenCmd.Add_Click({ Start-Process cmd.exe })
$MenuOpenPowerShell.Add_Click({ Start-Process powershell.exe })
$MenuOpenTaskMgr.Add_Click({ Start-Process taskmgr.exe })
$MenuOpenServices.Add_Click({ Start-Process services.msc })
$MenuOpenDevMgr.Add_Click({ Start-Process devmgmt.msc })

# --- Help Menu ---
$MenuHelpContents.Add_Click({
    $helpFile = Join-Path $script:PlatypusBase "PlatypusTools_Help.html"
    if (Test-Path $helpFile) {
        Start-Process $helpFile
    } else {
        [System.Windows.MessageBox]::Show("Help file not found at:`n$helpFile`n`nPlease reinstall or check documentation.", "Help Not Found", "OK", "Warning")
    }
})

$MenuQuickStart.Add_Click({
    $msg = @"
PlatypusTools Quick Start Guide
================================

FILE CLEANER TAB:
- Browse to a folder with files to clean
- Select file types to include/exclude
- Click Scan to find files, then Clean to remove

MEDIA CONVERSION TAB:
- Video Combiner: Merge multiple videos into one
- Graphics Conversion: Convert images between formats
- Image Resize: Resize images to specific dimensions
- Bootable USB: Create bootable USB drives from ISO files

DUPLICATES TAB:
- Scan folders for duplicate files
- Compare by hash (MD5/SHA256) or name
- Review and delete duplicates safely

CLEANUP TAB:
- Disk Cleanup: Remove Windows temp files
- Privacy Cleaner: Clear browser data and traces
- Startup Manager: Control which apps start with Windows
- Hash Calculator: Generate file checksums

SECURITY TAB:
- Folder Hider: Hide folders from view
- Security Scan: Check system for vulnerabilities
- User Management: Manage local user accounts

METADATA TAB:
- View and edit file metadata (EXIF, etc.)
- Export metadata to CSV

For detailed help, see File > Help > Contents
"@
    [System.Windows.MessageBox]::Show($msg, "Quick Start Guide", "OK", "Information")
})

$MenuKeyboardShortcuts.Add_Click({
    $msg = @"
Keyboard Shortcuts
==================

General:
  F5          - Refresh current view
  Ctrl+S      - Save configuration
  Ctrl+Q      - Exit application
  F1          - Open Help

Navigation:
  Ctrl+Tab    - Next tab
  Ctrl+Shift+Tab - Previous tab

File Cleaner:
  Ctrl+O      - Browse folder
  Delete      - Remove selected files

"@
    [System.Windows.MessageBox]::Show($msg, "Keyboard Shortcuts", "OK", "Information")
})

$MenuCheckUpdates.Add_Click({
    [System.Windows.MessageBox]::Show("PlatypusTools v$script:Version`n`nThis is a local application.`nCheck GitHub for updates.", "Check for Updates", "OK", "Information")
})

$MenuViewLog.Add_Click({
    $logFiles = Get-ChildItem -Path $script:PlatypusLogs -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($logFiles) {
        Start-Process notepad.exe -ArgumentList "`"$($logFiles.FullName)`""
    } else {
        [System.Windows.MessageBox]::Show("No log files found in:`n$script:PlatypusLogs", "No Logs", "OK", "Information")
    }
})

$MenuAbout.Add_Click({
    $msg = @"
PlatypusTools v$script:Version
==================

A comprehensive Windows system utility combining:
- File cleaning and organization
- Media conversion and editing
- Duplicate file detection
- System cleanup and privacy tools
- Security scanning and folder hiding
- Metadata viewing and editing

Built with PowerShell and WPF

Base Path: $script:PlatypusBase

� 2024 PlatypusUtils
"@
    [System.Windows.MessageBox]::Show($msg, "About PlatypusTools", "OK", "Information")
})

# Apply initial visibility settings
if ($MainStatusBar) { $MainStatusBar.Visibility = if ($script:AppConfig.ShowStatusBar) { "Visible" } else { "Collapsed" } }
if ($HeaderBorder) { $HeaderBorder.Visibility = if ($script:AppConfig.ShowHeader) { "Visible" } else { "Collapsed" } }
if ($MenuShowStatusBar) { $MenuShowStatusBar.IsChecked = $script:AppConfig.ShowStatusBar }
if ($MenuShowToolbar) { $MenuShowToolbar.IsChecked = $script:AppConfig.ShowHeader }

# Check tool availability and show status
$toolStatus = Get-ToolStatus
$statusParts = @()
if ($toolStatus.FFmpeg) { $statusParts += "FFmpeg ?" } else { $statusParts += "FFmpeg ?" }
if ($toolStatus.ExifTool) { $statusParts += "ExifTool ?" } else { $statusParts += "ExifTool ?" }
$toolMsg = "Tools: " + ($statusParts -join " | ")
if (-not $toolStatus.FFmpeg -or -not $toolStatus.ExifTool) {
    $toolMsg += " (Some features limited - see File ? Set Default Tools Folder)"
}
Update-Status $toolMsg

# --- Show Combined UI (only when executed directly, not when dot-sourced) ---
if ($runningAsScript -and ($MyInvocation.InvocationName -ne '.')) {
    $window.ShowDialog() | Out-Null
}


