using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public interface ISystemAuditService
    {
        Task<List<AuditItem>> RunFullAudit();
        Task<List<AuditItem>> AuditFilePermissions(string path);
        Task<List<AuditItem>> AuditFirewall();
        Task<List<AuditItem>> AuditWindowsUpdates();
        Task<List<AuditItem>> AuditInstalledSoftware();
        Task<List<AuditItem>> AuditStartupItems();
        Task<bool> FixIssue(AuditItem item);
    }

    public class AuditItem
    {
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AuditSeverity Severity { get; set; }
        public AuditStatus Status { get; set; }
        public string Details { get; set; } = string.Empty;
        public string? FixAction { get; set; }
        public bool CanAutoFix { get; set; }
    }

    public enum AuditSeverity
    {
        Info,
        Warning,
        Critical
    }

    public enum AuditStatus
    {
        Pass,
        Fail,
        Unknown
    }

    public class SystemAuditService : ISystemAuditService
    {
        public async Task<List<AuditItem>> RunFullAudit()
        {
            var items = new List<AuditItem>();

            items.AddRange(await AuditFirewall());
            items.AddRange(await AuditWindowsUpdates());
            items.AddRange(await AuditInstalledSoftware());
            items.AddRange(await AuditStartupItems());
            items.AddRange(await AuditSystemSettings());

            return items;
        }

        public async Task<List<AuditItem>> AuditFilePermissions(string path)
        {
            var items = new List<AuditItem>();

            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(path) && !File.Exists(path))
                    {
                        items.Add(new AuditItem
                        {
                            Category = "File Permissions",
                            Name = path,
                            Description = "Path not found",
                            Severity = AuditSeverity.Warning,
                            Status = AuditStatus.Fail
                        });
                        return;
                    }

                    var info = new FileInfo(path);
                    var security = info.GetAccessControl();
                    var rules = security.GetAccessRules(true, true, typeof(NTAccount));

                    foreach (AuthorizationRule rule in rules)
                    {
                        var fsRule = rule as FileSystemAccessRule;
                        if (fsRule != null)
                        {
                            var severity = DeterminePermissionSeverity(fsRule);
                            items.Add(new AuditItem
                            {
                                Category = "File Permissions",
                                Name = fsRule.IdentityReference.Value,
                                Description = $"{fsRule.FileSystemRights} - {fsRule.AccessControlType}",
                                Severity = severity,
                                Status = severity == AuditSeverity.Critical ? AuditStatus.Fail : AuditStatus.Pass,
                                Details = path
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    items.Add(new AuditItem
                    {
                        Category = "File Permissions",
                        Name = "Error",
                        Description = ex.Message,
                        Severity = AuditSeverity.Warning,
                        Status = AuditStatus.Unknown
                    });
                }
            });

            return items;
        }

        public async Task<List<AuditItem>> AuditFirewall()
        {
            var items = new List<AuditItem>();

            await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "advfirewall show allprofiles state",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        var isEnabled = output.Contains("State                                 ON");
                        items.Add(new AuditItem
                        {
                            Category = "Firewall",
                            Name = "Windows Firewall",
                            Description = isEnabled ? "Firewall is enabled" : "Firewall is disabled",
                            Severity = isEnabled ? AuditSeverity.Info : AuditSeverity.Critical,
                            Status = isEnabled ? AuditStatus.Pass : AuditStatus.Fail,
                            CanAutoFix = !isEnabled,
                            FixAction = "netsh advfirewall set allprofiles state on"
                        });
                    }
                }
                catch (Exception ex)
                {
                    items.Add(new AuditItem
                    {
                        Category = "Firewall",
                        Name = "Error",
                        Description = ex.Message,
                        Severity = AuditSeverity.Warning,
                        Status = AuditStatus.Unknown
                    });
                }
            });

            return items;
        }

        public async Task<List<AuditItem>> AuditWindowsUpdates()
        {
            var items = new List<AuditItem>();

            await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-Command \"Get-HotFix | Select-Object -First 1 -Property InstalledOn | ConvertTo-Json\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        var hasRecentUpdates = !string.IsNullOrEmpty(output);
                        items.Add(new AuditItem
                        {
                            Category = "Windows Updates",
                            Name = "Update Status",
                            Description = hasRecentUpdates ? "Updates are installed" : "No recent updates found",
                            Severity = hasRecentUpdates ? AuditSeverity.Info : AuditSeverity.Warning,
                            Status = hasRecentUpdates ? AuditStatus.Pass : AuditStatus.Fail,
                            Details = output
                        });
                    }
                }
                catch (Exception ex)
                {
                    items.Add(new AuditItem
                    {
                        Category = "Windows Updates",
                        Name = "Error",
                        Description = ex.Message,
                        Severity = AuditSeverity.Warning,
                        Status = AuditStatus.Unknown
                    });
                }
            });

            return items;
        }

        public async Task<List<AuditItem>> AuditInstalledSoftware()
        {
            var items = new List<AuditItem>();

            await Task.Run(() =>
            {
                try
                {
                    var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Product");
                    var count = 0;
                    
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        count++;
                        var name = obj["Name"]?.ToString() ?? "Unknown";
                        var version = obj["Version"]?.ToString() ?? "Unknown";
                        
                        items.Add(new AuditItem
                        {
                            Category = "Installed Software",
                            Name = name,
                            Description = $"Version: {version}",
                            Severity = AuditSeverity.Info,
                            Status = AuditStatus.Pass
                        });

                        if (count >= 50) break; // Limit to first 50
                    }
                }
                catch (Exception ex)
                {
                    items.Add(new AuditItem
                    {
                        Category = "Installed Software",
                        Name = "Error",
                        Description = ex.Message,
                        Severity = AuditSeverity.Warning,
                        Status = AuditStatus.Unknown
                    });
                }
            });

            return items;
        }

        public async Task<List<AuditItem>> AuditStartupItems()
        {
            var items = new List<AuditItem>();

            await Task.Run(() =>
            {
                try
                {
                    // Check registry startup items
                    var startupKeys = new[]
                    {
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
                    };

                    foreach (var keyPath in startupKeys)
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                        if (key != null)
                        {
                            foreach (var valueName in key.GetValueNames())
                            {
                                var value = key.GetValue(valueName)?.ToString() ?? string.Empty;
                                items.Add(new AuditItem
                                {
                                    Category = "Startup Items",
                                    Name = valueName,
                                    Description = value,
                                    Severity = AuditSeverity.Info,
                                    Status = AuditStatus.Pass,
                                    Details = keyPath
                                });
                            }
                        }
                    }

                    // Check startup folder
                    var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    if (Directory.Exists(startupFolder))
                    {
                        foreach (var file in Directory.GetFiles(startupFolder))
                        {
                            items.Add(new AuditItem
                            {
                                Category = "Startup Items",
                                Name = Path.GetFileName(file),
                                Description = file,
                                Severity = AuditSeverity.Info,
                                Status = AuditStatus.Pass,
                                Details = "Startup Folder"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    items.Add(new AuditItem
                    {
                        Category = "Startup Items",
                        Name = "Error",
                        Description = ex.Message,
                        Severity = AuditSeverity.Warning,
                        Status = AuditStatus.Unknown
                    });
                }
            });

            return items;
        }

        private async Task<List<AuditItem>> AuditSystemSettings()
        {
            var items = new List<AuditItem>();

            await Task.Run(() =>
            {
                // Check UAC
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                    if (key != null)
                    {
                        var uacEnabled = (int?)key.GetValue("EnableLUA") == 1;
                        items.Add(new AuditItem
                        {
                            Category = "System Settings",
                            Name = "UAC (User Account Control)",
                            Description = uacEnabled ? "UAC is enabled" : "UAC is disabled",
                            Severity = uacEnabled ? AuditSeverity.Info : AuditSeverity.Critical,
                            Status = uacEnabled ? AuditStatus.Pass : AuditStatus.Fail
                        });
                    }
                }
                catch { }

                // Check Windows Defender
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-Command \"Get-MpComputerStatus | Select-Object -Property RealTimeProtectionEnabled | ConvertTo-Json\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        var defenderEnabled = output.Contains("true");
                        items.Add(new AuditItem
                        {
                            Category = "System Settings",
                            Name = "Windows Defender",
                            Description = defenderEnabled ? "Real-time protection enabled" : "Real-time protection disabled",
                            Severity = defenderEnabled ? AuditSeverity.Info : AuditSeverity.Critical,
                            Status = defenderEnabled ? AuditStatus.Pass : AuditStatus.Fail
                        });
                    }
                }
                catch { }
            });

            return items;
        }

        public async Task<bool> FixIssue(AuditItem item)
        {
            if (!item.CanAutoFix || string.IsNullOrEmpty(item.FixAction))
                return false;

            try
            {
                await Task.Run(() =>
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {item.FixAction}",
                        UseShellExecute = true,
                        Verb = "runas", // Request admin
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    process?.WaitForExit();
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        private AuditSeverity DeterminePermissionSeverity(FileSystemAccessRule rule)
        {
            // Check for overly permissive rules
            if (rule.AccessControlType == AccessControlType.Allow)
            {
                if (rule.IdentityReference.Value.Contains("Everyone") ||
                    rule.IdentityReference.Value.Contains("Users"))
                {
                    if (rule.FileSystemRights.HasFlag(FileSystemRights.FullControl) ||
                        rule.FileSystemRights.HasFlag(FileSystemRights.Modify))
                    {
                        return AuditSeverity.Critical;
                    }
                }
            }

            return AuditSeverity.Info;
        }
    }
}
