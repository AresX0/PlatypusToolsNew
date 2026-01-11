$path='c:\Path\hidefolder.ps1'
$ln=0
Get-Content -LiteralPath $path | ForEach-Object {
    $ln++
    $chars = $_.ToCharArray()
    for ($i=0; $i -lt $chars.Length; $i++) {
        $c = $chars[$i]
        if ([int][char]$c -gt 127) {
            Write-Output ("Line {0} Col {1}: U+{2:X4} '{3}'" -f $ln, ($i+1), [int][char]$c, $c)
        }
    }
}
