<#
.SYNOPSIS
    Configure Platypus Remote with your Entra ID (Azure AD) app registration credentials.

.DESCRIPTION
    This script updates the configuration files for PlatypusTools.Remote.Server and 
    PlatypusTools.Remote.Client with your Azure AD application registration details.

.PARAMETER ClientId
    The Application (client) ID from your Azure AD app registration.

.PARAMETER TenantId
    The Directory (tenant) ID. Use "common" for multi-tenant or personal accounts.
    Default: "common"

.PARAMETER PublicUrl
    Optional public URL if using Cloudflare Tunnel or similar.

.EXAMPLE
    .\Configure-PlatypusRemote.ps1 -ClientId "12345678-abcd-1234-abcd-123456789abc"

.EXAMPLE
    .\Configure-PlatypusRemote.ps1 -ClientId "12345678-abcd-1234-abcd-123456789abc" -TenantId "87654321-dcba-4321-dcba-987654321cba"

.EXAMPLE
    .\Configure-PlatypusRemote.ps1 -ClientId "12345678-abcd-1234-abcd-123456789abc" -PublicUrl "https://platypus.yourdomain.com"

.NOTES
    See DOCS/ENTRA_ID_SETUP.md for full instructions on registering an Azure AD app.
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$ClientId,

    [Parameter(Mandatory = $false)]
    [string]$TenantId = "common",

    [Parameter(Mandatory = $false)]
    [string]$PublicUrl
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = if ($scriptDir -eq "") { Get-Location } else { $scriptDir }

# Paths to config files
$serverConfig = Join-Path $projectRoot "PlatypusTools.Remote.Server\appsettings.json"
$clientConfig = Join-Path $projectRoot "PlatypusTools.Remote.Client\wwwroot\appsettings.json"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Platypus Remote Configuration Setup" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Validate files exist
if (-not (Test-Path $serverConfig)) {
    throw "Server config not found: $serverConfig"
}
if (-not (Test-Path $clientConfig)) {
    throw "Client config not found: $clientConfig"
}

Write-Host "Configuration Values:" -ForegroundColor Yellow
Write-Host "  Client ID : $ClientId"
Write-Host "  Tenant ID : $TenantId"
if ($PublicUrl) {
    Write-Host "  Public URL: $PublicUrl"
}
Write-Host ""

# Update Server Configuration
Write-Host "Updating server configuration..." -ForegroundColor Green

$serverJson = Get-Content $serverConfig -Raw | ConvertFrom-Json

# Update AzureAd section
$serverJson.AzureAd.TenantId = $TenantId
$serverJson.AzureAd.ClientId = $ClientId
$serverJson.AzureAd.Audience = "api://$ClientId"

# Add public URL to allowed origins if provided
if ($PublicUrl) {
    $origins = @($serverJson.AllowedOrigins)
    if ($origins -notcontains $PublicUrl) {
        $origins += $PublicUrl
        $serverJson.AllowedOrigins = $origins
    }
}

$serverJson | ConvertTo-Json -Depth 10 | Set-Content $serverConfig -Encoding UTF8
Write-Host "  Updated: $serverConfig" -ForegroundColor Gray

# Update Client Configuration
Write-Host "Updating client configuration..." -ForegroundColor Green

$clientJson = Get-Content $clientConfig -Raw | ConvertFrom-Json

# Update AzureAd section
$clientJson.AzureAd.ClientId = $ClientId

# Update authority if not using common tenant
if ($TenantId -ne "common") {
    $clientJson.AzureAd.Authority = "https://login.microsoftonline.com/$TenantId"
}

# Update API base address if public URL provided
if ($PublicUrl) {
    $clientJson.ApiBaseAddress = $PublicUrl
}

$clientJson | ConvertTo-Json -Depth 10 | Set-Content $clientConfig -Encoding UTF8
Write-Host "  Updated: $clientConfig" -ForegroundColor Gray

# Also write the runtime entra-config.json for the installed app
Write-Host "Updating runtime Entra config..." -ForegroundColor Green

$entraConfigPaths = @(
    # AppData location (normal install)
    (Join-Path $env:APPDATA "PlatypusTools\entra-config.json"),
    # Portable mode location (next to exe, if publish folder exists)
    (Join-Path $projectRoot "publish\PlatypusData\entra-config.json")
)

$entraConfig = @{
    ClientId = $ClientId
    TenantId = $TenantId
    ApiScopeId = $ClientId
    GraphClientId = ""
}

$entraJson = $entraConfig | ConvertTo-Json -Depth 5

foreach ($entraPath in $entraConfigPaths) {
    try {
        $entraDir = Split-Path $entraPath -Parent
        if (-not (Test-Path $entraDir)) { New-Item -ItemType Directory -Path $entraDir -Force | Out-Null }
        Set-Content -Path $entraPath -Value $entraJson -Encoding UTF8
        Write-Host "  Updated: $entraPath" -ForegroundColor Gray
    }
    catch {
        Write-Host "  Skipped: $entraPath ($($_.Exception.Message))" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Configuration Complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Ensure your Azure AD app has these redirect URIs configured:" -ForegroundColor White
Write-Host "   - https://localhost:47392/authentication/login-callback"
if ($PublicUrl) {
    Write-Host "   - $PublicUrl/authentication/login-callback"
}
Write-Host ""
Write-Host "2. Ensure you've exposed the API scope:" -ForegroundColor White
Write-Host "   - api://$ClientId/access_as_user"
Write-Host ""
Write-Host "3. Start the server:" -ForegroundColor White
Write-Host "   dotnet run --project PlatypusTools.Remote.Server"
Write-Host ""
Write-Host "4. Start the client (in another terminal):" -ForegroundColor White
Write-Host "   dotnet run --project PlatypusTools.Remote.Client"
Write-Host ""
Write-Host "See DOCS/ENTRA_ID_SETUP.md for detailed instructions." -ForegroundColor Gray
