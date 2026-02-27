using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PlatypusTools.UI.Services.Vault;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Security Vault (Password Manager + Authenticator + Cloud Sync).
    /// Mirrors Bitwarden's feature set: vault items, TOTP authenticator, password generator,
    /// vault health reports, and cloud sync via OneDrive/Google Drive.
    /// </summary>
    public class SecurityVaultViewModel : BindableBase
    {
        private readonly EncryptedVaultService _vaultService = new();
        private readonly VaultCloudSyncService _syncService = new();
        private readonly VaultMfaService _mfaService = new();
        private readonly DispatcherTimer _totpTimer;
        private VaultDatabase? _vault;
        private string? _pendingMasterPassword; // Held between password validation and MFA verification
        private System.Threading.Timer? _clipboardClearTimer;

        #region Constructor

        public SecurityVaultViewModel()
        {
            // Collections
            AllItems = new ObservableCollection<VaultItem>();
            FilteredItems = new ObservableCollection<VaultItem>();
            Folders = new ObservableCollection<VaultFolder>();
            AuthenticatorEntries = new ObservableCollection<AuthenticatorEntry>();
            HealthWeakItems = new ObservableCollection<VaultItem>();
            HealthReusedItems = new ObservableCollection<VaultItem>();

            // Item Type filter options
            ItemTypeFilters = new ObservableCollection<string>
            {
                "All Items", "Logins", "Cards", "Identities", "Secure Notes", "Favorites"
            };
            SelectedItemTypeFilter = "All Items";

            // Password generator defaults
            GeneratorOptions = new PasswordGeneratorOptions();
            GeneratedPassword = PasswordGeneratorService.GeneratePassword(GeneratorOptions);
            UpdatePasswordStrength();

            // Commands
            CreateVaultCommand = new RelayCommand(async _ => await CreateVaultAsync());
            UnlockVaultCommand = new RelayCommand(async _ => await UnlockVaultAsync());
            LockVaultCommand = new RelayCommand(_ => LockVault());
            ChangeMasterPasswordCommand = new RelayCommand(async _ => await ChangeMasterPasswordAsync(), _ => IsUnlocked);

            AddLoginCommand = new RelayCommand(_ => AddNewItem(VaultItemType.Login), _ => IsUnlocked);
            AddCardCommand = new RelayCommand(_ => AddNewItem(VaultItemType.Card), _ => IsUnlocked);
            AddIdentityCommand = new RelayCommand(_ => AddNewItem(VaultItemType.Identity), _ => IsUnlocked);
            AddSecureNoteCommand = new RelayCommand(_ => AddNewItem(VaultItemType.SecureNote), _ => IsUnlocked);
            SaveItemCommand = new RelayCommand(async _ => await SaveCurrentItemAsync(), _ => IsUnlocked && SelectedItem != null);
            DeleteItemCommand = new RelayCommand(async _ => await DeleteCurrentItemAsync(), _ => IsUnlocked && SelectedItem != null);
            CopyUsernameCommand = new RelayCommand(_ => CopyToClipboard(SelectedItem?.Login?.Username), _ => SelectedItem?.Login?.Username != null);
            CopyPasswordCommand = new RelayCommand(_ => CopyToClipboard(SelectedItem?.Login?.Password), _ => SelectedItem?.Login?.Password != null);
            CopyTotpCommand = new RelayCommand(_ => CopyCurrentTotp(), _ => SelectedItem?.Login?.TotpSecret != null);
            CopyCardNumberCommand = new RelayCommand(_ => CopyToClipboard(SelectedItem?.Card?.Number), _ => SelectedItem?.Card?.Number != null);
            CopyCardCvvCommand = new RelayCommand(_ => CopyToClipboard(SelectedItem?.Card?.Code), _ => SelectedItem?.Card?.Code != null);
            LaunchUriCommand = new RelayCommand(_ => LaunchUri(), _ => SelectedItem?.Login?.Uris?.Any(u => !string.IsNullOrEmpty(u.Uri)) == true);
            ToggleFavoriteCommand = new RelayCommand(async _ => await ToggleFavoriteAsync(), _ => IsUnlocked && SelectedItem != null);

            AddFolderCommand = new RelayCommand(async _ => await AddFolderAsync(), _ => IsUnlocked);
            DeleteFolderCommand = new RelayCommand(async _ => await DeleteFolderAsync(), _ => IsUnlocked && SelectedFolder != null);
            ClearFolderFilterCommand = new RelayCommand(_ => SelectedFolder = null);

            AddAuthEntryCommand = new RelayCommand(_ => AddAuthenticatorEntry(), _ => IsUnlocked);
            DeleteAuthEntryCommand = new RelayCommand(async _ => await DeleteAuthenticatorEntryAsync(), _ => IsUnlocked && SelectedAuthEntry != null);
            CopyAuthCodeCommand = new RelayCommand(_ => CopyAuthCode(), _ => SelectedAuthEntry != null);

            GeneratePasswordCommand = new RelayCommand(_ => RegeneratePassword());
            CopyGeneratedPasswordCommand = new RelayCommand(_ => CopyToClipboard(GeneratedPassword));
            ApplyGeneratedPasswordCommand = new RelayCommand(_ => ApplyGeneratedPassword(), _ => SelectedItem?.Type == VaultItemType.Login);

            RunHealthCheckCommand = new RelayCommand(_ => RunHealthCheck(), _ => IsUnlocked);

            SignInMicrosoftCommand = new RelayCommand(async _ => await SignInMicrosoftAsync());
            SignInGoogleCommand = new RelayCommand(async _ => await SignInGoogleAsync());
            SignOutCloudCommand = new RelayCommand(async _ => await SignOutCloudAsync(), _ => IsSyncConnected);
            SyncNowCommand = new RelayCommand(async _ => await SyncNowAsync(), _ => IsUnlocked && IsSyncConnected);

            ExportVaultCommand = new RelayCommand(async _ => await ExportVaultAsync(), _ => IsUnlocked);
            ImportVaultCommand = new RelayCommand(async _ => await ImportVaultAsync(), _ => IsUnlocked);

            // MFA
            VerifyMfaCommand = new RelayCommand(async _ => await VerifyMfaAsync());
            CancelMfaCommand = new RelayCommand(_ => CancelMfa());
            SetupTotpMfaCommand = new RelayCommand(async _ => await SetupTotpMfaAsync(), _ => IsUnlocked);
            FinalizeTotpSetupCommand = new RelayCommand(async _ => await FinalizeTotpSetupAsync(), _ => IsUnlocked);
            SetupWindowsHelloMfaCommand = new RelayCommand(async _ => await SetupWindowsHelloMfaAsync(), _ => IsUnlocked);
            RemoveTotpMfaCommand = new RelayCommand(_ => RemoveTotpMfa(), _ => IsUnlocked);
            RemoveWindowsHelloMfaCommand = new RelayCommand(async _ => await RemoveWindowsHelloMfaAsync(), _ => IsUnlocked);

            AddUriCommand = new RelayCommand(_ => AddUri(), _ => SelectedItem?.Type == VaultItemType.Login);
            RemoveUriCommand = new RelayCommand(_ => RemoveUri(), _ => SelectedItem?.Login?.Uris?.Count > 0);
            AddCustomFieldCommand = new RelayCommand(_ => AddCustomField(), _ => SelectedItem != null);
            RemoveCustomFieldCommand = new RelayCommand(_ => RemoveCustomField(), _ => SelectedItem?.CustomFields?.Count > 0);

            // TOTP timer (updates every second)
            _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _totpTimer.Tick += (_, _) => UpdateTotpCodes();
            _totpTimer.Start();

            // Load sync state
            var syncState = _syncService.LoadSyncState();
            if (syncState.Provider != CloudProvider.None)
            {
                SyncProviderName = syncState.Provider.ToString();
                SyncAccountEmail = syncState.AccountEmail ?? string.Empty;
                IsSyncConnected = true;
                LastSyncTime = syncState.LastSyncTime?.ToString("g") ?? "Never";
            }

            // Check if vault exists
            VaultExists = EncryptedVaultService.VaultExists();
        }

        #endregion

        #region Properties - Vault State

        private bool _isUnlocked;
        public bool IsUnlocked
        {
            get => _isUnlocked;
            set { SetProperty(ref _isUnlocked, value); RaisePropertyChanged(nameof(IsLocked)); }
        }
        public bool IsLocked => !IsUnlocked;

        private bool _vaultExists;
        public bool VaultExists
        {
            get => _vaultExists;
            set => SetProperty(ref _vaultExists, value);
        }

        private string _masterPasswordInput = string.Empty;
        public string MasterPasswordInput
        {
            get => _masterPasswordInput;
            set => SetProperty(ref _masterPasswordInput, value);
        }

        private string _confirmPasswordInput = string.Empty;
        public string ConfirmPasswordInput
        {
            get => _confirmPasswordInput;
            set => SetProperty(ref _confirmPasswordInput, value);
        }

        private string _statusText = "Vault locked.";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        #endregion

        #region Properties - Item List

        public ObservableCollection<VaultItem> AllItems { get; }
        public ObservableCollection<VaultItem> FilteredItems { get; }
        public ObservableCollection<VaultFolder> Folders { get; }

        public ObservableCollection<string> ItemTypeFilters { get; }

        private string _selectedItemTypeFilter = "All Items";
        public string SelectedItemTypeFilter
        {
            get => _selectedItemTypeFilter;
            set { if (SetProperty(ref _selectedItemTypeFilter, value)) ApplyFilter(); }
        }

        private VaultFolder? _selectedFolder;
        public VaultFolder? SelectedFolder
        {
            get => _selectedFolder;
            set { if (SetProperty(ref _selectedFolder, value)) ApplyFilter(); }
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set { if (SetProperty(ref _searchQuery, value)) ApplyFilter(); }
        }

        private VaultItem? _selectedItem;
        public VaultItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    RaisePropertyChanged(nameof(IsItemSelected));
                    RaisePropertyChanged(nameof(IsLoginItem));
                    RaisePropertyChanged(nameof(IsCardItem));
                    RaisePropertyChanged(nameof(IsIdentityItem));
                    RaisePropertyChanged(nameof(IsSecureNoteItem));
                    RaisePropertyChanged(nameof(SelectedItemTotpCode));
                    RaisePropertyChanged(nameof(ItemUris));
                    RaisePropertyChanged(nameof(ItemCustomFields));
                    UpdateTotpCodes();
                }
            }
        }

        public bool IsItemSelected => SelectedItem != null;
        public bool IsLoginItem => SelectedItem?.Type == VaultItemType.Login;
        public bool IsCardItem => SelectedItem?.Type == VaultItemType.Card;
        public bool IsIdentityItem => SelectedItem?.Type == VaultItemType.Identity;
        public bool IsSecureNoteItem => SelectedItem?.Type == VaultItemType.SecureNote;

        public ObservableCollection<LoginUri>? ItemUris => SelectedItem?.Login?.Uris != null
            ? new ObservableCollection<LoginUri>(SelectedItem.Login.Uris)
            : null;

        public ObservableCollection<CustomField>? ItemCustomFields => SelectedItem?.CustomFields != null
            ? new ObservableCollection<CustomField>(SelectedItem.CustomFields)
            : null;

        // Toggle password visibility
        private bool _showPassword;
        public bool ShowPassword
        {
            get => _showPassword;
            set => SetProperty(ref _showPassword, value);
        }

        // Item counts
        public int TotalItemCount => AllItems.Count;
        public int LoginCount => AllItems.Count(i => i.Type == VaultItemType.Login);
        public int CardCount => AllItems.Count(i => i.Type == VaultItemType.Card);
        public int IdentityCount => AllItems.Count(i => i.Type == VaultItemType.Identity);
        public int NoteCount => AllItems.Count(i => i.Type == VaultItemType.SecureNote);
        public int FavoriteCount => AllItems.Count(i => i.Favorite);

        #endregion

        #region Properties - TOTP / Authenticator

        public ObservableCollection<AuthenticatorEntry> AuthenticatorEntries { get; }

        private AuthenticatorEntry? _selectedAuthEntry;
        public AuthenticatorEntry? SelectedAuthEntry
        {
            get => _selectedAuthEntry;
            set => SetProperty(ref _selectedAuthEntry, value);
        }

        private string _selectedItemTotpCode = string.Empty;
        public string SelectedItemTotpCode
        {
            get => _selectedItemTotpCode;
            set => SetProperty(ref _selectedItemTotpCode, value);
        }

        private int _totpRemainingSeconds = 30;
        public int TotpRemainingSeconds
        {
            get => _totpRemainingSeconds;
            set => SetProperty(ref _totpRemainingSeconds, value);
        }

        private string _newAuthIssuer = string.Empty;
        public string NewAuthIssuer { get => _newAuthIssuer; set => SetProperty(ref _newAuthIssuer, value); }

        private string _newAuthAccount = string.Empty;
        public string NewAuthAccount { get => _newAuthAccount; set => SetProperty(ref _newAuthAccount, value); }

        private string _newAuthSecret = string.Empty;
        public string NewAuthSecret { get => _newAuthSecret; set => SetProperty(ref _newAuthSecret, value); }

        #endregion

        #region Properties - Password Generator

        public PasswordGeneratorOptions GeneratorOptions { get; set; }

        private string _generatedPassword = string.Empty;
        public string GeneratedPassword
        {
            get => _generatedPassword;
            set { if (SetProperty(ref _generatedPassword, value)) UpdatePasswordStrength(); }
        }

        private int _generatedPasswordStrength;
        public int GeneratedPasswordStrength
        {
            get => _generatedPasswordStrength;
            set => SetProperty(ref _generatedPasswordStrength, value);
        }

        private string _generatedPasswordStrengthLabel = string.Empty;
        public string GeneratedPasswordStrengthLabel
        {
            get => _generatedPasswordStrengthLabel;
            set => SetProperty(ref _generatedPasswordStrengthLabel, value);
        }

        private string _generatedPasswordStrengthColor = "#FF9E9E9E";
        public string GeneratedPasswordStrengthColor
        {
            get => _generatedPasswordStrengthColor;
            set => SetProperty(ref _generatedPasswordStrengthColor, value);
        }

        // Generator option properties for binding
        private int _genLength = 20;
        public int GenLength
        {
            get => _genLength;
            set { if (SetProperty(ref _genLength, value)) { GeneratorOptions.Length = value; RegeneratePassword(); } }
        }
        private bool _genUppercase = true;
        public bool GenUppercase
        {
            get => _genUppercase;
            set { if (SetProperty(ref _genUppercase, value)) { GeneratorOptions.Uppercase = value; RegeneratePassword(); } }
        }
        private bool _genLowercase = true;
        public bool GenLowercase
        {
            get => _genLowercase;
            set { if (SetProperty(ref _genLowercase, value)) { GeneratorOptions.Lowercase = value; RegeneratePassword(); } }
        }
        private bool _genNumbers = true;
        public bool GenNumbers
        {
            get => _genNumbers;
            set { if (SetProperty(ref _genNumbers, value)) { GeneratorOptions.Numbers = value; RegeneratePassword(); } }
        }
        private bool _genSpecial = true;
        public bool GenSpecial
        {
            get => _genSpecial;
            set { if (SetProperty(ref _genSpecial, value)) { GeneratorOptions.Special = value; RegeneratePassword(); } }
        }
        private bool _genAvoidAmbiguous;
        public bool GenAvoidAmbiguous
        {
            get => _genAvoidAmbiguous;
            set { if (SetProperty(ref _genAvoidAmbiguous, value)) { GeneratorOptions.AvoidAmbiguous = value; RegeneratePassword(); } }
        }
        private bool _genUsePassphrase;
        public bool GenUsePassphrase
        {
            get => _genUsePassphrase;
            set { if (SetProperty(ref _genUsePassphrase, value)) { GeneratorOptions.UsePassphrase = value; RegeneratePassword(); } }
        }
        private int _genNumWords = 4;
        public int GenNumWords
        {
            get => _genNumWords;
            set { if (SetProperty(ref _genNumWords, value)) { GeneratorOptions.NumWords = value; RegeneratePassword(); } }
        }
        private string _genWordSeparator = "-";
        public string GenWordSeparator
        {
            get => _genWordSeparator;
            set { if (SetProperty(ref _genWordSeparator, value)) { GeneratorOptions.WordSeparator = value; RegeneratePassword(); } }
        }

        #endregion

        #region Properties - Health Check

        public ObservableCollection<VaultItem> HealthWeakItems { get; }
        public ObservableCollection<VaultItem> HealthReusedItems { get; }

        private int _healthTotalPasswords;
        public int HealthTotalPasswords { get => _healthTotalPasswords; set => SetProperty(ref _healthTotalPasswords, value); }

        private int _healthWeakCount;
        public int HealthWeakCount { get => _healthWeakCount; set => SetProperty(ref _healthWeakCount, value); }

        private int _healthReusedCount;
        public int HealthReusedCount { get => _healthReusedCount; set => SetProperty(ref _healthReusedCount, value); }

        private string _healthStatus = "Not scanned yet.";
        public string HealthStatus { get => _healthStatus; set => SetProperty(ref _healthStatus, value); }

        #endregion

        #region Properties - MFA

        private bool _mfaRequired;
        /// <summary>True when password validated but MFA verification is pending.</summary>
        public bool MfaRequired
        {
            get => _mfaRequired;
            set => SetProperty(ref _mfaRequired, value);
        }

        private string _mfaCodeInput = string.Empty;
        public string MfaCodeInput
        {
            get => _mfaCodeInput;
            set => SetProperty(ref _mfaCodeInput, value);
        }

        private MfaType _currentMfaType;
        public MfaType CurrentMfaType
        {
            get => _currentMfaType;
            set { SetProperty(ref _currentMfaType, value); RaisePropertyChanged(nameof(MfaRequiresTotp)); RaisePropertyChanged(nameof(MfaRequiresWindowsHello)); }
        }

        public bool MfaRequiresTotp => CurrentMfaType == MfaType.Totp || CurrentMfaType == MfaType.Both;
        public bool MfaRequiresWindowsHello => CurrentMfaType == MfaType.WindowsHello || CurrentMfaType == MfaType.Both;

        private bool _windowsHelloVerified;
        public bool WindowsHelloVerified
        {
            get => _windowsHelloVerified;
            set => SetProperty(ref _windowsHelloVerified, value);
        }

        // MFA Setup properties (shown when setting up MFA in security tab)
        private bool _isMfaSetupVisible;
        public bool IsMfaSetupVisible
        {
            get => _isMfaSetupVisible;
            set => SetProperty(ref _isMfaSetupVisible, value);
        }

        private string _mfaTotpSetupSecret = string.Empty;
        public string MfaTotpSetupSecret
        {
            get => _mfaTotpSetupSecret;
            set => SetProperty(ref _mfaTotpSetupSecret, value);
        }

        private string _mfaTotpSetupUri = string.Empty;
        public string MfaTotpSetupUri
        {
            get => _mfaTotpSetupUri;
            set => SetProperty(ref _mfaTotpSetupUri, value);
        }

        private string _mfaSetupVerifyCode = string.Empty;
        public string MfaSetupVerifyCode
        {
            get => _mfaSetupVerifyCode;
            set => SetProperty(ref _mfaSetupVerifyCode, value);
        }

        private System.Windows.Media.ImageSource? _mfaQrCodeImage;
        public System.Windows.Media.ImageSource? MfaQrCodeImage
        {
            get => _mfaQrCodeImage;
            set => SetProperty(ref _mfaQrCodeImage, value);
        }

        private bool _isTotpMfaEnabled;
        public bool IsTotpMfaEnabled
        {
            get => _isTotpMfaEnabled;
            set => SetProperty(ref _isTotpMfaEnabled, value);
        }

        private bool _isWindowsHelloMfaEnabled;
        public bool IsWindowsHelloMfaEnabled
        {
            get => _isWindowsHelloMfaEnabled;
            set => SetProperty(ref _isWindowsHelloMfaEnabled, value);
        }

        private bool _isWindowsHelloAvailable;
        public bool IsWindowsHelloAvailable
        {
            get => _isWindowsHelloAvailable;
            set => SetProperty(ref _isWindowsHelloAvailable, value);
        }

        private string _mfaStatusText = string.Empty;
        public string MfaStatusText
        {
            get => _mfaStatusText;
            set => SetProperty(ref _mfaStatusText, value);
        }

        #endregion

        #region Properties - Cloud Sync

        private bool _isSyncConnected;
        public bool IsSyncConnected
        {
            get => _isSyncConnected;
            set => SetProperty(ref _isSyncConnected, value);
        }

        private string _syncProviderName = "Not Connected";
        public string SyncProviderName
        {
            get => _syncProviderName;
            set => SetProperty(ref _syncProviderName, value);
        }

        private string _syncAccountEmail = string.Empty;
        public string SyncAccountEmail
        {
            get => _syncAccountEmail;
            set => SetProperty(ref _syncAccountEmail, value);
        }

        private string _lastSyncTime = "Never";
        public string LastSyncTime
        {
            get => _lastSyncTime;
            set => SetProperty(ref _lastSyncTime, value);
        }

        private string _syncStatus = string.Empty;
        public string SyncStatus
        {
            get => _syncStatus;
            set => SetProperty(ref _syncStatus, value);
        }

        #endregion

        #region Commands

        // Vault
        public ICommand CreateVaultCommand { get; }
        public ICommand UnlockVaultCommand { get; }
        public ICommand LockVaultCommand { get; }
        public ICommand ChangeMasterPasswordCommand { get; }

        // Items
        public ICommand AddLoginCommand { get; }
        public ICommand AddCardCommand { get; }
        public ICommand AddIdentityCommand { get; }
        public ICommand AddSecureNoteCommand { get; }
        public ICommand SaveItemCommand { get; }
        public ICommand DeleteItemCommand { get; }
        public ICommand CopyUsernameCommand { get; }
        public ICommand CopyPasswordCommand { get; }
        public ICommand CopyTotpCommand { get; }
        public ICommand CopyCardNumberCommand { get; }
        public ICommand CopyCardCvvCommand { get; }
        public ICommand LaunchUriCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }

        // Folders
        public ICommand AddFolderCommand { get; }
        public ICommand DeleteFolderCommand { get; }
        public ICommand ClearFolderFilterCommand { get; }

        // Authenticator
        public ICommand AddAuthEntryCommand { get; }
        public ICommand DeleteAuthEntryCommand { get; }
        public ICommand CopyAuthCodeCommand { get; }

        // Generator
        public ICommand GeneratePasswordCommand { get; }
        public ICommand CopyGeneratedPasswordCommand { get; }
        public ICommand ApplyGeneratedPasswordCommand { get; }

        // Health
        public ICommand RunHealthCheckCommand { get; }

        // Cloud Sync
        public ICommand SignInMicrosoftCommand { get; }
        public ICommand SignInGoogleCommand { get; }
        public ICommand SignOutCloudCommand { get; }
        public ICommand SyncNowCommand { get; }

        // Import/Export
        public ICommand ExportVaultCommand { get; }
        public ICommand ImportVaultCommand { get; }

        // MFA
        public ICommand VerifyMfaCommand { get; }
        public ICommand CancelMfaCommand { get; }
        public ICommand SetupTotpMfaCommand { get; }
        public ICommand FinalizeTotpSetupCommand { get; }
        public ICommand SetupWindowsHelloMfaCommand { get; }
        public ICommand RemoveTotpMfaCommand { get; }
        public ICommand RemoveWindowsHelloMfaCommand { get; }

        // URIs / Custom Fields
        public ICommand AddUriCommand { get; }
        public ICommand RemoveUriCommand { get; }
        public ICommand AddCustomFieldCommand { get; }
        public ICommand RemoveCustomFieldCommand { get; }

        #endregion

        #region Vault Operations

        private async Task CreateVaultAsync()
        {
            if (string.IsNullOrWhiteSpace(MasterPasswordInput))
            {
                StatusText = "Master password cannot be empty.";
                return;
            }

            if (MasterPasswordInput != ConfirmPasswordInput)
            {
                StatusText = "Passwords do not match.";
                return;
            }

            if (MasterPasswordInput.Length < 8)
            {
                StatusText = "Master password must be at least 8 characters.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusText = "Creating vault...";
                _vault = await _vaultService.CreateVaultAsync(MasterPasswordInput);
                IsUnlocked = true;
                VaultExists = true;
                StatusText = "Vault created and unlocked.";
                MasterPasswordInput = string.Empty;
                ConfirmPasswordInput = string.Empty;
                RefreshCollections();
            }
            catch (Exception ex)
            {
                StatusText = $"Error creating vault: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task UnlockVaultAsync()
        {
            if (string.IsNullOrWhiteSpace(MasterPasswordInput))
            {
                StatusText = "Enter your master password.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusText = "Unlocking vault...";
                _vault = await _vaultService.UnlockVaultAsync(MasterPasswordInput);

                // Check if MFA is required before completing unlock
                if (_mfaService.IsMfaEnabled())
                {
                    _pendingMasterPassword = MasterPasswordInput;
                    CurrentMfaType = _mfaService.GetMfaType();
                    MfaRequired = true;
                    WindowsHelloVerified = false;
                    MfaCodeInput = string.Empty;
                    StatusText = "Enter your MFA code to complete unlock.";
                    MasterPasswordInput = string.Empty;

                    // If Windows Hello is required, trigger it automatically
                    if (MfaRequiresWindowsHello)
                    {
                        _ = VerifyWindowsHelloForUnlockAsync();
                    }
                }
                else
                {
                    CompleteUnlock();
                }
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                StatusText = "Invalid master password.";
            }
            catch (FileNotFoundException)
            {
                VaultExists = false;
                StatusText = "Vault file not found. Create a new vault below.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error unlocking vault: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Completes the unlock. Called directly if no MFA, or after MFA verification.
        /// </summary>
        private void CompleteUnlock()
        {
            MfaRequired = false;
            _pendingMasterPassword = null;
            IsUnlocked = true;
            StatusText = $"Vault unlocked. {_vault?.Items.Count ?? 0} items loaded.";
            MasterPasswordInput = string.Empty;
            RefreshCollections();
            RefreshMfaStatus();
        }

        /// <summary>
        /// Verifies MFA code/challenge and completes unlock if valid.
        /// </summary>
        private async Task VerifyMfaAsync()
        {
            try
            {
                IsBusy = true;
                var mfaType = CurrentMfaType;

                // Verify TOTP if required
                if (mfaType == MfaType.Totp || mfaType == MfaType.Both)
                {
                    if (string.IsNullOrWhiteSpace(MfaCodeInput))
                    {
                        StatusText = "Enter the 6-digit code from your authenticator app.";
                        return;
                    }
                    if (_pendingMasterPassword == null)
                    {
                        StatusText = "Session expired. Please enter your master password again.";
                        CancelMfa();
                        return;
                    }
                    if (!_mfaService.ValidateTotpCode(_pendingMasterPassword, MfaCodeInput.Trim()))
                    {
                        StatusText = "Invalid MFA code. Try again.";
                        MfaCodeInput = string.Empty;
                        return;
                    }
                }

                // Verify Windows Hello if required
                if (mfaType == MfaType.WindowsHello || mfaType == MfaType.Both)
                {
                    if (!WindowsHelloVerified)
                    {
                        StatusText = "Windows Hello verification required.";
                        await VerifyWindowsHelloForUnlockAsync();
                        if (!WindowsHelloVerified)
                            return;
                    }
                }

                CompleteUnlock();
            }
            catch (Exception ex)
            {
                StatusText = $"MFA verification error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task VerifyWindowsHelloForUnlockAsync()
        {
            try
            {
                StatusText = "Verifying Windows Hello...";
                var verified = await _mfaService.ValidateWindowsHelloAsync();
                WindowsHelloVerified = verified;
                if (!verified)
                    StatusText = "Windows Hello verification failed or cancelled.";
                else if (CurrentMfaType == MfaType.WindowsHello)
                    CompleteUnlock(); // Windows Hello only — no TOTP needed
                else
                    StatusText = "Windows Hello verified. Enter TOTP code to continue.";
            }
            catch (Exception ex)
            {
                StatusText = $"Windows Hello error: {ex.Message}";
            }
        }

        private void CancelMfa()
        {
            MfaRequired = false;
            _pendingMasterPassword = null;
            MfaCodeInput = string.Empty;
            WindowsHelloVerified = false;
            _vaultService.Lock();
            _vault = null;
            StatusText = "Vault locked.";
        }

        private void LockVault()
        {
            _vaultService.Lock();
            _vault = null;
            IsUnlocked = false;
            SelectedItem = null;
            AllItems.Clear();
            FilteredItems.Clear();
            Folders.Clear();
            AuthenticatorEntries.Clear();
            HealthWeakItems.Clear();
            HealthReusedItems.Clear();
            StatusText = "Vault locked.";
            RaiseItemCounts();
        }

        private async Task ChangeMasterPasswordAsync()
        {
            if (_vault == null) return;

            if (string.IsNullOrWhiteSpace(MasterPasswordInput) || MasterPasswordInput.Length < 8)
            {
                StatusText = "New master password must be at least 8 characters.";
                return;
            }

            if (MasterPasswordInput != ConfirmPasswordInput)
            {
                StatusText = "Passwords do not match.";
                return;
            }

            try
            {
                IsBusy = true;
                await _vaultService.ChangeMasterPasswordAsync(_vault, MasterPasswordInput);
                StatusText = "Master password changed successfully.";
                MasterPasswordInput = string.Empty;
                ConfirmPasswordInput = string.Empty;
            }
            catch (Exception ex)
            {
                StatusText = $"Error changing password: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Item CRUD

        private void AddNewItem(VaultItemType type)
        {
            var item = new VaultItem
            {
                Type = type,
                Name = $"New {type}",
                FolderId = SelectedFolder?.Id,
            };

            switch (type)
            {
                case VaultItemType.Login:
                    item.Login = new LoginData();
                    break;
                case VaultItemType.Card:
                    item.Card = new CardData();
                    break;
                case VaultItemType.Identity:
                    item.Identity = new IdentityData();
                    break;
            }

            _vault?.Items.Add(item);
            AllItems.Add(item);
            SelectedItem = item;
            ApplyFilter();
            RaiseItemCounts();
            StatusText = $"New {type} item created. Edit and save.";
        }

        private async Task SaveCurrentItemAsync()
        {
            if (_vault == null || SelectedItem == null) return;

            try
            {
                IsBusy = true;
                SelectedItem.ModifiedAt = DateTime.UtcNow;
                await _vaultService.SaveVaultAsync(_vault);
                StatusText = $"Item '{SelectedItem.Name}' saved.";
                ApplyFilter();
            }
            catch (Exception ex)
            {
                StatusText = $"Error saving: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteCurrentItemAsync()
        {
            if (_vault == null || SelectedItem == null) return;

            var result = MessageBox.Show(
                $"Delete '{SelectedItem.Name}'? This cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _vault.Items.Remove(SelectedItem);
                AllItems.Remove(SelectedItem);
                SelectedItem = null;
                await _vaultService.SaveVaultAsync(_vault);
                ApplyFilter();
                RaiseItemCounts();
                StatusText = "Item deleted.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error deleting: {ex.Message}";
            }
        }

        private async Task ToggleFavoriteAsync()
        {
            if (_vault == null || SelectedItem == null) return;
            SelectedItem.Favorite = !SelectedItem.Favorite;
            SelectedItem.ModifiedAt = DateTime.UtcNow;
            await _vaultService.SaveVaultAsync(_vault);
            ApplyFilter();
            RaiseItemCounts();
            StatusText = SelectedItem.Favorite ? "Added to favorites." : "Removed from favorites.";
        }

        #endregion

        #region Folder Operations

        private async Task AddFolderAsync()
        {
            if (_vault == null) return;

            var name = "New Folder";
            // Simple inline: you can replace with a dialog prompt later
            var folder = new VaultFolder { Name = name };
            _vault.Folders.Add(folder);
            Folders.Add(folder);
            await _vaultService.SaveVaultAsync(_vault);
            StatusText = $"Folder '{name}' created.";
        }

        private async Task DeleteFolderAsync()
        {
            if (_vault == null || SelectedFolder == null) return;

            // Move items in this folder to "No Folder"
            foreach (var item in _vault.Items.Where(i => i.FolderId == SelectedFolder.Id))
                item.FolderId = null;

            _vault.Folders.Remove(SelectedFolder);
            Folders.Remove(SelectedFolder);
            SelectedFolder = null;
            await _vaultService.SaveVaultAsync(_vault);
            ApplyFilter();
            StatusText = "Folder deleted.";
        }

        #endregion

        #region Authenticator

        private void AddAuthenticatorEntry()
        {
            if (_vault == null) return;

            // Try parsing as otpauth:// URI first
            var entry = TotpService.ParseOtpAuthUri(NewAuthSecret);
            if (entry != null)
            {
                if (!string.IsNullOrEmpty(NewAuthIssuer)) entry.Issuer = NewAuthIssuer;
                if (!string.IsNullOrEmpty(NewAuthAccount)) entry.AccountName = NewAuthAccount;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(NewAuthSecret))
                {
                    StatusText = "Enter a secret key or otpauth:// URI.";
                    return;
                }

                entry = new AuthenticatorEntry
                {
                    Issuer = NewAuthIssuer,
                    AccountName = NewAuthAccount,
                    Secret = NewAuthSecret.Replace(" ", "").Replace("-", "").ToUpperInvariant(),
                };
            }

            _vault.AuthenticatorEntries.Add(entry);
            AuthenticatorEntries.Add(entry);
            NewAuthIssuer = string.Empty;
            NewAuthAccount = string.Empty;
            NewAuthSecret = string.Empty;
            _ = SaveVaultQuietAsync();
            StatusText = $"Authenticator entry added: {entry.Issuer}";
        }

        private async Task DeleteAuthenticatorEntryAsync()
        {
            if (_vault == null || SelectedAuthEntry == null) return;

            var result = MessageBox.Show(
                $"Delete authenticator entry '{SelectedAuthEntry.Issuer} ({SelectedAuthEntry.AccountName})'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            _vault.AuthenticatorEntries.Remove(SelectedAuthEntry);
            AuthenticatorEntries.Remove(SelectedAuthEntry);
            SelectedAuthEntry = null;
            await SaveVaultQuietAsync();
            StatusText = "Authenticator entry deleted.";
        }

        private void CopyAuthCode()
        {
            if (SelectedAuthEntry == null) return;
            var code = TotpService.GenerateCode(SelectedAuthEntry.Secret, SelectedAuthEntry.Digits, SelectedAuthEntry.Period, SelectedAuthEntry.Algorithm);
            CopyToClipboard(code);
        }

        private void CopyCurrentTotp()
        {
            if (SelectedItem?.Login?.TotpSecret == null) return;
            var code = TotpService.GenerateCode(SelectedItem.Login.TotpSecret);
            CopyToClipboard(code);
        }

        private void UpdateTotpCodes()
        {
            TotpRemainingSeconds = TotpService.GetRemainingSeconds();

            // Update selected item's TOTP
            if (SelectedItem?.Login?.TotpSecret != null)
            {
                SelectedItemTotpCode = TotpService.GenerateCode(SelectedItem.Login.TotpSecret);
            }

            // Update authenticator entries (trigger UI refresh through RaisePropertyChanged)
            RaisePropertyChanged(nameof(AuthenticatorEntries));
        }

        /// <summary>
        /// Gets the current TOTP code for an authenticator entry (used by the view via binding).
        /// </summary>
        public string GetTotpCode(AuthenticatorEntry entry)
        {
            return TotpService.GenerateCode(entry.Secret, entry.Digits, entry.Period, entry.Algorithm);
        }

        #endregion

        #region Password Generator

        private void RegeneratePassword()
        {
            GeneratedPassword = PasswordGeneratorService.GeneratePassword(GeneratorOptions);
        }

        private void UpdatePasswordStrength()
        {
            var strength = PasswordGeneratorService.EvaluateStrength(GeneratedPassword);
            GeneratedPasswordStrength = strength;
            GeneratedPasswordStrengthLabel = PasswordGeneratorService.GetStrengthLabel(strength);
            GeneratedPasswordStrengthColor = PasswordGeneratorService.GetStrengthColor(strength);
        }

        private void ApplyGeneratedPassword()
        {
            if (SelectedItem?.Type == VaultItemType.Login && SelectedItem.Login != null)
            {
                SelectedItem.Login.Password = GeneratedPassword;
                RaisePropertyChanged(nameof(SelectedItem));
                StatusText = "Generated password applied to current login.";
            }
        }

        #endregion

        #region Health Check

        private void RunHealthCheck()
        {
            if (_vault == null) return;

            var result = PasswordHealthService.AnalyzeVault(_vault);

            HealthTotalPasswords = result.TotalPasswords;
            HealthWeakCount = result.WeakPasswords;
            HealthReusedCount = result.ReusedPasswords;

            HealthWeakItems.Clear();
            foreach (var item in result.WeakItems)
                HealthWeakItems.Add(item);

            HealthReusedItems.Clear();
            foreach (var item in result.ReusedItems)
                HealthReusedItems.Add(item);

            HealthStatus = $"Scanned {result.TotalPasswords} passwords: {result.WeakPasswords} weak, {result.ReusedPasswords} reused.";
            StatusText = HealthStatus;
        }

        #endregion

        #region Cloud Sync

        private async Task SignInMicrosoftAsync()
        {
            try
            {
                IsBusy = true;
                SyncStatus = "Detecting OneDrive folder...";
                var result = await _syncService.ConnectOneDriveAsync();
                if (result != null)
                {
                    var state = new SyncState
                    {
                        Provider = CloudProvider.OneDrive,
                        AccountEmail = result.Value.AccountName,
                        AccountId = result.Value.FolderPath,
                    };
                    _syncService.SaveSyncState(state);
                    SyncProviderName = "OneDrive";
                    SyncAccountEmail = result.Value.AccountName;
                    IsSyncConnected = true;
                    SyncStatus = $"Connected to OneDrive ({result.Value.AccountName})";
                    StatusText = SyncStatus;
                }
            }
            catch (Exception ex)
            {
                SyncStatus = $"OneDrive connection failed: {ex.Message}";
                StatusText = SyncStatus;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private Task SignInGoogleAsync()
        {
            SyncStatus = "Google Drive sync is not yet available. Use OneDrive sync instead.";
            StatusText = SyncStatus;
            return Task.CompletedTask;
        }

        private async Task SignOutCloudAsync()
        {
            try
            {
                await _syncService.DisconnectOneDriveAsync();

                _syncService.SaveSyncState(new SyncState());
                IsSyncConnected = false;
                SyncProviderName = "Not Connected";
                SyncAccountEmail = string.Empty;
                LastSyncTime = "Never";
                SyncStatus = "Disconnected from cloud sync.";
                StatusText = SyncStatus;
            }
            catch (Exception ex)
            {
                SyncStatus = $"Sign-out error: {ex.Message}";
            }
        }

        private async Task SyncNowAsync()
        {
            if (_vault == null) return;

            try
            {
                IsBusy = true;
                SyncStatus = "Syncing...";

                // Save current vault first
                await _vaultService.SaveVaultAsync(_vault);

                var (localUpdated, message) = await _syncService.SyncVaultAsync(_vaultService);
                SyncStatus = message ?? "Sync complete.";
                LastSyncTime = DateTime.Now.ToString("g");
                StatusText = SyncStatus;

                if (localUpdated)
                {
                    // Re-unlock with current keys to reload
                    StatusText = "Remote vault was newer. Please re-enter master password to load changes.";
                    LockVault();
                }
            }
            catch (Exception ex)
            {
                SyncStatus = $"Sync error: {ex.Message}";
                StatusText = SyncStatus;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region MFA Setup & Management

        /// <summary>
        /// Refreshes MFA status properties from saved config.
        /// </summary>
        private async void RefreshMfaStatus()
        {
            try
            {
                var config = _mfaService.LoadConfig();
                IsTotpMfaEnabled = config.Enabled && (config.Type == MfaType.Totp || config.Type == MfaType.Both);
                IsWindowsHelloMfaEnabled = config.WindowsHelloEnabled && (config.Type == MfaType.WindowsHello || config.Type == MfaType.Both);
                IsWindowsHelloAvailable = await _mfaService.IsWindowsHelloAvailableAsync();

                if (config.Enabled)
                    MfaStatusText = $"MFA enabled: {config.Type} (since {config.SetupDate:g})";
                else
                    MfaStatusText = "MFA not enabled.";
            }
            catch (Exception ex)
            {
                MfaStatusText = $"Error loading MFA status: {ex.Message}";
            }
        }

        /// <summary>
        /// Begins TOTP setup: generates secret, shows QR code.
        /// </summary>
        private async Task SetupTotpMfaAsync()
        {
            if (_vault == null || _pendingMasterPassword == null && !_vaultService.IsUnlocked)
            {
                StatusText = "Vault must be unlocked to configure MFA.";
                return;
            }

            try
            {
                IsBusy = true;
                MfaStatusText = "Generating TOTP secret...";

                // We need the master password to encrypt the TOTP secret.
                // Since vault is unlocked, ask for re-entry via a prompt.
                var pwDialog = new System.Windows.Window
                {
                    Title = "Confirm Master Password",
                    Width = 400, Height = 180,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    ResizeMode = System.Windows.ResizeMode.NoResize,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                };
                var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = "Enter your master password to set up MFA:",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new System.Windows.Thickness(0, 0, 0, 8)
                };
                var pwBox = new System.Windows.Controls.PasswordBox { FontSize = 14, Padding = new System.Windows.Thickness(6) };
                var okBtn = new System.Windows.Controls.Button
                {
                    Content = "Confirm", Padding = new System.Windows.Thickness(12, 6, 12, 6),
                    Margin = new System.Windows.Thickness(0, 12, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                    Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                string? enteredPw = null;
                okBtn.Click += (_, _) => { enteredPw = pwBox.Password; pwDialog.DialogResult = true; };
                stack.Children.Add(label);
                stack.Children.Add(pwBox);
                stack.Children.Add(okBtn);
                pwDialog.Content = stack;

                if (pwDialog.ShowDialog() != true || string.IsNullOrEmpty(enteredPw))
                {
                    MfaStatusText = "MFA setup cancelled.";
                    return;
                }

                var (secret, otpAuthUri) = _mfaService.BeginTotpSetup(enteredPw);
                MfaTotpSetupSecret = secret;
                MfaTotpSetupUri = otpAuthUri;
                MfaSetupVerifyCode = string.Empty;
                IsMfaSetupVisible = true;

                // Generate QR code image
                try
                {
                    using var qrGenerator = new QRCoder.QRCodeGenerator();
                    var qrData = qrGenerator.CreateQrCode(otpAuthUri, QRCoder.QRCodeGenerator.ECCLevel.M);
                    using var qrCode = new QRCoder.PngByteQRCode(qrData);
                    var pngBytes = qrCode.GetGraphic(5);

                    var image = new System.Windows.Media.Imaging.BitmapImage();
                    image.BeginInit();
                    image.StreamSource = new MemoryStream(pngBytes);
                    image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    MfaQrCodeImage = image;
                }
                catch
                {
                    MfaQrCodeImage = null;
                }

                MfaStatusText = "Scan the QR code with your authenticator app, then enter the 6-digit code to verify.";
            }
            catch (Exception ex)
            {
                MfaStatusText = $"Error setting up TOTP: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Finalizes TOTP setup after user enters verification code.
        /// </summary>
        private async Task FinalizeTotpSetupAsync()
        {
            if (string.IsNullOrWhiteSpace(MfaSetupVerifyCode))
            {
                MfaStatusText = "Enter the 6-digit code from your authenticator app.";
                return;
            }

            try
            {
                IsBusy = true;

                // We need the master password again to decrypt the stored secret for verification
                var pwDialog = new System.Windows.Window
                {
                    Title = "Confirm Master Password",
                    Width = 400, Height = 180,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    ResizeMode = System.Windows.ResizeMode.NoResize,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                };
                var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = "Re-enter your master password to finalize MFA:",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new System.Windows.Thickness(0, 0, 0, 8)
                };
                var pwBox = new System.Windows.Controls.PasswordBox { FontSize = 14, Padding = new System.Windows.Thickness(6) };
                var okBtn = new System.Windows.Controls.Button
                {
                    Content = "Confirm", Padding = new System.Windows.Thickness(12, 6, 12, 6),
                    Margin = new System.Windows.Thickness(0, 12, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                    Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                string? enteredPw = null;
                okBtn.Click += (_, _) => { enteredPw = pwBox.Password; pwDialog.DialogResult = true; };
                stack.Children.Add(label);
                stack.Children.Add(pwBox);
                stack.Children.Add(okBtn);
                pwDialog.Content = stack;

                if (pwDialog.ShowDialog() != true || string.IsNullOrEmpty(enteredPw))
                {
                    MfaStatusText = "Finalization cancelled.";
                    return;
                }

                if (_mfaService.FinalizeTotpSetup(enteredPw, MfaSetupVerifyCode.Trim()))
                {
                    IsMfaSetupVisible = false;
                    MfaTotpSetupSecret = string.Empty;
                    MfaTotpSetupUri = string.Empty;
                    MfaQrCodeImage = null;
                    MfaSetupVerifyCode = string.Empty;
                    IsTotpMfaEnabled = true;
                    MfaStatusText = "✅ TOTP MFA enabled! You'll need your authenticator app code each time you unlock the vault.";
                    StatusText = MfaStatusText;
                }
                else
                {
                    MfaStatusText = "❌ Invalid code. Make sure the code matches what your authenticator app shows.";
                }
            }
            catch (Exception ex)
            {
                MfaStatusText = $"Error verifying TOTP code: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Sets up Windows Hello / FIDO2 MFA.
        /// </summary>
        private async Task SetupWindowsHelloMfaAsync()
        {
            try
            {
                IsBusy = true;
                MfaStatusText = "Setting up Windows Hello...";

                if (!await _mfaService.IsWindowsHelloAvailableAsync())
                {
                    MfaStatusText = "Windows Hello is not available on this device. You need a FIDO2 security key, fingerprint reader, or Windows Hello PIN configured.";
                    return;
                }

                if (await _mfaService.SetupWindowsHelloAsync())
                {
                    IsWindowsHelloMfaEnabled = true;
                    MfaStatusText = "✅ Windows Hello / FIDO2 MFA enabled!";
                    StatusText = MfaStatusText;
                }
                else
                {
                    MfaStatusText = "Windows Hello setup was cancelled or failed.";
                }
            }
            catch (Exception ex)
            {
                MfaStatusText = $"Error setting up Windows Hello: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Removes TOTP MFA.
        /// </summary>
        private void RemoveTotpMfa()
        {
            var result = MessageBox.Show(
                "Remove authenticator app MFA? You won't need a code to unlock the vault anymore.",
                "Remove TOTP MFA",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _mfaService.RemoveTotp();
            IsTotpMfaEnabled = false;
            IsMfaSetupVisible = false;
            RefreshMfaStatus();
            StatusText = "TOTP MFA removed.";
        }

        /// <summary>
        /// Removes Windows Hello / FIDO2 MFA.
        /// </summary>
        private async Task RemoveWindowsHelloMfaAsync()
        {
            var result = MessageBox.Show(
                "Remove Windows Hello / FIDO2 MFA?",
                "Remove Windows Hello MFA",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            await _mfaService.RemoveWindowsHelloAsync();
            IsWindowsHelloMfaEnabled = false;
            RefreshMfaStatus();
            StatusText = "Windows Hello MFA removed.";
        }

        #endregion

        #region Import / Export

        private async Task ExportVaultAsync()
        {
            if (_vault == null) return;

            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json|Encrypted Vault (*.encrypted)|*.encrypted",
                    FileName = "platypus_vault_export",
                    DefaultExt = ".json",
                };

                if (dlg.ShowDialog() == true)
                {
                    if (dlg.FileName.EndsWith(".encrypted", StringComparison.OrdinalIgnoreCase))
                    {
                        var data = await _vaultService.GetEncryptedVaultBytesAsync();
                        await File.WriteAllBytesAsync(dlg.FileName, data);
                    }
                    else
                    {
                        var json = _vaultService.ExportVaultJson(_vault);
                        await File.WriteAllTextAsync(dlg.FileName, json);
                    }
                    StatusText = $"Vault exported to {dlg.FileName}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
            }
        }

        private async Task ImportVaultAsync()
        {
            if (_vault == null) return;

            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON Files (*.json)|*.json|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                };

                if (dlg.ShowDialog() == true)
                {
                    var json = await File.ReadAllTextAsync(dlg.FileName);
                    var imported = _vaultService.ImportVaultJson(json);

                    var result = MessageBox.Show(
                        $"Import {imported.Items.Count} items and {imported.Folders.Count} folders?\nExisting items will be kept.",
                        "Confirm Import",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        foreach (var item in imported.Items)
                        {
                            item.Id = Guid.NewGuid().ToString("N"); // New IDs to avoid conflicts
                            _vault.Items.Add(item);
                        }
                        foreach (var folder in imported.Folders)
                        {
                            if (!_vault.Folders.Any(f => f.Name == folder.Name))
                                _vault.Folders.Add(folder);
                        }
                        foreach (var auth in imported.AuthenticatorEntries)
                        {
                            auth.Id = Guid.NewGuid().ToString("N");
                            _vault.AuthenticatorEntries.Add(auth);
                        }

                        await _vaultService.SaveVaultAsync(_vault);
                        RefreshCollections();
                        StatusText = $"Imported {imported.Items.Count} items.";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Import error: {ex.Message}";
            }
        }

        #endregion

        #region URI / Custom Field Helpers

        private void AddUri()
        {
            if (SelectedItem?.Login == null) return;
            SelectedItem.Login.Uris.Add(new LoginUri { Uri = "https://" });
            RaisePropertyChanged(nameof(ItemUris));
        }

        private void RemoveUri()
        {
            if (SelectedItem?.Login?.Uris == null || SelectedItem.Login.Uris.Count == 0) return;
            SelectedItem.Login.Uris.RemoveAt(SelectedItem.Login.Uris.Count - 1);
            RaisePropertyChanged(nameof(ItemUris));
        }

        private void AddCustomField()
        {
            if (SelectedItem == null) return;
            SelectedItem.CustomFields.Add(new CustomField { Name = "Field", Value = "" });
            RaisePropertyChanged(nameof(ItemCustomFields));
        }

        private void RemoveCustomField()
        {
            if (SelectedItem?.CustomFields == null || SelectedItem.CustomFields.Count == 0) return;
            SelectedItem.CustomFields.RemoveAt(SelectedItem.CustomFields.Count - 1);
            RaisePropertyChanged(nameof(ItemCustomFields));
        }

        private void LaunchUri()
        {
            var uri = SelectedItem?.Login?.Uris?.FirstOrDefault(u => !string.IsNullOrEmpty(u.Uri))?.Uri;
            if (uri == null) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch { }
        }

        #endregion

        #region Helpers

        private void RefreshCollections()
        {
            AllItems.Clear();
            FilteredItems.Clear();
            Folders.Clear();
            AuthenticatorEntries.Clear();

            if (_vault != null)
            {
                foreach (var folder in _vault.Folders)
                    Folders.Add(folder);
                foreach (var item in _vault.Items)
                    AllItems.Add(item);
                foreach (var auth in _vault.AuthenticatorEntries)
                    AuthenticatorEntries.Add(auth);
            }

            ApplyFilter();
            RaiseItemCounts();
        }

        private void ApplyFilter()
        {
            FilteredItems.Clear();

            IEnumerable<VaultItem> items = AllItems;

            // Type filter
            items = SelectedItemTypeFilter switch
            {
                "Logins" => items.Where(i => i.Type == VaultItemType.Login),
                "Cards" => items.Where(i => i.Type == VaultItemType.Card),
                "Identities" => items.Where(i => i.Type == VaultItemType.Identity),
                "Secure Notes" => items.Where(i => i.Type == VaultItemType.SecureNote),
                "Favorites" => items.Where(i => i.Favorite),
                _ => items,
            };

            // Folder filter
            if (SelectedFolder != null)
                items = items.Where(i => i.FolderId == SelectedFolder.Id);

            // Search
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.Trim().ToLowerInvariant();
                items = items.Where(i =>
                    (i.Name?.ToLowerInvariant().Contains(q) == true) ||
                    (i.Login?.Username?.ToLowerInvariant().Contains(q) == true) ||
                    (i.Login?.Uris?.Any(u => u.Uri?.ToLowerInvariant().Contains(q) == true) == true) ||
                    (i.Notes?.ToLowerInvariant().Contains(q) == true) ||
                    (i.Card?.CardholderName?.ToLowerInvariant().Contains(q) == true) ||
                    (i.Identity?.FirstName?.ToLowerInvariant().Contains(q) == true) ||
                    (i.Identity?.LastName?.ToLowerInvariant().Contains(q) == true) ||
                    (i.Identity?.Email?.ToLowerInvariant().Contains(q) == true));
            }

            foreach (var item in items.OrderByDescending(i => i.Favorite).ThenBy(i => i.Name))
                FilteredItems.Add(item);
        }

        private void RaiseItemCounts()
        {
            RaisePropertyChanged(nameof(TotalItemCount));
            RaisePropertyChanged(nameof(LoginCount));
            RaisePropertyChanged(nameof(CardCount));
            RaisePropertyChanged(nameof(IdentityCount));
            RaisePropertyChanged(nameof(NoteCount));
            RaisePropertyChanged(nameof(FavoriteCount));
        }

        private void CopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                StatusText = "Copied to clipboard (auto-clears in 30s).";

                // Auto-clear clipboard after 30 seconds (Bitwarden does this)
                _clipboardClearTimer?.Dispose();
                _clipboardClearTimer = new System.Threading.Timer(_ =>
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (Clipboard.GetText() == text)
                                Clipboard.Clear();
                        }
                        catch { }
                    });
                }, null, 30000, Timeout.Infinite);
            }
            catch { }
        }

        private async Task SaveVaultQuietAsync()
        {
            if (_vault == null) return;
            try
            {
                await _vaultService.SaveVaultAsync(_vault);
            }
            catch { }
        }

        #endregion
    }
}
