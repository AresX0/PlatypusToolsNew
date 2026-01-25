using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class RegistryIssueViewModel : BindableBase
    {
        private string _keyPath = string.Empty;
        public string KeyPath { get => _keyPath; set => SetProperty(ref _keyPath, value); }

        private string _issueType = string.Empty;
        public string IssueType { get => _issueType; set => SetProperty(ref _issueType, value); }

        private string _description = string.Empty;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private string _severity = "Low";
        public string Severity { get => _severity; set => SetProperty(ref _severity, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        private bool _isFixed;
        public bool IsFixed { get => _isFixed; set => SetProperty(ref _isFixed, value); }
    }

    public class RegistryCleanerViewModel : BindableBase
    {
        private readonly RegistryCleanerService _registryCleanerService;

        public RegistryCleanerViewModel()
        {
            _registryCleanerService = new RegistryCleanerService();

            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !IsScanning);
            FixSelectedCommand = new RelayCommand(async _ => await FixSelectedAsync(), _ => Issues.Any(i => i.IsSelected && !i.IsFixed));
            FixAllCommand = new RelayCommand(async _ => await FixAllAsync(), _ => Issues.Any(i => !i.IsFixed));
            BackupCommand = new RelayCommand(async _ => await BackupAsync());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            DeselectAllCommand = new RelayCommand(_ => DeselectAll());
        }

        public ObservableCollection<RegistryIssueViewModel> Issues { get; } = new();

        private bool _isScanning;
        public bool IsScanning 
        { 
            get => _isScanning; 
            set 
            { 
                SetProperty(ref _isScanning, value); 
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            } 
        }

        private string _statusMessage = "Ready. Click Scan to find registry issues.";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _totalIssues;
        public int TotalIssues { get => _totalIssues; set => SetProperty(ref _totalIssues, value); }

        private int _fixedIssues;
        public int FixedIssues { get => _fixedIssues; set => SetProperty(ref _fixedIssues, value); }

        public ICommand ScanCommand { get; }
        public ICommand FixSelectedCommand { get; }
        public ICommand FixAllCommand { get; }
        public ICommand BackupCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }

        public async Task ScanAsync()
        {
            IsScanning = true;
            StatusMessage = "Scanning registry for issues...";
            Issues.Clear();
            FixedIssues = 0;

            try
            {
                var issues = await Task.Run(() => _registryCleanerService.ScanRegistry());
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var issue in issues)
                    {
                        Issues.Add(new RegistryIssueViewModel
                        {
                            KeyPath = issue.KeyPath,
                            IssueType = issue.Type,
                            Description = issue.Description,
                            Severity = issue.Severity.ToString(),
                            IsSelected = true
                        });
                    }
                });

                TotalIssues = Issues.Count;
                StatusMessage = $"Found {TotalIssues} registry issues";
                ((RelayCommand)FixSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)FixAllCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        public async Task FixSelectedAsync()
        {
            var selectedIssues = Issues.Where(i => i.IsSelected && !i.IsFixed).ToList();
            if (!selectedIssues.Any()) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to fix {selectedIssues.Count} selected registry issue(s)?",
                "Confirm Fix",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                StatusMessage = "Fixing selected issues...";
                int fixedCount = 0;

                foreach (var issue in selectedIssues)
                {
                    try
                    {
                        await Task.Run(() => _registryCleanerService.DeleteRegistryKey(issue.KeyPath));
                        issue.IsFixed = true;
                        fixedCount++;
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error fixing {issue.KeyPath}: {ex.Message}";
                    }
                }

                FixedIssues += fixedCount;
                StatusMessage = $"Fixed {fixedCount} issue(s). Total fixed: {FixedIssues}";
                ((RelayCommand)FixSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)FixAllCommand).RaiseCanExecuteChanged();
            }
        }

        public async Task FixAllAsync()
        {
            var unfixedIssues = Issues.Where(i => !i.IsFixed).ToList();
            if (!unfixedIssues.Any()) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to fix all {unfixedIssues.Count} registry issue(s)?",
                "Confirm Fix All",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                StatusMessage = "Fixing all issues...";
                int fixedCount = 0;

                foreach (var issue in unfixedIssues)
                {
                    try
                    {
                        await Task.Run(() => _registryCleanerService.DeleteRegistryKey(issue.KeyPath));
                        issue.IsFixed = true;
                        fixedCount++;
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error fixing {issue.KeyPath}: {ex.Message}";
                    }
                }

                FixedIssues += fixedCount;
                StatusMessage = $"Fixed {fixedCount} issue(s). Total fixed: {FixedIssues}";
                ((RelayCommand)FixAllCommand).RaiseCanExecuteChanged();
            }
        }

        public async Task BackupAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Registry files (*.reg)|*.reg|All files (*.*)|*.*",
                    FileName = $"RegistryBackup_{DateTime.Now:yyyyMMdd_HHmmss}.reg"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusMessage = "Creating backup...";
                    await Task.Run(() => _registryCleanerService.BackupRegistry(dialog.FileName));
                    StatusMessage = $"Backup created: {dialog.FileName}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Backup error: {ex.Message}";
            }
        }

        public void SelectAll()
        {
            foreach (var issue in Issues.Where(i => !i.IsFixed))
            {
                issue.IsSelected = true;
            }
            ((RelayCommand)FixSelectedCommand).RaiseCanExecuteChanged();
        }

        public void DeselectAll()
        {
            foreach (var issue in Issues)
            {
                issue.IsSelected = false;
            }
            ((RelayCommand)FixSelectedCommand).RaiseCanExecuteChanged();
        }
    }
}
