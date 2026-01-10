$ErrorActionPreference = 'Stop'
$tmpTarget = Join-Path $env:TEMP 'pt_recent_parity_target'
if (Test-Path $tmpTarget) { Remove-Item $tmpTarget -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $tmpTarget | Out-Null
$targetFile = Join-Path $tmpTarget 'file.txt'
Set-Content -Path $targetFile -Value 'x'
$recent = Join-Path $env:TEMP 'pt_recent_parity_recent'
if (Test-Path $recent) { Remove-Item $recent -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $recent | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = Join-Path $recent 't.lnk'
$s = $shell.CreateShortcut($shortcut)
$s.TargetPath = $targetFile
$s.Save()
Write-Output "Created shortcut: $shortcut -> $targetFile"
. "C:\Projects\Platypustools\ArchivedScripts\PlatypusTools.ps1"
Write-Output "Value of 'RecentFolder' param to pass: $recent"
# Inspect what Remove-RecentShortcuts sees for 'recent'
# We'll call the function in a way that prints internal variables by invoking it in a scriptblock wrapper
$script = @"
param([string]`$rf, [string]`$td)
. 'C:\Projects\Platypustools\ArchivedScripts\PlatypusTools.ps1'
`$recentDebug = if (`$PSBoundParameters.ContainsKey('RecentFolder') -and `$RecentFolder) { `$RecentFolder } else { [Environment]::GetFolderPath('Recent') }
Write-Output "Inside function: recent = `$recentDebug"
Write-Output "Files in recent:"
Get-ChildItem -Path `$rf -Filter *.lnk -Force -ErrorAction SilentlyContinue | ForEach-Object { Write-Output $_.FullName }
`$r = Remove-RecentShortcuts -targetDirs @(`$td) -dryRun -includeSubDirs -recentFolder `$rf
if (`$r -and `$r.Count -gt 0) { `$r | Select-Object Path,Target | ConvertTo-Json -Compress | Write-Output } else { Write-Output "NORESULT_INSIDE" }
"@
# Execute the scriptblock with the temp values
powershell -NoProfile -NonInteractive -Command $script -rf $recent -td $tmpTarget | Write-Output