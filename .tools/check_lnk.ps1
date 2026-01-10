$lnk = Join-Path $env:TEMP 'pt_recent_parity_recent\t.lnk'
Write-Output "Exists: $(Test-Path $lnk)"
$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut($lnk)
Write-Output "Target: $($s.TargetPath)"
Write-Output "FullName: $lnk"