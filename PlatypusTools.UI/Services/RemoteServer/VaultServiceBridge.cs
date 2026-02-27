using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlatypusTools.UI.Services.Vault;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Bridges vault operations from the web app REST API to the WPF EncryptedVaultService.
/// Maintains its own unlock state for the web session.
/// </summary>
public class VaultServiceBridge : IVaultServiceBridge
{
    private readonly EncryptedVaultService _vaultService;
    private readonly VaultMfaService _mfaService = new();
    private VaultDatabase? _vault;
    private bool _mfaPending; // True when password verified but MFA not yet verified
    private string? _pendingMasterPassword; // Held for MFA TOTP decryption

    public bool IsUnlocked => _vault != null && !_mfaPending;
    public bool IsMfaPending => _mfaPending;

    public VaultServiceBridge(EncryptedVaultService vaultService)
    {
        _vaultService = vaultService;
    }

    public async Task<bool> UnlockAsync(string masterPassword)
    {
        try
        {
            _vault = await _vaultService.UnlockVaultAsync(masterPassword);
            if (_vault == null) return false;

            // Check if MFA is enabled
            if (_mfaService.IsMfaEnabled())
            {
                _mfaPending = true;
                _pendingMasterPassword = masterPassword;
                return true; // Password correct, but MFA still needed
            }

            return true;
        }
        catch
        {
            _vault = null;
            _mfaPending = false;
            _pendingMasterPassword = null;
            return false;
        }
    }

    public bool VerifyMfa(string code)
    {
        if (!_mfaPending || _pendingMasterPassword == null)
            return false;

        var mfaType = _mfaService.GetMfaType();
        // On the web, only TOTP is supported (no Windows Hello)
        if (mfaType == MfaType.Totp || mfaType == MfaType.Both)
        {
            if (!_mfaService.ValidateTotpCode(_pendingMasterPassword, code))
                return false;
        }

        _mfaPending = false;
        _pendingMasterPassword = null;
        return true;
    }

    public void CancelMfa()
    {
        _mfaPending = false;
        _pendingMasterPassword = null;
        _vault = null;
    }

    public void Lock()
    {
        _vault = null;
        _mfaPending = false;
        _pendingMasterPassword = null;
    }

    public VaultStatusDto GetStatus()
    {
        var vaultPath = _vaultService.VaultFilePath;
        var mfaEnabled = _mfaService.IsMfaEnabled();
        return new VaultStatusDto
        {
            IsUnlocked = _vault != null && !_mfaPending,
            VaultExists = File.Exists(vaultPath),
            MfaRequired = mfaEnabled,
            MfaPending = _mfaPending,
            ItemCount = _vault?.Items.Count ?? 0,
            FolderCount = _vault?.Folders.Count ?? 0,
            AuthenticatorCount = _vault?.AuthenticatorEntries.Count ?? 0,
        };
    }

    public IEnumerable<VaultItemDto> GetItems(string? search = null, string? folderId = null, string? type = null)
    {
        if (_vault == null) return Enumerable.Empty<VaultItemDto>();

        IEnumerable<VaultItem> items = _vault.Items;

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (Enum.TryParse<VaultItemType>(type, true, out var vaultType))
                items = items.Where(i => i.Type == vaultType);
            else if (type.Equals("favorites", StringComparison.OrdinalIgnoreCase))
                items = items.Where(i => i.Favorite);
        }

        if (!string.IsNullOrWhiteSpace(folderId))
        {
            if (folderId == "none")
                items = items.Where(i => string.IsNullOrEmpty(i.FolderId));
            else
                items = items.Where(i => i.FolderId == folderId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            items = items.Where(i =>
                (i.Name?.ToLowerInvariant().Contains(q) == true) ||
                (i.Login?.Username?.ToLowerInvariant().Contains(q) == true) ||
                (i.Login?.Uris?.Any(u => u.Uri?.ToLowerInvariant().Contains(q) == true) == true) ||
                (i.Notes?.ToLowerInvariant().Contains(q) == true));
        }

        return items.OrderByDescending(i => i.Favorite).ThenBy(i => i.Name).Select(i => MapItemToDto(i));
    }

    public VaultItemDto? GetItem(string id)
    {
        if (_vault == null) return null;
        var item = _vault.Items.FirstOrDefault(i => i.Id == id);
        return item != null ? MapItemToDto(item, includeSecrets: true) : null;
    }

    public async Task<VaultItemDto> AddItemAsync(VaultItemDto dto)
    {
        if (_vault == null) throw new InvalidOperationException("Vault is locked");

        var item = new VaultItem
        {
            Type = (VaultItemType)dto.Type,
            Name = dto.Name,
            Notes = dto.Notes,
            FolderId = dto.FolderId,
            Favorite = dto.Favorite,
        };

        if (item.Type == VaultItemType.Login)
        {
            item.Login = new LoginData
            {
                Username = dto.Username,
                Password = dto.Password,
                TotpSecret = dto.TotpSecret,
                Uris = dto.Uris?.Select(u => new LoginUri { Uri = u }).ToList() ?? new(),
            };
        }
        else if (item.Type == VaultItemType.Card)
        {
            item.Card = new CardData
            {
                CardholderName = dto.CardholderName,
                Number = dto.CardNumber,
                Brand = dto.CardBrand,
                ExpMonth = dto.CardExpMonth,
                ExpYear = dto.CardExpYear,
                Code = dto.CardCode,
            };
        }

        _vault.Items.Add(item);
        _vault.LastModified = DateTime.UtcNow;
        await _vaultService.SaveVaultAsync(_vault);

        return MapItemToDto(item);
    }

    public async Task<VaultItemDto?> UpdateItemAsync(string id, VaultItemDto dto)
    {
        if (_vault == null) return null;
        var item = _vault.Items.FirstOrDefault(i => i.Id == id);
        if (item == null) return null;

        item.Name = dto.Name;
        item.Notes = dto.Notes;
        item.FolderId = dto.FolderId;
        item.Favorite = dto.Favorite;
        item.ModifiedAt = DateTime.UtcNow;

        if (item.Type == VaultItemType.Login)
        {
            item.Login ??= new LoginData();
            item.Login.Username = dto.Username;
            item.Login.Password = dto.Password;
            item.Login.TotpSecret = dto.TotpSecret;
            item.Login.Uris = dto.Uris?.Select(u => new LoginUri { Uri = u }).ToList() ?? new();
        }
        else if (item.Type == VaultItemType.Card)
        {
            item.Card ??= new CardData();
            item.Card.CardholderName = dto.CardholderName;
            item.Card.Number = dto.CardNumber;
            item.Card.Brand = dto.CardBrand;
            item.Card.ExpMonth = dto.CardExpMonth;
            item.Card.ExpYear = dto.CardExpYear;
            item.Card.Code = dto.CardCode;
        }

        _vault.LastModified = DateTime.UtcNow;
        await _vaultService.SaveVaultAsync(_vault);

        return MapItemToDto(item);
    }

    public async Task<bool> DeleteItemAsync(string id)
    {
        if (_vault == null) return false;
        var item = _vault.Items.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;
        _vault.Items.Remove(item);
        _vault.LastModified = DateTime.UtcNow;
        await _vaultService.SaveVaultAsync(_vault);
        return true;
    }

    public IEnumerable<VaultFolderDto> GetFolders()
    {
        if (_vault == null) return Enumerable.Empty<VaultFolderDto>();
        return _vault.Folders.OrderBy(f => f.Name).Select(f => new VaultFolderDto
        {
            Id = f.Id,
            Name = f.Name,
            ItemCount = _vault.Items.Count(i => i.FolderId == f.Id),
        });
    }

    public string? GetTotpCode(string itemId)
    {
        if (_vault == null) return null;
        var item = _vault.Items.FirstOrDefault(i => i.Id == itemId);
        var secret = item?.Login?.TotpSecret;
        if (string.IsNullOrEmpty(secret)) return null;
        try { return TotpService.GenerateCode(secret); }
        catch { return null; }
    }

    public IEnumerable<AuthenticatorDto> GetAuthenticatorEntries()
    {
        if (_vault == null) return Enumerable.Empty<AuthenticatorDto>();
        return _vault.AuthenticatorEntries.Select(e =>
        {
            string code;
            try { code = TotpService.GenerateCode(e.Secret, e.Digits, e.Period, e.Algorithm); }
            catch { code = "------"; }
            return new AuthenticatorDto
            {
                Id = e.Id,
                Issuer = e.Issuer,
                AccountName = e.AccountName,
                Code = code,
                RemainingSeconds = TotpService.GetRemainingSeconds(e.Period),
                Period = e.Period,
            };
        });
    }

    public async Task<AuthenticatorDto?> AddAuthenticatorEntryAsync(string otpAuthUri)
    {
        if (_vault == null) return null;
        var entry = TotpService.ParseOtpAuthUri(otpAuthUri);
        if (entry == null) return null;

        _vault.AuthenticatorEntries.Add(entry);
        _vault.LastModified = DateTime.UtcNow;
        await _vaultService.SaveVaultAsync(_vault);

        string code;
        try { code = TotpService.GenerateCode(entry.Secret, entry.Digits, entry.Period, entry.Algorithm); }
        catch { code = "------"; }

        return new AuthenticatorDto
        {
            Id = entry.Id,
            Issuer = entry.Issuer,
            AccountName = entry.AccountName,
            Code = code,
            RemainingSeconds = TotpService.GetRemainingSeconds(entry.Period),
            Period = entry.Period,
        };
    }

    public async Task<bool> DeleteAuthenticatorEntryAsync(string id)
    {
        if (_vault == null) return false;
        var entry = _vault.AuthenticatorEntries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return false;
        _vault.AuthenticatorEntries.Remove(entry);
        _vault.LastModified = DateTime.UtcNow;
        await _vaultService.SaveVaultAsync(_vault);
        return true;
    }

    public string GeneratePassword(int length = 20, bool upper = true, bool lower = true,
        bool numbers = true, bool special = true)
    {
        return PasswordGeneratorService.GeneratePassword(new PasswordGeneratorOptions
        {
            Length = length,
            Uppercase = upper,
            Lowercase = lower,
            Numbers = numbers,
            Special = special,
        });
    }

    private static VaultItemDto MapItemToDto(VaultItem item, bool includeSecrets = false)
    {
        var dto = new VaultItemDto
        {
            Id = item.Id,
            Type = (int)item.Type,
            Name = item.Name,
            Notes = includeSecrets ? item.Notes : null,
            FolderId = item.FolderId,
            Favorite = item.Favorite,
            Username = item.Login?.Username,
            HasTotp = !string.IsNullOrEmpty(item.Login?.TotpSecret),
            Uris = item.Login?.Uris?.Select(u => u.Uri ?? "").Where(u => u.Length > 0).ToList(),
            // Card summary
            CardBrand = item.Card?.Brand,
            CardLast4 = item.Card?.Number?.Length >= 4
                ? item.Card.Number[^4..] : null,
            CardholderName = item.Card?.CardholderName,
            // Identity summary
            IdentityName = item.Identity != null
                ? $"{item.Identity.FirstName} {item.Identity.LastName}".Trim() : null,
            IdentityEmail = item.Identity?.Email,
        };

        if (includeSecrets)
        {
            dto.Password = item.Login?.Password;
            dto.TotpSecret = item.Login?.TotpSecret;
            dto.CardNumber = item.Card?.Number;
            dto.CardExpMonth = item.Card?.ExpMonth;
            dto.CardExpYear = item.Card?.ExpYear;
            dto.CardCode = item.Card?.Code;
        }

        return dto;
    }
}
