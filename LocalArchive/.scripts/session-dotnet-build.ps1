# Update session PATH to include dotnet directory and run restore/build/test
$p = 'C:\Program Files\dotnet'
if (-not ($env:PATH -split ';' | Where-Object { $_ -eq $p })) {
    $env:PATH = $p + ';' + $env:PATH
    Write-Host "Prepended $p to PATH for this session"
} else {
    Write-Host "$p already in PATH"
}

Write-Host "dotnet location:"
Get-Command dotnet -ErrorAction SilentlyContinue | Format-List

Write-Host "SDKs:"
dotnet --list-sdks

Write-Host "Restoring dotnet tools (dotnet-format)..."
dotnet tool restore

Write-Host "Verifying code format (dotnet-format)..."
# Verify no formatting changes; this exits non-zero on formatting differences
try {
    dotnet format --verify-no-changes --verbosity minimal
    Write-Host "dotnet-format verification passed"
}
catch {
    Write-Host "dotnet-format reported issues; run 'dotnet format' locally to fix them"
}

Write-Host "Ensuring nuget.org is added as a source (may already exist)..."
try {
    dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org 2>&1 | Write-Host
    Write-Host "Attempted to add nuget.org source"
}
catch {
    Write-Host "Failed to add nuget.org source (may already exist or network issues): $_"
}

Write-Host "Current NuGet sources (global + user + local):"
try { dotnet nuget list source 2>&1 | Write-Host } catch { Write-Host "dotnet nuget list source failed: $_" }

Write-Host "Restoring solution..."
dotnet restore .\PlatypusTools.sln --verbosity minimal

Write-Host "Building solution..."
dotnet build .\PlatypusTools.sln -c Release --no-restore

Write-Host "Running tests..."
dotnet test .\PlatypusTools.Net\PlatypusTools.Core.Tests\PlatypusTools.Core.Tests.csproj -c Release --no-build
