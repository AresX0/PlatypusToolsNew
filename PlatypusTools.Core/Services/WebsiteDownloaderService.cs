using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace PlatypusTools.Core.Services
{
    public class WebsiteDownloaderService
    {
        private readonly HttpClient _httpClient;
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico"
        };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv"
        };

        public WebsiteDownloaderService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task<List<DownloadItem>> ScanUrlAsync(string url, DownloadOptions options, CancellationToken cancellationToken = default)
        {
            var items = new List<DownloadItem>();
            var visitedUrls = new HashSet<string>();

            await ScanUrlRecursiveAsync(url, url, items, visitedUrls, options, 0, cancellationToken);

            return items;
        }

        private async Task ScanUrlRecursiveAsync(string baseUrl, string currentUrl, List<DownloadItem> items, 
            HashSet<string> visitedUrls, DownloadOptions options, int depth, CancellationToken cancellationToken)
        {
            if (depth > options.MaxDepth || visitedUrls.Contains(currentUrl))
                return;

            visitedUrls.Add(currentUrl);

            try
            {
                var response = await _httpClient.GetAsync(currentUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return;

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Find all links
                var links = new List<string>();

                // Images
                if (options.DownloadImages)
                {
                    foreach (var img in doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>())
                    {
                        var src = img.GetAttributeValue("src", "");
                        var fullUrl = MakeAbsoluteUrl(baseUrl, src);
                        if (!string.IsNullOrEmpty(fullUrl) && IsMatchingPattern(fullUrl, options.UrlPattern))
                        {
                            items.Add(new DownloadItem
                            {
                                Url = fullUrl,
                                FileName = Path.GetFileName(new Uri(fullUrl).AbsolutePath),
                                Type = "Image",
                                Status = "Pending"
                            });
                        }
                    }
                }

                // Videos
                if (options.DownloadVideos)
                {
                    foreach (var video in doc.DocumentNode.SelectNodes("//video[@src]|//video//source[@src]") ?? Enumerable.Empty<HtmlNode>())
                    {
                        var src = video.GetAttributeValue("src", "");
                        var fullUrl = MakeAbsoluteUrl(baseUrl, src);
                        if (!string.IsNullOrEmpty(fullUrl) && IsMatchingPattern(fullUrl, options.UrlPattern))
                        {
                            items.Add(new DownloadItem
                            {
                                Url = fullUrl,
                                FileName = Path.GetFileName(new Uri(fullUrl).AbsolutePath),
                                Type = "Video",
                                Status = "Pending"
                            });
                        }
                    }
                }

                // Documents and other files
                if (options.DownloadDocuments)
                {
                    foreach (var link in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
                    {
                        var href = link.GetAttributeValue("href", "");
                        var fullUrl = MakeAbsoluteUrl(baseUrl, href);
                        if (!string.IsNullOrEmpty(fullUrl))
                        {
                            var ext = Path.GetExtension(fullUrl).ToLowerInvariant();
                            if (DocumentExtensions.Contains(ext) && IsMatchingPattern(fullUrl, options.UrlPattern))
                            {
                                items.Add(new DownloadItem
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(new Uri(fullUrl).AbsolutePath),
                                    Type = "Document",
                                    Status = "Pending"
                                });
                            }
                        }
                    }
                }

                // Recursive crawl
                if (options.RecursiveCrawl && depth < options.MaxDepth)
                {
                    foreach (var link in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
                    {
                        var href = link.GetAttributeValue("href", "");
                        var fullUrl = MakeAbsoluteUrl(baseUrl, href);
                        
                        if (!string.IsNullOrEmpty(fullUrl) && fullUrl.StartsWith(baseUrl) && !visitedUrls.Contains(fullUrl))
                        {
                            links.Add(fullUrl);
                        }
                    }

                    foreach (var link in links.Take(options.MaxLinksPerPage))
                    {
                        await ScanUrlRecursiveAsync(baseUrl, link, items, visitedUrls, options, depth + 1, cancellationToken);
                    }
                }
            }
            catch (Exception)
            {
                // Log error but continue
            }
        }

        public async Task<DownloadResult> DownloadFileAsync(DownloadItem item, string outputDirectory, 
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var fileName = SanitizeFileName(item.FileName);
                var filePath = Path.Combine(outputDirectory, fileName);

                // Handle duplicates
                if (File.Exists(filePath))
                {
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var counter = 1;
                    while (File.Exists(filePath))
                    {
                        fileName = $"{name}_{counter}{ext}";
                        filePath = Path.Combine(outputDirectory, fileName);
                        counter++;
                    }
                }

                using var response = await _httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var percentage = (int)((downloadedBytes * 100) / totalBytes);
                        progress?.Report(new DownloadProgress
                        {
                            Url = item.Url,
                            BytesDownloaded = downloadedBytes,
                            TotalBytes = totalBytes,
                            Percentage = percentage
                        });
                    }
                }

                return new DownloadResult
                {
                    Success = true,
                    FilePath = filePath,
                    BytesDownloaded = downloadedBytes
                };
            }
            catch (Exception ex)
            {
                return new DownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string MakeAbsoluteUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl))
                return string.Empty;

            if (Uri.IsWellFormedUriString(relativeUrl, UriKind.Absolute))
                return relativeUrl;

            if (Uri.TryCreate(new Uri(baseUrl), relativeUrl, out var absoluteUri))
                return absoluteUri.ToString();

            return string.Empty;
        }

        private bool IsMatchingPattern(string url, string? pattern)
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

        private string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
    }

    public class DownloadOptions
    {
        public bool DownloadImages { get; set; } = true;
        public bool DownloadVideos { get; set; } = true;
        public bool DownloadDocuments { get; set; } = true;
        public bool RecursiveCrawl { get; set; } = false;
        public int MaxDepth { get; set; } = 1;
        public int MaxLinksPerPage { get; set; } = 50;
        public string? UrlPattern { get; set; }
        public int MaxConcurrentDownloads { get; set; } = 3;
    }

    public class DownloadItem
    {
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Size { get; set; }
        public int Progress { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DownloadProgress
    {
        public string Url { get; set; } = string.Empty;
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public int Percentage { get; set; }
    }

    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long BytesDownloaded { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
