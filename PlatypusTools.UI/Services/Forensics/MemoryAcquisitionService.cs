using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Memory acquisition result.
    /// </summary>
    public class MemoryAcquisitionResult
    {
        public bool Success { get; set; }
        public string? OutputPath { get; set; }
        public long FileSize { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public string? WinPmemVersion { get; set; }
    }

    /// <summary>
    /// Service for memory acquisition using WinPmem, Magnet RAM Capture, or ProcDump.
    /// Extracted from AdvancedForensicsViewModel for better modularity.
    /// </summary>
    public class MemoryAcquisitionService : ForensicOperationBase
    {
        private static readonly HttpClient _httpClient = new();
        
        public override string OperationName => "Memory Acquisition";

        /// <summary>
        /// Path to WinPmem executable.
        /// </summary>
        public string? WinPmemPath { get; set; }

        /// <summary>
        /// Path to Magnet RAM Capture executable.
        /// </summary>
        public string? MagnetRamCapturePath { get; set; }

        /// <summary>
        /// Path to ProcDump executable.
        /// </summary>
        public string? ProcDumpPath { get; set; }

        /// <summary>
        /// Output directory for memory dumps.
        /// </summary>
        public string OutputPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MemoryDumps");

        /// <summary>
        /// Dump format (raw, aff4, crashdump).
        /// </summary>
        public string Format { get; set; } = "raw";

        /// <summary>
        /// Checks if current process has administrator privileges.
        /// </summary>
        public static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Acquires full system memory dump.
        /// </summary>
        public async Task<MemoryAcquisitionResult> AcquireMemoryAsync(CancellationToken cancellationToken = default)
        {
            var result = new MemoryAcquisitionResult();
            var stopwatch = Stopwatch.StartNew();

            await ExecuteWithHandlingAsync(async (token) =>
            {
                LogHeader("Memory Acquisition");
                
                // Check administrator privileges
                if (!IsAdministrator())
                {
                    LogWarning("Administrator privileges required for memory acquisition!");
                    LogInfo("Please restart the application as Administrator.");
                    result.ErrorMessage = "Administrator privileges required";
                    return;
                }
                LogSuccess("Administrator privileges confirmed");

                // Find available tool
                var toolPath = FindAvailableTool();
                if (string.IsNullOrEmpty(toolPath))
                {
                    LogError("No memory acquisition tool found.");
                    LogInfo("Please download WinPmem from: https://github.com/Velocidex/WinPmem/releases");
                    result.ErrorMessage = "No memory acquisition tool available";
                    return;
                }

                // Prepare output
                Directory.CreateDirectory(OutputPath);
                var hostname = Environment.MachineName;
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var outputFile = Path.Combine(OutputPath, $"{hostname}_memory_{timestamp}.raw");

                // Check disk space
                var driveInfo = new DriveInfo(Path.GetPathRoot(OutputPath) ?? "C:");
                var freeSpaceGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var ramGB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0);
                
                Log($"Available disk space: {freeSpaceGB:F2} GB");
                Log($"System RAM: {ramGB:F2} GB");

                if (freeSpaceGB < ramGB * 1.1)
                {
                    LogError($"Insufficient disk space. Need at least {ramGB * 1.1:F2} GB, have {freeSpaceGB:F2} GB");
                    result.ErrorMessage = "Insufficient disk space";
                    return;
                }

                // Determine tool and arguments
                var (args, toolName) = GetToolArguments(toolPath, outputFile);
                
                Log($"Tool: {toolName}");
                Log($"Command: {toolPath}");
                Log($"Arguments: {args}");
                Log($"Output: {outputFile}");
                Log("");

                StatusMessage = "Acquiring memory dump (this may take several minutes)...";

                // Run the tool
                var psi = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                // Stream output
                var outputTask = Task.Run(async () =>
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                            Log(line);
                    }
                }, token);

                var errorTask = Task.Run(async () =>
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                            Log($"[STDERR] {line}");
                    }
                }, token);

                await process.WaitForExitAsync(token);
                await Task.WhenAll(outputTask, errorTask);

                if (process.ExitCode == 0 && File.Exists(outputFile))
                {
                    var fileInfo = new FileInfo(outputFile);
                    result.Success = true;
                    result.OutputPath = outputFile;
                    result.FileSize = fileInfo.Length;
                    
                    Log("");
                    LogSuccess($"Memory dump acquired successfully!");
                    Log($"  Size: {fileInfo.Length / (1024.0 * 1024.0 * 1024.0):F2} GB");
                    Log($"  Path: {outputFile}");
                }
                else
                {
                    result.ErrorMessage = $"Process exited with code {process.ExitCode}";
                    LogError($"Memory acquisition failed (exit code: {process.ExitCode})");
                }
            });

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        /// <summary>
        /// Downloads WinPmem from GitHub.
        /// </summary>
        public async Task<string?> DownloadWinPmemAsync(CancellationToken cancellationToken = default)
        {
            LogHeader("Downloading WinPmem");
            
            try
            {
                var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                Directory.CreateDirectory(toolsDir);

                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PlatypusTools/3.2.0");

                // Get latest release info
                const string apiUrl = "https://api.github.com/repos/Velocidex/WinPmem/releases/latest";
                var response = await _httpClient.GetStringAsync(apiUrl, cancellationToken);
                using var doc = JsonDocument.Parse(response);
                var assets = doc.RootElement.GetProperty("assets");

                string? downloadUrl = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name?.Contains("winpmem", StringComparison.OrdinalIgnoreCase) == true &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    LogError("Could not find WinPmem download URL");
                    return null;
                }

                var destPath = Path.Combine(toolsDir, "winpmem.exe");
                Log($"Downloading from: {downloadUrl}");

                var bytes = await _httpClient.GetByteArrayAsync(downloadUrl, cancellationToken);
                await File.WriteAllBytesAsync(destPath, bytes, cancellationToken);

                LogSuccess($"Downloaded to: {destPath}");
                WinPmemPath = destPath;
                return destPath;
            }
            catch (Exception ex)
            {
                LogError($"Download failed: {ex.Message}");
                return null;
            }
        }

        private string? FindAvailableTool()
        {
            if (!string.IsNullOrEmpty(WinPmemPath) && File.Exists(WinPmemPath))
                return WinPmemPath;
            if (!string.IsNullOrEmpty(MagnetRamCapturePath) && File.Exists(MagnetRamCapturePath))
                return MagnetRamCapturePath;
            if (!string.IsNullOrEmpty(ProcDumpPath) && File.Exists(ProcDumpPath))
                return ProcDumpPath;

            // Check default locations
            var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            var defaultWinPmem = Path.Combine(toolsDir, "winpmem.exe");
            if (File.Exists(defaultWinPmem))
            {
                WinPmemPath = defaultWinPmem;
                return defaultWinPmem;
            }

            return null;
        }

        private (string args, string toolName) GetToolArguments(string toolPath, string outputFile)
        {
            var fileName = Path.GetFileName(toolPath).ToLowerInvariant();

            if (fileName.Contains("winpmem"))
            {
                var args = Format.ToLowerInvariant() switch
                {
                    "aff4" => $"--format aff4 -o \"{outputFile.Replace(".raw", ".aff4")}\"",
                    "crashdump" => $"--format crashdump -o \"{outputFile.Replace(".raw", ".dmp")}\"",
                    _ => $"--format raw -o \"{outputFile}\""
                };
                return (args, "WinPmem");
            }

            if (fileName.Contains("magnet") || fileName.Contains("mrc"))
            {
                return ($"/accepteula /go \"{outputFile}\"", "Magnet RAM Capture");
            }

            if (fileName.Contains("procdump"))
            {
                return ($"-ma lsass.exe \"{outputFile}\"", "ProcDump");
            }

            return ($"-o \"{outputFile}\"", "Unknown");
        }
    }
}
