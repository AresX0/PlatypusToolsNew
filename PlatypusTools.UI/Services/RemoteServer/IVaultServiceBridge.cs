using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlatypusTools.UI.Services.Vault;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Interface for bridging vault operations from the remote web app to the WPF vault service.
/// </summary>
public interface IVaultServiceBridge
{
    bool IsUnlocked { get; }
    bool IsMfaPending { get; }
    Task<bool> UnlockAsync(string masterPassword);
    bool VerifyMfa(string code);
    void CancelMfa();
    void Lock();
    VaultStatusDto GetStatus();
    IEnumerable<VaultItemDto> GetItems(string? search = null, string? folderId = null, string? type = null);
    VaultItemDto? GetItem(string id);
    Task<VaultItemDto> AddItemAsync(VaultItemDto item);
    Task<VaultItemDto?> UpdateItemAsync(string id, VaultItemDto item);
    Task<bool> DeleteItemAsync(string id);
    IEnumerable<VaultFolderDto> GetFolders();
    string? GetTotpCode(string itemId);
    IEnumerable<AuthenticatorDto> GetAuthenticatorEntries();
    Task<AuthenticatorDto?> AddAuthenticatorEntryAsync(string otpAuthUri);
    Task<bool> DeleteAuthenticatorEntryAsync(string id);
    string GeneratePassword(int length = 20, bool upper = true, bool lower = true,
        bool numbers = true, bool special = true);
}

public class VaultStatusDto
{
    public bool IsUnlocked { get; set; }
    public bool VaultExists { get; set; }
    public bool MfaRequired { get; set; }
    public bool MfaPending { get; set; }
    public int ItemCount { get; set; }
    public int FolderCount { get; set; }
    public int AuthenticatorCount { get; set; }
}

public class VaultItemDto
{
    public string Id { get; set; } = string.Empty;
    public int Type { get; set; } // 1=Login, 2=SecureNote, 3=Card, 4=Identity
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? FolderId { get; set; }
    public bool Favorite { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? TotpSecret { get; set; }
    public List<string>? Uris { get; set; }
    public bool HasTotp { get; set; }
    // Card fields (masked for listing)
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardholderName { get; set; }
    public string? CardNumber { get; set; }
    public string? CardExpMonth { get; set; }
    public string? CardExpYear { get; set; }
    public string? CardCode { get; set; }
    // Identity fields
    public string? IdentityName { get; set; }
    public string? IdentityEmail { get; set; }
}

public class VaultFolderDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

public class AuthenticatorDto
{
    public string Id { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int RemainingSeconds { get; set; }
    public int Period { get; set; } = 30;
}
