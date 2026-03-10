using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class SshKeyEntry
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string PublicKeyPath { get; set; } = "";
        public string PrivateKeyPath { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public DateTime Created { get; set; }
        public string DisplayInfo => $"{Type} — {Fingerprint}";
    }

    public class SshKeyManagerViewModel : BindableBase
    {
        private readonly string _sshDir;

        public SshKeyManagerViewModel()
        {
            _sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

            GenerateRsaCommand = new RelayCommand(_ => GenerateKey("rsa", 4096));
            GenerateEd25519Command = new RelayCommand(_ => GenerateKey("ed25519", 0));
            RefreshCommand = new RelayCommand(_ => LoadKeys());
            CopyPublicKeyCommand = new RelayCommand(param =>
            {
                if (param is SshKeyEntry key && File.Exists(key.PublicKeyPath))
                {
                    Clipboard.SetText(File.ReadAllText(key.PublicKeyPath).Trim());
                    StatusMessage = $"Public key copied to clipboard: {key.Name}";
                }
            });
            OpenFolderCommand = new RelayCommand(_ =>
            {
                if (Directory.Exists(_sshDir))
                    Process.Start(new ProcessStartInfo("explorer.exe", _sshDir) { UseShellExecute = true });
            });
            DeleteKeyCommand = new RelayCommand(param =>
            {
                if (param is SshKeyEntry key)
                {
                    var result = MessageBox.Show(
                        $"Delete SSH key '{key.Name}'?\n\nThis will permanently remove:\n• {key.PrivateKeyPath}\n• {key.PublicKeyPath}",
                        "Delete SSH Key", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            if (File.Exists(key.PrivateKeyPath)) File.Delete(key.PrivateKeyPath);
                            if (File.Exists(key.PublicKeyPath)) File.Delete(key.PublicKeyPath);
                            Keys.Remove(key);
                            StatusMessage = $"Deleted key: {key.Name}";
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = $"Delete failed: {ex.Message}";
                        }
                    }
                }
            });
            ViewPublicKeyCommand = new RelayCommand(param =>
            {
                if (param is SshKeyEntry key && File.Exists(key.PublicKeyPath))
                {
                    var content = File.ReadAllText(key.PublicKeyPath).Trim();
                    MessageBox.Show(content, $"Public Key: {key.Name}", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });

            LoadKeys();
        }

        public ObservableCollection<SshKeyEntry> Keys { get; } = new();

        private string _keyName = "";
        public string KeyName { get => _keyName; set => SetProperty(ref _keyName, value); }

        private string _passphrase = "";
        public string Passphrase { get => _passphrase; set => SetProperty(ref _passphrase, value); }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

        public ICommand GenerateRsaCommand { get; }
        public ICommand GenerateEd25519Command { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CopyPublicKeyCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand DeleteKeyCommand { get; }
        public ICommand ViewPublicKeyCommand { get; }

        private void LoadKeys()
        {
            Keys.Clear();
            if (!Directory.Exists(_sshDir)) return;

            var pubFiles = Directory.GetFiles(_sshDir, "*.pub");
            foreach (var pubFile in pubFiles)
            {
                var privatePath = pubFile[..^4]; // Remove .pub
                var name = Path.GetFileNameWithoutExtension(pubFile);
                var pubContent = File.ReadAllText(pubFile).Trim();
                var type = pubContent.StartsWith("ssh-rsa") ? "RSA" :
                           pubContent.StartsWith("ssh-ed25519") ? "Ed25519" :
                           pubContent.StartsWith("ecdsa") ? "ECDSA" : "Unknown";

                var fingerprint = ComputeFingerprint(pubContent);

                Keys.Add(new SshKeyEntry
                {
                    Name = name,
                    Type = type,
                    PublicKeyPath = pubFile,
                    PrivateKeyPath = privatePath,
                    Fingerprint = fingerprint,
                    Created = File.GetCreationTime(pubFile)
                });
            }
            StatusMessage = $"Found {Keys.Count} SSH key(s)";
        }

        private static string ComputeFingerprint(string pubKeyContent)
        {
            try
            {
                var parts = pubKeyContent.Split(' ');
                if (parts.Length < 2) return "N/A";
                var keyBytes = Convert.FromBase64String(parts[1]);
                var hash = SHA256.HashData(keyBytes);
                return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
            }
            catch
            {
                return "N/A";
            }
        }

        private void GenerateKey(string type, int bits)
        {
            var name = string.IsNullOrWhiteSpace(KeyName) ? $"id_{type}" : KeyName.Trim();
            var keyPath = Path.Combine(_sshDir, name);

            if (File.Exists(keyPath))
            {
                var overwrite = MessageBox.Show(
                    $"Key '{name}' already exists. Overwrite?",
                    "Key Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes) return;
                File.Delete(keyPath);
                File.Delete(keyPath + ".pub");
            }

            if (!Directory.Exists(_sshDir))
                Directory.CreateDirectory(_sshDir);

            IsBusy = true;
            StatusMessage = $"Generating {type.ToUpperInvariant()} key...";

            try
            {
                var args = $"-t {type} -f \"{keyPath}\" -N \"{Passphrase}\" -C \"{Environment.UserName}@{Environment.MachineName}\"";
                if (bits > 0) args = $"-t {type} -b {bits} -f \"{keyPath}\" -N \"{Passphrase}\" -C \"{Environment.UserName}@{Environment.MachineName}\"";

                var psi = new ProcessStartInfo("ssh-keygen", args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(30000);
                    if (process.ExitCode == 0)
                    {
                        StatusMessage = $"Generated {type.ToUpperInvariant()} key: {name}";
                        LoadKeys();
                    }
                    else
                    {
                        var error = process.StandardError.ReadToEnd();
                        StatusMessage = $"ssh-keygen failed: {error}";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Passphrase = ""; // Clear passphrase from memory
            }
        }
    }
}
