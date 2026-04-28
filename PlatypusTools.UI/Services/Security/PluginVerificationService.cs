using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace PlatypusTools.UI.Services.Security
{
    /// <summary>
    /// Verifies plugin assemblies before they are loaded into the app domain.
    ///
    /// Modes (controlled by AppSettings.PluginVerificationMode):
    ///   • "Off"      — load anything (legacy behaviour).
    ///   • "Manifest" — require an entry in plugins/allowed-plugins.json
    ///                  matching the assembly's SHA-256 hash. Anything else
    ///                  is rejected.
    ///   • "Signed"   — require Authenticode signature (any cert) AND a
    ///                  manifest match. Strongest mode.
    ///
    /// Manifest schema:
    ///   { "plugins": [ { "fileName": "MyPlugin.dll", "sha256": "abcd..." } ] }
    /// </summary>
    public static class PluginVerificationService
    {
        public sealed class ManifestEntry
        {
            public string FileName { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public string? Description { get; set; }
        }

        public sealed class Manifest
        {
            public List<ManifestEntry> Plugins { get; set; } = new();
        }

        public sealed class VerificationResult
        {
            public bool Allowed { get; set; }
            public string Reason { get; set; } = "";
            public string Sha256 { get; set; } = "";
        }

        public static string ComputeSha256(string assemblyPath)
        {
            using var fs = File.OpenRead(assemblyPath);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static Manifest LoadManifest(string pluginsDir)
        {
            try
            {
                var path = Path.Combine(pluginsDir, "allowed-plugins.json");
                if (!File.Exists(path)) return new Manifest();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Manifest>(json) ?? new Manifest();
            }
            catch { return new Manifest(); }
        }

        public static VerificationResult Verify(string assemblyPath, string mode, string pluginsDir)
        {
            var result = new VerificationResult { Sha256 = "" };
            try
            {
                if (string.IsNullOrEmpty(mode) || string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
                {
                    result.Allowed = true;
                    result.Reason = "Verification disabled.";
                    return result;
                }

                var sha = ComputeSha256(assemblyPath);
                result.Sha256 = sha;

                var manifest = LoadManifest(pluginsDir);
                var fileName = Path.GetFileName(assemblyPath);
                var match = manifest.Plugins.FirstOrDefault(p =>
                    string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Sha256, sha, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    result.Allowed = false;
                    result.Reason = $"Hash {sha[..16]}… not in allowed-plugins.json. Add it to trust this plugin.";
                    return result;
                }

                if (string.Equals(mode, "Signed", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthenticodeSigned(assemblyPath))
                    {
                        result.Allowed = false;
                        result.Reason = "Strict mode requires Authenticode signature; assembly is unsigned.";
                        return result;
                    }
                }

                result.Allowed = true;
                result.Reason = "Verified against manifest.";
                return result;
            }
            catch (Exception ex)
            {
                result.Allowed = false;
                result.Reason = "Verification error: " + ex.Message;
                return result;
            }
        }

        private static bool IsAuthenticodeSigned(string path)
        {
            // X509Certificate.CreateFromSignedFile only succeeds on Authenticode-signed PE files.
            try
            {
                using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
                return !string.IsNullOrEmpty(cert.Subject);
            }
            catch { return false; }
        }

        /// <summary>
        /// Helper for adding a freshly-vetted plugin to the manifest. UI calls
        /// this from a "Trust this plugin" button after showing the user the
        /// hash + path.
        /// </summary>
        public static void TrustPlugin(string pluginsDir, string assemblyPath, string? description = null)
        {
            try
            {
                Directory.CreateDirectory(pluginsDir);
                var manifest = LoadManifest(pluginsDir);
                var fileName = Path.GetFileName(assemblyPath);
                var sha = ComputeSha256(assemblyPath);
                var existing = manifest.Plugins.FirstOrDefault(p =>
                    string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                if (existing != null) existing.Sha256 = sha;
                else manifest.Plugins.Add(new ManifestEntry { FileName = fileName, Sha256 = sha, Description = description });
                File.WriteAllText(
                    Path.Combine(pluginsDir, "allowed-plugins.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best-effort */ }
        }
    }
}
