<#
.SYNOPSIS
    Collects Cloudflare Tunnel and PlatypusTools Remote Server diagnostic info into a ZIP file.
.DESCRIPTION
    Run this on the device experiencing the 502 error. It gathers logs, config,
    network state, and settings so the issue can be diagnosed on another machine.
.OUTPUTS
    A ZIP file on the Desktop: PlatypusDiagnostics_<timestamp>.zip
#>

$ErrorActionPreference = 'SilentlyContinue'
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$tempDir = Join-Path $env:TEMP "PlatypusDiag_$timestamp"
$zipPath = Join-Path ([Environment]::GetFolderPath('Desktop')) "PlatypusDiagnostics_$timestamp.zip"

New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
Write-Host "Collecting diagnostics into: $tempDir" -ForegroundColor Cyan

# ── 1. App log ──────────────────────────────────────────────────────────────
Write-Host "  [1/8] App log..." -ForegroundColor Yellow
$logFile = Join-Path $env:LOCALAPPDATA "PlatypusTools\logs\platypustools.log"
if (Test-Path $logFile) {
    Copy-Item $logFile (Join-Path $tempDir "platypustools.log")
    Write-Host "        Copied ($([math]::Round((Get-Item $logFile).Length / 1KB, 1)) KB)" -ForegroundColor Green
} else {
    "Log file not found: $logFile" | Out-File (Join-Path $tempDir "platypustools_log_MISSING.txt")
    Write-Host "        NOT FOUND" -ForegroundColor Red
}

# ── 2. Cloudflared config.yml ───────────────────────────────────────────────
Write-Host "  [2/8] Cloudflared config.yml..." -ForegroundColor Yellow
$cfConfigDir = Join-Path $env:USERPROFILE ".cloudflared"
$cfConfig = Join-Path $cfConfigDir "config.yml"
if (Test-Path $cfConfig) {
    Copy-Item $cfConfig (Join-Path $tempDir "config.yml")
    Write-Host "        Copied" -ForegroundColor Green
} else {
    "config.yml not found: $cfConfig" | Out-File (Join-Path $tempDir "config_yml_MISSING.txt")
    Write-Host "        NOT FOUND" -ForegroundColor Red
}

# ── 3. Cloudflared credential files (list only, NOT contents) ───────────────
Write-Host "  [3/8] Cloudflared credential files..." -ForegroundColor Yellow
$credInfo = @()
if (Test-Path $cfConfigDir) {
    $credInfo += "Directory: $cfConfigDir"
    $credInfo += ""
    Get-ChildItem $cfConfigDir -File | ForEach-Object {
        $credInfo += "{0,-50} {1,12} {2}" -f $_.Name, "$([math]::Round($_.Length / 1KB, 1)) KB", $_.LastWriteTime
    }
} else {
    $credInfo += ".cloudflared directory not found: $cfConfigDir"
}
$credInfo | Out-File (Join-Path $tempDir "cloudflared_files.txt")
Write-Host "        Listed $((Get-ChildItem $cfConfigDir -File -ErrorAction SilentlyContinue).Count) files" -ForegroundColor Green

# ── 4. PlatypusTools settings.json (redact sensitive fields) ────────────────
Write-Host "  [4/8] Settings (redacted)..." -ForegroundColor Yellow
$settingsFile = Join-Path $env:APPDATA "PlatypusTools\settings.json"
if (Test-Path $settingsFile) {
    try {
        $raw = Get-Content $settingsFile -Raw
        $json = $raw | ConvertFrom-Json

        # Redact potentially sensitive fields but keep tunnel/remote config
        $sensitiveKeys = @('EntraClientSecret', 'EntraAllowedEmails', 'VaultPassword', 'ApiKey', 'Token')
        foreach ($prop in $json.PSObject.Properties) {
            foreach ($sk in $sensitiveKeys) {
                if ($prop.Name -like "*$sk*" -and $prop.Value) {
                    $prop.Value = "*** REDACTED ***"
                }
            }
        }
        $json | ConvertTo-Json -Depth 10 | Out-File (Join-Path $tempDir "settings_redacted.json")
        Write-Host "        Copied (sensitive fields redacted)" -ForegroundColor Green
    } catch {
        # If JSON parse fails, just copy the raw file with a warning
        Copy-Item $settingsFile (Join-Path $tempDir "settings_raw.json")
        Write-Host "        Copied raw (JSON parse failed)" -ForegroundColor Yellow
    }
} else {
    "Settings file not found: $settingsFile" | Out-File (Join-Path $tempDir "settings_MISSING.txt")
    Write-Host "        NOT FOUND" -ForegroundColor Red
}

# ── 5. Network / port listening state ───────────────────────────────────────
Write-Host "  [5/8] Network state..." -ForegroundColor Yellow
$netInfo = @()
$netInfo += "=== All PlatypusTools-range ports (47390-47400) ==="
$netInfo += (netstat -ano | Select-String "4739[0-9]" | Out-String)
$netInfo += ""
$netInfo += "=== All HTTPS listeners ==="
$netInfo += (netstat -ano | Select-String "LISTENING" | Select-String "443\b" | Out-String)
$netInfo += ""
$netInfo += "=== Firewall rules mentioning Platypus or cloudflared ==="
$netInfo += (netsh advfirewall firewall show rule name=all | Select-String -Pattern "Platypus|cloudflared" -Context 5 | Out-String)
$netInfo | Out-File (Join-Path $tempDir "network_state.txt")
Write-Host "        Done" -ForegroundColor Green

# ── 6. cloudflared version & tunnel list ────────────────────────────────────
Write-Host "  [6/8] cloudflared info..." -ForegroundColor Yellow
$cfExe = Join-Path $env:LOCALAPPDATA "PlatypusTools\cloudflared\cloudflared.exe"
$cfInfo = @()
if (Test-Path $cfExe) {
    $cfInfo += "=== cloudflared version ==="
    $cfInfo += (& $cfExe version 2>&1 | Out-String)
    $cfInfo += ""
    $cfInfo += "=== tunnel list ==="
    $cfInfo += (& $cfExe tunnel list 2>&1 | Out-String)
} else {
    # Try system PATH
    $cfSystem = Get-Command cloudflared -ErrorAction SilentlyContinue
    if ($cfSystem) {
        $cfInfo += "=== cloudflared version (system) ==="
        $cfInfo += (& cloudflared version 2>&1 | Out-String)
        $cfInfo += ""
        $cfInfo += "=== tunnel list ==="
        $cfInfo += (& cloudflared tunnel list 2>&1 | Out-String)
    } else {
        $cfInfo += "cloudflared not found at $cfExe or in PATH"
    }
}
$cfInfo | Out-File (Join-Path $tempDir "cloudflared_info.txt")
Write-Host "        Done" -ForegroundColor Green

# ── 7. Self-signed cert info ────────────────────────────────────────────────
Write-Host "  [7/8] Self-signed cert info..." -ForegroundColor Yellow
$certPath = Join-Path $env:APPDATA "PlatypusTools\server-cert.pfx"
$certInfo = @()
if (Test-Path $certPath) {
    $certInfo += "Certificate file: $certPath"
    $certInfo += "Size: $([math]::Round((Get-Item $certPath).Length / 1KB, 1)) KB"
    $certInfo += "Last modified: $((Get-Item $certPath).LastWriteTime)"
    try {
        # Try to load and inspect cert (no password on our self-signed certs)
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath, "", "Exportable")
        $certInfo += "Subject: $($cert.Subject)"
        $certInfo += "Issuer: $($cert.Issuer)"
        $certInfo += "Not Before: $($cert.NotBefore)"
        $certInfo += "Not After: $($cert.NotAfter)"
        $certInfo += "Thumbprint: $($cert.Thumbprint)"
        $certInfo += "Has Private Key: $($cert.HasPrivateKey)"
        $san = $cert.Extensions | Where-Object { $_.Oid.FriendlyName -eq 'Subject Alternative Name' }
        if ($san) { $certInfo += "SAN: $($san.Format($false))" }
        $cert.Dispose()
    } catch {
        $certInfo += "Could not inspect cert: $($_.Exception.Message)"
    }
} else {
    $certInfo += "Certificate file not found: $certPath"
}
$certInfo | Out-File (Join-Path $tempDir "cert_info.txt")
Write-Host "        Done" -ForegroundColor Green

# ── 8. System summary ──────────────────────────────────────────────────────
Write-Host "  [8/8] System summary..." -ForegroundColor Yellow
$sysInfo = @()
$sysInfo += "Computer: $env:COMPUTERNAME"
$sysInfo += "User: $env:USERNAME"
$sysInfo += "OS: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"
$sysInfo += "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
$sysInfo += ""
$sysInfo += "=== PlatypusTools processes ==="
$sysInfo += (Get-Process -Name "PlatypusTools*","cloudflared" -ErrorAction SilentlyContinue |
    Format-Table Id, ProcessName, StartTime, @{N='MemMB';E={[math]::Round($_.WorkingSet64/1MB,1)}} -AutoSize | Out-String)
$sysInfo += ""
$sysInfo += "=== .NET runtime ==="
$sysInfo += (dotnet --info 2>&1 | Select-Object -First 10 | Out-String)
$sysInfo | Out-File (Join-Path $tempDir "system_info.txt")
Write-Host "        Done" -ForegroundColor Green

# ── Package into ZIP ────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Packaging ZIP..." -ForegroundColor Cyan
Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -Force

# Clean up temp
Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Diagnostics collected!" -ForegroundColor Green
Write-Host " ZIP: $zipPath" -ForegroundColor Green
Write-Host " Size: $zipSize KB" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Transfer this ZIP file and share it for analysis." -ForegroundColor Yellow
