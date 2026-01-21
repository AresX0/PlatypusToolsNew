using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for executing Robocopy operations with full output parsing.
    /// </summary>
    public class RobocopyService
    {
        public event Action<string>? OutputReceived;
        public event Action<string>? ErrorReceived;
        public event Action<int>? ProgressChanged;

        /// <summary>
        /// Executes a Robocopy operation with the specified parameters.
        /// </summary>
        public async Task<RobocopyResult> ExecuteAsync(
            string sourceDirectory,
            string destinationDirectory,
            IEnumerable<RobocopySwitch> selectedSwitches,
            string? filePattern = null,
            bool createDestination = true,
            CancellationToken cancellationToken = default)
        {
            var result = new RobocopyResult
            {
                StartTime = DateTime.Now,
                SourceDirectory = sourceDirectory,
                DestinationDirectory = destinationDirectory
            };

            try
            {
                // Validate directories
                if (string.IsNullOrWhiteSpace(sourceDirectory))
                    throw new ArgumentException("Source directory is required.", nameof(sourceDirectory));

                if (!Directory.Exists(sourceDirectory))
                    throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");

                if (string.IsNullOrWhiteSpace(destinationDirectory))
                    throw new ArgumentException("Destination directory is required.", nameof(destinationDirectory));

                // Create destination if requested
                if (createDestination && !Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                // Build command line arguments
                var args = BuildArguments(sourceDirectory, destinationDirectory, selectedSwitches, filePattern);
                result.CommandLine = $"robocopy {args}";

                // Execute robocopy
                var startInfo = new ProcessStartInfo
                {
                    FileName = "robocopy",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = startInfo };
                var outputLines = new List<string>();
                var errorLines = new List<string>();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        OutputReceived?.Invoke(e.Data);
                        ParseOutputLine(e.Data, result);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        errorLines.Add(e.Data);
                        result.Errors.Add(e.Data);
                        ErrorReceived?.Invoke(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for process with cancellation support
                await Task.Run(() =>
                {
                    while (!process.WaitForExit(500))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(true); } catch { }
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    // Ensure all output is flushed
                    process.WaitForExit();
                }, cancellationToken);

                result.ExitCode = process.ExitCode;
                result.ExitCodeDescription = RobocopySwitches.GetExitCodeDescription(process.ExitCode);
                result.Success = RobocopySwitches.IsSuccessExitCode(process.ExitCode);
                result.Output = outputLines;

                // Parse final statistics from output
                ParseStatistics(outputLines, result);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Errors.Add("Operation was cancelled by user.");
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add(ex.Message);
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            return result;
        }

        /// <summary>
        /// Builds the command line arguments for Robocopy.
        /// </summary>
        private string BuildArguments(
            string source,
            string destination,
            IEnumerable<RobocopySwitch> switches,
            string? filePattern)
        {
            var sb = new StringBuilder();

            // Source and destination (quoted)
            sb.Append($"\"{source}\" \"{destination}\"");

            // File pattern if specified
            if (!string.IsNullOrWhiteSpace(filePattern))
            {
                sb.Append($" {filePattern}");
            }

            // Add selected switches
            foreach (var sw in switches.Where(s => s.IsSelected))
            {
                sb.Append(' ');

                if (sw.RequiresValue && !string.IsNullOrWhiteSpace(sw.Value))
                {
                    // Handle switches that have the value as part of the switch name
                    if (sw.Switch.Contains(':') || sw.Switch.Contains('['))
                    {
                        // Extract base switch name (e.g., "/R:n" -> "/R:")
                        var colonIndex = sw.Switch.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            var baseSwitch = sw.Switch.Substring(0, colonIndex + 1);
                            sb.Append($"{baseSwitch}{sw.Value}");
                        }
                        else
                        {
                            sb.Append($"{sw.Switch.Split('[')[0]}{sw.Value}");
                        }
                    }
                    else
                    {
                        sb.Append($"{sw.Switch} {sw.Value}");
                    }
                }
                else if (!sw.RequiresValue)
                {
                    sb.Append(sw.Switch);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parses an output line to extract file copy information.
        /// </summary>
        private void ParseOutputLine(string line, RobocopyResult result)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var trimmed = line.Trim();

            // Check for copied files (lines starting with file size or status)
            if (trimmed.StartsWith("New File") || trimmed.StartsWith("Newer") || 
                trimmed.StartsWith("Older") || trimmed.StartsWith("Changed"))
            {
                var parts = trimmed.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    result.FilesCopied.Add(new RobocopiedFile
                    {
                        Status = parts[0],
                        SourcePath = string.Join(" ", parts.Skip(2))
                    });
                }
            }

            // Check for errors (lines with ERROR or containing "Access is denied")
            if (trimmed.Contains("ERROR") || trimmed.Contains("Access is denied") ||
                trimmed.Contains("could not be") || trimmed.Contains("Cannot"))
            {
                var failedFile = new RobocopyFailedFile
                {
                    Path = ExtractPathFromError(trimmed),
                    Error = trimmed
                };
                
                if (!string.IsNullOrEmpty(failedFile.Path))
                {
                    result.FilesFailed.Add(failedFile);
                }
            }

            // Parse progress percentage
            var percentMatch = Regex.Match(trimmed, @"(\d+(\.\d+)?)\s*%");
            if (percentMatch.Success && double.TryParse(percentMatch.Groups[1].Value, out var percent))
            {
                ProgressChanged?.Invoke((int)percent);
            }
        }

        /// <summary>
        /// Extracts file path from an error message.
        /// </summary>
        private string ExtractPathFromError(string errorLine)
        {
            // Try to extract path from common error formats
            var pathMatch = Regex.Match(errorLine, @"([A-Za-z]:\\[^\t\r\n]+)");
            return pathMatch.Success ? pathMatch.Groups[1].Value : string.Empty;
        }

        /// <summary>
        /// Parses the statistics from the Robocopy output.
        /// </summary>
        private void ParseStatistics(List<string> output, RobocopyResult result)
        {
            var stats = result.Statistics;
            var inStatsSection = false;

            foreach (var line in output)
            {
                var trimmed = line.Trim();

                if (trimmed.Contains("----------"))
                {
                    inStatsSection = true;
                    continue;
                }

                if (!inStatsSection)
                    continue;

                // Parse directories line
                if (trimmed.StartsWith("Dirs :"))
                {
                    var numbers = Regex.Matches(trimmed, @"\d+");
                    if (numbers.Count >= 4)
                    {
                        stats.DirectoriesTotal = int.Parse(numbers[0].Value);
                        stats.DirectoriesCopied = int.Parse(numbers[1].Value);
                        stats.DirectoriesSkipped = int.Parse(numbers[2].Value);
                        // numbers[3] is Mismatch
                        if (numbers.Count >= 5)
                            stats.DirectoriesFailed = int.Parse(numbers[4].Value);
                    }
                }

                // Parse files line
                if (trimmed.StartsWith("Files :"))
                {
                    var numbers = Regex.Matches(trimmed, @"\d+");
                    if (numbers.Count >= 4)
                    {
                        stats.FilesTotal = int.Parse(numbers[0].Value);
                        stats.FilesCopied = int.Parse(numbers[1].Value);
                        stats.FilesSkipped = int.Parse(numbers[2].Value);
                        // numbers[3] is Mismatch
                        if (numbers.Count >= 5)
                            stats.FilesFailed = int.Parse(numbers[4].Value);
                    }
                }

                // Parse bytes line
                if (trimmed.StartsWith("Bytes :"))
                {
                    var numbers = Regex.Matches(trimmed, @"[\d,]+");
                    if (numbers.Count >= 3)
                    {
                        stats.BytesTotal = ParseBytes(numbers[0].Value);
                        stats.BytesCopied = ParseBytes(numbers[1].Value);
                        stats.BytesSkipped = ParseBytes(numbers[2].Value);
                    }
                }
            }
        }

        /// <summary>
        /// Parses a byte count string (may include commas).
        /// </summary>
        private long ParseBytes(string value)
        {
            var clean = value.Replace(",", "").Replace(" ", "");
            return long.TryParse(clean, out var result) ? result : 0;
        }

        /// <summary>
        /// Exports a Robocopy result to JSON file.
        /// </summary>
        public async Task ExportToJsonAsync(RobocopyResult result, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(result, options);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// Performs a dry run (list only) to preview what would be copied.
        /// </summary>
        public async Task<RobocopyResult> PreviewAsync(
            string sourceDirectory,
            string destinationDirectory,
            IEnumerable<RobocopySwitch> selectedSwitches,
            string? filePattern = null,
            CancellationToken cancellationToken = default)
        {
            // Add /L switch for list-only mode
            var switches = selectedSwitches.ToList();
            
            // Ensure /L is included for preview
            if (!switches.Any(s => s.Switch == "/L" && s.IsSelected))
            {
                switches.Add(new RobocopySwitch { Switch = "/L", IsSelected = true, Category = "Logging Options", Description = "List only" });
            }

            return await ExecuteAsync(sourceDirectory, destinationDirectory, switches, filePattern, false, cancellationToken);
        }
    }
}
