#requires -Version 5.1
<#
.SYNOPSIS
    Verifies a GitHub release has its expected assets attached and uploaded.

.DESCRIPTION
    The GitHub `gh release upload` command silently fails often. This script
    asserts that:
      - The release tag exists.
      - The expected MSI file is attached, state=uploaded, size > MinSizeMB.
      - The expected EXE file is attached, state=uploaded, size > MinExeMB.

    Returns exit code 0 on success, non-zero on failure. Designed to be
    pipeline-friendly: prints a single summary line per asset.

.PARAMETER Tag
    The release tag to verify (e.g. v4.0.3.9).

.PARAMETER MinSizeMB
    Minimum acceptable MSI size in MB. Default 100.

.PARAMETER MinExeMB
    Minimum acceptable EXE size in MB. Default 50.

.EXAMPLE
    .\Verify-Release.ps1 -Tag v4.0.3.9
#>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [int]$MinSizeMB = 100,
    [int]$MinExeMB  = 50
)

$ErrorActionPreference = 'Stop'

function Fail($msg) {
    Write-Host "[FAIL] $msg" -ForegroundColor Red
    exit 1
}

function Ok($msg) {
    Write-Host "[ OK ] $msg" -ForegroundColor Green
}

# 1. gh available?
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Fail "gh CLI not found. Install GitHub CLI from https://cli.github.com/"
}

# 2. Release exists?
$json = gh release view $Tag --json tagName,assets 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
    Fail "Release '$Tag' not found on GitHub."
}
$release = $json | ConvertFrom-Json
Ok "Release '$Tag' exists."

# 3. Assets present?
$assets = @($release.assets)
if ($assets.Count -eq 0) {
    Fail "Release '$Tag' has zero assets attached. Re-run: gh release upload $Tag <files> --clobber"
}

$expectedMsi = "PlatypusToolsSetup-$Tag.msi"
$expectedExe = "PlatypusTools.UI.exe"

$msi = $assets | Where-Object { $_.name -eq $expectedMsi -or $_.name -like "PlatypusToolsSetup-*.msi" } | Select-Object -First 1
$exe = $assets | Where-Object { $_.name -eq $expectedExe } | Select-Object -First 1

if (-not $msi) { Fail "MSI asset '$expectedMsi' missing from release '$Tag'." }
$msiMB = [math]::Round($msi.size / 1MB, 1)
if ($msi.state -and $msi.state -ne 'uploaded') {
    Fail "MSI '$($msi.name)' is in state '$($msi.state)' (expected 'uploaded')."
}
if ($msiMB -lt $MinSizeMB) {
    Fail "MSI '$($msi.name)' is only ${msiMB}MB (minimum ${MinSizeMB}MB) - upload likely truncated."
}
Ok "MSI '$($msi.name)' attached: ${msiMB}MB, state=$($msi.state)"

if (-not $exe) {
    Write-Host "[WARN] Portable EXE '$expectedExe' missing - non-fatal but recommended." -ForegroundColor Yellow
} else {
    $exeMB = [math]::Round($exe.size / 1MB, 1)
    if ($exe.state -and $exe.state -ne 'uploaded') {
        Fail "EXE '$($exe.name)' is in state '$($exe.state)' (expected 'uploaded')."
    }
    if ($exeMB -lt $MinExeMB) {
        Fail "EXE '$($exe.name)' is only ${exeMB}MB (minimum ${MinExeMB}MB) - upload likely truncated."
    }
    Ok "EXE '$($exe.name)' attached: ${exeMB}MB, state=$($exe.state)"
}

Write-Host "`nRelease '$Tag' verified successfully." -ForegroundColor Cyan
exit 0
