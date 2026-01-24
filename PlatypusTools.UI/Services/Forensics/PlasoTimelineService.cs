using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Service for Plaso (log2timeline) super-timeline creation.
    /// Creates forensic timelines from evidence sources for investigation.
    /// </summary>
    public class PlasoTimelineService : ForensicOperationBase
    {
        public override string OperationName => "Plaso Timeline";

        #region Configuration

        /// <summary>
        /// Gets or sets the path to Plaso installation.
        /// </summary>
        public string PlasoPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the evidence folder to process.
        /// </summary>
        public string EvidencePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Plaso storage file output path.
        /// </summary>
        public string StorageFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the OpenSearch URL for export.
        /// </summary>
        public string OpenSearchUrl { get; set; } = "http://localhost:9200";

        // Parser options
        public bool ParseWinEvtx { get; set; } = true;
        public bool ParsePrefetch { get; set; } = true;
        public bool ParseRegistry { get; set; } = true;
        public bool ParseLnk { get; set; } = true;
        public bool ParseMft { get; set; } = false;
        public bool ParseBrowser { get; set; } = true;

        #endregion

        #region Timeline Creation

        /// <summary>
        /// Creates a super-timeline using Plaso log2timeline.
        /// </summary>
        public async Task<PlasoResult> CreateTimelineAsync(CancellationToken cancellationToken = default)
        {
            var result = new PlasoResult();

            if (string.IsNullOrWhiteSpace(EvidencePath) || !Directory.Exists(EvidencePath))
            {
                ReportError("Please select evidence folder");
                ReportPlasaHelp();
                return result;
            }

            await ExecuteWithHandlingAsync(async () =>
            {
                var storageDir = Path.GetDirectoryName(StorageFilePath);
                if (!string.IsNullOrEmpty(storageDir))
                    Directory.CreateDirectory(storageDir);

                ReportProgress("========================================");
                ReportProgress("Plaso Timeline Creation Started");
                ReportProgress("========================================");
                ReportProgress($"Plaso Path: {(string.IsNullOrEmpty(PlasoPath) ? "(using system PATH)" : PlasoPath)}");
                ReportProgress($"Evidence: {EvidencePath}");
                ReportProgress($"Storage File: {StorageFilePath}");
                ReportProgress("");

                var log2timeline = FindLog2Timeline();
                var args = BuildArguments();

                ReportProgress($"[COMMAND] {log2timeline}");
                ReportProgress($"[ARGS] {args}");
                ReportProgress("");
                ReportProgress("--- Plaso Output ---");

                var (success, output, error) = await RunLog2TimelineAsync(log2timeline, args, cancellationToken);

                ReportProgress("");
                ReportProgress("--- End Plaso Output ---");
                ReportProgress("");

                if (success)
                {
                    ReportProgress("✓ Timeline created successfully!");
                    ReportProgress($"  Storage file: {StorageFilePath}");

                    var fileInfo = new FileInfo(StorageFilePath);
                    if (fileInfo.Exists)
                    {
                        ReportProgress($"  Size: {FormatBytes(fileInfo.Length)}");
                        result.StorageFileSize = fileInfo.Length;
                    }

                    result.Success = true;
                    result.StorageFilePath = StorageFilePath;
                    result.Message = "Plaso timeline created successfully";
                }
                else
                {
                    ReportProgress("✗ Plaso failed or not found.");
                    ReportProgress("");
                    ReportProgress($"Error: {error}");
                    ReportProgress("");
                    ReportProgress("Troubleshooting:");
                    ReportProgress("  • Ensure Plaso is installed");
                    ReportProgress("  • Click 'Download Plaso' to get installation page");
                    ReportProgress("  • Set Plaso Path to installation folder");

                    result.Success = false;
                    result.Message = "Plaso failed - see log for details";
                    result.Error = error;
                }

            }, cancellationToken);

            return result;
        }

        /// <summary>
        /// Exports Plaso timeline to OpenSearch.
        /// </summary>
        public async Task<bool> ExportToOpenSearchAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(StorageFilePath))
            {
                ReportError("Create a Plaso timeline first");
                return false;
            }

            ReportProgress("Exporting Plaso timeline to OpenSearch...");

            var psort = FindPsort();
            var timestamp = DateTime.Now.ToString("yyyy.MM");
            var indexName = $"dfir-plaso-{timestamp}";

            var args = $"-o opensearch --server {OpenSearchUrl} --index_name {indexName} \"{StorageFilePath}\"";

            var (success, _, error) = await RunProcessAsync(psort, args, cancellationToken);

            if (success)
            {
                ReportProgress($"✓ Exported to OpenSearch index: {indexName}");
                return true;
            }
            else
            {
                ReportProgress($"✗ Export failed: {error}");
                return false;
            }
        }

        private string FindLog2Timeline()
        {
            if (!string.IsNullOrEmpty(PlasoPath))
            {
                var log2timelinePy = Path.Combine(PlasoPath, "log2timeline.py");
                if (File.Exists(log2timelinePy)) return log2timelinePy;

                var log2timelineExe = Path.Combine(PlasoPath, "log2timeline.exe");
                if (File.Exists(log2timelineExe)) return log2timelineExe;
            }

            return "log2timeline"; // Try system PATH
        }

        private string FindPsort()
        {
            if (!string.IsNullOrEmpty(PlasoPath))
            {
                var psortPy = Path.Combine(PlasoPath, "psort.py");
                if (File.Exists(psortPy)) return psortPy;

                var psortExe = Path.Combine(PlasoPath, "psort.exe");
                if (File.Exists(psortExe)) return psortExe;
            }

            return "psort";
        }

        private string BuildArguments()
        {
            var args = new StringBuilder();

            args.Append($"--storage-file \"{StorageFilePath}\"");

            // Add parser filters if needed
            var parsers = new System.Collections.Generic.List<string>();
            if (ParseWinEvtx) parsers.Add("winevtx");
            if (ParsePrefetch) parsers.Add("prefetch");
            if (ParseRegistry) parsers.Add("winreg");
            if (ParseLnk) parsers.Add("lnk");
            if (ParseMft) parsers.Add("mft");
            if (ParseBrowser) parsers.Add("chrome,firefox,safari");

            if (parsers.Count > 0 && parsers.Count < 6)
            {
                args.Append($" --parsers {string.Join(",", parsers)}");
            }

            args.Append($" \"{EvidencePath}\"");

            return args.ToString();
        }

        private async Task<(bool success, string output, string error)> RunLog2TimelineAsync(
            string fileName, string arguments, CancellationToken cancellationToken)
        {
            return await RunProcessAsync(fileName, arguments, cancellationToken);
        }

        private async Task<(bool success, string output, string error)> RunProcessAsync(
            string fileName, string arguments, CancellationToken cancellationToken)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        ReportProgress(e.Data);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        ReportProgress($"[STDERR] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken);

                ReportProgress($"[EXIT CODE] {process.ExitCode}");

                return (process.ExitCode == 0, outputBuilder.ToString(), errorBuilder.ToString());
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        private void ReportPlasaHelp()
        {
            ReportProgress("========================================");
            ReportProgress("Plaso (log2timeline) - Super-Timeline Creator");
            ReportProgress("========================================");
            ReportProgress("⚠ Evidence folder not selected.");
            ReportProgress("");
            ReportProgress("Plaso creates forensic timelines from evidence sources.");
            ReportProgress("Download: https://github.com/log2timeline/plaso/releases");
            ReportProgress("");
            ReportProgress("Installation options:");
            ReportProgress("  • Windows: Download standalone ZIP from releases");
            ReportProgress("  • Python: pip install plaso");
            ReportProgress("  • Docker: docker pull log2timeline/plaso");
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {suffixes[order]}";
        }

        #endregion
    }

    #region Result Models

    public class PlasoResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string StorageFilePath { get; set; } = string.Empty;
        public long StorageFileSize { get; set; }
    }

    #endregion
}
