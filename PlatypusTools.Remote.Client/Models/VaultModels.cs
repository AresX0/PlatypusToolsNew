namespace PlatypusTools.Remote.Client.Models;

public class VaultStatusDto
{
    public bool VaultExists { get; set; }
    public bool IsUnlocked { get; set; }
    public int ItemCount { get; set; }
    public int FolderCount { get; set; }
    public int AuthenticatorCount { get; set; }
}

public class VaultItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; } // 1=Login, 2=Card, 3=Identity, 4=SecureNote
    public string? FolderId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    // Login fields
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Uri { get; set; }
    public string? TotpSecret { get; set; }
    public string? TotpCode { get; set; }
    public int TotpRemaining { get; set; }

    // Card fields
    public string? CardholderName { get; set; }
    public string? CardNumber { get; set; }
    public string? ExpirationMonth { get; set; }
    public string? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Brand { get; set; }

    // Identity fields
    public string? Title { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }

    public string TypeName => Type switch
    {
        1 => "Login",
        2 => "Card",
        3 => "Identity",
        4 => "Secure Note",
        _ => "Unknown"
    };

    public string TypeIcon => Type switch
    {
        1 => "🔑",
        2 => "💳",
        3 => "👤",
        4 => "📝",
        _ => "📦"
    };
}

public class VaultFolderDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class AuthenticatorEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string CurrentCode { get; set; } = string.Empty;
    public int TimeRemaining { get; set; }
}

public class VaultUnlockRequest
{
    public string MasterPassword { get; set; } = string.Empty;
}

public class AddVaultItemRequest
{
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; } = 1;
    public string? FolderId { get; set; }
    public string? Notes { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Uri { get; set; }
    public string? TotpSecret { get; set; }
    public string? CardholderName { get; set; }
    public string? CardNumber { get; set; }
    public string? ExpirationMonth { get; set; }
    public string? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Brand { get; set; }
    public string? Title { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
}

public class AddAuthenticatorRequest
{
    public string? OtpAuthUri { get; set; }
    public string? Issuer { get; set; }
    public string? AccountName { get; set; }
    public string? Secret { get; set; }
}

public class GeneratePasswordRequest
{
    public int Length { get; set; } = 20;
    public bool IncludeUppercase { get; set; } = true;
    public bool IncludeLowercase { get; set; } = true;
    public bool IncludeDigits { get; set; } = true;
    public bool IncludeSpecial { get; set; } = true;
}
