using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    }

    public class MediaLibraryViewModel : BindableBase
    {
        private readonly MediaLibraryService _mediaLibraryService;

        public MediaLibraryViewModel()
        {
            _mediaLibraryService = new MediaLibraryService();

            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !IsScanning && !string.IsNullOrWhiteSpace(DirectoryPath));
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsScanning);
            BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
            FilterByTypeCommand = new RelayCommand(type => FilterByType(type?.ToString()));
            SortCommand = new RelayCommand(column => Sort(column?.ToString()));
            LoadMetadataCommand = new RelayCommand(async item => await LoadMetadataAsync(item as MediaItemViewModel));
        }

        public ObservableCollection<MediaItemViewModel> MediaItems { get; } = new();

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
            } 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _totalFiles;
        public int TotalFiles { get => _totalFiles; set => SetProperty(ref _totalFiles, value); }

        public ICommand ScanCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public ICommand FilterByTypeCommand { get; }
        public ICommand SortCommand { get; }
        public ICommand LoadMetadataCommand { get; }

        private async Task ScanAsync()
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath) || !Directory.Exists(DirectoryPath))
            {
                StatusMessage = "Invalid directory path";
                return;
            }

            IsScanning = true;
            StatusMessage = "Scanning media files...";
            MediaItems.Clear();

            try
            {
                var files = await _mediaLibraryService.ScanDirectoryAsync(DirectoryPath);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var file in files)
                    {
                        var item = new MediaItemViewModel
                        {
                            FilePath = file.FilePath,
                            FileName = Path.GetFileName(file.FilePath),
                            FileType = Path.GetExtension(file.FilePath).TrimStart('.').ToUpper(),
                            FileSize = new FileInfo(file.FilePath).Length,
                            DateModified = File.GetLastWriteTime(file.FilePath)
                        };
                        MediaItems.Add(item);
                    }
                });

                TotalFiles = MediaItems.Count;
                StatusMessage = $"Found {TotalFiles} media files";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
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
            // Filtering logic can be implemented with CollectionView
        }

        private void Sort(string? column)
        {
            // Sorting logic can be implemented with CollectionView
        }

        private async Task LoadMetadataAsync(MediaItemViewModel? item)
        {
            if (item == null) return;

            try
            {
                var metadata = await _mediaLibraryService.GetMetadataAsync(item.FilePath);
                if (metadata != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
    }
}
