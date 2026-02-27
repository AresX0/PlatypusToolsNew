using System;
using System.Security.Cryptography;
using OtpNet;

namespace PlatypusTools.UI.Services.Vault
{
    /// <summary>
    /// Generates TOTP (Time-based One-Time Password) codes compatible with
    /// Google Authenticator, Microsoft Authenticator, Authy, etc.
    /// Implements RFC 6238.
    /// </summary>
    public static class TotpService
    {
        /// <summary>
        /// Generates a TOTP code for the given secret.
        /// </summary>
        /// <param name="base32Secret">Base32-encoded secret key.</param>
        /// <param name="digits">Number of digits (default 6).</param>
        /// <param name="period">Time step in seconds (default 30).</param>
        /// <param name="algorithm">Hash algorithm: SHA1, SHA256, SHA512.</param>
        /// <returns>The current TOTP code as a zero-padded string.</returns>
        public static string GenerateCode(string base32Secret, int digits = 6, int period = 30, string algorithm = "SHA1")
        {
            if (string.IsNullOrWhiteSpace(base32Secret))
                return string.Empty;

            try
            {
                var secretBytes = Base32Encoding.ToBytes(CleanBase32(base32Secret));
                var mode = algorithm?.ToUpperInvariant() switch
                {
                    "SHA256" => OtpHashMode.Sha256,
                    "SHA512" => OtpHashMode.Sha512,
                    _ => OtpHashMode.Sha1,
                };

                var totp = new Totp(secretBytes, step: period, mode: mode, totpSize: digits);
                return totp.ComputeTotp();
            }
            catch
            {
                return "ERROR";
            }
        }

        /// <summary>
        /// Gets the remaining seconds until the current TOTP code expires.
        /// </summary>
        public static int GetRemainingSeconds(int period = 30)
        {
            var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return period - (int)(epoch % period);
        }

        /// <summary>
        /// Validates a TOTP code against a secret (with time window tolerance).
        /// </summary>
        public static bool ValidateCode(string base32Secret, string code, int digits = 6, int period = 30, string algorithm = "SHA1")
        {
            if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
                return false;

            try
            {
                var secretBytes = Base32Encoding.ToBytes(CleanBase32(base32Secret));
                var mode = algorithm?.ToUpperInvariant() switch
                {
                    "SHA256" => OtpHashMode.Sha256,
                    "SHA512" => OtpHashMode.Sha512,
                    _ => OtpHashMode.Sha1,
                };

                var totp = new Totp(secretBytes, step: period, mode: mode, totpSize: digits);
                return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a new random TOTP secret (Base32 encoded).
        /// </summary>
        public static string GenerateSecret(int length = 20)
        {
            var secret = RandomNumberGenerator.GetBytes(length);
            return Base32Encoding.ToString(secret);
        }

        /// <summary>
        /// Generates an otpauth:// URI for QR code generation.
        /// </summary>
        public static string GenerateOtpAuthUri(string issuer, string accountName, string base32Secret,
            int digits = 6, int period = 30, string algorithm = "SHA1")
        {
            var encodedIssuer = Uri.EscapeDataString(issuer);
            var encodedAccount = Uri.EscapeDataString(accountName);
            var cleanSecret = CleanBase32(base32Secret);

            return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={cleanSecret}&issuer={encodedIssuer}&digits={digits}&period={period}&algorithm={algorithm}";
        }

        /// <summary>
        /// Parses an otpauth:// URI into an AuthenticatorEntry.
        /// </summary>
        public static AuthenticatorEntry? ParseOtpAuthUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("otpauth://totp/", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var parsed = new Uri(uri);
                var label = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
                var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);

                var entry = new AuthenticatorEntry
                {
                    Secret = query["secret"] ?? string.Empty,
                    Digits = int.TryParse(query["digits"], out var d) ? d : 6,
                    Period = int.TryParse(query["period"], out var p) ? p : 30,
                    Algorithm = query["algorithm"] ?? "SHA1",
                };

                // Parse label: "issuer:account" or just "account"
                if (label.Contains(':'))
                {
                    var parts = label.Split(':', 2);
                    entry.Issuer = parts[0].Trim();
                    entry.AccountName = parts[1].Trim();
                }
                else
                {
                    entry.AccountName = label;
                }

                // Issuer from query param takes precedence
                if (!string.IsNullOrEmpty(query["issuer"]))
                    entry.Issuer = query["issuer"];

                return entry;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Cleans a Base32 string (removes spaces, dashes, converts to uppercase).
        /// </summary>
        private static string CleanBase32(string input)
        {
            return input.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        }
    }
}
