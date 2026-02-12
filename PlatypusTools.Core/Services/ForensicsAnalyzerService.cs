using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Forensics analyzer service for memory, file system, registry, and log analysis.
    /// Supports lightweight (single-core, ~100MB output) and deep (multi-core, GB output) modes.
    /// </summary>
    public class ForensicsAnalyzerService
    {
        private readonly int _maxDegreeOfParallelism;
        private CancellationTokenSource? _cancellationTokenSource;

        // Suspicious indicators
        private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".scr", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf", ".hta"
        };

        private static readonly HashSet<string> SuspiciousPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows\Temp",
            @"C:\Users\Public",
            @"C:\ProgramData",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Temp"
        };

        private static readonly HashSet<int> SuspiciousEventIds = new()
        {
            4624, 4625, 4634, 4648, 4672, 4720, 4722, 4723, 4724, 4725, 4726, 4728, 4732,
            4740, 4756, 4767, 4768, 4769, 4771, 4776, 4778, 4779, 1102, 4688, 4689
        };

        /// <summary>
        /// Initializes a new instance of the ForensicsAnalyzerService with default parallelism settings.
        /// </summary>
        public ForensicsAnalyzerService()
        {
            _maxDegreeOfParallelism = Environment.ProcessorCount;
        }

        /// <summary>
        /// Runs a complete forensics analysis.
        /// </summary>
        public async Task<ForensicsAnalysisResult> AnalyzeAsync(
            ForensicsMode mode,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cancellationTokenSource.Token;

            var result = new ForensicsAnalysisResult
            {
                Mode = mode,
                StartTime = DateTime.Now
            };

            try
            {
                int parallelism = mode == ForensicsMode.Lightweight ? 1 : _maxDegreeOfParallelism;

                progress?.Report($"Starting {mode} forensics analysis (using {parallelism} core(s))...");

                // Run analyses based on mode
                var tasks = new List<Task>();

                progress?.Report("Analyzing memory...");
                result.MemoryAnalysis = await AnalyzeMemoryAsync(mode, progress, token);
                result.AllFindings.AddRange(result.MemoryAnalysis.Findings);

                progress?.Report("Examining file system...");
                result.FileSystemAnalysis = await AnalyzeFileSystemAsync(mode, parallelism, progress, token);
                result.AllFindings.AddRange(result.FileSystemAnalysis.Findings);

                progress?.Report("Analyzing registry...");
                result.RegistryAnalysis = await AnalyzeRegistryAsync(mode, progress, token);
                result.AllFindings.AddRange(result.RegistryAnalysis.Findings);

                progress?.Report("Aggregating logs...");
                result.LogAnalysis = await AnalyzeLogsAsync(mode, progress, token);
                result.AllFindings.AddRange(result.LogAnalysis.Findings);

                result.EndTime = DateTime.Now;

                // Generate report
                progress?.Report("Generating report...");
                await GenerateReportAsync(result, mode, progress, token);

                progress?.Report($"Analysis complete. Found {result.TotalFindings} findings in {result.Duration.TotalSeconds:F1}s");
            }
            catch (OperationCanceledException)
            {
                progress?.Report("Analysis cancelled.");
                result.EndTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                progress?.Report($"Error during analysis: {ex.Message}");
                result.AllFindings.Add(new ForensicsFinding
                {
                    Type = ForensicsFindingType.Anomaly,
                    Severity = ForensicsSeverity.High,
                    Title = "Analysis Error",
                    Description = ex.Message,
                    Details = ex.StackTrace ?? ""
                });
                result.EndTime = DateTime.Now;
            }

            return result;
        }

        /// <summary>
        /// Cancels the current analysis.
        /// </summary>
        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        #region Memory Analysis

        private async Task<MemoryAnalysisResult> AnalyzeMemoryAsync(
            ForensicsMode mode,
            IProgress<string>? progress,
            CancellationToken token)
        {
            return await Task.Run(() =>
            {
                var result = new MemoryAnalysisResult();

                try
                {
                    // Get system memory info using WMI
                    var memoryInfo = GetSystemMemoryInfo();
                    result.TotalPhysicalMemory = memoryInfo.totalMemory;
                    result.AvailableMemory = memoryInfo.availableMemory;

                    // Get all processes
                    var processes = Process.GetProcesses();
                    var processInfos = new List<ProcessMemoryInfo>();

                    foreach (var proc in processes)
                    {
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            var info = new ProcessMemoryInfo
                            {
                                ProcessId = proc.Id,
                                ProcessName = proc.ProcessName,
                                WorkingSet = proc.WorkingSet64,
                                PrivateBytes = proc.PrivateMemorySize64,
                                VirtualBytes = proc.VirtualMemorySize64
                            };

                            // Deep mode: get more details
                            if (mode == ForensicsMode.Deep)
                            {
                                try
                                {
                                    info.ExecutablePath = proc.MainModule?.FileName ?? "";
                                    info.StartTime = proc.StartTime;

                                    // Calculate hash for executables
                                    if (!string.IsNullOrEmpty(info.ExecutablePath) && File.Exists(info.ExecutablePath))
                                    {
                                        info.Hash = CalculateFileHash(info.ExecutablePath);
                                    }
                                }
                                catch { /* Access denied for system processes */ }
                            }

                            // Check for suspicious indicators
                            CheckProcessSuspicion(info, mode);
                            processInfos.Add(info);
                        }
                        catch { /* Skip inaccessible processes */ }
                    }

                    // Get top processes by memory
                    result.TopProcesses = processInfos
                        .OrderByDescending(p => p.WorkingSet)
                        .Take(mode == ForensicsMode.Lightweight ? 20 : 100)
                        .ToList();

                    // Get suspicious processes
                    result.SuspiciousProcesses = processInfos
                        .Where(p => p.IsSuspicious)
                        .ToList();

                    // Generate findings
                    foreach (var suspicious in result.SuspiciousProcesses)
                    {
                        result.Findings.Add(new ForensicsFinding
                        {
                            Type = ForensicsFindingType.Process,
                            Severity = ForensicsSeverity.Medium,
                            Title = $"Suspicious Process: {suspicious.ProcessName}",
                            Description = suspicious.SuspicionReason,
                            Details = $"PID: {suspicious.ProcessId}, Path: {suspicious.ExecutablePath}",
                            Source = "Memory Analysis"
                        });
                    }

                    // Check for high memory usage
                    if (result.UsagePercent > 90)
                    {
                        result.Findings.Add(new ForensicsFinding
                        {
                            Type = ForensicsFindingType.Memory,
                            Severity = ForensicsSeverity.High,
                            Title = "Critical Memory Usage",
                            Description = $"System memory usage is at {result.UsagePercent:F1}%",
                            Source = "Memory Analysis"
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Findings.Add(new ForensicsFinding
                    {
                        Type = ForensicsFindingType.Memory,
                        Severity = ForensicsSeverity.Low,
                        Title = "Memory Analysis Partial",
                        Description = $"Could not complete full memory analysis: {ex.Message}",
                        Source = "Memory Analysis"
                    });
                }

                return result;
            }, token);
        }

        private void CheckProcessSuspicion(ProcessMemoryInfo info, ForensicsMode mode)
        {
            var reasons = new List<string>();

            // Check for processes running from temp folders
            if (!string.IsNullOrEmpty(info.ExecutablePath))
            {
                foreach (var suspPath in SuspiciousPaths)
                {
                    if (info.ExecutablePath.StartsWith(suspPath, StringComparison.OrdinalIgnoreCase))
                    {
                        reasons.Add($"Running from suspicious location: {suspPath}");
                        break;
                    }
                }
            }

            // Check for high memory usage single process
            if (info.WorkingSet > 2L * 1024 * 1024 * 1024) // 2GB
            {
                reasons.Add("Extremely high memory usage (>2GB)");
            }

            // Check for suspicious names (basic check)
            var suspiciousNames = new[] { "miner", "crypter", "keylog", "inject", "hook" };
            foreach (var name in suspiciousNames)
            {
                if (info.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"Process name contains suspicious keyword: {name}");
                    break;
                }
            }

            if (reasons.Count > 0)
            {
                info.IsSuspicious = true;
                info.SuspicionReason = string.Join("; ", reasons);
            }
        }

        #endregion

        #region File System Analysis

        private async Task<FileSystemAnalysisResult> AnalyzeFileSystemAsync(
            ForensicsMode mode,
            int parallelism,
            IProgress<string>? progress,
            CancellationToken token)
        {
            return await Task.Run(() =>
            {
                var result = new FileSystemAnalysisResult();
                var suspiciousFiles = new ConcurrentBag<SuspiciousFileInfo>();
                var recentFiles = new ConcurrentBag<RecentFileInfo>();

                // Directories to scan
                var dirsToScan = new List<string>();

                if (mode == ForensicsMode.Lightweight)
                {
                    // Quick scan: only critical locations
                    dirsToScan.AddRange(new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                        @"C:\Windows\Temp",
                        Environment.GetFolderPath(Environment.SpecialFolder.Recent)
                    });
                }
                else
                {
                    // Deep scan: more locations
                    dirsToScan.AddRange(new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                        @"C:\Windows\Temp",
                        @"C:\Users\Public",
                        @"C:\ProgramData",
                        Environment.GetFolderPath(Environment.SpecialFolder.Recent),
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    });
                }

                var cutoffTime = mode == ForensicsMode.Lightweight
                    ? DateTime.Now.AddDays(-1)
                    : DateTime.Now.AddDays(-7);

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = token
                };

                foreach (var dir in dirsToScan.Where(Directory.Exists).Distinct())
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var searchDepth = mode == ForensicsMode.Lightweight
                            ? SearchOption.TopDirectoryOnly
                            : SearchOption.AllDirectories;

                        var files = Directory.EnumerateFiles(dir, "*.*", searchDepth);

                        Parallel.ForEach(files, options, filePath =>
                        {
                            try
                            {
                                result.TotalFilesScanned++;
                                var fileInfo = new FileInfo(filePath);

                                // Check for recently modified
                                if (fileInfo.LastWriteTime > cutoffTime)
                                {
                                    recentFiles.Add(new RecentFileInfo
                                    {
                                        FilePath = filePath,
                                        FileName = fileInfo.Name,
                                        ModifiedTime = fileInfo.LastWriteTime,
                                        FileSize = fileInfo.Length
                                    });
                                    result.RecentlyModifiedFiles++;
                                }

                                // Check for suspicious files
                                var ext = fileInfo.Extension;
                                bool isSuspicious = false;
                                var reasons = new List<string>();

                                if (SuspiciousExtensions.Contains(ext))
                                {
                                    // Executable in temp folder
                                    if (SuspiciousPaths.Any(p => filePath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        isSuspicious = true;
                                        reasons.Add("Executable in temporary/suspicious location");
                                        result.ExecutablesInTemp++;
                                    }
                                }

                                // Check for hidden files
                                if ((fileInfo.Attributes & FileAttributes.Hidden) != 0)
                                {
                                    result.HiddenFilesFound++;
                                    if (SuspiciousExtensions.Contains(ext))
                                    {
                                        isSuspicious = true;
                                        reasons.Add("Hidden executable file");
                                    }
                                }

                                // Check for double extensions
                                if (fileInfo.Name.Count(c => c == '.') > 1 && SuspiciousExtensions.Contains(ext))
                                {
                                    isSuspicious = true;
                                    reasons.Add("Double file extension (possible masquerading)");
                                }

                                if (isSuspicious)
                                {
                                    result.SuspiciousFilesFound++;
                                    var suspicious = new SuspiciousFileInfo
                                    {
                                        FilePath = filePath,
                                        FileName = fileInfo.Name,
                                        FileSize = fileInfo.Length,
                                        CreatedTime = fileInfo.CreationTime,
                                        ModifiedTime = fileInfo.LastWriteTime,
                                        IsHidden = (fileInfo.Attributes & FileAttributes.Hidden) != 0,
                                        IsSystem = (fileInfo.Attributes & FileAttributes.System) != 0,
                                        Reason = string.Join("; ", reasons)
                                    };

                                    // Deep mode: calculate hash
                                    if (mode == ForensicsMode.Deep && fileInfo.Length < 100 * 1024 * 1024)
                                    {
                                        suspicious.Hash = CalculateFileHash(filePath);
                                    }

                                    suspiciousFiles.Add(suspicious);
                                }
                            }
                            catch { /* Skip inaccessible files */ }
                        });
                    }
                    catch { /* Skip inaccessible directories */ }
                }

                result.SuspiciousFiles = suspiciousFiles.ToList();
                result.RecentlyModified = recentFiles
                    .OrderByDescending(f => f.ModifiedTime)
                    .Take(mode == ForensicsMode.Lightweight ? 50 : 500)
                    .ToList();

                // Generate findings
                foreach (var suspicious in result.SuspiciousFiles.Take(100))
                {
                    result.Findings.Add(new ForensicsFinding
                    {
                        Type = ForensicsFindingType.FileSystem,
                        Severity = ForensicsSeverity.Medium,
                        Title = $"Suspicious File: {suspicious.FileName}",
                        Description = suspicious.Reason,
                        Details = $"Path: {suspicious.FilePath}\nSize: {suspicious.FileSizeFormatted}\nModified: {suspicious.ModifiedTime}",
                        Source = "File System Analysis",
                        Hash = suspicious.Hash
                    });
                }

                if (result.ExecutablesInTemp > 10)
                {
                    result.Findings.Add(new ForensicsFinding
                    {
                        Type = ForensicsFindingType.FileSystem,
                        Severity = ForensicsSeverity.High,
                        Title = "High Number of Executables in Temp",
                        Description = $"Found {result.ExecutablesInTemp} executable files in temporary directories",
                        Source = "File System Analysis"
                    });
                }

                return result;
            }, token);
        }

        #endregion

        #region Registry Analysis

        private async Task<RegistryAnalysisResult> AnalyzeRegistryAsync(
            ForensicsMode mode,
            IProgress<string>? progress,
            CancellationToken token)
        {
            return await Task.Run(() =>
            {
                var result = new RegistryAnalysisResult();

                // Startup locations to check
                var startupKeys = new[]
                {
                    (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Registry.LocalMachine),
                    (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", Registry.LocalMachine),
                    (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Registry.CurrentUser),
                    (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", Registry.CurrentUser),
                    (@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", Registry.LocalMachine),
                };

                // Additional keys for deep mode
                var deepKeys = new[]
                {
                    (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", Registry.CurrentUser),
                    (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", Registry.LocalMachine),
                    (@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", Registry.LocalMachine),
                    (@"SYSTEM\CurrentControlSet\Services", Registry.LocalMachine),
                };

                var allKeys = mode == ForensicsMode.Deep
                    ? startupKeys.Concat(deepKeys)
                    : startupKeys;

                foreach (var (keyPath, hive) in allKeys)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using var key = hive.OpenSubKey(keyPath, false);
                        if (key == null) continue;

                        result.TotalKeysScanned++;

                        foreach (var valueName in key.GetValueNames())
                        {
                            try
                            {
                                var valueData = key.GetValue(valueName)?.ToString() ?? "";
                                var valueType = key.GetValueKind(valueName).ToString();

                                var entry = new RegistryEntryInfo
                                {
                                    KeyPath = $"{hive.Name}\\{keyPath}",
                                    ValueName = valueName,
                                    ValueData = valueData,
                                    ValueType = valueType
                                };

                                // Check if it's a startup entry
                                if (keyPath.Contains("Run", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.StartupEntriesFound++;
                                    result.StartupEntries.Add(entry);

                                    // Check for suspicious startup entries
                                    CheckRegistrySuspicion(entry, result);
                                }
                            }
                            catch { /* Skip inaccessible values */ }
                        }
                    }
                    catch { /* Skip inaccessible keys */ }
                }

                // Generate findings for suspicious entries
                foreach (var suspicious in result.SuspiciousEntries)
                {
                    result.Findings.Add(new ForensicsFinding
                    {
                        Type = ForensicsFindingType.Registry,
                        Severity = ForensicsSeverity.Medium,
                        Title = $"Suspicious Registry Entry: {suspicious.ValueName}",
                        Description = suspicious.Reason,
                        Details = $"Key: {suspicious.KeyPath}\nValue: {suspicious.ValueData}",
                        Source = "Registry Analysis"
                    });
                }

                return result;
            }, token);
        }

        private void CheckRegistrySuspicion(RegistryEntryInfo entry, RegistryAnalysisResult result)
        {
            var reasons = new List<string>();

            // Check for suspicious paths
            foreach (var suspPath in SuspiciousPaths)
            {
                if (entry.ValueData.Contains(suspPath, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"References suspicious path: {suspPath}");
                    break;
                }
            }

            // Check for PowerShell with encoded commands
            if (entry.ValueData.Contains("powershell", StringComparison.OrdinalIgnoreCase) &&
                entry.ValueData.Contains("-enc", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("PowerShell with encoded command");
            }

            // Check for cmd /c
            if (entry.ValueData.Contains("cmd", StringComparison.OrdinalIgnoreCase) &&
                entry.ValueData.Contains("/c", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("Command shell execution");
            }

            // Check for suspicious keywords
            var suspiciousKeywords = new[] { "mshta", "regsvr32", "rundll32", "wscript", "cscript" };
            foreach (var keyword in suspiciousKeywords)
            {
                if (entry.ValueData.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"Uses suspicious executor: {keyword}");
                    break;
                }
            }

            if (reasons.Count > 0)
            {
                entry.IsSuspicious = true;
                entry.Reason = string.Join("; ", reasons);
                result.SuspiciousEntriesFound++;
                result.SuspiciousEntries.Add(entry);
            }
        }

        #endregion

        #region Log Analysis

        private async Task<LogAnalysisResult> AnalyzeLogsAsync(
            ForensicsMode mode,
            IProgress<string>? progress,
            CancellationToken token)
        {
            return await Task.Run(() =>
            {
                var result = new LogAnalysisResult();

                // Event logs to query
                var logs = mode == ForensicsMode.Lightweight
                    ? new[] { "Security", "System" }
                    : new[] { "Security", "System", "Application", "Microsoft-Windows-PowerShell/Operational" };

                var maxEvents = mode == ForensicsMode.Lightweight ? 500 : 5000;
                var cutoffTime = mode == ForensicsMode.Lightweight
                    ? DateTime.Now.AddHours(-24)
                    : DateTime.Now.AddDays(-7);

                foreach (var logName in logs)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var query = $"*[System[TimeCreated[@SystemTime >= '{cutoffTime:yyyy-MM-ddTHH:mm:ss}']]]";

                        using var eventLog = new System.Diagnostics.Eventing.Reader.EventLogReader(
                            new System.Diagnostics.Eventing.Reader.EventLogQuery(logName, System.Diagnostics.Eventing.Reader.PathType.LogName, query));

                        int count = 0;
                        while (eventLog.ReadEvent() is { } eventRecord && count < maxEvents)
                        {
                            token.ThrowIfCancellationRequested();
                            count++;
                            result.TotalEventsScanned++;

                            var entry = new Models.EventLogEntry
                            {
                                RecordId = eventRecord.RecordId ?? 0,
                                LogName = logName,
                                Source = eventRecord.ProviderName ?? "",
                                EventId = eventRecord.Id,
                                Level = eventRecord.LevelDisplayName ?? eventRecord.Level?.ToString() ?? "Unknown",
                                TimeGenerated = eventRecord.TimeCreated ?? DateTime.MinValue,
                                Message = TruncateMessage(eventRecord.FormatDescription() ?? "", mode),
                                Computer = eventRecord.MachineName ?? ""
                            };

                            // Categorize event
                            if (entry.Level == "Error" || entry.Level == "Critical")
                            {
                                result.ErrorEventsFound++;
                                result.RecentErrors.Add(entry);
                            }
                            else if (entry.Level == "Warning")
                            {
                                result.WarningEventsFound++;
                            }

                            // Check for security events
                            if (logName == "Security")
                            {
                                result.SecurityEventsFound++;

                                // Check for suspicious event IDs
                                if (SuspiciousEventIds.Contains(entry.EventId))
                                {
                                    entry.IsSuspicious = true;
                                    entry.SuspicionReason = GetEventIdDescription(entry.EventId);
                                    result.SuspiciousEventsFound++;
                                    result.SuspiciousEvents.Add(entry);
                                }

                                result.SecurityEvents.Add(entry);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Findings.Add(new ForensicsFinding
                        {
                            Type = ForensicsFindingType.EventLog,
                            Severity = ForensicsSeverity.Low,
                            Title = $"Could not read {logName} log",
                            Description = ex.Message,
                            Source = "Log Analysis"
                        });
                    }
                }

                // Limit results
                result.SecurityEvents = result.SecurityEvents
                    .OrderByDescending(e => e.TimeGenerated)
                    .Take(mode == ForensicsMode.Lightweight ? 100 : 1000)
                    .ToList();

                result.SuspiciousEvents = result.SuspiciousEvents
                    .OrderByDescending(e => e.TimeGenerated)
                    .Take(mode == ForensicsMode.Lightweight ? 50 : 500)
                    .ToList();

                result.RecentErrors = result.RecentErrors
                    .OrderByDescending(e => e.TimeGenerated)
                    .Take(mode == ForensicsMode.Lightweight ? 50 : 500)
                    .ToList();

                // Generate findings for suspicious events
                var groupedEvents = result.SuspiciousEvents
                    .GroupBy(e => e.EventId)
                    .OrderByDescending(g => g.Count())
                    .Take(10);

                foreach (var group in groupedEvents)
                {
                    result.Findings.Add(new ForensicsFinding
                    {
                        Type = ForensicsFindingType.EventLog,
                        Severity = ForensicsSeverity.Medium,
                        Title = $"Security Event {group.Key}: {GetEventIdDescription(group.Key)}",
                        Description = $"Found {group.Count()} occurrences in the analysis period",
                        Details = $"Most recent: {group.First().TimeGenerated}",
                        Source = "Log Analysis"
                    });
                }

                return result;
            }, token);
        }

        private string TruncateMessage(string message, ForensicsMode mode)
        {
            int maxLength = mode == ForensicsMode.Lightweight ? 500 : 2000;
            return message.Length > maxLength ? message[..maxLength] + "..." : message;
        }

        private string GetEventIdDescription(int eventId)
        {
            return eventId switch
            {
                4624 => "Successful logon",
                4625 => "Failed logon attempt",
                4634 => "Account logoff",
                4648 => "Explicit credential logon",
                4672 => "Special privileges assigned",
                4720 => "User account created",
                4722 => "User account enabled",
                4723 => "Password change attempt",
                4724 => "Password reset attempt",
                4725 => "User account disabled",
                4726 => "User account deleted",
                4728 => "Member added to security group",
                4732 => "Member added to local group",
                4740 => "Account lockout",
                4756 => "Member added to universal group",
                4767 => "Account unlocked",
                4768 => "Kerberos TGT requested",
                4769 => "Kerberos service ticket requested",
                4771 => "Kerberos pre-auth failed",
                4776 => "NTLM credential validation",
                4778 => "Session reconnected",
                4779 => "Session disconnected",
                1102 => "Audit log cleared",
                4688 => "Process created",
                4689 => "Process terminated",
                _ => "Security event"
            };
        }

        #endregion

        #region Report Generation

        private async Task GenerateReportAsync(
            ForensicsAnalysisResult result,
            ForensicsMode mode,
            IProgress<string>? progress,
            CancellationToken token)
        {
            var outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PlatypusTools",
                "ForensicsReports");

            Directory.CreateDirectory(outputDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var modeStr = mode == ForensicsMode.Lightweight ? "quick" : "deep";
            var filename = $"forensics_{modeStr}_{timestamp}.json";
            result.OutputPath = Path.Combine(outputDir, filename);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(result, options);
            await File.WriteAllTextAsync(result.OutputPath, json, token);

            result.OutputSizeBytes = new FileInfo(result.OutputPath).Length;

            // For deep mode, also generate detailed text report
            if (mode == ForensicsMode.Deep)
            {
                var textFilename = $"forensics_{modeStr}_{timestamp}_detailed.txt";
                var textPath = Path.Combine(outputDir, textFilename);
                await GenerateDetailedTextReportAsync(result, textPath, token);
            }
        }

        private async Task GenerateDetailedTextReportAsync(
            ForensicsAnalysisResult result,
            string path,
            CancellationToken token)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=" + new string('=', 79));
            sb.AppendLine("FORENSICS ANALYSIS REPORT");
            sb.AppendLine("=" + new string('=', 79));
            sb.AppendLine();
            sb.AppendLine($"Analysis ID: {result.AnalysisId}");
            sb.AppendLine($"Mode: {result.Mode}");
            sb.AppendLine($"Computer: {result.ComputerName}");
            sb.AppendLine($"User: {result.UserName}");
            sb.AppendLine($"OS: {result.OsVersion}");
            sb.AppendLine($"Start Time: {result.StartTime}");
            sb.AppendLine($"End Time: {result.EndTime}");
            sb.AppendLine($"Duration: {result.Duration.TotalMinutes:F2} minutes");
            sb.AppendLine();

            sb.AppendLine("-" + new string('-', 79));
            sb.AppendLine("SUMMARY");
            sb.AppendLine("-" + new string('-', 79));
            sb.AppendLine($"Total Findings: {result.TotalFindings}");
            sb.AppendLine($"  Critical: {result.CriticalFindings}");
            sb.AppendLine($"  High: {result.HighFindings}");
            sb.AppendLine($"  Medium: {result.MediumFindings}");
            sb.AppendLine($"  Low: {result.LowFindings}");
            sb.AppendLine($"  Info: {result.InfoFindings}");
            sb.AppendLine();

            // Memory section
            if (result.MemoryAnalysis != null)
            {
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine("MEMORY ANALYSIS");
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine($"Memory Usage: {result.MemoryAnalysis.UsagePercent:F1}%");
                sb.AppendLine($"Suspicious Processes: {result.MemoryAnalysis.SuspiciousProcesses.Count}");
                sb.AppendLine();

                foreach (var proc in result.MemoryAnalysis.SuspiciousProcesses.Take(50))
                {
                    sb.AppendLine($"  [{proc.ProcessId}] {proc.ProcessName}");
                    sb.AppendLine($"      Path: {proc.ExecutablePath}");
                    sb.AppendLine($"      Reason: {proc.SuspicionReason}");
                    sb.AppendLine();
                }
            }

            // File System section
            if (result.FileSystemAnalysis != null)
            {
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine("FILE SYSTEM ANALYSIS");
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine($"Files Scanned: {result.FileSystemAnalysis.TotalFilesScanned}");
                sb.AppendLine($"Suspicious Files: {result.FileSystemAnalysis.SuspiciousFilesFound}");
                sb.AppendLine($"Executables in Temp: {result.FileSystemAnalysis.ExecutablesInTemp}");
                sb.AppendLine();

                foreach (var file in result.FileSystemAnalysis.SuspiciousFiles.Take(100))
                {
                    sb.AppendLine($"  {file.FileName}");
                    sb.AppendLine($"      Path: {file.FilePath}");
                    sb.AppendLine($"      Reason: {file.Reason}");
                    if (!string.IsNullOrEmpty(file.Hash))
                        sb.AppendLine($"      SHA256: {file.Hash}");
                    sb.AppendLine();
                }
            }

            // Registry section
            if (result.RegistryAnalysis != null)
            {
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine("REGISTRY ANALYSIS");
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine($"Keys Scanned: {result.RegistryAnalysis.TotalKeysScanned}");
                sb.AppendLine($"Startup Entries: {result.RegistryAnalysis.StartupEntriesFound}");
                sb.AppendLine($"Suspicious Entries: {result.RegistryAnalysis.SuspiciousEntriesFound}");
                sb.AppendLine();

                foreach (var entry in result.RegistryAnalysis.SuspiciousEntries.Take(50))
                {
                    sb.AppendLine($"  {entry.ValueName}");
                    sb.AppendLine($"      Key: {entry.KeyPath}");
                    sb.AppendLine($"      Value: {entry.ValueData}");
                    sb.AppendLine($"      Reason: {entry.Reason}");
                    sb.AppendLine();
                }
            }

            // Log section
            if (result.LogAnalysis != null)
            {
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine("LOG ANALYSIS");
                sb.AppendLine("-" + new string('-', 79));
                sb.AppendLine($"Events Scanned: {result.LogAnalysis.TotalEventsScanned}");
                sb.AppendLine($"Security Events: {result.LogAnalysis.SecurityEventsFound}");
                sb.AppendLine($"Suspicious Events: {result.LogAnalysis.SuspiciousEventsFound}");
                sb.AppendLine($"Errors: {result.LogAnalysis.ErrorEventsFound}");
                sb.AppendLine();

                foreach (var evt in result.LogAnalysis.SuspiciousEvents.Take(100))
                {
                    sb.AppendLine($"  [{evt.TimeGenerated}] Event {evt.EventId}");
                    sb.AppendLine($"      Log: {evt.LogName}");
                    sb.AppendLine($"      Reason: {evt.SuspicionReason}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("=" + new string('=', 79));
            sb.AppendLine("END OF REPORT");
            sb.AppendLine("=" + new string('=', 79));

            await File.WriteAllTextAsync(path, sb.ToString(), token);
        }

        #endregion

        #region Utilities

        private static string CalculateFileHash(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Gets system memory information using WMI.
        /// </summary>
        private static (long totalMemory, long availableMemory) GetSystemMemoryInfo()
        {
            long totalMemory = 0;
            long availableMemory = 0;

            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    // WMI returns values in KB, convert to bytes
                    var totalKB = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                    var freeKB = Convert.ToInt64(obj["FreePhysicalMemory"]);
                    totalMemory = totalKB * 1024;
                    availableMemory = freeKB * 1024;
                    break;
                }
            }
            catch
            {
                // Fallback: try to get from GC (less accurate but works)
                try
                {
                    var gcInfo = GC.GetGCMemoryInfo();
                    totalMemory = gcInfo.TotalAvailableMemoryBytes;
                    availableMemory = totalMemory - GC.GetTotalMemory(false);
                }
                catch { }
            }

            return (totalMemory, availableMemory);
        }

        #endregion
    }
}
