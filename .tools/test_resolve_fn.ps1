. 'C:\Projects\Platypustools\ArchivedScripts\PlatypusTools.ps1'
$shellRef = [ref] $null
$t = Resolve-LnkTarget 'C:\Users\doran\AppData\Local\Temp\pt_recent_parity_recent\t.lnk' $shellRef
Write-Output "RESOLVE: $t"
if ($shellRef.Value) { Write-Output "SHELLREF TYPE: $($shellRef.Value.GetType().FullName)" } else { Write-Output "SHELLREF is null" }