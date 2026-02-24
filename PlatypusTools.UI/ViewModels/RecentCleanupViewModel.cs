using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using static PlatypusTools.Core.Services.AppTaskSchedulerService;
using System.Diagnostics;

namespace PlatypusTools.UI.ViewModels
{
    public class RecentCleanupViewModel : BindableBase
    {
        public ObservableCollection<RecentMatchSelectable> Results { get; } = new ObservableCollection<RecentMatchSelectable>();
        public ObservableCollection<string> ExclusionDirs { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> TargetDirectoryList { get; } = new ObservableCollection<string>();

        private string _selectedTargetDirectory = "";
        public string SelectedTargetDirectory
        {
            get => _selectedTargetDirectory;
            set { _selectedTargetDirectory = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// Computed property: joins TargetDirectoryList with semicolons for backward compatibility.
        /// Setting this parses the semicolon-separated string into the list.
        /// </summary>
        public string TargetDirs
        {
            get => string.Join(";", TargetDirectoryList);
            set
            {
                TargetDirectoryList.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    foreach (var dir in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(d => d.Trim())
                                              .Where(d => !string.IsNullOrEmpty(d)))
                    {
                        if (!TargetDirectoryList.Contains(dir, StringComparer.OrdinalIgnoreCase))
                            TargetDirectoryList.Add(dir);
                    }
                }
                RaiseTargetDirsChanged();
            }
        }

        private void RaiseTargetDirsChanged()
        {
            RaisePropertyChanged(nameof(TargetDirs));
            RaisePropertyChanged(nameof(HasTargetDirs));
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScheduleCleanupCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ToggleBlockerCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CreateWindowsTaskCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RemoveTargetDirCommand)?.RaiseCanExecuteChanged();
        }

        public bool HasTargetDirs => TargetDirectoryList.Count > 0;

        private bool _dryRun = true;
        public bool DryRun { get => _dryRun; set { _dryRun = value; RaisePropertyChanged(); } }

        private bool _includeSubDirs = true;
        public bool IncludeSubDirs { get => _includeSubDirs; set { _includeSubDirs = value; RaisePropertyChanged(); } }

        private string _selectedExclusionDir = "";
        public string SelectedExclusionDir { get => _selectedExclusionDir; set { _selectedExclusionDir = value; RaisePropertyChanged(); } }

        private RecentMatchSelectable? _selectedResult;
        public RecentMatchSelectable? SelectedResult { get => _selectedResult; set { _selectedResult = value; RaisePropertyChanged(); } }

        public int SelectedCount => Results.Count(r => r.IsSelected);

        public ICommand ScanCommand { get; }
        public ICommand ScanAllRecentCommand { get; }
        public ICommand BrowseCommand { get; }
        public ICommand ClearTargetDirsCommand { get; }
        public ICommand AddExclusionCommand { get; }
        public ICommand RemoveExclusionCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand ClearAllRecentCommand { get; }
        public ICommand RefreshExplorerCommand { get; }
        public ICommand ScheduleCleanupCommand { get; }
        public ICommand ToggleBlockerCommand { get; }
        public ICommand CreateWindowsTaskCommand { get; }
        public ICommand RemoveWindowsTaskCommand { get; }
        public ICommand AddTargetDirCommand { get; }
        public ICommand RemoveTargetDirCommand { get; }

        private string _statusMessage = "Ready. Enter target directories or click 'Scan All Recent' to see all recent items.";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; RaisePropertyChanged(); } }

        private string _selectedScheduleFrequency = "Daily";
        public string SelectedScheduleFrequency { get => _selectedScheduleFrequency; set { _selectedScheduleFrequency = value; RaisePropertyChanged(); } }

        public string[] ScheduleFrequencyOptions { get; } = new[] { "Once", "Hourly", "Daily", "Weekly", "Monthly", "On Startup" };

        private bool _isBlockerActive;
        public bool IsBlockerActive
        {
            get => _isBlockerActive;
            set { _isBlockerActive = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(BlockerStatusText)); }
        }

        public string BlockerStatusText => IsBlockerActive 
            ? $"🛡️ Blocker ON ({RecentBlockerService.Instance.BlockedDirectories.Count} dirs, {RecentBlockerService.Instance.BlockedCount} blocked)" 
            : "🔓 Blocker OFF";

        private bool _windowsTaskExists;
        public bool WindowsTaskExists
        {
            get => _windowsTaskExists;
            set { _windowsTaskExists = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(WindowsTaskStatusText)); }
        }

        public string WindowsTaskStatusText => WindowsTaskExists 
            ? "✅ Windows Task active" 
            : "No Windows Task";

        public RecentCleanupViewModel()
        {
            ScanCommand = new RelayCommand(async _ => await Scan(), _ => TargetDirectoryList.Count > 0);
            ScanAllRecentCommand = new RelayCommand(async _ => await ScanAllRecent());
            BrowseCommand = new RelayCommand(_ => Browse());
            ClearTargetDirsCommand = new RelayCommand(_ => { TargetDirectoryList.Clear(); });
            AddExclusionCommand = new RelayCommand(_ => AddExclusion());
            RemoveExclusionCommand = new RelayCommand(_ => RemoveExclusion(), _ => !string.IsNullOrEmpty(SelectedExclusionDir));
            RemoveSelectedCommand = new RelayCommand(async _ => await RemoveSelected(), _ => Results.Any(r => r.IsSelected));
            SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => Results.Any());
            SelectNoneCommand = new RelayCommand(_ => SelectNone(), _ => Results.Any(r => r.IsSelected));
            ClearAllRecentCommand = new RelayCommand(async _ => await ClearAllRecent());
            RefreshExplorerCommand = new RelayCommand(_ => RefreshExplorer());
            ScheduleCleanupCommand = new RelayCommand(_ => ScheduleCleanup(), _ => TargetDirectoryList.Count > 0);
            ToggleBlockerCommand = new RelayCommand(_ => ToggleBlocker(), _ => TargetDirectoryList.Count > 0 || IsBlockerActive);
            CreateWindowsTaskCommand = new RelayCommand(_ => CreateWindowsTask(), _ => TargetDirectoryList.Count > 0);
            RemoveWindowsTaskCommand = new RelayCommand(_ => RemoveWindowsTask());
            AddTargetDirCommand = new RelayCommand(_ => AddTargetDir());
            RemoveTargetDirCommand = new RelayCommand(_ => RemoveTargetDir(), _ => !string.IsNullOrEmpty(SelectedTargetDirectory));

            // When the list changes, update computed properties
            TargetDirectoryList.CollectionChanged += (s, e) => RaiseTargetDirsChanged();

            // Sync blocker state
            _isBlockerActive = RecentBlockerService.Instance.IsRunning;
            _windowsTaskExists = RecentBlockerService.Instance.WindowsScheduledTaskExists();

            // If blocker has saved blocked dirs and was configured, auto-start
            if (RecentBlockerService.Instance.BlockedDirectories.Count > 0)
            {
                // Populate TargetDirectoryList from saved blocker config
                if (TargetDirectoryList.Count == 0)
                {
                    foreach (var dir in RecentBlockerService.Instance.BlockedDirectories)
                    {
                        if (!TargetDirectoryList.Contains(dir, StringComparer.OrdinalIgnoreCase))
                            TargetDirectoryList.Add(dir);
                    }
                }
            }

            RecentBlockerService.Instance.ItemBlocked += (s, e) =>
            {
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    RaisePropertyChanged(nameof(BlockerStatusText));
                    StatusMessage = $"🛡️ Blocked: {Path.GetFileName(e.ShortcutPath)} → {e.TargetPath}";
                });
            };
        }

        private void SelectAll()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in Results)
                    item.IsSelected = true;
                RaisePropertyChanged(nameof(SelectedCount));
                ((RelayCommand)RemoveSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SelectNoneCommand).RaiseCanExecuteChanged();
            });
        }

        private void SelectNone()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in Results)
                    item.IsSelected = false;
                RaisePropertyChanged(nameof(SelectedCount));
                ((RelayCommand)RemoveSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SelectNoneCommand).RaiseCanExecuteChanged();
            });
        }

        private void Browse()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select target directory to scan for Recent shortcuts",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!TargetDirectoryList.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
                {
                    TargetDirectoryList.Add(dialog.SelectedPath);
                }
            }
        }

        private void AddTargetDir()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select directory to add to target list",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!TargetDirectoryList.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
                {
                    TargetDirectoryList.Add(dialog.SelectedPath);
                }
            }
        }

        private void RemoveTargetDir()
        {
            if (!string.IsNullOrEmpty(SelectedTargetDirectory))
            {
                TargetDirectoryList.Remove(SelectedTargetDirectory);
                SelectedTargetDirectory = "";
            }
        }

        private void AddExclusion()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select directory to exclude from cleanup",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!ExclusionDirs.Contains(dialog.SelectedPath))
                {
                    ExclusionDirs.Add(dialog.SelectedPath);
                }
            }
        }

        private void RemoveExclusion()
        {
            if (!string.IsNullOrEmpty(SelectedExclusionDir))
            {
                ExclusionDirs.Remove(SelectedExclusionDir);
                SelectedExclusionDir = "";
            }
        }

        public async Task Scan()
        {
            if (string.IsNullOrWhiteSpace(TargetDirs))
            {
                StatusMessage = "Error: Please enter or browse to target directories";
                return;
            }

            try
            {
                StatusMessage = "Scanning Recent folder...";
                Results.Clear();
                var dirs = TargetDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(d => d.Trim())
                                     .Where(d => !string.IsNullOrEmpty(d))
                                     .ToArray();
                
                if (dirs.Length == 0)
                {
                    StatusMessage = "Error: No valid directories specified";
                    return;
                }

                var results = await Task.Run(() => RecentCleaner.RemoveRecentShortcuts(dirs, dryRun: true)); // Always dry run for scan
                
                foreach (var r in results)
                {
                    // Skip if in exclusion list
                    var isExcluded = ExclusionDirs.Any(ex => r.Target?.StartsWith(ex, StringComparison.OrdinalIgnoreCase) == true);
                    if (!isExcluded)
                    {
                        var selectable = new RecentMatchSelectable
                        {
                            Type = r.Type,
                            Path = r.Path,
                            Target = r.Target ?? string.Empty,
                            IsSelected = false
                        };
                        selectable.PropertyChanged += (s, e) => 
                        {
                            if (e.PropertyName == nameof(RecentMatchSelectable.IsSelected))
                            {
                                RaisePropertyChanged(nameof(SelectedCount));
                                ((RelayCommand)RemoveSelectedCommand).RaiseCanExecuteChanged();
                            }
                        };
                        await Application.Current.Dispatcher.InvokeAsync(() => Results.Add(selectable));
                    }
                }

                RaisePropertyChanged(nameof(SelectedCount));
                StatusMessage = $"Found {Results.Count} matching shortcuts (preview mode)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning: {ex.Message}";
            }
        }

        /// <summary>
        /// Scans all recent items in Windows Recent folder, Quick Access, Start Menu, etc.
        /// If TargetDirs is specified, filters to only show items pointing to files in those directories.
        /// </summary>
        public async Task ScanAllRecent()
        {
            try
            {
                // Parse target directories for filtering (if specified)
                var filterDirs = string.IsNullOrWhiteSpace(TargetDirs) 
                    ? Array.Empty<string>() 
                    : TargetDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => !string.IsNullOrEmpty(d))
                        .Select(d => {
                            try { return Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                            catch { return d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                        })
                        .ToArray();

                var filterMessage = filterDirs.Length > 0 
                    ? $"Scanning recent items matching {filterDirs.Length} target director{(filterDirs.Length == 1 ? "y" : "ies")}..." 
                    : "Scanning all recent items (Recent folder, Jump Lists, Favorites, Office)...";
                StatusMessage = filterMessage;
                Results.Clear();

                await Task.Run(() =>
                {
                    // Use the new comprehensive scanner in RecentCleaner
                    var allRecentItems = RecentCleaner.ScanAllRecentItems();
                    
                    foreach (var item in allRecentItems)
                    {
                        // Skip if in exclusion list
                        var isExcluded = ExclusionDirs.Any(ex => 
                            item.Target?.StartsWith(ex, StringComparison.OrdinalIgnoreCase) == true ||
                            item.Path?.StartsWith(ex, StringComparison.OrdinalIgnoreCase) == true);
                        
                        if (isExcluded) continue;

                        // If filter directories specified, only include items whose target is within those directories
                        if (filterDirs.Length > 0)
                        {
                            var target = item.Target ?? "";
                            string normalizedTarget;
                            try { normalizedTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                            catch { normalizedTarget = target; }

                            bool matchesFilter;
                            if (IncludeSubDirs)
                            {
                                // Match if target is within the directory (including subdirs)
                                matchesFilter = filterDirs.Any(dir => 
                                    normalizedTarget.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                    normalizedTarget.Equals(dir, StringComparison.OrdinalIgnoreCase));
                            }
                            else
                            {
                                // Match only if target's parent directory exactly matches one of the filter dirs
                                var targetParent = Path.GetDirectoryName(normalizedTarget) ?? "";
                                matchesFilter = filterDirs.Any(dir => 
                                    string.Equals(targetParent, dir, StringComparison.OrdinalIgnoreCase));
                            }

                            if (!matchesFilter) continue;
                        }

                        var selectable = new RecentMatchSelectable
                        {
                            Type = item.Type,
                            Path = item.Path,
                            Target = item.Target ?? "",
                            IsSelected = false
                        };
                        selectable.PropertyChanged += (s, e) => 
                        {
                            if (e.PropertyName == nameof(RecentMatchSelectable.IsSelected))
                            {
                                RaisePropertyChanged(nameof(SelectedCount));
                                ((RelayCommand)RemoveSelectedCommand).RaiseCanExecuteChanged();
                            }
                        };
                        _ = Application.Current.Dispatcher.InvokeAsync(() => Results.Add(selectable));
                    }
                });

                RaisePropertyChanged(nameof(SelectedCount));
                ((RelayCommand)SelectAllCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SelectNoneCommand).RaiseCanExecuteChanged();
                var resultMessage = filterDirs.Length > 0
                    ? $"Found {Results.Count} recent items matching target directories"
                    : $"Found {Results.Count} recent items across all locations";
                StatusMessage = resultMessage;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning: {ex.Message}";
            }
        }

        private async Task RemoveSelected()
        {
            var selectedItems = Results.Where(r => r.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                StatusMessage = "No items selected";
                return;
            }

            if (DryRun)
            {
                StatusMessage = $"Dry Run: Would remove {selectedItems.Count} shortcuts";
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to remove {selectedItems.Count} shortcuts?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                StatusMessage = $"Removing {selectedItems.Count} shortcuts...";
                int removed = 0;
                var undoOps = new System.Collections.Generic.List<FileOperation>();
                var undoBackupDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "UndoBackups");

                await Task.Run(() =>
                {
                    foreach (var item in selectedItems)
                    {
                        try
                        {
                            if (File.Exists(item.Path))
                            {
                                // Backup for undo
                                Directory.CreateDirectory(undoBackupDir);
                                var backupPath = Path.Combine(undoBackupDir, Guid.NewGuid().ToString("N") + Path.GetExtension(item.Path));
                                File.Copy(item.Path, backupPath);
                                File.Delete(item.Path);
                                undoOps.Add(new FileOperation { Type = OperationType.Delete, OriginalPath = item.Path, BackupPath = backupPath, Timestamp = DateTime.Now });
                                removed++;
                            }
                        }
                        catch
                        {
                            // Skip files that can't be deleted
                        }
                    }
                });

                if (undoOps.Count > 0)
                    UndoRedoService.Instance.RecordBatch(undoOps, $"Remove {undoOps.Count} recent shortcuts");

                // Remove from list
                foreach (var item in selectedItems)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => Results.Remove(item));
                }

                StatusMessage = $"Removed {removed} shortcuts successfully";
                RaisePropertyChanged(nameof(SelectedCount));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error removing shortcuts: {ex.Message}";
            }
        }

        private async Task ClearAllRecent()
        {
            if (DryRun)
            {
                StatusMessage = "Dry Run: Would clear all Recent items, Jump Lists, and Quick Access history";
                return;
            }

            var result = MessageBox.Show(
                "This will clear ALL recent items from Windows including:\n\n" +
                "• Recent folder shortcuts (.lnk files)\n" +
                "• Jump Lists (AutomaticDestinations)\n" +
                "• Custom Jump Lists (CustomDestinations)\n\n" +
                "You may need to restart Explorer for changes to take effect.\n\n" +
                "Continue?",
                "Clear All Recent History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                StatusMessage = "Clearing all recent history...";
                int deleted = 0;

                await Task.Run(() =>
                {
                    var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                    if (string.IsNullOrEmpty(recentPath))
                    {
                        recentPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "Microsoft", "Windows", "Recent");
                    }

                    // Delete .lnk files
                    if (Directory.Exists(recentPath))
                    {
                        foreach (var file in Directory.GetFiles(recentPath, "*.lnk", SearchOption.TopDirectoryOnly))
                        {
                            try { File.Delete(file); deleted++; } catch { }
                        }
                    }

                    // Delete AutomaticDestinations
                    var autoDestPath = System.IO.Path.Combine(recentPath, "AutomaticDestinations");
                    if (Directory.Exists(autoDestPath))
                    {
                        foreach (var file in Directory.GetFiles(autoDestPath, "*.automaticDestinations-ms"))
                        {
                            try { File.Delete(file); deleted++; } catch { }
                        }
                    }

                    // Delete CustomDestinations
                    var customDestPath = System.IO.Path.Combine(recentPath, "CustomDestinations");
                    if (Directory.Exists(customDestPath))
                    {
                        foreach (var file in Directory.GetFiles(customDestPath, "*.customDestinations-ms"))
                        {
                            try { File.Delete(file); deleted++; } catch { }
                        }
                    }
                });

                Results.Clear();
                RaisePropertyChanged(nameof(SelectedCount));
                StatusMessage = $"Cleared {deleted} recent items. Click 'Refresh Explorer' to update File Explorer.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error clearing recent history: {ex.Message}";
            }
        }

        private void RefreshExplorer()
        {
            try
            {
                // Kill and restart Explorer to refresh Recent cache
                var result = MessageBox.Show(
                    "This will restart Windows Explorer to refresh the Recent view.\n\n" +
                    "All Explorer windows will be closed temporarily.\n\n" +
                    "Continue?",
                    "Refresh Explorer",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                // Kill explorer
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
                {
                    try { proc.Kill(); } catch { }
                }

                // Wait a moment
                System.Threading.Thread.Sleep(500);

                // Start explorer again
                System.Diagnostics.Process.Start("explorer.exe");

                StatusMessage = "Explorer restarted. Recent view should be updated.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error restarting Explorer: {ex.Message}";
            }
        }

        private void ScheduleCleanup()
        {
            if (string.IsNullOrWhiteSpace(TargetDirs))
            {
                StatusMessage = "Error: Please enter or browse to target directories first";
                return;
            }

            var dirs = TargetDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(d => d.Trim())
                                 .Where(d => !string.IsNullOrEmpty(d))
                                 .ToArray();

            if (dirs.Length == 0)
            {
                StatusMessage = "Error: No valid directories specified";
                return;
            }

            // Map UI frequency to TaskScheduleType
            var scheduleType = SelectedScheduleFrequency switch
            {
                "Once" => TaskScheduleType.Once,
                "Hourly" => TaskScheduleType.Hourly,
                "Daily" => TaskScheduleType.Daily,
                "Weekly" => TaskScheduleType.Weekly,
                "Monthly" => TaskScheduleType.Monthly,
                "On Startup" => TaskScheduleType.OnStartup,
                _ => TaskScheduleType.Daily
            };

            var dirList = string.Join("; ", dirs.Select(d => Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar))));
            var result = MessageBox.Show(
                $"Create a scheduled task to automatically remove recent shortcuts for:\n\n" +
                $"Directories: {string.Join("; ", dirs)}\n" +
                $"Frequency: {SelectedScheduleFrequency}\n" +
                $"Include Subdirectories: {(IncludeSubDirs ? "Yes" : "No")}\n\n" +
                $"The task will run while PlatypusTools is open.\n\n" +
                $"Continue?",
                "Schedule Recent Cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var task = new ScheduledAppTask
                {
                    Name = $"Recent Cleanup: {dirList}",
                    Description = $"Remove recent shortcuts pointing to files in: {string.Join("; ", dirs)}",
                    Type = TaskType.RecentCleanup,
                    Schedule = scheduleType,
                    IsEnabled = true,
                    Parameters = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "TargetDirectories", string.Join(";", dirs) },
                        { "IncludeSubDirs", IncludeSubDirs.ToString().ToLower() }
                    }
                };

                AppTaskSchedulerService.Instance.AddTask(task);
                StatusMessage = $"✅ Scheduled {SelectedScheduleFrequency.ToLower()} recent cleanup for {dirs.Length} director{(dirs.Length == 1 ? "y" : "ies")}. Task will run while PlatypusTools is open.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating scheduled task: {ex.Message}";
            }
        }

        private void ToggleBlocker()
        {
            if (IsBlockerActive)
            {
                // Stop the blocker
                RecentBlockerService.Instance.Stop();
                IsBlockerActive = false;
                StatusMessage = "🔓 Real-time Recent blocker stopped.";
                ((RelayCommand)ToggleBlockerCommand).RaiseCanExecuteChanged();
                return;
            }

            // Start the blocker with current target dirs
            var dirs = TargetDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(d => d.Trim())
                                 .Where(d => !string.IsNullOrEmpty(d))
                                 .ToArray();

            if (dirs.Length == 0)
            {
                StatusMessage = "Error: Enter target directories to block first";
                return;
            }

            RecentBlockerService.Instance.SetBlockedDirectories(dirs);
            RecentBlockerService.Instance.SetIncludeSubDirs(IncludeSubDirs);
            RecentBlockerService.Instance.Start();

            var initialCleaned = 0; // Already cleaned in Start()
            IsBlockerActive = RecentBlockerService.Instance.IsRunning;

            if (IsBlockerActive)
            {
                StatusMessage = $"🛡️ Real-time blocker active! Files from {dirs.Length} director{(dirs.Length == 1 ? "y" : "ies")} will never appear in Recent. " +
                    $"(Cleaned {RecentBlockerService.Instance.BlockedCount} existing items)";
            }
            else
            {
                StatusMessage = "Error: Could not start the real-time blocker.";
            }

            ((RelayCommand)ToggleBlockerCommand).RaiseCanExecuteChanged();
        }

        private void CreateWindowsTask()
        {
            var dirs = TargetDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(d => d.Trim())
                                 .Where(d => !string.IsNullOrEmpty(d))
                                 .ToArray();

            if (dirs.Length == 0)
            {
                StatusMessage = "Error: Enter target directories first";
                return;
            }

            // Map UI frequency to schtasks frequency
            var schtasksFrequency = SelectedScheduleFrequency switch
            {
                "Once" => "DAILY",   // schtasks doesn't have "once" in the same way, default to daily
                "Hourly" => "HOURLY",
                "Daily" => "DAILY",
                "Weekly" => "WEEKLY",
                "Monthly" => "MONTHLY",
                "On Startup" => "ONLOGON",
                _ => "DAILY"
            };

            var result = MessageBox.Show(
                $"Create a Windows Scheduled Task to automatically clean recent items?\n\n" +
                $"Directories to block:\n" +
                $"{string.Join("\n", dirs.Select(d => "  • " + d))}\n\n" +
                $"Frequency: {SelectedScheduleFrequency}\n" +
                $"Include Subdirectories: {(IncludeSubDirs ? "Yes" : "No")}\n\n" +
                $"This runs even when PlatypusTools is closed.\n" +
                $"Requires administrator privileges.",
                "Create Windows Scheduled Task",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var (success, message) = RecentBlockerService.Instance.CreateWindowsScheduledTask(
                dirs, IncludeSubDirs, schtasksFrequency);

            WindowsTaskExists = RecentBlockerService.Instance.WindowsScheduledTaskExists();

            if (success)
                StatusMessage = $"✅ {message}";
            else
                StatusMessage = $"❌ {message}";
        }

        private void RemoveWindowsTask()
        {
            var result = MessageBox.Show(
                "Remove the Windows Scheduled Task for recent cleanup?\n\n" +
                "Files from blocked directories will no longer be automatically cleaned when PlatypusTools is closed.",
                "Remove Windows Scheduled Task",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var (success, message) = RecentBlockerService.Instance.RemoveWindowsScheduledTask();
            WindowsTaskExists = RecentBlockerService.Instance.WindowsScheduledTaskExists();
            StatusMessage = success ? $"✅ {message}" : $"❌ {message}";
        }
    }

    public class RecentMatchSelectable : BindableBase
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected 
        { 
            get => _isSelected; 
            set { _isSelected = value; RaisePropertyChanged(); } 
        }
    }
}