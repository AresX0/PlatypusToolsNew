$path='c:\Path\hidefolder.ps1'
$text = Get-Content -Raw -LiteralPath $path
$tokens = [System.Management.Automation.Language.Parser]::Tokenize($text, [ref]$null)
$index=0
foreach ($t in $tokens) {
    $index++
    if ($t.Text -match '"') {
        $snippet = $t.Text.Substring(0,[Math]::Min($t.Text.Length,60))
        Write-Output ("Token {0}: Type={1} Text=[{2}] Line={3}" -f $index, $t.Kind, $snippet, $t.Extent.StartLine)
    }
}

# Look for parser errors
$errors = [ref]$null
[void][System.Management.Automation.Language.Parser]::ParseInput($text, $errors, [ref]$null)
if ($errors.Value) { foreach ($e in $errors.Value) { Write-Output ("ERROR: {0} at {1}:{2}" -f $e.Message, $e.Extent.StartLine, $e.Extent.StartColumn) } }
