
#Requires -Version 5.1
<#
  FolderHiderPro.ps1
  Multi-folder manager with password-protected unhide, optional ACL restriction,
  auto-hide after inactivity (configurable), export/import configuration, tray icon quick toggle,
  logging, and optional Windows EFS encryption while hidden.

  Notes:
  - Attribute hiding is not strong security; use EFS/BitLocker for real protection.
  - EFS requires NTFS and an edition of Windows that supports EFS (Pro/Enterprise).
  - EFS encryption associates with the current user account (certificate-based).

  Usage:
  powershell -ExecutionPolicy Bypass -File .\FolderHiderPro.ps1
#>

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# --------------------------
# App paths & logging
# --------------------------
$AppDir      = Join-Path $env:APPDATA 'FolderHider'
$ConfigPath  = Join-Path $AppDir 'config.json'
$LogPath     = Join-Path $AppDir 'log.txt'
$AclBypassUsers = @('hide')

# Ensure path variables are populated even if this file was dot-sourced without the top-level initializers.
function Ensure-AppPaths {
    if (-not $script:AppDir -or [string]::IsNullOrWhiteSpace($script:AppDir)) {
        $script:AppDir = Join-Path $env:APPDATA 'FolderHider'
    }
    if (-not $script:ConfigPath -or [string]::IsNullOrWhiteSpace($script:ConfigPath)) {
        $script:ConfigPath = Join-Path $script:AppDir 'config.json'
    }
    if (-not $script:LogPath -or [string]::IsNullOrWhiteSpace($script:LogPath)) {
        $script:LogPath = Join-Path $script:AppDir 'log.txt'
    }
}

function Initialize-AppDir {
    Ensure-AppPaths
    if (-not (Test-Path -LiteralPath $AppDir)) {
        New-Item -ItemType Directory -Path $AppDir | Out-Null
    }
}

function Write-Log {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Message)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Message' 
    Ensure-AppPaths
    Initialize-AppDir
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[$ts] $Message"
    try { Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8 } catch { }
}

# --------------------------
# Config
# --------------------------
# Schema:
# {
#   Folders: [
#     { FolderPath, PasswordRecord {Salt, Hash, Iterations}, AclRestricted, EfsEnabled }
#   ],
#   AutoHideEnabled: true|false,
#   AutoHideMinutes: int
# }

$Global:Config = $null

function Get-DefaultConfig {
    return [PSCustomObject]@{
        Folders         = @()
        AutoHideEnabled = $false
        AutoHideMinutes = 5
    }
}

function Save-Config {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][object]$ConfigObject)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'ConfigObject' 
    Ensure-AppPaths
    Initialize-AppDir
    $json = $ConfigObject | ConvertTo-Json -Depth 6
    Set-Content -LiteralPath $ConfigPath -Encoding UTF8 -Value $json
}

function Get-Config {
    Ensure-AppPaths
    if (Test-Path -LiteralPath $ConfigPath) {
        try {
            $cfg = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
            # Backfill defaults for new fields
            if (-not $cfg.PSObject.Properties.Name -contains 'AutoHideEnabled') { $cfg | Add-Member -NotePropertyName AutoHideEnabled -NotePropertyValue $false }
            if (-not $cfg.PSObject.Properties.Name -contains 'AutoHideMinutes') { $cfg | Add-Member -NotePropertyName AutoHideMinutes -NotePropertyValue 5 }
            foreach ($rec in $cfg.Folders) {
                if (-not $rec.PSObject.Properties.Name -contains 'EfsEnabled') { $rec | Add-Member -NotePropertyName EfsEnabled -NotePropertyValue $false }
            }
            return $cfg
        } catch {
            Write-Log "Config load error: $($_.Exception.Message) - using defaults."
            return Get-DefaultConfig
        }
    } else {
        return Get-DefaultConfig
    }
}

function Get-FolderRecord {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not $Global:Config -or -not $Global:Config.Folders) { return $null }
    foreach ($rec in $Global:Config.Folders) {
        if ($rec.FolderPath -eq $Path) { return $rec }
    }
    return $null
}

function Add-FolderRecord {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not $Global:Config) { $Global:Config = Get-DefaultConfig }
    if (Get-FolderRecord -Path $Path) { return $false }
    $rec = [PSCustomObject]@{
        FolderPath     = $Path
        PasswordRecord = $null
        AclRestricted  = $false
        EfsEnabled     = $false
    }
    $Global:Config.Folders += $rec
    Save-Config -ConfigObject $Global:Config
    Write-Log "Added folder: $Path"
    return $true
}

function Remove-FolderRecord {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not $Global:Config -or -not $Global:Config.Folders) { return $false }
    $new = @()
    $removed = $false
    foreach ($rec in $Global:Config.Folders) {
        if ($rec.FolderPath -ne $Path) {
            $new += $rec
        } else {
            $removed = $true
        }
    }
    $Global:Config.Folders = $new
    Save-Config -ConfigObject $Global:Config
    if ($removed) { Write-Log "Removed folder: $Path" }
    return $removed
}

function Update-Record {
    param(
        [Parameter(Mandatory)][string]$Path,
        [object]$PasswordRecord,
        [Nullable[bool]]$AclRestricted,
        [Nullable[bool]]$EfsEnabled
    [switch]$NonInteractive
    )
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    $rec = Get-FolderRecord -Path $Path
    if (-not $rec) { return $false }
    if ($PSBoundParameters.ContainsKey('PasswordRecord')) { $rec.PasswordRecord = $PasswordRecord }
    if ($PSBoundParameters.ContainsKey('AclRestricted')) { $rec.AclRestricted = [bool]$AclRestricted }
    if ($PSBoundParameters.ContainsKey('EfsEnabled')) { $rec.EfsEnabled = [bool]$EfsEnabled }
    Save-Config -ConfigObject $Global:Config
    return $true
}

function Update-AutoHide {
    param([bool]$Enabled, [int]$Minutes)
    if (-not $Global:Config) { $Global:Config = Get-DefaultConfig }
    if ($PSBoundParameters.ContainsKey('Enabled')) { $Global:Config.AutoHideEnabled = [bool]$Enabled }
    if ($PSBoundParameters.ContainsKey('Minutes')) { $Global:Config.AutoHideMinutes = [Math]::Max(1, [int]$Minutes) }
    Save-Config -ConfigObject $Global:Config
}

# --------------------------
# Password Hashing (PBKDF2)
# --------------------------
function Convert-PlainToSecureString {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Plain)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Plain' 
    $ss = New-Object System.Security.SecureString
    foreach ($c in $Plain.ToCharArray()) { $ss.AppendChar($c) }
    $ss.MakeReadOnly()
    return $ss
}

function Convert-SecureStringToPlainText {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][System.Security.SecureString]$Secure)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Secure' 
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringAuto($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function New-PasswordRecord {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][System.Security.SecureString]$Password)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Password' 
    $saltBytes = New-Object byte[] 16
    $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    $rng.GetBytes($saltBytes)

    $iterations = 100000
    $plain = Convert-SecureStringToPlainText -Secure $Password
    try {
        $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
            $plain,
            $saltBytes,
            $iterations,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256
        )
    } catch {
        # Fallback for older .NET on some PS 5.1 installs: uses HMAC-SHA1
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
        [Parameter(Mandatory)][object]$PasswordRecord
    [switch]$NonInteractive
    )
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Password' 
Require-Parameter 'PasswordRecord' 
    try {
        $saltBytes = [Convert]::FromBase64String($PasswordRecord.Salt)
        $iterations = [int]$PasswordRecord.Iterations
        $plain = Convert-SecureStringToPlainText -Secure $Password
        try {
            $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
                $plain,
                $saltBytes,
                $iterations,
                [System.Security.Cryptography.HashAlgorithmName]::SHA256
            )
        } catch {
            # Fallback for older .NET on some PS 5.1 installs: uses HMAC-SHA1
            $pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($plain, $saltBytes, $iterations)
        }
        $hashBytes = $pbkdf2.GetBytes(32)
        $hashB64   = [Convert]::ToBase64String($hashBytes)
        return ($hashB64 -eq $PasswordRecord.Hash)
    } catch {
        return $false
    }
}

# --------------------------
# Hidden / ACL helpers
# --------------------------
function Get-HiddenState {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        if (-not $PSBoundParameters.ContainsKey('Path') -or [string]::IsNullOrWhiteSpace($Path)) { Write-Log "Get-HiddenState: Path null or empty."; return $false }
        if (-not (Test-Path -LiteralPath $Path)) { return $false }
        $attr = [System.IO.File]::GetAttributes($Path)
        return ($attr.HasFlag([IO.FileAttributes]::Hidden) -and $attr.HasFlag([IO.FileAttributes]::System))
    } catch {
        return $false
    }
}

function Set-Hidden {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        if (-not $PSBoundParameters.ContainsKey('Path') -or [string]::IsNullOrWhiteSpace($Path)) { Write-Log "Set-Hidden: Path null or empty."; return }
        if (-not (Test-Path -LiteralPath $Path)) { Write-Log "Set-Hidden: Path missing '$Path'"; return }
        $attrs = [System.IO.File]::GetAttributes($Path)
        $attrs = $attrs -bor [IO.FileAttributes]::Hidden
        $attrs = $attrs -bor [IO.FileAttributes]::System
        [System.IO.File]::SetAttributes($Path, $attrs)
        return $true
    } catch {
        Write-Log "Set-Hidden error on '$Path': $($_.Exception.Message)"
        return $false
    }
}

function Clear-Hidden {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        if (-not $PSBoundParameters.ContainsKey('Path') -or [string]::IsNullOrWhiteSpace($Path)) { Write-Log "Clear-Hidden: Path null or empty."; return }
        if (-not (Test-Path -LiteralPath $Path)) { Write-Log "Clear-Hidden: Path missing '$Path'"; return }
        $attrs = [System.IO.File]::GetAttributes($Path)
        $attrs = $attrs -band (-bnot [IO.FileAttributes]::Hidden)
        $attrs = $attrs -band (-bnot [IO.FileAttributes]::System)
        [System.IO.File]::SetAttributes($Path, $attrs)
        return $true
    } catch {
        Write-Log "Clear-Hidden error on '$Path': $($_.Exception.Message)"
        return $false
    }
}

# --------------------------
# ACL helpers (optional deny Everyone Read/Execute)
# --------------------------
function Set-AclRestriction {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        if (-not $PSBoundParameters.ContainsKey('Path') -or [string]::IsNullOrWhiteSpace($Path)) { Write-Log "Set-AclRestriction: Path is null or empty."; return $false }
        if (-not (Test-Path -LiteralPath $Path)) { Write-Log "Set-AclRestriction: Path missing '$Path'"; return $false }
        $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $sidEveryone = New-Object System.Security.Principal.SecurityIdentifier('S-1-1-0') # Everyone
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
    } catch {
        Write-Log "Set-AclRestriction error on '$Path': $($_.Exception.Message)"
        return $false
    }
}

function Restore-ACL {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        if (-not $PSBoundParameters.ContainsKey('Path') -or [string]::IsNullOrWhiteSpace($Path)) { Write-Log "Restore-ACL: Path is null or empty."; return $false }
        if (-not (Test-Path -LiteralPath $Path)) { Write-Log "Restore-ACL: Path missing '$Path'"; return $false }
        $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        $sidEveryone = New-Object System.Security.Principal.SecurityIdentifier('S-1-1-0')
        $rulesToRemove = @()
        foreach ($r in $acl.Access) {
            $isEveryone = $false
            try { $isEveryone = ($r.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $sidEveryone.Value) } catch { }
            if ($isEveryone -and $r.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Deny) {
                $rulesToRemove += $r
            }
        }
        foreach ($r in $rulesToRemove) { [void]$acl.RemoveAccessRuleSpecific($r) }
        Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop

        # Fallback: if any deny for Everyone remains, reset with icacls to avoid locking the folder
        $hasDeny = {
            param($p, $sidObj)
            $checkAcl = Get-Acl -LiteralPath $p -ErrorAction Stop
            foreach ($rule in $checkAcl.Access) {
                $isEveryone = $false
                try { $isEveryone = ($rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $sidObj.Value) } catch { }
                if ($isEveryone -and $rule.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Deny) { return $true }
            }
            return $false
        }

        if (& $hasDeny $Path $sidEveryone) {
            $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            $null = & icacls "$Path" /remove:d Everyone /inheritance:e /grant:r "${currentUser}:(OI)(CI)F" /T /C 2>$null
        }

        return -not (& $hasDeny $Path $sidEveryone)
    } catch {
        Write-Log "Restore-ACL error on '$Path': $($_.Exception.Message)"
        return $false
    }
}

# --------------------------
# EFS helpers (cipher.exe)
# --------------------------
function Test-EFSAvailable {
    $cipherPath = Join-Path $env:windir 'System32\cipher.exe'
    return (Test-Path -LiteralPath $cipherPath)
}

function Test-DriveNTFS {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        $root = [System.IO.Path]::GetPathRoot($Path)
        $di = New-Object System.IO.DriveInfo($root)
        return ($di.DriveFormat -eq 'NTFS')
    } catch {
        return $false
    }
}

function Invoke-Cipher {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Arguments)
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
    $out = $proc.StandardOutput.ReadToEnd()
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    return @{ ExitCode = $proc.ExitCode; StdOut = $out; StdErr = $err }
}

function Enable-EFS {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not (Test-Path -LiteralPath $Path)) { Write-Log "Enable-EFS: Path missing '$Path'"; return $false }
    if (-not (Test-EFSAvailable)) { Write-Log "Enable-EFS: EFS not available on this system"; return $false }
    if (-not (Test-DriveNTFS -Path $Path)) { Write-Log "Enable-EFS: Non-NTFS volume for '$Path'"; return $false }

    $res = Invoke-Cipher -Arguments "/E /S:`"$Path`""
    Write-Log "EFS Encrypt '$Path': Exit=$($res.ExitCode) OUT=`n$($res.StdOut)`nERR=`n$($res.StdErr)"
    return ($res.ExitCode -eq 0)
}

function Disable-EFS {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not (Test-Path -LiteralPath $Path)) { Write-Log "Disable-EFS: Path missing '$Path'"; return $false }
    if (-not (Test-EFSAvailable)) { Write-Log "Disable-EFS: EFS not available on this system"; return $false }
    if (-not (Test-DriveNTFS -Path $Path)) { Write-Log "Disable-EFS: Non-NTFS volume for '$Path'"; return $false }

    $res = Invoke-Cipher -Arguments "/D /S:`"$Path`""
    Write-Log "EFS Decrypt '$Path': Exit=$($res.ExitCode) OUT=`n$($res.StdOut)`nERR=`n$($res.StdErr)"
    return ($res.ExitCode -eq 0)
}

# --------------------------
# Password strength meter
# --------------------------
function Get-PasswordStrength {
    param([string]$Password)
    if ([string]::IsNullOrWhiteSpace($Password)) {
        return @{ Score = 0; Label = 'Very weak'; Color = [System.Drawing.Color]::Firebrick }
    }
    $len = $Password.Length
    $hasLower = ($Password -match '[a-z]')
    $hasUpper = ($Password -match '[A-Z]')
    $hasDigit = ($Password -match '\d')
    $hasSpecial = ($Password -match '[^a-zA-Z0-9]')

    $lengthScore = [Math]::Min(60, $len * 4) # up to 60
    $varScore = 0; foreach ($b in @($hasLower,$hasUpper,$hasDigit,$hasSpecial)) { if ($b) { $varScore += 10 } } # up to 40
    if (($hasLower + $hasUpper + $hasDigit + $hasSpecial) -le 1) { $varScore = [Math]::Max(0, $varScore - 10) }

    $score = [Math]::Min(100, $lengthScore + $varScore)

    if ($score -lt 25) { $label = 'Very weak'; $color = [System.Drawing.Color]::Firebrick }
    elseif ($score -lt 50) { $label = 'Weak'; $color = [System.Drawing.Color]::Tomato }
    elseif ($score -lt 70) { $label = 'Medium'; $color = [System.Drawing.Color]::DarkOrange }
    elseif ($score -lt 85) { $label = 'Strong'; $color = [System.Drawing.Color]::ForestGreen }
    else { $label = 'Very strong'; $color = [System.Drawing.Color]::SeaGreen }

    return @{ Score = $score; Label = $label; Color = $color }
}

# --------------------------
# GUI: Forms & Controls
# --------------------------
$Global:Config = Get-Config
$script:PendingChanges = $false
$script:SuppressPending = $false

function Should-ApplyAclRestriction {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][object]$Record)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Record' 
    if (-not $Record) { return $false }
    if (-not $Record.AclRestricted) { return $false }
    $current = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $sam = ($current -split '\\')[-1]
    return -not ($AclBypassUsers -contains $sam)
}

$form                     = New-Object System.Windows.Forms.Form
$form.Text                = 'Folder Hider (Pro)'
$form.StartPosition       = 'CenterScreen'
$form.FormBorderStyle     = 'Sizable'
$form.MaximizeBox         = $true
$form.MinimizeBox         = $true
$form.ClientSize          = New-Object System.Drawing.Size(840, 560)
$form.MinimumSize         = New-Object System.Drawing.Size(820, 520)

# ListView
$lvFolders                = New-Object System.Windows.Forms.ListView
$lvFolders.Location       = New-Object System.Drawing.Point(10, 10)
$lvFolders.Size           = New-Object System.Drawing.Size(820, 270)
$lvFolders.View           = 'Details'
$lvFolders.FullRowSelect  = $true
$lvFolders.GridLines      = $true
$lvFolders.HideSelection  = $false
$lvFolders.Anchor         = 'Top,Left,Right,Bottom'
[void]$lvFolders.Columns.Add('Folder', 410)
[void]$lvFolders.Columns.Add('Status', 120)
[void]$lvFolders.Columns.Add('ACL Restricted', 120)
[void]$lvFolders.Columns.Add('EFS Enabled', 120)

# Row 1 (actions)
$btnAdd                   = New-Object System.Windows.Forms.Button
$btnAdd.Text              = 'Add Folder...'
$btnAdd.Location          = New-Object System.Drawing.Point(10, 290)
$btnAdd.Size              = New-Object System.Drawing.Size(120, 28)

$btnRemove                = New-Object System.Windows.Forms.Button
$btnRemove.Text           = 'Remove'
$btnRemove.Location       = New-Object System.Drawing.Point(140, 290)
$btnRemove.Size           = New-Object System.Drawing.Size(90, 28)

$btnSetPassword           = New-Object System.Windows.Forms.Button
$btnSetPassword.Text      = 'Set / Change Password'
$btnSetPassword.Location  = New-Object System.Drawing.Point(240, 290)
$btnSetPassword.Size      = New-Object System.Drawing.Size(170, 28)

$btnHide                  = New-Object System.Windows.Forms.Button
$btnHide.Text             = 'Hide'
$btnHide.Location         = New-Object System.Drawing.Point(420, 290)
$btnHide.Size             = New-Object System.Drawing.Size(80, 28)

$btnUnhide                = New-Object System.Windows.Forms.Button
$btnUnhide.Text           = 'Unhide'
$btnUnhide.Location       = New-Object System.Drawing.Point(510, 290)
$btnUnhide.Size           = New-Object System.Drawing.Size(80, 28)

$btnOpenLog               = New-Object System.Windows.Forms.Button
$btnOpenLog.Text          = 'Open Log'
$btnOpenLog.Location      = New-Object System.Drawing.Point(600, 290)
$btnOpenLog.Size          = New-Object System.Drawing.Size(100, 28)

$btnExport                = New-Object System.Windows.Forms.Button
$btnExport.Text           = 'Export Config'
$btnExport.Location       = New-Object System.Drawing.Point(710, 290)
$btnExport.Size           = New-Object System.Drawing.Size(100, 28)

# Manual path entry (for paste/typing)
$lblManualPath            = New-Object System.Windows.Forms.Label
$lblManualPath.Text       = 'Manual path:'
$lblManualPath.Location   = New-Object System.Drawing.Point(10, 325)
$lblManualPath.AutoSize   = $true

$txtManualPath            = New-Object System.Windows.Forms.TextBox
$txtManualPath.Location   = New-Object System.Drawing.Point(110, 323)
$txtManualPath.Size       = New-Object System.Drawing.Size(480, 22)
$txtManualPath.Anchor     = 'Top,Left,Right'

$btnAddManual             = New-Object System.Windows.Forms.Button
$btnAddManual.Text        = 'Add Path'
$btnAddManual.Location    = New-Object System.Drawing.Point(600, 321)
$btnAddManual.Size        = New-Object System.Drawing.Size(90, 26)

# Row 2 (per-folder toggles & unhide password)
$chkAcl                   = New-Object System.Windows.Forms.CheckBox
$chkAcl.Text              = 'Restrict access while hidden (ACL) for selected folder'
$chkAcl.Location          = New-Object System.Drawing.Point(10, 355)
$chkAcl.AutoSize          = $true

$chkEfs                   = New-Object System.Windows.Forms.CheckBox
$chkEfs.Text              = 'Encrypt with EFS while hidden (selected folder)'
$chkEfs.Location          = New-Object System.Drawing.Point(10, 385)
$chkEfs.AutoSize          = $true

$lblEnterPwd              = New-Object System.Windows.Forms.Label
$lblEnterPwd.Text         = 'Password to unhide selected:'
$lblEnterPwd.Location     = New-Object System.Drawing.Point(10, 415)
$lblEnterPwd.AutoSize     = $true

$txtEnterPwd              = New-Object System.Windows.Forms.TextBox
$txtEnterPwd.Location     = New-Object System.Drawing.Point(200, 413)
$txtEnterPwd.Size         = New-Object System.Drawing.Size(220, 22)
$txtEnterPwd.UseSystemPasswordChar = $true

# Row 3 (auto-hide and import)
$chkAutoHide              = New-Object System.Windows.Forms.CheckBox
$chkAutoHide.Text         = 'Enable auto-hide after inactivity'
$chkAutoHide.Location     = New-Object System.Drawing.Point(10, 445)
$chkAutoHide.AutoSize     = $true

$lblAutoHide              = New-Object System.Windows.Forms.Label
$lblAutoHide.Text         = 'Minutes of inactivity:'
$lblAutoHide.Location     = New-Object System.Drawing.Point(240, 445)
$lblAutoHide.AutoSize     = $true

$nudAutoHide              = New-Object System.Windows.Forms.NumericUpDown
$nudAutoHide.Location     = New-Object System.Drawing.Point(380, 442)
$nudAutoHide.Size         = New-Object System.Drawing.Size(60, 22)
$nudAutoHide.Minimum      = 1
$nudAutoHide.Maximum      = 240
$nudAutoHide.Value        = [decimal]$Global:Config.AutoHideMinutes

$lblAutoStatus            = New-Object System.Windows.Forms.Label
$lblAutoStatus.Text       = 'Auto-hide status: Disabled'
$lblAutoStatus.Location   = New-Object System.Drawing.Point(450, 445)
$lblAutoStatus.AutoSize   = $true
$lblAutoStatus.ForeColor  = [System.Drawing.Color]::DimGray

$btnImport                = New-Object System.Windows.Forms.Button
$btnImport.Text           = 'Import Config'
$btnImport.Location       = New-Object System.Drawing.Point(700, 442)
$btnImport.Size           = New-Object System.Drawing.Size(110, 28)

$btnScanHidden            = New-Object System.Windows.Forms.Button
$btnScanHidden.Text       = 'Scan Hidden...'
$btnScanHidden.Location   = New-Object System.Drawing.Point(700, 475)
$btnScanHidden.Size       = New-Object System.Drawing.Size(110, 28)

$lblStatus                = New-Object System.Windows.Forms.Label
$lblStatus.Text           = 'Status:'
$lblStatus.Location       = New-Object System.Drawing.Point(10, 510)
$lblStatus.AutoSize       = $true
$lblStatus.ForeColor      = [System.Drawing.Color]::DarkBlue

$btnApply                 = New-Object System.Windows.Forms.Button
$btnApply.Text            = 'Apply'
$btnApply.Location        = New-Object System.Drawing.Point(630, 475)
$btnApply.Size            = New-Object System.Drawing.Size(80, 28)

$btnReset                 = New-Object System.Windows.Forms.Button
$btnReset.Text            = 'Reset'
$btnReset.Location        = New-Object System.Drawing.Point(720, 475)
$btnReset.Size            = New-Object System.Drawing.Size(80, 28)

$form.Controls.AddRange(@(
    $lvFolders, $btnAdd, $btnRemove, $btnSetPassword, $btnHide, $btnUnhide, $btnOpenLog, $btnExport,
    $lblManualPath, $txtManualPath, $btnAddManual,
    $chkAcl, $chkEfs, $lblEnterPwd, $txtEnterPwd,
    $chkAutoHide, $lblAutoHide, $nudAutoHide, $lblAutoStatus, $btnImport, $btnScanHidden,
    $lblStatus, $btnApply, $btnReset
))

# --------------------------
# UI helpers
# --------------------------
function Update-StatusLabel {
    param([int]$Count)
    $pendingSuffix = if ($script:PendingChanges) { ' Pending changes - click Apply.' } else { '' }
    $lblStatus.Text = "Status: $Count folder(s) listed. Log: $LogPath$pendingSuffix"
}

function Set-PendingState {
    param([bool]$State = $true)
    $script:PendingChanges = $State
    Update-StatusLabel -Count $lvFolders.Items.Count
}

function Reset-UiFromConfig {
    $script:SuppressPending = $true
    $chkAutoHide.Checked = [bool]$Global:Config.AutoHideEnabled
    $nudAutoHide.Value = [decimal][Math]::Min([double]$nudAutoHide.Maximum, [Math]::Max([double]$nudAutoHide.Minimum, [double]$Global:Config.AutoHideMinutes))
    $lblAutoStatus.Text = if ($Global:Config.AutoHideEnabled) { 'Auto-hide enabled' } else { 'Auto-hide status: Disabled' }

    $path = Get-SelectedPath
    if ($path) {
        $rec = Get-FolderRecord -Path $path
        if ($rec) {
            $chkAcl.Checked = [bool]$rec.AclRestricted
            $chkEfs.Checked = [bool]$rec.EfsEnabled
        } else {
            $chkAcl.Checked = $false
            $chkEfs.Checked = $false
        }
    } else {
        $chkAcl.Checked = $false
        $chkEfs.Checked = $false
    }
    $script:SuppressPending = $false
    $txtEnterPwd.Clear()
    Set-PendingState -State:$false
}

$folderDialog = New-Object System.Windows.Forms.FolderBrowserDialog
$folderDialog.Description = 'Choose a folder to manage'
$folderDialog.ShowNewFolderButton = $false

$saveDialog = New-Object System.Windows.Forms.SaveFileDialog
$saveDialog.Title = 'Export configuration'
$saveDialog.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'
$saveDialog.FileName = 'FolderHiderConfig.json'

$openDialog = New-Object System.Windows.Forms.OpenFileDialog
$openDialog.Title = 'Import configuration'
$openDialog.Filter = 'JSON files (*.json)|*.json|All files (*.*)|*.*'

# --------------------------
# Password dialog with meter
# --------------------------
function Show-SetPasswordDialog {
    $dlg           = New-Object System.Windows.Forms.Form
    $dlg.Text      = 'Set / Change Password'
    $dlg.StartPosition = 'CenterParent'
    $dlg.FormBorderStyle= 'FixedDialog'
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false
    $dlg.ClientSize = New-Object System.Drawing.Size(420, 210)

    $lbl1 = New-Object System.Windows.Forms.Label
    $lbl1.Text = 'New password:'
    $lbl1.Location = New-Object System.Drawing.Point(10, 20)
    $lbl1.AutoSize = $true

    $txt1 = New-Object System.Windows.Forms.TextBox
    $txt1.Location = New-Object System.Drawing.Point(130, 18)
    $txt1.Size = New-Object System.Drawing.Size(260, 22)
    $txt1.UseSystemPasswordChar = $true

    $lblMeter = New-Object System.Windows.Forms.Label
    $lblMeter.Text = 'Strength:'
    $lblMeter.Location = New-Object System.Drawing.Point(10, 55)
    $lblMeter.AutoSize = $true

    $pbMeter = New-Object System.Windows.Forms.ProgressBar
    $pbMeter.Location = New-Object System.Drawing.Point(130, 52)
    $pbMeter.Size = New-Object System.Drawing.Size(260, 20)
    $pbMeter.Minimum = 0
    $pbMeter.Maximum = 100

    $lbl2 = New-Object System.Windows.Forms.Label
    $lbl2.Text = 'Confirm password:'
    $lbl2.Location = New-Object System.Drawing.Point(10, 90)
    $lbl2.AutoSize = $true

    $txt2 = New-Object System.Windows.Forms.TextBox
    $txt2.Location = New-Object System.Drawing.Point(130, 88)
    $txt2.Size = New-Object System.Drawing.Size(260, 22)
    $txt2.UseSystemPasswordChar = $true

    $btnOK = New-Object System.Windows.Forms.Button
    $btnOK.Text = 'Save'
    $btnOK.Location = New-Object System.Drawing.Point(230, 130)
    $btnOK.Size = New-Object System.Drawing.Size(80, 28)
    $btnOK.DialogResult = [System.Windows.Forms.DialogResult]::OK

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = 'Cancel'
    $btnCancel.Location = New-Object System.Drawing.Point(320, 130)
    $btnCancel.Size = New-Object System.Drawing.Size(80, 28)
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel

    $lblHint = New-Object System.Windows.Forms.Label
    $lblHint.Text = 'Tip: use 12+ chars, mix upper/lower, digits, and symbols.'
    $lblHint.Location = New-Object System.Drawing.Point(10, 170)
    $lblHint.AutoSize = $true
    $lblHint.ForeColor = [System.Drawing.Color]::DimGray

    $dlg.Controls.AddRange(@($lbl1,$txt1,$lblMeter,$pbMeter,$lbl2,$txt2,$btnOK,$btnCancel,$lblHint))
    $dlg.AcceptButton = $btnOK
    $dlg.CancelButton = $btnCancel

    # Live strength update
    $updateMeter = {
        $res = Get-PasswordStrength -Password $txt1.Text
        $pbMeter.Value = $res.Score
        $lblMeter.Text = "Strength: $($res.Label)"
        $lblMeter.ForeColor = $res.Color
    }
    $txt1.add_TextChanged($updateMeter)
    & $updateMeter

    $result = $dlg.ShowDialog($form)
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
        $p1 = $txt1.Text
        $p2 = $txt2.Text
        if ([string]::IsNullOrWhiteSpace($p1) -or [string]::IsNullOrWhiteSpace($p2)) {
            [System.Windows.Forms.MessageBox]::Show('Password cannot be empty.', 'Error', 'OK', 'Error') | Out-Null
            return $null
        }
        if ($p1 -ne $p2) {
            [System.Windows.Forms.MessageBox]::Show('Passwords do not match.', 'Error', 'OK', 'Error') | Out-Null
            return $null
        }
        return Convert-PlainToSecureString -Plain $p1
    }
    return $null
}

function Show-PasswordPrompt {
    param([string]$Title = 'Enter password')
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = $Title
    $dlg.StartPosition = 'CenterParent'
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.ClientSize = New-Object System.Drawing.Size(360, 120)
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = 'Password:'
    $lbl.Location = New-Object System.Drawing.Point(10, 20)
    $lbl.AutoSize = $true
    $txt = New-Object System.Windows.Forms.TextBox
    $txt.Location = New-Object System.Drawing.Point(90, 18)
    $txt.Size = New-Object System.Drawing.Size(250, 22)
    $txt.UseSystemPasswordChar = $true
    $btnOK = New-Object System.Windows.Forms.Button
    $btnOK.Text = 'OK'
    $btnOK.Location = New-Object System.Drawing.Point(180, 60)
    $btnOK.Size = New-Object System.Drawing.Size(70, 28)
    $btnOK.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = 'Cancel'
    $btnCancel.Location = New-Object System.Drawing.Point(260, 60)
    $btnCancel.Size = New-Object System.Drawing.Size(80, 28)
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $dlg.Controls.AddRange(@($lbl,$txt,$btnOK,$btnCancel))
    $dlg.AcceptButton = $btnOK
    $dlg.CancelButton = $btnCancel

    $res = $dlg.ShowDialog($form)
    if ($res -eq [System.Windows.Forms.DialogResult]::OK) { return Convert-PlainToSecureString -Plain $txt.Text }
    return $null
}

# --------------------------
# Tray icon & menu
# --------------------------
$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
try {
    $proc = Get-Process -Id $PID -ErrorAction Stop
    $exePath = $proc.MainModule.FileName
} catch {
    $exePath = Join-Path $env:windir 'System32\notepad.exe'
}
$notifyIcon.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exePath)
$notifyIcon.Visible = $true
$notifyIcon.Text = 'Folder Hider (Pro)'

$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
$notifyIcon.ContextMenuStrip = $trayMenu

# Helper to hide all folders (avoids long lines getting mangled)
function Hide-AllManagedFolders {
    Set-LastActivity
    foreach ($rec in $Global:Config.Folders) {
        $path = $rec.FolderPath
        try {
            if (Test-Path -LiteralPath $path) {
                Set-Hidden -Path $path
                if (Should-ApplyAclRestriction -Record $rec) {
                    [void](Set-AclRestriction -Path $path)
                } elseif ($rec.AclRestricted) {
                    Write-Log "ACL restriction skipped for '$path' (bypass user)."
                }
                if ($rec.EfsEnabled)    { [void](Enable-EFS -Path $path) }
                Write-Log "Tray: Hid folder '$path'"
            }
        } catch {
            Write-Log "Tray: Error hiding '$path': $($_.Exception.Message)"
        }
    }
    Update-ListView
    Update-TrayMenu
}

function Update-TrayMenu {
    $trayMenu.Items.Clear()

    # Show Window
    $miShow = $trayMenu.Items.Add('Show Window')
    $miShow.add_Click({ $form.Show(); $form.WindowState = 'Normal'; $form.Activate(); Set-LastActivity })

    # Separator
    [void]$trayMenu.Items.Add('-')

    # Folder submenu
    $miFolders = $trayMenu.Items.Add('Folders')
    $miFolders.DropDownItems.Clear()

    foreach ($rec in $Global:Config.Folders) {
        # Capture per-item locals to avoid closure issues
        $p  = $rec.FolderPath
        $nm = [System.IO.Path]::GetFileName($p)
        if ([string]::IsNullOrEmpty($nm)) { $nm = $p }
        $r  = $rec

        $exists   = Test-Path -LiteralPath $p
        $isHidden = $false
        if ($exists) { $isHidden = Get-HiddenState -Path $p }

        # PowerShell 5.1-safe status
        $stateLabel = $( if ($exists) { if ($isHidden) { 'Hidden' } else { 'Visible' } } else { 'Missing' } )
        $text = "$nm - $stateLabel"

        $item = New-Object System.Windows.Forms.ToolStripMenuItem($text)
        $item.add_Click({
            Set-LastActivity

            # Re-evaluate state at click time
            $existsNow   = Test-Path -LiteralPath $p
            if (-not $existsNow) {
                [System.Windows.Forms.MessageBox]::Show("Path missing:`r`n$p", 'Error', 'OK', 'Error') | Out-Null
                return
            }
            $isHiddenNow = Get-HiddenState -Path $p

            try {
                if ($isHiddenNow) {
                    if (-not $r.PasswordRecord) {
                        [System.Windows.Forms.MessageBox]::Show("No password set for:`r`n$p", 'Password required', 'OK', 'Information') | Out-Null
                        return
                    }
                    $enteredPwdSecure = Show-PasswordPrompt -Title "Unhide '$nm'"
                    if (-not $enteredPwdSecure) { return }
                    if (-not (Test-Password -Password $enteredPwdSecure -PasswordRecord $r.PasswordRecord)) {
                        [System.Windows.Forms.MessageBox]::Show('Incorrect password.', 'Access denied', 'OK', 'Error') | Out-Null
                        return
                    }
                    Clear-Hidden -Path $p
                    if (Should-ApplyAclRestriction -Record $r) {
                        [void](Restore-ACL -Path $p)
                    } elseif ($r.AclRestricted) {
                        Write-Log "ACL restore skipped for '$p' (bypass user)."
                    }
                    if ($r.EfsEnabled)    { [void](Disable-EFS -Path $p) }
                    Write-Log "Tray: Unhid folder '$p'"
                } else {
                    Set-Hidden -Path $p
                    if (Should-ApplyAclRestriction -Record $r) {
                        [void](Set-AclRestriction -Path $p)
                    } elseif ($r.AclRestricted) {
                        Write-Log "ACL restriction skipped for '$p' (bypass user)."
                    }
                    if ($r.EfsEnabled)    { [void](Enable-EFS -Path $p) }
                    Write-Log "Tray: Hid folder '$p'"
                }
                Update-ListView
                Update-TrayMenu
            } catch {
                [System.Windows.Forms.MessageBox]::Show("Tray toggle error:`r`n$($_.Exception.Message)", 'Error', 'OK', 'Error') | Out-Null
                Write-Log "Tray toggle error on '$p': $($_.Exception.Message)"
            }
        }.GetNewClosure())

        [void]$miFolders.DropDownItems.Add($item)
    }

    # Separator
    [void]$trayMenu.Items.Add('-')

    # Hide All (call helper to avoid long lines and wrapping)
    $miHideAll = $trayMenu.Items.Add('Hide All')
    $miHideAll.add_Click({ Hide-AllManagedFolders })

    # Exit
    $miExit = $trayMenu.Items.Add('Exit')
    $miExit.add_Click({
        $notifyIcon.Visible = $false
        $form.Close()
        $form.Dispose()
        Write-Log "App exited."
        [System.Windows.Forms.Application]::Exit()
    })
}

# --------------------------
# ListView population
# --------------------------
function Update-ListView {
    $lvFolders.Items.Clear()
    foreach ($rec in $Global:Config.Folders) {
        $path = $rec.FolderPath
        $exists = Test-Path -LiteralPath $path
        $isHidden = $false
        if ($exists) { $isHidden = Get-HiddenState -Path $path }
        $status = if (-not $exists) { 'Missing' } elseif ($isHidden) { 'HIDDEN' } else { 'VISIBLE' }
        $aclTxt = if ($rec.AclRestricted) { 'Yes' } else { 'No' }
        $efsTxt = if ($rec.EfsEnabled) { 'Yes' } else { 'No' }
        $item = New-Object System.Windows.Forms.ListViewItem($path)
        [void]$item.SubItems.Add($status)
        [void]$item.SubItems.Add($aclTxt)
        [void]$item.SubItems.Add($efsTxt)
        [void]$lvFolders.Items.Add($item)
    }
    Update-StatusLabel -Count $lvFolders.Items.Count
}

function Get-SelectedPath {
    if ($lvFolders.SelectedItems.Count -eq 0) { return $null }
    return $lvFolders.SelectedItems[0].Text
}

function Get-ActivePath {
    $selected = Get-SelectedPath
    if ($selected) { return $selected }
    $manual = $txtManualPath.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($manual)) { return $null }
    return $manual
}

# --------------------------
# Inactivity (auto-hide)
# --------------------------
$Global:lastActivity = Get-Date
$inactivityTimer = New-Object System.Windows.Forms.Timer
$inactivityTimer.Interval = 1000  # 1 second

function Set-LastActivity { $Global:lastActivity = Get-Date }

$inactivityTimer.add_Tick({
    if ($script:PendingChanges) { return }
    if (-not $Global:Config.AutoHideEnabled) {
        $lblAutoStatus.Text = 'Auto-hide status: Disabled'
        return
    }
    $elapsed = (New-TimeSpan -Start $Global:lastActivity -End (Get-Date)).TotalMinutes
    $remaining = [Math]::Max(0, [Math]::Round($Global:Config.AutoHideMinutes - $elapsed, 1))
    $lblAutoStatus.Text = "Auto-hide in: $remaining min"
    if ($elapsed -ge $Global:Config.AutoHideMinutes) {
        Write-Log "Auto-hide triggered after $([Math]::Round($elapsed,2)) minutes of inactivity."
        foreach ($rec in $Global:Config.Folders) {
            $pah = $rec.FolderPath
            try {
                if (Test-Path -LiteralPath $pah) {
                    if (-not (Get-HiddenState -Path $pah)) {
                        Set-Hidden -Path $pah
                        if (Should-ApplyAclRestriction -Record $rec) {
                            [void](Set-AclRestriction -Path $pah)
                        } elseif ($rec.AclRestricted) {
                            Write-Log "Auto-hide ACL skipped for '$pah' (bypass user)."
                        }
                        if ($rec.EfsEnabled)    { [void](Enable-EFS -Path $pah) }
                        Write-Log "Auto-hide: Hid '$pah'"
                    }
                }
            } catch { Write-Log "Auto-hide error on '$pah': $($_.Exception.Message)" }
        }
                Update-ListView
                Update-TrayMenu
        $notifyIcon.ShowBalloonTip(2000, 'Folder Hider', 'Auto-hide completed.', [System.Windows.Forms.ToolTipIcon]::Info)
            Set-LastActivity  # reset to avoid re-triggering immediately
    }
})
$inactivityTimer.Start()

# Hook interaction events to reset inactivity
$form.add_MouseMove({ Set-LastActivity })
$form.add_KeyDown({ Set-LastActivity })
$lvFolders.add_MouseClick({ Set-LastActivity })
$lvFolders.add_KeyDown({ Set-LastActivity })
$trayMenu.add_Click({ Set-LastActivity })
$notifyIcon.add_DoubleClick({ Set-LastActivity })

# --------------------------
# Events (buttons & controls)
# --------------------------
$btnAdd.Add_Click({
    $result = $folderDialog.ShowDialog()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
        $path = $folderDialog.SelectedPath
        Set-LastActivity
        if (-not (Test-Path -LiteralPath $path)) {
            [System.Windows.Forms.MessageBox]::Show('Selected folder does not exist.', 'Error', 'OK', 'Error') | Out-Null
            return
        }
        if (Add-FolderRecord -Path $path) {
            Update-ListView
            Update-TrayMenu
            [System.Windows.Forms.MessageBox]::Show('Folder added.', 'Success', 'OK', 'Information') | Out-Null
        } else {
            [System.Windows.Forms.MessageBox]::Show('Folder already exists in the list.', 'Info', 'OK', 'Information') | Out-Null
        }
    }
})

$btnAddManual.Add_Click({
    Set-LastActivity
    $path = $txtManualPath.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($path)) {
        [System.Windows.Forms.MessageBox]::Show('Enter a folder path first.', 'Info', 'OK', 'Information') | Out-Null
        return
    }
    if (-not (Test-Path -LiteralPath $path)) {
        [System.Windows.Forms.MessageBox]::Show('Folder does not exist. Check the path and try again.', 'Error', 'OK', 'Error') | Out-Null
        return
    }
    if (Add-FolderRecord -Path $path) {
        Update-ListView
        Update-TrayMenu
        [System.Windows.Forms.MessageBox]::Show('Folder added.', 'Success', 'OK', 'Information') | Out-Null
    } else {
        [System.Windows.Forms.MessageBox]::Show('Folder already exists in the list.', 'Info', 'OK', 'Information') | Out-Null
    }
})

$btnRemove.Add_Click({
    Set-LastActivity
    $path = Get-ActivePath
    if (-not $path) {
        [System.Windows.Forms.MessageBox]::Show('Select or enter a folder to remove.', 'Info', 'OK', 'Information') | Out-Null
        return
    }
    $resp = [System.Windows.Forms.MessageBox]::Show("Remove this folder from the list?`r`n$path", 'Confirm', 'YesNo', 'Question')
    if ($resp -eq [System.Windows.Forms.DialogResult]::Yes) {
        if (Remove-FolderRecord -Path $path) {
                Update-ListView
            Update-TrayMenu
            [System.Windows.Forms.MessageBox]::Show('Folder removed.', 'Done', 'OK', 'Information') | Out-Null
        }
    }
})

$btnSetPassword.Add_Click({
    Set-LastActivity
    $path = Get-SelectedPath
    if (-not $path) {
        [System.Windows.Forms.MessageBox]::Show('Select a folder first.', 'Info', 'OK', 'Information') | Out-Null
        return
    }
    if (-not (Test-Path -LiteralPath $path)) {
        [System.Windows.Forms.MessageBox]::Show('Selected folder does not exist.', 'Error', 'OK', 'Error') | Out-Null
        return
    }
    $newPwdSecure = Show-SetPasswordDialog
    if ($newPwdSecure) {
        $record = New-PasswordRecord -Password $newPwdSecure
        # save password record into the folder record
        Update-Record -Path $path -PasswordRecord $record
        Write-Log "Password set for: $path"
        [System.Windows.Forms.MessageBox]::Show('Password saved.', 'Success', 'OK', 'Information') | Out-Null
    }
})

$btnHide.Add_Click({
    Set-LastActivity
    $path = Get-ActivePath
    if (-not $path) {
        [System.Windows.Forms.MessageBox]::Show('Select or enter a folder to hide.', 'Info', 'OK', 'Information') | Out-Null
        return
    }
    try {
        Set-Hidden -Path $path
        $rec = Get-FolderRecord -Path $path
        if (-not $rec) {
            [void](Add-FolderRecord -Path $path)
            $rec = Get-FolderRecord -Path $path
        }
        $aclApplied = $false
        $efsApplied = $false
        if ($rec -and (Should-ApplyAclRestriction -Record $rec)) { $aclApplied = Set-AclRestriction -Path $path }
        elseif ($rec -and $rec.AclRestricted) { Write-Log "ACL restriction skipped for '$path' (bypass user)." }
        if ($rec -and $rec.EfsEnabled)    { $efsApplied = Enable-EFS   -Path $path }
        Update-ListView
        Update-TrayMenu
        Write-Log "Hid folder: $path (ACL: $aclApplied, EFS: $efsApplied)"
        $msg = 'Folder hidden.'
        if ($aclApplied) { $msg += ' ACL applied.' }
        if ($efsApplied) { $msg += ' EFS encryption started.' }
        [System.Windows.Forms.MessageBox]::Show($msg, 'Done', 'OK', 'Information') | Out-Null
    } catch {
        [System.Windows.Forms.MessageBox]::Show("Error hiding folder:`r`n$($_.Exception.Message)", 'Error', 'OK', 'Error') | Out-Null
        Write-Log "Error hiding '$path': $($_.Exception.Message)"
    }
})

$btnUnhide.Add_Click({
    Set-LastActivity
    $path = Get-ActivePath
    if (-not $path) {
        [System.Windows.Forms.MessageBox]::Show('Select or enter a folder to unhide.', 'Info', 'OK', 'Information') | Out-Null
        return
    }
    $rec = Get-FolderRecord -Path $path
    if (-not $rec) {
        [void](Add-FolderRecord -Path $path)
        $rec = Get-FolderRecord -Path $path
    }
    if (-not $rec -or -not $rec.PasswordRecord) {
        [System.Windows.Forms.MessageBox]::Show('No password has been set for this folder.', 'Password required', 'OK', 'Information') | Out-Null
        return
    }
    $entered = $txtEnterPwd.Text
    if ([string]::IsNullOrWhiteSpace($entered)) {
        [System.Windows.Forms.MessageBox]::Show('Please enter the password to unhide.', 'Password required', 'OK', 'Information') | Out-Null
        return
    }
    $enteredSecure = Convert-PlainToSecureString -Plain $entered
    if (-not (Test-Password -Password $enteredSecure -PasswordRecord $rec.PasswordRecord)) {
        [System.Windows.Forms.MessageBox]::Show('Incorrect password.', 'Access denied', 'OK', 'Error') | Out-Null
        return
    }

    try {
        Clear-Hidden -Path $path
        $aclRestored = $true
        $efsRestored = $true
        if ($rec.AclRestricted -and (Should-ApplyAclRestriction -Record $rec)) { $aclRestored = Restore-ACL -Path $path }
        elseif ($rec.AclRestricted) { Write-Log "ACL restore skipped for '$path' (bypass user)." }
        if ($rec.EfsEnabled)    { $efsRestored = Disable-EFS -Path $path }
        $txtEnterPwd.Clear()
        Update-ListView
        Update-TrayMenu
        Write-Log "Unhid folder: $path (ACL removed: $aclRestored, EFS decrypted: $efsRestored)"
        $msg = 'Folder unhidden.'
        if ($rec.AclRestricted) {
            if ($aclRestored) { $msg += ' ACL removed.' } else { $msg += ' ACL removal failed.' }
        }
        if ($rec.EfsEnabled) {
            if ($efsRestored) { $msg += ' EFS decryption started.' } else { $msg += ' EFS decryption failed to start.' }
        }
        [System.Windows.Forms.MessageBox]::Show($msg, 'Done', 'OK', 'Information') | Out-Null
    } catch {
        [System.Windows.Forms.MessageBox]::Show("Error unhiding folder:`r`n$($_.Exception.Message)", 'Error', 'OK', 'Error') | Out-Null
        Write-Log "Error unhiding '$path': $($_.Exception.Message)"
    }
})

$btnOpenLog.Add_Click({
    Set-LastActivity
    Initialize-AppDir
    if (-not (Test-Path -LiteralPath $LogPath)) {
        New-Item -ItemType File -Path $LogPath | Out-Null
    }
    Write-Log "Log opened from UI."
    Start-Process notepad.exe $LogPath
})

$btnExport.Add_Click({
    Set-LastActivity
    $res = $saveDialog.ShowDialog()
    if ($res -eq [System.Windows.Forms.DialogResult]::OK) {
        try {
            $json = $Global:Config | ConvertTo-Json -Depth 6
            Set-Content -LiteralPath $saveDialog.FileName -Value $json -Encoding UTF8
            Write-Log "Exported config to: $($saveDialog.FileName)"
            [System.Windows.Forms.MessageBox]::Show('Configuration exported.', 'Success', 'OK', 'Information') | Out-Null
        } catch {
            [System.Windows.Forms.MessageBox]::Show("Export failed:`r`n$($_.Exception.Message)", 'Error', 'OK', 'Error') | Out-Null
            Write-Log "Export failed: $($_.Exception.Message)"
        }
    }
})

$btnImport.Add_Click({
    Set-LastActivity
    $res = $openDialog.ShowDialog()
    if ($res -eq [System.Windows.Forms.DialogResult]::OK) {
        try {
            $json = Get-Content -LiteralPath $openDialog.FileName -Raw
            $imported = $json | ConvertFrom-Json
            # Validate minimal schema
            if (-not $imported.PSObject.Properties.Name -contains 'Folders') {
                throw "Invalid config: missing 'Folders' array."
            }
            foreach ($recImp in $imported.Folders) {
                if (-not $recImp.PSObject.Properties.Name -contains 'FolderPath') {
                    throw "Invalid folder record: missing 'FolderPath'."
                }
                if (-not $recImp.PSObject.Properties.Name -contains 'AclRestricted') { $recImp | Add-Member -NotePropertyName AclRestricted -NotePropertyValue $false }
                if (-not $recImp.PSObject.Properties.Name -contains 'EfsEnabled')    { $recImp | Add-Member -NotePropertyName EfsEnabled -NotePropertyValue $false }
            }
            if (-not $imported.PSObject.Properties.Name -contains 'AutoHideEnabled') { $imported | Add-Member -NotePropertyName AutoHideEnabled -NotePropertyValue $false }
            if (-not $imported.PSObject.Properties.Name -contains 'AutoHideMinutes') { $imported | Add-Member -NotePropertyName AutoHideMinutes -NotePropertyValue 5 }

            # Ask merge or replace
            $resp = [System.Windows.Forms.MessageBox]::Show("Import config from:`r`n$($openDialog.FileName)`r`n`r`nChoose 'Yes' to MERGE, 'No' to REPLACE current config.", 'Merge or Replace', 'YesNoCancel', 'Question')
            if ($resp -eq [System.Windows.Forms.DialogResult]::Cancel) { return }
            if ($resp -eq [System.Windows.Forms.DialogResult]::No) {
                # Replace
                $Global:Config = $imported
                Save-Config -ConfigObject $Global:Config
                Write-Log "Imported config (replace) from: $($openDialog.FileName)"
            } else {
                # Merge
                $existingPaths = @($Global:Config.Folders | ForEach-Object { $_.FolderPath })
                foreach ($newRec in $imported.Folders) {
                    if (-not ($existingPaths -contains $newRec.FolderPath)) {
                        $Global:Config.Folders += $newRec
                    } else {
                        # Update toggles & password if present
                        $cur = Get-FolderRecord -Path $newRec.FolderPath
                        if ($newRec.PasswordRecord) { $cur.PasswordRecord = $newRec.PasswordRecord }
                        $cur.AclRestricted = [bool]$newRec.AclRestricted
                        $cur.EfsEnabled    = [bool]$newRec.EfsEnabled
                    }
                }
                # Merge auto-hide settings (import overrides current)
                $Global:Config.AutoHideEnabled = [bool]$imported.AutoHideEnabled
                $Global:Config.AutoHideMinutes = [int]$imported.AutoHideMinutes
                Save-Config -ConfigObject $Global:Config
                Write-Log "Imported config (merge) from: $($openDialog.FileName)"
            }
            Update-ListView
            Reset-UiFromConfig
            Update-TrayMenu
            [System.Windows.Forms.MessageBox]::Show('Configuration imported.', 'Success', 'OK', 'Information') | Out-Null
        } catch {
            [System.Windows.Forms.MessageBox]::Show("Import failed:`r`n$($_.Exception.Message)", 'Error', 'OK', 'Error') | Out-Null
            Write-Log "Import failed: $($_.Exception.Message)"
        }
    }
})

$btnScanHidden.Add_Click({
    Set-LastActivity
    $driveDialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $driveDialog.Description = 'Select a drive or root folder to scan for hidden managed folders'
    $driveDialog.ShowNewFolderButton = $false
    if ($driveDialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
    $root = $driveDialog.SelectedPath
    if (-not (Test-Path -LiteralPath $root)) {
        [System.Windows.Forms.MessageBox]::Show('Selected path does not exist.', 'Error', 'OK', 'Error') | Out-Null
        return
    }

    try {
        $found = @()
        $hiddenFlag = [IO.FileAttributes]::Hidden
        $systemFlag = [IO.FileAttributes]::System
        Get-ChildItem -LiteralPath $root -Directory -Force -Recurse -ErrorAction SilentlyContinue |
            Where-Object { ($_.Attributes -band $hiddenFlag) -and ($_.Attributes -band $systemFlag) } |
            ForEach-Object { $found += $_.FullName }

        $added = 0
        foreach ($p in $found) {
            if (-not (Get-FolderRecord -Path $p)) {
                if (Add-FolderRecord -Path $p) { $added++ }
            }
        }

        Update-ListView
        Update-TrayMenu
        $msg = "Scan complete. Found $($found.Count) hidden folder(s). Added $added new item(s) to the list."
        [System.Windows.Forms.MessageBox]::Show($msg, 'Scan Hidden', 'OK', 'Information') | Out-Null
    } catch {
        [System.Windows.Forms.MessageBox]::Show("Scan failed:`r`n$($_.Exception.Message)", 'Error', 'OK', 'Error') | Out-Null
        Write-Log "Hidden scan error: $($_.Exception.Message)"
    }
})

$lvFolders.add_SelectedIndexChanged({
    Set-LastActivity
    $path = Get-SelectedPath
    $script:SuppressPending = $true
    if ($path) {
        $rec = Get-FolderRecord -Path $path
        if ($rec) {
            $chkAcl.Checked = [bool]$rec.AclRestricted
            $chkEfs.Checked = [bool]$rec.EfsEnabled
        } else {
            $chkAcl.Checked = $false
            $chkEfs.Checked = $false
        }
    } else {
        $chkAcl.Checked = $false
        $chkEfs.Checked = $false
    }
    $script:SuppressPending = $false
    Update-StatusLabel -Count $lvFolders.Items.Count
})

$chkAcl.add_CheckedChanged({
    Set-LastActivity
    $path = Get-SelectedPath
    if ($script:SuppressPending) { return }
    if (-not $path) { return }
    Set-PendingState
    Write-Log "ACL preference for '$path' changed (pending): $($chkAcl.Checked)"
})

$chkEfs.add_CheckedChanged({
    Set-LastActivity
    $path = Get-SelectedPath
    if ($script:SuppressPending) { return }
    if (-not $path) { return }
    Set-PendingState
    Write-Log "EFS preference for '$path' changed (pending): $($chkEfs.Checked)"
})

# ---- FIXED: robust minutes conversion from NumericUpDown.Value (Decimal -> Int) ----
$chkAutoHide.Checked = [bool]$Global:Config.AutoHideEnabled
$chkAutoHide.add_CheckedChanged({
    Set-LastActivity
    if ($script:SuppressPending) { return }
    Set-PendingState
    $lblAutoStatus.Text = if ($chkAutoHide.Checked) { 'Auto-hide enabled (pending)' } else { 'Auto-hide status: Disabled (pending)' }
})

$nudAutoHide.add_ValueChanged({
    Set-LastActivity
    if ($script:SuppressPending) { return }
    Set-PendingState
    $lblAutoStatus.Text = "Auto-hide in: $([int]$nudAutoHide.Value) min (pending)"
})

$btnApply.Add_Click({
    Set-LastActivity
    $minutes = [int]$nudAutoHide.Value
    Update-AutoHide -Enabled $chkAutoHide.Checked -Minutes $minutes

    $path = Get-SelectedPath
    $folderLabel = if ($path) { $path } else { '(none selected)' }
    if ($path) {
        $rec = Get-FolderRecord -Path $path
        if ($rec) {
            Update-Record -Path $path -AclRestricted $chkAcl.Checked -EfsEnabled $chkEfs.Checked | Out-Null
        }
    }

    $lblAutoStatus.Text = if ($chkAutoHide.Checked) { 'Auto-hide enabled' } else { 'Auto-hide status: Disabled' }
    Write-Log "Apply clicked: AutoHide=$($chkAutoHide.Checked); Minutes=$minutes; Folder=$folderLabel; ACL=$($chkAcl.Checked); EFS=$($chkEfs.Checked)"
    Set-PendingState -State:$false
    Update-ListView
    Update-TrayMenu
    [System.Windows.Forms.MessageBox]::Show('Changes applied.', 'Success', 'OK', 'Information') | Out-Null
})

$btnReset.Add_Click({
    Set-LastActivity
    Reset-UiFromConfig
    Update-ListView
    Update-TrayMenu
    [System.Windows.Forms.MessageBox]::Show('Reset to last saved settings.', 'Reset', 'OK', 'Information') | Out-Null
})

# Initial populate & tray
Update-ListView
Reset-UiFromConfig
Update-TrayMenu

# Close to tray behavior
$form.add_FormClosing({
    param($senderObj, $e)
    if ($form.Visible -and $e.CloseReason -eq [System.Windows.Forms.CloseReason]::UserClosing) {
        $e.Cancel = $true
        $form.Hide()
        $notifyIcon.ShowBalloonTip(2000, 'Folder Hider', 'Still running in the tray.', [System.Windows.Forms.ToolTipIcon]::Info)
    } else {
        $notifyIcon.Visible = $false
    }
})

$notifyIcon.add_DoubleClick({ $form.Show(); $form.WindowState = 'Normal'; $form.Activate(); Set-LastActivity })

# Start
Write-Log "App started."
[void]$form.ShowDialog()

