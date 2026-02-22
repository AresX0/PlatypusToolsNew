using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service to compute and verify checksums for multiple files using various hash algorithms.
    /// </summary>
    public class BulkChecksumService
    {
        public static readonly string[] SupportedAlgorithms = { "MD5", "SHA1", "SHA256", "SHA384", "SHA512" };

        /// <summary>
        /// Computes checksums for a list of files.
        /// </summary>
        public async Task<List<ChecksumResult>> ComputeChecksumsAsync(
            IEnumerable<string> filePaths,
            string algorithm = "SHA256",
            IProgress<ChecksumProgress>? progress = null,
            CancellationToken ct = default)
        {
            var files = filePaths.ToList();
            var results = new List<ChecksumResult>();
            int total = files.Count;
            int processed = 0;

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();

                    var result = new ChecksumResult
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        Algorithm = algorithm
                    };

                    try
                    {
                        var fi = new FileInfo(file);
                        result.FileSize = fi.Length;
                        result.Hash = ComputeHash(file, algorithm);
                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Error = ex.Message;
                        result.Success = false;
                    }

                    results.Add(result);
                    processed++;
                    progress?.Report(new ChecksumProgress
                    {
                        Current = processed,
                        Total = total,
                        CurrentFile = file,
                        Percent = total > 0 ? (processed * 100.0 / total) : 0
                    });
                }
            }, ct);

            return results;
        }

        /// <summary>
        /// Verifies files against expected hashes.
        /// </summary>
        public async Task<List<ChecksumVerification>> VerifyChecksumsAsync(
            IEnumerable<(string FilePath, string ExpectedHash)> entries,
            string algorithm = "SHA256",
            IProgress<ChecksumProgress>? progress = null,
            CancellationToken ct = default)
        {
            var items = entries.ToList();
            var results = new List<ChecksumVerification>();
            int total = items.Count;
            int processed = 0;

            await Task.Run(() =>
            {
                foreach (var (filePath, expectedHash) in items)
                {
                    ct.ThrowIfCancellationRequested();

                    var result = new ChecksumVerification
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        ExpectedHash = expectedHash.Trim(),
                        Algorithm = algorithm
                    };

                    try
                    {
                        if (!File.Exists(filePath))
                        {
                            result.Error = "File not found";
                            result.Match = false;
                        }
                        else
                        {
                            result.ActualHash = ComputeHash(filePath, algorithm);
                            result.Match = string.Equals(result.ActualHash, result.ExpectedHash,
                                StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Error = ex.Message;
                        result.Match = false;
                    }

                    results.Add(result);
                    processed++;
                    progress?.Report(new ChecksumProgress
                    {
                        Current = processed,
                        Total = total,
                        CurrentFile = filePath
                    });
                }
            }, ct);

            return results;
        }

        /// <summary>
        /// Parses a checksum file (e.g., sha256sum output: "hash  filename").
        /// </summary>
        public List<(string FilePath, string ExpectedHash)> ParseChecksumFile(string checksumFilePath)
        {
            var results = new List<(string, string)>();
            var baseDir = Path.GetDirectoryName(checksumFilePath) ?? "";

            foreach (var line in File.ReadAllLines(checksumFilePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                // Format: "hash  filename" or "hash *filename"
                var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var hash = parts[0];
                    var fileName = parts[1].TrimStart('*', ' ');
                    var fullPath = Path.IsPathRooted(fileName) ? fileName : Path.Combine(baseDir, fileName);
                    results.Add((fullPath, hash));
                }
            }

            return results;
        }

        /// <summary>
        /// Generates a checksum file in standard format.
        /// </summary>
        public async Task GenerateChecksumFileAsync(List<ChecksumResult> results, string outputPath)
        {
            var lines = results
                .Where(r => r.Success)
                .Select(r => $"{r.Hash}  {r.FileName}");
            await File.WriteAllLinesAsync(outputPath, lines);
        }

        /// <summary>
        /// Detects the hash algorithm from a hash string length.
        /// </summary>
        public static string DetectAlgorithm(string hash)
        {
            return hash.Length switch
            {
                32 => "MD5",
                40 => "SHA1",
                64 => "SHA256",
                96 => "SHA384",
                128 => "SHA512",
                _ => "Unknown"
            };
        }

        private static string ComputeHash(string filePath, string algorithm)
        {
            using var stream = File.OpenRead(filePath);
            using var hasher = algorithm.ToUpperInvariant() switch
            {
                "MD5" => (HashAlgorithm)MD5.Create(),
                "SHA1" => SHA1.Create(),
                "SHA256" => SHA256.Create(),
                "SHA384" => SHA384.Create(),
                "SHA512" => SHA512.Create(),
                _ => SHA256.Create()
            };
            var bytes = hasher.ComputeHash(stream);
            return Convert.ToHexString(bytes);
        }
    }

    public class ChecksumResult
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Hash { get; set; } = "";
        public string Algorithm { get; set; } = "";
        public long FileSize { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }

        public string FileSizeDisplay => FileSize switch
        {
            >= 1_073_741_824 => $"{FileSize / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{FileSize / 1_048_576.0:F1} MB",
            >= 1024 => $"{FileSize / 1024.0:F1} KB",
            _ => $"{FileSize} B"
        };
    }

    public class ChecksumVerification
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ExpectedHash { get; set; } = "";
        public string ActualHash { get; set; } = "";
        public string Algorithm { get; set; } = "";
        public bool Match { get; set; }
        public string? Error { get; set; }

        public string StatusIcon => Error != null ? "⚠️" : Match ? "✅" : "❌";
        public string StatusText => Error ?? (Match ? "Match" : "MISMATCH");
    }

    public class ChecksumProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string CurrentFile { get; set; } = "";
        public double Percent { get; set; }
    }
}
