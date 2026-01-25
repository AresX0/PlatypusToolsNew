using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class MediaItemViewModel : BindableBase
    {
        private string _filePath = string.Empty;
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        private string _fileName = string.Empty;
        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

        private string _fileType = string.Empty;
        public string FileType { get => _fileType; set => SetProperty(ref _fileType, value); }

        private long _fileSize;
        public long FileSize { get => _fileSize; set => SetProperty(ref _fileSize, value); }

        private string _resolution = string.Empty;
        public string Resolution { get => _resolution; set => SetProperty(ref _resolution, value); }

        private string _duration = string.Empty;
        public string Duration { get => _duration; set => SetProperty(ref _duration, value); }

        private DateTime _dateModified;
        public DateTime DateModified { get => _dateModified; set => SetProperty(ref _dateModified, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        private string _sourcePath = string.Empty;
        public string SourcePath { get => _sourcePath; set => SetProperty(ref _sourcePath, value); }

        private string _hash = string.Empty;
        public string Hash { get => _hash; set => SetProperty(ref _hash, value); }
    }

    public class MediaDuplicateGroupViewModel : BindableBase
    {
        public string Hash { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public ObservableCollection<MediaItemViewModel> Files { get; } = new();
        public int DuplicateCount => Files.Count;
        public string FormattedSize => FormatBytes(FileSize);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    public class MediaLibraryViewModel : BindableBase
    {
        private readonly MediaLibraryService _mediaLibraryService;
        private CancellationTokenSource? _scanCancellationTokenSource;

        public MediaLibraryViewModel()
        {
            _mediaLibraryService = new MediaLibraryService();

            // Original commands
            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !IsScanning && !string.IsNullOrWhiteSpace(DirectoryPath));
            CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsScanning);
            BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
            FilterByTypeCommand = new RelayCommand(type => FilterByType(type?.ToString()));
            SortCommand = new RelayCommand(column => Sort(column?.ToString()));
            LoadMetadataCommand = new RelayCommand(async item => await LoadMetadataAsync(item as MediaItemViewModel));

            // New library management commands
            BrowseLibraryPathCommand = new RelayCommand(_ => BrowseLibraryPath());
            SetLibraryPathCommand = new RelayCommand(async _ => await SetLibraryPathAsync(), _ => !string.IsNullOrWhiteSpace(PrimaryLibraryPath));
            ScanForMediaCommand = new RelayCommand(async _ => await ScanForMediaAsync(), _ => !IsScanning && !string.IsNullOrWhiteSpace(ScanPath));
            BrowseScanPathCommand = new RelayCommand(_ => BrowseScanPath());
            CopyToLibraryCommand = new RelayCommand(async _ => await CopySelectedToLibraryAsync(), _ => !IsScanning && SelectedItemsCount > 0);
            SelectAllCommand = new RelayCommand(_ => SelectAll(true));
            DeselectAllCommand = new RelayCommand(_ => SelectAll(false));
            RefreshLibraryCommand = new RelayCommand(async _ => await RefreshLibraryAsync(), _ => !IsScanning && !string.IsNullOrWhiteSpace(PrimaryLibraryPath));
            FindDuplicatesCommand = new RelayCommand(async _ => await FindDuplicatesAsync(), _ => !IsScanning && MediaItems.Count > 0);

            // Load saved library path
            LoadSavedSettings();
        }

        public ObservableCollection<MediaItemViewModel> MediaItems { get; } = new();
        public ObservableCollection<MediaItemViewModel> ScannedItems { get; } = new();
        public ObservableCollection<MediaDuplicateGroupViewModel> DuplicateGroups { get; } = new();

        #region Original Properties

        private string _directoryPath = string.Empty;
        public string DirectoryPath { get => _directoryPath; set { SetProperty(ref _directoryPath, value); ((RelayCommand)ScanCommand).RaiseCanExecuteChanged(); } }

        private string _selectedType = "All";
        public string SelectedType { get => _selectedType; set => SetProperty(ref _selectedType, value); }

        private bool _isScanning;
        public bool IsScanning 
        { 
            get => _isScanning; 
            set 
            { 
                SetProperty(ref _isScanning, value); 
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CopyToLibraryCommand).RaiseCanExecuteChanged();
            } 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _totalFiles;
        public int TotalFiles { get => _totalFiles; set => SetProperty(ref _totalFiles, value); }

        #endregion

        #region New Library Properties

        private string _primaryLibraryPath = string.Empty;
        public string PrimaryLibraryPath
        {
            get => _primaryLibraryPath;
            set
            {
                if (SetProperty(ref _primaryLibraryPath, value))
                {
                    ((RelayCommand)SetLibraryPathCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)RefreshLibraryCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private string _scanPath = string.Empty;
        public string ScanPath
        {
            get => _scanPath;
            set
            {
                if (SetProperty(ref _scanPath, value))
                {
                    ((RelayCommand)ScanForMediaCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private int _scannedFilesCount;
        public int ScannedFilesCount { get => _scannedFilesCount; set => SetProperty(ref _scannedFilesCount, value); }

        private int _selectedItemsCount;
        public int SelectedItemsCount
        {
            get => _selectedItemsCount;
            set
            {
                if (SetProperty(ref _selectedItemsCount, value))
                {
                    ((RelayCommand)CopyToLibraryCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private double _progress;
        public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private bool _organizeByType = true;
        public bool OrganizeByType { get => _organizeByType; set => SetProperty(ref _organizeByType, value); }

        private string _libraryInfo = string.Empty;
        public string LibraryInfo { get => _libraryInfo; set => SetProperty(ref _libraryInfo, value); }

        #endregion

        #region Commands

        public ICommand ScanCommand { get; }
        public ICommand CancelScanCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public ICommand FilterByTypeCommand { get; }
        public ICommand SortCommand { get; }
        public ICommand LoadMetadataCommand { get; }

        // New commands
        public ICommand BrowseLibraryPathCommand { get; }
        public ICommand SetLibraryPathCommand { get; }
        public ICommand ScanForMediaCommand { get; }
        public ICommand BrowseScanPathCommand { get; }
        public ICommand CopyToLibraryCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }
        public ICommand RefreshLibraryCommand { get; }
        public ICommand FindDuplicatesCommand { get; }

        #endregion

        #region Original Methods

        private async Task ScanAsync()
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath) || !Directory.Exists(DirectoryPath))
            {
                StatusMessage = "Invalid directory path";
                return;
            }

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _scanCancellationTokenSource.Token;

            IsScanning = true;
            StatusMessage = "Scanning media files...";
            MediaItems.Clear();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                // Use new batch-processing scan with progress reporting
                var files = await _mediaLibraryService.ScanDirectoryAsync(
                    DirectoryPath,
                    includeSubdirectories: true,
                    onProgress: (current, total, message) =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = message;
                            TotalFiles = current;
                        });
                    },
                    onBatchProcessed: (batchItems) =>
                    {
                        // Add items to UI in real-time batches
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            foreach (var file in batchItems)
                            {
                                MediaItems.Add(new MediaItemViewModel
                                {
                                    FilePath = file.FilePath,
                                    FileName = Path.GetFileName(file.FilePath),
                                    FileType = Path.GetExtension(file.FilePath).TrimStart('.').ToUpper(),
                                    FileSize = file.Size,
                                    DateModified = file.DateModified
                                });
                            }
                            TotalFiles = MediaItems.Count;
                        });
                    },
                    cancellationToken: cancellationToken);
                
                if (cancellationToken.IsCancellationRequested)
                {
                    StatusMessage = "Scan canceled";
                    return;
                }

                TotalFiles = MediaItems.Count;
                StatusMessage = $"Found {TotalFiles} media files";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan canceled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
            }
        }

        private void CancelScan()
        {
            _scanCancellationTokenSource?.Cancel();
            StatusMessage = "Canceling...";
        }

        private async Task RefreshAsync()
        {
            await ScanAsync();
        }

        private void BrowseDirectory()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select media library directory";
            dialog.UseDescriptionForTitle = true;
            if (!string.IsNullOrWhiteSpace(DirectoryPath) && Directory.Exists(DirectoryPath))
            {
                dialog.SelectedPath = DirectoryPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DirectoryPath = dialog.SelectedPath;
            }
        }

        private void FilterByType(string? type)
        {
            SelectedType = type ?? "All";
        }

        private void Sort(string? column)
        {
        }

        private async Task LoadMetadataAsync(MediaItemViewModel? item)
        {
            if (item == null) return;

            try
            {
                var metadata = await _mediaLibraryService.GetMetadataAsync(item.FilePath);
                if (metadata != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        item.Resolution = $"{metadata.Width}x{metadata.Height}";
                        item.Duration = metadata.Duration.ToString(@"hh\:mm\:ss");
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading metadata: {ex.Message}";
            }
        }

        #endregion

        #region New Library Methods

        private void LoadSavedSettings()
        {
            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools",
                    "media_library_settings.json");

                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<MediaLibrarySettings>(json);
                    if (settings != null)
                    {
                        PrimaryLibraryPath = settings.PrimaryLibraryPath ?? string.Empty;
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var settingsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools");

                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                var settingsPath = Path.Combine(settingsDir, "media_library_settings.json");
                var settings = new MediaLibrarySettings { PrimaryLibraryPath = PrimaryLibraryPath };
                var json = System.Text.Json.JsonSerializer.Serialize(settings);
                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        private void BrowseLibraryPath()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Primary Media Library Location";
            dialog.UseDescriptionForTitle = true;

            if (!string.IsNullOrWhiteSpace(PrimaryLibraryPath) && Directory.Exists(PrimaryLibraryPath))
            {
                dialog.SelectedPath = PrimaryLibraryPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                PrimaryLibraryPath = dialog.SelectedPath;
            }
        }

        private async Task SetLibraryPathAsync()
        {
            if (string.IsNullOrWhiteSpace(PrimaryLibraryPath))
                return;

            try
            {
                await _mediaLibraryService.SetPrimaryLibraryPathAsync(PrimaryLibraryPath);
                SaveSettings();
                StatusMessage = $"Library path set to: {PrimaryLibraryPath}";
                await RefreshLibraryAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error setting library path: {ex.Message}";
            }
        }

        private void BrowseScanPath()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Folder to Scan for Media";
            dialog.UseDescriptionForTitle = true;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ScanPath = dialog.SelectedPath;
            }
        }

        private async Task ScanForMediaAsync()
        {
            if (string.IsNullOrWhiteSpace(ScanPath) || !Directory.Exists(ScanPath))
            {
                StatusMessage = "Invalid scan path";
                return;
            }

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();
            var ct = _scanCancellationTokenSource.Token;

            IsScanning = true;
            StatusMessage = "Scanning for media files...";
            ScannedItems.Clear();
            Progress = 0;

            var progress = new Progress<MediaScanProgress>(p =>
            {
                Progress = p.PercentComplete;
                StatusMessage = p.StatusMessage;
            });

            try
            {
                var items = await _mediaLibraryService.ScanFolderForMediaAsync(ScanPath, progress, ct);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in items)
                    {
                        ScannedItems.Add(new MediaItemViewModel
                        {
                            FilePath = item.FilePath,
                            FileName = item.FileName,
                            FileType = Path.GetExtension(item.FilePath).TrimStart('.').ToUpper(),
                            FileSize = item.Size,
                            DateModified = item.DateModified,
                            SourcePath = item.FilePath,
                            IsSelected = true
                        });
                    }
                });

                ScannedFilesCount = ScannedItems.Count;
                SelectedItemsCount = ScannedItems.Count(i => i.IsSelected);
                StatusMessage = $"Found {ScannedFilesCount} media files ready for import";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan canceled";
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

        private void SelectAll(bool select)
        {
            foreach (var item in ScannedItems)
            {
                item.IsSelected = select;
            }
            SelectedItemsCount = ScannedItems.Count(i => i.IsSelected);
        }

        private async Task CopySelectedToLibraryAsync()
        {
            if (string.IsNullOrWhiteSpace(PrimaryLibraryPath))
            {
                MessageBox.Show("Please set a primary library path first.", "Library Path Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedItems = ScannedItems.Where(i => i.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                StatusMessage = "No items selected";
                return;
            }

            var result = MessageBox.Show(
                $"Copy {selectedItems.Count} files to library?\n\n" +
                $"Destination: {PrimaryLibraryPath}\n" +
                $"Organize by type: {(OrganizeByType ? "Yes" : "No")}",
                "Confirm Copy",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();
            var ct = _scanCancellationTokenSource.Token;

            IsScanning = true;
            Progress = 0;

            var progress = new Progress<MediaScanProgress>(p =>
            {
                Progress = p.PercentComplete;
                StatusMessage = p.StatusMessage;
            });

            try
            {
                var mediaItems = selectedItems.Select(i => new MediaItem
                {
                    FilePath = i.FilePath,
                    FileName = i.FileName,
                    Size = i.FileSize,
                    DateModified = i.DateModified,
                    Type = MediaLibraryService.GetMediaType(Path.GetExtension(i.FilePath))
                });

                var copied = await _mediaLibraryService.CopyToLibraryAsync(
                    mediaItems,
                    PrimaryLibraryPath,
                    OrganizeByType,
                    progress,
                    ct);

                StatusMessage = $"Successfully copied {copied} files to library";
                
                // Clear copied items from scanned list
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in selectedItems)
                    {
                        ScannedItems.Remove(item);
                    }
                });

                ScannedFilesCount = ScannedItems.Count;
                SelectedItemsCount = ScannedItems.Count(i => i.IsSelected);

                // Refresh library view
                await RefreshLibraryAsync();
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Copy canceled";
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

        private async Task RefreshLibraryAsync()
        {
            if (string.IsNullOrWhiteSpace(PrimaryLibraryPath))
                return;

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();
            var ct = _scanCancellationTokenSource.Token;

            IsScanning = true;
            StatusMessage = "Refreshing library...";
            Progress = 0;

            var progress = new Progress<MediaScanProgress>(p =>
            {
                Progress = p.PercentComplete;
                StatusMessage = p.StatusMessage;
            });

            try
            {
                await _mediaLibraryService.LoadConfigAsync(PrimaryLibraryPath);
                await _mediaLibraryService.RefreshLibraryAsync(progress, ct);

                var entries = _mediaLibraryService.GetLibraryEntries();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MediaItems.Clear();
                    foreach (var entry in entries)
                    {
                        MediaItems.Add(new MediaItemViewModel
                        {
                            FilePath = entry.FilePath,
                            FileName = entry.FileName,
                            FileType = Path.GetExtension(entry.FilePath).TrimStart('.').ToUpper(),
                            FileSize = entry.Size,
                            DateModified = entry.DateModified
                        });
                    }
                });

                TotalFiles = MediaItems.Count;
                
                long totalSize = entries.Sum(e => e.Size);
                LibraryInfo = $"{TotalFiles} files • {FormatBytes(totalSize)}";
                StatusMessage = $"Library contains {TotalFiles} media files";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Refresh canceled";
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

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private async Task FindDuplicatesAsync()
        {
            if (MediaItems.Count == 0)
            {
                StatusMessage = "No media files to scan for duplicates";
                return;
            }

            IsScanning = true;
            StatusMessage = "Scanning for duplicates by MD5 hash...";
            DuplicateGroups.Clear();

            try
            {
                // Convert ViewModels to MediaItem for the service
                var mediaItems = MediaItems.Select(m => new MediaItem
                {
                    FilePath = m.FilePath,
                    FileName = m.FileName,
                    Size = m.FileSize,
                    DateModified = m.DateModified
                }).ToList();
                
                var duplicates = await _mediaLibraryService.FindDuplicatesAsync(
                    mediaItems,
                    new Progress<MediaScanProgress>(p =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            Progress = p.PercentComplete;
                            StatusMessage = !string.IsNullOrEmpty(p.StatusMessage) 
                                ? p.StatusMessage 
                                : $"Hashing file {p.FilesScanned} of {p.TotalFiles}...";
                        });
                    }),
                    _scanCancellationTokenSource?.Token ?? CancellationToken.None);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    DuplicateGroups.Clear();
                    int totalDuplicates = 0;

                    foreach (var group in duplicates)
                    {
                        var vmGroup = new MediaDuplicateGroupViewModel
                        {
                            Hash = group.Hash,
                            FileSize = group.Items.FirstOrDefault()?.Size ?? 0
                        };

                        foreach (var file in group.Items)
                        {
                            vmGroup.Files.Add(new MediaItemViewModel
                            {
                                FilePath = file.FilePath,
                                FileName = Path.GetFileName(file.FilePath),
                                FileType = Path.GetExtension(file.FilePath).TrimStart('.').ToUpper(),
                                FileSize = file.Size,
                                DateModified = file.DateModified,
                                Hash = group.Hash
                            });
                        }

                        if (vmGroup.Files.Count > 1)
                        {
                            DuplicateGroups.Add(vmGroup);
                            totalDuplicates += vmGroup.Files.Count - 1;
                        }
                    }

                    StatusMessage = DuplicateGroups.Count > 0 
                        ? $"Found {DuplicateGroups.Count} duplicate groups ({totalDuplicates} duplicate files)"
                        : "No duplicates found";
                });
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Duplicate scan canceled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning duplicates: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                Progress = 0;
            }
        }

        #endregion
    }

    internal class MediaLibrarySettings
    {
        public string? PrimaryLibraryPath { get; set; }
    }
}
