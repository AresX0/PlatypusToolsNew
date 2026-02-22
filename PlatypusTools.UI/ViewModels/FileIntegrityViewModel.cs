using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class FileIntegrityViewModel : BindableBase
    {
        private readonly FileIntegrityService _service = new();
        private CancellationTokenSource? _cts;
        private FileIntegrityBaseline? _currentBaseline;

        public FileIntegrityViewModel()
        {
            Changes = new ObservableCollection<FileChangeInfo>();
            Algorithms = new ObservableCollection<string> { "SHA256", "SHA512", "SHA1", "MD5" };
            SelectedAlgorithm = "SHA256";

            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            CreateBaselineCommand = new RelayCommand(async _ => await CreateBaselineAsync(), _ => !string.IsNullOrWhiteSpace(DirectoryPath));
            CompareCommand = new RelayCommand(async _ => await CompareAsync(), _ => _currentBaseline != null);
            SaveBaselineCommand = new RelayCommand(async _ => await SaveBaselineAsync(), _ => _currentBaseline != null);
            LoadBaselineCommand = new RelayCommand(async _ => await LoadBaselineAsync());
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);
        }

        private string _directoryPath = "";
        public string DirectoryPath { get => _directoryPath; set => SetProperty(ref _directoryPath, value); }

        private string _selectedAlgorithm = "SHA256";
        public string SelectedAlgorithm { get => _selectedAlgorithm; set => SetProperty(ref _selectedAlgorithm, value); }

        private bool _recursive = true;
        public bool Recursive { get => _recursive; set => SetProperty(ref _recursive, value); }

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isScanning;
        public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }

        private double _progress;
        public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private string _currentFile = "";
        public string CurrentFile { get => _currentFile; set => SetProperty(ref _currentFile, value); }

        private string _baselineInfo = "";
        public string BaselineInfo { get => _baselineInfo; set => SetProperty(ref _baselineInfo, value); }

        private int _addedCount;
        public int AddedCount { get => _addedCount; set => SetProperty(ref _addedCount, value); }

        private int _modifiedCount;
        public int ModifiedCount { get => _modifiedCount; set => SetProperty(ref _modifiedCount, value); }

        private int _deletedCount;
        public int DeletedCount { get => _deletedCount; set => SetProperty(ref _deletedCount, value); }

        public ObservableCollection<FileChangeInfo> Changes { get; }
        public ObservableCollection<string> Algorithms { get; }

        public ICommand BrowseFolderCommand { get; }
        public ICommand CreateBaselineCommand { get; }
        public ICommand CompareCommand { get; }
        public ICommand SaveBaselineCommand { get; }
        public ICommand LoadBaselineCommand { get; }
        public ICommand CancelCommand { get; }

        private void BrowseFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Directory to Monitor"
            };
            if (dialog.ShowDialog() == true)
            {
                DirectoryPath = dialog.FolderName;
            }
        }

        private async Task CreateBaselineAsync()
        {
            try
            {
                IsScanning = true;
                _cts = new CancellationTokenSource();
                StatusMessage = "Creating baseline...";

                var progressHandler = new Progress<FileIntegrityProgress>(p =>
                {
                    Progress = p.Percent;
                    CurrentFile = p.CurrentFile;
                    StatusMessage = $"Hashing {p.Current}/{p.Total}...";
                });

                _currentBaseline = await _service.CreateBaselineAsync(DirectoryPath, SelectedAlgorithm,
                    Recursive, progressHandler, _cts.Token);

                BaselineInfo = $"Baseline: {_currentBaseline.Entries.Count} files, {SelectedAlgorithm}, {_currentBaseline.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC";
                StatusMessage = $"Baseline created: {_currentBaseline.Entries.Count} files hashed.";

                if (_currentBaseline.Errors.Count > 0)
                    StatusMessage += $" ({_currentBaseline.Errors.Count} errors)";

                Changes.Clear();
                AddedCount = ModifiedCount = DeletedCount = 0;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Baseline creation cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                Progress = 0;
            }
        }

        private async Task CompareAsync()
        {
            if (_currentBaseline == null) return;

            try
            {
                IsScanning = true;
                _cts = new CancellationTokenSource();
                StatusMessage = "Comparing against baseline...";

                var progressHandler = new Progress<FileIntegrityProgress>(p =>
                {
                    Progress = p.Percent;
                    CurrentFile = p.CurrentFile;
                });

                var report = await _service.CompareWithBaselineAsync(_currentBaseline, progressHandler, _cts.Token);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Changes.Clear();
                    foreach (var c in report.Added) Changes.Add(c);
                    foreach (var c in report.Modified) Changes.Add(c);
                    foreach (var c in report.Deleted) Changes.Add(c);

                    AddedCount = report.Added.Count;
                    ModifiedCount = report.Modified.Count;
                    DeletedCount = report.Deleted.Count;
                });

                StatusMessage = report.HasChanges
                    ? $"Changes detected: {report.Added.Count} added, {report.Modified.Count} modified, {report.Deleted.Count} deleted"
                    : "No changes detected. Files are intact.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Comparison cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                Progress = 0;
            }
        }

        private async Task SaveBaselineAsync()
        {
            if (_currentBaseline == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Baseline|*.json",
                DefaultExt = ".json",
                FileName = "baseline.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _service.SaveBaselineAsync(_currentBaseline, dialog.FileName);
                    StatusMessage = $"Baseline saved to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error saving: {ex.Message}";
                }
            }
        }

        private async Task LoadBaselineAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON Baseline|*.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _currentBaseline = await _service.LoadBaselineAsync(dialog.FileName);
                    DirectoryPath = _currentBaseline.DirectoryPath;
                    SelectedAlgorithm = _currentBaseline.HashAlgorithm;
                    BaselineInfo = $"Loaded: {_currentBaseline.Entries.Count} files, {_currentBaseline.HashAlgorithm}, {_currentBaseline.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC";
                    StatusMessage = $"Baseline loaded: {_currentBaseline.Entries.Count} files.";
                    Changes.Clear();
                    AddedCount = ModifiedCount = DeletedCount = 0;
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error loading: {ex.Message}";
                }
            }
        }
    }
}
