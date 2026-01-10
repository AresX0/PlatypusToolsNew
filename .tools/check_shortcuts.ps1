$recent = Join-Path $env:TEMP 'pt_recent_parity_recent'
Write-Output "Checking recent dir: $recent"
$items = Get-ChildItem -Path $recent -Filter *.lnk -Force -ErrorAction SilentlyContinue
if ($items) { foreach ($i in $items) { Write-Output "Found: $($i.FullName)" } } else { Write-Output "No .lnk found" }