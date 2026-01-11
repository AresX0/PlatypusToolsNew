$list = New-Object System.Collections.Generic.List[object]
$list.Add([pscustomobject]@{Path='p';Target='t'})
Write-Output "List type: $($list.GetType().FullName)"
$list | Select-Object Path,Target | ConvertTo-Json -Compress | Write-Output