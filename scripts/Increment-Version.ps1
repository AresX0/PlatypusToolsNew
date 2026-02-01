<#
.SYNOPSIS
    Increments the version in all version files.
    
.DESCRIPTION
    This script finds all version references in the project and increments
    the version using a 4-part scheme: Major.Minor.Build.Revision
    
    Minor increment: Adds 1 to Revision, with overflow at 10
    - 3.3.0.9 -> 3.3.0.10 -> 3.3.1.0 (revision 10 overflows to build)
    - 3.3.9.10 -> 3.3.10.0 -> 3.4.0.0 (build 10 overflows to minor)
    - 3.9.10.0 -> 3.10.0.0 -> 4.0.0.0 (minor 10 overflows to major)
    
    Major increment: Adds 5 to Minor, with overflow at 10
    - 3.3.0.0 -> 3.8.0.0
    - 3.8.0.0 -> 4.3.0.0 (8+5=13, overflow: major+1, minor=3)
    
.PARAMETER Major
    If specified, performs a major version increment (+5 to minor).
    
.PARAMETER WhatIf
    Shows what changes would be made without actually making them.
    
.EXAMPLE
    .\Increment-Version.ps1
    Increments revision (minor change) in all project files.
    
.EXAMPLE
    .\Increment-Version.ps1 -Major
    Increments by 5 (major change) in all project files.
    
.EXAMPLE
    .\Increment-Version.ps1 -WhatIf
    Shows what changes would be made without applying them.
#>

param(
    [switch]$Major,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

# Files containing version information - support both 3-part and 4-part versions
$VersionFiles = @(
    @{ Path = "PlatypusTools.UI\PlatypusTools.UI.csproj"; Pattern = '<Version>(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?</Version>'; Template = '<Version>{0}</Version>' },
    @{ Path = "PlatypusTools.Core\PlatypusTools.Core.csproj"; Pattern = '<Version>(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?</Version>'; Template = '<Version>{0}</Version>' },
    @{ Path = "PlatypusTools.Installer\Product.wxs"; Pattern = 'Version="(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?"'; Template = 'Version="{0}"' },
    @{ Path = "PlatypusTools.Installer\Files.wxs"; Pattern = 'Value="(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?"'; Template = 'Value="{0}"' },
    @{ Path = "PlatypusTools.UI\Views\SettingsWindow.xaml"; Pattern = 'Text="Version: (\d+)\.(\d+)\.(\d+)(?:\.(\d+))?"'; Template = 'Text="Version: {0}"' },
    @{ Path = "PlatypusTools.UI\Views\AboutWindow.xaml"; Pattern = 'Text="(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?"'; Template = 'Text="{0}"' }
)

function Increment-MinorVersion {
    param([int]$Maj, [int]$Min, [int]$Bld, [int]$Rev)
    
    $Rev++
    
    # Overflow logic: any octet > 10 carries over
    if ($Rev -gt 10) {
        $Rev = 0
        $Bld++
    }
    if ($Bld -gt 10) {
        $Bld = 0
        $Min++
    }
    if ($Min -gt 10) {
        $Min = 0
        $Maj++
    }
    
    return @($Maj, $Min, $Bld, $Rev)
}

function Increment-MajorVersion {
    param([int]$Maj, [int]$Min, [int]$Bld, [int]$Rev)
    
    $Min += 5
    $Bld = 0
    $Rev = 0
    
    # Overflow logic
    if ($Min -gt 10) {
        $Maj++
        $Min = $Min - 10
    }
    
    return @($Maj, $Min, $Bld, $Rev)
}

# Get current version from the UI csproj
$UICsprojPath = Join-Path $ProjectRoot "PlatypusTools.UI\PlatypusTools.UI.csproj"
$UICsprojContent = Get-Content $UICsprojPath -Raw

if ($UICsprojContent -match '<Version>(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?</Version>') {
    $MajorNum = [int]$Matches[1]
    $MinorNum = [int]$Matches[2]
    $BuildNum = [int]$Matches[3]
    $RevisionNum = if ($Matches[4]) { [int]$Matches[4] } else { 0 }
    
    $CurrentVersion = "$MajorNum.$MinorNum.$BuildNum.$RevisionNum"
    
    if ($Major) {
        $NewParts = Increment-MajorVersion -Maj $MajorNum -Min $MinorNum -Bld $BuildNum -Rev $RevisionNum
        Write-Host "Performing MAJOR version increment (+5 to minor)" -ForegroundColor Magenta
    } else {
        $NewParts = Increment-MinorVersion -Maj $MajorNum -Min $MinorNum -Bld $BuildNum -Rev $RevisionNum
        Write-Host "Performing minor version increment (+1 to revision)" -ForegroundColor Cyan
    }
    
    $NewVersion = "$($NewParts[0]).$($NewParts[1]).$($NewParts[2]).$($NewParts[3])"
    
    Write-Host ""
    Write-Host "Current Version: $CurrentVersion"
    Write-Host "New Version:     $NewVersion"
    Write-Host ""
    
    if ($WhatIf) {
        Write-Host "WhatIf: Would update the following files:" -ForegroundColor Yellow
    }
    
    foreach ($FileInfo in $VersionFiles) {
        $FilePath = Join-Path $ProjectRoot $FileInfo.Path
        
        if (Test-Path $FilePath) {
            $Content = Get-Content $FilePath -Raw
            
            if ($Content -match $FileInfo.Pattern) {
                $OldMatch = $Matches[0]
                $NewMatch = $FileInfo.Template -f $NewVersion
                
                if ($WhatIf) {
                    Write-Host "  $($FileInfo.Path)"
                    Write-Host "    Old: $OldMatch"
                    Write-Host "    New: $NewMatch"
                } else {
                    $NewContent = $Content -replace [regex]::Escape($OldMatch), $NewMatch
                    Set-Content -Path $FilePath -Value $NewContent -NoNewline
                    Write-Host "Updated: $($FileInfo.Path)" -ForegroundColor Green
                }
            } else {
                Write-Host "Pattern not found in: $($FileInfo.Path)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "File not found: $($FileInfo.Path)" -ForegroundColor Red
        }
    }
    
    # Update HTML help file (has multiple occurrences)
    $HelpFilePath = Join-Path $ProjectRoot "PlatypusTools.UI\Assets\PlatypusTools_Help.html"
    if (Test-Path $HelpFilePath) {
        $HelpContent = Get-Content $HelpFilePath -Raw
        # Match both old 3-part versions and 4-part versions
        $HelpNewContent = $HelpContent -replace "v$([regex]::Escape($CurrentVersion))", "v$NewVersion"
        $HelpNewContent = $HelpNewContent -replace "Version $([regex]::Escape($CurrentVersion))", "Version $NewVersion"
        # Also update any 3-part version references
        $ThreePartCurrent = "$MajorNum.$MinorNum.$BuildNum"
        $HelpNewContent = $HelpNewContent -replace "v$([regex]::Escape($ThreePartCurrent))", "v$NewVersion"
        $HelpNewContent = $HelpNewContent -replace "Version $([regex]::Escape($ThreePartCurrent))", "Version $NewVersion"
        
        if ($WhatIf) {
            Write-Host "  PlatypusTools.UI\Assets\PlatypusTools_Help.html (multiple occurrences)"
        } else {
            Set-Content -Path $HelpFilePath -Value $HelpNewContent -NoNewline
            Write-Host "Updated: PlatypusTools.UI\Assets\PlatypusTools_Help.html" -ForegroundColor Green
        }
    }
    
    Write-Host ""
    if (-not $WhatIf) {
        Write-Host "Version incremented from $CurrentVersion to $NewVersion" -ForegroundColor Cyan
    }
    
} else {
    Write-Error "Could not find version pattern in PlatypusTools.UI.csproj"
    exit 1
}
