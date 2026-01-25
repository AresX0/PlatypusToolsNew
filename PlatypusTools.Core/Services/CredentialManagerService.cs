using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Secure credential storage using Windows DPAPI for encryption.
    /// Credentials are encrypted with the current user's credentials and cannot be read by other users.
    /// </summary>
    public class CredentialManagerService
    {
        private static readonly Lazy<CredentialManagerService> _instance = 
            new(() => new CredentialManagerService());
        
        public static CredentialManagerService Instance => _instance.Value;
        
        private readonly string _credentialsPath;
        private readonly Dictionary<string, StoredCredential> _cache = new();
        private readonly object _lock = new();

        private CredentialManagerService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "PlatypusTools", "Credentials");
            Directory.CreateDirectory(folder);
            _credentialsPath = Path.Combine(folder, "credentials.dat");
            LoadCredentials();
        }

        /// <summary>
        /// Stores a credential securely.
        /// </summary>
        public void SaveCredential(string key, string username, string password, string? description = null, 
            CredentialType type = CredentialType.Generic)
        {
            lock (_lock)
            {
                var credential = new StoredCredential
                {
                    Key = key,
                    Username = username,
                    EncryptedPassword = Encrypt(password),
                    Description = description ?? key,
                    Type = type,
                    CreatedAt = DateTime.UtcNow,
                    LastUsed = DateTime.UtcNow
                };

                _cache[key] = credential;
                PersistCredentials();
            }
        }

        /// <summary>
        /// Retrieves a credential by key.
        /// </summary>
        public StoredCredential? GetCredential(string key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cred))
                {
                    cred.LastUsed = DateTime.UtcNow;
                    PersistCredentials();
                    return cred;
                }
                return null;
            }
        }

        /// <summary>
        /// Gets the decrypted password for a credential.
        /// </summary>
        public string? GetPassword(string key)
        {
            var cred = GetCredential(key);
            if (cred?.EncryptedPassword != null)
            {
                return Decrypt(cred.EncryptedPassword);
            }
            return null;
        }

        /// <summary>
        /// Deletes a credential.
        /// </summary>
        public bool DeleteCredential(string key)
        {
            lock (_lock)
            {
                if (_cache.Remove(key))
                {
                    PersistCredentials();
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Gets all stored credentials (passwords remain encrypted).
        /// </summary>
        public IReadOnlyList<StoredCredential> GetAllCredentials()
        {
            lock (_lock)
            {
                return _cache.Values.OrderBy(c => c.Key).ToList();
            }
        }

        /// <summary>
        /// Gets credentials by type.
        /// </summary>
        public IReadOnlyList<StoredCredential> GetCredentialsByType(CredentialType type)
        {
            lock (_lock)
            {
                return _cache.Values.Where(c => c.Type == type).OrderBy(c => c.Key).ToList();
            }
        }

        /// <summary>
        /// Checks if a credential exists.
        /// </summary>
        public bool HasCredential(string key)
        {
            lock (_lock)
            {
                return _cache.ContainsKey(key);
            }
        }

        /// <summary>
        /// Updates the username and/or password for an existing credential.
        /// </summary>
        public bool UpdateCredential(string key, string? newUsername = null, string? newPassword = null)
        {
            lock (_lock)
            {
                if (!_cache.TryGetValue(key, out var cred))
                    return false;

                if (newUsername != null)
                    cred.Username = newUsername;

                if (newPassword != null)
                    cred.EncryptedPassword = Encrypt(newPassword);

                cred.LastUsed = DateTime.UtcNow;
                PersistCredentials();
                return true;
            }
        }

        /// <summary>
        /// Exports credentials to a file (encrypted with a master password).
        /// </summary>
        public void ExportCredentials(string filePath, string masterPassword)
        {
            lock (_lock)
            {
                var exportData = new CredentialExport
                {
                    ExportedAt = DateTime.UtcNow,
                    Credentials = _cache.Values.Select(c => new ExportedCredential
                    {
                        Key = c.Key,
                        Username = c.Username,
                        Password = Decrypt(c.EncryptedPassword!),
                        Description = c.Description,
                        Type = c.Type
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(exportData);
                var encrypted = EncryptWithPassword(json, masterPassword);
                File.WriteAllBytes(filePath, encrypted);
            }
        }

        /// <summary>
        /// Imports credentials from an encrypted file.
        /// </summary>
        public int ImportCredentials(string filePath, string masterPassword, bool overwrite = false)
        {
            lock (_lock)
            {
                var encrypted = File.ReadAllBytes(filePath);
                var json = DecryptWithPassword(encrypted, masterPassword);
                var exportData = JsonSerializer.Deserialize<CredentialExport>(json);

                if (exportData?.Credentials == null)
                    return 0;

                var count = 0;
                foreach (var cred in exportData.Credentials)
                {
                    if (overwrite || !_cache.ContainsKey(cred.Key))
                    {
                        SaveCredential(cred.Key, cred.Username, cred.Password, cred.Description, cred.Type);
                        count++;
                    }
                }

                return count;
            }
        }

        private void LoadCredentials()
        {
            try
            {
                if (File.Exists(_credentialsPath))
                {
                    var encrypted = File.ReadAllBytes(_credentialsPath);
                    var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                        encrypted, null, DataProtectionScope.CurrentUser));
                    var creds = JsonSerializer.Deserialize<List<StoredCredential>>(json);
                    
                    _cache.Clear();
                    if (creds != null)
                    {
                        foreach (var cred in creds)
                        {
                            _cache[cred.Key] = cred;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"Failed to load credentials: {ex.Message}");
            }
        }

        private void PersistCredentials()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache.Values.ToList());
                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_credentialsPath, encrypted);
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to save credentials: {ex.Message}");
            }
        }

        private byte[] Encrypt(string plainText)
        {
            return ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);
        }

        private string Decrypt(byte[] encrypted)
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
        }

        private byte[] EncryptWithPassword(string plainText, string password)
        {
            using var aes = Aes.Create();
            var salt = RandomNumberGenerator.GetBytes(16);
            
            // Use the static Pbkdf2 method instead of the obsolete constructor
            var keyBytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
            var ivBytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 16);
            aes.Key = keyBytes;
            aes.IV = ivBytes;

            using var ms = new MemoryStream();
            ms.Write(salt, 0, salt.Length);
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                var data = Encoding.UTF8.GetBytes(plainText);
                cs.Write(data, 0, data.Length);
            }
            return ms.ToArray();
        }

        private string DecryptWithPassword(byte[] encrypted, string password)
        {
            using var aes = Aes.Create();
            var salt = encrypted.Take(16).ToArray();
            var cipherText = encrypted.Skip(16).ToArray();

            // Use the static Pbkdf2 method instead of the obsolete constructor
            var keyBytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
            var ivBytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 16);
            aes.Key = keyBytes;
            aes.IV = ivBytes;

            using var ms = new MemoryStream(cipherText);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var reader = new StreamReader(cs);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Represents a stored credential.
    /// </summary>
    public class StoredCredential
    {
        public string Key { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public byte[]? EncryptedPassword { get; set; }
        public string? Description { get; set; }
        public CredentialType Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUsed { get; set; }

        // Display helpers
        public string TypeDisplay => Type.ToString();
        public string LastUsedDisplay => LastUsed.ToLocalTime().ToString("g");
        public string CreatedDisplay => CreatedAt.ToLocalTime().ToString("g");
    }

    /// <summary>
    /// Type of credential for categorization.
    /// </summary>
    public enum CredentialType
    {
        Generic,
        SSH,
        FTP,
        SFTP,
        Database,
        WebService,
        RemoteDesktop,
        Email,
        Other
    }

    // Export/import models
    internal class CredentialExport
    {
        public DateTime ExportedAt { get; set; }
        public List<ExportedCredential> Credentials { get; set; } = new();
    }

    internal class ExportedCredential
    {
        public string Key { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Description { get; set; }
        public CredentialType Type { get; set; }
    }
}
