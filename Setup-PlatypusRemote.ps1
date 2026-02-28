<#
.SYNOPSIS
    Interactive setup wizard for Platypus Remote authentication and tunneling.
    
.DESCRIPTION
    This script works on ANY system — no build directory required.
    It prompts for all configuration values and writes them to:
      %APPDATA%\PlatypusTools\entra-config.json
    
    PlatypusTools reads this file at startup for Entra ID and Cloudflare 
    Zero Trust authentication settings.
    
    You can also install/configure cloudflared (Cloudflare Tunnel) through
    this script.
    
.EXAMPLE
    .\Setup-PlatypusRemote.ps1
    
    Runs the interactive wizard.
    
.EXAMPLE
    .\Setup-PlatypusRemote.ps1 -SkipCloudflare
    
    Only configure Entra ID, skip Cloudflare setup.
    
.EXAMPLE
    .\Setup-PlatypusRemote.ps1 -SkipEntra
    
    Only configure Cloudflare, skip Entra ID setup.
    
.EXAMPLE
    .\Setup-PlatypusRemote.ps1 -ShowCurrent
    
    Display current configuration without changing anything.

.NOTES  
    Copy this script to any system with PlatypusTools installed and run it.
    No other files from the project are needed.
#>

param(
    [switch]$SkipCloudflare,
    [switch]$SkipEntra,
    [switch]$ShowCurrent
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────
# Config paths
# ─────────────────────────────────────────────
$dataDir = Join-Path $env:APPDATA "PlatypusTools"
$configFile = Join-Path $dataDir "entra-config.json"

function Write-Banner {
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║         Platypus Remote — Setup Wizard                      ║" -ForegroundColor Cyan
    Write-Host "║  Configure Entra ID & Cloudflare Zero Trust authentication  ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Config file: $configFile" -ForegroundColor DarkGray
    Write-Host ""
}

function Load-Config {
    if (Test-Path $configFile) {
        try {
            return (Get-Content $configFile -Raw | ConvertFrom-Json)
        }
        catch {
            Write-Host "  Warning: Could not parse existing config. Starting fresh." -ForegroundColor Yellow
        }
    }
    # Return default config
    return [PSCustomObject]@{
        ClientId                  = ""
        TenantId                  = "common"
        ApiScopeId                = ""
        GraphClientId             = ""
        EntraIdAuthEnabled        = $false
        EntraIdAllowedEmails      = ""
        CloudflareZeroTrustEnabled = $false
        CloudflareTeamDomain      = ""
        CloudflareAudience        = ""
        CloudflareAllowedEmails   = ""
    }
}

function Save-Config($config) {
    if (-not (Test-Path $dataDir)) {
        New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
    }
    $config | ConvertTo-Json -Depth 5 | Set-Content $configFile -Encoding UTF8
    Write-Host ""
    Write-Host "  ✅ Configuration saved to: $configFile" -ForegroundColor Green
}

function Show-Config($config) {
    Write-Host ""
    Write-Host "  ┌─ Current Configuration ──────────────────────────────┐" -ForegroundColor Cyan
    Write-Host "  │" -ForegroundColor Cyan
    Write-Host "  │  ENTRA ID" -ForegroundColor Yellow
    $entraStatus = if ($config.EntraIdAuthEnabled) { "✅ ENABLED" } else { "❌ Disabled" }
    Write-Host "  │  Status:          $entraStatus"
    Write-Host "  │  Client ID:       $(if ($config.ClientId) { $config.ClientId } else { '(not set)' })"
    Write-Host "  │  Tenant ID:       $(if ($config.TenantId) { $config.TenantId } else { 'common' })"
    Write-Host "  │  API Scope ID:    $(if ($config.ApiScopeId) { $config.ApiScopeId } else { '(auto = Client ID)' })"
    Write-Host "  │  Graph Client ID: $(if ($config.GraphClientId) { $config.GraphClientId } else { '(default MS Graph)' })"
    Write-Host "  │  Allowed Emails:  $(if ($config.EntraIdAllowedEmails) { $config.EntraIdAllowedEmails } else { '(all users)' })"
    Write-Host "  │" -ForegroundColor Cyan
    Write-Host "  │  CLOUDFLARE ZERO TRUST" -ForegroundColor Yellow
    $cfStatus = if ($config.CloudflareZeroTrustEnabled) { "✅ ENABLED" } else { "❌ Disabled" }
    Write-Host "  │  Status:          $cfStatus"
    Write-Host "  │  Team Domain:     $(if ($config.CloudflareTeamDomain) { $config.CloudflareTeamDomain } else { '(not set)' })"
    Write-Host "  │  App AUD Tag:     $(if ($config.CloudflareAudience) { $config.CloudflareAudience.Substring(0, [Math]::Min(40, $config.CloudflareAudience.Length)) + '...' } else { '(not set)' })"
    Write-Host "  │  Allowed Emails:  $(if ($config.CloudflareAllowedEmails) { $config.CloudflareAllowedEmails } else { '(all users)' })"
    Write-Host "  │" -ForegroundColor Cyan
    Write-Host "  └──────────────────────────────────────────────────────┘" -ForegroundColor Cyan
    Write-Host ""
}

function Prompt-Value {
    param(
        [string]$Label,
        [string]$Description,
        [string]$WhereToFind,
        [string]$Default = "",
        [string]$Example = "",
        [bool]$Required = $false
    )
    
    Write-Host ""
    Write-Host "  ── $Label ──" -ForegroundColor Yellow
    if ($Description) {
        Write-Host "  $Description" -ForegroundColor Gray
    }
    if ($WhereToFind) {
        Write-Host "  📍 Where to find it: $WhereToFind" -ForegroundColor DarkCyan
    }
    if ($Example) {
        Write-Host "  Example: $Example" -ForegroundColor DarkGray
    }
    
    $prompt = "  Enter value"
    if ($Default) {
        $prompt += " [$Default]"
    }
    $prompt += ": "
    
    while ($true) {
        $value = Read-Host $prompt
        if ([string]::IsNullOrWhiteSpace($value)) {
            if ($Default) { return $Default }
            if (-not $Required) { return "" }
            Write-Host "  ⚠ This field is required." -ForegroundColor Red
        }
        else {
            return $value.Trim()
        }
    }
}

function Prompt-YesNo {
    param(
        [string]$Question,
        [bool]$Default = $false
    )
    
    $defaultText = if ($Default) { "[Y/n]" } else { "[y/N]" }
    $response = Read-Host "  $Question $defaultText"
    if ([string]::IsNullOrWhiteSpace($response)) { return $Default }
    return $response.Trim().ToLower().StartsWith("y")
}

# ─────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────
Write-Banner
$config = Load-Config

if ($ShowCurrent) {
    Show-Config $config
    exit 0
}

# Show current config first
if (Test-Path $configFile) {
    Write-Host "  Found existing configuration:" -ForegroundColor Green
    Show-Config $config
}

# ─────────────────────────────────────────────
# ENTRA ID SETUP
# ─────────────────────────────────────────────
if (-not $SkipEntra) {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Magenta
    Write-Host "  SECTION 1: Microsoft Entra ID (Azure AD) Authentication" -ForegroundColor Magenta
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Magenta
    Write-Host ""
    Write-Host "  Entra ID lets users sign in with their Microsoft account" -ForegroundColor Gray
    Write-Host "  before accessing Platypus Remote. You need to create a" -ForegroundColor Gray
    Write-Host "  free App Registration in the Azure Portal first." -ForegroundColor Gray
    Write-Host ""
    Write-Host "  If you haven't created one yet, follow these steps:" -ForegroundColor Gray
    Write-Host "    1. Go to https://portal.azure.com" -ForegroundColor White
    Write-Host "    2. Search for 'App registrations' → click '+ New registration'" -ForegroundColor White
    Write-Host "    3. Name: 'Platypus Remote'" -ForegroundColor White
    Write-Host "    4. Account type: 'Accounts in any org + personal accounts'" -ForegroundColor White
    Write-Host "    5. Click 'Register'" -ForegroundColor White
    Write-Host "    6. You'll see your Client ID and Tenant ID on the Overview page" -ForegroundColor White
    Write-Host ""
    
    $setupEntra = Prompt-YesNo "Configure Entra ID authentication?" -Default $true
    
    if ($setupEntra) {
        $config.ClientId = Prompt-Value `
            -Label "Client ID (Required)" `
            -Description "The unique identifier for your app registration." `
            -WhereToFind "Azure Portal → App registrations → your app → Overview → 'Application (client) ID'" `
            -Example "12345678-abcd-1234-abcd-123456789abc" `
            -Default $config.ClientId `
            -Required $true

        $config.TenantId = Prompt-Value `
            -Label "Tenant ID" `
            -Description "Your Azure AD directory ID. Use 'common' to allow ANY Microsoft account." `
            -WhereToFind "Azure Portal → App registrations → your app → Overview → 'Directory (tenant) ID'" `
            -Example "87654321-dcba-4321-dcba-987654321cba  (or just: common)" `
            -Default $(if ($config.TenantId) { $config.TenantId } else { "common" })

        $config.ApiScopeId = Prompt-Value `
            -Label "API Scope ID (Optional — usually leave blank)" `
            -Description "The Application ID URI from 'Expose an API'. Leave blank to auto-use your Client ID." `
            -WhereToFind "Azure Portal → your app → Expose an API → 'Application ID URI' (usually api://<client-id>)" `
            -Example "Leave blank, or: 12345678-abcd-1234-abcd-123456789abc" `
            -Default $config.ApiScopeId

        $config.GraphClientId = Prompt-Value `
            -Label "Graph Client ID (Optional — advanced, usually leave blank)" `
            -Description "Only needed if you have a SEPARATE app registration for Microsoft Graph API calls. The default is fine for most users." `
            -WhereToFind "Only if you created a second app registration for Graph API" `
            -Default $config.GraphClientId

        $config.EntraIdAllowedEmails = Prompt-Value `
            -Label "Allowed Emails (Optional)" `
            -Description "Restrict access to specific email addresses or domains. Leave blank to allow all." `
            -Example "user@company.com,@company.com" `
            -Default $config.EntraIdAllowedEmails
        
        $config.EntraIdAuthEnabled = Prompt-YesNo "Enable Entra ID authentication now?" -Default $true

        Write-Host ""
        Write-Host "  ─── Reminder: Azure Portal Setup Checklist ───" -ForegroundColor Yellow
        Write-Host "  [ ] Authentication → Add platform → Single-page application" -ForegroundColor White
        Write-Host "      Redirect URI: https://localhost:47392/authentication/login-callback" -ForegroundColor Gray
        Write-Host "  [ ] Expose an API → Set Application ID URI → Add scope 'access_as_user'" -ForegroundColor White
        Write-Host "  [ ] API permissions → Add Microsoft Graph: openid, profile, email" -ForegroundColor White
        Write-Host ""
    }
}

# ─────────────────────────────────────────────
# CLOUDFLARE ZERO TRUST SETUP
# ─────────────────────────────────────────────
if (-not $SkipCloudflare) {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Blue
    Write-Host "  SECTION 2: Cloudflare Zero Trust Authentication" -ForegroundColor Blue
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Blue
    Write-Host ""
    Write-Host "  Cloudflare Zero Trust puts a login page in front of your" -ForegroundColor Gray
    Write-Host "  Platypus Remote server. Users must authenticate through" -ForegroundColor Gray
    Write-Host "  Cloudflare before they can reach your server at all." -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Requirements:" -ForegroundColor Gray
    Write-Host "    - A Cloudflare account (free)" -ForegroundColor White
    Write-Host "    - A Cloudflare Tunnel running (cloudflared)" -ForegroundColor White
    Write-Host "    - An Access Application configured in the Zero Trust dashboard" -ForegroundColor White
    Write-Host ""
    Write-Host "  If you haven't set this up yet:" -ForegroundColor Gray
    Write-Host "    1. Go to https://one.dash.cloudflare.com" -ForegroundColor White
    Write-Host "    2. Create a team (e.g. 'platypus') → this is your Team Domain" -ForegroundColor White
    Write-Host "    3. Go to Access → Applications → Add application → Self-hosted" -ForegroundColor White
    Write-Host "    4. Set domain to your tunnel URL, create an Allow policy" -ForegroundColor White
    Write-Host "    5. After creation, copy the 'Application Audience (AUD) Tag'" -ForegroundColor White
    Write-Host ""
    
    $setupCf = Prompt-YesNo "Configure Cloudflare Zero Trust?" -Default $true
    
    if ($setupCf) {
        $config.CloudflareTeamDomain = Prompt-Value `
            -Label "Team Domain (Required)" `
            -Description "Your Cloudflare Zero Trust team name. Just the subdomain part, NOT the full URL." `
            -WhereToFind "one.dash.cloudflare.com → Settings → Custom Pages, or the URL: <team>.cloudflareaccess.com" `
            -Example "platypus  (for platypus.cloudflareaccess.com)" `
            -Default $config.CloudflareTeamDomain `
            -Required $true

        $config.CloudflareAudience = Prompt-Value `
            -Label "Application Audience (AUD) Tag (Required)" `
            -Description "The unique audience tag for your Cloudflare Access application. It's a long hex string." `
            -WhereToFind "one.dash.cloudflare.com → Access → Applications → your app → Overview → 'Application Audience (AUD) Tag'" `
            -Example "32a4f8c7e1b2d3a4f5c6e7b8a9d0c1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8" `
            -Default $config.CloudflareAudience `
            -Required $true

        $config.CloudflareAllowedEmails = Prompt-Value `
            -Label "Allowed Emails (Optional)" `
            -Description "Extra email restriction ON TOP of your Cloudflare Access policies. Leave blank to rely on CF policies only." `
            -Example "user@company.com,@company.com" `
            -Default $config.CloudflareAllowedEmails

        $config.CloudflareZeroTrustEnabled = Prompt-YesNo "Enable Cloudflare Zero Trust authentication now?" -Default $true
    }
    
    # Offer to install cloudflared
    Write-Host ""
    $installCf = Prompt-YesNo "Would you like to install/check cloudflared (tunnel program)?" -Default $false
    
    if ($installCf) {
        $cfDir = Join-Path $env:LOCALAPPDATA "PlatypusTools\cloudflared"
        $cfExe = Join-Path $cfDir "cloudflared.exe"
        
        if (Test-Path $cfExe) {
            Write-Host "  ✅ cloudflared already installed at: $cfExe" -ForegroundColor Green
            & $cfExe --version
        }
        else {
            Write-Host "  Downloading cloudflared..." -ForegroundColor Yellow
            if (-not (Test-Path $cfDir)) {
                New-Item -ItemType Directory -Path $cfDir -Force | Out-Null
            }
            
            $url = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
            try {
                Invoke-WebRequest -Uri $url -OutFile $cfExe -UseBasicParsing
                Write-Host "  ✅ cloudflared installed to: $cfExe" -ForegroundColor Green
                & $cfExe --version
            }
            catch {
                Write-Host "  ❌ Download failed: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "  Download manually from: https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/installation/" -ForegroundColor Yellow
            }
        }
        
        Write-Host ""
        $authCf = Prompt-YesNo "Authenticate cloudflared with your Cloudflare account? (opens browser)" -Default $false
        if ($authCf) {
            Write-Host "  Opening browser for Cloudflare login..." -ForegroundColor Yellow
            & $cfExe tunnel login
            Write-Host "  ✅ Authentication complete. Certificate saved." -ForegroundColor Green
        }
    }
}

# ─────────────────────────────────────────────
# SAVE
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  SUMMARY" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green

Show-Config $config

$doSave = Prompt-YesNo "Save this configuration?" -Default $true

if ($doSave) {
    Save-Config $config
    
    Write-Host ""
    Write-Host "  ╔═══════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "  ║  Setup Complete!                                     ║" -ForegroundColor Green
    Write-Host "  ╚═══════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Next Steps:" -ForegroundColor Yellow
    Write-Host "  1. Start (or restart) PlatypusTools" -ForegroundColor White
    Write-Host "  2. Go to Settings → Remote Server → Start Remote Server" -ForegroundColor White
    Write-Host "  3. Open your tunnel URL or localhost:47392 in a browser" -ForegroundColor White
    Write-Host ""
    
    if ($config.EntraIdAuthEnabled) {
        Write-Host "  Entra ID Checklist (in Azure Portal):" -ForegroundColor Yellow
        Write-Host "  [ ] Authentication → redirect URI: https://localhost:47392/authentication/login-callback" -ForegroundColor White
        Write-Host "  [ ] Expose an API → scope: access_as_user" -ForegroundColor White
        Write-Host "  [ ] API permissions: openid, profile, email" -ForegroundColor White
        Write-Host ""
    }
    
    if ($config.CloudflareZeroTrustEnabled) {
        Write-Host "  Cloudflare Checklist:" -ForegroundColor Yellow
        Write-Host "  [ ] Cloudflare Tunnel (cloudflared) is running" -ForegroundColor White
        Write-Host "  [ ] Access Application exists for your tunnel domain" -ForegroundColor White
        Write-Host "  [ ] Access Policy allows your users" -ForegroundColor White
        Write-Host ""
    }
}
else {
    Write-Host "  Configuration NOT saved." -ForegroundColor Yellow
}

Write-Host ""
