using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Monitors the Windows Recent folder in real-time using FileSystemWatcher.
    /// Automatically deletes any new .lnk shortcut whose target points to a blocked directory.
    /// Also cleans AutomaticDestinations (jump lists) and notifies the shell to refresh.
    /// Provides support for creating Windows Scheduled Tasks for cleanup when app is closed.
    /// </summary>
    public class RecentBlockerService : IDisposable
    {
        private static RecentBlockerService? _instance;
        public static RecentBlockerService Instance => _instance ??= new RecentBlockerService();

        // P/Invoke: tell Explorer the Recent folder changed
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const int SHCNE_UPDATEDIR = 0x00001000;
        private const int SHCNE_ALLEVENTS = 0x7FFFFFFF;
        private const uint SHCNF_PATHW = 0x0005;

        private FileSystemWatcher? _watcher;
        private FileSystemWatcher? _autoDestWatcher;
        private readonly List<string> _blockedDirectories = new();
        private bool _includeSubDirs = true;
        private bool _isRunning;
        private readonly string _configFilePath;
        private readonly string _recentFolder;
        private readonly string _autoDestFolder;
        private readonly string _customDestFolder;
        private int _blockedCount;
        private readonly object _lock = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Timer> _pendingTimers = new();
        private Timer? _autoDestDebounceTimer;
        private Timer? _periodicCleanupTimer;

        // Known Explorer AppID for the Home/Recent view
        private const string ExplorerJumpListId = "f01b4d95cf55d32a";

        public event EventHandler<RecentBlockedEventArgs>? ItemBlocked;
        public event EventHandler<bool>? StatusChanged;

        public bool IsRunning => _isRunning;
        public int BlockedCount => _blockedCount;
        public IReadOnlyList<string> BlockedDirectories => _blockedDirectories.AsReadOnly();

        public RecentBlockerService()
        {
            var appData = SettingsManager.DataDirectory;
            _configFilePath = Path.Combine(appData, "recent_blocker.json");

            _recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (string.IsNullOrEmpty(_recentFolder) || !Directory.Exists(_recentFolder))
            {
                _recentFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Recent");
            }

            _autoDestFolder = Path.Combine(_recentFolder, "AutomaticDestinations");
            _customDestFolder = Path.Combine(_recentFolder, "CustomDestinations");

            LoadConfig();
        }

        #region Configuration

        public void AddBlockedDirectory(string directory)
        {
            lock (_lock)
            {
                var normalized = NormalizePath(directory);
                if (!string.IsNullOrEmpty(normalized) && !_blockedDirectories.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    _blockedDirectories.Add(normalized);
                    SaveConfig();
                }
            }
        }

        public void RemoveBlockedDirectory(string directory)
        {
            lock (_lock)
            {
                var normalized = NormalizePath(directory);
                _blockedDirectories.RemoveAll(d => string.Equals(d, normalized, StringComparison.OrdinalIgnoreCase));
                SaveConfig();
            }
        }

        public void SetBlockedDirectories(IEnumerable<string> directories)
        {
            lock (_lock)
            {
                _blockedDirectories.Clear();
                foreach (var dir in directories)
                {
                    var normalized = NormalizePath(dir);
                    if (!string.IsNullOrEmpty(normalized))
                        _blockedDirectories.Add(normalized);
                }
                SaveConfig();
            }
        }

        public void SetIncludeSubDirs(bool include)
        {
            _includeSubDirs = include;
            SaveConfig();
        }

        #endregion

        #region Watcher Control

        /// <summary>
        /// Starts the real-time Recent folder watcher.
        /// New .lnk files whose targets match blocked directories are deleted immediately.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            if (_blockedDirectories.Count == 0) return;
            if (!Directory.Exists(_recentFolder)) return;

            try
            {
                // Watch .lnk files in the Recent folder
                _watcher = new FileSystemWatcher(_recentFolder)
                {
                    Filter = "*.lnk",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnRecentFileCreated;
                _watcher.Renamed += OnRecentFileRenamed;

                // Also watch AutomaticDestinations folder (jump lists)
                if (Directory.Exists(_autoDestFolder))
                {
                    _autoDestWatcher = new FileSystemWatcher(_autoDestFolder)
                    {
                        Filter = "*.automaticDestinations-ms",
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    _autoDestWatcher.Changed += OnAutoDestChanged;
                    _autoDestWatcher.Created += OnAutoDestChanged;
                }

                _isRunning = true;
                StatusChanged?.Invoke(this, true);

                // Do an initial sweep to clean any existing matches
                CleanExistingRecent();

                // Also purge jump list files so Explorer rebuilds from current .lnk state
                PurgeJumpListFiles();

                // Force Explorer to refresh
                NotifyShellRecentChanged();

                // Start a periodic cleanup timer (every 3 seconds) to catch anything missed
                _periodicCleanupTimer = new Timer(_ =>
                {
                    try
                    {
                        var cleaned = CleanExistingRecentQuiet();
                        if (cleaned > 0)
                        {
                            PurgeJumpListFiles();
                            NotifyShellRecentChanged();
                        }
                    }
                    catch { }
                }, null, 3000, 3000);

                Debug.WriteLine($"RecentBlockerService started. Watching: {_recentFolder}, Blocking {_blockedDirectories.Count} directories.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting RecentBlockerService: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the real-time watcher.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnRecentFileCreated;
                    _watcher.Renamed -= OnRecentFileRenamed;
                    _watcher.Dispose();
                    _watcher = null;
                }

                if (_autoDestWatcher != null)
                {
                    _autoDestWatcher.EnableRaisingEvents = false;
                    _autoDestWatcher.Changed -= OnAutoDestChanged;
                    _autoDestWatcher.Created -= OnAutoDestChanged;
                    _autoDestWatcher.Dispose();
                    _autoDestWatcher = null;
                }

                _periodicCleanupTimer?.Dispose();
                _periodicCleanupTimer = null;

                // Dispose all pending per-file timers
                foreach (var kvp in _pendingTimers)
                {
                    kvp.Value.Dispose();
                }
                _pendingTimers.Clear();

                _isRunning = false;
                StatusChanged?.Invoke(this, false);
                Debug.WriteLine("RecentBlockerService stopped.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping RecentBlockerService: {ex.Message}");
            }
        }

        #endregion

        #region Windows Scheduled Task

        /// <summary>
        /// Creates a real Windows Scheduled Task (schtasks.exe) that runs a PowerShell cleanup script
        /// at the specified interval. Works even when PlatypusTools is not running.
        /// </summary>
        public (bool Success, string Message) CreateWindowsScheduledTask(
            IEnumerable<string> directories,
            bool includeSubDirs,
            string frequency = "DAILY",
            string taskName = "PlatypusTools_RecentCleaner")
        {
            try
            {
                var dirs = directories.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                if (dirs.Count == 0)
                    return (false, "No directories specified");

                // Create the PowerShell cleanup script
                var scriptDir = Path.Combine(SettingsManager.DataDirectory, "Scripts");
                Directory.CreateDirectory(scriptDir);
                var scriptPath = Path.Combine(scriptDir, $"{taskName}.ps1");

                var dirsArrayText = string.Join(",\n    ", dirs.Select(d => $"'{d.Replace("'", "''")}'"));
                var scriptContent = $@"# PlatypusTools Recent Cleaner - Auto-generated script
# Removes recent shortcuts pointing to blocked directories
# Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}

$blockedDirs = @(
    {dirsArrayText}
)
$includeSubDirs = ${(includeSubDirs ? "true" : "false")}

$recentPath = [Environment]::GetFolderPath('Recent')
if (-not $recentPath) {{
    $recentPath = Join-Path $env:APPDATA 'Microsoft\Windows\Recent'
}}

if (-not (Test-Path $recentPath)) {{ exit 0 }}

$shell = New-Object -ComObject WScript.Shell
$removed = 0

Get-ChildItem -Path $recentPath -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {{
    try {{
        $lnk = $shell.CreateShortcut($_.FullName)
        $target = $lnk.TargetPath
        if (-not $target) {{ return }}

        foreach ($dir in $blockedDirs) {{
            $normalizedDir = $dir.TrimEnd('\')
            $normalizedTarget = $target.TrimEnd('\')

            if ($includeSubDirs) {{
                if ($normalizedTarget -like ""$normalizedDir\*"" -or $normalizedTarget -eq $normalizedDir) {{
                    Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
                    $removed++
                    break
                }}
            }} else {{
                $parentDir = Split-Path $normalizedTarget -Parent
                if ($parentDir -eq $normalizedDir) {{
                    Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
                    $removed++
                    break
                }}
            }}
        }}
    }} catch {{ }}
}}

if ($removed -gt 0) {{
    Write-Output ""Removed $removed recent shortcuts""
}}
";

                File.WriteAllText(scriptPath, scriptContent);

                // Delete existing task if it exists
                RunSchtasks($"/Delete /TN \"{taskName}\" /F");

                // Build schtasks arguments
                var scheduleArg = frequency.ToUpperInvariant() switch
                {
                    "MINUTE" => "/SC MINUTE /MO 5",
                    "HOURLY" => "/SC HOURLY",
                    "DAILY" => "/SC DAILY /ST 03:00",
                    "WEEKLY" => "/SC WEEKLY /D MON /ST 03:00",
                    "MONTHLY" => "/SC MONTHLY /D 1 /ST 03:00",
                    "ONLOGON" => "/SC ONLOGON",
                    "ONIDLE" => "/SC ONIDLE /I 10",
                    _ => "/SC DAILY /ST 03:00"
                };

                var createArgs = $"/Create /TN \"{taskName}\" {scheduleArg} " +
                    $"/TR \"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \\\"{scriptPath}\\\"\" " +
                    $"/RL HIGHEST /F";

                var (exitCode, output) = RunSchtasks(createArgs);

                if (exitCode == 0)
                {
                    return (true, $"Windows Scheduled Task '{taskName}' created successfully.\n" +
                        $"Schedule: {frequency}\n" +
                        $"Script: {scriptPath}\n" +
                        $"Blocking {dirs.Count} director{(dirs.Count == 1 ? "y" : "ies")}");
                }
                else
                {
                    return (false, $"Failed to create scheduled task. schtasks output:\n{output}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error creating Windows scheduled task: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes the Windows Scheduled Task if it exists.
        /// </summary>
        public (bool Success, string Message) RemoveWindowsScheduledTask(string taskName = "PlatypusTools_RecentCleaner")
        {
            try
            {
                var (exitCode, output) = RunSchtasks($"/Delete /TN \"{taskName}\" /F");

                // Also clean up the script file
                var scriptPath = Path.Combine(SettingsManager.DataDirectory, "Scripts", $"{taskName}.ps1");
                if (File.Exists(scriptPath))
                    File.Delete(scriptPath);

                if (exitCode == 0)
                    return (true, $"Windows Scheduled Task '{taskName}' removed.");
                else
                    return (false, $"Task may not exist or could not be removed:\n{output}");
            }
            catch (Exception ex)
            {
                return (false, $"Error removing scheduled task: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the Windows Scheduled Task exists.
        /// </summary>
        public bool WindowsScheduledTaskExists(string taskName = "PlatypusTools_RecentCleaner")
        {
            try
            {
                var (exitCode, _) = RunSchtasks($"/Query /TN \"{taskName}\"");
                return exitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private (int ExitCode, string Output) RunSchtasks(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return (-1, "Failed to start schtasks.exe");

                var output = proc.StandardOutput.ReadToEnd();
                var error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(10000);

                return (proc.ExitCode, string.IsNullOrEmpty(output) ? error : output);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        #endregion

        #region FileSystemWatcher Handlers

        private void OnRecentFileCreated(object sender, FileSystemEventArgs e)
        {
            // Use per-file timer so rapid creations of multiple files all get handled
            ScheduleCheck(e.FullPath);
        }

        private void OnRecentFileRenamed(object sender, RenamedEventArgs e)
        {
            if (e.FullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleCheck(e.FullPath);
            }
        }

        private void ScheduleCheck(string fullPath)
        {
            // Dispose any existing timer for this exact path
            if (_pendingTimers.TryRemove(fullPath, out var existing))
                existing.Dispose();

            var timer = new Timer(_ =>
            {
                if (_pendingTimers.TryRemove(fullPath, out Timer? removed))
                    removed?.Dispose();
                CheckAndDeleteIfBlocked(fullPath);
            }, null, 100, Timeout.Infinite);

            _pendingTimers[fullPath] = timer;
        }

        private void OnAutoDestChanged(object sender, FileSystemEventArgs e)
        {
            // When a jump list file is modified, schedule cleanup of .lnk files
            // The jump list change often means new items were added to Recent
            _autoDestDebounceTimer?.Dispose();
            _autoDestDebounceTimer = new Timer(_ =>
            {
                try
                {
                    // Re-scan and clean any .lnk files that appeared
                    var cleaned = CleanExistingRecent();
                    if (cleaned > 0)
                    {
                        NotifyShellRecentChanged();
                        Debug.WriteLine($"RecentBlocker: AutoDest trigger cleaned {cleaned} shortcuts");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RecentBlocker: AutoDest cleanup error: {ex.Message}");
                }
            }, null, 300, Timeout.Infinite);
        }

        private void CheckAndDeleteIfBlocked(string lnkPath)
        {
            try
            {
                if (!File.Exists(lnkPath)) return;

                var target = ResolveShortcutTarget(lnkPath);
                if (string.IsNullOrEmpty(target)) return;

                string normalizedTarget;
                try { normalizedTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch { normalizedTarget = target; }

                lock (_lock)
                {
                    foreach (var blockedDir in _blockedDirectories)
                    {
                        bool isMatch;
                        if (_includeSubDirs)
                        {
                            isMatch = normalizedTarget.StartsWith(blockedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                      normalizedTarget.Equals(blockedDir, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            var parent = Path.GetDirectoryName(normalizedTarget) ?? "";
                            isMatch = string.Equals(parent, blockedDir, StringComparison.OrdinalIgnoreCase);
                        }

                        if (isMatch)
                        {
                            try
                            {
                                File.Delete(lnkPath);
                                Interlocked.Increment(ref _blockedCount);
                                Debug.WriteLine($"RecentBlocker: Deleted {Path.GetFileName(lnkPath)} (target: {target})");

                                // Notify the shell so Explorer refreshes its Recent view
                                NotifyShellRecentChanged();

                                // Also purge jump list files so Explorer doesn't keep showing this item
                                PurgeJumpListFiles();

                                ItemBlocked?.Invoke(this, new RecentBlockedEventArgs
                                {
                                    ShortcutPath = lnkPath,
                                    TargetPath = target,
                                    BlockedDirectory = blockedDir,
                                    Timestamp = DateTime.Now
                                });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"RecentBlocker: Failed to delete {lnkPath}: {ex.Message}");
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RecentBlocker: Error checking {lnkPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans existing Recent shortcuts and removes any matching blocked directories.
        /// Also purges jump list files so Explorer rebuilds from current state.
        /// </summary>
        public int CleanExistingRecent()
        {
            if (_blockedDirectories.Count == 0) return 0;

            var results = RecentCleaner.RemoveRecentShortcuts(_blockedDirectories, dryRun: false, includeSubDirs: _includeSubDirs);
            var count = results.Count;
            _blockedCount += count;

            if (count > 0)
            {
                Debug.WriteLine($"RecentBlocker: Sweep removed {count} existing shortcuts.");
                PurgeJumpListFiles();
                NotifyShellRecentChanged();
            }

            return count;
        }

        /// <summary>
        /// Same as CleanExistingRecent but doesn't update blocked count (used by periodic timer).
        /// </summary>
        private int CleanExistingRecentQuiet()
        {
            if (_blockedDirectories.Count == 0) return 0;

            var results = RecentCleaner.RemoveRecentShortcuts(_blockedDirectories, dryRun: false, includeSubDirs: _includeSubDirs);
            return results.Count;
        }

        /// <summary>
        /// Deletes AutomaticDestinations and CustomDestinations jump list files.
        /// This forces Explorer to rebuild its Recent items list from the remaining .lnk files.
        /// Without this, Explorer keeps showing deleted items from its cached jump list data.
        /// </summary>
        private void PurgeJumpListFiles()
        {
            try
            {
                // Delete Explorer's own jump list (this is what populates the Home > Recent view)
                if (Directory.Exists(_autoDestFolder))
                {
                    var explorerJumpList = Path.Combine(_autoDestFolder, $"{ExplorerJumpListId}.automaticDestinations-ms");
                    if (File.Exists(explorerJumpList))
                    {
                        try
                        {
                            File.Delete(explorerJumpList);
                            Debug.WriteLine("RecentBlocker: Deleted Explorer jump list file.");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"RecentBlocker: Could not delete Explorer jump list: {ex.Message}");
                        }
                    }

                    // Also delete other recently modified jump list files that may contain blocked entries
                    try
                    {
                        foreach (var file in Directory.GetFiles(_autoDestFolder, "*.automaticDestinations-ms")
                            .Where(f => !Path.GetFileName(f).StartsWith(ExplorerJumpListId))
                            .Where(f => (DateTime.Now - File.GetLastWriteTime(f)).TotalSeconds < 10))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                    catch { }
                }

                // Also clean recently modified CustomDestinations
                if (Directory.Exists(_customDestFolder))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(_customDestFolder, "*.customDestinations-ms")
                            .Where(f => (DateTime.Now - File.GetLastWriteTime(f)).TotalSeconds < 10))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RecentBlocker: Error purging jump lists: {ex.Message}");
            }
        }

        /// <summary>
        /// Notifies the Windows Shell that the Recent folder has changed,
        /// forcing Explorer to refresh its Recent view.
        /// </summary>
        private void NotifyShellRecentChanged()
        {
            try
            {
                var recentPtr = Marshal.StringToCoTaskMemUni(_recentFolder);
                try
                {
                    SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, recentPtr, IntPtr.Zero);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(recentPtr);
                }
                Debug.WriteLine("RecentBlocker: Shell notified of Recent folder change.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RecentBlocker: SHChangeNotify failed: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private string? ResolveShortcutTarget(string lnkPath)
        {
            try
            {
                var wsh = Type.GetTypeFromProgID("WScript.Shell");
                if (wsh != null)
                {
                    var shellObj = Activator.CreateInstance(wsh);
                    if (shellObj != null)
                    {
                        dynamic shell = shellObj;
                        dynamic lnk = shell.CreateShortcut(lnkPath);
                        return (string?)lnk?.TargetPath;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        #endregion

        #region Persistence

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<RecentBlockerConfig>(json);
                    if (config != null)
                    {
                        _blockedDirectories.Clear();
                        _blockedDirectories.AddRange(config.BlockedDirectories ?? new List<string>());
                        _includeSubDirs = config.IncludeSubDirs;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading RecentBlocker config: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                var config = new RecentBlockerConfig
                {
                    BlockedDirectories = _blockedDirectories.ToList(),
                    IncludeSubDirs = _includeSubDirs
                };
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving RecentBlocker config: {ex.Message}");
            }
        }

        private class RecentBlockerConfig
        {
            public List<string> BlockedDirectories { get; set; } = new();
            public bool IncludeSubDirs { get; set; } = true;
        }

        #endregion

        public void Dispose()
        {
            Stop();
            _autoDestDebounceTimer?.Dispose();
            _periodicCleanupTimer?.Dispose();
            foreach (var kvp in _pendingTimers)
                kvp.Value.Dispose();
            _pendingTimers.Clear();
            GC.SuppressFinalize(this);
        }
    }

    public class RecentBlockedEventArgs : EventArgs
    {
        public string ShortcutPath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string BlockedDirectory { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
