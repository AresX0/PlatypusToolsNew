$script = Get-Content -Raw 'c:\Path\hidefolder.ps1'
$lines = $script -split "\r?\n"
$inQuote = $false
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    for ($j = 0; $j -lt $line.Length; $j++) {
        $ch = $line[$j]
        $prev = if ($j -gt 0) { $line[$j-1] } else { '' }
        if ($ch -eq '"' -and $prev -ne '`') { $inQuote = -not $inQuote }
    }
    if ($inQuote) { Write-Output "Unclosed quote at or before line $($i+1): $line" }
}
if (-not $inQuote) { Write-Output 'No open quotes at EOF.' } else { Write-Output 'Ending with open quote.' }
