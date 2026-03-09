using PlatypusTools.Core.Models.Mail;
using PlatypusTools.Core.Services.Mail;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Converts a bool to one of two text strings.
    /// </summary>
    public class BoolToTextConverter : IValueConverter
    {
        public string TrueText { get; set; } = "True";
        public string FalseText { get; set; } = "False";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? TrueText : FalseText;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    /// <summary>
    /// ViewModel for the mail client feature.
    /// Supports IMAP and Exchange accounts with filtering and rules.
    /// </summary>
    public class MailClientViewModel : BindableBase, IDisposable
    {
        private IMailService? _mailService;
        private readonly MailRuleEngine _ruleEngine = new();
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private readonly string _accountsPath;
        private readonly string _rulesPath;
        private int _currentPage;

        public MailClientViewModel()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var configDir = Path.Combine(appData, "PlatypusTools", "Mail");
            Directory.CreateDirectory(configDir);
            _accountsPath = Path.Combine(configDir, "accounts.json");
            _rulesPath = Path.Combine(configDir, "rules.json");

            // Commands
            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => SelectedAccount != null && !IsConnected && !IsBusy);
            DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(), _ => IsConnected);
            RefreshCommand = new RelayCommand(async _ => await RefreshCurrentFolderAsync(), _ => IsConnected && !IsBusy);
            SearchCommand = new RelayCommand(async _ => await SearchAsync(), _ => IsConnected && !string.IsNullOrWhiteSpace(SearchQuery) && !IsBusy);
            ClearSearchCommand = new RelayCommand(async _ => { SearchQuery = ""; await RefreshCurrentFolderAsync(); });

            AddAccountCommand = new RelayCommand(_ => AddAccount());
            SaveAccountCommand = new RelayCommand(async _ => await SaveAccountAsync(), _ => !string.IsNullOrWhiteSpace(EditAccountName) && !string.IsNullOrWhiteSpace(EditEmailAddress));
            RemoveAccountCommand = new RelayCommand(async _ => await RemoveAccountAsync(), _ => SelectedAccount != null);
            TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync(), _ => !IsBusy);
            CancelEditAccountCommand = new RelayCommand(_ => IsEditingAccount = false);

            SelectFolderCommand = new RelayCommand(async param => await SelectFolderAsync(param as MailFolder), _ => IsConnected && !IsBusy);
            NextPageCommand = new RelayCommand(async _ => { _currentPage++; await LoadMessagesAsync(); }, _ => IsConnected && !IsBusy && Messages.Count >= PageSize);
            PrevPageCommand = new RelayCommand(async _ => { if (_currentPage > 0) _currentPage--; await LoadMessagesAsync(); }, _ => IsConnected && !IsBusy && _currentPage > 0);

            DeleteMessageCommand = new RelayCommand(async _ => await DeleteSelectedMessageAsync(), _ => SelectedMessage != null && IsConnected && !IsBusy);
            ToggleReadCommand = new RelayCommand(async _ => await ToggleReadAsync(), _ => SelectedMessage != null && IsConnected);
            ToggleFlagCommand = new RelayCommand(async _ => await ToggleFlagAsync(), _ => SelectedMessage != null && IsConnected);
            MoveToFolderCommand = new RelayCommand(async param => await MoveToFolderAsync(param?.ToString()), _ => SelectedMessage != null && IsConnected);
            SaveAttachmentCommand = new RelayCommand(async param => await SaveAttachmentAsync(param as MailAttachment), _ => SelectedMessage != null && IsConnected);

            AddRuleCommand = new RelayCommand(_ => AddRule());
            SaveRuleCommand = new RelayCommand(async _ => await SaveRuleAsync(), _ => !string.IsNullOrWhiteSpace(EditRuleName));
            RemoveRuleCommand = new RelayCommand(async _ => await RemoveRuleAsync(), _ => SelectedRule != null);
            ApplyRulesCommand = new RelayCommand(async _ => await ApplyRulesAsync(), _ => IsConnected && Rules.Count > 0 && !IsBusy);
            CancelEditRuleCommand = new RelayCommand(_ => IsEditingRule = false);
            AddConditionCommand = new RelayCommand(_ => EditRuleConditions.Add(new MailRuleCondition()));
            RemoveConditionCommand = new RelayCommand(param => 
            {
                if (param is MailRuleCondition c)
                    EditRuleConditions.Remove(c);
            });

            // Load saved data
            _ = LoadAccountsAsync();
            _ = LoadRulesAsync();
        }

        #region Collections

        public ObservableCollection<MailAccountConfig> Accounts { get; } = new();
        public ObservableCollection<MailFolder> Folders { get; } = new();
        public ObservableCollection<MailMessageItem> Messages { get; } = new();
        public ObservableCollection<MailRule> Rules { get; } = new();
        public ObservableCollection<MailRuleCondition> EditRuleConditions { get; } = new();

        #endregion

        #region Properties

        private MailAccountConfig? _selectedAccount;
        public MailAccountConfig? SelectedAccount
        {
            get => _selectedAccount;
            set { SetProperty(ref _selectedAccount, value); }
        }

        private MailFolder? _selectedFolder;
        public MailFolder? SelectedFolder
        {
            get => _selectedFolder;
            set { SetProperty(ref _selectedFolder, value); }
        }

        private MailMessageItem? _selectedMessage;
        public MailMessageItem? SelectedMessage
        {
            get => _selectedMessage;
            set
            {
                if (SetProperty(ref _selectedMessage, value) && value != null)
                    _ = LoadFullMessageAsync(value);
            }
        }

        private MailMessageItem? _fullMessage;
        public MailMessageItem? FullMessage
        {
            get => _fullMessage;
            set => SetProperty(ref _fullMessage, value);
        }

        private MailRule? _selectedRule;
        public MailRule? SelectedRule
        {
            get => _selectedRule;
            set => SetProperty(ref _selectedRule, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _statusMessage = "Not connected";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        private int _pageSize = 50;
        public int PageSize
        {
            get => _pageSize;
            set => SetProperty(ref _pageSize, value);
        }

        public string PageInfo => $"Page {_currentPage + 1}";

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        // Quick filter (client-side to augment server search)
        private string _quickFilter = string.Empty;
        public string QuickFilter
        {
            get => _quickFilter;
            set
            {
                if (SetProperty(ref _quickFilter, value))
                    ApplyQuickFilter();
            }
        }

        private string _filterField = "All";
        public string FilterField
        {
            get => _filterField;
            set
            {
                if (SetProperty(ref _filterField, value))
                    ApplyQuickFilter();
            }
        }

        #endregion

        #region Account Editing Properties

        private bool _isEditingAccount;
        public bool IsEditingAccount
        {
            get => _isEditingAccount;
            set => SetProperty(ref _isEditingAccount, value);
        }

        private string _editAccountName = string.Empty;
        public string EditAccountName { get => _editAccountName; set => SetProperty(ref _editAccountName, value); }
        private string _editEmailAddress = string.Empty;
        public string EditEmailAddress { get => _editEmailAddress; set => SetProperty(ref _editEmailAddress, value); }
        private MailAccountType _editAccountType = MailAccountType.IMAP;
        public MailAccountType EditAccountType
        {
            get => _editAccountType;
            set
            {
                if (SetProperty(ref _editAccountType, value))
                {
                    RaisePropertyChanged(nameof(IsEditImap));
                    RaisePropertyChanged(nameof(IsEditExchange));
                    RaisePropertyChanged(nameof(IsEditImapBased));
                    RaisePropertyChanged(nameof(IsPresetProvider));
                    RaisePropertyChanged(nameof(ProviderNote));
                    RaisePropertyChanged(nameof(ShowPasswordField));
                    RaisePropertyChanged(nameof(ShowOAuthClientId));
                    RaisePropertyChanged(nameof(NeedsOAuthClientId));
                    ApplyAccountTypePreset(value);
                }
            }
        }
        private string _editImapServer = string.Empty;
        public string EditImapServer { get => _editImapServer; set => SetProperty(ref _editImapServer, value); }
        private int _editImapPort = 993;
        public int EditImapPort { get => _editImapPort; set => SetProperty(ref _editImapPort, value); }
        private bool _editImapUseSsl = true;
        public bool EditImapUseSsl { get => _editImapUseSsl; set => SetProperty(ref _editImapUseSsl, value); }
        private string _editSmtpServer = string.Empty;
        public string EditSmtpServer { get => _editSmtpServer; set => SetProperty(ref _editSmtpServer, value); }
        private int _editSmtpPort = 587;
        public int EditSmtpPort { get => _editSmtpPort; set => SetProperty(ref _editSmtpPort, value); }
        private bool _editSmtpUseSsl = true;
        public bool EditSmtpUseSsl { get => _editSmtpUseSsl; set => SetProperty(ref _editSmtpUseSsl, value); }
        private string _editUsername = string.Empty;
        public string EditUsername { get => _editUsername; set => SetProperty(ref _editUsername, value); }
        private string _editPassword = string.Empty;
        public string EditPassword { get => _editPassword; set => SetProperty(ref _editPassword, value); }
        private bool _editUseOAuth;
        public bool EditUseOAuth
        {
            get => _editUseOAuth;
            set
            {
                if (SetProperty(ref _editUseOAuth, value))
                {
                    RaisePropertyChanged(nameof(ShowPasswordField));
                    RaisePropertyChanged(nameof(ShowOAuthClientId));
                    RaisePropertyChanged(nameof(ProviderNote));
                }
            }
        }
        private string _editTenantId = string.Empty;
        public string EditTenantId { get => _editTenantId; set => SetProperty(ref _editTenantId, value); }
        private string _editClientId = string.Empty;
        public string EditClientId { get => _editClientId; set => SetProperty(ref _editClientId, value); }
        private string? _editAccountId;

        /// <summary>All account types use IMAP.</summary>
        public bool IsEditImapBased => true;
        /// <summary>True only for generic/custom IMAP (shows editable server fields).</summary>
        public bool IsEditImap => EditAccountType == MailAccountType.IMAP;
        /// <summary>True for preset providers (server fields shown read-only).</summary>
        public bool IsPresetProvider => EditAccountType is MailAccountType.Gmail or MailAccountType.Yahoo or MailAccountType.Hotmail or MailAccountType.Exchange;
        public bool IsEditExchange => false;

        /// <summary>Show password fields when NOT using OAuth.</summary>
        public bool ShowPasswordField => !EditUseOAuth;
        /// <summary>Show OAuth Client ID field when using OAuth.</summary>
        public bool ShowOAuthClientId => EditUseOAuth;
        /// <summary>Whether the current provider needs a user-supplied Client ID for OAuth.</summary>
        public bool NeedsOAuthClientId => EditUseOAuth && !OAuthTokenService.HasDefaultClientId(EditAccountType);

        /// <summary>Provider-specific note about authentication.</summary>
        public string ProviderNote => (EditAccountType, EditUseOAuth) switch
        {
            (MailAccountType.Gmail, true) => "Requires a Google Cloud OAuth Client ID. Go to console.cloud.google.com > APIs > Credentials > Create OAuth 2.0 Client (Desktop app). Enable Gmail API.",
            (MailAccountType.Gmail, false) => "Gmail requires an App Password. Go to myaccount.google.com > Security > 2-Step Verification > App passwords.",
            (MailAccountType.Yahoo, true) => "Requires a Yahoo Developer App. Go to developer.yahoo.com > My Apps > Create App for OAuth.",
            (MailAccountType.Yahoo, false) => "Yahoo requires an App Password. Go to login.yahoo.com > Account Security > Generate app password.",
            (MailAccountType.Hotmail, true) => "Microsoft OAuth via browser. Works with MFA/2FA. Uses a public IMAP client ID — you can override with your own Azure AD app if needed.",
            (MailAccountType.Hotmail, false) => "For Hotmail/Outlook.com. Use your Microsoft account password (or App Password if 2FA is on).",
            (MailAccountType.Exchange, true) => "Microsoft OAuth via browser. Works with MFA/2FA. Uses a public IMAP client ID. Your tenant admin may need to consent to this app.",
            (MailAccountType.Exchange, false) => "For Exchange/M365 work accounts. Requires IMAP enabled. Use password or enable OAuth for MFA.",
            (MailAccountType.IMAP, true) => "OAuth for custom IMAP requires a Client ID from your provider. Most IMAP servers use password auth.",
            _ => "Enter your mail server's IMAP and SMTP settings manually."
        };

        #endregion

        #region Rule Editing Properties

        private bool _isEditingRule;
        public bool IsEditingRule
        {
            get => _isEditingRule;
            set => SetProperty(ref _isEditingRule, value);
        }

        private string _editRuleName = string.Empty;
        public string EditRuleName { get => _editRuleName; set => SetProperty(ref _editRuleName, value); }
        private bool _editRuleMatchAll = true;
        public bool EditRuleMatchAll { get => _editRuleMatchAll; set => SetProperty(ref _editRuleMatchAll, value); }
        private MailRuleAction _editRuleAction = MailRuleAction.MoveToFolder;
        public MailRuleAction EditRuleAction { get => _editRuleAction; set => SetProperty(ref _editRuleAction, value); }
        private string _editRuleTargetFolder = string.Empty;
        public string EditRuleTargetFolder { get => _editRuleTargetFolder; set => SetProperty(ref _editRuleTargetFolder, value); }
        private bool _editRuleStopProcessing = true;
        public bool EditRuleStopProcessing { get => _editRuleStopProcessing; set => SetProperty(ref _editRuleStopProcessing, value); }
        private bool _editRuleApplyToExisting;
        public bool EditRuleApplyToExisting { get => _editRuleApplyToExisting; set => SetProperty(ref _editRuleApplyToExisting, value); }
        private string? _editRuleId;

        public Array MailRuleActions => Enum.GetValues(typeof(MailRuleAction));
        public Array MailRuleFields => Enum.GetValues(typeof(MailRuleField));
        public Array MailRuleOperators => Enum.GetValues(typeof(MailRuleOperator));
        public Array MailAccountTypes => Enum.GetValues(typeof(MailAccountType));
        public string[] FilterFields => new[] { "All", "Subject", "From", "To", "Unread", "Flagged", "Has Attachments" };

        #endregion

        #region Commands

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand ClearSearchCommand { get; }

        public ICommand AddAccountCommand { get; }
        public ICommand SaveAccountCommand { get; }
        public ICommand RemoveAccountCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand CancelEditAccountCommand { get; }

        public ICommand SelectFolderCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }

        public ICommand DeleteMessageCommand { get; }
        public ICommand ToggleReadCommand { get; }
        public ICommand ToggleFlagCommand { get; }
        public ICommand MoveToFolderCommand { get; }
        public ICommand SaveAttachmentCommand { get; }

        public ICommand AddRuleCommand { get; }
        public ICommand SaveRuleCommand { get; }
        public ICommand RemoveRuleCommand { get; }
        public ICommand ApplyRulesCommand { get; }
        public ICommand CancelEditRuleCommand { get; }
        public ICommand AddConditionCommand { get; }
        public ICommand RemoveConditionCommand { get; }

        #endregion

        #region Connection

        private async Task ConnectAsync()
        {
            if (SelectedAccount == null) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                IsBusy = true;

                // Create IMAP service (all account types use IMAP)
                _mailService?.Dispose();
                _mailService = new ImapMailService();

                _mailService.StatusChanged += (s, msg) =>
                    Application.Current?.Dispatcher.InvokeAsync(() => StatusMessage = msg);
                _mailService.ProgressChanged += (s, pct) =>
                    Application.Current?.Dispatcher.InvokeAsync(() => Progress = pct);

                // Authenticate: OAuth or password
                if (SelectedAccount.UseOAuth)
                {
                    await AcquireOAuthTokenAsync(SelectedAccount, _cts.Token);
                }
                else
                {
                    DecryptPassword(SelectedAccount);
                }

                await _mailService.ConnectAsync(SelectedAccount, _cts.Token);
                IsConnected = true;

                // Load folders
                var folders = await _mailService.GetFoldersAsync(_cts.Token);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Folders.Clear();
                    foreach (var f in folders)
                        Folders.Add(f);
                });

                // Auto-select Inbox
                var inbox = folders.FirstOrDefault(f => f.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase));
                if (inbox != null)
                    await SelectFolderAsync(inbox);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusMessage = $"Connection failed: {ex.Message}";
                IsConnected = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DisconnectAsync()
        {
            _cts?.Cancel();
            if (_mailService != null)
            {
                await _mailService.DisconnectAsync();
                _mailService.Dispose();
                _mailService = null;
            }
            IsConnected = false;
            Folders.Clear();
            Messages.Clear();
            FullMessage = null;
            StatusMessage = "Disconnected";
        }

        private async Task TestConnectionAsync()
        {
            IMailService? testService = null;
            try
            {
                IsBusy = true;
                var account = BuildAccountFromEdit();

                if (account.UseOAuth)
                {
                    await AcquireOAuthTokenAsync(account, default);
                }
                else
                {
                    DecryptPassword(account);
                }

                testService = new ImapMailService();

                testService.StatusChanged += (s, msg) =>
                    Application.Current?.Dispatcher.InvokeAsync(() => StatusMessage = msg);

                await testService.ConnectAsync(account);
                StatusMessage = "✅ Connection successful!";
                await testService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Connection failed: {ex.Message}";
            }
            finally
            {
                testService?.Dispose();
                IsBusy = false;
            }
        }

        #endregion

        #region Folder & Messages

        private async Task SelectFolderAsync(MailFolder? folder)
        {
            if (folder == null || _mailService == null) return;
            SelectedFolder = folder;
            _currentPage = 0;
            await LoadMessagesAsync();
        }

        private async Task LoadMessagesAsync()
        {
            if (_mailService == null || SelectedFolder == null) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                IsBusy = true;
                var msgs = await _mailService.GetMessagesAsync(SelectedFolder.FullPath, _currentPage, PageSize, _cts.Token);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Messages.Clear();
                    foreach (var m in msgs)
                        Messages.Add(m);
                    FullMessage = null;
                });

                RaisePropertyChanged(nameof(PageInfo));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading messages: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadFullMessageAsync(MailMessageItem summary)
        {
            if (_mailService == null || SelectedFolder == null) return;

            try
            {
                var full = await _mailService.GetMessageAsync(SelectedFolder.FullPath, summary.UniqueId);
                Application.Current?.Dispatcher.Invoke(() => FullMessage = full);

                // Mark as read
                if (!summary.IsRead)
                {
                    await _mailService.SetReadAsync(SelectedFolder.FullPath, summary.UniqueId, true);
                    summary.IsRead = true;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading message: {ex.Message}";
            }
        }

        private async Task RefreshCurrentFolderAsync()
        {
            if (SelectedFolder != null)
                await LoadMessagesAsync();
        }

        private async Task SearchAsync()
        {
            if (_mailService == null || SelectedFolder == null || string.IsNullOrWhiteSpace(SearchQuery)) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                IsBusy = true;
                var results = await _mailService.SearchAsync(SelectedFolder.FullPath, SearchQuery, _cts.Token);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Messages.Clear();
                    foreach (var m in results)
                        Messages.Add(m);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyQuickFilter()
        {
            // Client-side filter on display - filter from the loaded messages
            // This is done by exposing FilteredMessages if needed,
            // but for simplicity we can indicate that search is recommended
            RaisePropertyChanged(nameof(QuickFilter));
            RaisePropertyChanged(nameof(FilterField));
        }

        #endregion

        #region Message Actions

        private async Task DeleteSelectedMessageAsync()
        {
            if (_mailService == null || SelectedMessage == null || SelectedFolder == null) return;
            var result = MessageBox.Show($"Delete message \"{SelectedMessage.Subject}\"?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                IsBusy = true;
                await _mailService.DeleteMessageAsync(SelectedFolder.FullPath, SelectedMessage.UniqueId);
                Messages.Remove(SelectedMessage);
                FullMessage = null;
            }
            catch (Exception ex) { StatusMessage = $"Delete failed: {ex.Message}"; }
            finally { IsBusy = false; }
        }

        private async Task ToggleReadAsync()
        {
            if (_mailService == null || SelectedMessage == null || SelectedFolder == null) return;
            try
            {
                bool newState = !SelectedMessage.IsRead;
                await _mailService.SetReadAsync(SelectedFolder.FullPath, SelectedMessage.UniqueId, newState);
                SelectedMessage.IsRead = newState;
                // Force re-render of the item
                var idx = Messages.IndexOf(SelectedMessage);
                if (idx >= 0) { var item = Messages[idx]; Messages[idx] = item; }
            }
            catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        }

        private async Task ToggleFlagAsync()
        {
            if (_mailService == null || SelectedMessage == null || SelectedFolder == null) return;
            try
            {
                bool newState = !SelectedMessage.IsFlagged;
                await _mailService.SetFlaggedAsync(SelectedFolder.FullPath, SelectedMessage.UniqueId, newState);
                SelectedMessage.IsFlagged = newState;
                var idx = Messages.IndexOf(SelectedMessage);
                if (idx >= 0) { var item = Messages[idx]; Messages[idx] = item; }
            }
            catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        }

        private async Task MoveToFolderAsync(string? targetFolder)
        {
            if (_mailService == null || SelectedMessage == null || SelectedFolder == null || string.IsNullOrEmpty(targetFolder)) return;
            try
            {
                IsBusy = true;
                await _mailService.MoveMessageAsync(SelectedFolder.FullPath, SelectedMessage.UniqueId, targetFolder);
                Messages.Remove(SelectedMessage);
                FullMessage = null;
            }
            catch (Exception ex) { StatusMessage = $"Move failed: {ex.Message}"; }
            finally { IsBusy = false; }
        }

        private async Task SaveAttachmentAsync(MailAttachment? attachment)
        {
            if (_mailService == null || SelectedMessage == null || SelectedFolder == null || attachment == null) return;
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = attachment.FileName,
                    Title = "Save Attachment"
                };

                if (dialog.ShowDialog() == true)
                {
                    IsBusy = true;
                    var data = await _mailService.DownloadAttachmentAsync(
                        SelectedFolder.FullPath, SelectedMessage.UniqueId, attachment.FileName);

                    if (data != null)
                    {
                        await File.WriteAllBytesAsync(dialog.FileName, data);
                        StatusMessage = $"Saved: {dialog.FileName}";
                    }
                    else
                    {
                        StatusMessage = "Failed to download attachment";
                    }
                }
            }
            catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; }
            finally { IsBusy = false; }
        }

        #endregion

        #region Account Management

        /// <summary>
        /// Applies preset IMAP/SMTP server settings for well-known providers.
        /// </summary>
        private void ApplyAccountTypePreset(MailAccountType type)
        {
            switch (type)
            {
                case MailAccountType.Gmail:
                    EditImapServer = "imap.gmail.com";
                    EditImapPort = 993;
                    EditImapUseSsl = true;
                    EditSmtpServer = "smtp.gmail.com";
                    EditSmtpPort = 587;
                    EditSmtpUseSsl = true;
                    EditUseOAuth = false;
                    EditClientId = "";
                    break;
                case MailAccountType.Yahoo:
                    EditImapServer = "imap.mail.yahoo.com";
                    EditImapPort = 993;
                    EditImapUseSsl = true;
                    EditSmtpServer = "smtp.mail.yahoo.com";
                    EditSmtpPort = 587;
                    EditSmtpUseSsl = true;
                    EditUseOAuth = false;
                    EditClientId = "";
                    break;
                case MailAccountType.Hotmail:
                    EditImapServer = "outlook.office365.com";
                    EditImapPort = 993;
                    EditImapUseSsl = true;
                    EditSmtpServer = "smtp.office365.com";
                    EditSmtpPort = 587;
                    EditSmtpUseSsl = true;
                    EditUseOAuth = false;
                    EditClientId = OAuthTokenService.GetDefaultClientId(MailAccountType.Hotmail);
                    break;
                case MailAccountType.IMAP:
                    // Clear fields for manual entry
                    EditImapServer = "";
                    EditImapPort = 993;
                    EditImapUseSsl = true;
                    EditSmtpServer = "";
                    EditSmtpPort = 587;
                    EditSmtpUseSsl = true;
                    EditUseOAuth = false;
                    EditClientId = "";
                    break;
                case MailAccountType.Exchange:
                    EditImapServer = "outlook.office365.com";
                    EditImapPort = 993;
                    EditImapUseSsl = true;
                    EditSmtpServer = "smtp.office365.com";
                    EditSmtpPort = 587;
                    EditSmtpUseSsl = true;
                    EditUseOAuth = true; // Default ON for Exchange (MFA)
                    EditClientId = OAuthTokenService.GetDefaultClientId(MailAccountType.Exchange);
                    break;
            }
        }

        private void AddAccount()
        {
            _editAccountId = null;
            EditAccountName = "";
            EditEmailAddress = "";
            EditAccountType = MailAccountType.IMAP;
            EditImapServer = "";
            EditImapPort = 993;
            EditImapUseSsl = true;
            EditSmtpServer = "";
            EditSmtpPort = 587;
            EditSmtpUseSsl = true;
            EditUsername = "";
            EditPassword = "";
            EditUseOAuth = false;
            EditTenantId = "";
            EditClientId = "";
            IsEditingAccount = true;
            SelectedTabIndex = 2; // Switch to Accounts tab
        }

        private async Task SaveAccountAsync()
        {
            var account = BuildAccountFromEdit();

            // Encrypt password
            if (!string.IsNullOrEmpty(account.Password))
                EncryptPassword(account);

            if (_editAccountId != null)
            {
                var existing = Accounts.FirstOrDefault(a => a.Id == _editAccountId);
                if (existing != null)
                {
                    var idx = Accounts.IndexOf(existing);
                    Accounts[idx] = account;
                    account.Id = _editAccountId;
                }
            }
            else
            {
                Accounts.Add(account);
            }

            IsEditingAccount = false;
            await PersistAccountsAsync();
            StatusMessage = $"Account \"{account.DisplayName}\" saved";
        }

        private MailAccountConfig BuildAccountFromEdit()
        {
            return new MailAccountConfig
            {
                Id = _editAccountId ?? Guid.NewGuid().ToString("N"),
                DisplayName = EditAccountName,
                EmailAddress = EditEmailAddress,
                AccountType = EditAccountType,
                ImapServer = EditImapServer,
                ImapPort = EditImapPort,
                ImapUseSsl = EditImapUseSsl,
                SmtpServer = EditSmtpServer,
                SmtpPort = EditSmtpPort,
                SmtpUseSsl = EditSmtpUseSsl,
                Username = EditUsername,
                Password = EditPassword,
                UseOAuth = EditUseOAuth,
                TenantId = EditTenantId,
                ClientId = EditClientId
            };
        }

        private async Task RemoveAccountAsync()
        {
            if (SelectedAccount == null) return;
            var result = MessageBox.Show($"Remove account \"{SelectedAccount.DisplayName}\"?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            if (IsConnected && _mailService != null)
                await DisconnectAsync();

            Accounts.Remove(SelectedAccount);
            await PersistAccountsAsync();
        }

        private async Task PersistAccountsAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(Accounts.ToList(), new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_accountsPath, json);
            }
            catch { }
        }

        private async Task LoadAccountsAsync()
        {
            try
            {
                if (File.Exists(_accountsPath))
                {
                    var json = await File.ReadAllTextAsync(_accountsPath);
                    var accounts = JsonSerializer.Deserialize<List<MailAccountConfig>>(json);
                    if (accounts != null)
                    {
                        foreach (var a in accounts)
                            Accounts.Add(a);
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Rule Management

        private void AddRule()
        {
            _editRuleId = null;
            EditRuleName = "";
            EditRuleMatchAll = true;
            EditRuleAction = MailRuleAction.MoveToFolder;
            EditRuleTargetFolder = "";
            EditRuleStopProcessing = true;
            EditRuleApplyToExisting = false;
            EditRuleConditions.Clear();
            EditRuleConditions.Add(new MailRuleCondition());
            IsEditingRule = true;
            SelectedTabIndex = 1; // Switch to Rules tab
        }

        private async Task SaveRuleAsync()
        {
            var rule = new MailRule
            {
                Id = _editRuleId ?? Guid.NewGuid().ToString("N"),
                Name = EditRuleName,
                MatchAll = EditRuleMatchAll,
                Action = EditRuleAction,
                TargetFolder = EditRuleTargetFolder,
                StopProcessing = EditRuleStopProcessing,
                ApplyToExisting = EditRuleApplyToExisting,
                Conditions = EditRuleConditions.ToList()
            };

            if (_editRuleId != null)
            {
                var existing = Rules.FirstOrDefault(r => r.Id == _editRuleId);
                if (existing != null)
                {
                    var idx = Rules.IndexOf(existing);
                    Rules[idx] = rule;
                }
            }
            else
            {
                Rules.Add(rule);
            }

            IsEditingRule = false;
            await PersistRulesAsync();
            StatusMessage = $"Rule \"{rule.Name}\" saved";

            // Apply to existing if requested
            if (rule.ApplyToExisting && IsConnected && SelectedFolder != null)
            {
                await ApplyRulesAsync();
            }
        }

        private async Task RemoveRuleAsync()
        {
            if (SelectedRule == null) return;
            Rules.Remove(SelectedRule);
            await PersistRulesAsync();
        }

        private async Task ApplyRulesAsync()
        {
            if (_mailService == null || SelectedFolder == null) return;

            try
            {
                IsBusy = true;
                var count = await _ruleEngine.ApplyRulesAsync(
                    _mailService, Rules.ToList(), SelectedFolder.FullPath, Messages.ToList());
                StatusMessage = $"Applied rules to {count} message(s)";

                // Refresh
                await LoadMessagesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Rule application failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PersistRulesAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(Rules.ToList(), new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_rulesPath, json);
            }
            catch { }
        }

        private async Task LoadRulesAsync()
        {
            try
            {
                if (File.Exists(_rulesPath))
                {
                    var json = await File.ReadAllTextAsync(_rulesPath);
                    var rules = JsonSerializer.Deserialize<List<MailRule>>(json);
                    if (rules != null)
                    {
                        foreach (var r in rules)
                            Rules.Add(r);
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Password Encryption (DPAPI)

        private static void EncryptPassword(MailAccountConfig account)
        {
            if (string.IsNullOrEmpty(account.Password)) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(account.Password);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                account.EncryptedPassword = Convert.ToBase64String(encrypted);
                account.Password = ""; // Clear plaintext
            }
            catch { }
        }

        private static void DecryptPassword(MailAccountConfig account)
        {
            if (string.IsNullOrEmpty(account.EncryptedPassword)) return;
            try
            {
                var encrypted = Convert.FromBase64String(account.EncryptedPassword);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                account.Password = Encoding.UTF8.GetString(decrypted);
            }
            catch { }
        }

        #endregion

        #region OAuth Token Acquisition

        /// <summary>
        /// Acquires an OAuth2 access token for the account.
        /// Tries to refresh an existing token first; if that fails, does full browser auth.
        /// Stores the refresh token (DPAPI-encrypted) for future use.
        /// </summary>
        private async Task AcquireOAuthTokenAsync(MailAccountConfig account, CancellationToken ct)
        {
            // Try refresh first if we have a saved refresh token
            if (!string.IsNullOrEmpty(account.EncryptedRefreshToken))
            {
                try
                {
                    var refreshToken = DecryptRefreshToken(account);
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        Application.Current?.Dispatcher.Invoke(() => StatusMessage = "Refreshing OAuth token...");
                        var result = await OAuthTokenService.RefreshTokenAsync(account, refreshToken, ct);
                        account.OAuthAccessToken = result.AccessToken;

                        // Update refresh token if provider returned a new one
                        if (!string.IsNullOrEmpty(result.RefreshToken) && result.RefreshToken != refreshToken)
                        {
                            EncryptRefreshToken(account, result.RefreshToken);
                            await PersistAccountsAsync();
                        }
                        return;
                    }
                }
                catch
                {
                    // Refresh failed — fall through to full auth
                }
            }

            // Full browser auth flow
            void StatusCallback(string msg)
            {
                Application.Current?.Dispatcher.Invoke(() => StatusMessage = msg);
            }

            var authResult = await OAuthTokenService.AuthenticateAsync(account, StatusCallback, ct);
            account.OAuthAccessToken = authResult.AccessToken;

            // Persist refresh token for next time
            if (!string.IsNullOrEmpty(authResult.RefreshToken))
            {
                EncryptRefreshToken(account, authResult.RefreshToken);

                // Find the account in our list and update it
                var saved = Accounts.FirstOrDefault(a => a.Id == account.Id);
                if (saved != null)
                    saved.EncryptedRefreshToken = account.EncryptedRefreshToken;

                await PersistAccountsAsync();
            }
        }

        private static void EncryptRefreshToken(MailAccountConfig account, string refreshToken)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(refreshToken);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                account.EncryptedRefreshToken = Convert.ToBase64String(encrypted);
            }
            catch { }
        }

        private static string DecryptRefreshToken(MailAccountConfig account)
        {
            if (string.IsNullOrEmpty(account.EncryptedRefreshToken)) return "";
            try
            {
                var encrypted = Convert.FromBase64String(account.EncryptedRefreshToken);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch { return ""; }
        }

        #endregion

        #region IDisposable

        private void DisposeService()
        {
            if (_mailService is IDisposable disposable)
                disposable.Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _cts?.Cancel();
                _cts?.Dispose();
                DisposeService();
            }
        }

        #endregion
    }
}
