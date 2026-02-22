using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class BulkChecksumViewModel : BindableBase
    {
        private readonly BulkChecksumService _service = new();
        private CancellationTokenSource? _cts;

        public BulkChecksumViewModel()
        {
            Results = new ObservableCollection<ChecksumResult>();
            Verifications = new ObservableCollection<ChecksumVerification>();
            Algorithms = new ObservableCollection<string>(BulkChecksumService.SupportedAlgorithms);
            SelectedAlgorithm = "SHA256";

            AddFilesCommand = new RelayCommand(_ => AddFiles());
            AddFolderCommand = new RelayCommand(_ => AddFolder());
            ComputeCommand = new RelayCommand(async _ => await ComputeAsync(), _ => FilePaths.Count > 0);
            VerifyCommand = new RelayCommand(async _ => await VerifyAsync());
            LoadChecksumFileCommand = new RelayCommand(async _ => await LoadChecksumFileAsync());
            ExportCommand = new RelayCommand(async _ => await ExportAsync(), _ => Results.Count > 0);
            ClearCommand = new RelayCommand(_ => Clear());
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsProcessing);
            CopyHashCommand = new RelayCommand(param => CopyHash(param));
        }

        private ObservableCollection<string> _filePaths = new();
        public ObservableCollection<string> FilePaths => _filePaths;

        public ObservableCollection<ChecksumResult> Results { get; }
        public ObservableCollection<ChecksumVerification> Verifications { get; }
        public ObservableCollection<string> Algorithms { get; }

        private string _selectedAlgorithm = "SHA256";
        public string SelectedAlgorithm { get => _selectedAlgorithm; set => SetProperty(ref _selectedAlgorithm, value); }

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isProcessing;
        public bool IsProcessing { get => _isProcessing; set => SetProperty(ref _isProcessing, value); }

        private double _progress;
        public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private int _selectedTabIndex;
        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

        // For verify tab - single file verification
        private string _verifyFilePath = "";
        public string VerifyFilePath { get => _verifyFilePath; set => SetProperty(ref _verifyFilePath, value); }

        private string _expectedHash = "";
        public string ExpectedHash
        {
            get => _expectedHash;
            set
            {
                if (SetProperty(ref _expectedHash, value) && !string.IsNullOrEmpty(value))
                {
                    var detected = BulkChecksumService.DetectAlgorithm(value.Trim());
                    if (detected != "Unknown")
                        DetectedAlgorithm = detected;
                }
            }
        }

        private string _detectedAlgorithm = "";
        public string DetectedAlgorithm { get => _detectedAlgorithm; set => SetProperty(ref _detectedAlgorithm, value); }

        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand ComputeCommand { get; }
        public ICommand VerifyCommand { get; }
        public ICommand LoadChecksumFileCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyHashCommand { get; }

        private void AddFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "All Files|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                    if (!FilePaths.Contains(file))
                        FilePaths.Add(file);
                StatusMessage = $"{FilePaths.Count} file(s) queued.";
            }
        }

        private void AddFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Folder"
            };
            if (dialog.ShowDialog() == true)
            {
                var files = Directory.GetFiles(dialog.FolderName, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                    if (!FilePaths.Contains(file))
                        FilePaths.Add(file);
                StatusMessage = $"{FilePaths.Count} file(s) queued.";
            }
        }

        private async Task ComputeAsync()
        {
            try
            {
                IsProcessing = true;
                _cts = new CancellationTokenSource();
                Results.Clear();
                StatusMessage = "Computing checksums...";

                var progressHandler = new Progress<ChecksumProgress>(p =>
                {
                    Progress = p.Percent;
                    StatusMessage = $"Hashing {p.Current}/{p.Total}...";
                });

                var results = await _service.ComputeChecksumsAsync(FilePaths, SelectedAlgorithm,
                    progressHandler, _cts.Token);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var r in results)
                        Results.Add(r);
                });

                StatusMessage = $"Done. {results.Count(r => r.Success)} hashed, {results.Count(r => !r.Success)} errors.";
                SelectedTabIndex = 0; // Show results tab
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                Progress = 0;
            }
        }

        private async Task VerifyAsync()
        {
            if (string.IsNullOrWhiteSpace(VerifyFilePath) || string.IsNullOrWhiteSpace(ExpectedHash))
            {
                StatusMessage = "Select a file and enter the expected hash.";
                return;
            }

            try
            {
                IsProcessing = true;
                _cts = new CancellationTokenSource();
                var algo = !string.IsNullOrEmpty(DetectedAlgorithm) ? DetectedAlgorithm : SelectedAlgorithm;

                var entries = new List<(string, string)> { (VerifyFilePath, ExpectedHash.Trim()) };
                var results = await _service.VerifyChecksumsAsync(entries, algo, ct: _cts.Token);

                Verifications.Clear();
                foreach (var r in results)
                    Verifications.Add(r);

                var v = results.FirstOrDefault();
                StatusMessage = v?.Match == true ? "✅ Hash matches!" : $"❌ Hash MISMATCH! Actual: {v?.ActualHash}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task LoadChecksumFileAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Checksum Files|*.sha256;*.sha1;*.md5;*.sha512;*.txt|All Files|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsProcessing = true;
                    var entries = _service.ParseChecksumFile(dialog.FileName);
                    if (entries.Count == 0)
                    {
                        StatusMessage = "No valid entries found in checksum file.";
                        return;
                    }

                    // Auto-detect algorithm from first hash
                    var algo = BulkChecksumService.DetectAlgorithm(entries[0].ExpectedHash);
                    if (algo != "Unknown") SelectedAlgorithm = algo;

                    var results = await _service.VerifyChecksumsAsync(entries, SelectedAlgorithm,
                        ct: CancellationToken.None);

                    Verifications.Clear();
                    foreach (var r in results)
                        Verifications.Add(r);

                    var matched = results.Count(r => r.Match);
                    var failed = results.Count(r => !r.Match);
                    StatusMessage = $"Verified {results.Count} files: {matched} matched, {failed} failed.";
                    SelectedTabIndex = 1; // Show verification tab
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
                finally
                {
                    IsProcessing = false;
                }
            }
        }

        private async Task ExportAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = $"{SelectedAlgorithm} Checksum|*.{SelectedAlgorithm.ToLower()}|Text File|*.txt",
                DefaultExt = $".{SelectedAlgorithm.ToLower()}"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _service.GenerateChecksumFileAsync(Results.ToList(), dialog.FileName);
                    StatusMessage = $"Exported to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }
        }

        private void Clear()
        {
            FilePaths.Clear();
            Results.Clear();
            Verifications.Clear();
            StatusMessage = "Cleared.";
        }

        private void CopyHash(object? param)
        {
            if (param is ChecksumResult result)
            {
                try
                {
                    System.Windows.Clipboard.SetText(result.Hash);
                    StatusMessage = $"Copied hash for {result.FileName}";
                }
                catch { }
            }
        }
    }
}
