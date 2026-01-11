$b = [System.IO.File]::ReadAllBytes('c:\Path\hidefolder.ps1')
$max = [Math]::Min(32, $b.Length)
$out = @()
for ($i=0; $i -lt $max; $i++) { $out += ('{0:X2}' -f $b[$i]) }
Write-Output ($out -join ' ')
