# Diagnostic script to investigate ACL / hide/unhide anomalies
$out = 'c:\Path\diag_acl_results.txt'
Remove-Item -LiteralPath $out -ErrorAction SilentlyContinue
Add-Content -LiteralPath $out -Value ("Diag start: {0}" -f (Get-Date))
$src = Get-Content -LiteralPath 'c:\Path\hidefolder.ps1' -Raw
# define functions
$pos=0; $len=$src.Length
while ($pos -lt $len) {
    $m = [regex]::Match($src.Substring($pos), '(?ms)^\s*function\s+([A-Za-z0-9_-]+)\s*')
    if (-not $m.Success) { break }
    $start = $pos + $m.Index
    $openIndex = $src.IndexOf('{', $start)
    if ($openIndex -lt 0) { break }
    $i = $openIndex; $depth = 0
    do { $c = $src[$i]; if ($c -eq '{') { $depth++ } elseif ($c -eq '}') { $depth-- }; $i++; if ($i -ge $len) { break } } while ($depth -gt 0)
    $end = $i
    if ($end -le $start) { break }
    $fnText = $src.Substring($start, $end-$start)
    try { Invoke-Expression $fnText; Add-Content -LiteralPath $out -Value ("Defined: {0}" -f $m.Groups[1].Value) } catch { Add-Content -LiteralPath $out -Value ("Define failed: {0} -> {1}" -f $m.Groups[1].Value, $_.Exception.Message) }
    $pos = $end
}
Add-Content -LiteralPath $out -Value "Functions defined."

# create temp folder
$temp = Join-Path $env:TEMP ('fh_diag_' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
Add-Content -LiteralPath $out -Value ("Temp folder: {0}" -f $temp)

# write basic checks
Add-Content -LiteralPath $out -Value ("Get-Command Set-AclRestriction: {0}" -f (Get-Command Set-AclRestriction -ErrorAction SilentlyContinue))
Add-Content -LiteralPath $out -Value ("Get-Command Restore-ACL: {0}" -f (Get-Command Restore-ACL -ErrorAction SilentlyContinue))

# verify Get-Acl works directly
try { $acl = Get-Acl -LiteralPath $temp; Add-Content -LiteralPath $out -Value 'Get-Acl direct: OK' } catch { Add-Content -LiteralPath $out -Value ("Get-Acl direct failed: {0}" -f $_.Exception.Message) }

# call Set-Hidden and Get-HiddenState
try { Set-Hidden -Path $temp; Add-Content -LiteralPath $out -Value 'Set-Hidden called OK' } catch { Add-Content -LiteralPath $out -Value ("Set-Hidden failed: {0}" -f $_.Exception.Message) }
try { $h = Get-HiddenState -Path $temp; Add-Content -LiteralPath $out -Value ("Get-HiddenState returned: {0}" -f $h) } catch { Add-Content -LiteralPath $out -Value ("Get-HiddenState failed: {0}" -f $_.Exception.Message) }

# call Set-AclRestriction with detailed catch
try {
    Add-Content -LiteralPath $out -Value 'Calling Set-AclRestriction...'
    $res = Set-AclRestriction -Path $temp
    Add-Content -LiteralPath $out -Value ("Set-AclRestriction result: {0}" -f $res)
} catch {
    Add-Content -LiteralPath $out -Value ("Set-AclRestriction EX: {0}" -f $_.Exception.ToString())
    if ($_.InvocationInfo) { Add-Content -LiteralPath $out -Value ("InvocationInfo: {0}" -f $_.InvocationInfo.PositionMessage) }
}

# call Restore-ACL
try { $r = Restore-ACL -Path $temp; Add-Content -LiteralPath $out -Value ("Restore-ACL result: {0}" -f $r) } catch { Add-Content -LiteralPath $out -Value ("Restore-ACL EX: {0}" -f $_.Exception.Message) }

# inspect attributes after ACL ops
try { $attrs = (Get-Item -LiteralPath $temp).Attributes; Add-Content -LiteralPath $out -Value ("Attributes now: {0}" -f $attrs) } catch { Add-Content -LiteralPath $out -Value ("Get-Item attrs failed: {0}" -f $_.Exception.Message) }

# cleanup
try { Remove-Item -LiteralPath $temp -Recurse -Force; Add-Content -LiteralPath $out -Value ("Removed temp {0}" -f $temp) } catch { Add-Content -LiteralPath $out -Value ("Cleanup failed: {0}" -f $_.Exception.Message) }
Add-Content -LiteralPath $out -Value ("Diag finished: {0}" -f (Get-Date))
Write-Output ("Wrote diag results to {0}" -f $out)
