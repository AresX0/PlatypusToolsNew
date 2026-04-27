using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class EncryptionFileItem
    {
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public string Size { get; set; } = "";
        public string Status { get; set; } = "Pending";
    }

    public class FileEncryptionViewModel : BindableBase
    {
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const int KeySize = 32; // AES-256
        private const int Iterations = 100_000;

        public FileEncryptionViewModel()
        {
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            ClearListCommand = new RelayCommand(_ => { Files.Clear(); StatusMessage = "List cleared"; });
            EncryptCommand = new RelayCommand(async _ => await ProcessFilesAsync(true), _ => Files.Count > 0 && !IsBusy && !string.IsNullOrEmpty(Password));
            DecryptCommand = new RelayCommand(async _ => await ProcessFilesAsync(false), _ => Files.Count > 0 && !IsBusy && !string.IsNullOrEmpty(Password));
            RemoveFileCommand = new RelayCommand(param =>
            {
                if (param is EncryptionFileItem item)
                    Files.Remove(item);
            });
            OpenOutputFolderCommand = new RelayCommand(_ =>
            {
                if (!string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder))
                    Process.Start(new ProcessStartInfo("explorer.exe", OutputFolder) { UseShellExecute = true });
            });
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutputFolder());
        }

        public ObservableCollection<EncryptionFileItem> Files { get; } = new();

        private string _password = "";
        public string Password { get => _password; set => SetProperty(ref _password, value); }

        private string _outputFolder = "";
        public string OutputFolder { get => _outputFolder; set => SetProperty(ref _outputFolder, value); }

        private bool _deleteOriginals;
        public bool DeleteOriginals { get => _deleteOriginals; set => SetProperty(ref _deleteOriginals, value); }

        private string _statusMessage = "Ready — Add files, set password, then encrypt or decrypt.";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

        private double _progress;
        public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

        public ICommand AddFilesCommand { get; }
        public ICommand ClearListCommand { get; }
        public ICommand EncryptCommand { get; }
        public ICommand DecryptCommand { get; }
        public ICommand RemoveFileCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand BrowseOutputCommand { get; }

        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Files to Encrypt/Decrypt",
                Multiselect = true,
                Filter = "All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                {
                    if (Files.Any(f => f.FilePath == file)) continue;
                    var fi = new FileInfo(file);
                    Files.Add(new EncryptionFileItem
                    {
                        FilePath = file,
                        Size = FormatSize(fi.Length),
                        Status = "Pending"
                    });
                }
                StatusMessage = $"{Files.Count} file(s) ready";
            }
        }

        private void BrowseOutputFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Output Folder"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                OutputFolder = dlg.SelectedPath;
        }

        private async Task ProcessFilesAsync(bool encrypt)
        {
            if (string.IsNullOrEmpty(Password))
            {
                StatusMessage = "Password is required.";
                return;
            }

            IsBusy = true;
            Progress = 0;
            var action = encrypt ? "Encrypting" : "Decrypting";
            int success = 0, fail = 0;

            try
            {
                for (int i = 0; i < Files.Count; i++)
                {
                    var item = Files[i];
                    item.Status = $"{action}...";
                    // Force UI update
                    RaisePropertyChanged(nameof(Files));

                    try
                    {
                        var outputDir = string.IsNullOrWhiteSpace(OutputFolder) ? Path.GetDirectoryName(item.FilePath)! : OutputFolder;
                        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                        string outputPath;
                        if (encrypt)
                        {
                            outputPath = Path.Combine(outputDir, Path.GetFileName(item.FilePath) + ".aes");
                            await Task.Run(() => EncryptFile(item.FilePath, outputPath, Password));
                        }
                        else
                        {
                            var name = Path.GetFileName(item.FilePath);
                            if (name.EndsWith(".aes", StringComparison.OrdinalIgnoreCase))
                                name = name[..^4];
                            else
                                name = "decrypted_" + name;
                            outputPath = Path.Combine(outputDir, name);
                            await Task.Run(() => DecryptFile(item.FilePath, outputPath, Password));
                        }

                        item.Status = "✅ Done";
                        success++;

                        if (DeleteOriginals && File.Exists(item.FilePath))
                            File.Delete(item.FilePath);
                    }
                    catch (Exception ex)
                    {
                        item.Status = $"❌ {ex.Message}";
                        fail++;
                        Debug.WriteLine($"FileEncryption error on {item.FilePath}: {ex}");
                    }

                    Progress = (double)(i + 1) / Files.Count * 100;
                }

                StatusMessage = $"Complete: {success} succeeded, {fail} failed";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void EncryptFile(string inputPath, string outputPath, string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var key = DeriveKey(password, salt);

            using var inputStream = File.OpenRead(inputPath);
            using var outputStream = File.Create(outputPath);

            // Write header: [SALT(16)][IV(16)][ENCRYPTED_DATA]
            outputStream.Write(salt);
            outputStream.Write(iv);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            using var cryptoStream = new CryptoStream(outputStream, encryptor, CryptoStreamMode.Write);
            inputStream.CopyTo(cryptoStream);
        }

        private static void DecryptFile(string inputPath, string outputPath, string password)
        {
            using var inputStream = File.OpenRead(inputPath);

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            if (inputStream.Read(salt, 0, SaltSize) != SaltSize)
                throw new InvalidDataException("Invalid encrypted file: cannot read salt");
            if (inputStream.Read(iv, 0, IvSize) != IvSize)
                throw new InvalidDataException("Invalid encrypted file: cannot read IV");

            var key = DeriveKey(password, salt);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var cryptoStream = new CryptoStream(inputStream, decryptor, CryptoStreamMode.Read);
            using var outputStream = File.Create(outputPath);
            cryptoStream.CopyTo(outputStream);
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            // Static Pbkdf2 API (non-obsolete equivalent of Rfc2898DeriveBytes ctor).
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }
    }
}
