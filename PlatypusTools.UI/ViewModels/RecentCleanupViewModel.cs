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
                            Target = r.Target,
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
                        Application.Current.Dispatcher.Invoke(() => Results.Add(selectable));
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
        /// </summary>
        public async Task ScanAllRecent()
        {
            try
            {
                StatusMessage = "Scanning all recent items...";
                Results.Clear();

                await Task.Run(() =>
                {
                    // Scan Windows Recent folder
                    var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                    if (Directory.Exists(recentPath))
                    {
                        foreach (var file in Directory.GetFiles(recentPath, "*.*", SearchOption.AllDirectories))
                        {
                            string target = "";
                            string type = "Recent";
                            
                            // Get the file name as target info (simplified - full shortcut resolution would require COM interop)
                            if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                            {
                                target = Path.GetFileNameWithoutExtension(file);
                            }
                            else
                            {
                                target = file;
                            }
                            
                            var selectable = new RecentMatchSelectable
                            {
                                Type = type,
                                Path = file,
                                Target = target,
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
                            Application.Current.Dispatcher.Invoke(() => Results.Add(selectable));
                        }
                    }
                    
                    // Scan Quick Access (Pinned items)
                    var quickAccessPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                        @"Microsoft\Windows\Recent\AutomaticDestinations");
                    if (Directory.Exists(quickAccessPath))
                    {
                        foreach (var file in Directory.GetFiles(quickAccessPath, "*.*"))
                        {
                            var selectable = new RecentMatchSelectable
                            {
                                Type = "QuickAccess",
                                Path = file,
                                Target = Path.GetFileNameWithoutExtension(file),
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
                            Application.Current.Dispatcher.Invoke(() => Results.Add(selectable));
                        }
                    }
                    
                    // Scan Start Menu Recent folder
                    var startMenuRecentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    // Note: We don't scan Start Menu programs - just listing this as a potential location
                });

                RaisePropertyChanged(nameof(SelectedCount));
                StatusMessage = $"Found {Results.Count} recent items";
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
                    Application.Current.Dispatcher.Invoke(() => Results.Remove(item));
                }

                StatusMessage = $"Removed {removed} shortcuts successfully";
                RaisePropertyChanged(nameof(SelectedCount));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error removing shortcuts: {ex.Message}";
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