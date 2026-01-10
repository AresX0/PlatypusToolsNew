. 'C:\Projects\Platypustools\ArchivedScripts\PlatypusTools.ps1'
$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut('C:\Users\doran\AppData\Local\Temp\pt_recent_parity_recent\t.lnk')
Write-Output "Direct COM Target: $($s.TargetPath)"