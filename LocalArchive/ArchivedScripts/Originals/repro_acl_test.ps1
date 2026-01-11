function Set-Hidden {
    param([Parameter(Mandatory)][string]$Path, [switch]$NonInteractive)
    . "$PSScriptRoot\Tools\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' $Path
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    $attrs = $item.Attributes
    $attrs = $attrs -bor [IO.FileAttributes]::Hidden
    $attrs = $attrs -bor [IO.FileAttributes]::System
    $item.Attributes = $attrs
}

function Set-AclRestriction {
    param([Parameter(Mandatory)][string]$Path, [switch]$NonInteractive)
    . "$PSScriptRoot\Tools\NonInteractive.ps1"
    Set-NonInteractive -Enable:$NonInteractive
    Require-Parameter 'Path' $Path
    try {
        $acl = Get-Acl -LiteralPath $Path
        $sidEveryone = New-Object System.Security.Principal.SecurityIdentifier('S-1-1-0')
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $sidEveryone,
            [System.Security.AccessControl.FileSystemRights]::ReadAndExecute,
            [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit,
            [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Deny
        )
        $acl.AddAccessRule($rule) | Out-Null
        Set-Acl -LiteralPath $Path -AclObject $acl
        return $true
    } catch {
        Write-Output "Set-AclRestriction error on '$Path': $($_.Exception.Message)"
        return $false
    }
}

$temp = Join-Path $env:TEMP ('fh_repro_' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
Write-Output "Temp: $temp"
Set-Hidden -Path $temp; Write-Output "After Set-Hidden: $((Get-Item -LiteralPath $temp).Attributes)"
$ok = Set-AclRestriction -Path $temp; Write-Output "Set-AclRestriction returned: $ok"
Remove-Item -LiteralPath $temp -Recurse -Force
