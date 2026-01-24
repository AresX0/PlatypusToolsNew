using PlatypusTools.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for FTP file item display.
    /// </summary>
    public class FtpFileItemViewModel : BindableBase
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public string DisplaySize { get; set; } = string.Empty;
        public string Icon => IsDirectory ? "📁" : "📄";
    }

    /// <summary>
    /// ViewModel for saved FTP session display.
    /// </summary>
    public class SavedFtpSessionViewModel : BindableBase
    {
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 21;
        public string Username { get; set; } = string.Empty;
        public bool UseSftp { get; set; }
        public bool UsePassive { get; set; } = true;
        public bool UseSsl { get; set; }
        public string PrivateKeyPath { get; set; } = string.Empty;
        public string DefaultRemotePath { get; set; } = "/";
        public string Protocol => UseSftp ? "SFTP" : (UseSsl ? "FTPS" : "FTP");
        public bool HasKey => !string.IsNullOrEmpty(PrivateKeyPath);
        public string DisplayText => $"{Name} ({Username}@{Host}:{Port})" + (HasKey ? " 🔑" : "");
    }

    /// <summary>
    /// ViewModel for FTP/SFTP client functionality.
    /// Memory-efficient implementation with streaming file transfers.
    /// </summary>
    public class FtpClientViewModel : BindableBase, IDisposable
    {
        private readonly FtpClientService _ftpService;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private readonly string _sessionsPath;

        public FtpClientViewModel()
        {
            _ftpService = new FtpClientService();
            _ftpService.StatusChanged += (s, status) => 
                Application.Current?.Dispatcher.Invoke(() => StatusMessage = status);
            _ftpService.ProgressChanged += (s, progress) => 
                Application.Current?.Dispatcher.Invoke(() => TransferProgress = progress);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _sessionsPath = Path.Combine(appData, "PlatypusTools", "FtpSessions.json");

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected && !string.IsNullOrWhiteSpace(Host));
            DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => IsConnected);
            NavigateUpCommand = new RelayCommand(async _ => await NavigateUpAsync(), _ => IsConnected && CurrentPath != "/");
            DownloadCommand = new RelayCommand(async _ => await DownloadSelectedAsync(), _ => IsConnected && SelectedItem != null && !SelectedItem.IsDirectory);
            UploadCommand = new RelayCommand(async _ => await UploadAsync(), _ => IsConnected);
            DeleteCommand = new RelayCommand(async _ => await DeleteSelectedAsync(), _ => IsConnected && SelectedItem != null && !SelectedItem.IsDirectory);
            CreateFolderCommand = new RelayCommand(async _ => await CreateFolderAsync(), _ => IsConnected);
            NavigateToCommand = new RelayCommand(async param => await NavigateToItemAsync(param as FtpFileItemViewModel), _ => IsConnected);
            SaveSessionCommand = new RelayCommand(_ => SaveSession(), _ => !string.IsNullOrWhiteSpace(Host));
            LoadSessionCommand = new RelayCommand(param => LoadSession(param as SavedFtpSessionViewModel));
            DeleteSessionCommand = new RelayCommand(param => DeleteSession(param as SavedFtpSessionViewModel));
            RefreshSessionsCommand = new RelayCommand(_ => LoadSavedSessions());
            BrowseKeyCommand = new RelayCommand(_ => BrowseForKey());
            LoadCredentialCommand = new RelayCommand(_ => RequestLoadCredential());

            LoadSavedSessions();
        }

        public ObservableCollection<FtpFileItemViewModel> Files { get; } = new();
        public ObservableCollection<SavedFtpSessionViewModel> SavedSessions { get; } = new();

        private string _host = string.Empty;
        public string Host
        {
            get => _host;
            set
            {
                SetProperty(ref _host, value);
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            }
        }

        private int _port = 21;
        public int Port { get => _port; set => SetProperty(ref _port, value); }

        private string _username = "anonymous";
        public string Username { get => _username; set => SetProperty(ref _username, value); }

        private string _password = string.Empty;
        public string Password { get => _password; set => SetProperty(ref _password, value); }

        private bool _useSftp;
        public bool UseSftp
        {
            get => _useSftp;
            set
            {
                SetProperty(ref _useSftp, value);
                Port = value ? 22 : 21;
            }
        }

        private bool _usePassive = true;
        public bool UsePassive { get => _usePassive; set => SetProperty(ref _usePassive, value); }

        private bool _useSsl;
        public bool UseSsl { get => _useSsl; set => SetProperty(ref _useSsl, value); }

        private string _privateKeyPath = string.Empty;
        public string PrivateKeyPath { get => _privateKeyPath; set => SetProperty(ref _privateKeyPath, value); }

        private string _sessionName = string.Empty;
        public string SessionName { get => _sessionName; set => SetProperty(ref _sessionName, value); }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                SetProperty(ref _isConnected, value);
                RaiseCommandsCanExecuteChanged();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _currentPath = "/";
        public string CurrentPath
        {
            get => _currentPath;
            set
            {
                SetProperty(ref _currentPath, value);
                ((RelayCommand)NavigateUpCommand).RaiseCanExecuteChanged();
            }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private double _transferProgress;
        public double TransferProgress { get => _transferProgress; set => SetProperty(ref _transferProgress, value); }

        private FtpFileItemViewModel? _selectedItem;
        public FtpFileItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                SetProperty(ref _selectedItem, value);
                ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            }
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand UploadCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CreateFolderCommand { get; }
        public ICommand NavigateToCommand { get; }
        public ICommand SaveSessionCommand { get; }
        public ICommand LoadSessionCommand { get; }
        public ICommand DeleteSessionCommand { get; }
        public ICommand RefreshSessionsCommand { get; }
        public ICommand BrowseKeyCommand { get; }
        public ICommand LoadCredentialCommand { get; }

        /// <summary>
        /// Event raised when the ViewModel needs to display the credential picker.
        /// The view should handle this and call ApplyCredential when a selection is made.
        /// </summary>
        public event EventHandler? LoadCredentialRequested;

        private void RequestLoadCredential()
        {
            LoadCredentialRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Applies credential data to the connection fields.
        /// Called from view after user selects a credential.
        /// </summary>
        public void ApplyCredential(string username, string password, string? host = null)
        {
            Username = username;
            Password = password;
            if (!string.IsNullOrEmpty(host))
            {
                // Parse host:port if included
                if (host.Contains(':'))
                {
                    var parts = host.Split(':');
                    Host = parts[0];
                    if (int.TryParse(parts[1], out var port))
                    {
                        Port = port;
                    }
                }
                else
                {
                    Host = host;
                }
            }
            StatusMessage = "Credentials loaded - ready to connect";
        }

        private async Task ConnectAsync()
        {
            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                _ftpService.Host = Host;
                _ftpService.Port = Port;
                _ftpService.Username = Username;
                _ftpService.Password = Password;
                _ftpService.UseSftp = UseSftp;
                _ftpService.UsePassive = UsePassive;
                _ftpService.UseSsl = UseSsl;

                IsConnected = await _ftpService.TestConnectionAsync(_cts.Token);

                if (IsConnected)
                {
                    CurrentPath = "/";
                    await RefreshAsync();
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Disconnect()
        {
            _cts?.Cancel();
            IsConnected = false;
            Files.Clear();
            CurrentPath = "/";
            StatusMessage = "Disconnected";
        }

        private async Task RefreshAsync()
        {
            if (!IsConnected) return;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                var files = await _ftpService.ListDirectoryAsync(CurrentPath, _cts.Token);

                Files.Clear();
                foreach (var file in files)
                {
                    Files.Add(new FtpFileItemViewModel
                    {
                        Name = file.Name,
                        FullPath = file.FullPath,
                        IsDirectory = file.IsDirectory,
                        DisplaySize = file.DisplaySize
                    });
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task NavigateUpAsync()
        {
            if (CurrentPath == "/") return;

            var parent = Path.GetDirectoryName(CurrentPath)?.Replace('\\', '/') ?? "/";
            if (string.IsNullOrEmpty(parent)) parent = "/";
            CurrentPath = parent;
            await RefreshAsync();
        }

        private async Task NavigateToItemAsync(FtpFileItemViewModel? item)
        {
            if (item == null || !item.IsDirectory) return;

            CurrentPath = item.FullPath;
            await RefreshAsync();
        }

        private async Task DownloadSelectedAsync()
        {
            if (SelectedItem == null || SelectedItem.IsDirectory) return;

            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                // Show save dialog
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = SelectedItem.Name,
                    Title = "Save Downloaded File"
                };

                if (dialog.ShowDialog() == true)
                {
                    await _ftpService.DownloadFileAsync(SelectedItem.FullPath, dialog.FileName, _cts.Token);
                }
            }
            finally
            {
                IsBusy = false;
                TransferProgress = 0;
            }
        }

        private async Task UploadAsync()
        {
            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select File to Upload",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    var remotePath = CurrentPath.TrimEnd('/') + "/" + Path.GetFileName(dialog.FileName);
                    await _ftpService.UploadFileAsync(dialog.FileName, remotePath, _cts.Token);
                    await RefreshAsync();
                }
            }
            finally
            {
                IsBusy = false;
                TransferProgress = 0;
            }
        }

        private async Task DeleteSelectedAsync()
        {
            if (SelectedItem == null || SelectedItem.IsDirectory) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{SelectedItem.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    IsBusy = true;
                    _cts = new CancellationTokenSource();
                    await _ftpService.DeleteFileAsync(SelectedItem.FullPath, _cts.Token);
                    await RefreshAsync();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task CreateFolderAsync()
        {
            var dialog = new Views.InputDialogWindow("Enter folder name:", "New Folder");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.EnteredText))
            {
                try
                {
                    IsBusy = true;
                    _cts = new CancellationTokenSource();
                    var remotePath = CurrentPath.TrimEnd('/') + "/" + dialog.EnteredText;
                    await _ftpService.CreateDirectoryAsync(remotePath, _cts.Token);
                    await RefreshAsync();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private void BrowseForKey()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Private Key File",
                Filter = "Private Key Files (*.pem;*.ppk;id_rsa)|*.pem;*.ppk;id_rsa|All Files (*.*)|*.*",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh")
            };

            if (dialog.ShowDialog() == true)
            {
                PrivateKeyPath = dialog.FileName;
            }
        }

        private void SaveSession()
        {
            var name = string.IsNullOrWhiteSpace(SessionName) ? $"{Username}@{Host}" : SessionName;

            var session = new SavedFtpSessionViewModel
            {
                Name = name,
                Host = Host,
                Port = Port,
                Username = Username,
                UseSftp = UseSftp,
                UsePassive = UsePassive,
                UseSsl = UseSsl,
                PrivateKeyPath = PrivateKeyPath,
                DefaultRemotePath = CurrentPath
            };

            // Remove existing with same name
            for (int i = SavedSessions.Count - 1; i >= 0; i--)
            {
                if (SavedSessions[i].Name == name)
                {
                    SavedSessions.RemoveAt(i);
                }
            }

            SavedSessions.Insert(0, session);
            SaveSessionsToFile();
            StatusMessage = $"Session '{name}' saved";
        }

        private void LoadSession(SavedFtpSessionViewModel? session)
        {
            if (session == null) return;

            Host = session.Host;
            Port = session.Port;
            Username = session.Username;
            UseSftp = session.UseSftp;
            UsePassive = session.UsePassive;
            UseSsl = session.UseSsl;
            PrivateKeyPath = session.PrivateKeyPath;
            SessionName = session.Name;
        }

        private void DeleteSession(SavedFtpSessionViewModel? session)
        {
            if (session == null) return;

            SavedSessions.Remove(session);
            SaveSessionsToFile();
            StatusMessage = $"Session '{session.Name}' deleted";
        }

        private void LoadSavedSessions()
        {
            SavedSessions.Clear();

            try
            {
                if (File.Exists(_sessionsPath))
                {
                    var json = File.ReadAllText(_sessionsPath);
                    var sessions = JsonSerializer.Deserialize<List<SavedFtpSessionViewModel>>(json);
                    if (sessions != null)
                    {
                        foreach (var session in sessions)
                        {
                            SavedSessions.Add(session);
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveSessionsToFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(_sessionsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(SavedSessions.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_sessionsPath, json);
            }
            catch { }
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
            ((RelayCommand)NavigateUpCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DownloadCommand).RaiseCanExecuteChanged();
            ((RelayCommand)UploadCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CreateFolderCommand).RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _ftpService.Dispose();
                _disposed = true;
            }
        }
    }
}
