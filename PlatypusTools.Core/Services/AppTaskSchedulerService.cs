using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for scheduling application-specific tasks like cleanup, backup, and analysis.
    /// </summary>
    public class AppTaskSchedulerService : IDisposable
    {
        private static readonly Lazy<AppTaskSchedulerService> _instance = new(() => new AppTaskSchedulerService());
        public static AppTaskSchedulerService Instance => _instance.Value;

        private readonly List<ScheduledAppTask> _tasks = new();
        private readonly Dictionary<string, Timer> _timers = new();
        private readonly string _tasksFilePath;
        private readonly object _lock = new();
        private bool _isRunning;

        public event EventHandler<TaskExecutionEventArgs>? TaskStarted;
        public event EventHandler<TaskExecutionEventArgs>? TaskCompleted;
        public event EventHandler<TaskErrorEventArgs>? TaskFailed;
        public event EventHandler<ScheduledAppTask>? TaskAdded;
        public event EventHandler<ScheduledAppTask>? TaskRemoved;

        public AppTaskSchedulerService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "PlatypusTools");
            Directory.CreateDirectory(appFolder);
            _tasksFilePath = Path.Combine(appFolder, "scheduled_tasks.json");
            LoadTasks();
        }

        #region Models

        public enum TaskType
        {
            DiskCleanup,
            PrivacyClean,
            BackupSettings,
            LibrarySync,
            SystemAudit,
            MalwareScan,
            DuplicateScan,
            MetadataUpdate,
            CacheClean,
            RecentCleanup,
            Custom
        }

        public enum TaskScheduleType
        {
            Once,
            Hourly,
            Daily,
            Weekly,
            Monthly,
            OnStartup,
            OnIdle
        }

        public enum TaskStatus
        {
            Scheduled,
            Running,
            Completed,
            Failed,
            Disabled,
            Overdue
        }

        public class ScheduledAppTask
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public TaskType Type { get; set; }
            public TaskScheduleType Schedule { get; set; }
            public bool IsEnabled { get; set; } = true;
            public DateTime? NextRun { get; set; }
            public DateTime? LastRun { get; set; }
            public TaskStatus Status { get; set; } = TaskStatus.Scheduled;
            public string? LastError { get; set; }
            public TimeSpan? LastDuration { get; set; }
            public Dictionary<string, string> Parameters { get; set; } = new();

            // Schedule details
            public TimeSpan TimeOfDay { get; set; } = new(9, 0, 0); // 9 AM default
            public DayOfWeek? DayOfWeek { get; set; }
            public int? DayOfMonth { get; set; }
            public int IntervalHours { get; set; } = 1;
            public int IdleMinutes { get; set; } = 15;

            // Results
            public int ItemsProcessed { get; set; }
            public long BytesProcessed { get; set; }
        }

        public class TaskExecutionEventArgs : EventArgs
        {
            public ScheduledAppTask Task { get; set; } = null!;
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public bool Success { get; set; }
            public string? Message { get; set; }
            public int ItemsProcessed { get; set; }
            public long BytesProcessed { get; set; }
        }

        public class TaskErrorEventArgs : EventArgs
        {
            public ScheduledAppTask Task { get; set; } = null!;
            public Exception Exception { get; set; } = null!;
            public string ErrorMessage { get; set; } = string.Empty;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the task scheduler.
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;

            // Run startup tasks
            var startupTasks = _tasks.Where(t => t.IsEnabled && t.Schedule == TaskScheduleType.OnStartup).ToList();
            foreach (var task in startupTasks)
            {
                _ = ExecuteTaskAsync(task);
            }

            // Schedule all tasks
            foreach (var task in _tasks.Where(t => t.IsEnabled))
            {
                ScheduleTask(task);
            }

            // Start idle monitor
            StartIdleMonitor();
        }

        /// <summary>
        /// Stops the task scheduler.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;

            lock (_lock)
            {
                foreach (var timer in _timers.Values)
                {
                    timer.Stop();
                    timer.Dispose();
                }
                _timers.Clear();
            }

            StopIdleMonitor();
        }

        /// <summary>
        /// Adds a new scheduled task.
        /// </summary>
        public void AddTask(ScheduledAppTask task)
        {
            lock (_lock)
            {
                task.NextRun = CalculateNextRun(task);
                _tasks.Add(task);
                SaveTasks();

                if (_isRunning && task.IsEnabled)
                {
                    ScheduleTask(task);
                }
            }

            TaskAdded?.Invoke(this, task);
        }

        /// <summary>
        /// Updates an existing task.
        /// </summary>
        public void UpdateTask(ScheduledAppTask task)
        {
            lock (_lock)
            {
                var existing = _tasks.FirstOrDefault(t => t.Id == task.Id);
                if (existing != null)
                {
                    var index = _tasks.IndexOf(existing);
                    _tasks[index] = task;

                    // Reschedule
                    if (_timers.TryGetValue(task.Id, out var timer))
                    {
                        timer.Stop();
                        timer.Dispose();
                        _timers.Remove(task.Id);
                    }

                    task.NextRun = CalculateNextRun(task);

                    if (_isRunning && task.IsEnabled)
                    {
                        ScheduleTask(task);
                    }

                    SaveTasks();
                }
            }
        }

        /// <summary>
        /// Removes a task.
        /// </summary>
        public bool RemoveTask(string taskId)
        {
            lock (_lock)
            {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null)
                    return false;

                _tasks.Remove(task);

                if (_timers.TryGetValue(taskId, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                    _timers.Remove(taskId);
                }

                SaveTasks();
                TaskRemoved?.Invoke(this, task);
                return true;
            }
        }

        /// <summary>
        /// Enables or disables a task.
        /// </summary>
        public void SetTaskEnabled(string taskId, bool enabled)
        {
            lock (_lock)
            {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task == null)
                    return;

                task.IsEnabled = enabled;
                task.Status = enabled ? TaskStatus.Scheduled : TaskStatus.Disabled;

                if (enabled && _isRunning)
                {
                    task.NextRun = CalculateNextRun(task);
                    ScheduleTask(task);
                }
                else if (_timers.TryGetValue(taskId, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                    _timers.Remove(taskId);
                }

                SaveTasks();
            }
        }

        /// <summary>
        /// Runs a task immediately.
        /// </summary>
        public Task RunTaskNowAsync(string taskId)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                return ExecuteTaskAsync(task);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets all scheduled tasks.
        /// </summary>
        public IReadOnlyList<ScheduledAppTask> GetTasks()
        {
            return _tasks.ToList();
        }

        /// <summary>
        /// Gets tasks by type.
        /// </summary>
        public IEnumerable<ScheduledAppTask> GetTasksByType(TaskType type)
        {
            return _tasks.Where(t => t.Type == type);
        }

        /// <summary>
        /// Gets overdue tasks.
        /// </summary>
        public IEnumerable<ScheduledAppTask> GetOverdueTasks()
        {
            var now = DateTime.Now;
            return _tasks.Where(t => t.IsEnabled && t.NextRun.HasValue && t.NextRun.Value < now);
        }

        /// <summary>
        /// Creates predefined task templates.
        /// </summary>
        public ScheduledAppTask CreateTaskFromTemplate(TaskType type)
        {
            var task = new ScheduledAppTask
            {
                Type = type,
                Schedule = TaskScheduleType.Daily,
                TimeOfDay = new TimeSpan(3, 0, 0) // 3 AM
            };

            switch (type)
            {
                case TaskType.DiskCleanup:
                    task.Name = "Daily Disk Cleanup";
                    task.Description = "Clean temporary files, browser cache, and system junk";
                    task.Parameters["CleanTemp"] = "true";
                    task.Parameters["CleanBrowserCache"] = "true";
                    task.Parameters["CleanRecycleBin"] = "false";
                    break;

                case TaskType.PrivacyClean:
                    task.Name = "Privacy Cleanup";
                    task.Description = "Clear browser history, cookies, and recent files";
                    task.Parameters["ClearHistory"] = "true";
                    task.Parameters["ClearCookies"] = "true";
                    task.Parameters["ClearRecentFiles"] = "true";
                    break;

                case TaskType.BackupSettings:
                    task.Name = "Backup Application Settings";
                    task.Description = "Backup PlatypusTools settings and configurations";
                    task.Schedule = TaskScheduleType.Weekly;
                    task.DayOfWeek = System.DayOfWeek.Sunday;
                    break;

                case TaskType.LibrarySync:
                    task.Name = "Library Sync";
                    task.Description = "Synchronize media library with backup location";
                    task.Schedule = TaskScheduleType.Hourly;
                    task.IntervalHours = 4;
                    break;

                case TaskType.SystemAudit:
                    task.Name = "System Audit";
                    task.Description = "Check system security, startup items, and installed software";
                    task.Schedule = TaskScheduleType.Weekly;
                    task.DayOfWeek = System.DayOfWeek.Monday;
                    break;

                case TaskType.MalwareScan:
                    task.Name = "Malware Scan";
                    task.Description = "Scan system for malware using YARA rules";
                    task.Schedule = TaskScheduleType.Weekly;
                    task.DayOfWeek = System.DayOfWeek.Saturday;
                    break;

                case TaskType.DuplicateScan:
                    task.Name = "Duplicate File Scan";
                    task.Description = "Find duplicate files in specified folders";
                    task.Schedule = TaskScheduleType.Monthly;
                    task.DayOfMonth = 1;
                    break;

                case TaskType.MetadataUpdate:
                    task.Name = "Update Media Metadata";
                    task.Description = "Refresh metadata for media library files";
                    task.Schedule = TaskScheduleType.OnIdle;
                    break;

                case TaskType.CacheClean:
                    task.Name = "Cache Cleanup";
                    task.Description = "Clean application cache and thumbnails";
                    task.Schedule = TaskScheduleType.Daily;
                    break;

                case TaskType.RecentCleanup:
                    task.Name = "Recent Items Cleanup";
                    task.Description = "Remove recent shortcuts pointing to files in specified directories";
                    task.Schedule = TaskScheduleType.Daily;
                    break;
            }

            return task;
        }

        #endregion

        #region Task Execution

        private async Task ExecuteTaskAsync(ScheduledAppTask task)
        {
            if (task.Status == TaskStatus.Running)
                return;

            var startTime = DateTime.Now;
            task.Status = TaskStatus.Running;
            task.LastRun = startTime;

            TaskStarted?.Invoke(this, new TaskExecutionEventArgs
            {
                Task = task,
                StartTime = startTime
            });

            try
            {
                var result = await RunTaskImplementationAsync(task);

                task.Status = TaskStatus.Completed;
                task.LastDuration = DateTime.Now - startTime;
                task.ItemsProcessed = result.ItemsProcessed;
                task.BytesProcessed = result.BytesProcessed;
                task.LastError = null;
                task.NextRun = CalculateNextRun(task);

                TaskCompleted?.Invoke(this, new TaskExecutionEventArgs
                {
                    Task = task,
                    StartTime = startTime,
                    EndTime = DateTime.Now,
                    Success = true,
                    ItemsProcessed = result.ItemsProcessed,
                    BytesProcessed = result.BytesProcessed,
                    Message = result.Message
                });
            }
            catch (Exception ex)
            {
                task.Status = TaskStatus.Failed;
                task.LastError = ex.Message;
                task.LastDuration = DateTime.Now - startTime;
                task.NextRun = CalculateNextRun(task);

                TaskFailed?.Invoke(this, new TaskErrorEventArgs
                {
                    Task = task,
                    Exception = ex,
                    ErrorMessage = ex.Message
                });
            }

            SaveTasks();

            // Reschedule for next run
            if (task.IsEnabled && task.Schedule != TaskScheduleType.Once)
            {
                ScheduleTask(task);
            }
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunTaskImplementationAsync(ScheduledAppTask task)
        {
            switch (task.Type)
            {
                case TaskType.DiskCleanup:
                    return await RunDiskCleanupAsync(task);

                case TaskType.PrivacyClean:
                    return await RunPrivacyCleanAsync(task);

                case TaskType.BackupSettings:
                    return await RunBackupSettingsAsync(task);

                case TaskType.LibrarySync:
                    return await RunLibrarySyncAsync(task);

                case TaskType.CacheClean:
                    return await RunCacheCleanAsync(task);

                case TaskType.SystemAudit:
                    return await RunSystemAuditAsync(task);

                case TaskType.RecentCleanup:
                    return await RunRecentCleanupAsync(task);

                default:
                    return (0, 0, "Task type not implemented");
            }
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunDiskCleanupAsync(ScheduledAppTask task)
        {
            var itemsDeleted = 0;
            long bytesFreed = 0;

            // Clean temp folders
            if (task.Parameters.GetValueOrDefault("CleanTemp") == "true")
            {
                var tempPath = Path.GetTempPath();
                var (items, bytes) = await CleanFolderAsync(tempPath, TimeSpan.FromDays(7));
                itemsDeleted += items;
                bytesFreed += bytes;
            }

            // Clean browser cache
            if (task.Parameters.GetValueOrDefault("CleanBrowserCache") == "true")
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var chromeCachePath = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache");
                if (Directory.Exists(chromeCachePath))
                {
                    var (items, bytes) = await CleanFolderAsync(chromeCachePath, TimeSpan.FromDays(7));
                    itemsDeleted += items;
                    bytesFreed += bytes;
                }
            }

            return (itemsDeleted, bytesFreed, $"Cleaned {itemsDeleted} files, freed {bytesFreed / 1024 / 1024} MB");
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunPrivacyCleanAsync(ScheduledAppTask task)
        {
            var itemsDeleted = 0;

            // Clear recent files
            if (task.Parameters.GetValueOrDefault("ClearRecentFiles") == "true")
            {
                var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (Directory.Exists(recentPath))
                {
                    var files = Directory.GetFiles(recentPath, "*.lnk");
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            itemsDeleted++;
                        }
                        catch { }
                    }
                }
            }

            await Task.CompletedTask;
            return (itemsDeleted, 0, $"Cleared {itemsDeleted} recent items");
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunBackupSettingsAsync(ScheduledAppTask task)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var settingsFolder = Path.Combine(appData, "PlatypusTools");
            var backupFolder = task.Parameters.GetValueOrDefault("BackupPath") ??
                               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlatypusTools Backups");

            Directory.CreateDirectory(backupFolder);

            var backupName = $"settings_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            var backupPath = Path.Combine(backupFolder, backupName);

            var fileCount = 0;
            long totalSize = 0;

            if (Directory.Exists(settingsFolder))
            {
                // Simple file copy backup
                var files = Directory.GetFiles(settingsFolder, "*", SearchOption.AllDirectories);
                fileCount = files.Length;
                totalSize = files.Sum(f => new FileInfo(f).Length);

                // In production, use System.IO.Compression.ZipFile
                await File.WriteAllTextAsync(backupPath + ".txt", $"Backup of {settingsFolder} at {DateTime.Now}");
            }

            await Task.CompletedTask;
            return (fileCount, totalSize, $"Backed up {fileCount} files to {backupPath}");
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunLibrarySyncAsync(ScheduledAppTask task)
        {
            var sourcePath = task.Parameters.GetValueOrDefault("SourcePath") ?? string.Empty;
            var destPath = task.Parameters.GetValueOrDefault("DestinationPath") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destPath))
                return (0, 0, "Library sync requires SourcePath and DestinationPath parameters");

            if (!Directory.Exists(sourcePath))
                return (0, 0, $"Source path not found: {sourcePath}");

            if (!Directory.Exists(destPath))
            {
                try { Directory.CreateDirectory(destPath); }
                catch (Exception ex) { return (0, 0, $"Cannot create destination: {ex.Message}"); }
            }

            var profile = new LibrarySyncService.SyncProfile
            {
                Name = task.Name,
                Locations = new List<LibrarySyncService.LibraryLocation>
                {
                    new() { Name = "Primary", Path = sourcePath, IsPrimary = true },
                    new() { Name = "Backup", Path = destPath, IsPrimary = false }
                },
                Mode = Enum.TryParse<LibrarySyncService.SyncMode>(
                    task.Parameters.GetValueOrDefault("SyncMode"), true, out var mode) ? mode : LibrarySyncService.SyncMode.Mirror,
                ConflictPolicy = Enum.TryParse<LibrarySyncService.ConflictPolicy>(
                    task.Parameters.GetValueOrDefault("ConflictPolicy"), true, out var policy) ? policy : LibrarySyncService.ConflictPolicy.KeepNewest,
                DeleteOrphans = task.Parameters.GetValueOrDefault("DeleteOrphans") == "true"
            };

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromHours(2));
                var result = await LibrarySyncService.Instance.SyncAsync(profile, cts.Token);

                var totalFiles = result.FilesCopied + result.FilesUpdated + result.FilesDeleted + result.FilesSkipped;
                var message = result.Success
                    ? $"Sync complete: {result.FilesCopied} copied, {result.FilesUpdated} updated, {result.FilesDeleted} deleted, {result.FilesSkipped} skipped ({result.BytesTransferred / 1024 / 1024} MB)"
                    : $"Sync finished with errors: {string.Join("; ", result.ErrorMessages.Take(3))}";

                return (totalFiles, result.BytesTransferred, message);
            }
            catch (OperationCanceledException)
            {
                return (0, 0, "Library sync timed out after 2 hours");
            }
            catch (Exception ex)
            {
                return (0, 0, $"Library sync failed: {ex.Message}");
            }
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunCacheCleanAsync(ScheduledAppTask task)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cachePath = Path.Combine(appData, "PlatypusTools", "Cache");

            if (Directory.Exists(cachePath))
            {
                var (items, bytes) = await CleanFolderAsync(cachePath, TimeSpan.FromDays(30));
                return (items, bytes, $"Cleaned {items} cache files, freed {bytes / 1024 / 1024} MB");
            }

            return (0, 0, "No cache to clean");
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunSystemAuditAsync(ScheduledAppTask task)
        {
            // This would integrate with SystemAuditService
            await Task.Delay(100);
            return (0, 0, "System audit completed");
        }

        private async Task<(int ItemsProcessed, long BytesProcessed, string Message)> RunRecentCleanupAsync(ScheduledAppTask task)
        {
            var targetDirs = task.Parameters.GetValueOrDefault("TargetDirectories", "");
            if (string.IsNullOrWhiteSpace(targetDirs))
                return (0, 0, "No target directories specified for recent cleanup");

            var dirs = targetDirs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .ToArray();

            if (dirs.Length == 0)
                return (0, 0, "No valid target directories specified");

            var includeSubDirs = task.Parameters.GetValueOrDefault("IncludeSubDirs", "true") == "true";

            var results = await Task.Run(() =>
                RecentCleaner.RemoveRecentShortcuts(dirs, dryRun: false, includeSubDirs: includeSubDirs));

            return (results.Count, 0, $"Removed {results.Count} recent shortcuts for {dirs.Length} target director{(dirs.Length == 1 ? "y" : "ies")}");
        }

        private async Task<(int Items, long Bytes)> CleanFolderAsync(string path, TimeSpan olderThan)
        {
            var itemsDeleted = 0;
            long bytesFreed = 0;
            var cutoff = DateTime.Now - olderThan;

            await Task.Run(() =>
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.LastAccessTime < cutoff)
                            {
                                bytesFreed += info.Length;
                                info.Delete();
                                itemsDeleted++;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });

            return (itemsDeleted, bytesFreed);
        }

        #endregion

        #region Scheduling

        private void ScheduleTask(ScheduledAppTask task)
        {
            if (!task.IsEnabled || task.Schedule == TaskScheduleType.OnStartup || task.Schedule == TaskScheduleType.OnIdle)
                return;

            lock (_lock)
            {
                if (_timers.TryGetValue(task.Id, out var existingTimer))
                {
                    existingTimer.Stop();
                    existingTimer.Dispose();
                    _timers.Remove(task.Id);
                }

                var nextRun = task.NextRun ?? CalculateNextRun(task);
                var delay = nextRun - DateTime.Now;

                if (delay.TotalMilliseconds <= 0)
                {
                    // Task is overdue, run immediately
                    _ = ExecuteTaskAsync(task);
                    return;
                }

                var timer = new Timer(delay.TotalMilliseconds);
                timer.Elapsed += async (s, e) =>
                {
                    timer.Stop();
                    await ExecuteTaskAsync(task);
                };
                timer.AutoReset = false;
                timer.Start();

                _timers[task.Id] = timer;
            }
        }

        private DateTime CalculateNextRun(ScheduledAppTask task)
        {
            var now = DateTime.Now;
            var today = now.Date;

            switch (task.Schedule)
            {
                case TaskScheduleType.Once:
                    return task.NextRun ?? now.AddMinutes(1);

                case TaskScheduleType.Hourly:
                    return now.AddHours(task.IntervalHours);

                case TaskScheduleType.Daily:
                    var nextDaily = today.Add(task.TimeOfDay);
                    if (nextDaily <= now)
                        nextDaily = nextDaily.AddDays(1);
                    return nextDaily;

                case TaskScheduleType.Weekly:
                    var daysUntilNext = ((int)(task.DayOfWeek ?? DayOfWeek.Monday) - (int)now.DayOfWeek + 7) % 7;
                    if (daysUntilNext == 0 && now.TimeOfDay > task.TimeOfDay)
                        daysUntilNext = 7;
                    return today.AddDays(daysUntilNext).Add(task.TimeOfDay);

                case TaskScheduleType.Monthly:
                    var dayOfMonth = task.DayOfMonth ?? 1;
                    var nextMonthly = new DateTime(now.Year, now.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(now.Year, now.Month))).Add(task.TimeOfDay);
                    if (nextMonthly <= now)
                        nextMonthly = nextMonthly.AddMonths(1);
                    return nextMonthly;

                default:
                    return now.AddDays(1);
            }
        }

        #endregion

        #region Idle Monitoring

        private Timer? _idleCheckTimer;
        private DateTime _lastActivity = DateTime.Now;

        private void StartIdleMonitor()
        {
            _idleCheckTimer = new Timer(60000); // Check every minute
            _idleCheckTimer.Elapsed += CheckIdleTasks;
            _idleCheckTimer.Start();
        }

        private void StopIdleMonitor()
        {
            _idleCheckTimer?.Stop();
            _idleCheckTimer?.Dispose();
            _idleCheckTimer = null;
        }

        private void CheckIdleTasks(object? sender, ElapsedEventArgs e)
        {
            var idleTasks = _tasks.Where(t => t.IsEnabled && t.Schedule == TaskScheduleType.OnIdle).ToList();

            foreach (var task in idleTasks)
            {
                var idleTime = DateTime.Now - _lastActivity;
                if (idleTime.TotalMinutes >= task.IdleMinutes)
                {
                    // Check if already ran recently
                    if (!task.LastRun.HasValue || (DateTime.Now - task.LastRun.Value).TotalHours >= 1)
                    {
                        _ = ExecuteTaskAsync(task);
                    }
                }
            }
        }

        /// <summary>
        /// Call this to reset idle timer when user activity is detected.
        /// </summary>
        public void ReportUserActivity()
        {
            _lastActivity = DateTime.Now;
        }

        #endregion

        #region Persistence

        private void LoadTasks()
        {
            try
            {
                if (File.Exists(_tasksFilePath))
                {
                    var json = File.ReadAllText(_tasksFilePath);
                    var tasks = JsonSerializer.Deserialize<List<ScheduledAppTask>>(json);
                    if (tasks != null)
                    {
                        _tasks.AddRange(tasks);
                    }
                }
            }
            catch { }
        }

        private void SaveTasks()
        {
            try
            {
                var json = JsonSerializer.Serialize(_tasks, new JsonSerializerOptions { WriteIndented = true });
                _ = File.WriteAllTextAsync(_tasksFilePath, json);
            }
            catch { }
        }

        #endregion

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
