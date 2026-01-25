using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class ProcessInfoViewModel : BindableBase
    {
        private int _processId;
        public int ProcessId { get => _processId; set => SetProperty(ref _processId, value); }

        private string _processName = string.Empty;
        public string ProcessName { get => _processName; set => SetProperty(ref _processName, value); }

        private long _memoryUsage;
        public long MemoryUsage { get => _memoryUsage; set => SetProperty(ref _memoryUsage, value); }

        private string _memoryUsageDisplay = string.Empty;
        public string MemoryUsageDisplay { get => _memoryUsageDisplay; set => SetProperty(ref _memoryUsageDisplay, value); }

        private double _cpuUsage;
        public double CPUUsage { get => _cpuUsage; set => SetProperty(ref _cpuUsage, value); }

        private int _threadCount;
        public int ThreadCount { get => _threadCount; set => SetProperty(ref _threadCount, value); }

        private string _userName = string.Empty;
        public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

        private DateTime _startTime;
        public DateTime StartTime { get => _startTime; set => SetProperty(ref _startTime, value); }

        private string _filePath = string.Empty;
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
    }

    public class ProcessManagerViewModel : BindableBase
    {
        private readonly ProcessManagerService _processManagerService;

        public ProcessManagerViewModel()
        {
            _processManagerService = Services.ServiceLocator.ProcessManager;

            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsRefreshing);
            KillProcessCommand = new RelayCommand(async _ => await KillProcessAsync(), _ => SelectedProcess != null);
            LoadDetailsCommand = new RelayCommand(async item => await LoadDetailsAsync(item as ProcessInfoViewModel));
            FilterCommand = new RelayCommand(_ => ApplyFilter());
            
            // Don't auto-refresh on construction to prevent blocking UI thread
            StatusMessage = "Click Refresh to load processes";
        }

        public ObservableCollection<ProcessInfoViewModel> Processes { get; } = new();
        public ObservableCollection<ProcessInfoViewModel> SelectedProcesses { get; } = new();
        private ObservableCollection<ProcessInfoViewModel> _allProcesses = new();

        private ProcessInfoViewModel? _selectedProcess;
        public ProcessInfoViewModel? SelectedProcess 
        { 
            get => _selectedProcess; 
            set 
            { 
                SetProperty(ref _selectedProcess, value); 
                ((RelayCommand)KillProcessCommand).RaiseCanExecuteChanged();
            } 
        }

        private bool _isRefreshing;
        public bool IsRefreshing 
        { 
            get => _isRefreshing; 
            set 
            { 
                SetProperty(ref _isRefreshing, value); 
                ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
            } 
        }

        private string _filterText = string.Empty;
        public string FilterText 
        { 
            get => _filterText; 
            set 
            { 
                SetProperty(ref _filterText, value); 
                ApplyFilter();
            } 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _totalProcesses;
        public int TotalProcesses { get => _totalProcesses; set => SetProperty(ref _totalProcesses, value); }

        public ICommand RefreshCommand { get; }
        public ICommand KillProcessCommand { get; }
        public ICommand LoadDetailsCommand { get; }
        public ICommand FilterCommand { get; }

        public async Task RefreshAsync()
        {
            IsRefreshing = true;
            StatusMessage = "Loading processes...";

            try
            {
                var processes = await Task.Run(() => _processManagerService.GetProcesses());
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _allProcesses.Clear();
                    foreach (var proc in processes)
                    {
                        var procVM = new ProcessInfoViewModel
                        {
                            ProcessId = proc.Id,
                            ProcessName = proc.Name,
                            MemoryUsage = proc.MemoryUsage,
                            MemoryUsageDisplay = FormatMemory(proc.MemoryUsage),
                            CPUUsage = proc.CpuUsage,
                            ThreadCount = proc.ThreadCount,
                            UserName = proc.UserName,
                            StartTime = proc.StartTime,
                            FilePath = proc.Path
                        };
                        _allProcesses.Add(procVM);
                    }
                    ApplyFilter();
                });

                TotalProcesses = _allProcesses.Count;
                StatusMessage = $"Loaded {TotalProcesses} processes";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        public async Task KillProcessAsync()
        {
            if (SelectedProcess == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to kill process '{SelectedProcess.ProcessName}' (PID: {SelectedProcess.ProcessId})?",
                "Confirm Kill Process",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    await Task.Run(() => _processManagerService.KillProcess(SelectedProcess.ProcessId));
                    StatusMessage = $"Killed process {SelectedProcess.ProcessName}";
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error killing process: {ex.Message}";
                }
            }
        }

        public async Task KillProcessesAsync(System.Collections.Generic.List<ProcessInfoViewModel> processes)
        {
            if (processes == null || processes.Count == 0) return;

            var processNames = string.Join(", ", processes.Take(5).Select(p => p.ProcessName));
            if (processes.Count > 5)
                processNames += $" and {processes.Count - 5} more";

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to kill {processes.Count} process(es)?\n\n{processNames}",
                "Confirm Kill Processes",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                int killedCount = 0;
                int failedCount = 0;

                foreach (var process in processes)
                {
                    try
                    {
                        await Task.Run(() => _processManagerService.KillProcess(process.ProcessId));
                        killedCount++;
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                StatusMessage = $"Killed {killedCount} process(es)" + (failedCount > 0 ? $", {failedCount} failed" : "");
                await RefreshAsync();
            }
        }

        private async Task LoadDetailsAsync(ProcessInfoViewModel? process)
        {
            if (process == null) return;

            try
            {
                var details = await _processManagerService.GetProcessDetails(process.ProcessId);
                if (details != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        process.UserName = details.UserName;
                        process.FilePath = details.Path;
                        process.ThreadCount = details.ThreadCount;
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading details: {ex.Message}";
            }
        }

        private void ApplyFilter()
        {
            Processes.Clear();
            var filtered = string.IsNullOrWhiteSpace(FilterText)
                ? _allProcesses
                : _allProcesses.Where(p => p.ProcessName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

            foreach (var proc in filtered)
            {
                Processes.Add(proc);
            }
        }

        private string FormatMemory(long bytes)
        {
            return $"{bytes / 1024.0 / 1024.0:0.##} MB";
        }
    }
}
