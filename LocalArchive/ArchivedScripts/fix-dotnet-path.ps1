# Fix dotnet PATH entry in machine Path
$mp = [Environment]::GetEnvironmentVariable('Path','Machine')
$parts = $mp -split ';'
$changed = $false
for ($i = 0; $i -lt $parts.Length; $i++) {
    if ($parts[$i] -eq 'C:\Program Files\dotnet\dotnet.exe') {
        $parts[$i] = 'C:\Program Files\dotnet'
        $changed = $true
    }
}
if ($changed) {
    $new = ($parts | Where-Object { $_ -ne '' }) -join ';'
    try {
        [Environment]::SetEnvironmentVariable('Path', $new, 'Machine')
        Write-Host 'Updated Machine PATH. Restart VS Code and sign out/in for changes to propagate.'
    }
    catch {
        Write-Error "Failed to update Machine PATH: $_"
    }
}
else {
    Write-Host 'No change needed.'
}