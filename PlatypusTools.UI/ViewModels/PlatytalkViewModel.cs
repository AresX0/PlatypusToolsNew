using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PlatypusTools.Core.Services.Platytalk;

namespace PlatypusTools.UI.ViewModels
{
    public sealed class DisappearingOption
    {
        public int Seconds { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    public sealed class PlatytalkViewModel : BindableBase, IDisposable
    {
        // Signal-style TTL options: off, 30s, 5m, 1h, 8h, 1d, 1w, 4w.
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

        private readonly PlatytalkService _svc;
        private readonly PlatytalkBackupService _backup;

        /// <summary>Local PIN-lock + idle-timeout (15-min default) for this view.</summary>
        public PlatytalkLockManager Lock { get; }

        public PlatytalkViewModel() : this(new PlatytalkService()) { }

        public PlatytalkViewModel(PlatytalkService service)
        {
            _svc = service;
            _backup = new PlatytalkBackupService(_svc.Relay);
            Lock = new PlatytalkLockManager();
            // If we restored an already-signed-in session at startup, immediately require the PIN.
            if (_svc.IsRegistered) Lock.OnSignedIn();
            _svc.StatusChanged += (_, s) =>
            {
                Status = s;
                // The async /v1/me refresh in PlatytalkService raises StatusChanged once
                // it populates the handle on a restored session — re-bind the rail label.
                OnPropertyChanged(nameof(SignedInHandle));
            };
            _svc.MessageReceived += (_, m) =>
            {
                if (SelectedConversation?.ConversationId == m.ConversationId)
                    OnPropertyChanged(nameof(MessagesView));
            };

            StartRegistrationCommand = new RelayCommand(async _ => await StartRegistrationAsync(), _ => !IsRegistered);
            CompleteRegistrationCommand = new RelayCommand(async _ => await CompleteRegistrationAsync(), _ => Challenge != null);
            AddContactCommand = new RelayCommand(async _ => await AddContactAsync(), _ => IsRegistered && !string.IsNullOrWhiteSpace(NewContactHandle));
            StartChatCommand = new RelayCommand(_ => StartChat(), _ => SelectedContact != null);
            CreateGroupCommand = new RelayCommand(async _ => await CreateGroupAsync(), _ => IsRegistered && !string.IsNullOrWhiteSpace(NewGroupTitle));
            SendCommand = new RelayCommand(async _ => await SendAsync(), _ => SelectedConversation != null && !string.IsNullOrWhiteSpace(Composer));
            DeleteEverywhereCommand = new RelayCommand(async p => await DeleteEverywhereAsync(p as PlatytalkMessage), _ => true);
            CopyInviteLinkCommand = new RelayCommand(_ => CopyInviteLink(), _ => IsRegistered);
            ToggleDisappearingCommand = new RelayCommand(_ => ToggleDisappearing(), _ => SelectedConversation != null);
            WipeDataCommand = new RelayCommand(_ => WipeData());
            SignInWithMicrosoftCommand = new RelayCommand(async _ => await SignInWithMicrosoftAsync(), _ => !IsRegistered && !IsSigningIn);
            SignOutCommand = new RelayCommand(_ => SignOut(), _ => IsRegistered);
            BeginEditHandleCommand = new RelayCommand(_ => BeginEditHandle(), _ => IsRegistered);
            CancelEditHandleCommand = new RelayCommand(_ => IsEditingHandle = false);
            SaveHandleCommand = new RelayCommand(async _ => await SaveHandleAsync(), _ => IsEditingHandle && !string.IsNullOrWhiteSpace(HandleEditValue));
            CreateBackupCommand = new RelayCommand(async _ => await CreateBackupAsync(), _ => IsRegistered && !string.IsNullOrEmpty(BackupPassphrase));
            RestoreBackupCommand = new RelayCommand(async _ => await RestoreBackupAsync(), _ => IsRegistered && !string.IsNullOrEmpty(BackupPassphrase));
        }

        public ObservableCollection<PlatytalkContact> Contacts => _svc.Contacts;
        public ObservableCollection<PlatytalkConversation> Conversations => _svc.Conversations;

        private PlatytalkContact? _selectedContact;
        public PlatytalkContact? SelectedContact { get => _selectedContact; set { _selectedContact = value; OnPropertyChanged(); } }

        private PlatytalkConversation? _selectedConversation;
        public PlatytalkConversation? SelectedConversation
        {
            get => _selectedConversation;
            set
            {
                _selectedConversation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MessagesView));
                OnPropertyChanged(nameof(InviteLink));
                OnPropertyChanged(nameof(IsConversationSelected));
                OnPropertyChanged(nameof(SelectedDisappearingSeconds));
                OnPropertyChanged(nameof(DisappearingStatusText));
            }
        }

        public bool IsConversationSelected => _selectedConversation != null;

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
                var ts = SelectedConversation.DisappearingAfter.Value;
                return $"disappearing after {FormatTtl((int)ts.TotalSeconds)}";
            }
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
                    var cid = _svc.BuildDirectConversationId(other);
                    await _svc.SetConvSettingsAsync(cid, seconds);
                }
                else
                {
                    await _svc.UpdateServerGroupSettingsAsync(conv.ConversationId, disappearingSeconds: seconds);
                }
                Status = seconds == 0 ? "Disappearing messages disabled" : $"Disappearing TTL: {FormatTtl(seconds)}";
            }
            catch (Exception ex) { Status = "TTL sync failed: " + ex.Message; }
        }

        public ObservableCollection<PlatytalkMessage>? MessagesView =>
            SelectedConversation is null ? null : _svc.GetMessages(SelectedConversation.ConversationId);

        private string _newContactHandle = string.Empty;
        public string NewContactHandle { get => _newContactHandle; set { _newContactHandle = value; OnPropertyChanged(); } }

        private string _newGroupTitle = string.Empty;
        public string NewGroupTitle { get => _newGroupTitle; set { _newGroupTitle = value; OnPropertyChanged(); } }

        private string _composer = string.Empty;
        public string Composer { get => _composer; set { _composer = value; OnPropertyChanged(); } }

        private string _registerHandle = string.Empty;
        public string RegisterHandle { get => _registerHandle; set { _registerHandle = value; OnPropertyChanged(); } }

        private string _displayName = Environment.UserName;
        public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }

        private VerificationChannel _channel = VerificationChannel.Sms;
        public VerificationChannel Channel { get => _channel; set { _channel = value; OnPropertyChanged(); } }

        private string _otpCode = string.Empty;
        public string OtpCode { get => _otpCode; set { _otpCode = value; OnPropertyChanged(); } }

        private RegistrationChallenge? _challenge;
        public RegistrationChallenge? Challenge { get => _challenge; set { _challenge = value; OnPropertyChanged(); } }

        public bool IsRegistered => _svc.IsRegistered;

        private string _status = "Idle";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        public string InviteLink => _svc.IsRegistered ? _svc.BuildInviteLink() : string.Empty;

        /// <summary>PNG bytes of the QR code for the current invite link, or empty when signed out.</summary>
        public byte[] InviteQrPng => _svc.IsRegistered
            ? PlatytalkQr.EncodePng(_svc.BuildInviteLink())
            : Array.Empty<byte>();

        public BitmapImage? InviteQrSource
        {
            get
            {
                var bytes = InviteQrPng;
                if (bytes.Length == 0) return null;
                var img = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        public ICommand StartRegistrationCommand { get; }
        public ICommand CompleteRegistrationCommand { get; }
        public ICommand AddContactCommand { get; }
        public ICommand StartChatCommand { get; }
        public ICommand CreateGroupCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand DeleteEverywhereCommand { get; }
        public ICommand CopyInviteLinkCommand { get; }
        public ICommand ToggleDisappearingCommand { get; }
        public ICommand WipeDataCommand { get; }
        public ICommand SignInWithMicrosoftCommand { get; }
        public ICommand SignOutCommand { get; }
        public ICommand BeginEditHandleCommand { get; }
        public ICommand CancelEditHandleCommand { get; }
        public ICommand SaveHandleCommand { get; }
        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }

        // ----- Edit handle -----
        private bool _isEditingHandle;
        public bool IsEditingHandle { get => _isEditingHandle; set { _isEditingHandle = value; OnPropertyChanged(); } }
        private string _handleEditValue = string.Empty;
        public string HandleEditValue { get => _handleEditValue; set { _handleEditValue = value; OnPropertyChanged(); } }
        private string _handleEditError = string.Empty;
        public string HandleEditError { get => _handleEditError; set { _handleEditError = value; OnPropertyChanged(); } }

        private void BeginEditHandle()
        {
            HandleEditValue = SignedInHandle;
            HandleEditError = string.Empty;
            IsEditingHandle = true;
        }
        private async Task SaveHandleAsync()
        {
            try
            {
                HandleEditError = string.Empty;
                await _svc.SetHandleAsync(HandleEditValue.Trim().ToLowerInvariant());
                IsEditingHandle = false;
                OnPropertyChanged(nameof(SignedInHandle));
                OnPropertyChanged(nameof(InviteLink));
                OnPropertyChanged(nameof(InviteQrPng));
                OnPropertyChanged(nameof(InviteQrSource));
            }
            catch (PlatytalkRelayException ex) when (ex.Message.Contains("409")) { HandleEditError = "That handle is taken."; }
            catch (PlatytalkRelayException ex) when (ex.Message.Contains("400")) { HandleEditError = "Use 3-32 lowercase letters/numbers/hyphens."; }
            catch (Exception ex) { HandleEditError = ex.Message; }
        }

        // ----- Backup -----
        private string _backupPassphrase = string.Empty;
        public string BackupPassphrase { get => _backupPassphrase; set { _backupPassphrase = value; OnPropertyChanged(); } }

        private async Task CreateBackupAsync()
        {
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

        private async Task RestoreBackupAsync()
        {
            try
            {
                Status = "Downloading and decrypting backup…";
                var snap = await _backup.RestoreAsync(BackupPassphrase);
                if (snap is null) { Status = "No backup found for this account."; return; }
                _svc.ApplySnapshot(snap);
                BackupPassphrase = string.Empty;
                OnPropertyChanged(nameof(Contacts));
                OnPropertyChanged(nameof(Conversations));
            }
            catch (System.Security.Cryptography.CryptographicException) { Status = "Wrong passphrase or corrupt backup."; }
            catch (Exception ex) { Status = "Restore failed: " + ex.Message; }
        }

        private bool _isSigningIn;
        public bool IsSigningIn { get => _isSigningIn; set { _isSigningIn = value; OnPropertyChanged(); } }

        public string SignedInHandle => string.IsNullOrWhiteSpace(_svc.Identity?.Handle)
            ? (_svc.Identity?.DisplayName ?? string.Empty)
            : ("@" + _svc.Identity!.Handle);

        private async Task SignInWithMicrosoftAsync()
        {
            try
            {
                IsSigningIn = true;
                Status = "Opening browser for Microsoft sign-in…";
                var result = await _svc.SignInWithMicrosoftAsync();
                Status = $"Signed in as @{result.Handle}";
                OnPropertyChanged(nameof(IsRegistered));
                OnPropertyChanged(nameof(InviteLink));
                OnPropertyChanged(nameof(SignedInHandle));
                Lock.OnSignedIn();
                try { await _svc.ConnectAsync(); } catch { /* WS will retry */ }
            }
            catch (Exception ex) { Status = "Sign-in failed: " + ex.Message; }
            finally { IsSigningIn = false; }
        }

        private void SignOut()
        {
            _svc.SignOut();
            Lock.OnSignedOut();
            OnPropertyChanged(nameof(IsRegistered));
            OnPropertyChanged(nameof(InviteLink));
            OnPropertyChanged(nameof(SignedInHandle));
            Status = "Signed out";
        }

        private async Task StartRegistrationAsync()
        {
            try { Challenge = await _svc.StartRegistrationAsync(RegisterHandle, Channel); Status = "Code sent — enter it below"; }
            catch (Exception ex) { Status = "Failed: " + ex.Message; }
        }

        private async Task CompleteRegistrationAsync()
        {
            if (Challenge is null) return;
            try
            {
                await _svc.CompleteRegistrationAsync(Challenge, OtpCode, DisplayName, Environment.MachineName);
                OnPropertyChanged(nameof(IsRegistered));
                OnPropertyChanged(nameof(InviteLink));
                Status = "Registered";
            }
            catch (Exception ex) { Status = "Failed: " + ex.Message; }
        }

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

        private void StartChat()
        {
            if (SelectedContact is null) return;
            SelectedConversation = _svc.StartDirectConversation(SelectedContact);
        }

        private async Task CreateGroupAsync()
        {
            try
            {
                var members = Contacts.Where(c => c.IsVerified).ToList();
                if (members.Count == 0) members = Contacts.ToList();
                var resp = await _svc.CreateServerGroupAsync(NewGroupTitle, members.Select(c => c.ContactId), disappearingSeconds: 0);
                if (resp.Group is null) { Status = "Group create failed (empty response)"; return; }
                var conv = _svc.CreateGroup(resp.Group.Name, members);
                // align local id with server id so settings sync targets the right row
                Conversations.Remove(conv);
                var serverConv = new PlatytalkConversation
                {
                    ConversationId = resp.Group.Id,
                    Kind = ConversationKind.Group,
                    Title = resp.Group.Name,
                    DisappearingAfter = resp.Group.DisappearingSeconds > 0 ? TimeSpan.FromSeconds(resp.Group.DisappearingSeconds) : (TimeSpan?)null,
                };
                foreach (var m in resp.Group.Members) serverConv.ParticipantIds.Add(m.Id);
                Conversations.Add(serverConv);
                SelectedConversation = serverConv;
                NewGroupTitle = string.Empty;
                Status = $"Group '{resp.Group.Name}' created.";
            }
            catch (Exception ex) { Status = "Group create failed: " + ex.Message; }
        }

        private void CreateGroup()
        {
            // Legacy local-only fallback (kept for offline/first-run scenarios).
            var members = Contacts.Where(c => c.IsVerified).ToList();
            if (members.Count == 0) members = Contacts.ToList();
            SelectedConversation = _svc.CreateGroup(NewGroupTitle, members);
            NewGroupTitle = string.Empty;
        }

        private async Task SendAsync()
        {
            if (SelectedConversation is null) return;
            var body = Composer;
            Composer = string.Empty;
            await _svc.SendMessageAsync(SelectedConversation, body);
        }

        private async Task DeleteEverywhereAsync(PlatytalkMessage? msg)
        {
            if (msg is null) return;
            await _svc.DeleteMessageEverywhereAsync(msg);
        }

        private void CopyInviteLink()
        {
            try { System.Windows.Clipboard.SetText(InviteLink); Status = "Invite link copied"; }
            catch { Status = "Clipboard unavailable"; }
        }

        private void ToggleDisappearing()
        {
            if (SelectedConversation is null) return;
            SelectedConversation.DisappearingAfter = SelectedConversation.DisappearingAfter is null
                ? TimeSpan.FromHours(24)
                : (TimeSpan?)null;
            OnPropertyChanged(nameof(SelectedConversation));
        }

        private void WipeData()
        {
            _svc.WipeLocalData();
            OnPropertyChanged(nameof(IsRegistered));
            OnPropertyChanged(nameof(InviteLink));
        }

        public void Dispose()
        {
            try { _svc.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* best effort during shutdown */ }
        }
    }
}
