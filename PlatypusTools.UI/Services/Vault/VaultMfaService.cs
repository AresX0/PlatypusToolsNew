using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Vault
{
    /// <summary>
    /// MFA configuration stored on disk alongside the vault.
    /// The TOTP secret is AES-encrypted with a key derived from the master password.
    /// </summary>
    public class VaultMfaConfig
    {
        public bool Enabled { get; set; }
        public MfaType Type { get; set; } = MfaType.None;
        public string? EncryptedTotpSecret { get; set; } // Base64 AES ciphertext
        public string? TotpSalt { get; set; }            // Base64 salt for key derivation
        public string? TotpIv { get; set; }              // Base64 IV for AES
        public bool WindowsHelloEnabled { get; set; }
        public string? WindowsHelloKeyId { get; set; }   // Credential name for Windows Hello
        public DateTime? SetupDate { get; set; }
    }

    /// <summary>
    /// Types of MFA supported by the vault.
    /// </summary>
    public enum MfaType
    {
        None = 0,
        Totp = 1,           // Authenticator app (Google Authenticator, Microsoft Authenticator, etc.)
        WindowsHello = 2,   // Windows Hello (PIN, biometrics, or FIDO2 security key)
        Both = 3            // Require both TOTP and Windows Hello
    }

    /// <summary>
    /// Manages Multi-Factor Authentication for the vault.
    /// Supports TOTP (authenticator apps) and Windows Hello (FIDO2 keys / biometrics).
    /// MFA is enforced when cloud sync is enabled to protect cross-device vault access.
    /// </summary>
    public class VaultMfaService
    {
        private const int SaltSize = 32;
        private const int KeySize = 32;
        private const int IvSize = 16;
        private const int KdfIterations = 600000;
        private const string MfaConfigFileName = "mfa_config.json";
        private const string WindowsHelloKeyName = "PlatypusVaultMFA";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        private string GetMfaConfigPath()
        {
            var dir = Path.Combine(SettingsManager.DataDirectory, "Vault");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, MfaConfigFileName);
        }

        #region Configuration

        /// <summary>
        /// Loads the MFA configuration from disk.
        /// </summary>
        public VaultMfaConfig LoadConfig()
        {
            var path = GetMfaConfigPath();
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<VaultMfaConfig>(json, JsonOptions) ?? new VaultMfaConfig();
                }
                catch
                {
                    return new VaultMfaConfig();
                }
            }
            return new VaultMfaConfig();
        }

        /// <summary>
        /// Saves the MFA configuration to disk.
        /// </summary>
        public void SaveConfig(VaultMfaConfig config)
        {
            var path = GetMfaConfigPath();
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        }

        /// <summary>
        /// Checks if MFA is enabled.
        /// </summary>
        public bool IsMfaEnabled()
        {
            var config = LoadConfig();
            return config.Enabled && config.Type != MfaType.None;
        }

        /// <summary>
        /// Gets the current MFA type.
        /// </summary>
        public MfaType GetMfaType()
        {
            var config = LoadConfig();
            return config.Enabled ? config.Type : MfaType.None;
        }

        /// <summary>
        /// Deletes MFA configuration (used when vault is reset).
        /// </summary>
        public void DeleteMfaConfig()
        {
            var path = GetMfaConfigPath();
            if (File.Exists(path))
                File.Delete(path);
        }

        #endregion

        #region TOTP Setup & Validation

        /// <summary>
        /// Sets up TOTP MFA. Generates a secret, encrypts it with the master password,
        /// and stores it. Returns the plaintext secret and otpauth:// URI for QR code display.
        /// The setup is NOT finalized until FinalizeTotpSetup is called with a valid code.
        /// </summary>
        public (string Secret, string OtpAuthUri) BeginTotpSetup(string masterPassword)
        {
            var secret = TotpService.GenerateSecret(20);
            var otpAuthUri = TotpService.GenerateOtpAuthUri(
                "PlatypusTools Vault", "vault-mfa", secret);

            // Encrypt the secret with the master password
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var key = DeriveKey(masterPassword, salt);
            var encrypted = EncryptAes(System.Text.Encoding.UTF8.GetBytes(secret), key, iv);

            // Save as pending (not yet finalized)
            var config = LoadConfig();
            config.EncryptedTotpSecret = Convert.ToBase64String(encrypted);
            config.TotpSalt = Convert.ToBase64String(salt);
            config.TotpIv = Convert.ToBase64String(iv);
            // Don't set Enabled=true yet — wait for verification
            SaveConfig(config);

            return (secret, otpAuthUri);
        }

        /// <summary>
        /// Finalizes TOTP setup after the user verifies a code from their authenticator app.
        /// </summary>
        public bool FinalizeTotpSetup(string masterPassword, string verificationCode)
        {
            var config = LoadConfig();
            if (string.IsNullOrEmpty(config.EncryptedTotpSecret))
                return false;

            // Decrypt the secret to validate the code
            var secret = DecryptTotpSecret(config, masterPassword);
            if (secret == null)
                return false;

            if (!TotpService.ValidateCode(secret, verificationCode))
                return false;

            // Activate TOTP MFA
            config.Enabled = true;
            config.Type = config.WindowsHelloEnabled ? MfaType.Both : MfaType.Totp;
            config.SetupDate = DateTime.UtcNow;
            SaveConfig(config);
            return true;
        }

        /// <summary>
        /// Validates a TOTP code during vault unlock.
        /// </summary>
        public bool ValidateTotpCode(string masterPassword, string code)
        {
            var config = LoadConfig();
            if (string.IsNullOrEmpty(config.EncryptedTotpSecret))
                return false;

            var secret = DecryptTotpSecret(config, masterPassword);
            if (secret == null)
                return false;

            return TotpService.ValidateCode(secret, code);
        }

        /// <summary>
        /// Removes TOTP MFA.
        /// </summary>
        public void RemoveTotp()
        {
            var config = LoadConfig();
            config.EncryptedTotpSecret = null;
            config.TotpSalt = null;
            config.TotpIv = null;

            if (config.WindowsHelloEnabled)
                config.Type = MfaType.WindowsHello;
            else
            {
                config.Type = MfaType.None;
                config.Enabled = false;
            }
            SaveConfig(config);
        }

        private string? DecryptTotpSecret(VaultMfaConfig config, string masterPassword)
        {
            try
            {
                if (string.IsNullOrEmpty(config.EncryptedTotpSecret) ||
                    string.IsNullOrEmpty(config.TotpSalt) ||
                    string.IsNullOrEmpty(config.TotpIv))
                    return null;

                var salt = Convert.FromBase64String(config.TotpSalt);
                var iv = Convert.FromBase64String(config.TotpIv);
                var ciphertext = Convert.FromBase64String(config.EncryptedTotpSecret);
                var key = DeriveKey(masterPassword, salt);
                var plaintext = DecryptAes(ciphertext, key, iv);
                return System.Text.Encoding.UTF8.GetString(plaintext);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Windows Hello / FIDO2

        /// <summary>
        /// Checks if Windows Hello is available on this device.
        /// </summary>
        public async Task<bool> IsWindowsHelloAvailableAsync()
        {
            try
            {
                var result = await global::Windows.Security.Credentials.KeyCredentialManager.IsSupportedAsync();
                return result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sets up Windows Hello / FIDO2 MFA. Creates a credential that can be verified
        /// with PIN, biometrics, or a FIDO2 security key.
        /// </summary>
        public async Task<bool> SetupWindowsHelloAsync()
        {
            try
            {
                var result = await global::Windows.Security.Credentials.KeyCredentialManager
                    .RequestCreateAsync(WindowsHelloKeyName,
                        global::Windows.Security.Credentials.KeyCredentialCreationOption.ReplaceExisting);

                if (result.Status == global::Windows.Security.Credentials.KeyCredentialStatus.Success)
                {
                    var config = LoadConfig();
                    config.WindowsHelloEnabled = true;
                    config.WindowsHelloKeyId = WindowsHelloKeyName;
                    config.Enabled = true;
                    config.Type = config.EncryptedTotpSecret != null ? MfaType.Both : MfaType.WindowsHello;
                    config.SetupDate ??= DateTime.UtcNow;
                    SaveConfig(config);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates Windows Hello / FIDO2 authentication.
        /// This triggers the Windows Hello prompt (PIN, fingerprint, face, or FIDO2 key).
        /// </summary>
        public async Task<bool> ValidateWindowsHelloAsync()
        {
            try
            {
                var result = await global::Windows.Security.Credentials.KeyCredentialManager
                    .OpenAsync(WindowsHelloKeyName);

                if (result.Status != global::Windows.Security.Credentials.KeyCredentialStatus.Success)
                    return false;

                var credential = result.Credential;

                // Create a challenge to sign
                var challenge = CryptographicBuffer.CreateFromByteArray(
                    RandomNumberGenerator.GetBytes(32));

                var signResult = await credential.RequestSignAsync(challenge);
                return signResult.Status == global::Windows.Security.Credentials.KeyCredentialStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes Windows Hello / FIDO2 MFA.
        /// </summary>
        public async Task RemoveWindowsHelloAsync()
        {
            try
            {
                await global::Windows.Security.Credentials.KeyCredentialManager.DeleteAsync(WindowsHelloKeyName);
            }
            catch { }

            var config = LoadConfig();
            config.WindowsHelloEnabled = false;
            config.WindowsHelloKeyId = null;

            if (config.EncryptedTotpSecret != null)
                config.Type = MfaType.Totp;
            else
            {
                config.Type = MfaType.None;
                config.Enabled = false;
            }
            SaveConfig(config);
        }

        #endregion

        #region Crypto Helpers

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, KdfIterations, HashAlgorithmName.SHA256, KeySize);
        }

        private static byte[] EncryptAes(byte[] plaintext, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        }

        private static byte[] DecryptAes(byte[] ciphertext, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        #endregion
    }

    /// <summary>
    /// Helper for WinRT IBuffer interop.
    /// </summary>
    internal static class CryptographicBuffer
    {
        public static global::Windows.Storage.Streams.IBuffer CreateFromByteArray(byte[] data)
        {
            using var writer = new global::Windows.Storage.Streams.DataWriter();
            writer.WriteBytes(data);
            return writer.DetachBuffer();
        }
    }
}
