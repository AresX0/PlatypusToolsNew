using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Validates Cloudflare Access JWT tokens for Zero Trust authentication.
/// Cloudflare Access injects a signed JWT in the Cf-Access-Jwt-Assertion header
/// when users authenticate through a Zero Trust Access policy.
/// Reference: https://developers.cloudflare.com/cloudflare-one/identity/authorization-cookie/validating-json/
/// </summary>
public class CloudflareZeroTrustValidator
{
    private readonly string _teamDomain;
    private readonly string _audience;
    private readonly HashSet<string> _allowedEmails;
    private JsonWebKeySet? _cachedJwks;
    private DateTime _jwksCachedAt;
    private static readonly TimeSpan JwksCacheDuration = TimeSpan.FromHours(1);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _jwksLock = new(1, 1);

    /// <summary>
    /// Gets whether Zero Trust validation is enabled.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets the last validated user email (from the JWT).
    /// </summary>
    public string? LastValidatedEmail { get; private set; }

    public CloudflareZeroTrustValidator(bool enabled, string teamDomain, string audience, string? allowedEmails = null)
    {
        _teamDomain = teamDomain?.Trim() ?? "";
        _audience = audience?.Trim() ?? "";
        IsEnabled = enabled && !string.IsNullOrEmpty(_teamDomain) && !string.IsNullOrEmpty(_audience);

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
    /// Validates a Cloudflare Access JWT token.
    /// Returns the authenticated user's email on success, null on failure.
    /// </summary>
    public async Task<CloudflareAuthResult> ValidateTokenAsync(string? jwtToken)
    {
        if (!IsEnabled)
            return CloudflareAuthResult.Success("anonymous@local", "Zero Trust not configured");

        if (string.IsNullOrWhiteSpace(jwtToken))
            return CloudflareAuthResult.Failure("Missing Cf-Access-Jwt-Assertion header");

        try
        {
            var jwks = await GetSigningKeysAsync();
            if (jwks == null)
                return CloudflareAuthResult.Failure("Failed to fetch Cloudflare Access signing keys");

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://{_teamDomain}.cloudflareaccess.com",
                ValidateAudience = true,
                ValidAudiences = new[] { _audience },
                ValidateLifetime = true,
                IssuerSigningKeys = jwks.GetSigningKeys(),
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(jwtToken, validationParams, out var validatedToken);

            // Extract email claim
            var email = principal.FindFirst("email")?.Value
                     ?? principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            if (string.IsNullOrEmpty(email))
                return CloudflareAuthResult.Failure("JWT valid but missing email claim");

            // Check email allowlist if configured
            if (_allowedEmails.Count > 0 && !IsEmailAllowed(email))
                return CloudflareAuthResult.Failure($"User {email} not in allowed list");

            // Extract authentication context claims
            var acrs = principal.FindFirst("acrs")?.Value;
            var country = principal.FindFirst("country")?.Value;
            var identityProvider = principal.FindFirst("idp")?.Value
                                ?? principal.FindFirst("identity_nonce")?.Value;

            LastValidatedEmail = email;

            return CloudflareAuthResult.Success(email, "Token validated", new Dictionary<string, string?>
            {
                ["country"] = country,
                ["idp"] = identityProvider,
                ["acrs"] = acrs,
                ["sub"] = principal.FindFirst("sub")?.Value
            });
        }
        catch (SecurityTokenExpiredException)
        {
            return CloudflareAuthResult.Failure("CF Access token expired - re-authenticate at Cloudflare");
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return CloudflareAuthResult.Failure("CF Access token audience mismatch - check Application AUD tag in settings");
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return CloudflareAuthResult.Failure("CF Access token issuer mismatch - check team domain in settings");
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Invalidate cached keys and retry once
            _cachedJwks = null;
            return CloudflareAuthResult.Failure("Signing key not found - try refreshing (keys may have rotated)");
        }
        catch (Exception ex)
        {
            return CloudflareAuthResult.Failure($"Token validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if an authentication context (acrs) value matches a required context.
    /// Used for step-up/conditional access enforcement per the CA auth context pattern.
    /// </summary>
    public async Task<CloudflareAuthResult> ValidateWithAuthContextAsync(string? jwtToken, string requiredAuthContext)
    {
        var result = await ValidateTokenAsync(jwtToken);
        if (!result.IsAuthenticated)
            return result;

        if (string.IsNullOrEmpty(requiredAuthContext))
            return result;

        var acrs = result.Claims?.GetValueOrDefault("acrs");
        if (acrs != requiredAuthContext)
        {
            return CloudflareAuthResult.InsufficientClaims(result.Email ?? "", requiredAuthContext);
        }

        return result;
    }

    private bool IsEmailAllowed(string email)
    {
        // Direct match
        if (_allowedEmails.Contains(email))
            return true;

        // Domain match (entries starting with @)
        var domain = "@" + email.Split('@').LastOrDefault();
        return _allowedEmails.Contains(domain);
    }

    private async Task<JsonWebKeySet?> GetSigningKeysAsync()
    {
        // Return cached keys if fresh
        if (_cachedJwks != null && DateTime.UtcNow - _jwksCachedAt < JwksCacheDuration)
            return _cachedJwks;

        await _jwksLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedJwks != null && DateTime.UtcNow - _jwksCachedAt < JwksCacheDuration)
                return _cachedJwks;

            var certsUrl = $"https://{_teamDomain}.cloudflareaccess.com/cdn-cgi/access/certs";
            var response = await HttpClient.GetStringAsync(certsUrl);

            // Cloudflare returns { keys: [...], public_cert: {...}, public_certs: [...] }
            // We need the keys array to build a JWKS
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("keys", out var keysElement))
            {
                var jwksJson = JsonSerializer.Serialize(new { keys = keysElement });
                _cachedJwks = new JsonWebKeySet(jwksJson);
                _jwksCachedAt = DateTime.UtcNow;
                return _cachedJwks;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching CF Access certs: {ex.Message}");
            return _cachedJwks; // Return stale cache if available
        }
        finally
        {
            _jwksLock.Release();
        }
    }
}

/// <summary>
/// Result of a Cloudflare Zero Trust JWT validation.
/// </summary>
public class CloudflareAuthResult
{
    public bool IsAuthenticated { get; init; }
    public string? Email { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, string?>? Claims { get; init; }

    /// <summary>
    /// If true, the user is authenticated but needs to satisfy an additional
    /// authentication context (step-up auth via Conditional Access).
    /// The client should redirect to re-authenticate with the required context.
    /// </summary>
    public bool RequiresStepUp { get; init; }
    public string? RequiredAuthContext { get; init; }

    public static CloudflareAuthResult Success(string email, string? message = null, Dictionary<string, string?>? claims = null)
        => new() { IsAuthenticated = true, Email = email, ErrorMessage = message, Claims = claims };

    public static CloudflareAuthResult Failure(string error)
        => new() { IsAuthenticated = false, ErrorMessage = error };

    public static CloudflareAuthResult InsufficientClaims(string email, string requiredContext)
        => new()
        {
            IsAuthenticated = false,
            Email = email,
            ErrorMessage = $"Insufficient authentication context. Required: {requiredContext}",
            RequiresStepUp = true,
            RequiredAuthContext = requiredContext
        };
}
