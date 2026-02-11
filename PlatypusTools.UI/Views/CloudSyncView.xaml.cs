using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Views
{
    public partial class CloudSyncView : UserControl
    {
        private readonly CloudSyncService _syncService;
        private readonly ObservableCollection<CloudSyncService.CloudProvider> _providers = new();
        private readonly ObservableCollection<CloudSyncService.SyncRule> _syncRules = new();
        private CancellationTokenSource? _syncCts;

        public CloudSyncView()
        {
            InitializeComponent();
            
            _syncService = CloudSyncService.Instance;
            _syncService.SyncProgressChanged += OnSyncProgress;
            _syncService.SyncError += OnSyncError;
            _syncService.SyncCompleted += OnSyncCompleted;
            
            ProvidersGrid.ItemsSource = _providers;
            SyncRulesGrid.ItemsSource = _syncRules;
            
            // Auto-detect on load
            Loaded += (s, e) => DetectProviders();
        }

        private void DetectProviders_Click(object sender, RoutedEventArgs e)
        {
            DetectProviders();
        }

        private void DetectProviders()
        {
            _providers.Clear();
            var detected = _syncService.DetectCloudProviders();
            foreach (var p in detected)
            {
                _providers.Add(p);
                _syncService.AddProvider(p);
            }
            StatusText.Text = $"Detected {detected.Count} cloud provider(s)";
        }

        private void AddLocalProvider_Click(object sender, RoutedEventArgs e)
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
                    Name = System.IO.Path.GetFileName(dialog.SelectedPath),
                    Type = CloudSyncService.CloudProviderType.LocalFolder,
                    RootPath = dialog.SelectedPath,
                    IsConnected = true
                };
                _providers.Add(provider);
                _syncService.AddProvider(provider);
                StatusText.Text = $"Added local folder: {dialog.SelectedPath}";
            }
        }

        private async void SyncNow_Click(object sender, RoutedEventArgs e)
        {
            if (SyncRulesGrid.SelectedItem is not CloudSyncService.SyncRule rule)
            {
                MessageBox.Show("Select a sync rule to run.", "Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            _syncCts = new CancellationTokenSource();
            StatusText.Text = "Syncing...";
            
            try
            {
                var result = await _syncService.SyncAsync(rule, _syncCts.Token);
                StatusText.Text = result.Success
                    ? $"Sync complete: ↑{result.FilesUploaded} ↓{result.FilesDownloaded} 🗑{result.FilesDeleted} ({result.Duration.TotalSeconds:F1}s)"
                    : $"Sync failed: {string.Join(", ", result.ErrorMessages)}";
                SyncProgress.Value = 0;
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Sync cancelled";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Sync error: {ex.Message}";
            }
        }

        private void StopSync_Click(object sender, RoutedEventArgs e)
        {
            _syncCts?.Cancel();
            StatusText.Text = "Stopping sync...";
        }

        private void AddSyncRule_Click(object sender, RoutedEventArgs e)
        {
            if (_providers.Count == 0)
            {
                MessageBox.Show("Please detect or add a cloud provider first.", "Add Rule", MessageBoxButton.OK, MessageBoxImage.Information);
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
                Name = $"Sync {System.IO.Path.GetFileName(localDialog.SelectedPath)}",
                LocalPath = localDialog.SelectedPath,
                RemotePath = remoteDialog.SelectedPath,
                ProviderId = _providers[0].Id,
                Direction = CloudSyncService.SyncDirection.Bidirectional,
                IsEnabled = true,
                SyncIntervalMinutes = 15
            };
            
            _syncRules.Add(rule);
            StatusText.Text = $"Added sync rule: {rule.Name}";
        }

        private void RemoveSyncRule_Click(object sender, RoutedEventArgs e)
        {
            if (SyncRulesGrid.SelectedItem is CloudSyncService.SyncRule rule)
            {
                _syncRules.Remove(rule);
                StatusText.Text = "Removed sync rule";
            }
        }

        private void EditSyncRule_Click(object sender, RoutedEventArgs e)
        {
            if (SyncRulesGrid.SelectedItem is not CloudSyncService.SyncRule rule) return;
            
            // Toggle direction as simple edit
            rule.Direction = rule.Direction switch
            {
                CloudSyncService.SyncDirection.Upload => CloudSyncService.SyncDirection.Download,
                CloudSyncService.SyncDirection.Download => CloudSyncService.SyncDirection.Bidirectional,
                _ => CloudSyncService.SyncDirection.Upload
            };
            
            SyncRulesGrid.Items.Refresh();
            StatusText.Text = $"Changed direction to {rule.Direction}";
        }

        private void OnSyncProgress(object? sender, CloudSyncService.SyncProgressEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SyncProgress.Value = e.PercentComplete;
                StatusText.Text = $"Syncing: {e.CurrentFile} ({e.FilesProcessed}/{e.TotalFiles})";
            });
        }

        private void OnSyncError(object? sender, CloudSyncService.SyncErrorEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText.Text = $"Error: {e.ErrorMessage}";
            });
        }

        private void OnSyncCompleted(object? sender, string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SyncProgress.Value = 0;
                StatusText.Text = message;
            });
        }
    }
}
