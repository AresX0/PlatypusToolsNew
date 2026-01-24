using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.IO;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for saved session display.
    /// </summary>
    public class SavedSessionViewModel : BindableBase
    {
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public string PrivateKeyPath { get; set; } = string.Empty;
        public bool UseSsh { get; set; } = true;
        public string Protocol => UseSsh ? "SSH" : "Telnet";
        public bool HasKey => !string.IsNullOrEmpty(PrivateKeyPath);
        public string DisplayText => $"{Name} ({Username}@{Host}:{Port})" + (HasKey ? " 🔑" : "");
    }

    /// <summary>
    /// ViewModel for Telnet/SSH terminal client.
    /// Memory-efficient with streaming output.
    /// </summary>
    public class TerminalClientViewModel : BindableBase, IDisposable
    {
        private readonly TerminalService _terminalService;
        private readonly StringBuilder _outputBuffer;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private readonly string _sessionsPath;

        public TerminalClientViewModel()
        {
            _terminalService = new TerminalService();
            _outputBuffer = new StringBuilder();

            _terminalService.DataReceived += OnDataReceived;
            _terminalService.StatusChanged += (s, status) =>
                Application.Current?.Dispatcher.Invoke(() => StatusMessage = status);
            _terminalService.Disconnected += (s, e) =>
                Application.Current?.Dispatcher.Invoke(() => IsConnected = false);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _sessionsPath = Path.Combine(appData, "PlatypusTools", "TerminalSessions.json");

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected && !string.IsNullOrWhiteSpace(Host));
            DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
            SendCommand = new RelayCommand(async _ => await SendCommandAsync(), _ => IsConnected && !string.IsNullOrWhiteSpace(CommandText));
            ClearCommand = new RelayCommand(_ => ClearOutput());
            OpenInTerminalCommand = new RelayCommand(_ => OpenInTerminal(), _ => UseSsh && !string.IsNullOrWhiteSpace(Host));
            SaveSessionCommand = new RelayCommand(_ => SaveSession(), _ => !string.IsNullOrWhiteSpace(Host));
            LoadSessionCommand = new RelayCommand(param => LoadSession(param as SavedSessionViewModel));
            DeleteSessionCommand = new RelayCommand(param => DeleteSession(param as SavedSessionViewModel));
            RefreshSessionsCommand = new RelayCommand(_ => LoadSavedSessions());
            BrowseKeyCommand = new RelayCommand(_ => BrowseForKey());
            LoadCredentialCommand = new RelayCommand(_ => RequestLoadCredential());

            LoadSavedSessions();
        }

        public ObservableCollection<SavedSessionViewModel> SavedSessions { get; } = new();

        private string _host = string.Empty;
        public string Host
        {
            get => _host;
            set
            {
                SetProperty(ref _host, value);
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)OpenInTerminalCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveSessionCommand).RaiseCanExecuteChanged();
            }
        }

        private int _port = 22;
        public int Port { get => _port; set => SetProperty(ref _port, value); }

        private string _username = string.Empty;
        public string Username { get => _username; set => SetProperty(ref _username, value); }

        private string _password = string.Empty;
        public string Password { get => _password; set => SetProperty(ref _password, value); }

        private string _privateKeyPath = string.Empty;
        public string PrivateKeyPath
        {
            get => _privateKeyPath;
            set => SetProperty(ref _privateKeyPath, value);
        }

        private bool _useSsh = true;
        public bool UseSsh
        {
            get => _useSsh;
            set
            {
                SetProperty(ref _useSsh, value);
                Port = value ? 22 : 23;
                ((RelayCommand)OpenInTerminalCommand).RaiseCanExecuteChanged();
            }
        }

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
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

        private string _commandText = string.Empty;
        public string CommandText
        {
            get => _commandText;
            set
            {
                SetProperty(ref _commandText, value);
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
            }
        }

        private string _outputText = string.Empty;
        public string OutputText { get => _outputText; set => SetProperty(ref _outputText, value); }

        private string _statusMessage = "Ready - Enter host details and click Connect";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _sessionName = string.Empty;
        public string SessionName { get => _sessionName; set => SetProperty(ref _sessionName, value); }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand OpenInTerminalCommand { get; }
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
                // Parse host:port if included in key
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

        private void OnDataReceived(object? sender, string data)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _outputBuffer.Append(data);
                // Limit buffer size for memory efficiency
                if (_outputBuffer.Length > 100000)
                {
                    _outputBuffer.Remove(0, _outputBuffer.Length - 80000);
                }
                OutputText = _outputBuffer.ToString();
            });
        }

        private async Task ConnectAsync()
        {
            try
            {
                IsBusy = true;
                _cts = new CancellationTokenSource();

                _terminalService.Host = Host;
                _terminalService.Port = Port;
                _terminalService.Username = Username;
                _terminalService.Password = Password;
                _terminalService.PrivateKeyPath = PrivateKeyPath;
                _terminalService.UseSsh = UseSsh;

                ClearOutput();
                IsConnected = await _terminalService.ConnectAsync(_cts.Token);

                if (IsConnected)
                {
                    AppendOutput($"Connected to {Host}:{Port}\r\n");
                }
                else if (UseSsh)
                {
                    // SSH requires external terminal
                    AppendOutput("SSH connections require an external terminal.\r\n");
                    AppendOutput("Click 'Open in Terminal' to start an SSH session.\r\n");
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
            _terminalService.Disconnect();
            IsConnected = false;
            AppendOutput("\r\n--- Disconnected ---\r\n");
        }

        private async Task SendCommandAsync()
        {
            if (string.IsNullOrWhiteSpace(CommandText)) return;

            try
            {
                AppendOutput($"> {CommandText}\r\n");
                await _terminalService.SendAsync(CommandText);
                CommandText = string.Empty;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error: {ex.Message}\r\n");
            }
        }

        private void ClearOutput()
        {
            _outputBuffer.Clear();
            OutputText = string.Empty;
        }

        private void AppendOutput(string text)
        {
            _outputBuffer.Append(text);
            OutputText = _outputBuffer.ToString();
        }

        private void OpenInTerminal()
        {
            _terminalService.Host = Host;
            _terminalService.Port = Port;
            _terminalService.Username = Username;
            _terminalService.PrivateKeyPath = PrivateKeyPath;
            _terminalService.OpenSshInTerminal();
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

            var session = new SavedSessionViewModel
            {
                Name = name,
                Host = Host,
                Port = Port,
                Username = Username,
                PrivateKeyPath = PrivateKeyPath,
                UseSsh = UseSsh
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

        private void LoadSession(SavedSessionViewModel? session)
        {
            if (session == null) return;

            Host = session.Host;
            Port = session.Port;
            Username = session.Username;
            PrivateKeyPath = session.PrivateKeyPath;
            UseSsh = session.UseSsh;
            SessionName = session.Name;
        }

        private void DeleteSession(SavedSessionViewModel? session)
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
                    var sessions = JsonSerializer.Deserialize<List<SavedSessionViewModel>>(json);
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
            ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _terminalService.Dispose();
                _disposed = true;
            }
        }
    }
}
