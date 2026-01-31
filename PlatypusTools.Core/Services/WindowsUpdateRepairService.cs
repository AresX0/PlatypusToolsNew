using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for diagnosing and repairing Windows Update issues
    /// </summary>
    public interface IWindowsUpdateRepairService
    {
        /// <summary>
        /// Scans Windows Update components for issues
        /// </summary>
        Task<WindowsUpdateScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to repair Windows Update using multiple methods
        /// </summary>
        Task<WindowsUpdateRepairResult> RepairAsync(RepairOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs DISM to check and repair Windows image
        /// </summary>
        Task<CommandResult> RunDismHealthCheckAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs System File Checker (SFC)
        /// </summary>
        Task<CommandResult> RunSfcScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets Windows Update components (stops services, clears cache, restarts services)
        /// </summary>
        Task<CommandResult> ResetWindowsUpdateComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears the Windows Update download cache
        /// </summary>
        Task<CommandResult> ClearUpdateCacheAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-registers Windows Update DLLs
        /// </summary>
        Task<CommandResult> ReregisterDllsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs Windows Update troubleshooter
        /// </summary>
        Task<CommandResult> RunTroubleshooterAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a Windows Update scan
    /// </summary>
    public class WindowsUpdateScanResult
    {
        public bool HasIssues { get; set; }
        public List<WindowsUpdateIssue> Issues { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; } = DateTime.Now;
        public bool DismHealthy { get; set; }
        public bool SfcHealthy { get; set; }
        public bool ServicesRunning { get; set; }
        public long CacheSizeBytes { get; set; }
    }

    /// <summary>
    /// Represents a detected Windows Update issue
    /// </summary>
    public class WindowsUpdateIssue
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IssueSeverity Severity { get; set; }
        public string RecommendedFix { get; set; } = string.Empty;
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Options for repair operation
    /// </summary>
    public class RepairOptions
    {
        public bool RunDism { get; set; } = true;
        public bool RunSfc { get; set; } = true;
        public bool ResetComponents { get; set; } = true;
        public bool ClearCache { get; set; } = true;
        public bool ReregisterDlls { get; set; } = true;
        public bool RunTroubleshooter { get; set; } = true;
        public bool CreateRestorePoint { get; set; } = true;
    }

    /// <summary>
    /// Result of a Windows Update repair operation
    /// </summary>
    public class WindowsUpdateRepairResult
    {
        public bool Success { get; set; }
        public List<RepairStepResult> Steps { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public DateTime RepairTime { get; set; } = DateTime.Now;
        public bool RequiresReboot { get; set; }
    }

    /// <summary>
    /// Result of a single repair step
    /// </summary>
    public class RepairStepResult
    {
        public string StepName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Result of a command execution
    /// </summary>
    public class CommandResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Implementation of Windows Update repair service
    /// </summary>
    public class WindowsUpdateRepairService : IWindowsUpdateRepairService
    {
        private readonly string[] _windowsUpdateServices = new[]
        {
            "wuauserv",     // Windows Update
            "bits",         // Background Intelligent Transfer Service
            "cryptSvc",     // Cryptographic Services
            "msiserver"     // Windows Installer
        };

        private readonly string[] _windowsUpdateDlls = new[]
        {
            "atl.dll", "urlmon.dll", "mshtml.dll", "shdocvw.dll", "browseui.dll",
            "jscript.dll", "vbscript.dll", "scrrun.dll", "msxml.dll", "msxml3.dll",
            "msxml6.dll", "actxprxy.dll", "softpub.dll", "wintrust.dll", "dssenh.dll",
            "rsaenh.dll", "gpkcsp.dll", "sccbase.dll", "slbcsp.dll", "cryptdlg.dll",
            "oleaut32.dll", "ole32.dll", "shell32.dll", "initpki.dll", "wuapi.dll",
            "wuaueng.dll", "wuaueng1.dll", "wucltui.dll", "wups.dll", "wups2.dll",
            "wuweb.dll", "qmgr.dll", "qmgrprxy.dll", "wucltux.dll", "muweb.dll", "wuwebv.dll"
        };

        public async Task<WindowsUpdateScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new WindowsUpdateScanResult();

            progress?.Report("Starting Windows Update scan...");

            // Check Windows Update services status
            progress?.Report("Checking Windows Update services...");
            result.ServicesRunning = await CheckServicesAsync(progress, cancellationToken);
            if (!result.ServicesRunning)
            {
                result.Issues.Add(new WindowsUpdateIssue
                {
                    Code = "WU_SVC_STOPPED",
                    Description = "One or more Windows Update services are not running",
                    Severity = IssueSeverity.Error,
                    RecommendedFix = "Reset Windows Update components"
                });
            }

            // Check SoftwareDistribution folder size
            progress?.Report("Checking Windows Update cache...");
            var softwareDistPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution");
            if (Directory.Exists(softwareDistPath))
            {
                result.CacheSizeBytes = await GetDirectorySizeAsync(softwareDistPath, cancellationToken);
                if (result.CacheSizeBytes > 5L * 1024 * 1024 * 1024) // > 5GB
                {
                    result.Issues.Add(new WindowsUpdateIssue
                    {
                        Code = "WU_CACHE_LARGE",
                        Description = $"Windows Update cache is large ({result.CacheSizeBytes / (1024 * 1024 * 1024):F1} GB)",
                        Severity = IssueSeverity.Warning,
                        RecommendedFix = "Clear Windows Update cache"
                    });
                }
            }

            // Quick DISM check
            progress?.Report("Running DISM health check (quick)...");
            var dismResult = await RunCommandAsync("DISM.exe", "/Online /Cleanup-Image /CheckHealth", progress, cancellationToken);
            result.DismHealthy = dismResult.ExitCode == 0;
            if (!result.DismHealthy)
            {
                result.Issues.Add(new WindowsUpdateIssue
                {
                    Code = "DISM_UNHEALTHY",
                    Description = "Windows component store may be corrupted",
                    Severity = IssueSeverity.Error,
                    RecommendedFix = "Run DISM RestoreHealth"
                });
            }

            // Check for pending.xml (indicates incomplete update)
            var pendingXmlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS", "pending.xml");
            if (File.Exists(pendingXmlPath))
            {
                result.Issues.Add(new WindowsUpdateIssue
                {
                    Code = "WU_PENDING",
                    Description = "There are pending Windows Update operations",
                    Severity = IssueSeverity.Warning,
                    RecommendedFix = "Restart the computer and try again"
                });
            }

            result.HasIssues = result.Issues.Count > 0;
            result.Summary = result.HasIssues
                ? $"Found {result.Issues.Count} issue(s) with Windows Update"
                : "No issues detected with Windows Update";

            progress?.Report(result.Summary);
            return result;
        }

        public async Task<WindowsUpdateRepairResult> RepairAsync(RepairOptions options, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new WindowsUpdateRepairResult();
            var sw = Stopwatch.StartNew();

            progress?.Report("Starting Windows Update repair...");

            // Create restore point first if requested
            if (options.CreateRestorePoint)
            {
                progress?.Report("Creating system restore point...");
                try
                {
                    var restoreService = new SystemRestoreService();
                    await restoreService.CreateRestorePoint("Before Windows Update Repair");
                    result.Steps.Add(new RepairStepResult { StepName = "Create Restore Point", Success = true });
                }
                catch (Exception ex)
                {
                    result.Steps.Add(new RepairStepResult { StepName = "Create Restore Point", Success = false, Error = ex.Message });
                }
            }

            // Run Windows Update Troubleshooter
            if (options.RunTroubleshooter)
            {
                progress?.Report("Running Windows Update troubleshooter...");
                var troubleshooterResult = await RunTroubleshooterAsync(progress, cancellationToken);
                result.Steps.Add(new RepairStepResult
                {
                    StepName = "Windows Update Troubleshooter",
                    Success = troubleshooterResult.Success,
                    Output = troubleshooterResult.Output,
                    Error = troubleshooterResult.Error,
                    Duration = troubleshooterResult.Duration
                });
            }

            // Reset Windows Update components
            if (options.ResetComponents)
            {
                progress?.Report("Resetting Windows Update components...");
                var resetResult = await ResetWindowsUpdateComponentsAsync(progress, cancellationToken);
                result.Steps.Add(new RepairStepResult
                {
                    StepName = "Reset Windows Update Components",
                    Success = resetResult.Success,
                    Output = resetResult.Output,
                    Error = resetResult.Error,
                    Duration = resetResult.Duration
                });
            }

            // Clear update cache
            if (options.ClearCache)
            {
                progress?.Report("Clearing Windows Update cache...");
                var cacheResult = await ClearUpdateCacheAsync(progress, cancellationToken);
                result.Steps.Add(new RepairStepResult
                {
                    StepName = "Clear Update Cache",
                    Success = cacheResult.Success,
                    Output = cacheResult.Output,
                    Error = cacheResult.Error,
                    Duration = cacheResult.Duration
                });
            }

            // Re-register DLLs
            if (options.ReregisterDlls)
            {
                progress?.Report("Re-registering Windows Update DLLs...");
                var dllResult = await ReregisterDllsAsync(progress, cancellationToken);
                result.Steps.Add(new RepairStepResult
                {
                    StepName = "Re-register DLLs",
                    Success = dllResult.Success,
                    Output = dllResult.Output,
                    Error = dllResult.Error,
                    Duration = dllResult.Duration
                });
            }

            // Run SFC
            if (options.RunSfc)
            {
                progress?.Report("Running System File Checker (this may take a while)...");
                var sfcResult = await RunSfcScanAsync(progress, cancellationToken);
                result.Steps.Add(new RepairStepResult
                {
                    StepName = "System File Checker (SFC)",
                    Success = sfcResult.Success,
                    Output = sfcResult.Output,
                    Error = sfcResult.Error,
                    Duration = sfcResult.Duration
                });
                if (sfcResult.Output.Contains("found corrupt files"))
                {
                    result.RequiresReboot = true;
                }
            }

            // Run DISM
            if (options.RunDism)
            {
                progress?.Report("Running DISM RestoreHealth (this may take a while)...");
                var dismResult = await RunDismHealthCheckAsync(progress, cancellationToken);
                result.Steps.Add(new RepairStepResult
                {
                    StepName = "DISM RestoreHealth",
                    Success = dismResult.Success,
                    Output = dismResult.Output,
                    Error = dismResult.Error,
                    Duration = dismResult.Duration
                });
                if (dismResult.Output.Contains("repair"))
                {
                    result.RequiresReboot = true;
                }
            }

            sw.Stop();
            result.Success = result.Steps.TrueForAll(s => s.Success);
            result.Summary = result.Success
                ? "Windows Update repair completed successfully"
                : "Windows Update repair completed with some errors";

            if (result.RequiresReboot)
            {
                result.Summary += ". A restart is recommended.";
            }

            progress?.Report(result.Summary);
            return result;
        }

        public async Task<CommandResult> RunDismHealthCheckAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report("Running DISM RestoreHealth...");
            return await RunCommandAsync("DISM.exe", "/Online /Cleanup-Image /RestoreHealth", progress, cancellationToken, 1800); // 30 min timeout
        }

        public async Task<CommandResult> RunSfcScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report("Running System File Checker...");
            return await RunCommandAsync("sfc.exe", "/scannow", progress, cancellationToken, 1800); // 30 min timeout
        }

        public async Task<CommandResult> ResetWindowsUpdateComponentsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var output = new StringBuilder();
            var sw = Stopwatch.StartNew();

            try
            {
                // Stop services
                progress?.Report("Stopping Windows Update services...");
                foreach (var service in _windowsUpdateServices)
                {
                    await RunCommandAsync("net", $"stop {service}", null, cancellationToken, 60);
                    output.AppendLine($"Stopped {service}");
                }

                // Rename SoftwareDistribution and catroot2 folders
                progress?.Report("Renaming cache folders...");
                var softwareDistPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution");
                var catroot2Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "catroot2");
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

                if (Directory.Exists(softwareDistPath))
                {
                    var backupPath = $"{softwareDistPath}.old.{timestamp}";
                    try
                    {
                        Directory.Move(softwareDistPath, backupPath);
                        output.AppendLine($"Renamed SoftwareDistribution to {backupPath}");
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine($"Could not rename SoftwareDistribution: {ex.Message}");
                    }
                }

                if (Directory.Exists(catroot2Path))
                {
                    var backupPath = $"{catroot2Path}.old.{timestamp}";
                    try
                    {
                        Directory.Move(catroot2Path, backupPath);
                        output.AppendLine($"Renamed catroot2 to {backupPath}");
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine($"Could not rename catroot2: {ex.Message}");
                    }
                }

                // Reset BITS and Windows Update service security descriptors
                progress?.Report("Resetting service security descriptors...");
                await RunCommandAsync("sc.exe", "sdset bits D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)", null, cancellationToken, 30);
                await RunCommandAsync("sc.exe", "sdset wuauserv D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)", null, cancellationToken, 30);
                output.AppendLine("Reset service security descriptors");

                // Start services
                progress?.Report("Starting Windows Update services...");
                foreach (var service in _windowsUpdateServices)
                {
                    await RunCommandAsync("net", $"start {service}", null, cancellationToken, 60);
                    output.AppendLine($"Started {service}");
                }

                sw.Stop();
                return new CommandResult
                {
                    Success = true,
                    Output = output.ToString(),
                    Duration = sw.Elapsed
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new CommandResult
                {
                    Success = false,
                    Error = ex.Message,
                    Output = output.ToString(),
                    Duration = sw.Elapsed
                };
            }
        }

        public async Task<CommandResult> ClearUpdateCacheAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var output = new StringBuilder();

            try
            {
                // Stop Windows Update service
                progress?.Report("Stopping Windows Update service...");
                await RunCommandAsync("net", "stop wuauserv", null, cancellationToken, 60);
                await RunCommandAsync("net", "stop bits", null, cancellationToken, 60);

                // Clear Download folder
                var downloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
                if (Directory.Exists(downloadPath))
                {
                    progress?.Report("Clearing Windows Update download cache...");
                    var files = Directory.GetFiles(downloadPath, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { /* Skip locked files */ }
                    }
                    output.AppendLine($"Cleared {files.Length} files from download cache");
                }

                // Start services back
                progress?.Report("Starting Windows Update services...");
                await RunCommandAsync("net", "start bits", null, cancellationToken, 60);
                await RunCommandAsync("net", "start wuauserv", null, cancellationToken, 60);

                sw.Stop();
                return new CommandResult
                {
                    Success = true,
                    Output = output.ToString(),
                    Duration = sw.Elapsed
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new CommandResult
                {
                    Success = false,
                    Error = ex.Message,
                    Output = output.ToString(),
                    Duration = sw.Elapsed
                };
            }
        }

        public async Task<CommandResult> ReregisterDllsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var output = new StringBuilder();
            var successCount = 0;
            var failCount = 0;

            progress?.Report($"Re-registering {_windowsUpdateDlls.Length} DLLs...");

            foreach (var dll in _windowsUpdateDlls)
            {
                try
                {
                    var result = await RunCommandAsync("regsvr32.exe", $"/s {dll}", null, cancellationToken, 30);
                    if (result.ExitCode == 0)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch
                {
                    failCount++;
                }
            }

            sw.Stop();
            output.AppendLine($"Successfully registered: {successCount} DLLs");
            if (failCount > 0)
            {
                output.AppendLine($"Failed to register: {failCount} DLLs (some may not exist on this system)");
            }

            return new CommandResult
            {
                Success = true, // Some DLLs may not exist, which is normal
                Output = output.ToString(),
                Duration = sw.Elapsed
            };
        }

        public async Task<CommandResult> RunTroubleshooterAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report("Running Windows Update troubleshooter...");
            
            // MSDT is deprecated in Windows 11 23H2+. Try modern approaches first.
            
            // Method 1: Try PowerShell Get-TroubleshootingPack (works on Windows 10/11)
            progress?.Report("Attempting PowerShell troubleshooter...");
            var result = await RunCommandAsync("powershell.exe", 
                "-ExecutionPolicy Bypass -Command \"$ErrorActionPreference = 'SilentlyContinue'; " +
                "$pack = Get-TroubleshootingPack -Path $env:SystemRoot\\diagnostics\\system\\WindowsUpdate; " +
                "if ($pack) { Invoke-TroubleshootingPack -Pack $pack -Unattended -Result $env:TEMP\\WUTroubleshoot } " +
                "else { Write-Output 'TroubleshootingPack not available' }\"",
                progress, cancellationToken, 300);
            
            if (result.Success && !result.Output.Contains("not available"))
            {
                return result;
            }
            
            // Method 2: Try Windows Settings troubleshooter (Windows 10 1809+)
            // This opens the Settings app troubleshooter which uses the modern platform
            progress?.Report("Opening Windows Settings troubleshooter...");
            result = await RunCommandAsync("cmd.exe", 
                "/c start ms-settings:troubleshoot",
                progress, cancellationToken, 10);
            
            if (result.ExitCode == 0)
            {
                result.Output = "Opened Windows Settings > Troubleshoot.\n" +
                    "Please navigate to: Other troubleshooters > Windows Update > Run\n\n" +
                    "Note: The legacy MSDT troubleshooter (msdt.exe) is deprecated and being removed by Microsoft.\n" +
                    "The Windows Settings troubleshooter provides the same functionality.";
                result.Success = true;
                return result;
            }
            
            // Method 3: Legacy MSDT as last resort (may not work on newer Windows 11)
            progress?.Report("Falling back to legacy troubleshooter (MSDT)...");
            result = await RunCommandAsync("msdt.exe", "/id WindowsUpdateDiagnostic /skip true", progress, cancellationToken, 300);
            
            if (result.ExitCode != 0)
            {
                result.Output = "The Windows Update troubleshooter could not be launched.\n\n" +
                    "Please run it manually:\n" +
                    "1. Open Settings > System > Troubleshoot > Other troubleshooters\n" +
                    "2. Find 'Windows Update' and click 'Run'\n\n" +
                    "Note: Microsoft has deprecated the legacy MSDT troubleshooter in Windows 11 23H2 and newer.";
            }

            return result;
        }

        private async Task<bool> CheckServicesAsync(IProgress<string>? progress, CancellationToken cancellationToken)
        {
            foreach (var service in _windowsUpdateServices)
            {
                var result = await RunCommandAsync("sc.exe", $"query {service}", null, cancellationToken, 30);
                if (!result.Output.Contains("RUNNING"))
                {
                    progress?.Report($"Service {service} is not running");
                    return false;
                }
            }
            return true;
        }

        private async Task<long> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                long size = 0;
                try
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        try
                        {
                            size += new FileInfo(file).Length;
                        }
                        catch { /* Skip inaccessible files */ }
                    }
                }
                catch { /* Directory may be locked */ }
                return size;
            }, cancellationToken);
        }

        private async Task<CommandResult> RunCommandAsync(string fileName, string arguments, IProgress<string>? progress, CancellationToken cancellationToken, int timeoutSeconds = 300)
        {
            var sw = Stopwatch.StartNew();
            var output = new StringBuilder();
            var error = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.AppendLine(e.Data);
                        progress?.Report(e.Data);
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        error.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                await process.WaitForExitAsync(cts.Token);

                sw.Stop();
                return new CommandResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    Output = output.ToString(),
                    Error = error.ToString(),
                    Duration = sw.Elapsed
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new CommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    Error = "Operation timed out or was cancelled",
                    Duration = sw.Elapsed
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new CommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    Error = ex.Message,
                    Duration = sw.Elapsed
                };
            }
        }
    }
}
