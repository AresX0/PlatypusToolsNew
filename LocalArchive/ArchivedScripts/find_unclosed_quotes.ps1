$path = 'c:\Path\hidefolder.ps1'
$text = Get-Content -LiteralPath $path -Raw -ErrorAction Stop
$inDouble = $false
$inSingle = $false
$line = 1
$startLineDouble = $null
for ($i=0; $i -lt $text.Length; $i++) {
    $c = $text[$i]
    if ($c -eq "`n") { $line++ }
    if ($c -eq '"') {
        # check if previous char is backtick (escape)
        $prev = if ($i -gt 0) { $text[$i-1] } else { '' }
        if ($prev -ne '`') {
            if (-not $inSingle) {
                $inDouble = -not $inDouble
                if ($inDouble -and -not $startLineDouble) { $startLineDouble = $line }
                if (-not $inDouble) { $startLineDouble = $null }
            }
        }
    } elseif ($c -eq "'") {
        $prev = if ($i -gt 0) { $text[$i-1] } else { '' }
        if ($prev -ne '`') {
            if (-not $inDouble) {
                $inSingle = -not $inSingle
            }
        }
    }
}
if ($inDouble) {
    Write-Output "Unclosed double-quote started at line $startLineDouble"
} else {
    Write-Output "No unclosed double-quote found"
}
if ($inSingle) { Write-Output "Unclosed single-quote present" }
