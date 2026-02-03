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
    public class WebsiteDownloaderService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _httpHandler;
        private bool _disposed;
        
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
            _httpHandler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            
            _httpClient = new HttpClient(_httpHandler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            
            // Set browser-like headers to avoid WAF blocking
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
        }
        
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
                }
                _disposed = true;
            }
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
            // For pagination, we use a higher depth limit
            int effectiveMaxDepth = options.FollowPagination ? Math.Max(options.MaxDepth, options.MaxPaginationPages) : options.MaxDepth;
            
            if (depth > effectiveMaxDepth || visitedUrls.Contains(currentUrl))
                return;

            visitedUrls.Add(currentUrl);

            try
            {
                // Use HttpRequestMessage to add Referer header for pagination
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                if (depth > 0)
                {
                    // Add referer from base URL to look like natural navigation
                    request.Headers.Add("Referer", baseUrl);
                }
                
                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[Scan] HTTP {(int)response.StatusCode} for {currentUrl}");
                    return;
                }

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
                            // Remove query string from extension check
                            var cleanUrl = fullUrl.Split('?')[0];
                            ext = Path.GetExtension(cleanUrl).ToLowerInvariant();
                            if (DocumentExtensions.Contains(ext) && IsMatchingPattern(fullUrl, options.UrlPattern))
                            {
                                items.Add(new DownloadItem
                                {
                                    Url = fullUrl,
                                    FileName = Path.GetFileName(new Uri(cleanUrl).AbsolutePath),
                                    Type = "Document",
                                    Status = "Pending"
                                });
                            }
                        }
                    }
                }

                // Follow pagination links (Next, page numbers, etc.)
                if (options.FollowPagination && depth < options.MaxPaginationPages)
                {
                    var paginationLinks = FindPaginationLinks(doc, baseUrl, currentUrl);
                    System.Diagnostics.Debug.WriteLine($"[Pagination] Found {paginationLinks.Count} pagination links on {currentUrl}");
                    foreach (var pageUrl in paginationLinks)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Pagination] Next page URL: {pageUrl}");
                        if (!visitedUrls.Contains(pageUrl))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Pagination] Scanning page: {pageUrl}");
                            // Add a small delay between pagination requests to avoid rate limiting
                            await Task.Delay(500, cancellationToken);
                            // For pagination, we stay at the same depth conceptually
                            await ScanUrlRecursiveAsync(baseUrl, pageUrl, items, visitedUrls, options, depth + 1, cancellationToken);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[Pagination] Already visited: {pageUrl}");
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

        /// <summary>
        /// Finds pagination links (Next, page numbers, etc.) in the HTML document.
        /// </summary>
        private List<string> FindPaginationLinks(HtmlDocument doc, string baseUrl, string currentUrl)
        {
            var paginationLinks = new List<string>();
            var currentUri = new Uri(currentUrl);
            
            // For query-string pagination like ?page=X, we need to resolve against current URL's path
            // Get the base path without query string for resolving relative URLs
            var currentPageBase = currentUrl.Split('?')[0];
            
            System.Diagnostics.Debug.WriteLine($"[FindPagination] Searching for pagination links in: {currentUrl}");
            System.Diagnostics.Debug.WriteLine($"[FindPagination] Current page base: {currentPageBase}");
            
            // Simple approach: Search ALL links for "Next page" pattern
            var allLinks = doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>();
            
            System.Diagnostics.Debug.WriteLine($"[FindPagination] Found {allLinks.Count()} total links");
            
            foreach (var link in allLinks)
            {
                var href = link.GetAttributeValue("href", "");
                var ariaLabel = link.GetAttributeValue("aria-label", "");
                var linkClass = link.GetAttributeValue("class", "");
                var text = link.InnerText?.Trim() ?? "";
                
                // Skip empty hrefs
                if (string.IsNullOrWhiteSpace(href))
                    continue;
                
                // Check for "Next page" link - multiple patterns
                bool isNextPage = ariaLabel.Equals("Next page", StringComparison.OrdinalIgnoreCase) ||
                                  linkClass.Contains("next-page", StringComparison.OrdinalIgnoreCase) ||
                                  linkClass.Contains("next_page", StringComparison.OrdinalIgnoreCase) ||
                                  linkClass.Contains("__next-page", StringComparison.OrdinalIgnoreCase) ||
                                  linkClass.Contains("pager-next", StringComparison.OrdinalIgnoreCase) ||
                                  linkClass.Contains("pager__next", StringComparison.OrdinalIgnoreCase) ||
                                  (text.Equals("Next", StringComparison.OrdinalIgnoreCase) && href.Contains("page="));
                
                // Skip "Last page" - we only want sequential next
                if (ariaLabel.Equals("Last page", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Skip data set navigation
                if (text.Contains("Data Set", StringComparison.OrdinalIgnoreCase) || 
                    text.Contains("Dataset", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                if (isNextPage)
                {
                    System.Diagnostics.Debug.WriteLine($"[FindPagination] FOUND NEXT PAGE: href={href}, text={text}, aria={ariaLabel}");
                    
                    // Resolve the URL
                    string fullUrl;
                    if (href.StartsWith("?"))
                    {
                        fullUrl = currentPageBase + href;
                    }
                    else
                    {
                        fullUrl = MakeAbsoluteUrl(currentPageBase, href);
                    }
                    
                    if (!string.IsNullOrEmpty(fullUrl) && Uri.TryCreate(fullUrl, UriKind.Absolute, out var targetUri))
                    {
                        if (targetUri.Host.Equals(currentUri.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!paginationLinks.Contains(fullUrl))
                            {
                                paginationLinks.Add(fullUrl);
                                return paginationLinks; // Found next page, return immediately
                            }
                        }
                    }
                }
            }
            
            return paginationLinks;
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
        public bool FollowPagination { get; set; } = true;  // New option to follow Next/page links
        public int MaxDepth { get; set; } = 1;
        public int MaxLinksPerPage { get; set; } = 50;
        public int MaxPaginationPages { get; set; } = 100;  // Maximum pages to follow in pagination
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
