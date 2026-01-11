$path = 'c:\Path\hidefolder.ps1'
$content = Get-Content -LiteralPath $path -Raw -ErrorAction Stop
$doubleQuotes = ($content.ToCharArray() | Where-Object {$_ -eq '"'}).Count
"Double-quote count: $doubleQuotes"

# Show lines where cumulative double-quote parity is odd (possible start without end)
$cum = 0
Get-Content -LiteralPath $path | ForEach-Object -Begin { $ln=0 } -Process {
    $ln++
    $count = ($_ -split '"').Count - 1
    $cum += $count
    if (($cum % 2) -ne 0) { Write-Output ("Odd parity at line {0}: {1}" -f $ln, $_) }
} -ErrorAction Stop
