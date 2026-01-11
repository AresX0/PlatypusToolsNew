$p = 'C:\Program Files\dotnet'
if (-not ($env:PATH -split ';' | Where-Object { $_ -eq $p })) { $env:PATH = $p + ';' + $env:PATH }
Write-Host "dotnet location:"; Get-Command dotnet -ErrorAction SilentlyContinue | Format-List
Write-Host "Running dotnet format to apply fixes..."
dotnet format .\PlatypusTools.sln --verbosity minimal
Write-Host "dotnet format completed with exit code: $LASTEXITCODE"
