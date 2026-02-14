using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using static PlatypusTools.Core.Services.AppTaskSchedulerService;

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
        private readonly AppTaskSchedulerService? _appTaskScheduler;

        public ScheduledTasksViewModel()
        {
            _scheduledTasksService = ServiceContainer.GetService<ScheduledTasksService>() ?? new ScheduledTasksService();
            _appTaskScheduler = ServiceContainer.GetService<AppTaskSchedulerService>();

            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsRefreshing);
            EnableCommand = new RelayCommand(async _ => await EnableTaskAsync(), _ => SelectedTask != null && !SelectedTask.IsEnabled);
            DisableCommand = new RelayCommand(async _ => await DisableTaskAsync(), _ => SelectedTask != null && SelectedTask.IsEnabled);
            DeleteCommand = new RelayCommand(async _ => await DeleteTaskAsync(), _ => SelectedTask != null);
            CreateCommand = new RelayCommand(_ => CreateTask());
            RunCommand = new RelayCommand(async _ => await RunTaskAsync(), _ => SelectedTask != null);

            // App internal task commands
            AddAppTaskCommand = new RelayCommand(_ => AddAppTask());
            RemoveAppTaskCommand = new RelayCommand(async _ => await RemoveAppTaskAsync(), _ => SelectedAppTask != null);
            RunAppTaskNowCommand = new RelayCommand(async _ => await RunAppTaskNowAsync(), _ => SelectedAppTask != null);
            ToggleAppTaskCommand = new RelayCommand(_ => ToggleAppTask(), _ => SelectedAppTask != null);
            RefreshAppTasksCommand = new RelayCommand(_ => RefreshAppTasks());

            // Wire up app scheduler events
            if (_appTaskScheduler != null)
            {
                _appTaskScheduler.TaskStarted += (s, e) =>
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        AppTaskStatus = $"Running: {e.Task.Name}...");

                _appTaskScheduler.TaskCompleted += (s, e) =>
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        AppTaskStatus = $"Completed: {e.Task.Name} - {e.Message}";
                        RefreshAppTasks();
                    });

                _appTaskScheduler.TaskFailed += (s, e) =>
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        AppTaskStatus = $"Failed: {e.Task.Name} - {e.ErrorMessage}";
                        RefreshAppTasks();
                    });
            }
            
            // Don't auto-refresh on construction to prevent blocking UI thread
            // User needs to click Refresh button
            StatusMessage = "Click Refresh to load scheduled tasks";
            RefreshAppTasks();
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

        // App internal task commands
        public ICommand AddAppTaskCommand { get; }
        public ICommand RemoveAppTaskCommand { get; }
        public ICommand RunAppTaskNowCommand { get; }
        public ICommand ToggleAppTaskCommand { get; }
        public ICommand RefreshAppTasksCommand { get; }

        // App internal tasks collection
        public ObservableCollection<AppTaskViewModel> AppTasks { get; } = new();

        private AppTaskViewModel? _selectedAppTask;
        public AppTaskViewModel? SelectedAppTask
        {
            get => _selectedAppTask;
            set
            {
                SetProperty(ref _selectedAppTask, value);
                ((RelayCommand)RemoveAppTaskCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RunAppTaskNowCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ToggleAppTaskCommand).RaiseCanExecuteChanged();
            }
        }

        private string _appTaskStatus = "Ready";
        public string AppTaskStatus { get => _appTaskStatus; set => SetProperty(ref _appTaskStatus, value); }

        private int _totalAppTasks;
        public int TotalAppTasks { get => _totalAppTasks; set => SetProperty(ref _totalAppTasks, value); }

        public async Task RefreshAsync()
        {
            IsRefreshing = true;
            StatusMessage = "Loading scheduled tasks...";
            Tasks.Clear();

            try
            {
                var tasks = await Task.Run(() => _scheduledTasksService.GetScheduledTasks());
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
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
            try
            {
                // Open Windows Task Scheduler's create task wizard
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskschd.msc",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                StatusMessage = "Task Scheduler opened. Create a new task using the Action menu.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening Task Scheduler: {ex.Message}";
            }
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

        #region App Internal Task Methods

        private void AddAppTask()
        {
            if (_appTaskScheduler == null)
            {
                AppTaskStatus = "App task scheduler not available";
                return;
            }

            // Show a selection of task templates
            var taskTypes = new[]
            {
                TaskType.DiskCleanup,
                TaskType.PrivacyClean,
                TaskType.BackupSettings,
                TaskType.LibrarySync,
                TaskType.CacheClean,
                TaskType.SystemAudit,
                TaskType.DuplicateScan,
                TaskType.MetadataUpdate
            };

            // Find the first task type that doesn't already exist
            TaskType? typeToAdd = null;
            foreach (var type in taskTypes)
            {
                if (!_appTaskScheduler.GetTasksByType(type).Any())
                {
                    typeToAdd = type;
                    break;
                }
            }

            if (!typeToAdd.HasValue)
            {
                AppTaskStatus = "All task templates have been added. Remove a task to add another.";
                return;
            }

            var newTask = _appTaskScheduler.CreateTaskFromTemplate(typeToAdd.Value);
            _appTaskScheduler.AddTask(newTask);
            AppTaskStatus = $"Added task: {newTask.Name}";
            RefreshAppTasks();
        }

        private async Task RemoveAppTaskAsync()
        {
            if (SelectedAppTask == null || _appTaskScheduler == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Remove app task '{SelectedAppTask.Name}'?",
                "Confirm Remove",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _appTaskScheduler.RemoveTask(SelectedAppTask.Id);
                AppTaskStatus = $"Removed task: {SelectedAppTask.Name}";
                RefreshAppTasks();
            }

            await Task.CompletedTask;
        }

        private async Task RunAppTaskNowAsync()
        {
            if (SelectedAppTask == null || _appTaskScheduler == null) return;

            AppTaskStatus = $"Running: {SelectedAppTask.Name}...";
            await _appTaskScheduler.RunTaskNowAsync(SelectedAppTask.Id);
        }

        private void ToggleAppTask()
        {
            if (SelectedAppTask == null || _appTaskScheduler == null) return;

            _appTaskScheduler.SetTaskEnabled(SelectedAppTask.Id, !SelectedAppTask.IsEnabled);
            AppTaskStatus = SelectedAppTask.IsEnabled
                ? $"Disabled: {SelectedAppTask.Name}"
                : $"Enabled: {SelectedAppTask.Name}";
            RefreshAppTasks();
        }

        private void RefreshAppTasks()
        {
            if (_appTaskScheduler == null) return;

            AppTasks.Clear();
            foreach (var task in _appTaskScheduler.GetTasks())
            {
                AppTasks.Add(new AppTaskViewModel
                {
                    Id = task.Id,
                    Name = task.Name,
                    Description = task.Description,
                    Type = task.Type.ToString(),
                    Schedule = task.Schedule.ToString(),
                    IsEnabled = task.IsEnabled,
                    Status = task.Status.ToString(),
                    NextRun = task.NextRun,
                    LastRun = task.LastRun,
                    LastDuration = task.LastDuration?.ToString(@"hh\:mm\:ss") ?? "N/A",
                    LastError = task.LastError ?? ""
                });
            }
            TotalAppTasks = AppTasks.Count;
        }

        #endregion
    }

    /// <summary>
    /// ViewModel for an internal PlatypusTools scheduled task.
    /// </summary>
    public class AppTaskViewModel : BindableBase
    {
        private string _id = string.Empty;
        public string Id { get => _id; set => SetProperty(ref _id, value); }

        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _description = string.Empty;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private string _type = string.Empty;
        public string Type { get => _type; set => SetProperty(ref _type, value); }

        private string _schedule = string.Empty;
        public string Schedule { get => _schedule; set => SetProperty(ref _schedule, value); }

        private bool _isEnabled = true;
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }

        private string _status = string.Empty;
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        private DateTime? _nextRun;
        public DateTime? NextRun { get => _nextRun; set => SetProperty(ref _nextRun, value); }

        private DateTime? _lastRun;
        public DateTime? LastRun { get => _lastRun; set => SetProperty(ref _lastRun, value); }

        private string _lastDuration = "N/A";
        public string LastDuration { get => _lastDuration; set => SetProperty(ref _lastDuration, value); }

        private string _lastError = string.Empty;
        public string LastError { get => _lastError; set => SetProperty(ref _lastError, value); }
    }
}
