$path='c:\Path\hidefolder.ps1'
$lines = Get-Content -LiteralPath $path -ErrorAction Stop
$total = $lines.Count
for ($i=1; $i -le $total; $i++) {
    $text = ($lines[0..($i-1)] -join "`n")
    $errors = [ref]$null
    try {
        [void][System.Management.Automation.Language.Parser]::ParseInput($text, $errors, [ref]$null)
    } catch {
        Write-Output "Parse threw at prefix $i"
        break
    }
    if ($errors.Value -and $errors.Value.Count -gt 0) {
        Write-Output "Errors at prefix lines: $i"
        $errors.Value | ForEach-Object { Write-Output ("{0} at {1}:{2}" -f $_.Message, $_.Extent.StartLine, $_.Extent.StartColumn) }
        break
    }
}
Write-Output "Done. Checked up to $i of $total lines."