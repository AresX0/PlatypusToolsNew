using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Driver for "Sign in with Microsoft" using the loopback PKCE flow.
    /// Talks to the Platytalk relay's <c>/v1/auth/native/start</c> and
    /// <c>/v1/auth/native/exchange</c> endpoints — the relay holds the
    /// Entra app secret, this client only needs the redirect URI.
    /// </summary>
    public static class MicrosoftSignIn
    {
        private static readonly int[] CandidatePorts = { 53682, 53683, 53684, 53685 };

        public sealed class SignInResult
        {
            public string Token { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string Handle { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
        }

        private sealed class StartResponse
        {
            [JsonPropertyName("authUrl")] public string AuthUrl { get; set; } = string.Empty;
            [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
        }

        private sealed class ExchangeResponse
        {
            [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
            [JsonPropertyName("user")] public ExchangeUser User { get; set; } = new();
        }

        private sealed class ExchangeUser
        {
            [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
            [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
            [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
        }

        /// <summary>
        /// Runs the full sign-in flow: opens a loopback HTTP listener, asks
        /// the relay for an auth URL, opens the user's browser, awaits the
        /// callback, then exchanges the code with the relay for a JWT.
        /// </summary>
        /// <param name="relayBaseUrl">e.g. https://platytalk.platysoft.com</param>
        /// <param name="openBrowser">Optional override for opening the URL.</param>
        public static async Task<SignInResult> SignInAsync(
            string relayBaseUrl,
            Action<string>? openBrowser = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(relayBaseUrl))
                throw new ArgumentException("relayBaseUrl is required", nameof(relayBaseUrl));

            // Enforce HTTPS to avoid leaking the JWT. http://localhost is allowed for self-host dev.
            if (!Uri.TryCreate(relayBaseUrl, UriKind.Absolute, out var relayUri))
                throw new ArgumentException("relayBaseUrl must be an absolute URL", nameof(relayBaseUrl));
            var isLocalhost = relayUri.IsLoopback;
            if (relayUri.Scheme != Uri.UriSchemeHttps && !(relayUri.Scheme == Uri.UriSchemeHttp && isLocalhost))
                throw new ArgumentException("relayBaseUrl must use https (http only allowed for loopback)", nameof(relayBaseUrl));

            var (listener, redirectUri) = StartListener();
            try
            {
                using var http = new HttpClient { BaseAddress = relayUri, Timeout = TimeSpan.FromSeconds(30) };
                var startResp = await http.PostAsJsonAsync("/v1/auth/native/start",
                    new { redirectUri }, ct).ConfigureAwait(false);
                if (!startResp.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"Sign-in start failed: {(int)startResp.StatusCode} {startResp.ReasonPhrase}");

                var start = await startResp.Content.ReadFromJsonAsync<StartResponse>(cancellationToken: ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Sign-in start returned empty body.");

                // Validate the relay handed us a real Microsoft authorize URL before we open a browser.
                if (!Uri.TryCreate(start.AuthUrl, UriKind.Absolute, out var authUri) ||
                    authUri.Scheme != Uri.UriSchemeHttps ||
                    !(authUri.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
                      authUri.Host.Equals("login.live.com", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Relay returned an unexpected auth URL.");

                (openBrowser ?? OpenBrowser)(authUri.AbsoluteUri);

                var (code, returnedState) = await WaitForCallbackAsync(listener, ct).ConfigureAwait(false);
                if (!string.Equals(returnedState, start.State, StringComparison.Ordinal))
                    throw new InvalidOperationException("OAuth state mismatch.");

                var exchangeResp = await http.PostAsJsonAsync("/v1/auth/native/exchange",
                    new { code, state = start.State, redirectUri }, ct).ConfigureAwait(false);
                if (!exchangeResp.IsSuccessStatusCode)
                {
                    // Don't echo response body — may contain auth-server hints / PII.
                    throw new InvalidOperationException(
                        $"Sign-in exchange failed: {(int)exchangeResp.StatusCode} {exchangeResp.ReasonPhrase}");
                }

                var ex = await exchangeResp.Content.ReadFromJsonAsync<ExchangeResponse>(cancellationToken: ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Sign-in exchange returned empty body.");

                return new SignInResult
                {
                    Token = ex.Token,
                    UserId = ex.User.Id,
                    Handle = ex.User.Handle,
                    DisplayName = ex.User.DisplayName,
                };
            }
            finally
            {
                try { listener.Stop(); } catch { }
                try { listener.Close(); } catch { }
            }
        }

        private static (HttpListener listener, string redirectUri) StartListener()
        {
            Exception? last = null;
            foreach (var port in CandidatePorts)
            {
                var prefix = $"http://localhost:{port}/";
                var l = new HttpListener();
                l.Prefixes.Add(prefix);
                try
                {
                    l.Start();
                    return (l, prefix);
                }
                catch (Exception ex)
                {
                    last = ex;
                    try { l.Close(); } catch { }
                }
            }
            throw new InvalidOperationException(
                "Could not bind any loopback port for Microsoft sign-in. " +
                "Ensure ports 53682-53685 are not in use.", last);
        }

        private static async Task<(string code, string state)> WaitForCallbackAsync(
            HttpListener listener, CancellationToken ct)
        {
            using var reg = ct.Register(() => { try { listener.Stop(); } catch { } });

            // Loop until we see the OAuth callback. Browsers often probe localhost
            // for /favicon.ico, /.well-known/*, etc. before the redirect arrives,
            // and other local processes may also touch the port.
            for (var i = 0; i < 50; i++)
            {
                ct.ThrowIfCancellationRequested();
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (HttpListenerException) { throw new OperationCanceledException("Sign-in cancelled."); }
                catch (ObjectDisposedException) { throw new OperationCanceledException("Sign-in cancelled."); }

                var query = ctx.Request.QueryString;
                var code = query["code"];
                var state = query["state"];
                var error = query["error"];

                if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error))
                {
                    // Not the OAuth callback — quietly 204 and keep waiting.
                    ctx.Response.StatusCode = 204;
                    try { ctx.Response.Close(); } catch { }
                    continue;
                }

                string body;
                if (!string.IsNullOrEmpty(error))
                {
                    body = "<!doctype html><html><body style=\"font-family:sans-serif;background:#04070d;color:#ff5577;text-align:center;padding-top:4rem\">" +
                           $"<h1>Sign-in failed</h1><p>{WebUtility.HtmlEncode(error)}</p>" +
                           "<p>You can close this tab.</p></body></html>";
                }
                else
                {
                    body = "<!doctype html><html><body style=\"font-family:sans-serif;background:#04070d;color:#00e5ff;text-align:center;padding-top:4rem\">" +
                           "<h1>Signed in</h1><p>You can close this tab and return to PlatypusTools.</p>" +
                           "<script>setTimeout(()=>window.close(),1500)</script></body></html>";
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                try { await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false); } catch { }
                try { ctx.Response.Close(); } catch { }

                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException("Sign-in returned error: " + error);
                if (string.IsNullOrEmpty(state))
                    throw new InvalidOperationException("Sign-in callback missing state.");

                return (code!, state!);
            }

            throw new InvalidOperationException("Sign-in callback was never received.");
        }

        private static void OpenBrowser(string url)
        {
            // Defense in depth: caller already validated the URL, but reject anything
            // that isn't an https URL here too — never invoke a shell on raw input.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                return;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // UseShellExecute=true lets the OS resolve the default browser without
                    // shell quoting — no command-injection surface.
                    Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Pass URL via argv to avoid `open --` flag injection if URL ever began with '-'.
                    Process.Start(new ProcessStartInfo("open") { ArgumentList = { "--", u.AbsoluteUri }, UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { u.AbsoluteUri }, UseShellExecute = false });
                }
            }
            catch
            {
                // Best-effort. Caller can still copy the URL from logs if needed.
            }
        }
    }
}
