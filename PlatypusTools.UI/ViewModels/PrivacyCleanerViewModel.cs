using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels;

public class PrivacyCleanerViewModel : BindableBase
{
    private readonly PrivacyCleanerService _privacyCleanerService;
    
    // Browsers
    private bool _browserChrome = true;
    private bool _browserEdge = true;
    private bool _browserFirefox = true;
    private bool _browserBrave;
    
    // Cloud
    private bool _cloudOneDrive;
    private bool _cloudGoogle;
    private bool _cloudDropbox;
    private bool _cloudiCloud;
    
    // Windows
    private bool _windowsRecentDocs = true;
    private bool _windowsJumpLists = true;
    private bool _windowsExplorerHistory = true;
    private bool _windowsClipboard;
    
    // Applications
    private bool _applicationOffice;
    private bool _applicationAdobe;
    private bool _applicationMediaPlayers;
    
    private bool _dryRun = true;
    private string _statusMessage = "⚠️ WARNING: This will delete browsing history, cookies, and cached data. Click Analyze to scan.";
    private bool _isAnalyzing;
    private bool _isCleaning;
    private PrivacyAnalysisResult? _lastAnalysisResult;
    private CancellationTokenSource? _cancellationTokenSource;

    public PrivacyCleanerViewModel() : this(new PrivacyCleanerService())
    {
    }

    public PrivacyCleanerViewModel(PrivacyCleanerService privacyCleanerService)
    {
        _privacyCleanerService = privacyCleanerService;
        
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing && !IsCleaning);
        CleanNowCommand = new AsyncRelayCommand(CleanNowAsync, () => !IsAnalyzing && !IsCleaning && ItemsToClean.Any());
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsAnalyzing || IsCleaning);
    }

    public ObservableCollection<PrivacyItemViewModel> ItemsToClean { get; } = new();

    // Browsers
    public bool BrowserChrome { get => _browserChrome; set { _browserChrome = value; RaisePropertyChanged(); } }
    public bool BrowserEdge { get => _browserEdge; set { _browserEdge = value; RaisePropertyChanged(); } }
    public bool BrowserFirefox { get => _browserFirefox; set { _browserFirefox = value; RaisePropertyChanged(); } }
    public bool BrowserBrave { get => _browserBrave; set { _browserBrave = value; RaisePropertyChanged(); } }
    
    // Cloud
    public bool CloudOneDrive { get => _cloudOneDrive; set { _cloudOneDrive = value; RaisePropertyChanged(); } }
    public bool CloudGoogle { get => _cloudGoogle; set { _cloudGoogle = value; RaisePropertyChanged(); } }
    public bool CloudDropbox { get => _cloudDropbox; set { _cloudDropbox = value; RaisePropertyChanged(); } }
    public bool CloudiCloud { get => _cloudiCloud; set { _cloudiCloud = value; RaisePropertyChanged(); } }
    
    // Windows
    public bool WindowsRecentDocs { get => _windowsRecentDocs; set { _windowsRecentDocs = value; RaisePropertyChanged(); } }
    public bool WindowsJumpLists { get => _windowsJumpLists; set { _windowsJumpLists = value; RaisePropertyChanged(); } }
    public bool WindowsExplorerHistory { get => _windowsExplorerHistory; set { _windowsExplorerHistory = value; RaisePropertyChanged(); } }
    public bool WindowsClipboard { get => _windowsClipboard; set { _windowsClipboard = value; RaisePropertyChanged(); } }
    
    // Applications
    public bool ApplicationOffice { get => _applicationOffice; set { _applicationOffice = value; RaisePropertyChanged(); } }
    public bool ApplicationAdobe { get => _applicationAdobe; set { _applicationAdobe = value; RaisePropertyChanged(); } }
    public bool ApplicationMediaPlayers { get => _applicationMediaPlayers; set { _applicationMediaPlayers = value; RaisePropertyChanged(); } }

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

            _lastAnalysisResult = await _privacyCleanerService.AnalyzeAsync(
                categories,
                progress,
                _cancellationTokenSource.Token);

            // Populate results
            foreach (var category in _lastAnalysisResult.Categories.Where(c => c.ItemCount > 0))
            {
                ItemsToClean.Add(new PrivacyItemViewModel
                {
                    Category = category.Category,
                    Items = category.ItemCount,
                    Size = FormatBytes(category.TotalSize)
                });
            }

            StatusMessage = $"Analysis complete. Found {_lastAnalysisResult.TotalItems} items ({FormatBytes(_lastAnalysisResult.TotalSize)})";
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

            var result = await _privacyCleanerService.CleanAsync(
                _lastAnalysisResult,
                DryRun,
                progress,
                _cancellationTokenSource.Token);

            var dryRunText = result.WasDryRun ? " (Dry Run)" : "";
            StatusMessage = $"Cleanup complete{dryRunText}. Deleted {result.ItemsDeleted} items, freed {FormatBytes(result.SpaceFreed)}";

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

    private PrivacyCategories BuildSelectedCategories()
    {
        var categories = PrivacyCategories.None;

        // Browsers
        if (BrowserChrome) categories |= PrivacyCategories.BrowserChrome;
        if (BrowserEdge) categories |= PrivacyCategories.BrowserEdge;
        if (BrowserFirefox) categories |= PrivacyCategories.BrowserFirefox;
        if (BrowserBrave) categories |= PrivacyCategories.BrowserBrave;
        
        // Cloud
        if (CloudOneDrive) categories |= PrivacyCategories.CloudOneDrive;
        if (CloudGoogle) categories |= PrivacyCategories.CloudGoogle;
        if (CloudDropbox) categories |= PrivacyCategories.CloudDropbox;
        if (CloudiCloud) categories |= PrivacyCategories.CloudiCloud;
        
        // Windows
        if (WindowsRecentDocs) categories |= PrivacyCategories.WindowsRecentDocs;
        if (WindowsJumpLists) categories |= PrivacyCategories.WindowsJumpLists;
        if (WindowsExplorerHistory) categories |= PrivacyCategories.WindowsExplorerHistory;
        if (WindowsClipboard) categories |= PrivacyCategories.WindowsClipboard;
        
        // Applications
        if (ApplicationOffice) categories |= PrivacyCategories.ApplicationOffice;
        if (ApplicationAdobe) categories |= PrivacyCategories.ApplicationAdobe;
        if (ApplicationMediaPlayers) categories |= PrivacyCategories.ApplicationMediaPlayers;

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

public class PrivacyItemViewModel
{
    public string Category { get; init; } = string.Empty;
    public int Items { get; init; }
    public string Size { get; init; } = string.Empty;
}
