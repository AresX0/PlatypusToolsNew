using System.Net.Http.Json;
using PlatypusTools.Remote.Client.Models;

namespace PlatypusTools.Remote.Client.Services;

/// <summary>
/// Client-side service for vault operations via REST API with authenticated HttpClient.
/// </summary>
public class VaultApiService
{
    private readonly HttpClient _httpClient;

    public event Action? OnVaultStateChanged;

    public VaultStatusDto? CurrentStatus { get; private set; }
    public bool IsUnlocked => CurrentStatus?.IsUnlocked ?? false;

    public VaultApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<VaultStatusDto?> GetStatusAsync()
    {
        try
        {
            CurrentStatus = await _httpClient.GetFromJsonAsync<VaultStatusDto>("/api/vault/status");
            OnVaultStateChanged?.Invoke();
            return CurrentStatus;
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Success, string? Error)> UnlockAsync(string masterPassword)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/vault/unlock",
                new VaultUnlockRequest { MasterPassword = masterPassword });

            if (response.IsSuccessStatusCode)
            {
                CurrentStatus = await response.Content.ReadFromJsonAsync<VaultStatusDto>();
                OnVaultStateChanged?.Invoke();
                return (true, null);
            }
            return (false, "Invalid master password");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task LockAsync()
    {
        try
        {
            await _httpClient.PostAsync("/api/vault/lock", null);
            CurrentStatus = new VaultStatusDto { VaultExists = CurrentStatus?.VaultExists ?? false };
            OnVaultStateChanged?.Invoke();
        }
        catch
        {
            // Ignore lock errors
        }
    }

    public async Task<List<VaultItemDto>> GetItemsAsync(string? filter = null, int? type = null, string? folderId = null)
    {
        try
        {
            var query = new List<string>();
            if (!string.IsNullOrEmpty(filter)) query.Add($"filter={Uri.EscapeDataString(filter)}");
            if (type.HasValue) query.Add($"type={type.Value}");
            if (!string.IsNullOrEmpty(folderId)) query.Add($"folderId={Uri.EscapeDataString(folderId)}");

            var url = "/api/vault/items";
            if (query.Count > 0) url += "?" + string.Join("&", query);

            return await _httpClient.GetFromJsonAsync<List<VaultItemDto>>(url) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public async Task<VaultItemDto?> GetItemAsync(string id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<VaultItemDto>($"/api/vault/items/{id}");
        }
        catch
        {
            return null;
        }
    }

    public async Task<VaultItemDto?> AddItemAsync(AddVaultItemRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/vault/items", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<VaultItemDto>();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeleteItemAsync(string id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/vault/items/{id}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<VaultFolderDto>> GetFoldersAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<VaultFolderDto>>("/api/vault/folders") ?? new();
        }
        catch
        {
            return new();
        }
    }

    public async Task<List<AuthenticatorEntryDto>> GetAuthenticatorEntriesAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<AuthenticatorEntryDto>>("/api/vault/authenticator") ?? new();
        }
        catch
        {
            return new();
        }
    }

    public async Task<AuthenticatorEntryDto?> AddAuthenticatorEntryAsync(AddAuthenticatorRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/vault/authenticator", request);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<AuthenticatorEntryDto>();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeleteAuthenticatorEntryAsync(string id)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/vault/authenticator/{id}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GeneratePasswordAsync(GeneratePasswordRequest? request = null)
    {
        try
        {
            request ??= new GeneratePasswordRequest();
            var response = await _httpClient.PostAsJsonAsync("/api/vault/generate-password", request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PasswordResult>();
                return result?.Password;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private class PasswordResult
    {
        public string Password { get; set; } = string.Empty;
    }
}
