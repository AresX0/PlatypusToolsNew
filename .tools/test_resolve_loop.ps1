. 'C:\Projects\Platypustools\ArchivedScripts\PlatypusTools.ps1'
$shellRef = [ref] $null
$recent = Join-Path $env:TEMP 'pt_recent_parity_recent'
$shortcuts = Get-ChildItem -Path $recent -Filter *.lnk -Force -ErrorAction SilentlyContinue
foreach ($lnk in $shortcuts) {
    Write-Output "LNK: $($lnk.FullName)"
    $target = Resolve-LnkTarget $lnk.FullName $shellRef
    Write-Output "TARGET: $target"
    if ($shellRef.Value) { Write-Output "SHELLREF: $($shellRef.Value.GetType().FullName)" } else { Write-Output "SHELLREF: null" }
}