namespace PlatypusTools.Remote.Server.Models;

/// <summary>
/// DTOs for Security Vault remote access.
/// Mirrors the vault models in PlatypusTools.UI but as lightweight transfer objects.
/// </summary>
/// 
public class VaultUnlockRequest
{
    public string MasterPassword { get; set; } = "";
}

public class VaultStatusDto
{
    public bool VaultExists { get; set; }
    public bool IsUnlocked { get; set; }
    public int ItemCount { get; set; }
    public int AuthenticatorCount { get; set; }
    public int FolderCount { get; set; }
}

public class VaultItemDto
{
    public string Id { get; set; } = "";
    public int Type { get; set; } // 1=Login, 2=SecureNote, 3=Card, 4=Identity
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? FolderId { get; set; }
    public bool Favorite { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    // Login fields (only populated for Type=1)
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? TotpSecret { get; set; }
    public List<string> Uris { get; set; } = new();

    // Card fields (only populated for Type=3)
    public string? CardholderName { get; set; }
    public string? CardBrand { get; set; }
    public string? CardNumber { get; set; }
    public string? CardExpMonth { get; set; }
    public string? CardExpYear { get; set; }
    public string? CardCode { get; set; }

    // Identity fields (only populated for Type=4)
    public string? IdentityFirstName { get; set; }
    public string? IdentityLastName { get; set; }
    public string? IdentityEmail { get; set; }
    public string? IdentityPhone { get; set; }
    public string? IdentityCompany { get; set; }

    // TOTP code (computed server-side, refreshed periodically)
    public string? TotpCode { get; set; }
    public int TotpRemaining { get; set; }
}

public class VaultFolderDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class AuthenticatorEntryDto
{
    public string Id { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string CurrentCode { get; set; } = "";
    public int TimeRemaining { get; set; }
    public int Digits { get; set; } = 6;
    public int Period { get; set; } = 30;
}

public class AddAuthenticatorRequest
{
    public string? OtpAuthUri { get; set; }
    public string? Issuer { get; set; }
    public string? AccountName { get; set; }
    public string? Secret { get; set; }
}

public class AddVaultItemRequest
{
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? FolderId { get; set; }
    public bool Favorite { get; set; }

    // Login
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? TotpSecret { get; set; }
    public List<string>? Uris { get; set; }

    // Card
    public string? CardholderName { get; set; }
    public string? CardBrand { get; set; }
    public string? CardNumber { get; set; }
    public string? CardExpMonth { get; set; }
    public string? CardExpYear { get; set; }
    public string? CardCode { get; set; }

    // Identity
    public string? IdentityFirstName { get; set; }
    public string? IdentityLastName { get; set; }
    public string? IdentityEmail { get; set; }
    public string? IdentityPhone { get; set; }
    public string? IdentityCompany { get; set; }
}

public class GeneratePasswordRequest
{
    public int Length { get; set; } = 20;
    public bool Uppercase { get; set; } = true;
    public bool Lowercase { get; set; } = true;
    public bool Numbers { get; set; } = true;
    public bool Special { get; set; } = true;
}
