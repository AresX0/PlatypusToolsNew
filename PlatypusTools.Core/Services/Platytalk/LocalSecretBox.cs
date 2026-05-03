using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Wraps "encrypt-at-rest for small secrets" so the Platytalk identity
    /// (private keys + JWT) is never written as plaintext.
    /// <para/>
    /// On Windows: DPAPI (CurrentUser scope). On macOS/Linux: AES-256-GCM with
    /// a 32-byte random key stored in <c>{dataDir}/.localkey</c> at 0600
    /// permissions. The key file is best-effort protection — a co-resident
    /// attacker running as the same user can still read it. The threat
    /// model here is "another local user" and "stolen unattended drive",
    /// not "user account compromise".
    /// </summary>
    internal static class LocalSecretBox
    {
        private const string KeyFileName = ".localkey";

        public static byte[] Protect(byte[] plaintext, string dataDir)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ProtectDpapi(plaintext);
            return ProtectAesGcm(plaintext, dataDir);
        }

        public static byte[] Unprotect(byte[] ciphertext, string dataDir)
        {
            ArgumentNullException.ThrowIfNull(ciphertext);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return UnprotectDpapi(ciphertext);
            return UnprotectAesGcm(ciphertext, dataDir);
        }

        // ---- Windows DPAPI -------------------------------------------------

        [SupportedOSPlatform("windows")]
        private static byte[] ProtectDpapi(byte[] plaintext) =>
            ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        [SupportedOSPlatform("windows")]
        private static byte[] UnprotectDpapi(byte[] ciphertext) =>
            ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);

        // ---- POSIX AES-GCM -------------------------------------------------
        // Layout: [version=1 (1B)][nonce (12B)][tag (16B)][ciphertext]

        private static byte[] LoadOrCreateKey(string dataDir)
        {
            Directory.CreateDirectory(dataDir);
            var path = Path.Combine(dataDir, KeyFileName);
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == 32) return existing;
                // Corrupt or wrong-length — replace.
            }
            var key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(path, key);
            TryRestrictPermissions(path);
            return key;
        }

        private static void TryRestrictPermissions(string path)
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { /* best effort */ }
        }

        private static byte[] ProtectAesGcm(byte[] plaintext, string dataDir)
        {
            var key = LoadOrCreateKey(dataDir);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ct = new byte[plaintext.Length];
            using (var aes = new AesGcm(key, 16))
                aes.Encrypt(nonce, plaintext, ct, tag);

            var output = new byte[1 + 12 + 16 + ct.Length];
            output[0] = 1;
            Buffer.BlockCopy(nonce, 0, output, 1, 12);
            Buffer.BlockCopy(tag, 0, output, 13, 16);
            Buffer.BlockCopy(ct, 0, output, 29, ct.Length);
            CryptographicOperations.ZeroMemory(key);
            return output;
        }

        private static byte[] UnprotectAesGcm(byte[] blob, string dataDir)
        {
            if (blob.Length < 29 || blob[0] != 1)
                throw new CryptographicException("Encrypted blob has unsupported format.");
            var key = LoadOrCreateKey(dataDir);
            var nonce = new byte[12];
            var tag = new byte[16];
            Buffer.BlockCopy(blob, 1, nonce, 0, 12);
            Buffer.BlockCopy(blob, 13, tag, 0, 16);
            var ct = new byte[blob.Length - 29];
            Buffer.BlockCopy(blob, 29, ct, 0, ct.Length);
            var plain = new byte[ct.Length];
            using (var aes = new AesGcm(key, 16))
                aes.Decrypt(nonce, ct, tag, plain);
            CryptographicOperations.ZeroMemory(key);
            return plain;
        }
    }
}
