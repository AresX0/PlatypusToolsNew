<#
Create a timestamped backup and a versioned working copy for PlatypusTools.ps1.
Usage: .\new_version.ps1 -Version 1.2.0 -Note "Short changelog summary"
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Note = ''
    [switch]$NonInteractive
)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Version' 
$ts = (Get-Date).ToString('yyyyMMdd_HHmmss')
$backupsDir = Join-Path $PSScriptRoot '..\backups' | Resolve-Path -Relative | ForEach-Object { $_ }
if (-not (Test-Path -LiteralPath $backupsDir)) { New-Item -ItemType Directory -Path $backupsDir -Force | Out-Null }
$src = Join-Path (Get-Location) 'PlatypusTools.ps1'
$bk = Join-Path $backupsDir "PlatypusTools_$ts.ps1"
Copy-Item -Path $src -Destination $bk -Force
$wc = Join-Path (Get-Location) "PlatypusTools.v$Version.ps1"
Copy-Item -Path $src -Destination $wc -Force
# Update version token in working copy
(Get-Content -Path $wc) -replace "\$script:Version\s*=\s*'[^']+'","`$script:Version = '$Version'" | Set-Content -Path $wc -Encoding UTF8
# Append changelog entry
$cl = Join-Path (Get-Location) 'CHANGELOG.md'
$entry = @"
## v$Version - $(Get-Date -Format 'yyyy-MM-dd')
- Backup: $(Split-Path -Leaf $bk)
- Working copy: $(Split-Path -Leaf $wc)
- Note: $Note
"@
Add-Content -Path $cl -Value $entry
Write-Output "Backup created: $bk"
Write-Output "Working copy created: $wc"
Write-Output "CHANGELOG.md updated"

