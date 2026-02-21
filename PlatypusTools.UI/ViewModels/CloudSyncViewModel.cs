using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// ViewModel for Cloud Sync view. Manages cloud provider detection,
/// sync rules, and folder synchronization operations.
/// </summary>
public class CloudSyncViewModel : BindableBase
{
    private readonly CloudSyncService _syncService;
    private CancellationTokenSource? _syncCts;

    public CloudSyncViewModel()
    {
        _syncService = CloudSyncService.Instance;
        _syncService.SyncProgressChanged += OnSyncProgress;
        _syncService.SyncError += OnSyncError;
        _syncService.SyncCompleted += OnSyncCompleted;

        Providers = new ObservableCollection<CloudSyncService.CloudProvider>();
        SyncRules = new ObservableCollection<CloudSyncService.SyncRule>();

        DetectProvidersCommand = new RelayCommand(_ => DetectProviders());
        AddLocalProviderCommand = new RelayCommand(_ => AddLocalProvider());
        SyncNowCommand = new AsyncRelayCommand(SyncNowAsync, () => SelectedSyncRule != null && !IsSyncing);
        StopSyncCommand = new RelayCommand(_ => StopSync(), _ => IsSyncing);
        AddSyncRuleCommand = new RelayCommand(_ => AddSyncRule(), _ => Providers.Count > 0);
        RemoveSyncRuleCommand = new RelayCommand(_ => RemoveSyncRule(), _ => SelectedSyncRule != null);
        EditSyncRuleCommand = new RelayCommand(_ => EditSyncRule(), _ => SelectedSyncRule != null);

        // Auto-detect on construction
        DetectProviders();
    }

    #region Properties

    public ObservableCollection<CloudSyncService.CloudProvider> Providers { get; }
    public ObservableCollection<CloudSyncService.SyncRule> SyncRules { get; }

    private CloudSyncService.CloudProvider? _selectedProvider;
    public CloudSyncService.CloudProvider? SelectedProvider
    {
        get => _selectedProvider;
        set { _selectedProvider = value; RaisePropertyChanged(); }
    }

    private CloudSyncService.SyncRule? _selectedSyncRule;
    public CloudSyncService.SyncRule? SelectedSyncRule
    {
        get => _selectedSyncRule;
        set { _selectedSyncRule = value; RaisePropertyChanged(); }
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; RaisePropertyChanged(); }
    }

    private double _syncProgress;
    public double SyncProgress
    {
        get => _syncProgress;
        set { _syncProgress = value; RaisePropertyChanged(); }
    }

    private bool _isSyncing;
    public bool IsSyncing
    {
        get => _isSyncing;
        set { _isSyncing = value; RaisePropertyChanged(); }
    }

    #endregion

    #region Commands

    public ICommand DetectProvidersCommand { get; }
    public ICommand AddLocalProviderCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand StopSyncCommand { get; }
    public ICommand AddSyncRuleCommand { get; }
    public ICommand RemoveSyncRuleCommand { get; }
    public ICommand EditSyncRuleCommand { get; }

    #endregion

    #region Methods

    private void DetectProviders()
    {
        Providers.Clear();
        var detected = _syncService.DetectCloudProviders();
        foreach (var p in detected)
        {
            Providers.Add(p);
            _syncService.AddProvider(p);
        }
        StatusMessage = $"Detected {detected.Count} cloud provider(s)";
    }

    private void AddLocalProvider()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder to use as sync target",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var provider = new CloudSyncService.CloudProvider
            {
                Name = Path.GetFileName(dialog.SelectedPath),
                Type = CloudSyncService.CloudProviderType.LocalFolder,
                RootPath = dialog.SelectedPath,
                IsConnected = true
            };
            Providers.Add(provider);
            _syncService.AddProvider(provider);
            StatusMessage = $"Added local folder: {dialog.SelectedPath}";
        }
    }

    private async Task SyncNowAsync()
    {
        if (SelectedSyncRule == null)
        {
            StatusMessage = "Select a sync rule to run.";
            return;
        }

        _syncCts = new CancellationTokenSource();
        IsSyncing = true;
        StatusMessage = "Syncing...";

        try
        {
            var result = await _syncService.SyncAsync(SelectedSyncRule, _syncCts.Token);
            StatusMessage = result.Success
                ? $"Sync complete: ↑{result.FilesUploaded} ↓{result.FilesDownloaded} 🗑{result.FilesDeleted} ({result.Duration.TotalSeconds:F1}s)"
                : $"Sync failed: {string.Join(", ", result.ErrorMessages)}";
            SyncProgress = 0;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sync cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void StopSync()
    {
        _syncCts?.Cancel();
        StatusMessage = "Stopping sync...";
    }

    private void AddSyncRule()
    {
        if (Providers.Count == 0)
        {
            StatusMessage = "Please detect or add a cloud provider first.";
            return;
        }

        using var localDialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select local folder to sync",
            UseDescriptionForTitle = true
        };

        if (localDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        using var remoteDialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select remote/cloud folder to sync with",
            UseDescriptionForTitle = true
        };

        if (remoteDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var rule = new CloudSyncService.SyncRule
        {
            Name = $"Sync {Path.GetFileName(localDialog.SelectedPath)}",
            LocalPath = localDialog.SelectedPath,
            RemotePath = remoteDialog.SelectedPath,
            ProviderId = (SelectedProvider ?? Providers[0]).Id,
            Direction = CloudSyncService.SyncDirection.Bidirectional,
            IsEnabled = true,
            SyncIntervalMinutes = 15
        };

        SyncRules.Add(rule);
        StatusMessage = $"Added sync rule: {rule.Name}";
    }

    private void RemoveSyncRule()
    {
        if (SelectedSyncRule != null)
        {
            var name = SelectedSyncRule.Name;
            SyncRules.Remove(SelectedSyncRule);
            StatusMessage = $"Removed sync rule: {name}";
        }
    }

    private void EditSyncRule()
    {
        if (SelectedSyncRule == null) return;

        // Cycle through directions
        SelectedSyncRule.Direction = SelectedSyncRule.Direction switch
        {
            CloudSyncService.SyncDirection.Upload => CloudSyncService.SyncDirection.Download,
            CloudSyncService.SyncDirection.Download => CloudSyncService.SyncDirection.Bidirectional,
            _ => CloudSyncService.SyncDirection.Upload
        };

        // Force UI refresh by replacing the item
        var idx = SyncRules.IndexOf(SelectedSyncRule);
        if (idx >= 0)
        {
            var rule = SelectedSyncRule;
            SyncRules.RemoveAt(idx);
            SyncRules.Insert(idx, rule);
            SelectedSyncRule = rule;
        }

        StatusMessage = $"Changed direction to {SelectedSyncRule.Direction}";
    }

    private void OnSyncProgress(object? sender, CloudSyncService.SyncProgressEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SyncProgress = e.PercentComplete;
            StatusMessage = $"Syncing: {e.CurrentFile} ({e.FilesProcessed}/{e.TotalFiles})";
        });
    }

    private void OnSyncError(object? sender, CloudSyncService.SyncErrorEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            StatusMessage = $"Error: {e.ErrorMessage}";
        });
    }

    private void OnSyncCompleted(object? sender, string message)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SyncProgress = 0;
            StatusMessage = message;
            IsSyncing = false;
        });
    }

    #endregion
}
