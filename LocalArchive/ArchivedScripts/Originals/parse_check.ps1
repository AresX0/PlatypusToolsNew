$script = Get-Content -Raw 'c:\Path\hidefolder.ps1'
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseInput($script, [ref]$tokens, [ref]$errors)
if ($errors -and $errors.Count -gt 0) {
    foreach ($e in $errors) {
        Write-Output ("MESSAGE: $($e.Message)")
        Write-Output ("LINE: $($e.Extent.StartLineNumber) COL: $($e.Extent.StartColumn)")
        Write-Output "EXTENT:";
        Write-Output $e.Extent.Text
        Write-Output '----'
    }
    exit 2
}
Write-Output 'PARSE_OK'
