using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class WebsiteDownloaderViewModel : BindableBase
    {
        private readonly WebsiteDownloaderService _service;
        private CancellationTokenSource? _cancellationTokenSource;

        private string _url = string.Empty;
        private string _outputDirectory = string.Empty;
        private bool _downloadImages = true;
        private bool _downloadVideos = true;
        private bool _downloadDocuments = true;
        private bool _recursiveCrawl = false;
        private int _maxDepth = 1;
        private string? _urlPattern;
        private bool _isScanning = false;
        private bool _isDownloading = false;
        private int _progress = 0;
        private string _statusMessage = string.Empty;

        public WebsiteDownloaderViewModel()
        {
            _service = new WebsiteDownloaderService();
            DownloadItems = new ObservableCollection<DownloadItemViewModel>();

            ScanCommand = new RelayCommand(async _ => await ScanUrlAsync(), _ => !IsScanning && !string.IsNullOrWhiteSpace(Url));
            StartDownloadCommand = new RelayCommand(async _ => await StartDownloadAsync(), _ => !IsDownloading && DownloadItems.Any(i => i.IsSelected));
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsScanning || IsDownloading);
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            ClearCommand = new RelayCommand(_ => Clear());
        }

        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set => SetProperty(ref _outputDirectory, value);
        }

        public bool DownloadImages
        {
            get => _downloadImages;
            set => SetProperty(ref _downloadImages, value);
        }

        public bool DownloadVideos
        {
            get => _downloadVideos;
            set => SetProperty(ref _downloadVideos, value);
        }

        public bool DownloadDocuments
        {
            get => _downloadDocuments;
            set => SetProperty(ref _downloadDocuments, value);
        }

        public bool RecursiveCrawl
        {
            get => _recursiveCrawl;
            set => SetProperty(ref _recursiveCrawl, value);
        }

        public int MaxDepth
        {
            get => _maxDepth;
            set => SetProperty(ref _maxDepth, value);
        }

        public string? UrlPattern
        {
            get => _urlPattern;
            set => SetProperty(ref _urlPattern, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<DownloadItemViewModel> DownloadItems { get; }

        public ICommand ScanCommand { get; }
        public ICommand StartDownloadCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand ClearCommand { get; }

        private async Task ScanUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(Url))
                return;

            IsScanning = true;
            DownloadItems.Clear();
            StatusMessage = "Scanning website...";
            Progress = 0;

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var options = new DownloadOptions
                {
                    DownloadImages = DownloadImages,
                    DownloadVideos = DownloadVideos,
                    DownloadDocuments = DownloadDocuments,
                    RecursiveCrawl = RecursiveCrawl,
                    MaxDepth = MaxDepth,
                    UrlPattern = UrlPattern
                };

                var items = await _service.ScanUrlAsync(Url, options, _cancellationTokenSource.Token);

                foreach (var item in items.DistinctBy(i => i.Url))
                {
                    DownloadItems.Add(new DownloadItemViewModel(item));
                }

                StatusMessage = $"Found {DownloadItems.Count} items";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task StartDownloadAsync()
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                StatusMessage = "Please select an output directory";
                return;
            }

            if (!Directory.Exists(OutputDirectory))
            {
                try
                {
                    Directory.CreateDirectory(OutputDirectory);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error creating directory: {ex.Message}";
                    return;
                }
            }

            IsDownloading = true;
            Progress = 0;
            _cancellationTokenSource = new CancellationTokenSource();

            var selectedItems = DownloadItems.Where(i => i.IsSelected).ToList();
            var completed = 0;
            var total = selectedItems.Count;

            try
            {
                using var semaphore = new SemaphoreSlim(3); // Max 3 concurrent downloads

                var tasks = selectedItems.Select(async item =>
                {
                    await semaphore.WaitAsync(_cancellationTokenSource.Token);
                    try
                    {
                        item.Status = "Downloading";
                        var progress = new Progress<DownloadProgress>(p =>
                        {
                            item.Progress = p.Percentage;
                        });

                        var result = await _service.DownloadFileAsync(item.Item, OutputDirectory, progress, _cancellationTokenSource.Token);

                        if (result.Success)
                        {
                            item.Status = "Completed";
                            item.Progress = 100;
                        }
                        else
                        {
                            item.Status = "Failed";
                            item.ErrorMessage = result.ErrorMessage;
                        }

                        Interlocked.Increment(ref completed);
                        Progress = (completed * 100) / total;
                        StatusMessage = $"Downloaded {completed} of {total} files";
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
                StatusMessage = $"Download completed: {completed} of {total} files";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
        }

        private void SelectAll()
        {
            foreach (var item in DownloadItems)
                item.IsSelected = true;
        }

        private void SelectNone()
        {
            foreach (var item in DownloadItems)
                item.IsSelected = false;
        }

        private void BrowseOutput()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select Output Directory",
                FileName = "Folder Selection",
                Filter = "Folders|*.none",
                CheckFileExists = false,
                CheckPathExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                OutputDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }

        private void Clear()
        {
            DownloadItems.Clear();
            StatusMessage = string.Empty;
            Progress = 0;
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StartDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public class DownloadItemViewModel : BindableBase
    {
        private bool _isSelected = true;
        private string _status;
        private int _progress;
        private string? _errorMessage;

        public DownloadItemViewModel(DownloadItem item)
        {
            Item = item;
            _status = item.Status;
        }

        public DownloadItem Item { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string Url => Item.Url;
        public string FileName => Item.FileName;
        public string Type => Item.Type;

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }
    }
}
