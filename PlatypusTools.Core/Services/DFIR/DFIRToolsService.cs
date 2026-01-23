using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.DFIR
{
    /// <summary>
    /// Service for integrating with external DFIR tools like Volatility, KAPE, and WinPmem.
    /// Provides memory acquisition and forensic artifact collection capabilities.
    /// </summary>
    public class DFIRToolsService
    {
        private readonly string _toolsPath;
        
        public DFIRToolsService()
        {
            _toolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PlatypusTools", "DFIR");
        }
        
        #region Memory Acquisition (SEC-006)
        
        /// <summary>
        /// Checks if WinPmem is available.
        /// </summary>
        public bool IsWinPmemAvailable => File.Exists(GetWinPmemPath());
        
        private string GetWinPmemPath() => Path.Combine(_toolsPath, "winpmem_mini_x64.exe");
        
        /// <summary>
        /// Acquires memory dump using WinPmem.
        /// </summary>
        public async Task<MemoryAcquisitionResult> AcquireMemoryAsync(
            string outputPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new MemoryAcquisitionResult
            {
                StartTime = DateTime.Now
            };
            
            if (!IsWinPmemAvailable)
            {
                result.Success = false;
                result.ErrorMessage = "WinPmem not found. Please download and install it.";
                return result;
            }
            
            try
            {
                progress?.Report("Starting memory acquisition...");
                
                var outputFile = Path.Combine(outputPath, $"memdump_{DateTime.Now:yyyyMMdd_HHmmss}.raw");
                
                var psi = new ProcessStartInfo
                {
                    FileName = GetWinPmemPath(),
                    Arguments = outputFile,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas" // Requires admin
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start WinPmem process.";
                    return result;
                }
                
                var output = await process.StandardOutput.ReadToEndAsync(ct);
                var error = await process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                
                if (process.ExitCode == 0 && File.Exists(outputFile))
                {
                    result.Success = true;
                    result.OutputPath = outputFile;
                    result.FileSize = new FileInfo(outputFile).Length;
                    progress?.Report($"Memory dump saved: {outputFile} ({result.FileSize / (1024 * 1024):N0} MB)");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"WinPmem failed: {error}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            result.EndTime = DateTime.Now;
            return result;
        }
        
        #endregion
        
        #region Volatility 3 Integration (SEC-007)
        
        /// <summary>
        /// Checks if Volatility 3 is available.
        /// </summary>
        public bool IsVolatilityAvailable => File.Exists(GetVolatilityPath()) || IsVolatilityInPath();
        
        private string GetVolatilityPath() => Path.Combine(_toolsPath, "vol.py");
        
        private bool IsVolatilityInPath()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "vol",
                    Arguments = "--help",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                return process != null;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Available Volatility 3 plugins.
        /// </summary>
        public static readonly string[] VolatilityPlugins = new[]
        {
            "windows.pslist", "windows.pstree", "windows.netscan", "windows.netstat",
            "windows.dlllist", "windows.handles", "windows.cmdline", "windows.malfind",
            "windows.filescan", "windows.registry.hivelist", "windows.registry.printkey",
            "windows.svcscan", "windows.driverirp", "windows.ssdt", "windows.callbacks"
        };
        
        /// <summary>
        /// Runs Volatility 3 analysis on a memory dump.
        /// </summary>
        public async Task<VolatilityResult> RunVolatilityAnalysisAsync(
            string memoryDumpPath,
            string plugin,
            string outputPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new VolatilityResult
            {
                Plugin = plugin,
                StartTime = DateTime.Now
            };
            
            if (!File.Exists(memoryDumpPath))
            {
                result.Success = false;
                result.ErrorMessage = "Memory dump file not found.";
                return result;
            }
            
            try
            {
                progress?.Report($"Running Volatility plugin: {plugin}...");
                
                var outputFile = Path.Combine(outputPath, $"vol_{plugin.Replace(".", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var volExe = IsVolatilityInPath() ? "vol" : $"python \"{GetVolatilityPath()}\"";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {volExe} -f \"{memoryDumpPath}\" -o \"{outputFile}\" --renderer=json {plugin}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start Volatility process.";
                    return result;
                }
                
                var output = await process.StandardOutput.ReadToEndAsync(ct);
                var error = await process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                
                if (process.ExitCode == 0 || File.Exists(outputFile))
                {
                    result.Success = true;
                    result.OutputPath = outputFile;
                    
                    // Parse output if JSON exists
                    if (File.Exists(outputFile))
                    {
                        var json = await File.ReadAllTextAsync(outputFile, ct);
                        result.OutputData = JsonSerializer.Deserialize<object>(json);
                    }
                    else
                    {
                        result.RawOutput = output;
                    }
                    
                    progress?.Report($"Plugin {plugin} completed successfully.");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = error;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            result.EndTime = DateTime.Now;
            return result;
        }
        
        #endregion
        
        #region KAPE Integration (SEC-008)
        
        /// <summary>
        /// Checks if KAPE is available.
        /// </summary>
        public bool IsKapeAvailable => File.Exists(GetKapePath());
        
        private string GetKapePath() => Path.Combine(_toolsPath, "kape.exe");
        
        /// <summary>
        /// Available KAPE targets for artifact collection.
        /// </summary>
        public static readonly string[] KapeTargets = new[]
        {
            "!SANS_Triage", "!BasicCollection", "EventLogs", "RegistryHives",
            "Prefetch", "SRUM", "RecycleBin", "RecentFiles", "AmCache",
            "BrowserHistory", "Antivirus", "CloudStorage", "USB"
        };
        
        /// <summary>
        /// Available KAPE modules for processing.
        /// </summary>
        public static readonly string[] KapeModules = new[]
        {
            "!EZParser", "EvtxECmd", "RECmd", "PECmd", "AmcacheParser",
            "AppCompatCacheParser", "MFTECmd", "JLECmd", "LECmd", "SBECmd"
        };
        
        /// <summary>
        /// Runs KAPE artifact collection.
        /// </summary>
        public async Task<KapeResult> RunKapeCollectionAsync(
            string targetDrive,
            string outputPath,
            IEnumerable<string> targets,
            IEnumerable<string>? modules = null,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new KapeResult
            {
                StartTime = DateTime.Now,
                Targets = targets.ToList()
            };
            
            if (!IsKapeAvailable)
            {
                result.Success = false;
                result.ErrorMessage = "KAPE not found. Please download and install it.";
                return result;
            }
            
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var targetStr = string.Join(",", targets);
                var args = $"--tsource {targetDrive} --tdest \"{outputPath}\\{timestamp}\\Targets\" --target {targetStr}";
                
                if (modules?.Any() == true)
                {
                    var moduleStr = string.Join(",", modules);
                    args += $" --msource \"{outputPath}\\{timestamp}\\Targets\" --mdest \"{outputPath}\\{timestamp}\\Modules\" --module {moduleStr}";
                    result.Modules = modules.ToList();
                }
                
                progress?.Report($"Running KAPE collection: {targetStr}...");
                
                var psi = new ProcessStartInfo
                {
                    FileName = GetKapePath(),
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start KAPE process.";
                    return result;
                }
                
                // Stream output
                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        progress?.Report(e.Data);
                };
                process.BeginOutputReadLine();
                
                await process.WaitForExitAsync(ct);
                
                result.Success = process.ExitCode == 0;
                result.OutputPath = Path.Combine(outputPath, timestamp);
                
                if (result.Success)
                {
                    // Count collected files
                    if (Directory.Exists(result.OutputPath))
                    {
                        result.FilesCollected = Directory.GetFiles(result.OutputPath, "*", SearchOption.AllDirectories).Length;
                        progress?.Report($"KAPE collection complete: {result.FilesCollected} files collected.");
                    }
                }
                else
                {
                    result.ErrorMessage = await process.StandardError.ReadToEndAsync(ct);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            result.EndTime = DateTime.Now;
            return result;
        }
        
        #endregion
        
        #region OpenSearch Export (SEC-009)
        
        /// <summary>
        /// Exports forensic data to OpenSearch/Elasticsearch.
        /// </summary>
        public async Task<ExportResult> ExportToOpenSearchAsync(
            object data,
            string opensearchUrl,
            string indexName,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new ExportResult
            {
                StartTime = DateTime.Now,
                Destination = opensearchUrl
            };
            
            try
            {
                progress?.Report($"Exporting to OpenSearch: {opensearchUrl}/{indexName}...");
                
                using var client = new System.Net.Http.HttpClient();
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                
                using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var response = await client.PostAsync($"{opensearchUrl}/{indexName}/_doc", content, ct);
                
                if (response.IsSuccessStatusCode)
                {
                    result.Success = true;
                    result.RecordsExported = 1;
                    progress?.Report("Export successful.");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = await response.Content.ReadAsStringAsync(ct);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            result.EndTime = DateTime.Now;
            return result;
        }
        
        /// <summary>
        /// Bulk exports multiple documents to OpenSearch.
        /// </summary>
        public async Task<ExportResult> BulkExportToOpenSearchAsync(
            IEnumerable<object> documents,
            string opensearchUrl,
            string indexName,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new ExportResult
            {
                StartTime = DateTime.Now,
                Destination = opensearchUrl
            };
            
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var bulkBody = new System.Text.StringBuilder();
                
                int count = 0;
                foreach (var doc in documents)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    // Bulk format: action line + document line
                    bulkBody.AppendLine($"{{\"index\":{{\"_index\":\"{indexName}\"}}}}");
                    bulkBody.AppendLine(JsonSerializer.Serialize(doc));
                    count++;
                }
                
                progress?.Report($"Sending {count} documents to OpenSearch...");
                
                using var content = new System.Net.Http.StringContent(bulkBody.ToString(), System.Text.Encoding.UTF8, "application/x-ndjson");
                using var response = await client.PostAsync($"{opensearchUrl}/_bulk", content, ct);
                
                if (response.IsSuccessStatusCode)
                {
                    result.Success = true;
                    result.RecordsExported = count;
                    progress?.Report($"Exported {count} documents successfully.");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = await response.Content.ReadAsStringAsync(ct);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            result.EndTime = DateTime.Now;
            return result;
        }
        
        #endregion
        
        #region Plaso/log2timeline Integration (SEC-010)
        
        /// <summary>
        /// Checks if Plaso is available.
        /// </summary>
        public bool IsPlasoAvailable => IsToolInPath("log2timeline.py") || File.Exists(GetPlasoPath());
        
        private string GetPlasoPath() => Path.Combine(_toolsPath, "log2timeline.py");
        
        private bool IsToolInPath(string toolName)
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            return pathVar.Split(';').Any(p =>
                File.Exists(Path.Combine(p, toolName)) ||
                File.Exists(Path.Combine(p, toolName + ".exe")));
        }
        
        /// <summary>
        /// Creates a super-timeline using Plaso.
        /// </summary>
        public async Task<TimelineResult> CreateSuperTimelineAsync(
            string sourcePath,
            string outputPath,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new TimelineResult
            {
                StartTime = DateTime.Now
            };
            
            if (!IsPlasoAvailable)
            {
                result.Success = false;
                result.ErrorMessage = "Plaso (log2timeline) not found. Please install it.";
                return result;
            }
            
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var plasoFile = Path.Combine(outputPath, $"timeline_{timestamp}.plaso");
                var csvFile = Path.Combine(outputPath, $"timeline_{timestamp}.csv");
                
                progress?.Report("Creating Plaso database...");
                
                // Step 1: Create Plaso storage file
                var log2timeline = IsToolInPath("log2timeline.py") ? "log2timeline.py" : $"python \"{GetPlasoPath()}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {log2timeline} \"{plasoFile}\" \"{sourcePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start log2timeline process.";
                    return result;
                }
                
                await process.WaitForExitAsync(ct);
                
                if (process.ExitCode != 0)
                {
                    result.Success = false;
                    result.ErrorMessage = await process.StandardError.ReadToEndAsync(ct);
                    return result;
                }
                
                progress?.Report("Converting to CSV timeline...");
                
                // Step 2: Convert to CSV
                var psort = IsToolInPath("psort.py") ? "psort.py" : $"python \"{Path.Combine(_toolsPath, "psort.py")}\"";
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {psort} -o l2tcsv -w \"{csvFile}\" \"{plasoFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var psortProcess = Process.Start(psi);
                if (psortProcess != null)
                {
                    await psortProcess.WaitForExitAsync(ct);
                }
                
                result.Success = File.Exists(plasoFile);
                result.PlasoDbPath = plasoFile;
                result.CsvPath = File.Exists(csvFile) ? csvFile : null;
                
                if (result.Success)
                {
                    // Count events
                    if (File.Exists(csvFile))
                    {
                        result.EventCount = File.ReadLines(csvFile).Count() - 1; // Minus header
                    }
                    progress?.Report($"Super-timeline created: {result.EventCount} events.");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            result.EndTime = DateTime.Now;
            return result;
        }
        
        #endregion
        
        #region Tool Installation
        
        /// <summary>
        /// Gets status of all DFIR tools.
        /// </summary>
        public DFIRToolStatus GetToolStatus()
        {
            return new DFIRToolStatus
            {
                WinPmemAvailable = IsWinPmemAvailable,
                VolatilityAvailable = IsVolatilityAvailable,
                KapeAvailable = IsKapeAvailable,
                PlasoAvailable = IsPlasoAvailable
            };
        }
        
        /// <summary>
        /// Gets download URLs for DFIR tools.
        /// </summary>
        public static readonly Dictionary<string, string> ToolDownloadUrls = new()
        {
            ["WinPmem"] = "https://github.com/Velocidex/WinPmem/releases",
            ["Volatility3"] = "https://github.com/volatilityfoundation/volatility3/releases",
            ["KAPE"] = "https://www.kroll.com/en/insights/publications/cyber/kroll-artifact-parser-extractor-kape",
            ["Plaso"] = "https://github.com/log2timeline/plaso/releases"
        };
        
        #endregion
    }
    
    #region Result Models
    
    public class MemoryAcquisitionResult
    {
        public bool Success { get; set; }
        public string? OutputPath { get; set; }
        public long FileSize { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }
    
    public class VolatilityResult
    {
        public bool Success { get; set; }
        public string Plugin { get; set; } = string.Empty;
        public string? OutputPath { get; set; }
        public object? OutputData { get; set; }
        public string? RawOutput { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }
    
    public class KapeResult
    {
        public bool Success { get; set; }
        public List<string> Targets { get; set; } = new();
        public List<string> Modules { get; set; } = new();
        public string? OutputPath { get; set; }
        public int FilesCollected { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }
    
    public class ExportResult
    {
        public bool Success { get; set; }
        public string Destination { get; set; } = string.Empty;
        public int RecordsExported { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }
    
    public class TimelineResult
    {
        public bool Success { get; set; }
        public string? PlasoDbPath { get; set; }
        public string? CsvPath { get; set; }
        public int EventCount { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }
    
    public class DFIRToolStatus
    {
        public bool WinPmemAvailable { get; set; }
        public bool VolatilityAvailable { get; set; }
        public bool KapeAvailable { get; set; }
        public bool PlasoAvailable { get; set; }
        
        public bool AllToolsAvailable => WinPmemAvailable && VolatilityAvailable && KapeAvailable && PlasoAvailable;
        public int ToolsAvailableCount => (WinPmemAvailable ? 1 : 0) + (VolatilityAvailable ? 1 : 0) + (KapeAvailable ? 1 : 0) + (PlasoAvailable ? 1 : 0);
    }
    
    #endregion
}
