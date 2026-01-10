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

        private bool _dryRun = true;
        public bool DryRun { get => _dryRun; set { _dryRun = value; RaisePropertyChanged(); } }

        public ICommand ScanCommand { get; }

        public RecentCleanupViewModel()
        {
            ScanCommand = new RelayCommand(async _ => await Scan());
        }

        public async Task Scan()
        {
            Results.Clear();
            var dirs = string.IsNullOrWhiteSpace(TargetDirs) ? new string[0] : TargetDirs.Split(';');
            var results = await Task.Run(() => RecentCleaner.RemoveRecentShortcuts(dirs, dryRun: DryRun));
            
            foreach (var r in results)
            {
                Application.Current.Dispatcher.Invoke(() => Results.Add(r));
            }
        }
    }
}