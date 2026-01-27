using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class EmptyFolderItemViewModel : BindableBase
    {
        private bool _isSelected = true;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; RaisePropertyChanged(); } }
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public int Depth { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class EmptyFolderScannerViewModel : BindableBase
    {
        private readonly EmptyFolderScannerService _scanner = new();
        private CancellationTokenSource? _cts;

        public EmptyFolderScannerViewModel()
        {
            EmptyFolders = new ObservableCollection<EmptyFolderItemViewModel>();
            BrowseCommand = new RelayCommand(_ => Browse());
            ScanCommand = new RelayCommand(_ => Scan(), _ => CanScan());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsScanning);
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => !IsScanning && EmptyFolders.Any(f => f.IsSelected));
            DeleteAllCommand = new RelayCommand(_ => DeleteAll(), _ => !IsScanning && EmptyFolders.Any());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());
        }

        private bool CanScan() => !IsScanning && !string.IsNullOrWhiteSpace(SelectedFolder);

        private string _selectedFolder = string.Empty;
        public string SelectedFolder
        {
            get => _selectedFolder;
            set 
            { 
                _selectedFolder = value; 
                RaisePropertyChanged();
                RaiseCommandsCanExecuteChanged();
            }
        }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set 
            { 
                _isScanning = value; 
                RaisePropertyChanged();
                RaiseCommandsCanExecuteChanged();
            }
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private string _statusMessage = "Select a folder and click Scan to find empty folders.";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; RaisePropertyChanged(); }
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; RaisePropertyChanged(); }
        }

        private bool _ignoreJunkFiles = true;
        /// <summary>
        /// When true, folders containing only junk files (Thumbs.db, desktop.ini, etc.) are treated as empty.
        /// </summary>
        public bool IgnoreJunkFiles
        {
            get => _ignoreJunkFiles;
            set { _ignoreJunkFiles = value; RaisePropertyChanged(); }
        }

        public ObservableCollection<EmptyFolderItemViewModel> EmptyFolders { get; }

        public ICommand BrowseCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand DeleteAllCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }

        private void Browse()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "Select folder to scan for empty subfolders";
            dlg.ShowNewFolderButton = false;
            if (!string.IsNullOrWhiteSpace(SelectedFolder))
                dlg.SelectedPath = SelectedFolder;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SelectedFolder = dlg.SelectedPath;
            }
        }

        private async void Scan()
        {
            if (string.IsNullOrWhiteSpace(SelectedFolder))
                return;

            try
            {
                IsScanning = true;
                EmptyFolders.Clear();
                Progress = 0;
                StatusMessage = "Scanning for empty folders...";
                StatusBarViewModel.Instance.StartOperation("Scanning for empty folders...", isCancellable: true);
                _cts = new CancellationTokenSource();

                // Apply settings to scanner
                _scanner.IgnoreJunkFiles = IgnoreJunkFiles;

                _scanner.ProgressChanged += msg => _ = Application.Current?.Dispatcher.InvokeAsync(() => StatusMessage = msg);
                _scanner.FolderScanned += count => _ = Application.Current?.Dispatcher.InvokeAsync(() => Progress = count);

                var results = await _scanner.ScanForEmptyFoldersAsync(SelectedFolder, _cts.Token);

                foreach (var folder in results.OrderBy(f => f.Path))
                {
                    EmptyFolders.Add(new EmptyFolderItemViewModel
                    {
                        Path = folder.Path,
                        Name = folder.Name,
                        ParentPath = folder.ParentPath,
                        Depth = folder.Depth,
                        CreatedDate = folder.CreatedDate,
                        IsSelected = true
                    });
                }

                StatusMessage = $"Found {EmptyFolders.Count} empty folder(s).";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                StatusBarViewModel.Instance.CompleteOperation(StatusMessage);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Cancel()
        {
            _cts?.Cancel();
        }

        private async void DeleteSelected()
        {
            var selectedPaths = EmptyFolders.Where(f => f.IsSelected).Select(f => f.Path).ToList();
            if (!selectedPaths.Any())
            {
                MessageBox.Show("No folders selected.", "Delete Empty Folders", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedPaths.Count} selected empty folder(s)?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                IsScanning = true;
                StatusMessage = "Deleting selected folders...";
                _cts = new CancellationTokenSource();

                // Ensure scanner has current IgnoreJunkFiles setting
                _scanner.IgnoreJunkFiles = IgnoreJunkFiles;

                var progress = new Progress<string>(msg => StatusMessage = msg);
                var deleted = await _scanner.DeleteFoldersAsync(selectedPaths, progress, _cts.Token);

                // Remove deleted items from the list
                var toRemove = EmptyFolders.Where(f => f.IsSelected && !System.IO.Directory.Exists(f.Path)).ToList();
                foreach (var item in toRemove)
                    EmptyFolders.Remove(item);

                StatusMessage = $"Deleted {deleted} folder(s). {EmptyFolders.Count} remaining.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                StatusBarViewModel.Instance.CompleteOperation(StatusMessage);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async void DeleteAll()
        {
            if (!EmptyFolders.Any())
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete ALL {EmptyFolders.Count} empty folder(s)?",
                "Confirm Delete All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                IsScanning = true;
                StatusMessage = "Deleting all empty folders...";
                _cts = new CancellationTokenSource();

                // Ensure scanner has current IgnoreJunkFiles setting
                _scanner.IgnoreJunkFiles = IgnoreJunkFiles;

                var allPaths = EmptyFolders.Select(f => f.Path).ToList();
                var progress = new Progress<string>(msg => StatusMessage = msg);
                var deleted = await _scanner.DeleteFoldersAsync(allPaths, progress, _cts.Token);

                // Remove only folders that were actually deleted (no longer exist)
                var toRemove = EmptyFolders.Where(f => !System.IO.Directory.Exists(f.Path)).ToList();
                foreach (var item in toRemove)
                    EmptyFolders.Remove(item);

                StatusMessage = $"Deleted {deleted} folder(s). {EmptyFolders.Count} remaining.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                StatusBarViewModel.Instance.CompleteOperation(StatusMessage);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void SelectAll()
        {
            foreach (var folder in EmptyFolders)
                folder.IsSelected = true;
        }

        private void SelectNone()
        {
            foreach (var folder in EmptyFolders)
                folder.IsSelected = false;
        }
    }
}

