using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class DiskCleanupViewModel : BindableBase
    {
        private readonly DiskCleanupService _diskCleanupService;
        private bool _windowsTempFiles = true;
        private bool _userTempFiles = true;
        private bool _prefetchFiles;
        private bool _recycleBin = true;
        private bool _downloadsOlderThan30Days;
        private bool _windowsUpdateCache;
        private bool _thumbnailCache = true;
        private bool _windowsErrorReports = true;
        private bool _oldLogFiles;
        private bool _dryRun = true;
        private string _statusMessage = "Ready. Click Analyze to scan.";
        private bool _isAnalyzing;
        private bool _isCleaning;
        private CleanupAnalysisResult? _lastAnalysisResult;
        private CancellationTokenSource? _cancellationTokenSource;

        public DiskCleanupViewModel() : this(new DiskCleanupService())
        {
        }

        public DiskCleanupViewModel(DiskCleanupService diskCleanupService)
        {
            _diskCleanupService = diskCleanupService;
            
            AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing && !IsCleaning);
            CleanNowCommand = new AsyncRelayCommand(CleanNowAsync, () => !IsAnalyzing && !IsCleaning && ItemsToClean.Any());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsAnalyzing || IsCleaning);
        }

        public ObservableCollection<CleanupItemViewModel> ItemsToClean { get; } = new();

        public bool WindowsTempFiles
        {
            get => _windowsTempFiles;
            set { _windowsTempFiles = value; RaisePropertyChanged(); }
        }

        public bool UserTempFiles
        {
            get => _userTempFiles;
            set { _userTempFiles = value; RaisePropertyChanged(); }
        }

        public bool PrefetchFiles
        {
            get => _prefetchFiles;
            set { _prefetchFiles = value; RaisePropertyChanged(); }
        }

        public bool RecycleBin
        {
            get => _recycleBin;
            set { _recycleBin = value; RaisePropertyChanged(); }
        }

        public bool DownloadsOlderThan30Days
        {
            get => _downloadsOlderThan30Days;
            set { _downloadsOlderThan30Days = value; RaisePropertyChanged(); }
        }

        public bool WindowsUpdateCache
        {
            get => _windowsUpdateCache;
            set { _windowsUpdateCache = value; RaisePropertyChanged(); }
        }

        public bool ThumbnailCache
        {
            get => _thumbnailCache;
            set { _thumbnailCache = value; RaisePropertyChanged(); }
        }

        public bool WindowsErrorReports
        {
            get => _windowsErrorReports;
            set { _windowsErrorReports = value; RaisePropertyChanged(); }
        }

        public bool OldLogFiles
        {
            get => _oldLogFiles;
            set { _oldLogFiles = value; RaisePropertyChanged(); }
        }

        public bool DryRun
        {
            get => _dryRun;
            set { _dryRun = value; RaisePropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; RaisePropertyChanged(); }
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            private set
            {
                _isAnalyzing = value;
                RaisePropertyChanged();
                ((AsyncRelayCommand)AnalyzeCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)CleanNowCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }

        public bool IsCleaning
        {
            get => _isCleaning;
            private set
            {
                _isCleaning = value;
                RaisePropertyChanged();
                ((AsyncRelayCommand)AnalyzeCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)CleanNowCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }

        public ICommand AnalyzeCommand { get; }
        public ICommand CleanNowCommand { get; }
        public ICommand CancelCommand { get; }

    private async Task AnalyzeAsync()
    {
        ItemsToClean.Clear();
        IsAnalyzing = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var categories = BuildSelectedCategories();

            var progress = new Progress<string>(message =>
            {
                StatusMessage = message;
            });

            _lastAnalysisResult = await _diskCleanupService.AnalyzeAsync(
                categories,
                progress,
                _cancellationTokenSource.Token);

            // Populate results
            foreach (var category in _lastAnalysisResult.Categories.Where(c => c.FileCount > 0))
            {
                ItemsToClean.Add(new CleanupItemViewModel
                {
                    Category = category.Category,
                    Files = category.FileCount,
                    Size = FormatBytes(category.TotalSize),
                    Path = category.Path
                });
            }

            StatusMessage = $"Analysis complete. Found {_lastAnalysisResult.TotalFiles} files ({FormatBytes(_lastAnalysisResult.TotalSize)})";
            ((AsyncRelayCommand)CleanNowCommand).RaiseCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Analysis cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task CleanNowAsync()
    {
        if (_lastAnalysisResult == null)
        {
            StatusMessage = "Please analyze first";
            return;
        }

        IsCleaning = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusMessage = message;
            });

            var result = await _diskCleanupService.CleanAsync(
                _lastAnalysisResult,
                DryRun,
                progress,
                _cancellationTokenSource.Token);

            var dryRunText = result.WasDryRun ? " (Dry Run)" : "";
            StatusMessage = $"Cleanup complete{dryRunText}. Deleted {result.FilesDeleted} files, freed {FormatBytes(result.SpaceFreed)}";

            if (result.Errors.Any())
            {
                StatusMessage += $" ({result.Errors.Count} errors)";
            }

            // Clear items after successful cleanup (unless dry run)
            if (!result.WasDryRun)
            {
                ItemsToClean.Clear();
                _lastAnalysisResult = null;
                ((AsyncRelayCommand)CleanNowCommand).RaiseCanExecuteChanged();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cleanup cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }

    private DiskCleanupCategories BuildSelectedCategories()
    {
        var categories = DiskCleanupCategories.None;

        if (WindowsTempFiles) categories |= DiskCleanupCategories.WindowsTempFiles;
        if (UserTempFiles) categories |= DiskCleanupCategories.UserTempFiles;
        if (PrefetchFiles) categories |= DiskCleanupCategories.PrefetchFiles;
        if (RecycleBin) categories |= DiskCleanupCategories.RecycleBin;
        if (DownloadsOlderThan30Days) categories |= DiskCleanupCategories.DownloadsOlderThan30Days;
        if (WindowsUpdateCache) categories |= DiskCleanupCategories.WindowsUpdateCache;
        if (ThumbnailCache) categories |= DiskCleanupCategories.ThumbnailCache;
        if (WindowsErrorReports) categories |= DiskCleanupCategories.WindowsErrorReports;
        if (OldLogFiles) categories |= DiskCleanupCategories.OldLogFiles;

        return categories;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
    }
}

public class CleanupItemViewModel
{
    public string Category { get; init; } = string.Empty;
    public int Files { get; init; }
    public string Size { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
