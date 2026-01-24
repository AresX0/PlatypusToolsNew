using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Service for bulk_extractor feature extraction.
    /// Extracts email addresses, URLs, credit cards, phone numbers, and IP addresses from disk images.
    /// </summary>
    public class BulkExtractorService : ForensicOperationBase
    {
        public override string OperationName => "Bulk Extractor";

        #region Configuration

        /// <summary>
        /// Gets or sets the path to bulk_extractor executable.
        /// </summary>
        public string BulkExtractorPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the input file or folder to scan.
        /// </summary>
        public string InputPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output directory for results.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        // Scanner toggles
        public bool ExtractEmails { get; set; } = true;
        public bool ExtractUrls { get; set; } = true;
        public bool ExtractCreditCards { get; set; } = true;
        public bool ExtractPhoneNumbers { get; set; } = false;
        public bool ExtractIpAddresses { get; set; } = true;
        public bool ExtractDomains { get; set; } = false;
        public bool ExtractGps { get; set; } = false;

        #endregion

        #region Extraction

        /// <summary>
        /// Runs bulk_extractor on the input.
        /// </summary>
        public async Task<BulkExtractorResult> ExtractAsync(CancellationToken cancellationToken = default)
        {
            var result = new BulkExtractorResult();

            if (string.IsNullOrWhiteSpace(InputPath))
            {
                ReportError("Please select input folder or disk image");
                ReportBulkExtractorHelp();
                return result;
            }

            await ExecuteWithHandlingAsync(async () =>
            {
                var outputDir = Path.Combine(OutputPath, "bulk_extractor_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(outputDir);

                ReportProgress("========================================");
                ReportProgress("bulk_extractor Scan Started");
                ReportProgress("========================================");
                ReportProgress($"bulk_extractor Path: {(string.IsNullOrEmpty(BulkExtractorPath) ? "(using system PATH)" : BulkExtractorPath)}");
                ReportProgress($"Input: {InputPath}");
                ReportProgress($"Output: {outputDir}");
                ReportProgress("");

                var scanners = BuildScannerList();
                ReportProgress($"Enabled scanners: {string.Join(", ", scanners)}");
                ReportProgress("");

                var args = BuildArguments(outputDir, scanners);
                var bulkExe = FindBulkExtractor();

                ReportProgress($"[COMMAND] {bulkExe}");
                ReportProgress($"[ARGS] {args}");
                ReportProgress("");
                ReportProgress("--- bulk_extractor Output ---");

                var (success, output, error) = await RunBulkExtractorAsync(bulkExe, args, cancellationToken);

                ReportProgress("");
                ReportProgress("--- End bulk_extractor Output ---");
                ReportProgress("");

                if (success)
                {
                    // Parse results
                    await ParseResultsAsync(result, outputDir);

                    ReportProgress("========================================");
                    ReportProgress("Extraction Summary");
                    ReportProgress("========================================");

                    var emailCount = result.Findings.Count(r => r.Type == "email");
                    var urlCount = result.Findings.Count(r => r.Type == "url");
                    var ipCount = result.Findings.Count(r => r.Type == "ip");
                    var ccnCount = result.Findings.Count(r => r.Type == "credit_card");
                    var phoneCount = result.Findings.Count(r => r.Type == "phone");

                    ReportProgress($"Emails found: {emailCount}");
                    ReportProgress($"URLs found: {urlCount}");
                    ReportProgress($"IP addresses: {ipCount}");
                    ReportProgress($"Credit cards: {ccnCount}");
                    ReportProgress($"Phone numbers: {phoneCount}");
                    ReportProgress($"Total findings: {result.Findings.Count}");
                    ReportProgress($"Output folder: {outputDir}");

                    result.Success = true;
                    result.OutputPath = outputDir;
                    result.Message = $"Extraction complete: {result.Findings.Count} findings";
                }
                else
                {
                    ReportProgress("✗ bulk_extractor failed or not found.");
                    ReportProgress("");
                    ReportProgress($"Error: {error}");
                    ReportProgress("");
                    ReportProgress("Troubleshooting:");
                    ReportProgress("  • Click 'Download' to install bulk_extractor");
                    ReportProgress("  • Or set path to bulk_extractor.exe manually");

                    result.Success = false;
                    result.Message = "bulk_extractor failed - see log";
                    result.Error = error;
                }

            }, cancellationToken);

            return result;
        }

        private List<string> BuildScannerList()
        {
            var scanners = new List<string>();

            if (ExtractEmails) scanners.Add("email");
            if (ExtractUrls) scanners.Add("url");
            if (ExtractCreditCards) scanners.Add("accts");
            if (ExtractPhoneNumbers) scanners.Add("telephone");
            if (ExtractIpAddresses) scanners.Add("net");
            if (ExtractDomains) scanners.Add("domain");
            if (ExtractGps) scanners.Add("gps");

            return scanners;
        }

        private string BuildArguments(string outputDir, List<string> scanners)
        {
            var args = new StringBuilder();

            if (scanners.Count > 0)
            {
                args.Append($"-e {string.Join(" -e ", scanners)} ");
            }

            args.Append($"-o \"{outputDir}\" \"{InputPath}\"");

            return args.ToString();
        }

        private string FindBulkExtractor()
        {
            if (!string.IsNullOrEmpty(BulkExtractorPath) && File.Exists(BulkExtractorPath))
                return BulkExtractorPath;

            return "bulk_extractor"; // Try system PATH
        }

        private async Task<(bool success, string output, string error)> RunBulkExtractorAsync(
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

        private async Task ParseResultsAsync(BulkExtractorResult result, string outputDir)
        {
            var resultFiles = new[]
            {
                ("email.txt", "email"),
                ("url.txt", "url"),
                ("ip.txt", "ip"),
                ("ccn.txt", "credit_card"),
                ("telephone.txt", "phone"),
                ("domain.txt", "domain"),
                ("gps.txt", "gps")
            };

            foreach (var (filename, type) in resultFiles)
            {
                var filePath = Path.Combine(outputDir, filename);
                if (File.Exists(filePath))
                {
                    var lines = await File.ReadAllLinesAsync(filePath);
                    foreach (var line in lines.Take(1000)) // Limit for performance
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                        {
                            var parts = line.Split('\t');
                            result.Findings.Add(new BulkExtractorFinding
                            {
                                Type = type,
                                Value = parts.Length > 1 ? parts[1] : parts[0],
                                Source = parts.Length > 0 ? parts[0] : "unknown"
                            });
                        }
                    }
                }
            }

            ReportProgress($"  Parsed {result.Findings.Count} total findings");
        }

        private void ReportBulkExtractorHelp()
        {
            ReportProgress("========================================");
            ReportProgress("bulk_extractor - Feature Extraction Tool");
            ReportProgress("========================================");
            ReportProgress("⚠ No input selected.");
            ReportProgress("");
            ReportProgress("bulk_extractor extracts features from disk images:");
            ReportProgress("  • Email addresses");
            ReportProgress("  • URLs and domains");
            ReportProgress("  • Credit card numbers");
            ReportProgress("  • Phone numbers");
            ReportProgress("  • IP addresses");
            ReportProgress("");
            ReportProgress("💡 Click 'Download bulk_extractor' to get the tool.");
        }

        #endregion
    }

    #region Result Models

    public class BulkExtractorResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public List<BulkExtractorFinding> Findings { get; } = new();
    }

    public class BulkExtractorFinding
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    #endregion
}
