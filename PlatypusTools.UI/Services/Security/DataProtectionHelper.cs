using System;
using System.Security.Cryptography;
using System.Text;

namespace PlatypusTools.UI.Services.Security
{
    /// <summary>
    /// Wraps Windows DPAPI (<see cref="ProtectedData"/>) for at-rest secrets
    /// like API keys. Ciphertext is base64; on a different user/machine the
    /// blob fails to decrypt and we return the original value unchanged so
    /// settings can still load (the user is then prompted to re-enter).
    ///
    /// Marker prefix `dpapi:v1:` lets us round-trip safely: values without it
    /// are treated as plaintext.
    /// </summary>
    public static class DataProtectionHelper
    {
        private const string Prefix = "dpapi:v1:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PlatypusTools.AI.Keys.v1");

        public static string Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return string.Empty;
            if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext; // already encrypted
            try
            {
                var data = Encoding.UTF8.GetBytes(plaintext);
                var cipher = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(cipher);
            }
            catch
            {
                // DPAPI not available (e.g. headless / wrong platform) — leave plaintext.
                return plaintext;
            }
        }

        public static string Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // legacy plaintext
            try
            {
                var cipher = Convert.FromBase64String(stored.Substring(Prefix.Length));
                var data = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                // Wrong user/machine — caller will see empty and re-prompt.
                return string.Empty;
            }
        }

        public static bool IsProtected(string? stored) =>
            !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
