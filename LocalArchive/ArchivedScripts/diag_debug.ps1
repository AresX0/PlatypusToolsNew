$src = Get-Content -LiteralPath 'c:\Path\hidefolder.ps1' -Raw
$pos = 0
$len = $src.Length
while ($pos -lt $len) {
    $m = [regex]::Match($src.Substring($pos), '(?ms)^\s*function\s+([A-Za-z0-9_-]+)\s*')
    if (-not $m.Success) { break }
    $start = $pos + $m.Index
    $openIndex = $src.IndexOf('{', $start)
    if ($openIndex -lt 0) { break }
    $i = $openIndex
    $depth = 0
    do {
        $c = $src[$i]
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') { $depth-- }
        $i++
        if ($i -ge $len) { break }
    } while ($depth -gt 0)
    $end = $i
    if ($end -le $start) { break }
    $fnText = $src.Substring($start, $end - $start)
    Invoke-Expression $fnText
    $pos = $end
}

$temp = Join-Path $env:TEMP ('fh_diag_' + [guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $temp | Out-Null

Set-Hidden -Path $temp
$attrsAfterSet = (Get-Item -LiteralPath $temp).Attributes
$isHidden = Get-HiddenState -Path $temp

$aclResult = $null
try {
    $aclResult = Set-AclRestriction -Path $temp
} catch {
    $aclResult = "EX: $($_.Exception.Message)"
}

Write-Output "After Set-Hidden: $attrsAfterSet | IsHidden=$isHidden"
Write-Output "Set-AclRestriction: $aclResult"

Clear-Hidden -Path $temp
$attrsAfterClear = (Get-Item -LiteralPath $temp).Attributes
$isHidden2 = Get-HiddenState -Path $temp
Write-Output "After Clear-Hidden: $attrsAfterClear | IsHidden=$isHidden2"

Restore-ACL -Path $temp
Remove-Item -LiteralPath $temp -Recurse -Force
