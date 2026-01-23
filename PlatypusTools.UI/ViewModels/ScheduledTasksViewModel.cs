using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class ScheduledTaskViewModel : BindableBase
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _path = string.Empty;
        public string Path { get => _path; set => SetProperty(ref _path, value); }

        private string _status = string.Empty;
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        private bool _isEnabled;
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }

        private DateTime _lastRunTime;
        public DateTime LastRunTime { get => _lastRunTime; set => SetProperty(ref _lastRunTime, value); }

        private DateTime _nextRunTime;
        public DateTime NextRunTime { get => _nextRunTime; set => SetProperty(ref _nextRunTime, value); }

        private string _trigger = string.Empty;
        public string Trigger { get => _trigger; set => SetProperty(ref _trigger, value); }

        private string _author = string.Empty;
        public string Author { get => _author; set => SetProperty(ref _author, value); }

        private string _description = string.Empty;
        public string Description { get => _description; set => SetProperty(ref _description, value); }
    }

    public class ScheduledTasksViewModel : BindableBase
    {
        private readonly ScheduledTasksService _scheduledTasksService;

        public ScheduledTasksViewModel()
        {
            _scheduledTasksService = Services.ServiceLocator.ScheduledTasks;

            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsRefreshing);
            EnableCommand = new RelayCommand(async _ => await EnableTaskAsync(), _ => SelectedTask != null && !SelectedTask.IsEnabled);
            DisableCommand = new RelayCommand(async _ => await DisableTaskAsync(), _ => SelectedTask != null && SelectedTask.IsEnabled);
            DeleteCommand = new RelayCommand(async _ => await DeleteTaskAsync(), _ => SelectedTask != null);
            CreateCommand = new RelayCommand(_ => CreateTask());
            RunCommand = new RelayCommand(async _ => await RunTaskAsync(), _ => SelectedTask != null);
            
            // Don't auto-refresh on construction to prevent blocking UI thread
            // User needs to click Refresh button
            StatusMessage = "Click Refresh to load scheduled tasks";
        }

        public ObservableCollection<ScheduledTaskViewModel> Tasks { get; } = new();

        private ScheduledTaskViewModel? _selectedTask;
        public ScheduledTaskViewModel? SelectedTask 
        { 
            get => _selectedTask; 
            set 
            { 
                SetProperty(ref _selectedTask, value); 
                ((RelayCommand)EnableCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisableCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RunCommand).RaiseCanExecuteChanged();
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

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _totalTasks;
        public int TotalTasks { get => _totalTasks; set => SetProperty(ref _totalTasks, value); }

        private int _enabledTasks;
        public int EnabledTasks { get => _enabledTasks; set => SetProperty(ref _enabledTasks, value); }

        public ICommand RefreshCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand DisableCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CreateCommand { get; }
        public ICommand RunCommand { get; }

        public async Task RefreshAsync()
        {
            IsRefreshing = true;
            StatusMessage = "Loading scheduled tasks...";
            Tasks.Clear();

            try
            {
                var tasks = await Task.Run(() => _scheduledTasksService.GetScheduledTasks());
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    int enabledCount = 0;
                    foreach (var task in tasks)
                    {
                        var taskVM = new ScheduledTaskViewModel
                        {
                            Name = task.Name,
                            Path = task.Path,
                            Status = task.Status,
                            IsEnabled = task.IsEnabled,
                            LastRunTime = task.LastRunTime ?? DateTime.MinValue,
                            NextRunTime = task.NextRunTime ?? DateTime.MinValue,
                            Trigger = task.TaskToRun ?? "N/A",
                            Author = task.Author ?? "Unknown",
                            Description = task.LastResult ?? "N/A"
                        };
                        Tasks.Add(taskVM);
                        if (task.IsEnabled) enabledCount++;
                    }
                    EnabledTasks = enabledCount;
                });

                TotalTasks = Tasks.Count;
                StatusMessage = $"Loaded {TotalTasks} tasks ({EnabledTasks} enabled)";
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

        public async Task EnableTaskAsync()
        {
            if (SelectedTask == null) return;

            try
            {
                await Task.Run(() => _scheduledTasksService.EnableTask(SelectedTask.Path));
                SelectedTask.IsEnabled = true;
                SelectedTask.Status = "Ready";
                EnabledTasks++;
                StatusMessage = $"Enabled task: {SelectedTask.Name}";
                ((RelayCommand)EnableCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisableCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error enabling task: {ex.Message}";
            }
        }

        public async Task DisableTaskAsync()
        {
            if (SelectedTask == null) return;

            try
            {
                await Task.Run(() => _scheduledTasksService.DisableTask(SelectedTask.Path));
                SelectedTask.IsEnabled = false;
                SelectedTask.Status = "Disabled";
                EnabledTasks--;
                StatusMessage = $"Disabled task: {SelectedTask.Name}";
                ((RelayCommand)EnableCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisableCommand).RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error disabling task: {ex.Message}";
            }
        }

        public async Task DeleteTaskAsync()
        {
            if (SelectedTask == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete task '{SelectedTask.Name}'?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    await Task.Run(() => _scheduledTasksService.DeleteTask(SelectedTask.Path));
                    StatusMessage = $"Deleted task: {SelectedTask.Name}";
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error deleting task: {ex.Message}";
                }
            }
        }

        private void CreateTask()
        {
            StatusMessage = "Task creation dialog not implemented yet";
        }

        public async Task RunTaskAsync()
        {
            if (SelectedTask == null) return;

            try
            {
                StatusMessage = $"Running task: {SelectedTask.Name}...";
                var success = await _scheduledTasksService.RunTask(SelectedTask.Path);
                
                if (success)
                {
                    StatusMessage = $"✅ Started task: {SelectedTask.Name}";
                    // Refresh after a short delay to show updated status
                    await Task.Delay(1000);
                    await RefreshAsync();
                }
                else
                {
                    StatusMessage = $"❌ Failed to start task: {SelectedTask.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error running task: {ex.Message}";
            }
        }
    }
}
