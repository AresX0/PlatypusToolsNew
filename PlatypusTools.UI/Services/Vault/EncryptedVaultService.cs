using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Vault
{
    /// <summary>
    /// Core encryption service for the password vault.
    /// Uses AES-256-CBC with PBKDF2 key derivation and HMAC-SHA256 integrity verification.
    /// Mirrors Bitwarden's encryption approach.
    /// </summary>
    public class EncryptedVaultService
    {
        private const int SaltSize = 32;       // 256-bit salt
        private const int KeySize = 32;        // 256-bit key (AES-256)
        private const int IvSize = 16;         // 128-bit IV for AES-CBC
        private const int DefaultIterations = 600000; // OWASP 2023 PBKDF2 recommendation

        private byte[]? _masterKey;
        private byte[]? _macKey;
        private string? _vaultPath;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>
        /// Options for the outer EncryptedVaultFile JSON (consistent for read + write).
        /// Uses CamelCase so property names match between serialize and deserialize.
        /// PropertyNameCaseInsensitive handles legacy PascalCase vault files.
        /// </summary>
        private static readonly JsonSerializerOptions VaultFileJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Gets whether the vault is currently unlocked.
        /// </summary>
        public bool IsUnlocked => _masterKey != null;

        /// <summary>
        /// Gets the path to the vault file.
        /// </summary>
        public string VaultFilePath => _vaultPath ?? GetDefaultVaultPath();

        /// <summary>
        /// Gets the default vault file path.
        /// </summary>
        public static string GetDefaultVaultPath()
        {
            var dir = Path.Combine(SettingsManager.DataDirectory, "Vault");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "vault.encrypted");
        }

        /// <summary>
        /// Checks if a vault file exists at the default location.
        /// </summary>
        public static bool VaultExists()
        {
            return File.Exists(GetDefaultVaultPath());
        }

        /// <summary>
        /// Creates a new vault with the given master password.
        /// </summary>
        public async Task<VaultDatabase> CreateVaultAsync(string masterPassword)
        {
            if (string.IsNullOrWhiteSpace(masterPassword))
                throw new ArgumentException("Master password cannot be empty.");

            _vaultPath = GetDefaultVaultPath();

            var vault = new VaultDatabase();
            var salt = RandomNumberGenerator.GetBytes(SaltSize);

            DeriveKeys(masterPassword, salt, DefaultIterations);

            await SaveVaultInternalAsync(vault, salt, DefaultIterations);

            return vault;
        }

        /// <summary>
        /// Unlocks an existing vault with the master password.
        /// </summary>
        public async Task<VaultDatabase> UnlockVaultAsync(string masterPassword, string? vaultPath = null)
        {
            _vaultPath = vaultPath ?? GetDefaultVaultPath();

            if (!File.Exists(_vaultPath))
                throw new FileNotFoundException("Vault file not found.", _vaultPath);

            var json = await File.ReadAllTextAsync(_vaultPath);
            var encryptedFile = JsonSerializer.Deserialize<EncryptedVaultFile>(json, VaultFileJsonOptions)
                ?? throw new InvalidDataException("Invalid vault file format.");

            var salt = Convert.FromBase64String(encryptedFile.Salt);
            var iv = Convert.FromBase64String(encryptedFile.Iv);
            var ciphertext = Convert.FromBase64String(encryptedFile.Data);
            var storedHash = Convert.FromBase64String(encryptedFile.Hash);

            DeriveKeys(masterPassword, salt, encryptedFile.KdfIterations);

            // Verify HMAC integrity
            var computedHash = ComputeHmac(ciphertext);
            if (!CryptographicOperations.FixedTimeEquals(storedHash, computedHash))
            {
                Lock();
                throw new CryptographicException("Invalid master password or vault data corrupted.");
            }

            // Decrypt
            var plaintext = DecryptAes(ciphertext, iv);
            var vault = JsonSerializer.Deserialize<VaultDatabase>(plaintext, JsonOptions)
                ?? new VaultDatabase();

            return vault;
        }

        /// <summary>
        /// Saves the vault database to disk (must be unlocked).
        /// </summary>
        public async Task SaveVaultAsync(VaultDatabase vault)
        {
            if (_masterKey == null)
                throw new InvalidOperationException("Vault is locked. Unlock first.");

            _vaultPath ??= GetDefaultVaultPath();

            // Read existing file for salt/iterations, or generate new ones
            byte[] salt;
            int iterations;
            if (File.Exists(_vaultPath))
            {
                var existing = await File.ReadAllTextAsync(_vaultPath);
                var existingFile = JsonSerializer.Deserialize<EncryptedVaultFile>(existing, VaultFileJsonOptions);
                salt = Convert.FromBase64String(existingFile?.Salt ?? "");
                iterations = existingFile?.KdfIterations ?? DefaultIterations;
            }
            else
            {
                salt = RandomNumberGenerator.GetBytes(SaltSize);
                iterations = DefaultIterations;
            }

            vault.LastModified = DateTime.UtcNow;
            await SaveVaultInternalAsync(vault, salt, iterations);
        }

        /// <summary>
        /// Changes the master password. Vault must be unlocked.
        /// </summary>
        public async Task ChangeMasterPasswordAsync(VaultDatabase vault, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("New master password cannot be empty.");

            _vaultPath ??= GetDefaultVaultPath();

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            DeriveKeys(newPassword, salt, DefaultIterations);
            await SaveVaultInternalAsync(vault, salt, DefaultIterations);
        }

        /// <summary>
        /// Locks the vault, clearing encryption keys from memory.
        /// </summary>
        public void Lock()
        {
            if (_masterKey != null)
            {
                CryptographicOperations.ZeroMemory(_masterKey);
                _masterKey = null;
            }
            if (_macKey != null)
            {
                CryptographicOperations.ZeroMemory(_macKey);
                _macKey = null;
            }
        }

        /// <summary>
        /// Deletes the vault file and locks the vault, effectively resetting it.
        /// A new vault can be created afterwards with CreateVaultAsync.
        /// </summary>
        public void DeleteVault()
        {
            Lock();
            var path = _vaultPath ?? GetDefaultVaultPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            _vaultPath = null;
        }

        /// <summary>
        /// Exports the vault as unencrypted JSON (for backup/migration).
        /// </summary>
        public string ExportVaultJson(VaultDatabase vault)
        {
            return JsonSerializer.Serialize(vault, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }

        /// <summary>
        /// Imports a vault from unencrypted JSON.
        /// </summary>
        public VaultDatabase ImportVaultJson(string json)
        {
            return JsonSerializer.Deserialize<VaultDatabase>(json, JsonOptions)
                ?? new VaultDatabase();
        }

        /// <summary>
        /// Gets the raw encrypted vault file bytes for cloud sync.
        /// </summary>
        public async Task<byte[]> GetEncryptedVaultBytesAsync()
        {
            var path = _vaultPath ?? GetDefaultVaultPath();
            if (!File.Exists(path))
                throw new FileNotFoundException("Vault file not found.");
            return await File.ReadAllBytesAsync(path);
        }

        /// <summary>
        /// Writes encrypted vault bytes from cloud sync to disk.
        /// </summary>
        public async Task WriteEncryptedVaultBytesAsync(byte[] data)
        {
            var path = _vaultPath ?? GetDefaultVaultPath();
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(path, data);
        }

        /// <summary>
        /// Computes SHA-256 hash of the vault file for sync comparison.
        /// </summary>
        public async Task<string> ComputeVaultFileHashAsync()
        {
            var path = _vaultPath ?? GetDefaultVaultPath();
            if (!File.Exists(path)) return string.Empty;
            var bytes = await File.ReadAllBytesAsync(path);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        #region Private encryption helpers

        private void DeriveKeys(string password, byte[] salt, int iterations)
        {
            // Derive 64 bytes: 32 for encryption key, 32 for MAC key
            using var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256);

            var derived = pbkdf2.GetBytes(64);
            _masterKey = derived[..32];
            _macKey = derived[32..];
        }

        private byte[] EncryptAes(byte[] plaintext)
        {
            if (_masterKey == null) throw new InvalidOperationException("Keys not derived.");

            using var aes = Aes.Create();
            aes.Key = _masterKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

            // Prepend IV to ciphertext for storage
            var result = new byte[aes.IV.Length + ciphertext.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);
            return result;
        }

        private byte[] DecryptAes(byte[] ciphertext, byte[] iv)
        {
            if (_masterKey == null) throw new InvalidOperationException("Keys not derived.");

            using var aes = Aes.Create();
            aes.Key = _masterKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        private byte[] ComputeHmac(byte[] data)
        {
            if (_macKey == null) throw new InvalidOperationException("Keys not derived.");
            using var hmac = new HMACSHA256(_macKey);
            return hmac.ComputeHash(data);
        }

        private async Task SaveVaultInternalAsync(VaultDatabase vault, byte[] salt, int iterations)
        {
            var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(vault, JsonOptions));
            var iv = RandomNumberGenerator.GetBytes(IvSize);

            // Encrypt
            if (_masterKey == null) throw new InvalidOperationException("Keys not derived.");
            using var aes = Aes.Create();
            aes.Key = _masterKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

            // Compute HMAC
            var hash = ComputeHmac(ciphertext);

            // Build file
            var encryptedFile = new EncryptedVaultFile
            {
                Version = 1,
                Salt = Convert.ToBase64String(salt),
                Iv = Convert.ToBase64String(iv),
                Data = Convert.ToBase64String(ciphertext),
                Hash = Convert.ToBase64String(hash),
                KdfIterations = iterations,
            };

            var dir = Path.GetDirectoryName(_vaultPath!)!;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(encryptedFile, VaultFileJsonOptions);
            await File.WriteAllTextAsync(_vaultPath!, json);
        }

        #endregion
    }
}
