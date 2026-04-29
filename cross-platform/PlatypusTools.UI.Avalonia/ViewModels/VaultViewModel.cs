using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class VaultViewModel : ObservableObject
{
    private readonly VaultService _service = new();
    private string? _masterPassword;

    [ObservableProperty] private bool _isLocked = true;
    [ObservableProperty] private bool _vaultExists;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private VaultEntry? _selectedEntry;
    [ObservableProperty] private string _newTitle = "";
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPasswordValue = "";
    [ObservableProperty] private string _newUrl = "";
    [ObservableProperty] private string _newNotes = "";

    public ObservableCollection<VaultEntry> Entries { get; } = new();

    public string VaultPath => _service.VaultPath;

    public VaultViewModel()
    {
        VaultExists = _service.VaultExists;
        Status = VaultExists
            ? "Enter master password to unlock."
            : "No vault found. Set a master password to create one.";
    }

    [RelayCommand]
    private async Task UnlockOrCreateAsync()
    {
        try
        {
            if (_service.VaultExists)
            {
                var payload = await _service.UnlockAsync(Password);
                Entries.Clear();
                foreach (var e in payload.Entries) Entries.Add(e);
                _masterPassword = Password;
                Password = "";
                IsLocked = false;
                Status = $"Unlocked. {Entries.Count} entries.";
            }
            else
            {
                if (Password.Length < 8)
                { Status = "Master password must be at least 8 characters."; return; }
                if (Password != ConfirmPassword)
                { Status = "Passwords do not match."; return; }
                await _service.CreateAsync(Password);
                _masterPassword = Password;
                Password = "";
                ConfirmPassword = "";
                VaultExists = true;
                IsLocked = false;
                Status = "Vault created.";
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Status = "Wrong master password (decryption failed).";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Lock()
    {
        Entries.Clear();
        _masterPassword = null;
        IsLocked = true;
        Status = "Locked.";
    }

    [RelayCommand]
    private async Task AddEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTitle)) { Status = "Title required."; return; }
        var entry = new VaultEntry
        {
            Title = NewTitle.Trim(),
            Username = NewUsername,
            Password = NewPasswordValue,
            Url = NewUrl,
            Notes = NewNotes
        };
        Entries.Add(entry);
        NewTitle = NewUsername = NewPasswordValue = NewUrl = NewNotes = "";
        await PersistAsync();
        Status = $"Added '{entry.Title}'.";
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is null) return;
        var t = SelectedEntry.Title;
        Entries.Remove(SelectedEntry);
        await PersistAsync();
        Status = $"Deleted '{t}'.";
    }

    [RelayCommand]
    private async Task CopyPasswordAsync()
    {
        if (SelectedEntry is null) return;
        try
        {
            var top = global::Avalonia.Application.Current?.ApplicationLifetime
                is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow : null;
            var clip = top?.Clipboard;
            if (clip is not null)
            {
                await clip.SetTextAsync(SelectedEntry.Password);
                Status = "Password copied to clipboard.";
            }
        }
        catch (Exception ex) { Status = $"Clipboard failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var sb = new System.Text.StringBuilder();
        foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
        NewPasswordValue = sb.ToString();
    }

    private async Task PersistAsync()
    {
        if (_masterPassword is null) return;
        var payload = new VaultPayload { Entries = Entries.ToList() };
        await _service.SaveAsync(payload, _masterPassword);
    }
}
