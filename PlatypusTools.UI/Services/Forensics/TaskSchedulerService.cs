using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Scheduled task definition for PlatypusTools operations.
    /// </summary>
    public class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ScheduledTaskType TaskType { get; set; }
        public bool IsEnabled { get; set; } = true;
        
        // Schedule settings
        public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
        public TimeSpan TimeOfDay { get; set; } = TimeSpan.FromHours(2); // 2:00 AM default
        public DayOfWeek? DayOfWeek { get; set; }
        public int? DayOfMonth { get; set; }
        public TimeSpan? Interval { get; set; } // For recurring tasks
        
        // Task-specific parameters
        public Dictionary<string, string> Parameters { get; set; } = new();
        
        // Execution tracking
        public DateTime? LastRun { get; set; }
        public DateTime? NextRun { get; set; }
        public TaskExecutionStatus LastStatus { get; set; } = TaskExecutionStatus.NotRun;
        public string? LastError { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
    }

    public enum ScheduledTaskType
    {
        DiskCleanup,
        TempFilesCleanup,
        RecycleBinCleanup,
        BrowserCacheCleanup,
        ForensicScan,
        YaraScan,
        BackupArtifacts,
        MemoryDump,
        RegistrySnapshot,
        IOCScan,
        Custom
    }

    public enum ScheduleFrequency
    {
        Once,
        Hourly,
        Daily,
        Weekly,
        Monthly,
        OnStartup,
        OnIdle
    }

    public enum TaskExecutionStatus
    {
        NotRun,
        Running,
        Completed,
        Failed,
        Cancelled,
        Skipped
    }

    /// <summary>
    /// Result of a scheduled task execution.
    /// </summary>
    public class TaskExecutionResult
    {
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public List<string> Logs { get; } = new();
    }

    /// <summary>
    /// Service for scheduling and executing PlatypusTools operations.
    /// Supports disk cleanup, forensic scans, backups, and custom tasks.
    /// </summary>
    public class TaskSchedulerService : ForensicOperationBase, IDisposable
    {
        private static readonly string TasksFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "scheduled_tasks.json");

        private readonly List<ScheduledTask> _tasks = new();
        private readonly Dictionary<string, CancellationTokenSource> _runningTasks = new();
        private Timer? _schedulerTimer;
        private bool _isRunning;

        public override string OperationName => "Task Scheduler";

        public IReadOnlyList<ScheduledTask> Tasks => _tasks.AsReadOnly();

        public event Action<ScheduledTask, TaskExecutionResult>? TaskCompleted;
        public event Action<ScheduledTask>? TaskStarted;

        #region Initialization

        public TaskSchedulerService()
        {
            LoadTasks();
        }

        /// <summary>
        /// Starts the scheduler timer.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _schedulerTimer = new Timer(CheckSchedule, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            Log("Task scheduler started");
        }

        /// <summary>
        /// Stops the scheduler timer.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _schedulerTimer?.Dispose();
            _schedulerTimer = null;
            Log("Task scheduler stopped");
        }

        #endregion

        #region Task Management

        /// <summary>
        /// Adds a new scheduled task.
        /// </summary>
        public void AddTask(ScheduledTask task)
        {
            task.NextRun = CalculateNextRun(task);
            _tasks.Add(task);
            SaveTasks();
            Log($"Added task: {task.Name}");
        }

        /// <summary>
        /// Updates an existing task.
        /// </summary>
        public void UpdateTask(ScheduledTask task)
        {
            var index = _tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0)
            {
                task.ModifiedAt = DateTime.Now;
                task.NextRun = CalculateNextRun(task);
                _tasks[index] = task;
                SaveTasks();
                Log($"Updated task: {task.Name}");
            }
        }

        /// <summary>
        /// Removes a scheduled task.
        /// </summary>
        public void RemoveTask(string taskId)
        {
            var task = _tasks.Find(t => t.Id == taskId);
            if (task != null)
            {
                _tasks.Remove(task);
                SaveTasks();
                Log($"Removed task: {task.Name}");
            }
        }

        /// <summary>
        /// Clears all tasks. Useful for testing.
        /// </summary>
        public void ClearTasks()
        {
            _tasks.Clear();
            SaveTasks();
            Log("Cleared all tasks");
        }

        /// <summary>
        /// Gets a task by ID.
        /// </summary>
        public ScheduledTask? GetTask(string taskId)
        {
            return _tasks.Find(t => t.Id == taskId);
        }

        /// <summary>
        /// Enables or disables a task.
        /// </summary>
        public void SetTaskEnabled(string taskId, bool enabled)
        {
            var task = GetTask(taskId);
            if (task != null)
            {
                task.IsEnabled = enabled;
                task.ModifiedAt = DateTime.Now;
                if (enabled)
                    task.NextRun = CalculateNextRun(task);
                SaveTasks();
            }
        }

        #endregion

        #region Execution

        /// <summary>
        /// Runs a task immediately.
        /// </summary>
        public async Task<TaskExecutionResult> RunTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            var task = GetTask(taskId);
            if (task == null)
            {
                return new TaskExecutionResult
                {
                    TaskId = taskId,
                    Success = false,
                    Message = "Task not found"
                };
            }

            return await ExecuteTaskAsync(task, cancellationToken);
        }

        /// <summary>
        /// Cancels a running task.
        /// </summary>
        public void CancelTask(string taskId)
        {
            if (_runningTasks.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                Log($"Cancelled task: {taskId}");
            }
        }

        private async Task<TaskExecutionResult> ExecuteTaskAsync(ScheduledTask task, CancellationToken cancellationToken)
        {
            var result = new TaskExecutionResult
            {
                TaskId = task.Id,
                StartTime = DateTime.Now
            };

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runningTasks[task.Id] = cts;

            try
            {
                task.LastStatus = TaskExecutionStatus.Running;
                TaskStarted?.Invoke(task);
                Log($"Executing task: {task.Name}");

                result.Logs.Add($"[{DateTime.Now:HH:mm:ss}] Starting {task.Name}");

                switch (task.TaskType)
                {
                    case ScheduledTaskType.DiskCleanup:
                        await ExecuteDiskCleanupAsync(task, result, cts.Token);
                        break;

                    case ScheduledTaskType.TempFilesCleanup:
                        await ExecuteTempCleanupAsync(task, result, cts.Token);
                        break;

                    case ScheduledTaskType.RecycleBinCleanup:
                        await ExecuteRecycleBinCleanupAsync(result, cts.Token);
                        break;

                    case ScheduledTaskType.BrowserCacheCleanup:
                        await ExecuteBrowserCacheCleanupAsync(result, cts.Token);
                        break;

                    case ScheduledTaskType.ForensicScan:
                        await ExecuteForensicScanAsync(task, result, cts.Token);
                        break;

                    case ScheduledTaskType.YaraScan:
                        await ExecuteYaraScanAsync(task, result, cts.Token);
                        break;

                    case ScheduledTaskType.BackupArtifacts:
                        await ExecuteBackupArtifactsAsync(task, result, cts.Token);
                        break;

                    case ScheduledTaskType.RegistrySnapshot:
                        await ExecuteRegistrySnapshotAsync(task, result, cts.Token);
                        break;

                    case ScheduledTaskType.IOCScan:
                        await ExecuteIOCScanAsync(task, result, cts.Token);
                        break;

                    default:
                        result.Logs.Add("Unknown task type");
                        break;
                }

                result.Success = true;
                result.Message = "Task completed successfully";
                task.LastStatus = TaskExecutionStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "Task was cancelled";
                task.LastStatus = TaskExecutionStatus.Cancelled;
                result.Logs.Add($"[{DateTime.Now:HH:mm:ss}] Task cancelled");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                task.LastStatus = TaskExecutionStatus.Failed;
                task.LastError = ex.Message;
                result.Logs.Add($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;
                task.LastRun = result.StartTime;
                task.NextRun = CalculateNextRun(task);
                _runningTasks.Remove(task.Id);
                SaveTasks();

                result.Logs.Add($"[{DateTime.Now:HH:mm:ss}] Completed in {result.Duration.TotalSeconds:F1}s");
                TaskCompleted?.Invoke(task, result);
            }

            return result;
        }

        #endregion

        #region Task Implementations

        private async Task ExecuteDiskCleanupAsync(ScheduledTask task, TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Running disk cleanup...");
            
            var targets = new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") };
            long totalFreed = 0;

            foreach (var target in targets)
            {
                if (Directory.Exists(target))
                {
                    var freed = await CleanDirectoryAsync(target, TimeSpan.FromDays(7), token);
                    totalFreed += freed;
                    result.Logs.Add($"  Cleaned {target}: {FormatBytes(freed)}");
                }
            }

            result.Logs.Add($"Total freed: {FormatBytes(totalFreed)}");
        }

        private async Task ExecuteTempCleanupAsync(ScheduledTask task, TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Cleaning temp files...");
            
            var tempPath = Path.GetTempPath();
            var freed = await CleanDirectoryAsync(tempPath, TimeSpan.FromDays(1), token);
            result.Logs.Add($"Freed: {FormatBytes(freed)}");
        }

        private Task ExecuteRecycleBinCleanupAsync(TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Emptying recycle bin...");
            
            try
            {
                // Use SHEmptyRecycleBin via shell
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c rd /s /q %systemdrive%\\$Recycle.Bin",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit();
                result.Logs.Add("Recycle bin emptied");
            }
            catch (Exception ex)
            {
                result.Logs.Add($"Warning: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private async Task ExecuteBrowserCacheCleanupAsync(TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Cleaning browser caches...");

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var cachePaths = new[]
            {
                Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                Path.Combine(userProfile, "AppData", "Local", "Mozilla", "Firefox", "Profiles")
            };

            long total = 0;
            foreach (var path in cachePaths)
            {
                if (Directory.Exists(path))
                {
                    var freed = await CleanDirectoryAsync(path, TimeSpan.Zero, token);
                    total += freed;
                    result.Logs.Add($"  Cleaned {Path.GetFileName(Path.GetDirectoryName(path) ?? path)}: {FormatBytes(freed)}");
                }
            }

            result.Logs.Add($"Total freed: {FormatBytes(total)}");
        }

        private Task ExecuteForensicScanAsync(ScheduledTask task, TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Running forensic scan...");
            
            task.Parameters.TryGetValue("ScanPath", out var scanPath);
            scanPath ??= Environment.GetFolderPath(Environment.SpecialFolder.System);

            result.Logs.Add($"Scan path: {scanPath}");
            result.Logs.Add("Forensic scan completed (placeholder)");
            
            return Task.CompletedTask;
        }

        private async Task ExecuteYaraScanAsync(ScheduledTask task, TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Running YARA scan...");

            task.Parameters.TryGetValue("TargetPath", out var targetPath);
            task.Parameters.TryGetValue("RulesPath", out var rulesPath);

            if (string.IsNullOrEmpty(targetPath))
            {
                result.Logs.Add("Warning: TargetPath not configured");
                return;
            }

            using var yaraService = ForensicsServiceFactory.CreateYaraService();
            if (!string.IsNullOrEmpty(rulesPath))
            {
                yaraService.RulesDirectory = rulesPath;
            }
            yaraService.TargetPath = targetPath;

            var scanResult = await yaraService.ScanAsync(token);
            result.Logs.Add($"YARA scan complete: {scanResult.Matches.Count} matches");
        }

        private Task ExecuteBackupArtifactsAsync(ScheduledTask task, TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Backing up artifacts...");

            task.Parameters.TryGetValue("SourcePath", out var sourcePath);
            task.Parameters.TryGetValue("DestPath", out var destPath);

            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destPath))
            {
                result.Logs.Add("Warning: SourcePath or DestPath not configured");
                return Task.CompletedTask;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupDir = Path.Combine(destPath, $"backup_{timestamp}");
            Directory.CreateDirectory(backupDir);

            // Copy files
            if (Directory.Exists(sourcePath))
            {
                foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(sourcePath, file);
                    var destFile = Path.Combine(backupDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    File.Copy(file, destFile, true);
                }
            }

            result.Logs.Add($"Backup created: {backupDir}");
            return Task.CompletedTask;
        }

        private Task ExecuteRegistrySnapshotAsync(ScheduledTask task, TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Creating registry snapshot...");
            
            task.Parameters.TryGetValue("OutputPath", out var outputPath);
            outputPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RegistrySnapshots");

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var snapshotFile = Path.Combine(outputPath, $"registry_snapshot_{timestamp}.reg");
            Directory.CreateDirectory(outputPath);

            // Export key registry hives
            var hives = new[] { "HKLM\\SOFTWARE", "HKCU\\SOFTWARE", "HKLM\\SYSTEM\\CurrentControlSet\\Services" };
            foreach (var hive in hives)
            {
                var hiveName = hive.Replace("\\", "_").Replace(":", "");
                var hiveFile = Path.Combine(outputPath, $"{hiveName}_{timestamp}.reg");
                
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"export \"{hive}\" \"{hiveFile}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit();
                result.Logs.Add($"  Exported: {hive}");
            }

            result.Logs.Add($"Snapshots saved to: {outputPath}");
            return Task.CompletedTask;
        }

        private Task ExecuteIOCScanAsync(ScheduledTask task, TaskExecutionResult result, CancellationToken token)
        {
            result.Logs.Add("Running IOC scan...");
            result.Logs.Add("IOC scan requires IOCScannerService (placeholder)");
            return Task.CompletedTask;
        }

        #endregion

        #region Helpers

        private void CheckSchedule(object? state)
        {
            if (!_isRunning) return;

            var now = DateTime.Now;
            foreach (var task in _tasks.ToArray())
            {
                if (!task.IsEnabled || task.NextRun == null) continue;
                if (task.LastStatus == TaskExecutionStatus.Running) continue;

                if (now >= task.NextRun)
                {
                    _ = ExecuteTaskAsync(task, CancellationToken.None);
                }
            }
        }

        private DateTime? CalculateNextRun(ScheduledTask task)
        {
            if (!task.IsEnabled) return null;

            var now = DateTime.Now;
            var todayAtTime = now.Date.Add(task.TimeOfDay);

            return task.Frequency switch
            {
                ScheduleFrequency.Once => task.NextRun ?? todayAtTime,
                ScheduleFrequency.Hourly => now.AddHours(1),
                ScheduleFrequency.Daily => todayAtTime > now ? todayAtTime : todayAtTime.AddDays(1),
                ScheduleFrequency.Weekly => GetNextWeeklyRun(task, now),
                ScheduleFrequency.Monthly => GetNextMonthlyRun(task, now),
                ScheduleFrequency.OnStartup => null, // Handled specially
                ScheduleFrequency.OnIdle => null, // Handled specially
                _ => null
            };
        }

        private DateTime GetNextWeeklyRun(ScheduledTask task, DateTime now)
        {
            var target = task.DayOfWeek ?? DayOfWeek.Sunday;
            var daysUntilTarget = ((int)target - (int)now.DayOfWeek + 7) % 7;
            if (daysUntilTarget == 0 && now.TimeOfDay >= task.TimeOfDay)
                daysUntilTarget = 7;
            return now.Date.AddDays(daysUntilTarget).Add(task.TimeOfDay);
        }

        private DateTime GetNextMonthlyRun(ScheduledTask task, DateTime now)
        {
            var day = task.DayOfMonth ?? 1;
            var thisMonth = new DateTime(now.Year, now.Month, Math.Min(day, DateTime.DaysInMonth(now.Year, now.Month))).Add(task.TimeOfDay);
            if (thisMonth > now) return thisMonth;
            
            var nextMonth = now.AddMonths(1);
            return new DateTime(nextMonth.Year, nextMonth.Month, Math.Min(day, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month))).Add(task.TimeOfDay);
        }

        private async Task<long> CleanDirectoryAsync(string path, TimeSpan olderThan, CancellationToken token)
        {
            long freed = 0;
            var cutoff = DateTime.Now - olderThan;

            await Task.Run(() =>
            {
                try
                {
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var fi = new FileInfo(file);
                            if (olderThan == TimeSpan.Zero || fi.LastWriteTime < cutoff)
                            {
                                freed += fi.Length;
                                fi.Delete();
                            }
                        }
                        catch { /* Skip locked files */ }
                    }
                }
                catch { /* Skip inaccessible dirs */ }
            }, token);

            return freed;
        }

        private void LoadTasks()
        {
            try
            {
                if (File.Exists(TasksFilePath))
                {
                    var json = File.ReadAllText(TasksFilePath);
                    var tasks = JsonSerializer.Deserialize<List<ScheduledTask>>(json);
                    if (tasks != null)
                    {
                        _tasks.Clear();
                        _tasks.AddRange(tasks);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to load tasks: {ex.Message}");
            }
        }

        private void SaveTasks()
        {
            try
            {
                var dir = Path.GetDirectoryName(TasksFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_tasks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(TasksFilePath, json);
            }
            catch (Exception ex)
            {
                LogError($"Failed to save tasks: {ex.Message}");
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {suffixes[order]}";
        }

        public new void Dispose()
        {
            Stop();
            foreach (var cts in _runningTasks.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _runningTasks.Clear();
            base.Dispose();
        }

        #endregion
    }
}
