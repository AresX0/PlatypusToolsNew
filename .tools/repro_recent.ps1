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
. "C:\Projects\Platypustools\ArchivedScripts\PlatypusTools.ps1"
$r = Remove-RecentShortcuts -targetDirs @($tmpTarget) -dryRun -includeSubDirs -recentFolder $recent
Write-Output "RESULTS TYPE: $($r.GetType().FullName)"
if ($r -is [System.Collections.ICollection]) { Write-Output "RESULTS COUNT: $($r.Count)" }
if ($r -and ($r.Count -gt 0)) {
    foreach ($i in $r) {
        Write-Output "--- ITEM ---"
        Write-Output "Type: $($i.Type)"
        Write-Output "Path: $($i.Path)"
        Write-Output "Target: $($i.Target)"
        if ($i.PSObject.Properties.Name -contains 'MatchedPaths') { Write-Output "MatchedPaths: $($i.MatchedPaths -join ',')" }
    }
} else {
    Write-Output "NORESULT"
}