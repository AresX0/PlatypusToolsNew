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
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(1)) // Skip header
                    {
                        if (line.Contains("READY") || line.Contains("RUNNING"))
                        {
                            var parts = line.Split(',');
                            if (parts.Length > 1)
                            {
                                var taskName = parts[0].Trim('"');
                                if (taskName.Contains("\\Microsoft\\Windows\\") == false) // Filter out system tasks
                                {
                                    items.Add(new StartupItem
                                    {
                                        Name = taskName,
                                        Command = parts.Length > 8 ? parts[8].Trim('"') : "",
                                        Location = "Task Scheduler",
                                        IsEnabled = line.Contains("READY") || line.Contains("RUNNING"),
                                        Type = "Scheduled Task"
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return items;
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
