. 'C:\Projects\Platypustools\ArchivedScripts\PlatypusTools.ps1'
$tmp = 'C:\Users\doran\AppData\Local\Temp\pt_hider_parity'
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $tmp | Out-Null
Write-Output "Before: Attributes = $((Get-Item -LiteralPath $tmp).Attributes)"
Set-Hidden -Path $tmp
Write-Output "After PS Set-Hidden: Get-HiddenState = $(Get-HiddenState -Path $tmp)"
Write-Output "After PS Attributes = $((Get-Item -LiteralPath $tmp).Attributes)"
