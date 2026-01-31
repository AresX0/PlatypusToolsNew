using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Windows Update Repair View - Diagnoses and repairs Windows Update issues
    /// </summary>
    public partial class WindowsUpdateRepairView : UserControl
    {
        private readonly WindowsUpdateRepairService _repairService;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public WindowsUpdateRepairView()
        {
            InitializeComponent();
            _repairService = new WindowsUpdateRepairService();
            AppendLog("Windows Update Repair Tool initialized.");
            AppendLog("This tool requires administrator privileges to function properly.");
            AppendLog("Click 'Scan for Issues' to diagnose Windows Update problems.");
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            SetRunningState(true);
            ClearLog();
            AppendLog("Starting Windows Update scan...\n");

            try
            {
                _cts = new CancellationTokenSource();
                var progress = new Progress<string>(msg => AppendLog(msg));

                var result = await _repairService.ScanAsync(progress, _cts.Token);

                UpdateScanStatus(result);
                DisplayIssues(result.Issues);

                if (result.HasIssues)
                {
                    RepairButton.IsEnabled = true;
                    AppendLog($"\n⚠️ {result.Summary}");
                    AppendLog("Click 'Repair All' to fix detected issues.");
                }
                else
                {
                    AppendLog($"\n✅ {result.Summary}");
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n❌ Scan cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"\n❌ Error during scan: {ex.Message}");
            }
            finally
            {
                SetRunningState(false);
            }
        }

        private async void RepairButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            var result = MessageBox.Show(
                "This will attempt to repair Windows Update using multiple methods.\n\n" +
                "This process may take 30-60 minutes to complete.\n\n" +
                "Do you want to continue?",
                "Confirm Repair",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            SetRunningState(true);
            AppendLog("\n" + new string('=', 50));
            AppendLog("Starting Windows Update repair...\n");

            try
            {
                _cts = new CancellationTokenSource();
                var progress = new Progress<string>(msg => AppendLog(msg));

                var options = GetRepairOptions();
                var repairResult = await _repairService.RepairAsync(options, progress, _cts.Token);

                DisplayRepairResult(repairResult);
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n❌ Repair cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"\n❌ Error during repair: {ex.Message}");
            }
            finally
            {
                SetRunningState(false);
            }
        }

        private async void QuickFixButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            SetRunningState(true);
            AppendLog("\n" + new string('=', 50));
            AppendLog("Running quick fix (essential repairs only)...\n");

            try
            {
                _cts = new CancellationTokenSource();
                var progress = new Progress<string>(msg => AppendLog(msg));

                var options = new RepairOptions
                {
                    RunDism = false,
                    RunSfc = false,
                    ResetComponents = true,
                    ClearCache = true,
                    ReregisterDlls = true,
                    RunTroubleshooter = true,
                    CreateRestorePoint = false
                };

                var repairResult = await _repairService.RepairAsync(options, progress, _cts.Token);
                DisplayRepairResult(repairResult);
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n❌ Quick fix cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"\n❌ Error during quick fix: {ex.Message}");
            }
            finally
            {
                SetRunningState(false);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                AppendLog("\nCancelling operation...");
                _cts.Cancel();
            }
        }

        private async void BtnSfc_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            SetRunningState(true);
            AppendLog("\n" + new string('=', 50));
            AppendLog("Running System File Checker...\n");

            try
            {
                _cts = new CancellationTokenSource();
                var progress = new Progress<string>(msg => AppendLog(msg));

                var result = await _repairService.RunSfcScanAsync(progress, _cts.Token);
                AppendLog($"\nSFC completed in {result.Duration.TotalMinutes:F1} minutes");
                AppendLog(result.Success ? "✅ SFC completed successfully" : $"⚠️ SFC completed with issues: {result.Error}");
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n❌ SFC cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"\n❌ Error: {ex.Message}");
            }
            finally
            {
                SetRunningState(false);
            }
        }

        private async void BtnDism_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            SetRunningState(true);
            AppendLog("\n" + new string('=', 50));
            AppendLog("Running DISM RestoreHealth...\n");

            try
            {
                _cts = new CancellationTokenSource();
                var progress = new Progress<string>(msg => AppendLog(msg));

                var result = await _repairService.RunDismHealthCheckAsync(progress, _cts.Token);
                AppendLog($"\nDISM completed in {result.Duration.TotalMinutes:F1} minutes");
                AppendLog(result.Success ? "✅ DISM completed successfully" : $"⚠️ DISM completed with issues: {result.Error}");
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n❌ DISM cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"\n❌ Error: {ex.Message}");
            }
            finally
            {
                SetRunningState(false);
            }
        }

        private async void BtnResetComponents_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            SetRunningState(true);
            AppendLog("\n" + new string('=', 50));
            AppendLog("Resetting Windows Update components...\n");

            try
            {
                _cts = new CancellationTokenSource();
                var progress = new Progress<string>(msg => AppendLog(msg));

                var result = await _repairService.ResetWindowsUpdateComponentsAsync(progress, _cts.Token);
                AppendLog(result.Success ? "\n✅ Components reset successfully" : $"\n⚠️ Reset completed with issues: {result.Error}");
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n❌ Reset cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"\n❌ Error: {ex.Message}");
            }
            finally
            {
                SetRunningState(false);
            }
        }

        private async void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            SetRunningState(true);
            AppendLog("\n" + new string('=', 50));
            AppendLog("Clearing Windows Update cache...\n");

            try
            {
                _cts = new CancellationTokenSource();
                var progress = new Progress<string>(msg => AppendLog(msg));

                var result = await _repairService.ClearUpdateCacheAsync(progress, _cts.Token);
                AppendLog(result.Success ? "\n✅ Cache cleared successfully" : $"\n⚠️ Cache clear completed with issues: {result.Error}");
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n❌ Clear cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"\n❌ Error: {ex.Message}");
            }
            finally
            {
                SetRunningState(false);
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            ClearLog();
        }

        private RepairOptions GetRepairOptions()
        {
            return new RepairOptions
            {
                CreateRestorePoint = ChkCreateRestore.IsChecked == true,
                RunTroubleshooter = ChkTroubleshooter.IsChecked == true,
                ResetComponents = ChkResetComponents.IsChecked == true,
                ClearCache = ChkClearCache.IsChecked == true,
                ReregisterDlls = ChkReregisterDlls.IsChecked == true,
                RunSfc = ChkRunSfc.IsChecked == true,
                RunDism = ChkRunDism.IsChecked == true
            };
        }

        private void UpdateScanStatus(WindowsUpdateScanResult result)
        {
            StatusDetails.Visibility = Visibility.Visible;
            ServicesStatus.Text = result.ServicesRunning 
                ? "✅ Windows Update services are running" 
                : "❌ Some services are not running";
            DismStatus.Text = result.DismHealthy 
                ? "✅ Windows component store is healthy" 
                : "⚠️ Windows component store may need repair";
            CacheStatus.Text = $"📁 Update cache size: {result.CacheSizeBytes / (1024 * 1024):N0} MB";

            if (result.HasIssues)
            {
                StatusText.Text = $"Found {result.Issues.Count} issue(s)";
                StatusBorder.Background = Application.Current.TryFindResource("WarningBrush") as System.Windows.Media.Brush 
                    ?? System.Windows.Media.Brushes.Orange;
            }
            else
            {
                StatusText.Text = "No issues detected";
                StatusBorder.Background = Application.Current.TryFindResource("SuccessBrush") as System.Windows.Media.Brush 
                    ?? System.Windows.Media.Brushes.Green;
            }
        }

        private void DisplayIssues(List<WindowsUpdateIssue> issues)
        {
            if (issues.Count > 0)
            {
                IssuesPanel.Visibility = Visibility.Visible;
                var displayIssues = new List<IssueDisplayItem>();
                foreach (var issue in issues)
                {
                    displayIssues.Add(new IssueDisplayItem
                    {
                        SeverityIcon = issue.Severity switch
                        {
                            IssueSeverity.Critical => "🔴",
                            IssueSeverity.Error => "🟠",
                            IssueSeverity.Warning => "🟡",
                            _ => "🔵"
                        },
                        Description = issue.Description,
                        RecommendedFix = issue.RecommendedFix
                    });
                }
                IssuesList.ItemsSource = displayIssues;
            }
            else
            {
                IssuesPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void DisplayRepairResult(WindowsUpdateRepairResult result)
        {
            AppendLog("\n" + new string('=', 50));
            AppendLog("REPAIR SUMMARY");
            AppendLog(new string('=', 50));

            foreach (var step in result.Steps)
            {
                var status = step.Success ? "✅" : "❌";
                AppendLog($"{status} {step.StepName} ({step.Duration.TotalSeconds:F0}s)");
                if (!step.Success && !string.IsNullOrEmpty(step.Error))
                {
                    AppendLog($"   Error: {step.Error}");
                }
            }

            AppendLog("\n" + (result.Success ? "✅ " : "⚠️ ") + result.Summary);

            if (result.RequiresReboot)
            {
                AppendLog("\n⚠️ A system restart is recommended to complete the repairs.");
                
                var restart = MessageBox.Show(
                    "Some repairs require a system restart to complete.\n\nWould you like to restart now?",
                    "Restart Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (restart == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("shutdown", "/r /t 30 /c \"Restarting to complete Windows Update repairs\"");
                        AppendLog("System will restart in 30 seconds...");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Failed to initiate restart: {ex.Message}");
                    }
                }
            }
        }

        private void SetRunningState(bool isRunning)
        {
            _isRunning = isRunning;
            
            ScanButton.IsEnabled = !isRunning;
            RepairButton.IsEnabled = !isRunning && RepairButton.IsEnabled;
            QuickFixButton.IsEnabled = !isRunning;
            CancelButton.IsEnabled = isRunning;
            BtnSfc.IsEnabled = !isRunning;
            BtnDism.IsEnabled = !isRunning;
            BtnResetComponents.IsEnabled = !isRunning;
            BtnClearCache.IsEnabled = !isRunning;

            ProgressBar.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            ProgressBar.IsIndeterminate = isRunning;
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogScrollViewer.ScrollToEnd();
            });
        }

        private void ClearLog()
        {
            LogTextBox.Clear();
        }

        private class IssueDisplayItem
        {
            public string SeverityIcon { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string RecommendedFix { get; set; } = string.Empty;
        }
    }
}
