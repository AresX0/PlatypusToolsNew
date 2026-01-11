$p = 'c:\Path\hidefolder.ps1'
$bytes = [System.IO.File]::ReadAllBytes($p)
# Attempt UTF8 decode first
$t = [System.Text.Encoding]::UTF8.GetString($bytes)
# Fix common mojibake and smart quotes
$t = $t -replace 'â€¦','…'
$t = $t -replace 'â€™','\''
$t = $t -replace 'â€œ','"'
$t = $t -replace 'â€d','"'
$t = $t -replace 'â€','\''
$t = $t -replace 'Â',''
$t = $t -replace 'â€"','"'
$t = $t -replace '“','"'
$t = $t -replace '”','"'
# Save as UTF8 without BOM
[System.IO.File]::WriteAllText($p, $t, [System.Text.Encoding]::UTF8)
Write-Output "Normalized encoding and replaced common smart characters in $p"