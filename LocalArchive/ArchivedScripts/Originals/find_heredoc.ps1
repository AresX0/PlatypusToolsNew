$path = 'c:\Path\hidefolder.ps1'
$ln = 0
$openCount = 0
Get-Content -LiteralPath $path | ForEach-Object {
    $ln++
    $s = $_.TrimStart()
    $prefix = if ($s.Length -ge 2) { $s.Substring(0,2) } else { '' }
    if ($prefix -eq '"@' -or $prefix -eq "@'") { $openCount++; Write-Output ("Found {0} open at line {1}" -f $prefix, $ln) }
    if ($s -eq '"@' -or $s -eq "'@") { $openCount--; Write-Output ("Found {0} close at line {1}" -f $s, $ln) }
}
Write-Output ("Heredoc net opens: {0}" -f $openCount)