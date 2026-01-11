# SystemCleaner.ps1
# Combines Recent Cleaner with Folder Hider features in a single WPF interface.

# --- STA guard: only relaunch when running as .ps1 (skip when packaged as EXE) ---
$scriptPath = $MyInvocation.MyCommand.Path
$runningAsScript = ($scriptPath -and $scriptPath.EndsWith('.ps1', [System.StringComparison]::OrdinalIgnoreCase))
if ($runningAsScript) {
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

# Elevation guard: relaunch elevated if not running as admin (only when invoked as script)
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($runningAsScript -and -not $isAdmin) {
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
Add-Type -AssemblyName System.Windows.Forms

function Ensure-Directory($path) {
    if (-not $path) { return }
    if (-not (Test-Path $path)) {
        try { New-Item -Path $path -ItemType Directory -Force | Out-Null } catch {}
    }
}

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
$HiderAppDir    = Join-Path $env:APPDATA 'SystemCleaner'
$HiderConfigPath= Join-Path $HiderAppDir 'FolderHiderConfig.json'
$HiderLogPath   = Join-Path $HiderAppDir 'FolderHider.log'
$Global:HiderConfig = $null
$Global:HiderLastActivity = Get-Date

function Ensure-HiderPaths {
    if (-not (Test-Path -LiteralPath $HiderAppDir)) {
        New-Item -ItemType Directory -Path $HiderAppDir -Force | Out-Null
    }
}

function Write-HiderLog {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Message)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Message' 
    Ensure-HiderPaths
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[$ts] $Message"
    try { Add-Content -LiteralPath $HiderLogPath -Value $line -Encoding UTF8 } catch {}
}

function Get-DefaultHiderConfig {
    return [PSCustomObject]@{
        Folders         = @()
        AutoHideEnabled = $false
        AutoHideMinutes = 5
    }
}

function Save-HiderConfig {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][object]$ConfigObject)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Plain)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Plain' 
    $ss = New-Object System.Security.SecureString
    foreach ($c in $Plain.ToCharArray()) { $ss.AppendChar($c) }
    $ss.MakeReadOnly(); return $ss
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try {
        if (-not (Test-Path -LiteralPath $Path)) { Write-HiderLog "Set-Hidden: Path missing '$Path'"; return }
        $attrs = [System.IO.File]::GetAttributes($Path)
        $attrs = $attrs -bor [IO.FileAttributes]::Hidden
        $attrs = $attrs -bor [IO.FileAttributes]::System
        [System.IO.File]::SetAttributes($Path, $attrs)
        return $true
    } catch { Write-HiderLog "Set-Hidden error on '$Path': $($_.Exception.Message)"; return $false }
}

function Clear-Hidden {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
            try { $isEveryone = ($r.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $sidEveryone.Value) } catch {}
            if ($isEveryone -and $r.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Deny) { $rulesToRemove += $r }
        }
        foreach ($r in $rulesToRemove) { [void]$acl.RemoveAccessRuleSpecific($r) }
        Set-Acl -LiteralPath $Path -AclObject $acl -ErrorAction Stop

        $hasDeny = {
            param($p, $sidObj)
            $checkAcl = Get-Acl -LiteralPath $p -ErrorAction Stop
            foreach ($rule in $checkAcl.Access) {
                $isEveryone = $false
                try { $isEveryone = ($rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $sidObj.Value) } catch {}
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    try { return ((New-Object System.IO.DriveInfo([System.IO.Path]::GetPathRoot($Path))).DriveFormat -eq 'NTFS') } catch { return $false }
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
    $out = $proc.StandardOutput.ReadToEnd(); $err = $proc.StandardError.ReadToEnd(); $proc.WaitForExit()
    return @{ ExitCode = $proc.ExitCode; StdOut = $out; StdErr = $err }
}

function Enable-EFS {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
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
        try { return Get-LocalUser } catch {}
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
    $results = @()
    $users = Get-LocalUsersSafe

    $elevatedGroups = @('Administrators','Backup Operators','Power Users')
    $groupMap = @{}
    foreach ($g in $elevatedGroups) { $groupMap[$g] = @(Get-LocalGroupMembersSafe -GroupName $g) }

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

        foreach ($folder in $sensitiveFolders) {
            if (Test-AclElevation -Path $folder -UserIdentities $idList) { $reasons += "Write/Modify on $folder" }
        }

        if (Test-RegistryElevation -KeyPath 'HKLM:\SOFTWARE' -UserIdentities $idList) { $reasons += 'Elevated rights on HKLM\SOFTWARE' }
        if (Test-RegistryElevation -KeyPath 'HKLM:\SYSTEM' -UserIdentities $idList) { $reasons += 'Elevated rights on HKLM\SYSTEM' }

        if ($reasons.Count -gt 0) {
            $results += [pscustomobject]@{
                User = $name
                Reasons = ($reasons -join '; ')
                Enabled = if ($u.PSObject.Properties.Name -contains 'Enabled') { [bool]$u.Enabled } else { $true }
            }
        }
    }
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
        [Parameter(Mandatory)][string]$Propagation
    [switch]$NonInteractive
    )
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

function Show-OutboundWindow {
    $data = Get-SuspiciousOutbound
    $win = New-Object System.Windows.Window
    $win.Title = 'Outbound Connections'
    $win.Height = 480; $win.Width = 820
    $win.WindowStartupLocation = 'CenterOwner'

    $root = New-Object System.Windows.Controls.DockPanel
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

    $grid.ItemsSource = $data

    $refresh = {
        param($g)
        $g.ItemsSource = Get-SuspiciousOutbound
    }

    $refreshBtn.Add_Click({ & $refresh $grid; Set-SecStatus "Outbound scan refreshed" })

    $killBtn.Add_Click({
        $sel = $grid.SelectedItem
        if (-not $sel) { [System.Windows.MessageBox]::Show('Select a connection first.'); return }
        if (-not $sel.PID) { [System.Windows.MessageBox]::Show('PID missing; cannot kill connection.'); return }
        $resp = [System.Windows.MessageBox]::Show("Kill process $($sel.Process) (PID $($sel.PID)) to drop this connection?",'Confirm','YesNo','Warning')
        if ($resp -ne [System.Windows.MessageBoxResult]::Yes) { return }
        try {
            Stop-Process -Id $sel.PID -Force -ErrorAction Stop
            Set-SecStatus "Killed PID $($sel.PID) and connection"
            & $refresh $grid
        } catch {
            [System.Windows.MessageBox]::Show("Failed to kill PID $($sel.PID): $($_.Exception.Message)")
        }
    })

    $root.Children.Add($btnPanel) | Out-Null
    [System.Windows.Controls.DockPanel]::SetDock($btnPanel, 'Top')
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
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Title="SystemCleaner" Height="760" Width="1100" MinWidth="900" MinHeight="560" WindowStartupLocation="CenterScreen">
    <Window.Resources>
        <Style TargetType="Button">
            <Setter Property="Background" Value="#FF2B579A"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="Padding" Value="6,4"/>
            <Setter Property="Margin" Value="4"/>
            <Setter Property="BorderBrush" Value="#FF1F446E"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
        </Style>
        <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#222"/></Style>
        <LinearGradientBrush x:Key="HeaderBrush" StartPoint="0,0" EndPoint="1,0">
            <GradientStop Color="#FF4A90E2" Offset="0"/>
            <GradientStop Color="#FF50C878" Offset="1"/>
        </LinearGradientBrush>
    </Window.Resources>

    <DockPanel LastChildFill="True">
        <Border DockPanel.Dock="Top" Background="{StaticResource HeaderBrush}" Padding="12" CornerRadius="6" Margin="8">
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <Image Name="AppLogo" Width="28" Height="28" Stretch="Uniform" Margin="0,0,8,0"/>
                <TextBlock Text="SystemCleaner — Recent shortcuts and folder hiding" FontSize="16" FontWeight="Bold" Foreground="White"/>
            </StackPanel>
        </Border>

        <StatusBar DockPanel.Dock="Bottom" Margin="8">
            <StatusBarItem>
                <TextBlock Name="StatusText" Text="Ready" />
            </StatusBarItem>
            <StatusBarItem HorizontalAlignment="Right">
                <StackPanel Orientation="Horizontal">
                    <Button Name="ShowPreview" Content="Show Preview" Width="120"/>
                    <Button Name="ExportCSV" Content="Export Preview" Width="130"/>
                    <Button Name="UndoLast" Content="Undo Last" Width="110"/>
                    <Button Name="RunNow" Content="Run Now" Width="110"/>
                    <Button Name="SaveTask" Content="Schedule Task" Width="140"/>
                    <Button Name="OpenLusrmgr" Content="Local Users &amp; Groups" Width="170"/>
                    <Button Name="ExitApp" Content="Exit" Width="90"/>
                </StackPanel>
            </StatusBarItem>
        </StatusBar>

        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" Margin="0" DockPanel.Dock="Bottom">
            <TabControl Name="MainTabs" Margin="8">
            <TabItem Header="Recent Cleaner">
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
                        <DataGrid Name="PreviewGrid" AutoGenerateColumns="True" IsReadOnly="True" Margin="0" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF"/>
                    </Grid>
                </Grid>
            </TabItem>

            <TabItem Header="Folder Hider">
                <Grid Margin="4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="12"/>
                        <ColumnDefinition Width="420"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>

                    <GroupBox Header="Managed Folders" Grid.Column="0" Grid.Row="0" Margin="0,0,8,0">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="*"/>
                            </Grid.RowDefinitions>

                            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                <Button Name="HAddDir" Content="Add Folder..." Width="110"/>
                                <Button Name="HAddManual" Content="Add From Path" Width="120"/>
                                <Button Name="HRemoveDir" Content="Remove" Width="90"/>
                            </StackPanel>

                            <Grid Grid.Row="1">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto"/>
                                    <RowDefinition Height="*"/>
                                    <RowDefinition Height="Auto"/>
                                </Grid.RowDefinitions>

                                <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                                    <TextBlock Text="Manual path:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                                    <TextBox Name="HManualPath" MinWidth="300" />
                                </StackPanel>

                                <ListView Name="HiderList" Grid.Row="1" Margin="0,0,0,6" VerticalAlignment="Stretch">
                                    <ListView.View>
                                        <GridView>
                                            <GridViewColumn Header="Folder" Width="300" DisplayMemberBinding="{Binding Path}"/>
                                            <GridViewColumn Header="Status" Width="80" DisplayMemberBinding="{Binding Status}"/>
                                            <GridViewColumn Header="ACL" Width="60" DisplayMemberBinding="{Binding ACL}"/>
                                            <GridViewColumn Header="EFS" Width="60" DisplayMemberBinding="{Binding EFS}"/>
                                        </GridView>
                                    </ListView.View>
                                </ListView>

                                <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Left" Margin="0,4,0,0">
                                    <Button Name="HHideBtn" Content="Hide" Width="80"/>
                                    <Button Name="HUnhideBtn" Content="Unhide" Width="90"/>
                                    <Button Name="HSetPwdBtn" Content="Set Password" Width="120"/>
                                    <Button Name="HOpenLogBtn" Content="Open Log" Width="100"/>
                                    <Button Name="HScanHiddenBtn" Content="Scan Hidden" Width="120"/>
                                </StackPanel>
                            </Grid>
                        </Grid>
                    </GroupBox>

                    <StackPanel Grid.Column="2" Grid.Row="0" Margin="0">
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

            <TabItem Header="Security Scan">
                <Grid Margin="4">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>

                    <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                        <Button Name="SecScanBtn" Content="Scan for Elevated Users" Width="190"/>
                        <Button Name="SecDisableBtn" Content="Disable User" Width="120" Margin="6,0,0,0"/>
                        <Button Name="SecResetBtn" Content="Reset Password" Width="130" Margin="6,0,0,0"/>
                        <Button Name="SecDeleteBtn" Content="Delete User" Width="110" Margin="6,0,0,0"/>
                        <Button Name="SecOpenLusr" Content="Open Users &amp; Groups" Width="170" Margin="6,0,0,0"/>
                        <Button Name="SecAclBtn" Content="Scan Critical ACLs" Width="160" Margin="6,0,0,0"/>
                        <Button Name="SecOutboundBtn" Content="Scan Outbound Traffic" Width="180" Margin="6,0,0,0"/>
                        <StackPanel Orientation="Horizontal" Margin="12,0,0,0" VerticalAlignment="Center">
                            <TextBlock Text="New password:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                            <PasswordBox Name="SecPasswordBox" Width="160"/>
                        </StackPanel>
                    </StackPanel>

                    <DataGrid Name="SecGrid" Grid.Row="1" AutoGenerateColumns="True" IsReadOnly="True" Margin="0" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF"/>

                    <TextBlock Name="SecStatus" Grid.Row="2" Text="Ready" Foreground="#444" Margin="0,8,0,0" TextWrapping="Wrap"/>
                </Grid>
            </TabItem>
        </TabControl>
        </ScrollViewer>
    </DockPanel>
</Window>
"@

# --- Load window and validate controls ---
try {
    $reader = New-Object System.Xml.XmlNodeReader $xaml
    $window = [Windows.Markup.XamlReader]::Load($reader)
} catch {
    Write-Error "Failed to load XAML: $_"
    return
}

$required = @(
    "MainTabs","AppLogo",
    "DirList","AddDir","RemoveDir","IncludeSubdirs","DryRunToggle","OutputPathBox","BrowseOutput",
    "FrequencyBox","TimeBox","DaysBox","WeekdaysPanel","chkSunday","chkMonday","chkTuesday","chkWednesday","chkThursday","chkFriday","chkSaturday",
    "PreviewGrid","ShowPreview","ExportCSV","UndoLast","RunNow","SaveTask","OpenLusrmgr","ExitApp","StatusText",
    "HiderList","HAddDir","HAddManual","HRemoveDir","HHideBtn","HUnhideBtn","HSetPwdBtn","HOpenLogBtn","HScanHiddenBtn",
    "HManualPath","HAclToggle","HEfsToggle","HAutoHideToggle","HAutoMinutesBox","HApplyBtn","HiderStatus",
    "SecScanBtn","SecDisableBtn","SecResetBtn","SecDeleteBtn","SecOpenLusr","SecAclBtn","SecOutboundBtn","SecPasswordBox","SecGrid","SecStatus"
)
$controls = @{}
foreach ($name in $required) {
    $ctrl = $window.FindName($name)
    if (-not $ctrl) { [System.Windows.MessageBox]::Show("Missing control in XAML: $name"); return }
    $controls[$name] = $ctrl
}

# --- Aliases ---
$dirList    = $controls["DirList"]
$addDir     = $controls["AddDir"]
$removeDir  = $controls["RemoveDir"]
$includeSub = $controls["IncludeSubdirs"]
$dryRunBox  = $controls["DryRunToggle"]
$outputBox  = $controls["OutputPathBox"]
$browseOut  = $controls["BrowseOutput"]
$frequency  = $controls["FrequencyBox"]
$timeBox    = $controls["TimeBox"]
$daysBox    = $controls["DaysBox"]
$chkSun     = $controls["chkSunday"]; $chkMon = $controls["chkMonday"]; $chkTue = $controls["chkTuesday"]
$chkWed     = $controls["chkWednesday"]; $chkThu = $controls["chkThursday"]; $chkFri = $controls["chkFriday"]; $chkSat = $controls["chkSaturday"]
$previewGrid= $controls["PreviewGrid"]
$showPreview= $controls["ShowPreview"]
$exportCSV  = $controls["ExportCSV"]
$undoLast   = $controls["UndoLast"]
$runNow     = $controls["RunNow"]
$saveTask   = $controls["SaveTask"]
$openLusr   = $controls["OpenLusrmgr"]
$exitApp    = $controls["ExitApp"]
$statusText = $controls["StatusText"]
$mainTabs   = $controls["MainTabs"]
$appLogo    = $controls["AppLogo"]

$hiderList  = $controls["HiderList"]
$hAdd       = $controls["HAddDir"]
$hAddManual = $controls["HAddManual"]
$hRemove    = $controls["HRemoveDir"]
$hHide      = $controls["HHideBtn"]
$hUnhide    = $controls["HUnhideBtn"]
$hSetPwd    = $controls["HSetPwdBtn"]
$hOpenLog   = $controls["HOpenLogBtn"]
$hScanHidden= $controls["HScanHiddenBtn"]
$hManualBox = $controls["HManualPath"]
$hAclToggle = $controls["HAclToggle"]
$hEfsToggle = $controls["HEfsToggle"]
$hAutoHide  = $controls["HAutoHideToggle"]
$hAutoMinutes = $controls["HAutoMinutesBox"]
$hApply     = $controls["HApplyBtn"]
$hStatusLbl = $controls["HiderStatus"]

$secScanBtn   = $controls["SecScanBtn"]
$secDisableBtn= $controls["SecDisableBtn"]
$secResetBtn  = $controls["SecResetBtn"]
$secDeleteBtn = $controls["SecDeleteBtn"]
$secOpenLusr  = $controls["SecOpenLusr"]
$secAclBtn    = $controls["SecAclBtn"]
$secOutbound  = $controls["SecOutboundBtn"]
$secPwdBox    = $controls["SecPasswordBox"]
$secGrid      = $controls["SecGrid"]
$secStatus    = $controls["SecStatus"]
$script:secStatus = $secStatus

# Load and apply app icon for window and header
$appBasePath = if ($PSScriptRoot) { $PSScriptRoot } elseif ($scriptPath) { Split-Path -Path $scriptPath -Parent } else { Split-Path -Path ([System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName) -Parent }
$iconCandidates = @(
    (Join-Path $appBasePath 'platypus.png')
    (Join-Path $appBasePath 'platypus.ico')
    (Join-Path $appBasePath 'Assets\platypus.png')
    (Join-Path $appBasePath 'Assets\platypus.ico')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

if ($iconCandidates.Count -gt 0) {
    $iconPath = $iconCandidates[0]
    try {
        $bmp = New-Object System.Windows.Media.Imaging.BitmapImage
        $bmp.BeginInit(); $bmp.CacheOption = 'OnLoad'; $bmp.UriSource = New-Object System.Uri($iconPath); $bmp.DecodePixelWidth = 64; $bmp.EndInit(); $bmp.Freeze()
        $window.Icon = $bmp
        if ($appLogo) { $appLogo.Source = $bmp }
    } catch {
        Write-Verbose "Icon load failed: $($_.Exception.Message)"
    }
}

# --- Config init (safe) ---
$configPath = Join-Path $env:APPDATA "RecentCleanerConfig.json"
$defaultOut = Join-Path $env:LOCALAPPDATA "RecentCleaner"
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

# --- Populate UI ---
foreach ($d in $config.ExcludedDirs) { if ($d) { $dirList.Items.Add($d) } }
$outputBox.Text = if ($config.OutputPath) { $config.OutputPath } else { $defaultOut }
$timeBox.Text = if ($config.Time) { $config.Time } else { "00:00" }
$includeSub.IsChecked = [bool]$config.IncludeSubdirs

foreach ($wd in $config.Weekdays) {
    switch ($wd.ToLower()) {
        "sunday"   { $chkSun.IsChecked = $true }
        "monday"   { $chkMon.IsChecked = $true }
        "tuesday"  { $chkTue.IsChecked = $true }
        "wednesday"{ $chkWed.IsChecked = $true }
        "thursday" { $chkThu.IsChecked = $true }
        "friday"   { $chkFri.IsChecked = $true }
        "saturday" { $chkSat.IsChecked = $true }
    }
}

# --- Hider config init ---
$Global:HiderConfig = Get-HiderConfig
Refresh-HiderList -ListControl $hiderList
$hAutoHide.IsChecked   = [bool]$Global:HiderConfig.AutoHideEnabled
$hAutoMinutes.Text     = [string]$Global:HiderConfig.AutoHideMinutes
$hStatusLbl.Text       = "Folders: $($Global:HiderConfig.Folders.Count)"
Set-SecStatus "Ready"

# --- Logging helper ---
function Log-Error($message, $outputPath) {
    $pathToUse = if ($outputPath) { $outputPath } elseif ($config.OutputPath) { $config.OutputPath } else { $defaultOut }
    Ensure-Directory $pathToUse
    $logPath = Join-Path $pathToUse "RecentCleaner_ErrorLog_$(Get-Date -Format 'yyyyMMdd').log"
    try { Add-Content -Path $logPath -Value "$(Get-Date -Format 'u') ERROR: $message" } catch {}
}

function Set-Status($text, $durationMs=1000) {
    try {
        $statusText.Text = $text
        if ($durationMs -gt 0) {
            $timer = New-Object System.Windows.Threading.DispatcherTimer
            $timer.Interval = [TimeSpan]::FromMilliseconds($durationMs)
            $timer.Add_Tick({
                $statusText.Text = "Ready"
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
    try { if (-not $shellObj.Value) { $shellObj.Value = New-Object -ComObject WScript.Shell }; return $shellObj.Value.CreateShortcut($lnkPath).TargetPath } catch { return $null }
}

function Remove-RecentShortcuts {
    param (
        [string[]]$TargetDirs,
        [switch]$DryRun,
        [string]$BackupPath,
        [bool]$IncludeSubDirs = $true
    )

    $recent = [Environment]::GetFolderPath("Recent")
    try { $shortcuts = Get-ChildItem -Path $recent -Filter *.lnk -Force -ErrorAction SilentlyContinue } catch { $shortcuts = @() }
    $matched = New-Object System.Collections.Generic.List[object]
    $shellRef = [ref] $null

    foreach ($lnk in $shortcuts) {
        try {
            $target = Resolve-LnkTarget $lnk.FullName ([ref]$shellRef)
            if (-not $target) { continue }
            foreach ($dir in $TargetDirs) {
                if (-not $dir) { continue }
                if ($IncludeSubDirs) {
                    $isMatch = $target.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)
                } else {
                    $parent = Split-Path -Path $target -Parent
                    $isMatch = ($parent -and ([string]::Equals($parent, $dir, [System.StringComparison]::OrdinalIgnoreCase)))
                }
                if ($isMatch) {
                    $matched.Add([PSCustomObject]@{ Shortcut = $lnk.FullName; Target = $target })
                    if (-not $DryRun) {
                        if ($BackupPath) { Ensure-Directory $BackupPath; Copy-Item -Path $lnk.FullName -Destination $BackupPath -Force -ErrorAction SilentlyContinue }
                        Remove-Item -Path $lnk.FullName -Force -ErrorAction SilentlyContinue
                    }
                    break
                }
            }
        } catch { Log-Error "Failed processing $($lnk.FullName): $_" $config.OutputPath }
    }

    return $matched
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
        })
    } catch {
        try {
            $frequency.add_SelectionChanged({
                try {
                    $sel = $frequency.SelectedItem
                    if ($sel -and $sel.Content -eq 'Monthly') { $daysBox.Visibility = 'Visible' } else { $daysBox.Visibility = 'Collapsed' }
                } catch { Log-Error "Frequency fallback handler error: $_" $outputBox.Text }
            })
        } catch { Write-Warning "Failed to attach Frequency selection handler: $_" }
    }
}

# --- UI handlers ---
$addDir.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($dlg.ShowDialog() -eq 'OK') { if (-not $dirList.Items.Contains($dlg.SelectedPath)) { $dirList.Items.Add($dlg.SelectedPath); Save-Config } }
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
        $statusText.Text = "Previewing..."
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
        $statusText.Text = "Running..."
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

$openLusr.Add_Click({
    try {
        Start-Process -FilePath "mmc.exe" -ArgumentList "lusrmgr.msc"
    } catch {
        Set-Status "Unable to open Local Users & Groups" 1800
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
    $secGrid.ItemsSource = $null
    [System.Threading.Tasks.Task]::Run([Action]{
        $res = $null; $err = $null
        try { $res = Run-SecurityScan } catch { $err = $_ }
        $window.Dispatcher.Invoke([Action]{
            if ($err) { Set-SecStatus "Scan failed: $($err.Exception.Message)"; return }
            $secGrid.ItemsSource = $res
            Set-SecStatus "Scan complete. Found $($res.Count) elevated user(s)."
        })
    }) | Out-Null
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
    if (Disable-LocalUserSafe -User $u) { Set-SecStatus "Disabled $u"; $secScanBtn.RaiseEvent([System.Windows.RoutedEventArgs][System.Windows.Controls.Primitives.ButtonBase]::ClickEvent) }
    else { Set-SecStatus "Failed to disable $u" }
})

$secDeleteBtn.Add_Click({
    $u = Get-SecSelectedUser
    if (-not $u) { Set-SecStatus "Select a user first."; return }
    $resp = [System.Windows.MessageBox]::Show("Delete user $u?","Confirm",'YesNo','Question')
    if ($resp -ne [System.Windows.MessageBoxResult]::Yes) { return }
    if (Remove-LocalUserSafe -User $u) { Set-SecStatus "Deleted $u"; $secScanBtn.RaiseEvent([System.Windows.RoutedEventArgs][System.Windows.Controls.Primitives.ButtonBase]::ClickEvent) }
    else { Set-SecStatus "Failed to delete $u" }
})

$secAclBtn.Add_Click({
    try { Show-CriticalAclWindow } catch { Set-SecStatus "ACL scan failed: $($_.Exception.Message)" }
})

$secOutbound.Add_Click({
    try { Show-OutboundWindow } catch { Set-SecStatus "Outbound scan failed: $($_.Exception.Message)" }
})

$secOpenLusr.Add_Click({
    try {
        Start-Process -FilePath "mmc.exe" -ArgumentList "lusrmgr.msc"
        Set-SecStatus "Opened Local Users & Groups"
    } catch {
        Set-SecStatus "Unable to open Local Users & Groups"
    }
})

$secResetBtn.Add_Click({
    $u = Get-SecSelectedUser
    if (-not $u) { Set-SecStatus "Select a user first."; return }
    $pwdPlain = $secPwdBox.Password
    if ([string]::IsNullOrWhiteSpace($pwdPlain)) { Set-SecStatus "Enter a new password first."; return }
    $pwd = Convert-PlainToSecureString -Plain $pwdPlain
    if (Reset-LocalUserPasswordSafe -User $u -Password $pwd) { Set-SecStatus "Password reset for $u" }
    else { Set-SecStatus "Failed to reset password for $u" }
})

$exitApp.Add_Click({ try { $window.Close() } catch {} })

# --- Show UI ---
try {
    $window.ShowDialog() | Out-Null
} catch {
    Log-Error "UI failed to show: $_" $outputBox.Text
    [System.Windows.MessageBox]::Show("UI failed to open. See error log in output folder.")
}
