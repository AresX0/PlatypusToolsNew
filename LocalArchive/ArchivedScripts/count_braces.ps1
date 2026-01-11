$path='c:\Path\hidefolder.ps1'
$cum=0
$ln=0
Get-Content -LiteralPath $path | ForEach-Object {
    $ln++
    $open = ($_ -split '\{').Count -1
    $close = ($_ -split '\}').Count -1
    $cum += $open - $close
    if ($cum -ne 0) { Write-Output ("Line {0}: cumulative braces = {1}" -f $ln, $cum) }
}
Write-Output "Final cumulative braces = $cum"