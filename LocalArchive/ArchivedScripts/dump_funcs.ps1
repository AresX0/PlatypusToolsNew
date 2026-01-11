$src = Get-Content -LiteralPath 'c:\Path\hidefolder.ps1' -Raw
function DumpFunc([string]$name, [string]$out) {
    $m = [regex]::Match($src, "(?ms)^\s*function\s+" + [regex]::Escape($name) + "\s*")
    if (-not $m.Success) { "No match for $name" | Out-File -FilePath $out -Encoding utf8; return }
    $start = $m.Index
    $open = $src.IndexOf('{', $start)
    if ($open -lt 0) { "No open brace for $name" | Out-File -FilePath $out -Encoding utf8; return }
    $i = $open; $depth = 0; $len = $src.Length
    do {
        $c = $src[$i]
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') { $depth-- }
        $i++
        if ($i -ge $len) { break }
    } while ($depth -gt 0)
    $block = $src.Substring($start, $i - $start)
    $block | Out-File -FilePath $out -Encoding utf8
}

DumpFunc 'Set-AclRestriction' 'c:\Path\diag_SetAcl_func.txt'
DumpFunc 'Set-Hidden' 'c:\Path\diag_SetHidden_func.txt'
Write-Output 'Dumped function text to files'