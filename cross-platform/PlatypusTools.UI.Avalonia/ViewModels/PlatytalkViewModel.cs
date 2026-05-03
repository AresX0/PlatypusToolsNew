using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.Core.Services.Platytalk;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public sealed class DisappearingOption
{
    public int Seconds { get; init; }
    public string Label { get; init; } = string.Empty;
}

public partial class PlatytalkViewModel : ObservableObject, IDisposable
{
    public static readonly IReadOnlyList<DisappearingOption> AllDisappearingOptions = new List<DisappearingOption>
    {
        new() { Seconds = 0,        Label = "Off" },
        new() { Seconds = 30,       Label = "30 seconds" },
        new() { Seconds = 300,      Label = "5 minutes" },
        new() { Seconds = 3600,     Label = "1 hour" },
        new() { Seconds = 28800,    Label = "8 hours" },
        new() { Seconds = 86400,    Label = "1 day" },
        new() { Seconds = 604800,   Label = "1 week" },
        new() { Seconds = 2419200,  Label = "4 weeks" },
    };

    private readonly PlatytalkService _svc = new();
    private readonly PlatytalkBackupService _backup;

    [ObservableProperty] private string _displayName = Environment.UserName;

    [ObservableProperty] private string _newContactHandle = string.Empty;
    [ObservableProperty] private string _newGroupTitle = string.Empty;
    [ObservableProperty] private string _composer = string.Empty;
    [ObservableProperty] private string _status = "Idle";

    [ObservableProperty] private PlatytalkContact? _selectedContact;
    [ObservableProperty] private PlatytalkConversation? _selectedConversation;
    [ObservableProperty] private ObservableCollection<PlatytalkMessage>? _messagesView;

    [ObservableProperty] private bool _isEditingHandle;
    [ObservableProperty] private string _handleEditValue = string.Empty;
    [ObservableProperty] private string _handleEditError = string.Empty;
    [ObservableProperty] private string _backupPassphrase = string.Empty;

    public ObservableCollection<PlatytalkContact> Contacts => _svc.Contacts;
    public ObservableCollection<PlatytalkConversation> Conversations => _svc.Conversations;

    public bool IsRegistered => _svc.IsRegistered;
    public bool IsConversationSelected => SelectedConversation is not null;
    public string InviteLink => _svc.IsRegistered ? _svc.BuildInviteLink() : string.Empty;
    public string SignedInHandle => _svc.Identity?.DisplayName ?? string.Empty;

    public IReadOnlyList<DisappearingOption> DisappearingOptions => AllDisappearingOptions;

    public int SelectedDisappearingSeconds
    {
        get => SelectedConversation?.DisappearingAfter is { } ts ? (int)Math.Min(ts.TotalSeconds, int.MaxValue) : 0;
        set
        {
            if (SelectedConversation is null) return;
            var newTs = value > 0 ? TimeSpan.FromSeconds(value) : (TimeSpan?)null;
            if (SelectedConversation.DisappearingAfter == newTs) return;
            SelectedConversation.DisappearingAfter = newTs;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisappearingStatusText));
            _ = SyncDisappearingToServerAsync(SelectedConversation, value);
        }
    }

    public string DisappearingStatusText
    {
        get
        {
            if (SelectedConversation is null) return "no conversation";
            if (SelectedConversation.DisappearingAfter is null) return "disappearing: off";
            return $"disappearing after {FormatTtl((int)SelectedConversation.DisappearingAfter.Value.TotalSeconds)}";
        }
    }

    public Bitmap? InviteQrSource
    {
        get
        {
            if (!_svc.IsRegistered) return null;
            var bytes = PlatytalkQr.EncodePng(_svc.BuildInviteLink());
            if (bytes.Length == 0) return null;
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
    }

    public PlatytalkViewModel()
    {
        _backup = new PlatytalkBackupService(_svc.Relay);
        _svc.StatusChanged += (_, s) => Status = s;
        _svc.MessageReceived += (_, m) =>
        {
            if (SelectedConversation?.ConversationId == m.ConversationId)
                MessagesView = _svc.GetMessages(m.ConversationId);
        };
    }

    partial void OnSelectedConversationChanged(PlatytalkConversation? value)
    {
        MessagesView = value is null ? null : _svc.GetMessages(value.ConversationId);
        OnPropertyChanged(nameof(IsConversationSelected));
        OnPropertyChanged(nameof(SelectedDisappearingSeconds));
        OnPropertyChanged(nameof(DisappearingStatusText));
    }

    private static string FormatTtl(int seconds) => seconds switch
    {
        <= 0 => "off",
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60}m",
        < 86400 => $"{seconds / 3600}h",
        < 604800 => $"{seconds / 86400}d",
        _ => $"{seconds / 604800}w",
    };

    private async Task SyncDisappearingToServerAsync(PlatytalkConversation conv, int seconds)
    {
        try
        {
            if (conv.Kind == ConversationKind.Direct)
            {
                var other = conv.ParticipantIds.FirstOrDefault(p => p != _svc.Identity?.UserId);
                if (string.IsNullOrEmpty(other)) return;
                await _svc.SetConvSettingsAsync(_svc.BuildDirectConversationId(other), seconds);
            }
            else
            {
                await _svc.UpdateServerGroupSettingsAsync(conv.ConversationId, disappearingSeconds: seconds);
            }
            Status = seconds == 0 ? "Disappearing messages disabled" : $"Disappearing TTL: {FormatTtl(seconds)}";
        }
        catch (Exception ex) { Status = "TTL sync failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task SignInWithMicrosoftAsync()
    {
        try
        {
            Status = "Opening browser for Microsoft sign-in…";
            var result = await _svc.SignInWithMicrosoftAsync();
            Status = $"Signed in as @{result.Handle}";
            OnPropertyChanged(nameof(IsRegistered));
            OnPropertyChanged(nameof(InviteLink));
            OnPropertyChanged(nameof(SignedInHandle));
            OnPropertyChanged(nameof(InviteQrSource));
            try { await _svc.ConnectAsync(); } catch { }
        }
        catch (Exception ex) { Status = "Sign-in failed: " + ex.Message; }
    }

    [RelayCommand]
    private void SignOut()
    {
        _svc.SignOut();
        OnPropertyChanged(nameof(IsRegistered));
        OnPropertyChanged(nameof(InviteLink));
        OnPropertyChanged(nameof(SignedInHandle));
        OnPropertyChanged(nameof(InviteQrSource));
        Status = "Signed out";
    }

    [RelayCommand]
    private async Task AddContactAsync()
    {
        try
        {
            var c = await _svc.AddContactByHandleAsync(NewContactHandle);
            Status = c is null ? "Not found" : $"Added {c.DisplayName}";
            NewContactHandle = string.Empty;
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private void StartChat()
    {
        if (SelectedContact is null) return;
        SelectedConversation = _svc.StartDirectConversation(SelectedContact);
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        try
        {
            var members = Contacts.ToList();
            if (members.Count == 0) { Status = "Add contacts first"; return; }
            var resp = await _svc.CreateServerGroupAsync(NewGroupTitle, members.Select(c => c.ContactId), 0);
            if (resp.Group is null) { Status = "Group create failed."; return; }
            var conv = new PlatytalkConversation
            {
                ConversationId = resp.Group.Id,
                Kind = ConversationKind.Group,
                Title = resp.Group.Name,
                DisappearingAfter = resp.Group.DisappearingSeconds > 0 ? TimeSpan.FromSeconds(resp.Group.DisappearingSeconds) : (TimeSpan?)null,
            };
            foreach (var m in resp.Group.Members) conv.ParticipantIds.Add(m.Id);
            Conversations.Add(conv);
            SelectedConversation = conv;
            NewGroupTitle = string.Empty;
            Status = $"Group '{resp.Group.Name}' created.";
        }
        catch (Exception ex) { Status = "Group create failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (SelectedConversation is null || string.IsNullOrWhiteSpace(Composer)) return;
        var body = Composer;
        Composer = string.Empty;
        await _svc.SendMessageAsync(SelectedConversation, body);
    }

    [RelayCommand]
    private async Task DeleteEverywhereAsync(PlatytalkMessage? msg)
    {
        if (msg is null) return;
        await _svc.DeleteMessageEverywhereAsync(msg);
    }

    [RelayCommand]
    private void WipeData()
    {
        _svc.WipeLocalData();
        OnPropertyChanged(nameof(IsRegistered));
        OnPropertyChanged(nameof(InviteLink));
        OnPropertyChanged(nameof(InviteQrSource));
    }

    [RelayCommand]
    private void BeginEditHandle()
    {
        HandleEditValue = SignedInHandle;
        HandleEditError = string.Empty;
        IsEditingHandle = true;
    }

    [RelayCommand]
    private void CancelEditHandle() => IsEditingHandle = false;

    [RelayCommand]
    private async Task SaveHandleAsync()
    {
        if (string.IsNullOrWhiteSpace(HandleEditValue)) return;
        try
        {
            HandleEditError = string.Empty;
            await _svc.SetHandleAsync(HandleEditValue.Trim().ToLowerInvariant());
            IsEditingHandle = false;
            OnPropertyChanged(nameof(SignedInHandle));
            OnPropertyChanged(nameof(InviteLink));
            OnPropertyChanged(nameof(InviteQrSource));
        }
        catch (PlatytalkRelayException ex) when (ex.Message.Contains("409")) { HandleEditError = "That handle is taken."; }
        catch (PlatytalkRelayException ex) when (ex.Message.Contains("400")) { HandleEditError = "Use 3-32 lowercase letters/numbers/hyphens."; }
        catch (Exception ex) { HandleEditError = ex.Message; }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (string.IsNullOrEmpty(BackupPassphrase)) { Status = "Enter a backup passphrase first."; return; }
        try
        {
            Status = "Encrypting backup…";
            var snap = _svc.CaptureSnapshot();
            var resp = await _backup.CreateAsync(snap, BackupPassphrase);
            Status = $"Backup uploaded (v{resp.Version}).";
            BackupPassphrase = string.Empty;
        }
        catch (Exception ex) { Status = "Backup failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (string.IsNullOrEmpty(BackupPassphrase)) { Status = "Enter the backup passphrase."; return; }
        try
        {
            Status = "Downloading and decrypting backup…";
            var snap = await _backup.RestoreAsync(BackupPassphrase);
            if (snap is null) { Status = "No backup found for this account."; return; }
            _svc.ApplySnapshot(snap);
            BackupPassphrase = string.Empty;
        }
        catch (System.Security.Cryptography.CryptographicException) { Status = "Wrong passphrase or corrupt backup."; }
        catch (Exception ex) { Status = "Restore failed: " + ex.Message; }
    }

    public void Dispose()
    {
        try { _svc.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { }
    }
}
