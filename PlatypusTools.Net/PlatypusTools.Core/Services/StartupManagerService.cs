using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PlatypusTools.Core.Services
{
    public class StartupManagerService
    {
        private static readonly string[] RegistryRunKeys = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        public List<StartupItem> GetStartupItems()
        {
            var items = new List<StartupItem>();
            
            // Get from registry (Current User)
            foreach (var keyPath in RegistryRunKeys)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(keyPath);
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                            items.Add(new StartupItem
                            {
                                Name = valueName,
                                Command = value,
                                Location = $"HKCU\\{keyPath}",
                                IsEnabled = true,
                                Type = keyPath.Contains("RunOnce") ? "RunOnce" : "Registry"
                            });
                        }
                    }
                }
                catch { }
            }

            // Get from registry (Local Machine)
            foreach (var keyPath in RegistryRunKeys)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                            items.Add(new StartupItem
                            {
                                Name = valueName,
                                Command = value,
                                Location = $"HKLM\\{keyPath}",
                                IsEnabled = true,
                                Type = keyPath.Contains("RunOnce") ? "RunOnce" : "Registry"
                            });
                        }
                    }
                }
                catch { }
            }

            // Get from Startup folder (Current User)
            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    foreach (var file in Directory.GetFiles(startupFolder, "*.*"))
                    {
                        items.Add(new StartupItem
                        {
                            Name = Path.GetFileName(file),
                            Command = file,
                            Location = startupFolder,
                            IsEnabled = true,
                            Type = "Startup Folder"
                        });
                    }
                }
            }
            catch { }

            // Get from Startup folder (All Users)
            try
            {
                var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                if (Directory.Exists(commonStartup))
                {
                    foreach (var file in Directory.GetFiles(commonStartup, "*.*"))
                    {
                        items.Add(new StartupItem
                        {
                            Name = Path.GetFileName(file),
                            Command = file,
                            Location = commonStartup,
                            IsEnabled = true,
                            Type = "Startup Folder (All Users)"
                        });
                    }
                }
            }
            catch { }

            // Get from Task Scheduler startup tasks
            try
            {
                var scheduledItems = GetScheduledStartupTasks();
                items.AddRange(scheduledItems);
            }
            catch { }

            return items.OrderBy(i => i.Name).ToList();
        }

        private List<StartupItem> GetScheduledStartupTasks()
        {
            var items = new List<StartupItem>();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = "/Query /FO CSV /V",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        return items; // Return empty list on failure
                    }

                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    
                    // Skip header line if present
                    foreach (var line in lines)
                    {
                        try
                        {
                            // Skip header line
                            if (line.StartsWith("\"TaskName\"") || line.StartsWith("TaskName") || 
                                line.StartsWith("HostName") || line.StartsWith("\"HostName\""))
                                continue;

                            // Only process task lines that indicate they're running or ready
                            if (!line.Contains("Ready") && !line.Contains("Running") && 
                                !line.Contains("READY") && !line.Contains("RUNNING"))
                                continue;

                            var parts = SplitCsvLine(line);
                            if (parts.Count > 1)
                            {
                                var taskName = parts[0].Trim('"');
                                
                                // Filter out system tasks
                                if (taskName.Contains("\\Microsoft\\Windows\\"))
                                    continue;

                                // Get task command if available (usually at index 8 in verbose output)
                                var taskCommand = parts.Count > 8 ? parts[8].Trim('"') : "";

                                items.Add(new StartupItem
                                {
                                    Name = taskName,
                                    Command = taskCommand,
                                    Location = "Task Scheduler",
                                    IsEnabled = line.Contains("READY") || line.Contains("RUNNING"),
                                    Type = "Scheduled Task"
                                });
                            }
                        }
                        catch
                        {
                            // Skip problematic lines
                            continue;
                        }
                    }
                }
            }
            catch
            {
                // Return empty list on any error
            }

            return items;
        }

        private List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result;
        }

        public bool DisableStartupItem(StartupItem item)
        {
            try
            {
                if (item.Type == "Registry" || item.Type == "RunOnce")
                {
                    // Registry items
                    var location = item.Location;
                    if (location.StartsWith("HKCU\\"))
                    {
                        var keyPath = location.Substring(5);
                        using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
                        key?.DeleteValue(item.Name, false);
                        return true;
                    }
                    else if (location.StartsWith("HKLM\\"))
                    {
                        var keyPath = location.Substring(5);
                        using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                        key?.DeleteValue(item.Name, false);
                        return true;
                    }
                }
                else if (item.Type.Contains("Startup Folder"))
                {
                    // Rename file to disable it
                    if (File.Exists(item.Command))
                    {
                        var disabledPath = item.Command + ".disabled";
                        if (File.Exists(disabledPath))
                            File.Delete(disabledPath);
                        File.Move(item.Command, disabledPath);
                        return true;
                    }
                }
                else if (item.Type == "Scheduled Task")
                {
                    // Disable scheduled task
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Change /TN \"{item.Name}\" /DISABLE",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = System.Diagnostics.Process.Start(psi);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
            }
            catch { }

            return false;
        }

        public bool EnableStartupItem(StartupItem item, string value)
        {
            try
            {
                if (item.Type == "Registry" || item.Type == "RunOnce")
                {
                    var location = item.Location;
                    if (location.StartsWith("HKCU\\"))
                    {
                        var keyPath = location.Substring(5);
                        using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
                        key?.SetValue(item.Name, value);
                        return true;
                    }
                    else if (location.StartsWith("HKLM\\"))
                    {
                        var keyPath = location.Substring(5);
                        using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                        key?.SetValue(item.Name, value);
                        return true;
                    }
                }
                else if (item.Type.Contains("Startup Folder"))
                {
                    // Rename file to enable it
                    var disabledPath = item.Command + ".disabled";
                    if (File.Exists(disabledPath))
                    {
                        if (File.Exists(item.Command))
                            File.Delete(item.Command);
                        File.Move(disabledPath, item.Command);
                        return true;
                    }
                }
                else if (item.Type == "Scheduled Task")
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Change /TN \"{item.Name}\" /ENABLE",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = System.Diagnostics.Process.Start(psi);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
            }
            catch { }

            return false;
        }

        public bool DeleteStartupItem(StartupItem item)
        {
            try
            {
                if (item.Type == "Registry" || item.Type == "RunOnce")
                {
                    return DisableStartupItem(item); // Same as disable for registry
                }
                else if (item.Type.Contains("Startup Folder"))
                {
                    if (File.Exists(item.Command))
                    {
                        File.Delete(item.Command);
                        return true;
                    }
                    var disabledPath = item.Command + ".disabled";
                    if (File.Exists(disabledPath))
                    {
                        File.Delete(disabledPath);
                        return true;
                    }
                }
                else if (item.Type == "Scheduled Task")
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/Delete /TN \"{item.Name}\" /F",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = System.Diagnostics.Process.Start(psi);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
            }
            catch { }

            return false;
        }
    }

    public class StartupItem
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }
}
