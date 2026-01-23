using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for managing Windows scheduled tasks
    /// </summary>
    public interface IScheduledTasksService
    {
        Task<List<ScheduledTaskInfo>> GetScheduledTasks();
        Task<bool> EnableTask(string taskName);
        Task<bool> DisableTask(string taskName);
        Task<bool> DeleteTask(string taskName);
        Task<bool> CreateTask(string taskName, string command, string schedule);
        Task<bool> RunTask(string taskName);
    }

    /// <summary>
    /// Represents information about a scheduled task
    /// </summary>
    public class ScheduledTaskInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string State => Status; // Alias for Status
        public DateTime? NextRunTime { get; set; }
        public DateTime? LastRunTime { get; set; }
        public string? LastResult { get; set; }
        public string LastTaskResult => LastResult ?? "N/A";
        public string? Author { get; set; }
        public string? TaskToRun { get; set; }
        public string TriggerType { get; set; } = string.Empty;
        public bool IsEnabled => Status.Equals("Ready", StringComparison.OrdinalIgnoreCase) || 
                                 Status.Equals("Running", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Implementation of scheduled tasks service
    /// </summary>
    public class ScheduledTasksService : IScheduledTasksService
    {
        /// <summary>
        /// Gets all scheduled tasks on the system
        /// </summary>
        /// <returns>List of scheduled tasks</returns>
        public async Task<List<ScheduledTaskInfo>> GetScheduledTasks()
        {
            return await Task.Run(() =>
            {
                var tasks = new List<ScheduledTaskInfo>();

                try
                {
                    // Use schtasks to list all tasks
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = "/Query /FO CSV /V",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        throw new InvalidOperationException("Failed to start schtasks process");
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("Failed to query scheduled tasks");
                    }

                    // Parse CSV output
                    var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length < 2) return tasks;

                    // Skip header line
                    for (int i = 1; i < lines.Length; i++)
                    {
                        try
                        {
                            var task = ParseTaskLine(lines[i]);
                            if (task != null)
                            {
                                tasks.Add(task);
                            }
                        }
                        catch
                        {
                            // Skip malformed lines
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to get scheduled tasks: {ex.Message}", ex);
                }

                return tasks;
            });
        }

        /// <summary>
        /// Enables a scheduled task
        /// </summary>
        /// <param name="taskName">Task name or path</param>
        /// <returns>True if successful</returns>
        public async Task<bool> EnableTask(string taskName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Change /TN \"{taskName}\" /Enable",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to enable task '{taskName}': {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Disables a scheduled task
        /// </summary>
        /// <param name="taskName">Task name or path</param>
        /// <returns>True if successful</returns>
        public async Task<bool> DisableTask(string taskName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Change /TN \"{taskName}\" /Disable",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to disable task '{taskName}': {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Deletes a scheduled task
        /// </summary>
        /// <param name="taskName">Task name or path</param>
        /// <returns>True if successful</returns>
        public async Task<bool> DeleteTask(string taskName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Delete /TN \"{taskName}\" /F",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to delete task '{taskName}': {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Creates a new scheduled task
        /// </summary>
        /// <param name="taskName">Name for the task</param>
        /// <param name="command">Command to execute</param>
        /// <param name="schedule">Schedule string (e.g., DAILY, WEEKLY, ONCE)</param>
        /// <returns>True if successful</returns>
        public async Task<bool> CreateTask(string taskName, string command, string schedule)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Parse schedule to determine the type
                    var scheduleType = schedule.ToUpperInvariant() switch
                    {
                        "DAILY" => "DAILY",
                        "WEEKLY" => "WEEKLY",
                        "MONTHLY" => "MONTHLY",
                        "ONCE" => "ONCE",
                        _ => "DAILY"
                    };

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Create /TN \"{taskName}\" /TR \"{command}\" /SC {scheduleType} /F",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to create task '{taskName}': {ex.Message}", ex);
                }
            });
        }

        private ScheduledTaskInfo? ParseTaskLine(string line)
        {
            // CSV format from schtasks /Query /FO CSV /V:
            // 0: HostName, 1: TaskName, 2: Next Run Time, 3: Status, 4: Logon Mode, 5: Last Run Time,
            // 6: Last Result, 7: Author, 8: Task To Run, 9: Start In, 10: Comment, 
            // 11: Scheduled Task State, 12: Idle Time, 13: Power Management, 14: Run As User, 15: Delete Task If Not Rescheduled
            // 16: Stop Task If Runs X Hours, 17: Schedule, 18: Schedule Type, 19: Start Time, etc.
            var fields = ParseCsvLine(line);
            if (fields.Count < 12) return null; // Need at least through Scheduled Task State

            // Skip header line (check if first field is "HostName")
            if (fields[0].Equals("HostName", StringComparison.OrdinalIgnoreCase)) return null;

            // Get task name from index 1, strip leading backslash
            var taskName = fields[1].TrimStart('\\');
            var taskDisplayName = taskName.Split('\\').Last();

            var task = new ScheduledTaskInfo
            {
                Name = taskDisplayName,
                Path = fields[1]
            };

            // Next run time (index 2)
            if (!string.IsNullOrWhiteSpace(fields[2]) && !fields[2].Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParse(fields[2], out var nextRun) && nextRun.Year > 1900)
                {
                    task.NextRunTime = nextRun;
                }
            }

            // Status (index 3) - e.g., "Ready", "Running", "Disabled"
            task.Status = fields[3];

            // Last run time (index 5)
            if (fields.Count > 5 && !string.IsNullOrWhiteSpace(fields[5]) && !fields[5].Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParse(fields[5], out var lastRun) && lastRun.Year > 1900)
                {
                    task.LastRunTime = lastRun;
                }
            }

            // Last result (index 6) - typically error codes like 0, 267011, etc.
            if (fields.Count > 6)
            {
                task.LastResult = fields[6];
            }

            // Author (index 7)
            if (fields.Count > 7)
            {
                task.Author = fields[7];
            }

            // Task to run (index 8)
            if (fields.Count > 8)
            {
                task.TaskToRun = fields[8];
            }

            // Schedule Type (index 18) - e.g., "Daily", "On demand only", "At log on"
            if (fields.Count > 18 && !string.IsNullOrWhiteSpace(fields[18]))
            {
                task.TriggerType = fields[18];
            }
            else if (fields.Count > 17 && !string.IsNullOrWhiteSpace(fields[17]))
            {
                task.TriggerType = fields[17];
            }
            else
            {
                task.TriggerType = "Unknown";
            }

            return task;
        }

        private List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var inQuotes = false;
            var currentField = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    // Handle escaped quotes (two consecutive quotes inside quoted string)
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // Skip the next quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            fields.Add(currentField.ToString());
            return fields;
        }

        /// <summary>
        /// Runs a scheduled task immediately
        /// </summary>
        /// <param name="taskName">Task name or path</param>
        /// <returns>True if the task was started successfully</returns>
        public async Task<bool> RunTask(string taskName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Run /TN \"{taskName}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to run task '{taskName}': {ex.Message}", ex);
                }
            });
        }
    }
}
