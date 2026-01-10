. 'ArchivedScripts\PlatypusTools.ps1'
if (Get-Command -Name Remove-RecentShortcuts -ErrorAction SilentlyContinue) {
    Write-Output 'FOUND'
} else {
    Write-Output 'MISSING'
    Write-Output 'Functions matching *Recent*:'
    Get-Command -CommandType Function | Where-Object { $_.Name -like '*Recent*' } | ForEach-Object { Write-Output $_.Name }
}