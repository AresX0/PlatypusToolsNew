$x=@([pscustomobject]@{Path='p';Target='t'})
Write-Output "GetType: $($x.GetType().FullName)"
$x | ConvertTo-Json -Compress | Write-Output
Write-Output "---"
, $x | ConvertTo-Json -Compress | Write-Output
Write-Output "---"
@($x) | ConvertTo-Json -Compress | Write-Output