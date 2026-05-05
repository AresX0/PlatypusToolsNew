<#
.SYNOPSIS
  Sync the workspace `website/` folder into the PlatySoft Azure App Service deploy
  tree and push to https://platytalk.platysoft.com.

.DESCRIPTION
  The two trees are intentionally separate:
    Source : C:\Projects\PlatypusToolsNew\website\          (committed to git here)
    Deploy : C:\Projects\PlatySoft\public\platytalk\        (served by Express)

  Without this script it's easy to edit `website/` and run `az webapp deploy` from
  PlatySoft and ship nothing — the workspace edits never reached the deploy tree.

  This script:
    1. Mirrors website/ -> public/platytalk/ (Robocopy /MIR by default).
    2. Zips PlatySoft (excluding node_modules, .git, azure-logs*, data, .azure).
    3. Runs `az webapp deploy` synchronously (waits for restart).
    4. Polls https://platytalk.platysoft.com/platytalk.js until the deployed copy
       matches the source by SHA-256 (or 90s timeout).

.PARAMETER NoMirror
  Skip the website/ -> public/platytalk/ copy step. Useful if you've already
  hand-edited files in PlatySoft and just want to deploy.

.PARAMETER NoVerify
  Skip the post-deploy SHA-256 verification. Faster, but you won't know if
  the CDN/edge cache served stale bytes.

.PARAMETER Async
  Use --async true on `az webapp deploy`. Returns immediately. Default is sync.

.EXAMPLE
  .\Deploy-Web.ps1
    Mirrors, deploys, and verifies. Recommended path.
#>
[CmdletBinding()]
param(
  [switch]$NoMirror,
  [switch]$NoVerify,
  [switch]$Async
)

$ErrorActionPreference = 'Stop'

$Source    = 'C:\Projects\PlatypusToolsNew\website'
$DeployDir = 'C:\Projects\PlatySoft'
$WebTarget = Join-Path $DeployDir 'public\platytalk'
$Zip       = Join-Path $env:TEMP 'platysoft-deploy.zip'
$AppRg     = 'platysoft-rg'
$AppName   = 'platysoft'
$VerifyUrl = 'https://platytalk.platysoft.com/platytalk.js'

if (-not (Test-Path $Source))    { throw "Source not found: $Source" }
if (-not (Test-Path $DeployDir)) { throw "Deploy tree not found: $DeployDir" }

# ---- 1. Mirror website/ -> public/platytalk/ ----------------------------
if (-not $NoMirror) {
  if (-not (Test-Path $WebTarget)) { New-Item -ItemType Directory -Path $WebTarget -Force | Out-Null }
  Write-Host "==> Mirroring $Source -> $WebTarget" -ForegroundColor Cyan
  # Robocopy: /MIR mirrors (deletes extra files in target), /NFL/NDL/NJH/NJS quiets it.
  & robocopy $Source $WebTarget /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
  # Robocopy exit codes 0..7 are success; >=8 is an error.
  if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
  Write-Host "    OK ($((Get-ChildItem $WebTarget -Recurse -File).Count) files)" -ForegroundColor Green
} else {
  Write-Host "==> Skipping mirror (--NoMirror)" -ForegroundColor Yellow
}

# ---- 2. Build deployment zip --------------------------------------------
Write-Host "==> Building deploy zip" -ForegroundColor Cyan
if (Test-Path $Zip) { Remove-Item $Zip -Force }
$exclude = @('node_modules','.git','azure-logs','azure-logs2','azure-logs3','azure-logs4','azure-logs5',
             'azure-logs2.zip','azure-logs3.zip','azure-logs4.zip','azure-logs5.zip','data','.azure')
Push-Location $DeployDir
try {
  $items = Get-ChildItem -Force | Where-Object { $exclude -notcontains $_.Name }
  Compress-Archive -Path $items.FullName -DestinationPath $Zip -Force
}
finally { Pop-Location }
$zipSizeMB = [Math]::Round((Get-Item $Zip).Length / 1MB, 1)
Write-Host "    Zip: $Zip ($zipSizeMB MB)" -ForegroundColor Green

# ---- 3. Push to Azure ----------------------------------------------------
Write-Host "==> az webapp deploy (rg=$AppRg name=$AppName)" -ForegroundColor Cyan
$asyncFlag = if ($Async) { 'true' } else { 'false' }
$out = az webapp deploy -g $AppRg -n $AppName --src-path $Zip --type zip --async $asyncFlag 2>&1
if ($LASTEXITCODE -ne 0) { $out | Write-Host; throw "az webapp deploy failed (exit $LASTEXITCODE)" }
Write-Host "    Deploy submitted." -ForegroundColor Green

if ($Async) {
  Write-Host "==> Async mode: not verifying. Check status with:`n    az webapp log deployment show -g $AppRg -n $AppName" -ForegroundColor Yellow
  return
}

# ---- 4. Verify (SHA-256 of deployed platytalk.js matches local) ---------
if ($NoVerify) {
  Write-Host "==> Skipping verify (--NoVerify)" -ForegroundColor Yellow
  return
}

$srcHash = (Get-FileHash (Join-Path $Source 'platytalk.js') -Algorithm SHA256).Hash
Write-Host "==> Verifying https://.../platytalk.js matches local SHA-256 $($srcHash.Substring(0,12))..." -ForegroundColor Cyan
$deadline = (Get-Date).AddSeconds(90)
$matched = $false
while ((Get-Date) -lt $deadline) {
  try {
    $bust = [guid]::NewGuid()
    $r = Invoke-WebRequest -Uri "$VerifyUrl`?_=$bust" -UseBasicParsing -Headers @{'Cache-Control'='no-cache'}
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($r.Content)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $remoteHash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','')
    if ($remoteHash -eq $srcHash) { $matched = $true; break }
  } catch { }
  Start-Sleep -Seconds 4
}
if ($matched) {
  Write-Host "    LIVE: bytes match local copy." -ForegroundColor Green
} else {
  Write-Warning "Timed out waiting for live site to match. The deploy may still be propagating, or the site may be serving cached bytes."
  Write-Host "    Local SHA : $srcHash"
  exit 1
}
