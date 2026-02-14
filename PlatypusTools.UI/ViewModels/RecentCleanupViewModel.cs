using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;

namespace PlatypusTools.UI.ViewModels
{
    public class RecentCleanupViewModel : BindableBase
    {
        public ObservableCollection<RecentMatchSelectable> Results { get; } = new ObservableCollection<RecentMatchSelectable>();
        public ObservableCollection<string> ExclusionDirs { get; } = new ObservableCollection<string>();

        private string _targetDirs = "";
        public string TargetDirs 
        { 
            get => _targetDirs; 
            set 
            { 
                _targetDirs = value; 
                RaisePropertyChanged(); 
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            } 
        }

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

        private string _statusMessage = "Ready. Enter target directories or click 'Scan All Recent' to see all recent items.";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; RaisePropertyChanged(); } }

        public RecentCleanupViewModel()
        {
            ScanCommand = new RelayCommand(async _ => await Scan(), _ => !string.IsNullOrWhiteSpace(TargetDirs));
            ScanAllRecentCommand = new RelayCommand(async _ => await ScanAllRecent());
            BrowseCommand = new RelayCommand(_ => Browse());
            ClearTargetDirsCommand = new RelayCommand(_ => { TargetDirs = ""; });
            AddExclusionCommand = new RelayCommand(_ => AddExclusion());
            RemoveExclusionCommand = new RelayCommand(_ => RemoveExclusion(), _ => !string.IsNullOrEmpty(SelectedExclusionDir));
            RemoveSelectedCommand = new RelayCommand(async _ => await RemoveSelected(), _ => Results.Any(r => r.IsSelected));
            SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => Results.Any());
            SelectNoneCommand = new RelayCommand(_ => SelectNone(), _ => Results.Any(r => r.IsSelected));
            ClearAllRecentCommand = new RelayCommand(async _ => await ClearAllRecent());
            RefreshExplorerCommand = new RelayCommand(_ => RefreshExplorer());
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
                if (string.IsNullOrWhiteSpace(TargetDirs))
                    TargetDirs = dialog.SelectedPath;
                else
                    TargetDirs += ";" + dialog.SelectedPath;
                
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
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

                await Task.Run(() =>
                {
                    foreach (var item in selectedItems)
                    {
                        try
                        {
                            if (File.Exists(item.Path))
                            {
                                File.Delete(item.Path);
                                removed++;
                            }
                        }
                        catch
                        {
                            // Skip files that can't be deleted
                        }
                    }
                });

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