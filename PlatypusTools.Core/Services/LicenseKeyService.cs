using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for validating license keys.
    /// Keys are in format: XXXX-XXXX-XXXX-XXXX (16 alphanumeric chars)
    /// First 12 chars are payload, last 4 are checksum.
    /// </summary>
    public static class LicenseKeyService
    {
        // Valid characters for license keys (no confusing chars like O/0, I/1, L)
        // Stored as bytes to avoid plain string in decompiled code
        private static readonly byte[] _vc = { 
            0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x4A, 0x4B, 
            0x4D, 0x4E, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 
            0x58, 0x59, 0x5A, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 
        };
        
        private static string ValidChars => Encoding.ASCII.GetString(_vc);
        
        /// <summary>
        /// Validates a license key format and checksum.
        /// </summary>
        /// <param name="key">The license key to validate (with or without dashes)</param>
        /// <returns>True if the key is valid, false otherwise</returns>
        public static bool ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            // Normalize: remove dashes, uppercase
            var normalized = key.Replace("-", "").Replace(" ", "").ToUpperInvariant();

            // Must be exactly 16 characters
            if (normalized.Length != 16)
                return false;

            // All chars must be valid
            var vc = ValidChars;
            if (!normalized.All(c => vc.Contains(c)))
                return false;

            // Extract payload (first 12) and checksum (last 4)
            var payload = normalized.Substring(0, 12);
            var checksum = normalized.Substring(12, 4);

            // Verify checksum
            var expectedChecksum = ComputeChecksum(payload);
            return checksum == expectedChecksum;
        }

        /// <summary>
        /// Formats a raw key string into display format with dashes.
        /// </summary>
        public static string FormatKey(string rawKey)
        {
            var normalized = rawKey.Replace("-", "").Replace(" ", "").ToUpperInvariant();
            if (normalized.Length != 16)
                return rawKey;

            return $"{normalized[0..4]}-{normalized[4..8]}-{normalized[8..12]}-{normalized[12..16]}";
        }

        // Obfuscated secret components - stored as encoded bytes
        // These are XOR'd with a key to reveal the actual values at runtime
        private static readonly byte[] _k1 = { 0x17, 0x25 }; // XOR key
        private static readonly byte[] _s1 = { 0x47, 0x71 }; // "PT" ^ key -> P=0x50^0x17=0x47, T=0x54^0x25=0x71
        private static readonly byte[] _s2 = { 0x56, 0x61 }; // "AD" ^ key -> A=0x41^0x17=0x56, D=0x44^0x25=0x61
        private static readonly byte[] _s3 = { 0x44, 0x62 }; // "SG" ^ key -> S=0x53^0x17=0x44, G=0x47^0x25=0x62
        private static readonly byte[] _s4 = { 0x20, 0x66, 0x7F, 0x53, 0x3A, 0x69, 0x78, 0x46, 0x7C }; // "7Chv-Lock" ^ key (cycling)
        
        private static string DecodeSegment(byte[] encoded, byte[] key)
        {
            var result = new byte[encoded.Length];
            for (int i = 0; i < encoded.Length; i++)
                result[i] = (byte)(encoded[i] ^ key[i % key.Length]);
            return Encoding.UTF8.GetString(result);
        }

        /// <summary>
        /// Computes the 4-character checksum for a 12-character payload.
        /// Uses SHA256 with obfuscated salt components.
        /// </summary>
        private static string ComputeChecksum(string payload)
        {
            // Decode secret components at runtime
            var p1 = DecodeSegment(_s1, _k1);
            var p2 = DecodeSegment(_s2, _k1);
            var p3 = DecodeSegment(_s3, _k1);
            var sec = DecodeSegment(_s4, _k1);
            
            // Combine in specific order - pattern matches generator
            var sb = new StringBuilder(64);
            sb.Append(p1);
            sb.Append(payload);
            sb.Append(p2);
            sb.Append(sec);
            sb.Append(p3);
            
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            
            // Convert first bytes to valid chars
            var vc = ValidChars;
            var checksum = new StringBuilder(4);
            for (int i = 0; i < 4; i++)
            {
                checksum.Append(vc[hash[i] % vc.Length]);
            }
            return checksum.ToString();
        }

        /// <summary>
        /// Gets information about a license key (for display purposes).
        /// </summary>
        public static LicenseInfo GetKeyInfo(string key)
        {
            if (!ValidateKey(key))
            {
                return new LicenseInfo
                {
                    IsValid = false,
                    FormattedKey = FormatKey(key),
                    Feature = "Unknown"
                };
            }

            return new LicenseInfo
            {
                IsValid = true,
                FormattedKey = FormatKey(key),
                Feature = "AD Security Analyzer",
                ActivatedOn = DateTime.Now
            };
        }
    }

    /// <summary>
    /// Information about a validated license key.
    /// </summary>
    public class LicenseInfo
    {
        public bool IsValid { get; set; }
        public string FormattedKey { get; set; } = string.Empty;
        public string Feature { get; set; } = string.Empty;
        public DateTime? ActivatedOn { get; set; }
    }
}
