using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace PlatypusTools.Core.Services
{
    #region Models

    /// <summary>
    /// Options controlling website scanning and downloading behavior.
    /// </summary>
    public class DownloadOptions
    {
        public bool DownloadImages { get; set; } = true;
        public bool DownloadVideos { get; set; } = true;
        public bool DownloadDocuments { get; set; } = true;
        public bool DownloadArchives { get; set; } = true;
        public bool RecursiveCrawl { get; set; } = false;
        public bool FollowPagination { get; set; } = true;
        public int MaxDepth { get; set; } = 1;
        public int MaxLinksPerPage { get; set; } = 50;
        public int MaxPaginationPages { get; set; } = 10000;
        public int MaxConcurrentDownloads { get; set; } = 3;
        public int RequestDelayMs { get; set; } = 500;
        public int MaxRetries { get; set; } = 3;
        public string? UrlPattern { get; set; }

        /// <summary>When true, computes SHA-256 hashes and skips already-downloaded files.</summary>
        public bool SkipDuplicates { get; set; } = true;

        /// <summary>HTTP/HTTPS proxy URL (e.g., http://user:pass@host:port).</summary>
        public string? ProxyUrl { get; set; }

        /// <summary>Download speed limit in KB/s (0 = unlimited).</summary>
        public int SpeedLimitKbps { get; set; }

        /// <summary>When true, mirrors the URL path structure in the output directory.</summary>
        public bool MirrorDirectoryStructure { get; set; } = true;
    }

    /// <summary>
    /// Represents a single file discovered during website scanning.
    /// </summary>
    public class DownloadItem
    {
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public long Size { get; set; }
        public int Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SourcePage { get; set; }
        public int PageNumber { get; set; }
    }

    /// <summary>
    /// Reports download progress for a single file.
    /// </summary>
    public class DownloadProgress
    {
        public string Url { get; set; } = string.Empty;
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public int Percentage { get; set; }

        /// <summary>Current download speed in KB/s.</summary>
        public double SpeedKbps { get; set; }

        /// <summary>Estimated time remaining.</summary>
        public TimeSpan EstimatedTimeRemaining { get; set; }

        /// <summary>Human-readable speed string.</summary>
        public string SpeedText { get; set; } = string.Empty;

        /// <summary>Human-readable ETA string.</summary>
        public string EtaText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of a single file download attempt.
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long BytesDownloaded { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>SHA-256 hash of the downloaded file (lowercase hex).</summary>
        public string? Hash { get; set; }

        /// <summary>True if the file was skipped because a duplicate hash already existed.</summary>
        public bool SkippedDuplicate { get; set; }
    }

    /// <summary>
    /// Scan progress report emitted during website scanning.
    /// </summary>
    public class ScanProgress
    {
        public string CurrentUrl { get; set; } = string.Empty;
        public int PagesScanned { get; set; }
        public int ItemsFound { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// A single entry in the download history log.
    /// </summary>
    public class DownloadHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long BytesDownloaded { get; set; }
        public string? Hash { get; set; }
        public string? ErrorMessage { get; set; }
        public double SpeedKbps { get; set; }
        public TimeSpan Duration { get; set; }

        public override string ToString() =>
            $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Status,-12} {FileName} ({FormatSize(BytesDownloaded)}) [{SpeedKbps:F0} KB/s]" +
            (ErrorMessage != null ? $" — {ErrorMessage}" : "");

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }
    }

    /// <summary>
    /// A saved site profile for quick re-use.
    /// </summary>
    public class SiteProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DownloadOptions Options { get; set; } = new();
        public List<string> AdditionalUrls { get; set; } = [];

        /// <summary>
        /// Indicates this is a user-created custom profile (editable/deletable).
        /// Not serialized for built-in profiles.
        /// </summary>
        public bool IsCustom { get; set; }

        /// <summary>
        /// Built-in profiles for well-known data dump sites.
        /// </summary>
        public static List<SiteProfile> GetBuiltInProfiles() =>
        [
            new SiteProfile
            {
                Name = "DOJ Epstein Disclosures",
                Description = "All 12 data sets from the DOJ Epstein disclosures page with full pagination support",
                Url = "https://www.justice.gov/epstein/doj-disclosures",
                Options = new DownloadOptions
                {
                    DownloadDocuments = true,
                    DownloadImages = false,
                    DownloadVideos = false,
                    FollowPagination = true,
                    MaxPaginationPages = 10000,
                    RecursiveCrawl = true,
                    MaxDepth = 2,
                    RequestDelayMs = 1000,
                    MaxConcurrentDownloads = 2,
                },
                AdditionalUrls =
                [
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-1-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-2-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-3-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-4-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-5-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-6-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-7-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-8-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-9-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-10-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-11-files",
                    "https://www.justice.gov/epstein/doj-disclosures/data-set-12-files",
                ]
            },
            new SiteProfile
            {
                Name = "DOJ FOIA Reading Room",
                Description = "DOJ Freedom of Information Act Electronic Reading Room",
                Url = "https://www.justice.gov/oip/available-documents-foia-library",
                Options = new DownloadOptions
                {
                    DownloadDocuments = true,
                    FollowPagination = true,
                    MaxPaginationPages = 500,
                    RecursiveCrawl = true,
                    MaxDepth = 2,
                    RequestDelayMs = 1000,
                }
            },
            new SiteProfile
            {
                Name = "House Oversight (Custom URL)",
                Description = "Congressional documents from House Oversight committee",
                Url = "https://oversight.house.gov/release/",
                Options = new DownloadOptions
                {
                    DownloadDocuments = true,
                    FollowPagination = true,
                    RecursiveCrawl = false,
                    MaxPaginationPages = 100,
                }
            },
            new SiteProfile
            {
                Name = "Generic Website (enter URL)",
                Description = "Scan any website for downloadable files",
                Url = "",
                Options = new DownloadOptions
                {
                    DownloadDocuments = true,
                    DownloadImages = true,
                    DownloadVideos = true,
                    FollowPagination = true,
                    MaxPaginationPages = 100,
                    RecursiveCrawl = false,
                    MaxDepth = 1,
                }
            }
        ];
    }

    #endregion

    #region SpeedTracker

    /// <summary>
    /// Tracks download speed and estimates time remaining (ETA).
    /// Thread-safe; updated per download chunk.
    /// </summary>
    public class SpeedTracker
    {
        private readonly Stopwatch _stopwatch = new();
        private long _bytesDownloaded;
        private long _totalBytes;
        private readonly Queue<(DateTime time, long bytes)> _samples = new();
        private readonly object _lock = new();
        private const int MaxSamples = 20;
        private const double SampleWindowSeconds = 5.0;

        public double SpeedBytesPerSecond { get; private set; }
        public double SpeedKbps => SpeedBytesPerSecond / 1024.0;
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public TimeSpan EstimatedTimeRemaining
        {
            get
            {
                if (SpeedBytesPerSecond <= 0 || _totalBytes <= 0)
                    return TimeSpan.Zero;

                var remaining = _totalBytes - _bytesDownloaded;
                if (remaining <= 0) return TimeSpan.Zero;

                return TimeSpan.FromSeconds(remaining / SpeedBytesPerSecond);
            }
        }

        public string SpeedText
        {
            get
            {
                if (SpeedBytesPerSecond <= 0) return "—";
                if (SpeedKbps < 1024) return $"{SpeedKbps:F0} KB/s";
                return $"{SpeedKbps / 1024:F1} MB/s";
            }
        }

        public string EtaText
        {
            get
            {
                var eta = EstimatedTimeRemaining;
                if (eta <= TimeSpan.Zero) return "—";
                if (eta.TotalHours >= 1) return $"{eta.Hours}h {eta.Minutes}m";
                if (eta.TotalMinutes >= 1) return $"{eta.Minutes}m {eta.Seconds}s";
                return $"{eta.Seconds}s";
            }
        }

        public void Start(long totalBytes = 0)
        {
            _totalBytes = totalBytes;
            _bytesDownloaded = 0;
            SpeedBytesPerSecond = 0;

            lock (_lock) { _samples.Clear(); }
            _stopwatch.Restart();
        }

        public void Update(long bytesDownloaded, long totalBytes = 0)
        {
            _bytesDownloaded = bytesDownloaded;
            if (totalBytes > 0) _totalBytes = totalBytes;

            var now = DateTime.UtcNow;

            lock (_lock)
            {
                _samples.Enqueue((now, bytesDownloaded));

                while (_samples.Count > MaxSamples ||
                       (_samples.Count > 1 && (now - _samples.Peek().time).TotalSeconds > SampleWindowSeconds))
                {
                    _samples.Dequeue();
                }

                if (_samples.Count >= 2)
                {
                    var oldest = _samples.Peek();
                    var elapsed = (now - oldest.time).TotalSeconds;
                    if (elapsed > 0)
                    {
                        SpeedBytesPerSecond = (bytesDownloaded - oldest.bytes) / elapsed;
                    }
                }
            }
        }

        public void Stop() => _stopwatch.Stop();

        public void Reset()
        {
            _stopwatch.Reset();
            _bytesDownloaded = 0;
            _totalBytes = 0;
            SpeedBytesPerSecond = 0;
            lock (_lock) { _samples.Clear(); }
        }
    }

    #endregion

    #region HashDatabase

    /// <summary>
    /// SHA-256 hash database for duplicate file detection.
    /// Maintains a persistent hash file (existing_hashes.txt) and an in-memory cache.
    /// </summary>
    public sealed class WebDownloadHashDatabase : IDisposable
    {
        private const string HashFileName = "existing_hashes.txt";

        private readonly ConcurrentDictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _fileLock = new();
        private string _outputDirectory = string.Empty;
        private bool _disposed;

        /// <summary>Number of hashes currently tracked.</summary>
        public int Count => _hashes.Count;

        /// <summary>
        /// Scans an output directory for existing files, computing SHA-256 hashes.
        /// </summary>
        public async Task ScanExistingFilesAsync(
            string outputDirectory,
            IProgress<(int scanned, int total, string currentFile)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _outputDirectory = outputDirectory;

            // Load any existing hash file entries
            LoadHashFile(outputDirectory);

            var files = Directory.Exists(outputDirectory)
                ? Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).StartsWith('.') && Path.GetFileName(f) != HashFileName)
                    .ToArray()
                : [];

            if (files.Length == 0) return;

            var maxParallelism = Math.Max(1, Environment.ProcessorCount / 2);
            var scanned = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = maxParallelism,
                        CancellationToken = cancellationToken
                    },
                    file =>
                    {
                        var relativePath = Path.GetRelativePath(outputDirectory, file);
                        if (_hashes.Values.Any(v => v.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            Interlocked.Increment(ref scanned);
                            return;
                        }

                        var hash = ComputeFileHash(file);
                        if (hash != null)
                        {
                            _hashes.TryAdd(hash, relativePath);
                        }

                        var current = Interlocked.Increment(ref scanned);
                        progress?.Report((current, files.Length, Path.GetFileName(file)));
                    });
            }, cancellationToken);

            SaveHashFile(outputDirectory);
        }

        /// <summary>Checks whether a SHA-256 hash already exists in the database.</summary>
        public bool HashExists(string hash) => _hashes.ContainsKey(hash);

        /// <summary>Registers a newly downloaded file's hash.</summary>
        public void RegisterFile(string hash, string relativePath)
        {
            _hashes.TryAdd(hash, relativePath);

            if (!string.IsNullOrEmpty(_outputDirectory))
            {
                AppendHashEntry(_outputDirectory, hash, relativePath);
            }
        }

        /// <summary>Clears the in-memory cache.</summary>
        public void ClearCache() => _hashes.Clear();

        /// <summary>Computes the SHA-256 hash of a file on disk.</summary>
        public static string? ComputeFileHash(string filePath)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                var hashBytes = sha.ComputeHash(stream);
                return Convert.ToHexStringLower(hashBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Hash] Error hashing {filePath}: {ex.Message}");
                return null;
            }
        }

        private void LoadHashFile(string outputDirectory)
        {
            var hashFilePath = Path.Combine(outputDirectory, HashFileName);
            if (!File.Exists(hashFilePath)) return;

            try
            {
                foreach (var line in File.ReadLines(hashFilePath))
                {
                    var parts = line.Split('\t', 2);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        _hashes.TryAdd(parts[0].Trim(), parts[1].Trim());
                    }
                }
                Debug.WriteLine($"[Hash] Loaded {_hashes.Count} hashes from {hashFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Hash] Error loading hash file: {ex.Message}");
            }
        }

        private void SaveHashFile(string outputDirectory)
        {
            var hashFilePath = Path.Combine(outputDirectory, HashFileName);

            try
            {
                lock (_fileLock)
                {
                    using var writer = new StreamWriter(hashFilePath, append: false);
                    foreach (var kvp in _hashes)
                    {
                        writer.WriteLine($"{kvp.Key}\t{kvp.Value}");
                    }
                }
                Debug.WriteLine($"[Hash] Saved {_hashes.Count} hashes to {hashFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Hash] Error saving hash file: {ex.Message}");
            }
        }

        private void AppendHashEntry(string outputDirectory, string hash, string relativePath)
        {
            var hashFilePath = Path.Combine(outputDirectory, HashFileName);

            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(hashFilePath, $"{hash}\t{relativePath}\n");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Hash] Error appending hash: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // Save any pending hashes
                if (!string.IsNullOrEmpty(_outputDirectory))
                    SaveHashFile(_outputDirectory);
            }
        }
    }

    #endregion

    #region WebsiteDownloaderService

    /// <summary>
    /// Scans websites for downloadable files and downloads them with:
    /// - Full pagination following across all pages
    /// - Recursive crawl with depth control
    /// - File type filtering (images, videos, documents, archives)
    /// - SHA-256 hash-based duplicate detection
    /// - Speed tracking with ETA
    /// - Pause/Resume support
    /// - Bandwidth throttling
    /// - Proxy support
    /// - Directory mirroring
    /// - Retry with exponential backoff
    /// </summary>
    public class WebsiteDownloaderService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _httpHandler;
        private readonly WebDownloadHashDatabase? _hashDatabase;
        private readonly ManualResetEventSlim _pauseEvent = new(true);
        private bool _disposed;

        /// <summary>Speed limit in KB/s (0 = unlimited).</summary>
        public int SpeedLimitKbps { get; set; }

        /// <summary>Mirror URL directory structure in output folder.</summary>
        public bool MirrorDirectoryStructure { get; set; } = true;

        /// <summary>True when downloads are paused.</summary>
        public bool IsPaused => !_pauseEvent.IsSet;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff", ".tif"
        };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv",
            ".rtf", ".odt", ".ods", ".odp", ".epub"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tar.gz", ".tgz"
        };

        public WebsiteDownloaderService(WebDownloadHashDatabase? hashDatabase = null, string? proxyUrl = null)
        {
            _hashDatabase = hashDatabase;

            _httpHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                CookieContainer = new CookieContainer()
            };

            // Proxy support
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                _httpHandler.Proxy = new WebProxy(proxyUrl);
                _httpHandler.UseProxy = true;
                Debug.WriteLine($"[WebDL] Using proxy: {proxyUrl}");
            }
            
            _httpClient = new HttpClient(_httpHandler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            
            // Browser-like headers to avoid WAF blocking
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
        }

        public void Pause() => _pauseEvent.Reset();
        public void Resume() => _pauseEvent.Set();

        #region Scanning

        /// <summary>
        /// Scans a URL (and its paginated pages) for downloadable files.
        /// </summary>
        public async Task<List<DownloadItem>> ScanUrlAsync(
            string url,
            DownloadOptions options,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var items = new List<DownloadItem>();
            var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await ScanWithHttpAsync(url, url, items, visitedUrls, options, 0, progress, cancellationToken);

            // Deduplicate by URL
            return items.DistinctBy(i => i.Url).ToList();
        }

        /// <summary>
        /// Scans multiple URLs (e.g., all data set pages for a site profile).
        /// </summary>
        public async Task<List<DownloadItem>> ScanMultipleAsync(
            IEnumerable<string> urls,
            DownloadOptions options,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var allItems = new List<DownloadItem>();

            foreach (var url in urls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new ScanProgress
                {
                    CurrentUrl = url,
                    ItemsFound = allItems.Count,
                    Message = $"Scanning {url}..."
                });

                var items = await ScanUrlAsync(url, options, progress, cancellationToken);
                allItems.AddRange(items);

                if (options.RequestDelayMs > 0)
                    await Task.Delay(options.RequestDelayMs, cancellationToken);
            }

            return allItems.DistinctBy(i => i.Url).ToList();
        }

        private async Task ScanWithHttpAsync(
            string baseUrl, string currentUrl, List<DownloadItem> items,
            HashSet<string> visitedUrls, DownloadOptions options, int depth,
            IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        {
            if (visitedUrls.Contains(currentUrl))
                return;

            visitedUrls.Add(currentUrl);

            var pageNumber = GetPageNumber(currentUrl);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                if (depth > 0)
                {
                    request.Headers.Add("Referer", baseUrl);
                }

                var response = await SendWithRetryAsync(request, options.MaxRetries, cancellationToken);
                if (response == null || !response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[Scan] HTTP {response?.StatusCode} for {currentUrl}");
                    return;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Extract downloadable file links
                ExtractDownloadItems(doc, baseUrl, items, options, pageNumber);

                progress?.Report(new ScanProgress
                {
                    CurrentUrl = currentUrl,
                    PagesScanned = visitedUrls.Count,
                    ItemsFound = items.Count,
                    Message = $"Page {pageNumber}: found {items.Count} items total"
                });

                // Follow pagination
                if (options.FollowPagination && pageNumber < options.MaxPaginationPages)
                {
                    var nextPageUrl = FindNextPageUrl(doc, currentUrl);
                    if (nextPageUrl != null && !visitedUrls.Contains(nextPageUrl))
                    {
                        Debug.WriteLine($"[Pagination] Following: {nextPageUrl}");
                        if (options.RequestDelayMs > 0)
                            await Task.Delay(options.RequestDelayMs, cancellationToken);

                        await ScanWithHttpAsync(baseUrl, nextPageUrl, items, visitedUrls, options, depth + 1, progress, cancellationToken);
                    }
                }

                // Recursive crawl of sub-links (not pagination, but navigation)
                if (options.RecursiveCrawl && depth < options.MaxDepth)
                {
                    var subLinks = ExtractNavigationLinks(doc, baseUrl, currentUrl, visitedUrls);
                    foreach (var link in subLinks.Take(options.MaxLinksPerPage))
                    {
                        if (options.RequestDelayMs > 0)
                            await Task.Delay(options.RequestDelayMs, cancellationToken);

                        await ScanWithHttpAsync(baseUrl, link, items, visitedUrls, options, depth + 1, progress, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Scan] Error scanning {currentUrl}: {ex.Message}");
            }
        }

        #endregion

        #region Downloading

        /// <summary>
        /// Downloads a single file with progress reporting, speed tracking,
        /// bandwidth throttling, and optional hash-based dedup.
        /// </summary>
        public async Task<DownloadResult> DownloadFileAsync(
            DownloadItem item,
            string outputDirectory,
            bool skipDuplicates = true,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var fileName = SanitizeFileName(item.FileName);

                // Build output path — optionally mirror URL directory structure
                string subDir = string.Empty;
                if (MirrorDirectoryStructure && !string.IsNullOrEmpty(item.SourcePage))
                {
                    subDir = GetMirrorSubDirectory(item.SourcePage);
                }

                var targetDir = string.IsNullOrEmpty(subDir)
                    ? outputDirectory
                    : Path.Combine(outputDirectory, subDir);

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                var relativePath = string.IsNullOrEmpty(subDir)
                    ? fileName
                    : Path.Combine(subDir, fileName);
                var filePath = Path.Combine(targetDir, fileName);

                // Pre-download dedup check
                if (File.Exists(filePath) && skipDuplicates && _hashDatabase != null)
                {
                    var existingHash = WebDownloadHashDatabase.ComputeFileHash(filePath);
                    if (existingHash != null && _hashDatabase.HashExists(existingHash))
                    {
                        return new DownloadResult
                        {
                            Success = true, FilePath = filePath,
                            Hash = existingHash, SkippedDuplicate = true
                        };
                    }
                }

                // Handle duplicate file names
                if (File.Exists(filePath))
                {
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var counter = 1;
                    while (File.Exists(filePath))
                    {
                        fileName = $"{name}_{counter}{ext}";
                        filePath = Path.Combine(targetDir, fileName);
                        counter++;
                    }
                    relativePath = string.IsNullOrEmpty(subDir) ? fileName : Path.Combine(subDir, fileName);
                }

                using var response = await _httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;
                var speedTracker = new SpeedTracker();
                speedTracker.Start(totalBytes);

                var tempPath = filePath + ".tmp";
                var lastReport = DateTime.UtcNow;

                await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        // Pause support
                        _pauseEvent.Wait(cancellationToken);

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        downloadedBytes += bytesRead;

                        speedTracker.Update(downloadedBytes, totalBytes);

                        // Bandwidth throttling
                        if (SpeedLimitKbps > 0)
                        {
                            var targetBytesPerSec = SpeedLimitKbps * 1024.0;
                            var elapsed = speedTracker.Elapsed.TotalSeconds;
                            if (elapsed > 0)
                            {
                                var actualRate = downloadedBytes / elapsed;
                                if (actualRate > targetBytesPerSec)
                                {
                                    var sleepMs = (int)((downloadedBytes / targetBytesPerSec - elapsed) * 1000);
                                    if (sleepMs > 0)
                                        await Task.Delay(Math.Min(sleepMs, 1000), cancellationToken);
                                }
                            }
                        }

                        // Report progress at most every 200ms
                        var now = DateTime.UtcNow;
                        if ((now - lastReport).TotalMilliseconds >= 200 || downloadedBytes >= totalBytes)
                        {
                            lastReport = now;
                            progress?.Report(new DownloadProgress
                            {
                                Url = item.Url,
                                BytesDownloaded = downloadedBytes,
                                TotalBytes = totalBytes,
                                Percentage = totalBytes > 0 ? (int)((downloadedBytes * 100) / totalBytes) : 0,
                                SpeedKbps = speedTracker.SpeedKbps,
                                EstimatedTimeRemaining = speedTracker.EstimatedTimeRemaining,
                                SpeedText = speedTracker.SpeedText,
                                EtaText = speedTracker.EtaText
                            });
                        }
                    }
                }

                speedTracker.Stop();

                // Compute hash
                var fileHash = WebDownloadHashDatabase.ComputeFileHash(tempPath);

                // Post-download dedup check
                if (skipDuplicates && _hashDatabase != null && fileHash != null && _hashDatabase.HashExists(fileHash))
                {
                    try { File.Delete(tempPath); } catch { /* best effort */ }
                    return new DownloadResult
                    {
                        Success = true, FilePath = null,
                        BytesDownloaded = downloadedBytes, Hash = fileHash,
                        SkippedDuplicate = true
                    };
                }

                File.Move(tempPath, filePath, overwrite: true);

                if (_hashDatabase != null && fileHash != null)
                    _hashDatabase.RegisterFile(fileHash, relativePath);

                return new DownloadResult
                {
                    Success = true, FilePath = filePath,
                    BytesDownloaded = downloadedBytes, Hash = fileHash
                };
            }
            catch (Exception ex)
            {
                return new DownloadResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// Downloads a file with retry logic and exponential backoff.
        /// </summary>
        public async Task<DownloadResult> DownloadWithRetryAsync(
            DownloadItem item,
            string outputDirectory,
            int maxRetries = 3,
            bool skipDuplicates = true,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadResult? lastResult = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                lastResult = await DownloadFileAsync(item, outputDirectory, skipDuplicates, progress, cancellationToken);
                if (lastResult.Success) return lastResult;

                if (attempt < maxRetries)
                {
                    var delay = (int)Math.Pow(2, attempt) * 1000;
                    Debug.WriteLine($"[Download Retry] {item.FileName} attempt {attempt + 1}/{maxRetries + 1}, waiting {delay}ms");
                    await Task.Delay(delay, cancellationToken);
                }
            }

            return lastResult ?? new DownloadResult { Success = false, ErrorMessage = "Max retries exceeded" };
        }

        /// <summary>
        /// Tests if a download URL is reachable by sending a HEAD request.
        /// </summary>
        public async Task<(bool reachable, string message)> TestDownloadLinkAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var size = response.Content.Headers.ContentLength;
                var sizeStr = size.HasValue ? $" ({FormatSize(size.Value)})" : "";
                return (response.IsSuccessStatusCode,
                    $"HTTP {(int)response.StatusCode} {response.StatusCode}{sizeStr}");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates a JSON file tree of downloaded files.
        /// </summary>
        public static void ExportFileTree(string outputDirectory, string outputPath)
        {
            var tree = new Dictionary<string, List<string>>();

            if (Directory.Exists(outputDirectory))
            {
                foreach (var file in Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories))
                {
                    var dir = Path.GetDirectoryName(Path.GetRelativePath(outputDirectory, file)) ?? ".";
                    if (!tree.ContainsKey(dir))
                        tree[dir] = [];
                    tree[dir].Add(Path.GetFileName(file));
                }
            }

            var json = System.Text.Json.JsonSerializer.Serialize(tree,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outputPath, json);
        }

        #endregion

        #region Extraction

        private void ExtractDownloadItems(HtmlDocument doc, string baseUrl, List<DownloadItem> items, DownloadOptions options, int pageNumber)
        {
            var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
            if (allLinks == null) return;

            foreach (var link in allLinks)
            {
                var href = link.GetAttributeValue("href", "");
                var fullUrl = MakeAbsoluteUrl(baseUrl, href);
                if (string.IsNullOrEmpty(fullUrl)) continue;

                // Check URL pattern filter
                if (!IsMatchingPattern(fullUrl, options.UrlPattern)) continue;

                var ext = GetCleanExtension(fullUrl);
                string? type = null;

                if (options.DownloadDocuments && DocumentExtensions.Contains(ext))
                    type = "Document";
                else if (options.DownloadImages && ImageExtensions.Contains(ext))
                    type = "Image";
                else if (options.DownloadVideos && VideoExtensions.Contains(ext))
                    type = "Video";
                else if (options.DownloadArchives && ArchiveExtensions.Contains(ext))
                    type = "Archive";

                if (type == null) continue;

                var cleanUrl = fullUrl.Split('?')[0];
                var fileName = Path.GetFileName(Uri.UnescapeDataString(new Uri(cleanUrl).AbsolutePath));

                if (string.IsNullOrWhiteSpace(fileName)) continue;

                items.Add(new DownloadItem
                {
                    Url = fullUrl,
                    FileName = fileName,
                    Type = type,
                    Status = "Pending",
                    SourcePage = baseUrl,
                    PageNumber = pageNumber
                });
            }

            // Also check images directly (img src)
            if (options.DownloadImages)
            {
                var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = img.GetAttributeValue("src", "");
                        var fullUrl = MakeAbsoluteUrl(baseUrl, src);
                        if (string.IsNullOrEmpty(fullUrl)) continue;
                        if (!IsMatchingPattern(fullUrl, options.UrlPattern)) continue;

                        var ext = GetCleanExtension(fullUrl);
                        if (!ImageExtensions.Contains(ext)) continue;

                        var fileName = Path.GetFileName(Uri.UnescapeDataString(new Uri(fullUrl.Split('?')[0]).AbsolutePath));
                        if (string.IsNullOrWhiteSpace(fileName)) continue;

                        items.Add(new DownloadItem
                        {
                            Url = fullUrl,
                            FileName = fileName,
                            Type = "Image",
                            Status = "Pending",
                            SourcePage = baseUrl,
                            PageNumber = pageNumber
                        });
                    }
                }
            }

            // Check video elements
            if (options.DownloadVideos)
            {
                var videoNodes = doc.DocumentNode.SelectNodes("//video[@src]|//video//source[@src]");
                if (videoNodes != null)
                {
                    foreach (var video in videoNodes)
                    {
                        var src = video.GetAttributeValue("src", "");
                        var fullUrl = MakeAbsoluteUrl(baseUrl, src);
                        if (string.IsNullOrEmpty(fullUrl)) continue;
                        if (!IsMatchingPattern(fullUrl, options.UrlPattern)) continue;

                        var ext = GetCleanExtension(fullUrl);
                        if (!VideoExtensions.Contains(ext)) continue;

                        var fileName = Path.GetFileName(Uri.UnescapeDataString(new Uri(fullUrl.Split('?')[0]).AbsolutePath));
                        if (string.IsNullOrWhiteSpace(fileName)) continue;

                        items.Add(new DownloadItem
                        {
                            Url = fullUrl,
                            FileName = fileName,
                            Type = "Video",
                            Status = "Pending",
                            SourcePage = baseUrl,
                            PageNumber = pageNumber
                        });
                    }
                }
            }
        }

        private static List<string> ExtractNavigationLinks(HtmlDocument doc, string baseUrl, string currentUrl, HashSet<string> visitedUrls)
        {
            var links = new List<string>();
            var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
            if (allLinks == null) return links;

            var baseUri = new Uri(baseUrl);

            foreach (var link in allLinks)
            {
                var href = link.GetAttributeValue("href", "");
                var fullUrl = MakeAbsoluteUrl(baseUrl, href);

                if (string.IsNullOrEmpty(fullUrl)) continue;
                if (visitedUrls.Contains(fullUrl)) continue;
                if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out var targetUri)) continue;
                if (!targetUri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

                // Only follow links that share the base path
                if (targetUri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.OrdinalIgnoreCase))
                {
                    var ext = GetCleanExtension(fullUrl);
                    if (string.IsNullOrEmpty(ext) || ext == ".html" || ext == ".htm" || ext == ".php" || ext == ".asp" || ext == ".aspx")
                    {
                        links.Add(fullUrl);
                    }
                }
            }

            return links.Distinct().ToList();
        }

        #endregion

        #region Pagination

        /// <summary>
        /// Finds the "next page" URL from HTML pagination controls.
        /// </summary>
        private static string? FindNextPageUrl(HtmlDocument doc, string currentUrl)
        {
            var currentUri = new Uri(currentUrl);
            var currentPageBase = currentUrl.Split('?')[0];

            var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
            if (allLinks == null) return null;

            foreach (var link in allLinks)
            {
                var href = link.GetAttributeValue("href", "");
                var ariaLabel = link.GetAttributeValue("aria-label", "");
                var linkClass = link.GetAttributeValue("class", "");
                var rel = link.GetAttributeValue("rel", "");
                var text = link.InnerText?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(href))
                    continue;

                // Skip "Last page" links — we want sequential traversal
                if (ariaLabel.Equals("Last page", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip links that look like data set navigation, not pagination
                if (text.Contains("Data Set", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Dataset", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isNextPage =
                    // Aria labels
                    ariaLabel.Equals("Next page", StringComparison.OrdinalIgnoreCase) ||
                    ariaLabel.Equals("Go to next page", StringComparison.OrdinalIgnoreCase) ||
                    // rel="next"
                    rel.Equals("next", StringComparison.OrdinalIgnoreCase) ||
                    // CSS classes (Drupal, Bootstrap, generic)
                    linkClass.Contains("next-page", StringComparison.OrdinalIgnoreCase) ||
                    linkClass.Contains("next_page", StringComparison.OrdinalIgnoreCase) ||
                    linkClass.Contains("pager-next", StringComparison.OrdinalIgnoreCase) ||
                    linkClass.Contains("pager__next", StringComparison.OrdinalIgnoreCase) ||
                    linkClass.Contains("pagination-next", StringComparison.OrdinalIgnoreCase) ||
                    // Text-based detection
                    (text.Equals("Next", StringComparison.OrdinalIgnoreCase) && href.Contains("page=")) ||
                    (text.Equals("Next ›", StringComparison.OrdinalIgnoreCase)) ||
                    (text.Equals("›", StringComparison.OrdinalIgnoreCase) && href.Contains("page=")) ||
                    (text.Equals("»", StringComparison.OrdinalIgnoreCase) && href.Contains("page=")) ||
                    (text.Equals("Next →", StringComparison.OrdinalIgnoreCase));

                if (!isNextPage) continue;

                // Resolve to absolute URL
                string fullUrl;
                if (href.StartsWith('?'))
                {
                    fullUrl = currentPageBase + href;
                }
                else
                {
                    fullUrl = MakeAbsoluteUrl(currentPageBase, href);
                }

                if (string.IsNullOrEmpty(fullUrl)) continue;
                if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out var targetUri)) continue;
                if (!targetUri.Host.Equals(currentUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

                return fullUrl;
            }

            return null;
        }

        /// <summary>
        /// Extracts the current page number from a URL with ?page=N query parameter.
        /// </summary>
        private static int GetPageNumber(string url)
        {
            var match = Regex.Match(url, @"[?&]page=(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        #endregion

        #region Helpers

        private async Task<HttpResponseMessage?> SendWithRetryAsync(HttpRequestMessage request, int maxRetries, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var clone = new HttpRequestMessage(request.Method, request.RequestUri);
                    foreach (var header in request.Headers)
                    {
                        clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    var response = await _httpClient.SendAsync(clone, cancellationToken);

                    if (response.IsSuccessStatusCode)
                        return response;

                    // Retry on rate limit (429) or server error (5xx)
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        var delay = (int)Math.Pow(2, attempt) * 1000;
                        Debug.WriteLine($"[Retry] HTTP {response.StatusCode}, waiting {delay}ms (attempt {attempt + 1}/{maxRetries + 1})");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    // 403 Forbidden — likely WAF
                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        Debug.WriteLine($"[Scan] 403 Forbidden for {request.RequestUri}");
                        return response;
                    }

                    return response;
                }
                catch (HttpRequestException) when (attempt < maxRetries)
                {
                    var delay = (int)Math.Pow(2, attempt) * 1000;
                    await Task.Delay(delay, cancellationToken);
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a mirror sub-directory from a source URL path.
        /// </summary>
        private static string GetMirrorSubDirectory(string sourceUrl)
        {
            try
            {
                var uri = new Uri(sourceUrl);
                var path = uri.AbsolutePath.Trim('/');
                var segments = path.Split('/')
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => SanitizeFileName(Uri.UnescapeDataString(s)));
                return Path.Combine(segments.ToArray());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string MakeAbsoluteUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl))
                return string.Empty;

            if (Uri.IsWellFormedUriString(relativeUrl, UriKind.Absolute))
                return relativeUrl;

            if (Uri.TryCreate(new Uri(baseUrl), relativeUrl, out var absoluteUri))
                return absoluteUri.ToString();

            return string.Empty;
        }

        private static bool IsMatchingPattern(string url, string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return true;

            try
            {
                return Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return url.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Extracts the clean file extension from a URL (stripping query string).
        /// </summary>
        private static string GetCleanExtension(string url)
        {
            var cleanUrl = url.Split('?')[0].Split('#')[0];
            return Path.GetExtension(cleanUrl).ToLowerInvariant();
        }

        private static string SanitizeFileName(string fileName)
        {
            var decoded = WebUtility.UrlDecode(fileName);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                decoded = decoded.Replace(c, '_');
            }
            return decoded;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                    _httpHandler?.Dispose();
                    _pauseEvent?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    #endregion
}
