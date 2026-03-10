using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlatypusTools.Remote.Server.Models;

namespace PlatypusTools.Remote.Server.Services;

/// <summary>
/// Server-side vault service that reads the same encrypted vault file as the WPF app.
/// Uses AES-256-CBC with PBKDF2 key derivation and HMAC-SHA256 integrity verification.
/// The vault is unlocked per-session with the user's master password over HTTPS.
/// </summary>
public interface IVaultService
{
    VaultStatusDto GetStatus();
    bool Unlock(string masterPassword);
    void Lock();
    bool IsUnlocked { get; }
    List<VaultItemDto> GetItems(string? filter = null, int? typeFilter = null, string? folderId = null);
    VaultItemDto? GetItem(string id);
    VaultItemDto AddItem(AddVaultItemRequest request);
    bool DeleteItem(string id);
    List<VaultFolderDto> GetFolders();
    List<AuthenticatorEntryDto> GetAuthenticatorEntries();
    AuthenticatorEntryDto? AddAuthenticatorEntry(AddAuthenticatorRequest request);
    bool DeleteAuthenticatorEntry(string id);
    string GeneratePassword(GeneratePasswordRequest request);
    string GenerateTotpCode(string base32Secret, int digits = 6, int period = 30, string algorithm = "SHA1");
    int GetTotpRemainingSeconds(int period = 30);
}

public class VaultService : IVaultService, IDisposable
{
    private const int SaltSize = 32;
    private const int KeySize = 32;
    private const int IvSize = 16;

    private readonly ILogger<VaultService> _logger;
    private readonly object _lock = new();

    private byte[]? _masterKey;
    private byte[]? _macKey;
    private VaultDatabaseInternal? _vault;
    private string? _vaultPath;
    private DateTime _lastActivity;
    private readonly TimeSpan _autoLockTimeout = TimeSpan.FromMinutes(15);
    private Timer? _autoLockTimer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions VaultFileJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public VaultService(ILogger<VaultService> logger)
    {
        _logger = logger;
        _autoLockTimer = new Timer(CheckAutoLock, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsUnlocked => _masterKey != null && _vault != null;

    private string GetVaultPath()
    {
        if (_vaultPath != null) return _vaultPath;

        // Same path as the WPF app: %APPDATA%/PlatypusTools/Vault/vault.encrypted
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "Vault");
        Directory.CreateDirectory(dir);
        _vaultPath = Path.Combine(dir, "vault.encrypted");
        return _vaultPath;
    }

    public VaultStatusDto GetStatus()
    {
        var exists = File.Exists(GetVaultPath());
        return new VaultStatusDto
        {
            VaultExists = exists,
            IsUnlocked = IsUnlocked,
            ItemCount = _vault?.Items.Count ?? 0,
            AuthenticatorCount = _vault?.AuthenticatorEntries.Count ?? 0,
            FolderCount = _vault?.Folders.Count ?? 0,
        };
    }

    public bool Unlock(string masterPassword)
    {
        lock (_lock)
        {
            try
            {
                var path = GetVaultPath();
                if (!File.Exists(path))
                {
                    _logger.LogWarning("Vault file not found at {Path}", path);
                    return false;
                }

                var json = File.ReadAllText(path);
                var encryptedFile = JsonSerializer.Deserialize<EncryptedVaultFileInternal>(json, VaultFileJsonOptions);
                if (encryptedFile == null)
                {
                    _logger.LogWarning("Invalid vault file format");
                    return false;
                }

                var salt = Convert.FromBase64String(encryptedFile.Salt);
                var iv = Convert.FromBase64String(encryptedFile.Iv);
                var ciphertext = Convert.FromBase64String(encryptedFile.Data);
                var storedHash = Convert.FromBase64String(encryptedFile.Hash);

                DeriveKeys(masterPassword, salt, encryptedFile.KdfIterations);

                // Verify HMAC
                var computedHash = ComputeHmac(ciphertext);
                if (!CryptographicOperations.FixedTimeEquals(storedHash, computedHash))
                {
                    Lock();
                    _logger.LogWarning("Invalid master password");
                    return false;
                }

                // Decrypt
                var plaintext = DecryptAes(ciphertext, iv);
                _vault = JsonSerializer.Deserialize<VaultDatabaseInternal>(plaintext, JsonOptions)
                    ?? new VaultDatabaseInternal();

                _lastActivity = DateTime.UtcNow;
                _logger.LogInformation("Vault unlocked: {Items} items, {Auth} authenticator entries",
                    _vault.Items.Count, _vault.AuthenticatorEntries.Count);
                return true;
            }
            catch (Exception ex)
            {
                Lock();
                _logger.LogError(ex, "Failed to unlock vault");
                return false;
            }
        }
    }

    public void Lock()
    {
        lock (_lock)
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
            _vault = null;
        }
    }

    public List<VaultItemDto> GetItems(string? filter = null, int? typeFilter = null, string? folderId = null)
    {
        TouchActivity();
        if (_vault == null) return new();

        var query = _vault.Items.AsEnumerable();

        if (typeFilter.HasValue)
            query = query.Where(i => i.Type == typeFilter.Value);

        if (!string.IsNullOrEmpty(folderId))
            query = query.Where(i => i.FolderId == folderId);

        if (!string.IsNullOrEmpty(filter))
        {
            var f = filter.ToLowerInvariant();
            query = query.Where(i =>
                (i.Name?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Login?.Username?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Login?.Uris?.Any(u => u.Uri?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ?? false) ||
                (i.Notes?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query.Select(MapToDto).ToList();
    }

    public VaultItemDto? GetItem(string id)
    {
        TouchActivity();
        var item = _vault?.Items.FirstOrDefault(i => i.Id == id);
        return item != null ? MapToDto(item) : null;
    }

    public VaultItemDto AddItem(AddVaultItemRequest request)
    {
        TouchActivity();
        if (_vault == null) throw new InvalidOperationException("Vault is locked");

        var item = new VaultItemInternal
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = request.Type,
            Name = request.Name,
            Notes = request.Notes,
            FolderId = request.FolderId,
            Favorite = request.Favorite,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        };

        if (request.Type == 1) // Login
        {
            item.Login = new LoginDataInternal
            {
                Username = request.Username,
                Password = request.Password,
                TotpSecret = request.TotpSecret,
                Uris = request.Uris?.Select(u => new LoginUriInternal { Uri = u }).ToList() ?? new()
            };
        }
        else if (request.Type == 3) // Card
        {
            item.Card = new CardDataInternal
            {
                CardholderName = request.CardholderName,
                Brand = request.CardBrand,
                Number = request.CardNumber,
                ExpMonth = request.CardExpMonth,
                ExpYear = request.CardExpYear,
                Code = request.CardCode,
            };
        }
        else if (request.Type == 4) // Identity
        {
            item.Identity = new IdentityDataInternal
            {
                FirstName = request.IdentityFirstName,
                LastName = request.IdentityLastName,
                Email = request.IdentityEmail,
                Phone = request.IdentityPhone,
                Company = request.IdentityCompany,
            };
        }

        _vault.Items.Add(item);
        SaveVault();
        return MapToDto(item);
    }

    public bool DeleteItem(string id)
    {
        TouchActivity();
        if (_vault == null) return false;
        var item = _vault.Items.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;
        _vault.Items.Remove(item);
        SaveVault();
        return true;
    }

    public List<VaultFolderDto> GetFolders()
    {
        TouchActivity();
        return _vault?.Folders.Select(f => new VaultFolderDto { Id = f.Id, Name = f.Name }).ToList() ?? new();
    }

    public List<AuthenticatorEntryDto> GetAuthenticatorEntries()
    {
        TouchActivity();
        if (_vault == null) return new();

        var period = 30;
        return _vault.AuthenticatorEntries.Select(a => new AuthenticatorEntryDto
        {
            Id = a.Id,
            Issuer = a.Issuer,
            AccountName = a.AccountName,
            CurrentCode = GenerateTotpCode(a.Secret, a.Digits, a.Period, a.Algorithm),
            TimeRemaining = GetTotpRemainingSeconds(a.Period),
            Digits = a.Digits,
            Period = a.Period,
        }).ToList();
    }

    public AuthenticatorEntryDto? AddAuthenticatorEntry(AddAuthenticatorRequest request)
    {
        TouchActivity();
        if (_vault == null) return null;

        AuthenticatorEntryInternal? entry;

        if (!string.IsNullOrEmpty(request.OtpAuthUri))
        {
            entry = ParseOtpAuthUri(request.OtpAuthUri);
            if (entry == null) return null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Secret)) return null;
            entry = new AuthenticatorEntryInternal
            {
                Issuer = request.Issuer ?? "",
                AccountName = request.AccountName ?? "",
                Secret = CleanBase32(request.Secret),
            };
        }

        _vault.AuthenticatorEntries.Add(entry);
        SaveVault();

        return new AuthenticatorEntryDto
        {
            Id = entry.Id,
            Issuer = entry.Issuer,
            AccountName = entry.AccountName,
            CurrentCode = GenerateTotpCode(entry.Secret, entry.Digits, entry.Period, entry.Algorithm),
            TimeRemaining = GetTotpRemainingSeconds(entry.Period),
            Digits = entry.Digits,
            Period = entry.Period,
        };
    }

    public bool DeleteAuthenticatorEntry(string id)
    {
        TouchActivity();
        if (_vault == null) return false;
        var entry = _vault.AuthenticatorEntries.FirstOrDefault(a => a.Id == id);
        if (entry == null) return false;
        _vault.AuthenticatorEntries.Remove(entry);
        SaveVault();
        return true;
    }

    public string GeneratePassword(GeneratePasswordRequest request)
    {
        var chars = new StringBuilder();
        if (request.Lowercase) chars.Append("abcdefghijklmnopqrstuvwxyz");
        if (request.Uppercase) chars.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        if (request.Numbers) chars.Append("0123456789");
        if (request.Special) chars.Append("!@#$%^&*()-_=+[]{}|;:,.<>?");

        if (chars.Length == 0) chars.Append("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");

        var charArray = chars.ToString();
        var password = new char[Math.Max(8, Math.Min(128, request.Length))];
        for (int i = 0; i < password.Length; i++)
        {
            password[i] = charArray[RandomNumberGenerator.GetInt32(charArray.Length)];
        }
        return new string(password);
    }

    public string GenerateTotpCode(string base32Secret, int digits = 6, int period = 30, string algorithm = "SHA1")
    {
        if (string.IsNullOrWhiteSpace(base32Secret)) return "";

        try
        {
            var secretBytes = Base32Decode(CleanBase32(base32Secret));
            var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / period;
            var timeBytes = BitConverter.GetBytes((long)timeStep);
            if (BitConverter.IsLittleEndian) Array.Reverse(timeBytes);

            byte[] hash;
            using (var hmac = algorithm?.ToUpperInvariant() switch
            {
                "SHA256" => (HMAC)new HMACSHA256(secretBytes),
                "SHA512" => new HMACSHA512(secretBytes),
                _ => new HMACSHA1(secretBytes),
            })
            {
                hash = hmac.ComputeHash(timeBytes);
            }

            int offset = hash[^1] & 0xf;
            int binary = ((hash[offset] & 0x7f) << 24)
                       | ((hash[offset + 1] & 0xff) << 16)
                       | ((hash[offset + 2] & 0xff) << 8)
                       | (hash[offset + 3] & 0xff);

            int otp = binary % (int)Math.Pow(10, digits);
            return otp.ToString().PadLeft(digits, '0');
        }
        catch
        {
            return "ERROR";
        }
    }

    public int GetTotpRemainingSeconds(int period = 30)
    {
        var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return period - (int)(epoch % period);
    }

    #region Private helpers

    private void TouchActivity() => _lastActivity = DateTime.UtcNow;

    private void CheckAutoLock(object? state)
    {
        if (IsUnlocked && DateTime.UtcNow - _lastActivity > _autoLockTimeout)
        {
            _logger.LogInformation("Auto-locking vault due to inactivity");
            Lock();
        }
    }

    private void DeriveKeys(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256);
        var derived = pbkdf2.GetBytes(64);
        _masterKey = derived[..32];
        _macKey = derived[32..];
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

    private byte[] EncryptAes(byte[] plaintext, byte[] iv)
    {
        if (_masterKey == null) throw new InvalidOperationException("Keys not derived.");
        using var aes = Aes.Create();
        aes.Key = _masterKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    private byte[] ComputeHmac(byte[] data)
    {
        if (_macKey == null) throw new InvalidOperationException("Keys not derived.");
        using var hmac = new HMACSHA256(_macKey);
        return hmac.ComputeHash(data);
    }

    private void SaveVault()
    {
        if (_vault == null || _masterKey == null) return;

        try
        {
            var path = GetVaultPath();
            var existingJson = File.ReadAllText(path);
            var existingFile = JsonSerializer.Deserialize<EncryptedVaultFileInternal>(existingJson, VaultFileJsonOptions);
            if (existingFile == null) return;

            var salt = Convert.FromBase64String(existingFile.Salt);
            var iterations = existingFile.KdfIterations;

            _vault.LastModified = DateTime.UtcNow;
            var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_vault, JsonOptions));
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var ciphertext = EncryptAes(plaintext, iv);
            var hash = ComputeHmac(ciphertext);

            var encryptedFile = new EncryptedVaultFileInternal
            {
                Version = 1,
                Salt = Convert.ToBase64String(salt),
                Iv = Convert.ToBase64String(iv),
                Data = Convert.ToBase64String(ciphertext),
                Hash = Convert.ToBase64String(hash),
                KdfIterations = iterations,
            };

            var json = JsonSerializer.Serialize(encryptedFile, VaultFileJsonOptions);
            File.WriteAllText(path, json);
            _logger.LogInformation("Vault saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save vault");
        }
    }

    private VaultItemDto MapToDto(VaultItemInternal item)
    {
        var dto = new VaultItemDto
        {
            Id = item.Id,
            Type = item.Type,
            Name = item.Name,
            Notes = item.Notes,
            FolderId = item.FolderId,
            Favorite = item.Favorite,
            CreatedAt = item.CreatedAt,
            ModifiedAt = item.ModifiedAt,
        };

        if (item.Login != null)
        {
            dto.Username = item.Login.Username;
            dto.Password = item.Login.Password;
            dto.TotpSecret = item.Login.TotpSecret;
            dto.Uris = item.Login.Uris?.Select(u => u.Uri ?? "").Where(u => !string.IsNullOrEmpty(u)).ToList() ?? new();

            if (!string.IsNullOrEmpty(item.Login.TotpSecret))
            {
                dto.TotpCode = GenerateTotpCode(item.Login.TotpSecret);
                dto.TotpRemaining = GetTotpRemainingSeconds();
            }
        }

        if (item.Card != null)
        {
            dto.CardholderName = item.Card.CardholderName;
            dto.CardBrand = item.Card.Brand;
            dto.CardNumber = item.Card.Number;
            dto.CardExpMonth = item.Card.ExpMonth;
            dto.CardExpYear = item.Card.ExpYear;
            dto.CardCode = item.Card.Code;
        }

        if (item.Identity != null)
        {
            dto.IdentityFirstName = item.Identity.FirstName;
            dto.IdentityLastName = item.Identity.LastName;
            dto.IdentityEmail = item.Identity.Email;
            dto.IdentityPhone = item.Identity.Phone;
            dto.IdentityCompany = item.Identity.Company;
        }

        return dto;
    }

    private static AuthenticatorEntryInternal? ParseOtpAuthUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("otpauth://totp/", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var parsed = new Uri(uri);
            var label = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
            var queryString = parsed.Query.TrimStart('?');
            var queryParams = queryString.Split('&')
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => Uri.UnescapeDataString(p[0]).ToLowerInvariant(),
                              p => Uri.UnescapeDataString(p[1]));

            var entry = new AuthenticatorEntryInternal
            {
                Secret = queryParams.GetValueOrDefault("secret", ""),
                Digits = int.TryParse(queryParams.GetValueOrDefault("digits"), out var d) ? d : 6,
                Period = int.TryParse(queryParams.GetValueOrDefault("period"), out var p) ? p : 30,
                Algorithm = queryParams.GetValueOrDefault("algorithm", "SHA1"),
            };

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

            if (queryParams.TryGetValue("issuer", out var issuer) && !string.IsNullOrEmpty(issuer))
                entry.Issuer = issuer;

            return entry;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bitsLeft = 0;

        foreach (var c in base32)
        {
            var val = alphabet.IndexOf(char.ToUpperInvariant(c));
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
                buffer &= (1 << bitsLeft) - 1;
            }
        }
        return output.ToArray();
    }

    private static string CleanBase32(string input)
    {
        return input.Replace(" ", "").Replace("-", "").ToUpperInvariant();
    }

    public void Dispose()
    {
        _autoLockTimer?.Dispose();
        Lock();
    }

    #endregion

    #region Internal models (matching WPF vault file format)

    private class EncryptedVaultFileInternal
    {
        public int Version { get; set; } = 1;
        public string Salt { get; set; } = "";
        public string Iv { get; set; } = "";
        public string Data { get; set; } = "";
        public string Hash { get; set; } = "";
        public int KdfIterations { get; set; } = 600000;
    }

    private class VaultDatabaseInternal
    {
        public int Version { get; set; } = 1;
        public List<VaultItemInternal> Items { get; set; } = new();
        public List<VaultFolderInternal> Folders { get; set; } = new();
        public List<AuthenticatorEntryInternal> AuthenticatorEntries { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public string? SyncAccountId { get; set; }
    }

    private class VaultItemInternal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int Type { get; set; }
        public string Name { get; set; } = "";
        public string? Notes { get; set; }
        public string? FolderId { get; set; }
        public bool Favorite { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        public LoginDataInternal? Login { get; set; }
        public CardDataInternal? Card { get; set; }
        public IdentityDataInternal? Identity { get; set; }
    }

    private class LoginDataInternal
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? TotpSecret { get; set; }
        public List<LoginUriInternal> Uris { get; set; } = new();
    }

    private class LoginUriInternal
    {
        public string? Uri { get; set; }
        public int Match { get; set; }
    }

    private class CardDataInternal
    {
        public string? CardholderName { get; set; }
        public string? Brand { get; set; }
        public string? Number { get; set; }
        public string? ExpMonth { get; set; }
        public string? ExpYear { get; set; }
        public string? Code { get; set; }
    }

    private class IdentityDataInternal
    {
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public string? Company { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    private class VaultFolderInternal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
    }

    private class AuthenticatorEntryInternal
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Issuer { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string Secret { get; set; } = "";
        public int Digits { get; set; } = 6;
        public int Period { get; set; } = 30;
        public string Algorithm { get; set; } = "SHA1";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion
}
