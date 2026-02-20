using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels;

public class PrivacyCleanerViewModel : BindableBase
{
    private readonly PrivacyCleanerService _privacyCleanerService;
    private DispatcherTimer? _exclusionTimer;
    
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
        SelectAllItemsCommand = new RelayCommand(_ => SelectAllItems());
        SelectNoneItemsCommand = new RelayCommand(_ => SelectNoneItems());

        // Excluded folders commands
        AddExcludedFolderCommand = new RelayCommand(_ => AddExcludedFolder());
        RemoveExcludedFolderCommand = new RelayCommand(_ => RemoveExcludedFolder(), _ => SelectedExcludedFolder != null);
        CleanExcludedNowCommand = new RelayCommand(_ => CleanExcludedFoldersNow());
        ToggleExclusionTimerCommand = new RelayCommand(_ => ToggleExclusionTimer());

        // Load excluded folders from settings
        ExcludedFolders = new ObservableCollection<string>();
        LoadExcludedFolders();
        InitializeExclusionTimer();
    }

    private void SelectAllItems()
    {
        foreach (var category in ItemsToClean)
        {
            category.IsSelected = true;
            foreach (var child in category.Children)
                child.IsSelected = true;
        }
    }

    private void SelectNoneItems()
    {
        foreach (var category in ItemsToClean)
        {
            category.IsSelected = false;
            foreach (var child in category.Children)
                child.IsSelected = false;
        }
    }

    public ObservableCollection<PrivacyCategoryViewModel> ItemsToClean { get; } = new();

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
    public ICommand SelectAllItemsCommand { get; }
    public ICommand SelectNoneItemsCommand { get; }

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

            // Populate results with hierarchical structure (categories with children)
            foreach (var category in _lastAnalysisResult.Categories.Where(c => c.ItemCount > 0))
            {
                var categoryVm = new PrivacyCategoryViewModel
                {
                    CategoryName = category.Category,
                    TotalItems = category.ItemCount,
                    TotalSize = FormatBytes(category.TotalSize),
                    TotalSizeBytes = category.TotalSize
                };
                
                // Add individual file items as children
                foreach (var item in category.Items)
                {
                    var fileName = Path.GetFileName(item.Path);
                    if (string.IsNullOrEmpty(fileName))
                        fileName = item.Path;
                        
                    categoryVm.Children.Add(new PrivacyFileViewModel
                    {
                        FileName = fileName,
                        FilePath = item.Path,
                        Size = FormatBytes(item.Size),
                        SizeBytes = item.Size,
                        IsFile = item.IsFile,
                        Parent = categoryVm
                    });
                }
                
                ItemsToClean.Add(categoryVm);
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

        // Build a filtered result containing only selected items
        var selectedResult = BuildSelectedItemsResult();
        if (selectedResult.TotalItems == 0)
        {
            StatusMessage = "No items selected for cleaning";
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
                selectedResult,
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

    private PrivacyAnalysisResult BuildSelectedItemsResult()
    {
        var categories = new System.Collections.Generic.List<PrivacyCategoryResult>();
        
        foreach (var categoryVm in ItemsToClean)
        {
            // Get selected children (or all if category is selected and no children deselected)
            var selectedChildren = categoryVm.Children.Where(c => c.IsSelected).ToList();
            
            if (selectedChildren.Any())
            {
                var categoryResult = new PrivacyCategoryResult
                {
                    Category = categoryVm.CategoryName,
                    ItemCount = selectedChildren.Count,
                    TotalSize = selectedChildren.Sum(c => c.SizeBytes)
                };
                
                foreach (var child in selectedChildren)
                {
                    categoryResult.Items.Add(new PrivacyItem
                    {
                        Path = child.FilePath,
                        Size = child.SizeBytes,
                        IsFile = child.IsFile
                    });
                }
                
                categories.Add(categoryResult);
            }
        }
        
        return new PrivacyAnalysisResult
        {
            Categories = categories,
            TotalItems = categories.Sum(c => c.ItemCount),
            TotalSize = categories.Sum(c => c.TotalSize)
        };
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

    #region Recent Files Exclusion

    public ObservableCollection<string> ExcludedFolders { get; }

    private string? _selectedExcludedFolder;
    public string? SelectedExcludedFolder
    {
        get => _selectedExcludedFolder;
        set { _selectedExcludedFolder = value; RaisePropertyChanged(); }
    }

    private bool _exclusionTimerRunning;
    public bool ExclusionTimerRunning
    {
        get => _exclusionTimerRunning;
        set { _exclusionTimerRunning = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ExclusionTimerStatus)); }
    }

    public string ExclusionTimerStatus => ExclusionTimerRunning
        ? $"Auto-clean ON (every {Services.SettingsManager.Current.RecentExclusionIntervalMinutes} min)"
        : "Auto-clean OFF";

    private string _exclusionLog = "";
    public string ExclusionLog
    {
        get => _exclusionLog;
        set { _exclusionLog = value; RaisePropertyChanged(); }
    }

    public ICommand AddExcludedFolderCommand { get; }
    public ICommand RemoveExcludedFolderCommand { get; }
    public ICommand CleanExcludedNowCommand { get; }
    public ICommand ToggleExclusionTimerCommand { get; }

    private void LoadExcludedFolders()
    {
        ExcludedFolders.Clear();
        var settings = Services.SettingsManager.Current;
        if (settings.RecentExcludedFolders != null)
        {
            foreach (var folder in settings.RecentExcludedFolders)
                ExcludedFolders.Add(folder);
        }
    }

    private void SaveExcludedFolders()
    {
        var settings = Services.SettingsManager.Current;
        settings.RecentExcludedFolders = ExcludedFolders.ToList();
        Services.SettingsManager.Save(settings);
    }

    private void AddExcludedFolder()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder to exclude from Windows Recent list",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var path = dlg.SelectedPath;
            if (!ExcludedFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                ExcludedFolders.Add(path);
                SaveExcludedFolders();
                AddExclusionLogEntry($"Added: {path}");
                // Immediately clean entries for this folder
                CleanRecentForFolders(new[] { path });
            }
        }
    }

    private void RemoveExcludedFolder()
    {
        if (SelectedExcludedFolder != null)
        {
            var removed = SelectedExcludedFolder;
            ExcludedFolders.Remove(SelectedExcludedFolder);
            SaveExcludedFolders();
            AddExclusionLogEntry($"Removed: {removed}");
        }
    }

    private void CleanExcludedFoldersNow()
    {
        if (ExcludedFolders.Count == 0)
        {
            AddExclusionLogEntry("No excluded folders configured");
            return;
        }
        CleanRecentForFolders(ExcludedFolders);
    }

    private void CleanRecentForFolders(System.Collections.Generic.IEnumerable<string> folders)
    {
        try
        {
            var results = RecentCleaner.RemoveRecentShortcuts(folders, dryRun: false, includeSubDirs: true);
            if (results.Count > 0)
            {
                AddExclusionLogEntry($"Removed {results.Count} recent entries:");
                foreach (var r in results.Take(20))
                {
                    AddExclusionLogEntry($"  Removed: {Path.GetFileName(r.Path)} → {r.Target}");
                }
                if (results.Count > 20)
                    AddExclusionLogEntry($"  ... and {results.Count - 20} more");
            }
            else
            {
                AddExclusionLogEntry("No matching recent entries found");
            }
        }
        catch (Exception ex)
        {
            AddExclusionLogEntry($"Error: {ex.Message}");
        }
    }

    private void InitializeExclusionTimer()
    {
        var settings = Services.SettingsManager.Current;
        if (settings.RecentExclusionEnabled && ExcludedFolders.Count > 0)
        {
            StartExclusionTimer();
        }
    }

    private void ToggleExclusionTimer()
    {
        if (ExclusionTimerRunning)
        {
            StopExclusionTimer();
        }
        else
        {
            if (ExcludedFolders.Count == 0)
            {
                AddExclusionLogEntry("Add at least one folder before enabling auto-clean");
                return;
            }
            StartExclusionTimer();
        }
    }

    private void StartExclusionTimer()
    {
        var settings = Services.SettingsManager.Current;
        var interval = TimeSpan.FromMinutes(settings.RecentExclusionIntervalMinutes);

        _exclusionTimer?.Stop();
        _exclusionTimer = new DispatcherTimer { Interval = interval };
        _exclusionTimer.Tick += (_, _) => CleanExcludedFoldersNow();
        _exclusionTimer.Start();

        ExclusionTimerRunning = true;
        settings.RecentExclusionEnabled = true;
        Services.SettingsManager.Save(settings);
        AddExclusionLogEntry($"Auto-clean started (every {settings.RecentExclusionIntervalMinutes} min)");

        // Run immediately on start
        CleanExcludedFoldersNow();
    }

    private void StopExclusionTimer()
    {
        _exclusionTimer?.Stop();
        _exclusionTimer = null;
        ExclusionTimerRunning = false;

        var settings = Services.SettingsManager.Current;
        settings.RecentExclusionEnabled = false;
        Services.SettingsManager.Save(settings);
        AddExclusionLogEntry("Auto-clean stopped");
    }

    private void AddExclusionLogEntry(string message)
    {
        ExclusionLog = $"[{DateTime.Now:HH:mm:ss}] {message}\n{ExclusionLog}";
    }

    #endregion
}

/// <summary>
/// Represents a category of privacy items (e.g., "Chrome Browser Data")
/// </summary>
public class PrivacyCategoryViewModel : BindableBase
{
    private bool _isSelected = true;
    private bool _isExpanded;
    
    public string CategoryName { get; init; } = string.Empty;
    public int TotalItems { get; init; }
    public string TotalSize { get; init; } = string.Empty;
    public long TotalSizeBytes { get; init; }
    
    public ObservableCollection<PrivacyFileViewModel> Children { get; } = new();
    
    public bool IsSelected 
    { 
        get => _isSelected; 
        set 
        { 
            if (SetProperty(ref _isSelected, value))
            {
                // When category selection changes, update all children
                foreach (var child in Children)
                    child.SetSelectedWithoutParentUpdate(value);
            }
        } 
    }
    
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    
    public int SelectedCount => Children.Count(c => c.IsSelected);
    
    public void UpdateSelectionState()
    {
        // Update selection state based on children
        var allSelected = Children.All(c => c.IsSelected);
        var noneSelected = Children.All(c => !c.IsSelected);
        
        // Set without triggering child updates
        _isSelected = allSelected;
        RaisePropertyChanged(nameof(IsSelected));
        RaisePropertyChanged(nameof(SelectedCount));
    }
}

/// <summary>
/// Represents an individual file/folder to be cleaned
/// </summary>
public class PrivacyFileViewModel : BindableBase
{
    private bool _isSelected = true;
    
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public bool IsFile { get; init; }
    public PrivacyCategoryViewModel? Parent { get; init; }
    
    public bool IsSelected 
    { 
        get => _isSelected; 
        set 
        { 
            if (SetProperty(ref _isSelected, value))
            {
                // Notify parent to update its selection state
                Parent?.UpdateSelectionState();
            }
        } 
    }
    
    // Used when parent updates children to avoid circular updates
    internal void SetSelectedWithoutParentUpdate(bool value)
    {
        if (_isSelected != value)
        {
            _isSelected = value;
            RaisePropertyChanged(nameof(IsSelected));
        }
    }
}
