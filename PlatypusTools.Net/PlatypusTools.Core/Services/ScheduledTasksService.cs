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
    }

    /// <summary>
    /// Represents information about a scheduled task
    /// </summary>
    public class ScheduledTaskInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? NextRunTime { get; set; }
        public DateTime? LastRunTime { get; set; }
        public string? LastResult { get; set; }
        public string? Author { get; set; }
        public string? TaskToRun { get; set; }
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
            // 0: TaskName, 1: Next Run Time, 2: Status, 3: Logon Mode, 4: Last Run Time,
            // 5: Last Result, 6: Author, 7: Task To Run, 8: Start In, 9: Comment, etc.
            var fields = ParseCsvLine(line);
            if (fields.Count < 3) return null;

            var task = new ScheduledTaskInfo
            {
                Name = fields[0].Split('\\').Last(),
                Path = fields[0]
            };

            // Next run time (index 1)
            if (fields.Count > 1 && !string.IsNullOrWhiteSpace(fields[1]))
            {
                if (DateTime.TryParse(fields[1], out var nextRun))
                {
                    task.NextRunTime = nextRun;
                }
            }

            // Status (index 2)
            if (fields.Count > 2)
            {
                task.Status = fields[2];
            }

            // Last run time (index 4)
            if (fields.Count > 4 && !string.IsNullOrWhiteSpace(fields[4]))
            {
                if (DateTime.TryParse(fields[4], out var lastRun))
                {
                    task.LastRunTime = lastRun;
                }
            }

            // Last result (index 5)
            if (fields.Count > 5)
            {
                task.LastResult = fields[5];
            }

            // Author (index 6)
            if (fields.Count > 6)
            {
                task.Author = fields[6];
            }

            // Task to run (index 7)
            if (fields.Count > 7)
            {
                task.TaskToRun = fields[7];
            }

            return task;
        }

        private List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var inQuotes = false;
            var currentField = string.Empty;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(currentField.Trim());
                    currentField = string.Empty;
                }
                else
                {
                    currentField += c;
                }
            }

            fields.Add(currentField.Trim());
            return fields;
        }
    }
}
