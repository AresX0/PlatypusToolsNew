using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlatypusTools.Core.Models.Mail;

namespace PlatypusTools.Core.Services.Mail
{
    /// <summary>
    /// OAuth2 provider configuration for a mail provider.
    /// </summary>
    public class OAuthProviderConfig
    {
        public required string AuthEndpoint { get; init; }
        public required string TokenEndpoint { get; init; }
        public string DefaultClientId { get; init; } = "";
        public required string Scopes { get; init; }
    }

    /// <summary>
    /// Result of an OAuth2 token exchange.
    /// </summary>
    public class OAuthTokenResult
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int ExpiresIn { get; set; }
    }

    /// <summary>
    /// Handles OAuth2 Authorization Code + PKCE flow for IMAP authentication.
    /// Opens a browser for user sign-in and captures the token via localhost redirect.
    /// Supports Microsoft (Exchange/Hotmail), Gmail, and Yahoo.
    /// </summary>
    public static class OAuthTokenService
    {
        private static readonly Dictionary<MailAccountType, OAuthProviderConfig> Providers = new()
        {
            [MailAccountType.Exchange] = new()
            {
                AuthEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                DefaultClientId = "9e5f94bc-e8a4-4e73-b8be-63364c29d753", // Thunderbird public client (IMAP/SMTP pre-authorized)
                Scopes = "https://outlook.office365.com/IMAP.AccessAsUser.All https://outlook.office365.com/SMTP.Send offline_access"
            },
            [MailAccountType.Hotmail] = new()
            {
                AuthEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize",
                TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                DefaultClientId = "9e5f94bc-e8a4-4e73-b8be-63364c29d753", // Thunderbird public client (IMAP/SMTP pre-authorized)
                Scopes = "https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send offline_access"
            },
            [MailAccountType.Gmail] = new()
            {
                AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                TokenEndpoint = "https://oauth2.googleapis.com/token",
                DefaultClientId = "", // User must register their own Google Cloud OAuth client
                Scopes = "https://mail.google.com/ openid email"
            },
            [MailAccountType.Yahoo] = new()
            {
                AuthEndpoint = "https://api.login.yahoo.com/oauth2/request_auth",
                TokenEndpoint = "https://api.login.yahoo.com/oauth2/get_token",
                DefaultClientId = "", // User must register their own Yahoo Developer app
                Scopes = "mail-r"
            }
        };

        /// <summary>Whether OAuth2 is supported for the given account type.</summary>
        public static bool SupportsOAuth(MailAccountType type) => Providers.ContainsKey(type);

        /// <summary>Whether the provider has a built-in Client ID (Microsoft does, Google/Yahoo don't).</summary>
        public static bool HasDefaultClientId(MailAccountType type) =>
            Providers.TryGetValue(type, out var config) && !string.IsNullOrEmpty(config.DefaultClientId);

        /// <summary>Gets the default Client ID for a provider, or empty string.</summary>
        public static string GetDefaultClientId(MailAccountType type) =>
            Providers.TryGetValue(type, out var config) ? config.DefaultClientId : "";

        /// <summary>
        /// Performs full OAuth2 authorization code + PKCE flow via system browser.
        /// Opens the login page, waits for redirect to localhost, exchanges code for tokens.
        /// </summary>
        public static async Task<OAuthTokenResult> AuthenticateAsync(
            MailAccountConfig account,
            Action<string>? statusCallback = null,
            CancellationToken ct = default)
        {
            if (!Providers.TryGetValue(account.AccountType, out var provider))
                throw new NotSupportedException($"OAuth is not supported for {account.AccountType}.");

            var clientId = !string.IsNullOrEmpty(account.ClientId) ? account.ClientId : provider.DefaultClientId;
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException(
                    $"OAuth Client ID is required for {account.AccountType}. " +
                    "Please enter a Client ID in Account Settings.");

            // Generate PKCE code verifier/challenge
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);

            // Generate state parameter for CSRF protection
            var state = GenerateCodeVerifier();

            // Find an available port and start listener
            var port = FindAvailablePort();
            var redirectUri = $"http://localhost:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            try
            {
                // Build authorization URL
                var authUrl = BuildAuthUrl(provider, clientId, redirectUri, codeChallenge, account.EmailAddress, state);

                // Open system browser
                statusCallback?.Invoke("Opening browser for sign-in...");
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                // Wait for the browser callback (2-minute timeout)
                statusCallback?.Invoke("Waiting for browser sign-in (2 min timeout)...");
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var context = await listener.GetContextAsync().WaitAsync(linkedCts.Token);

                var code = context.Request.QueryString["code"];
                var error = context.Request.QueryString["error"];
                var errorDesc = context.Request.QueryString["error_description"];
                var returnedState = context.Request.QueryString["state"];

                // Send HTML response to browser
                await SendBrowserResponse(context, error, errorDesc);

                if (!string.IsNullOrEmpty(error))
                    throw new Exception($"OAuth error: {error} — {errorDesc}");

                // Validate state parameter to prevent CSRF attacks
                if (returnedState != state)
                    throw new Exception("OAuth state mismatch — possible CSRF attack. Please try again.");

                if (string.IsNullOrEmpty(code))
                    throw new Exception("No authorization code was received from the provider.");

                // Exchange auth code for tokens
                statusCallback?.Invoke("Exchanging authorization code for tokens...");
                return await ExchangeCodeAsync(provider, clientId, code, redirectUri, codeVerifier, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("Sign-in timed out after 2 minutes. Please try again.");
            }
            finally
            {
                listener.Stop();
                listener.Close();
            }
        }

        /// <summary>
        /// Refreshes an expired access token using a refresh token.
        /// </summary>
        public static async Task<OAuthTokenResult> RefreshTokenAsync(
            MailAccountConfig account,
            string refreshToken,
            CancellationToken ct = default)
        {
            if (!Providers.TryGetValue(account.AccountType, out var provider))
                throw new NotSupportedException($"OAuth is not supported for {account.AccountType}.");

            var clientId = !string.IsNullOrEmpty(account.ClientId) ? account.ClientId : provider.DefaultClientId;

            using var httpClient = new HttpClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = provider.Scopes
            });

            var response = await httpClient.PostAsync(provider.TokenEndpoint, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Token refresh failed: {json}");

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);
            return new OAuthTokenResult
            {
                AccessToken = tokenResponse?.AccessToken ?? "",
                RefreshToken = tokenResponse?.RefreshToken ?? refreshToken, // keep old if not returned
                ExpiresIn = tokenResponse?.ExpiresIn ?? 3600
            };
        }

        #region Private Helpers

        private static string BuildAuthUrl(
            OAuthProviderConfig provider, string clientId,
            string redirectUri, string codeChallenge, string? loginHint, string state)
        {
            var sb = new StringBuilder(provider.AuthEndpoint);
            sb.Append("?client_id=").Append(Uri.EscapeDataString(clientId));
            sb.Append("&response_type=code");
            sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
            sb.Append("&scope=").Append(Uri.EscapeDataString(provider.Scopes));
            sb.Append("&code_challenge=").Append(codeChallenge);
            sb.Append("&code_challenge_method=S256");
            sb.Append("&state=").Append(Uri.EscapeDataString(state));
            sb.Append("&prompt=login");

            if (!string.IsNullOrEmpty(loginHint))
                sb.Append("&login_hint=").Append(Uri.EscapeDataString(loginHint));

            return sb.ToString();
        }

        private static async Task SendBrowserResponse(HttpListenerContext context, string? error, string? errorDesc)
        {
            var html = error == null
                ? "<html><body style='font-family:sans-serif;text-align:center;padding:60px'>" +
                  "<h2 style='color:#4CAF50'>&#10004; Sign-in successful!</h2>" +
                  "<p>You can close this tab and return to PlatypusTools.</p></body></html>"
                : "<html><body style='font-family:sans-serif;text-align:center;padding:60px'>" +
                  $"<h2 style='color:#F44336'>&#10008; Sign-in failed</h2>" +
                  $"<p>{System.Net.WebUtility.HtmlEncode(error)}: {System.Net.WebUtility.HtmlEncode(errorDesc ?? "")}</p></body></html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();
        }

        private static async Task<OAuthTokenResult> ExchangeCodeAsync(
            OAuthProviderConfig provider, string clientId, string code,
            string redirectUri, string codeVerifier, CancellationToken ct)
        {
            using var httpClient = new HttpClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            });

            var response = await httpClient.PostAsync(provider.TokenEndpoint, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Token exchange failed: {json}");

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);
            return new OAuthTokenResult
            {
                AccessToken = tokenResponse?.AccessToken ?? "",
                RefreshToken = tokenResponse?.RefreshToken ?? "",
                ExpiresIn = tokenResponse?.ExpiresIn ?? 3600
            };
        }

        private static int FindAvailablePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            return Convert.ToBase64String(hash)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        #endregion
    }
}
