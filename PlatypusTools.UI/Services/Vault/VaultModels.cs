using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.UI.Services.Vault
{
    /// <summary>
    /// Type of vault item (mirrors Bitwarden item types).
    /// </summary>
    public enum VaultItemType
    {
        Login = 1,
        SecureNote = 2,
        Card = 3,
        Identity = 4
    }

    /// <summary>
    /// Represents a folder for organizing vault items.
    /// </summary>
    public class VaultFolder
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        public override string ToString() => Name;
    }

    /// <summary>
    /// Base vault item containing common fields.
    /// </summary>
    public class VaultItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public VaultItemType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? FolderId { get; set; }
        public bool Favorite { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
        public int Reprompt { get; set; } // 0 = none, 1 = re-prompt master password

        // Login fields
        public LoginData? Login { get; set; }

        // Card fields
        public CardData? Card { get; set; }

        // Identity fields
        public IdentityData? Identity { get; set; }

        /// <summary>
        /// Custom fields (name/value pairs).
        /// </summary>
        public List<CustomField> CustomFields { get; set; } = new();
    }

    /// <summary>
    /// Login-specific data (username, password, URIs, TOTP).
    /// </summary>
    public class LoginData
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? TotpSecret { get; set; }
        public List<LoginUri> Uris { get; set; } = new();
    }

    /// <summary>
    /// A URI associated with a login item.
    /// </summary>
    public class LoginUri
    {
        public string? Uri { get; set; }
        public int Match { get; set; } // 0=default, 1=baseDomain, 2=host, 3=startsWith, 4=exact, 5=regex, 6=never
    }

    /// <summary>
    /// Credit/debit card data.
    /// </summary>
    public class CardData
    {
        public string? CardholderName { get; set; }
        public string? Brand { get; set; } // Visa, Mastercard, Amex, etc.
        public string? Number { get; set; }
        public string? ExpMonth { get; set; }
        public string? ExpYear { get; set; }
        public string? Code { get; set; } // CVV
    }

    /// <summary>
    /// Identity (personal information) data.
    /// </summary>
    public class IdentityData
    {
        public string? Title { get; set; } // Mr, Mrs, Ms, Dr, etc.
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Username { get; set; }
        public string? Company { get; set; }
        public string? Ssn { get; set; }
        public string? PassportNumber { get; set; }
        public string? LicenseNumber { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? Address3 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
    }

    /// <summary>
    /// Custom field on a vault item.
    /// </summary>
    public class CustomField
    {
        public string Name { get; set; } = string.Empty;
        public string? Value { get; set; }
        public int Type { get; set; } // 0=text, 1=hidden, 2=boolean, 3=linked
    }

    /// <summary>
    /// The entire vault database (encrypted at rest).
    /// </summary>
    public class VaultDatabase
    {
        public int Version { get; set; } = 1;
        public List<VaultItem> Items { get; set; } = new();
        public List<VaultFolder> Folders { get; set; } = new();
        public List<AuthenticatorEntry> AuthenticatorEntries { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public string? SyncAccountId { get; set; }
    }

    /// <summary>
    /// Standalone authenticator entry (for items not tied to a login).
    /// </summary>
    public class AuthenticatorEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Issuer { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        public int Digits { get; set; } = 6;
        public int Period { get; set; } = 30;
        public string Algorithm { get; set; } = "SHA1"; // SHA1, SHA256, SHA512
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Encrypted vault file format stored on disk / synced to cloud.
    /// </summary>
    public class EncryptedVaultFile
    {
        public int Version { get; set; } = 1;
        public string Salt { get; set; } = string.Empty; // Base64 salt for PBKDF2
        public string Iv { get; set; } = string.Empty;   // Base64 IV for AES
        public string Data { get; set; } = string.Empty;  // Base64 AES-encrypted JSON
        public string Hash { get; set; } = string.Empty;  // HMAC-SHA256 for integrity
        public int KdfIterations { get; set; } = 600000;  // PBKDF2 iterations (OWASP 2023 recommendation)
    }

    /// <summary>
    /// Password generator options (mirrors Bitwarden generator).
    /// </summary>
    public class PasswordGeneratorOptions
    {
        public int Length { get; set; } = 20;
        public bool Uppercase { get; set; } = true;
        public bool Lowercase { get; set; } = true;
        public bool Numbers { get; set; } = true;
        public bool Special { get; set; } = true;
        public int MinUppercase { get; set; } = 1;
        public int MinLowercase { get; set; } = 1;
        public int MinNumbers { get; set; } = 1;
        public int MinSpecial { get; set; } = 1;
        public bool AvoidAmbiguous { get; set; }

        // Passphrase options
        public bool UsePassphrase { get; set; }
        public int NumWords { get; set; } = 4;
        public string WordSeparator { get; set; } = "-";
        public bool Capitalize { get; set; } = true;
        public bool IncludeNumber { get; set; } = true;
    }

    /// <summary>
    /// Result of a password health check.
    /// </summary>
    public class PasswordHealthResult
    {
        public int TotalPasswords { get; set; }
        public int WeakPasswords { get; set; }
        public int ReusedPasswords { get; set; }
        public List<VaultItem> WeakItems { get; set; } = new();
        public List<VaultItem> ReusedItems { get; set; } = new();
        public List<(string Password, List<VaultItem> Items)> ReusedGroups { get; set; } = new();
    }

    /// <summary>
    /// Cloud sync provider type.
    /// </summary>
    public enum CloudProvider
    {
        None = 0,
        OneDrive = 1,
        GoogleDrive = 2
    }

    /// <summary>
    /// Tracks sync state.
    /// </summary>
    public class SyncState
    {
        public CloudProvider Provider { get; set; }
        public string? AccountEmail { get; set; }
        public string? AccountId { get; set; }
        public DateTime? LastSyncTime { get; set; }
        public string? RemoteFileId { get; set; } // Google Drive file ID or OneDrive item ID
        public string? RemoteFileHash { get; set; }
    }
}
