using PlatypusTools.Core.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;

namespace PlatypusTools.UI.ViewModels
{
    public class RecentCleanupViewModel : BindableBase
    {
        public ObservableCollection<RecentMatch> Results { get; } = new ObservableCollection<RecentMatch>();

        private string _targetDirs = "";
        public string TargetDirs { get => _targetDirs; set { _targetDirs = value; RaisePropertyChanged(); } }

        private bool _dryRun = false;
        public bool DryRun { get => _dryRun; set { _dryRun = value; RaisePropertyChanged(); } }

        public ICommand ScanCommand { get; }
        public ICommand BrowseCommand { get; }

        private string _statusMessage = "Ready. Enter target directories or click Browse.";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; RaisePropertyChanged(); } }

        public RecentCleanupViewModel()
        {
            ScanCommand = new RelayCommand(async _ => await Scan(), _ => !string.IsNullOrWhiteSpace(TargetDirs));
            BrowseCommand = new RelayCommand(_ => Browse());
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

                var results = await Task.Run(() => RecentCleaner.RemoveRecentShortcuts(dirs, dryRun: DryRun));
                
                foreach (var r in results)
                {
                    Application.Current.Dispatcher.Invoke(() => Results.Add(r));
                }

                StatusMessage = DryRun 
                    ? $"Found {Results.Count} matching shortcuts (DRY RUN - no files deleted)"
                    : $"Processed {Results.Count} shortcuts";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning: {ex.Message}";
                System.Windows.MessageBox.Show($"Error scanning Recent folder:\n{ex.Message}", 
                    "Scan Error", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}