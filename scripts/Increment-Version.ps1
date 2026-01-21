<#
.SYNOPSIS
    Increments the patch version (fourth number) in all version files.
    
.DESCRIPTION
    This script finds all version references in the project and increments
    the fourth number (build/patch). For example: 3.2.6.1 -> 3.2.6.2
    
.PARAMETER WhatIf
    Shows what changes would be made without actually making them.
    
.EXAMPLE
    .\Increment-Version.ps1
    Increments version in all project files.
    
.EXAMPLE
    .\Increment-Version.ps1 -WhatIf
    Shows what changes would be made without applying them.
#>

param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

# Files containing version information
$VersionFiles = @(
    @{ Path = "PlatypusTools.UI\PlatypusTools.UI.csproj"; Pattern = '<Version>(\d+)\.(\d+)\.(\d+)\.(\d+)</Version>' },
    @{ Path = "PlatypusTools.Core\PlatypusTools.Core.csproj"; Pattern = '<Version>(\d+)\.(\d+)\.(\d+)\.(\d+)</Version>' },
    @{ Path = "PlatypusTools.Installer\Product.wxs"; Pattern = 'Version="(\d+)\.(\d+)\.(\d+)\.(\d+)"' },
    @{ Path = "PlatypusTools.UI\Views\SettingsWindow.xaml"; Pattern = 'Text="Version: (\d+)\.(\d+)\.(\d+)\.(\d+)"' },
    @{ Path = "PlatypusTools.UI\Views\AboutWindow.xaml"; Pattern = 'Text="(\d+)\.(\d+)\.(\d+)\.(\d+)"' }
)

# Get current version from the UI csproj
$UICsprojPath = Join-Path $ProjectRoot "PlatypusTools.UI\PlatypusTools.UI.csproj"
$UICsprojContent = Get-Content $UICsprojPath -Raw

if ($UICsprojContent -match '<Version>(\d+)\.(\d+)\.(\d+)\.(\d+)</Version>') {
    $Major = [int]$Matches[1]
    $Minor = [int]$Matches[2]
    $Build = [int]$Matches[3]
    $Revision = [int]$Matches[4]
    
    $CurrentVersion = "$Major.$Minor.$Build.$Revision"
    $NewRevision = $Revision + 1
    $NewVersion = "$Major.$Minor.$Build.$NewRevision"
    
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
                $NewMatch = $OldMatch -replace '(\d+)\.(\d+)\.(\d+)\.(\d+)', $NewVersion
                
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
        $HelpNewContent = $HelpContent -replace "v$([regex]::Escape($CurrentVersion))", "v$NewVersion"
        $HelpNewContent = $HelpNewContent -replace "Version $([regex]::Escape($CurrentVersion))", "Version $NewVersion"
        
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
