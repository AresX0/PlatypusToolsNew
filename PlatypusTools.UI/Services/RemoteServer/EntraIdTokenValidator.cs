using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Validates Entra ID (Azure AD) bearer tokens for direct authentication.
/// The webapp uses MSAL.js to acquire tokens, then sends them as Authorization: Bearer headers.
/// The server validates them against the Entra ID OIDC metadata endpoint.
/// </summary>
public class EntraIdTokenValidator
{
    private readonly string _clientId;
    private readonly string _tenantId;
    private readonly string _apiScopeId;
    private readonly HashSet<string> _allowedEmails;
    private ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private readonly SemaphoreSlim _configLock = new(1, 1);

    /// <summary>
    /// Gets whether Entra ID authentication is enabled.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets the MSAL configuration needed by the webapp to initiate login.
    /// </summary>
    public string ClientId => _clientId;
    public string TenantId => _tenantId;
    public string ApiScopeId => _apiScopeId;

    public EntraIdTokenValidator(bool enabled, string clientId, string tenantId, string apiScopeId, string? allowedEmails = null)
    {
        _clientId = clientId?.Trim() ?? "";
        _tenantId = string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId.Trim();
        _apiScopeId = string.IsNullOrWhiteSpace(apiScopeId) ? _clientId : apiScopeId.Trim();
        IsEnabled = enabled && !string.IsNullOrEmpty(_clientId);

        _allowedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(allowedEmails))
        {
            foreach (var entry in allowedEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _allowedEmails.Add(entry);
            }
        }
    }

    /// <summary>
    /// Validates an Entra ID access token (Bearer token from MSAL.js).
    /// </summary>
    public async Task<EntraIdAuthResult> ValidateTokenAsync(string? accessToken)
    {
        if (!IsEnabled)
            return EntraIdAuthResult.Success("anonymous@local", "Entra ID auth not enabled");

        if (string.IsNullOrWhiteSpace(accessToken))
            return EntraIdAuthResult.Failure("Missing Authorization Bearer token");

        try
        {
            var config = await GetOidcConfigAsync();
            if (config == null)
                return EntraIdAuthResult.Failure("Failed to fetch Entra ID OIDC configuration");

            // Build audience list: the app URI (api://{clientId}) and the raw client ID
            var validAudiences = new List<string> { _clientId };
            if (!string.IsNullOrEmpty(_apiScopeId) && _apiScopeId != _clientId)
            {
                validAudiences.Add(_apiScopeId);
                validAudiences.Add($"api://{_apiScopeId}");
            }
            validAudiences.Add($"api://{_clientId}");

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[]
                {
                    $"https://login.microsoftonline.com/{_tenantId}/v2.0",
                    $"https://sts.windows.net/{_tenantId}/",
                    // For multi-tenant apps
                    "https://login.microsoftonline.com/common/v2.0",
                    $"https://login.microsoftonline.com/{_tenantId}/"
                },
                ValidateAudience = true,
                ValidAudiences = validAudiences,
                ValidateLifetime = true,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            // For multi-tenant, skip strict issuer validation
            if (_tenantId.Equals("common", StringComparison.OrdinalIgnoreCase)
                || _tenantId.Equals("organizations", StringComparison.OrdinalIgnoreCase))
            {
                validationParams.ValidateIssuer = false;
            }

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(accessToken, validationParams, out var validatedToken);

            // Extract user info from claims
            var email = principal.FindFirst("email")?.Value
                     ?? principal.FindFirst("preferred_username")?.Value
                     ?? principal.FindFirst("upn")?.Value
                     ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                     ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value;

            var name = principal.FindFirst("name")?.Value
                    ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(email))
                return EntraIdAuthResult.Failure("Token valid but missing email/UPN claim");

            // Check email allowlist if configured
            if (_allowedEmails.Count > 0 && !IsEmailAllowed(email))
                return EntraIdAuthResult.Failure($"User {email} not in allowed list");

            // Extract authentication context
            var acrs = principal.FindFirst("acrs")?.Value;
            var tid = principal.FindFirst("tid")?.Value;
            var oid = principal.FindFirst("oid")?.Value;

            return EntraIdAuthResult.Success(email, "Token validated", new Dictionary<string, string?>
            {
                ["name"] = name,
                ["tid"] = tid,
                ["oid"] = oid,
                ["acrs"] = acrs,
                ["sub"] = principal.FindFirst("sub")?.Value
            });
        }
        catch (SecurityTokenExpiredException)
        {
            return EntraIdAuthResult.Failure("Access token expired — please sign in again");
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return EntraIdAuthResult.Failure("Token audience mismatch — check API Scope ID in settings");
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return EntraIdAuthResult.Failure("Token issuer mismatch — check Tenant ID in settings");
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Force refresh keys and retry
            _configManager = null;
            return EntraIdAuthResult.Failure("Signing key not found — keys may have rotated, please retry");
        }
        catch (Exception ex)
        {
            return EntraIdAuthResult.Failure($"Token validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a token and checks for a required authentication context (acrs) claim.
    /// Used for step-up / conditional access enforcement.
    /// </summary>
    public async Task<EntraIdAuthResult> ValidateWithAuthContextAsync(string? accessToken, string requiredAuthContext)
    {
        var result = await ValidateTokenAsync(accessToken);
        if (!result.IsAuthenticated)
            return result;

        if (string.IsNullOrEmpty(requiredAuthContext))
            return result;

        var acrs = result.Claims?.GetValueOrDefault("acrs");
        if (acrs != requiredAuthContext)
        {
            return EntraIdAuthResult.InsufficientClaims(result.Email ?? "", requiredAuthContext);
        }

        return result;
    }

    private bool IsEmailAllowed(string email)
    {
        if (_allowedEmails.Contains(email))
            return true;

        var domain = "@" + email.Split('@').LastOrDefault();
        return _allowedEmails.Contains(domain);
    }

    private async Task<OpenIdConnectConfiguration?> GetOidcConfigAsync()
    {
        if (_configManager != null)
        {
            try
            {
                return await _configManager.GetConfigurationAsync();
            }
            catch
            {
                // Fall through to recreate
            }
        }

        await _configLock.WaitAsync();
        try
        {
            if (_configManager != null)
            {
                try { return await _configManager.GetConfigurationAsync(); }
                catch { /* recreate below */ }
            }

            var metadataUrl = $"https://login.microsoftonline.com/{_tenantId}/v2.0/.well-known/openid-configuration";
#pragma warning disable CA2000 // ConfigurationManager takes ownership of HttpClient
            var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
#pragma warning restore CA2000
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataUrl,
                new OpenIdConnectConfigurationRetriever(),
                httpClient);

            return await _configManager.GetConfigurationAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching Entra ID OIDC config: {ex.Message}");
            return null;
        }
        finally
        {
            _configLock.Release();
        }
    }
}

/// <summary>
/// Result of an Entra ID token validation.
/// </summary>
public class EntraIdAuthResult
{
    public bool IsAuthenticated { get; init; }
    public string? Email { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, string?>? Claims { get; init; }
    public bool RequiresStepUp { get; init; }
    public string? RequiredAuthContext { get; init; }

    public static EntraIdAuthResult Success(string email, string? message = null, Dictionary<string, string?>? claims = null)
        => new() { IsAuthenticated = true, Email = email, ErrorMessage = message, Claims = claims };

    public static EntraIdAuthResult Failure(string error)
        => new() { IsAuthenticated = false, ErrorMessage = error };

    public static EntraIdAuthResult InsufficientClaims(string email, string requiredContext)
        => new()
        {
            IsAuthenticated = false,
            Email = email,
            ErrorMessage = $"Insufficient authentication context. Required: {requiredContext}",
            RequiresStepUp = true,
            RequiredAuthContext = requiredContext
        };
}
