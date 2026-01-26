using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    #region Models

    /// <summary>
    /// Indicator of Compromise types.
    /// </summary>
    public enum IOCType
    {
        MD5,
        SHA1,
        SHA256,
        IPv4,
        IPv6,
        Domain,
        URL,
        Email,
        FileName,
        FilePath,
        RegistryKey,
        Mutex,
        PipeName,
        CVE,
        YARA,
        Custom
    }

    /// <summary>
    /// Represents a single Indicator of Compromise.
    /// </summary>
    public class IOC
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public IOCType Type { get; set; }
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string ThreatName { get; set; } = string.Empty;
        public string Severity { get; set; } = "medium";
        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// IOC feed source configuration.
    /// </summary>
    public class IOCFeed
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public IOCFeedType FeedType { get; set; }
        public bool IsEnabled { get; set; } = true;
        public DateTime LastUpdated { get; set; }
        public int IOCCount { get; set; }
    }

    public enum IOCFeedType
    {
        PlainText,
        CSV,
        JSON,
        STIX,
        OpenIOC,
        MISP
    }

    /// <summary>
    /// Result of an IOC scan match.
    /// </summary>
    public class IOCMatch
    {
        public IOC IOC { get; set; } = new();
        public string MatchLocation { get; set; } = string.Empty;
        public string MatchContext { get; set; } = string.Empty;
        public DateTime MatchTime { get; set; } = DateTime.Now;
        public double Confidence { get; set; } = 1.0;
    }

    /// <summary>
    /// Complete IOC scan result.
    /// </summary>
    public class IOCScanResult
    {
        public string ScanPath { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int FilesScanned { get; set; }
        public int IOCsChecked { get; set; }
        public List<IOCMatch> Matches { get; } = new();
        public List<string> Errors { get; } = new();
        
        public TimeSpan Duration => EndTime - StartTime;
    }

    #endregion

    /// <summary>
    /// IOC Scanner service for detecting Indicators of Compromise.
    /// Supports hash matching, IP/domain blocklists, STIX/TAXII feeds, and pattern matching.
    /// </summary>
    public class IOCScannerService : ForensicOperationBase
    {
        private readonly HttpClient _httpClient;
        private readonly List<IOC> _iocs = new();
        private readonly List<IOCFeed> _feeds = new();

        private static readonly string IOCDatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "ioc_database.json");

        private static readonly string FeedsConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "ioc_feeds.json");

        public override string OperationName => "IOC Scanner";

        // Scan options
        public bool ScanFileHashes { get; set; } = true;
        public bool ScanFileContents { get; set; } = true;
        public bool ScanFileNames { get; set; } = true;
        public bool ScanFilePaths { get; set; } = true;
        public bool ScanNetworkIOCs { get; set; } = true;
        public string[] FileExtensions { get; set; } = { "*" };
        public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024; // 100MB

        public IReadOnlyList<IOC> IOCs => _iocs.AsReadOnly();
        public IReadOnlyList<IOCFeed> Feeds => _feeds.AsReadOnly();

        // Regex patterns for IOC extraction
        private static readonly Regex IPv4Regex = new(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b", RegexOptions.Compiled);
        private static readonly Regex IPv6Regex = new(@"\b(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}\b", RegexOptions.Compiled);
        private static readonly Regex DomainRegex = new(@"\b(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}\b", RegexOptions.Compiled);
        private static readonly Regex MD5Regex = new(@"\b[a-fA-F0-9]{32}\b", RegexOptions.Compiled);
        private static readonly Regex SHA1Regex = new(@"\b[a-fA-F0-9]{40}\b", RegexOptions.Compiled);
        private static readonly Regex SHA256Regex = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
        private static readonly Regex URLRegex = new(@"\bhttps?://[^\s<>\""]+\b", RegexOptions.Compiled);

        public IOCScannerService()
        {
            // Use shared HttpClient from factory
            _httpClient = PlatypusTools.Core.Services.HttpClientFactory.Api;
            LoadDatabase();
            LoadFeeds();
        }

        #region IOC Management

        /// <summary>
        /// Adds an IOC to the database.
        /// </summary>
        public void AddIOC(IOC ioc)
        {
            if (!_iocs.Any(i => i.Type == ioc.Type && i.Value.Equals(ioc.Value, StringComparison.OrdinalIgnoreCase)))
            {
                _iocs.Add(ioc);
                SaveDatabase();
            }
        }

        /// <summary>
        /// Adds multiple IOCs to the database.
        /// </summary>
        public void AddIOCs(IEnumerable<IOC> iocs)
        {
            var added = 0;
            foreach (var ioc in iocs)
            {
                if (!_iocs.Any(i => i.Type == ioc.Type && i.Value.Equals(ioc.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    _iocs.Add(ioc);
                    added++;
                }
            }
            if (added > 0)
            {
                SaveDatabase();
                Log($"Added {added} new IOCs");
            }
        }

        /// <summary>
        /// Removes an IOC from the database.
        /// </summary>
        public void RemoveIOC(string id)
        {
            var ioc = _iocs.Find(i => i.Id == id);
            if (ioc != null)
            {
                _iocs.Remove(ioc);
                SaveDatabase();
            }
        }

        /// <summary>
        /// Clears all IOCs from the database.
        /// </summary>
        public void ClearIOCs()
        {
            _iocs.Clear();
            SaveDatabase();
        }

        #endregion

        #region Feed Management

        /// <summary>
        /// Adds a feed source.
        /// </summary>
        public void AddFeed(IOCFeed feed)
        {
            if (!_feeds.Any(f => f.Url.Equals(feed.Url, StringComparison.OrdinalIgnoreCase)))
            {
                _feeds.Add(feed);
                SaveFeeds();
            }
        }

        /// <summary>
        /// Updates IOCs from a specific feed.
        /// </summary>
        public async Task<int> UpdateFeedAsync(IOCFeed feed, CancellationToken token = default)
        {
            Log($"Updating feed: {feed.Name}");
            var beforeCount = _iocs.Count;

            try
            {
                var response = await _httpClient.GetStringAsync(feed.Url, token);
                var iocs = ParseFeed(feed, response);
                AddIOCs(iocs);

                feed.LastUpdated = DateTime.Now;
                feed.IOCCount = iocs.Count();
                SaveFeeds();

                var added = _iocs.Count - beforeCount;
                LogSuccess($"Feed {feed.Name}: {added} new IOCs added");
                return added;
            }
            catch (Exception ex)
            {
                LogError($"Failed to update feed {feed.Name}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Updates all enabled feeds.
        /// </summary>
        public async Task<int> UpdateAllFeedsAsync(CancellationToken token = default)
        {
            LogHeader("Updating all IOC feeds");
            var totalAdded = 0;

            foreach (var feed in _feeds.Where(f => f.IsEnabled))
            {
                token.ThrowIfCancellationRequested();
                totalAdded += await UpdateFeedAsync(feed, token);
            }

            LogSuccess($"Feed update complete: {totalAdded} new IOCs");
            return totalAdded;
        }

        private IEnumerable<IOC> ParseFeed(IOCFeed feed, string content)
        {
            return feed.FeedType switch
            {
                IOCFeedType.PlainText => ParsePlainTextFeed(content, feed.Name),
                IOCFeedType.CSV => ParseCSVFeed(content, feed.Name),
                IOCFeedType.JSON => ParseJSONFeed(content, feed.Name),
                IOCFeedType.STIX => ParseSTIXFeed(content, feed.Name),
                _ => Enumerable.Empty<IOC>()
            };
        }

        private IEnumerable<IOC> ParsePlainTextFeed(string content, string source)
        {
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var value = line.Trim();
                if (string.IsNullOrEmpty(value) || value.StartsWith("#")) continue;

                var type = DetectIOCType(value);
                if (type.HasValue)
                {
                    yield return new IOC
                    {
                        Type = type.Value,
                        Value = value,
                        Source = source
                    };
                }
            }
        }

        private IEnumerable<IOC> ParseCSVFeed(string content, string source)
        {
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var isHeader = true;
            
            foreach (var line in lines)
            {
                if (isHeader) { isHeader = false; continue; }
                
                var parts = line.Split(',');
                if (parts.Length >= 1)
                {
                    var value = parts[0].Trim().Trim('"');
                    var type = DetectIOCType(value);
                    if (type.HasValue)
                    {
                        yield return new IOC
                        {
                            Type = type.Value,
                            Value = value,
                            Source = source,
                            Description = parts.Length > 1 ? parts[1].Trim().Trim('"') : ""
                        };
                    }
                }
            }
        }

        private IEnumerable<IOC> ParseJSONFeed(string content, string source)
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var ioc = ParseJSONIOC(item, source);
                    if (ioc != null) yield return ioc;
                }
            }
            else if (root.TryGetProperty("data", out var data) || root.TryGetProperty("indicators", out data) || root.TryGetProperty("iocs", out data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var ioc = ParseJSONIOC(item, source);
                    if (ioc != null) yield return ioc;
                }
            }
        }

        private IOC? ParseJSONIOC(JsonElement element, string source)
        {
            string? value = null;
            IOCType? type = null;

            if (element.TryGetProperty("indicator", out var ind)) value = ind.GetString();
            else if (element.TryGetProperty("value", out var val)) value = val.GetString();
            else if (element.TryGetProperty("ioc", out var iocVal)) value = iocVal.GetString();

            if (string.IsNullOrEmpty(value)) return null;

            if (element.TryGetProperty("type", out var typeEl))
            {
                var typeStr = typeEl.GetString()?.ToLower();
                type = typeStr switch
                {
                    "md5" => IOCType.MD5,
                    "sha1" => IOCType.SHA1,
                    "sha256" => IOCType.SHA256,
                    "ipv4" or "ip" or "ipv4-addr" => IOCType.IPv4,
                    "ipv6" or "ipv6-addr" => IOCType.IPv6,
                    "domain" or "domain-name" => IOCType.Domain,
                    "url" => IOCType.URL,
                    "email" or "email-addr" => IOCType.Email,
                    _ => DetectIOCType(value)
                };
            }
            else
            {
                type = DetectIOCType(value);
            }

            if (!type.HasValue) return null;

            return new IOC
            {
                Type = type.Value,
                Value = value,
                Source = source,
                Description = element.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                ThreatName = element.TryGetProperty("threat", out var threat) ? threat.GetString() ?? "" : ""
            };
        }

        private IEnumerable<IOC> ParseSTIXFeed(string content, string source)
        {
            // Basic STIX 2.x parsing
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            JsonElement objects;
            if (root.TryGetProperty("objects", out objects))
            {
                foreach (var obj in objects.EnumerateArray())
                {
                    if (!obj.TryGetProperty("type", out var typeEl)) continue;
                    var objType = typeEl.GetString();

                    if (objType == "indicator" && obj.TryGetProperty("pattern", out var pattern))
                    {
                        var patternStr = pattern.GetString() ?? "";
                        var iocs = ExtractIOCsFromSTIXPattern(patternStr, source);
                        foreach (var ioc in iocs) yield return ioc;
                    }
                }
            }
        }

        private IEnumerable<IOC> ExtractIOCsFromSTIXPattern(string pattern, string source)
        {
            // Parse STIX patterns like: [file:hashes.MD5 = 'd41d8cd98f00b204e9800998ecf8427e']
            var hashMatch = Regex.Match(pattern, @"file:hashes\.'?(MD5|SHA-1|SHA-256|SHA1|SHA256)'?\s*=\s*'([^']+)'", RegexOptions.IgnoreCase);
            if (hashMatch.Success)
            {
                var hashType = hashMatch.Groups[1].Value.ToUpper().Replace("-", "");
                var hashValue = hashMatch.Groups[2].Value;
                yield return new IOC
                {
                    Type = hashType switch { "MD5" => IOCType.MD5, "SHA1" => IOCType.SHA1, _ => IOCType.SHA256 },
                    Value = hashValue,
                    Source = source
                };
            }

            var ipMatch = Regex.Match(pattern, @"ipv4-addr:value\s*=\s*'([^']+)'");
            if (ipMatch.Success)
            {
                yield return new IOC { Type = IOCType.IPv4, Value = ipMatch.Groups[1].Value, Source = source };
            }

            var domainMatch = Regex.Match(pattern, @"domain-name:value\s*=\s*'([^']+)'");
            if (domainMatch.Success)
            {
                yield return new IOC { Type = IOCType.Domain, Value = domainMatch.Groups[1].Value, Source = source };
            }

            var urlMatch = Regex.Match(pattern, @"url:value\s*=\s*'([^']+)'");
            if (urlMatch.Success)
            {
                yield return new IOC { Type = IOCType.URL, Value = urlMatch.Groups[1].Value, Source = source };
            }
        }

        private IOCType? DetectIOCType(string value)
        {
            if (SHA256Regex.IsMatch(value)) return IOCType.SHA256;
            if (SHA1Regex.IsMatch(value)) return IOCType.SHA1;
            if (MD5Regex.IsMatch(value)) return IOCType.MD5;
            if (IPv4Regex.IsMatch(value)) return IOCType.IPv4;
            if (IPv6Regex.IsMatch(value)) return IOCType.IPv6;
            if (URLRegex.IsMatch(value)) return IOCType.URL;
            if (EmailRegex.IsMatch(value)) return IOCType.Email;
            if (DomainRegex.IsMatch(value)) return IOCType.Domain;
            return null;
        }

        #endregion

        #region Scanning

        /// <summary>
        /// Scans a directory for IOC matches.
        /// </summary>
        public async Task<IOCScanResult> ScanDirectoryAsync(string path, CancellationToken token = default)
        {
            var result = new IOCScanResult
            {
                ScanPath = path,
                StartTime = DateTime.Now,
                IOCsChecked = _iocs.Count
            };

            LogHeader($"IOC Scan: {path}");
            Log($"Loaded {_iocs.Count} IOCs from database");

            if (!Directory.Exists(path))
            {
                result.Errors.Add($"Directory not found: {path}");
                result.EndTime = DateTime.Now;
                return result;
            }

            // Build lookup sets for fast matching
            var hashLookup = BuildHashLookup();
            var networkLookup = BuildNetworkLookup();
            var fileNameLookup = BuildFileNameLookup();

            try
            {
                var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
                
                await Task.Run(async () =>
                {
                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length > MaxFileSizeBytes) continue;

                            result.FilesScanned++;
                        ReportProgress(fi.Name);
                            // Check file name
                            if (ScanFileNames)
                            {
                                var matches = CheckFileName(fi.Name, fileNameLookup);
                                foreach (var m in matches)
                                {
                                    m.MatchLocation = file;
                                    result.Matches.Add(m);
                                }
                            }

                            // Check file path
                            if (ScanFilePaths)
                            {
                                var matches = CheckFilePath(file, _iocs.Where(i => i.Type == IOCType.FilePath));
                                foreach (var m in matches)
                                {
                                    m.MatchLocation = file;
                                    result.Matches.Add(m);
                                }
                            }

                            // Check file hash
                            if (ScanFileHashes && hashLookup.Count > 0)
                            {
                                var matches = await CheckFileHashAsync(file, hashLookup, token);
                                result.Matches.AddRange(matches);
                            }

                            // Check file contents for network IOCs
                            if (ScanFileContents && ScanNetworkIOCs && networkLookup.Count > 0)
                            {
                                var matches = await CheckFileContentsAsync(file, networkLookup, token);
                                result.Matches.AddRange(matches);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"{file}: {ex.Message}");
                        }
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                Log("Scan cancelled");
            }

            result.EndTime = DateTime.Now;
            LogSuccess($"Scan complete: {result.FilesScanned} files, {result.Matches.Count} matches in {result.Duration.TotalSeconds:F1}s");

            return result;
        }

        private Dictionary<string, IOC> BuildHashLookup()
        {
            var lookup = new Dictionary<string, IOC>(StringComparer.OrdinalIgnoreCase);
            foreach (var ioc in _iocs.Where(i => i.Type == IOCType.MD5 || i.Type == IOCType.SHA1 || i.Type == IOCType.SHA256))
            {
                lookup.TryAdd(ioc.Value, ioc);
            }
            return lookup;
        }

        private Dictionary<string, IOC> BuildNetworkLookup()
        {
            var lookup = new Dictionary<string, IOC>(StringComparer.OrdinalIgnoreCase);
            foreach (var ioc in _iocs.Where(i => i.Type == IOCType.IPv4 || i.Type == IOCType.IPv6 || i.Type == IOCType.Domain || i.Type == IOCType.URL || i.Type == IOCType.Email))
            {
                lookup.TryAdd(ioc.Value, ioc);
            }
            return lookup;
        }

        private Dictionary<string, IOC> BuildFileNameLookup()
        {
            var lookup = new Dictionary<string, IOC>(StringComparer.OrdinalIgnoreCase);
            foreach (var ioc in _iocs.Where(i => i.Type == IOCType.FileName))
            {
                lookup.TryAdd(ioc.Value, ioc);
            }
            return lookup;
        }

        private IEnumerable<IOCMatch> CheckFileName(string fileName, Dictionary<string, IOC> lookup)
        {
            if (lookup.TryGetValue(fileName, out var ioc))
            {
                yield return new IOCMatch
                {
                    IOC = ioc,
                    MatchContext = $"File name: {fileName}",
                    Confidence = 1.0
                };
            }

            // Partial match
            foreach (var kvp in lookup)
            {
                if (fileName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new IOCMatch
                    {
                        IOC = kvp.Value,
                        MatchContext = $"File name contains: {kvp.Key}",
                        Confidence = 0.7
                    };
                }
            }
        }

        private IEnumerable<IOCMatch> CheckFilePath(string filePath, IEnumerable<IOC> pathIOCs)
        {
            foreach (var ioc in pathIOCs)
            {
                if (filePath.Contains(ioc.Value, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new IOCMatch
                    {
                        IOC = ioc,
                        MatchLocation = filePath,
                        MatchContext = $"Path contains: {ioc.Value}",
                        Confidence = 0.9
                    };
                }
            }
        }

        private async Task<List<IOCMatch>> CheckFileHashAsync(string filePath, Dictionary<string, IOC> hashLookup, CancellationToken token)
        {
            var matches = new List<IOCMatch>();

            try
            {
                using var stream = File.OpenRead(filePath);
                
                // Calculate MD5
                using var md5 = MD5.Create();
                var md5Hash = await ComputeHashAsync(md5, stream, token);
                stream.Position = 0;

                if (hashLookup.TryGetValue(md5Hash, out var md5Ioc))
                {
                    matches.Add(new IOCMatch
                    {
                        IOC = md5Ioc,
                        MatchLocation = filePath,
                        MatchContext = $"MD5: {md5Hash}",
                        Confidence = 1.0
                    });
                }

                // Calculate SHA1
                using var sha1 = SHA1.Create();
                var sha1Hash = await ComputeHashAsync(sha1, stream, token);
                stream.Position = 0;

                if (hashLookup.TryGetValue(sha1Hash, out var sha1Ioc))
                {
                    matches.Add(new IOCMatch
                    {
                        IOC = sha1Ioc,
                        MatchLocation = filePath,
                        MatchContext = $"SHA1: {sha1Hash}",
                        Confidence = 1.0
                    });
                }

                // Calculate SHA256
                using var sha256 = SHA256.Create();
                var sha256Hash = await ComputeHashAsync(sha256, stream, token);

                if (hashLookup.TryGetValue(sha256Hash, out var sha256Ioc))
                {
                    matches.Add(new IOCMatch
                    {
                        IOC = sha256Ioc,
                        MatchLocation = filePath,
                        MatchContext = $"SHA256: {sha256Hash}",
                        Confidence = 1.0
                    });
                }
            }
            catch { /* Skip files we can't hash */ }

            return matches;
        }

        private async Task<string> ComputeHashAsync(HashAlgorithm algorithm, Stream stream, CancellationToken token)
        {
            var hash = await algorithm.ComputeHashAsync(stream, token);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private async Task<List<IOCMatch>> CheckFileContentsAsync(string filePath, Dictionary<string, IOC> networkLookup, CancellationToken token)
        {
            var matches = new List<IOCMatch>();

            try
            {
                var content = await File.ReadAllTextAsync(filePath, token);

                // Extract network indicators from content
                var foundIPs = IPv4Regex.Matches(content).Cast<Match>().Select(m => m.Value);
                var foundDomains = DomainRegex.Matches(content).Cast<Match>().Select(m => m.Value);
                var foundURLs = URLRegex.Matches(content).Cast<Match>().Select(m => m.Value);
                var foundEmails = EmailRegex.Matches(content).Cast<Match>().Select(m => m.Value);

                foreach (var ip in foundIPs)
                {
                    if (networkLookup.TryGetValue(ip, out var ioc))
                    {
                        matches.Add(new IOCMatch
                        {
                            IOC = ioc,
                            MatchLocation = filePath,
                            MatchContext = $"IP in content: {ip}",
                            Confidence = 0.95
                        });
                    }
                }

                foreach (var domain in foundDomains)
                {
                    if (networkLookup.TryGetValue(domain, out var ioc))
                    {
                        matches.Add(new IOCMatch
                        {
                            IOC = ioc,
                            MatchLocation = filePath,
                            MatchContext = $"Domain in content: {domain}",
                            Confidence = 0.9
                        });
                    }
                }

                foreach (var url in foundURLs)
                {
                    if (networkLookup.TryGetValue(url, out var ioc))
                    {
                        matches.Add(new IOCMatch
                        {
                            IOC = ioc,
                            MatchLocation = filePath,
                            MatchContext = $"URL in content: {url}",
                            Confidence = 0.95
                        });
                    }
                }

                foreach (var email in foundEmails)
                {
                    if (networkLookup.TryGetValue(email, out var ioc))
                    {
                        matches.Add(new IOCMatch
                        {
                            IOC = ioc,
                            MatchLocation = filePath,
                            MatchContext = $"Email in content: {email}",
                            Confidence = 0.9
                        });
                    }
                }
            }
            catch { /* Skip binary/unreadable files */ }

            return matches;
        }

        #endregion

        #region Persistence

        private void LoadDatabase()
        {
            try
            {
                if (File.Exists(IOCDatabasePath))
                {
                    var json = File.ReadAllText(IOCDatabasePath);
                    var iocs = JsonSerializer.Deserialize<List<IOC>>(json);
                    if (iocs != null)
                    {
                        _iocs.Clear();
                        _iocs.AddRange(iocs);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to load IOC database: {ex.Message}");
            }
        }

        private void SaveDatabase()
        {
            try
            {
                var dir = Path.GetDirectoryName(IOCDatabasePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_iocs, new JsonSerializerOptions { WriteIndented = true });
                _ = File.WriteAllTextAsync(IOCDatabasePath, json);
            }
            catch (Exception ex)
            {
                LogError($"Failed to save IOC database: {ex.Message}");
            }
        }

        private void LoadFeeds()
        {
            try
            {
                if (File.Exists(FeedsConfigPath))
                {
                    var json = File.ReadAllText(FeedsConfigPath);
                    var feeds = JsonSerializer.Deserialize<List<IOCFeed>>(json);
                    if (feeds != null)
                    {
                        _feeds.Clear();
                        _feeds.AddRange(feeds);
                    }
                }
                else
                {
                    // Add default feeds
                    InitializeDefaultFeeds();
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to load feeds: {ex.Message}");
                InitializeDefaultFeeds();
            }
        }

        private void InitializeDefaultFeeds()
        {
            _feeds.AddRange(new[]
            {
                new IOCFeed { Name = "Abuse.ch Malware Bazaar", Url = "https://bazaar.abuse.ch/export/txt/md5/recent/", FeedType = IOCFeedType.PlainText },
                new IOCFeed { Name = "Abuse.ch URLhaus", Url = "https://urlhaus.abuse.ch/downloads/text_recent/", FeedType = IOCFeedType.PlainText },
                new IOCFeed { Name = "Abuse.ch Feodo Tracker", Url = "https://feodotracker.abuse.ch/downloads/ipblocklist.txt", FeedType = IOCFeedType.PlainText },
                new IOCFeed { Name = "Emerging Threats", Url = "https://rules.emergingthreats.net/blockrules/compromised-ips.txt", FeedType = IOCFeedType.PlainText, IsEnabled = false }
            });
            SaveFeeds();
        }

        private void SaveFeeds()
        {
            try
            {
                var dir = Path.GetDirectoryName(FeedsConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_feeds, new JsonSerializerOptions { WriteIndented = true });
                _ = File.WriteAllTextAsync(FeedsConfigPath, json);
            }
            catch (Exception ex)
            {
                LogError($"Failed to save feeds: {ex.Message}");
            }
        }

        #endregion

        #region Export

        /// <summary>
        /// Exports scan results to JSON.
        /// </summary>
        public string ExportResultsToJson(IOCScanResult result)
        {
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Exports scan results to CSV.
        /// </summary>
        public string ExportResultsToCsv(IOCScanResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Type,Value,Location,Context,Confidence,ThreatName,Severity,Source");

            foreach (var match in result.Matches)
            {
                sb.AppendLine($"\"{match.IOC.Type}\",\"{match.IOC.Value}\",\"{match.MatchLocation}\",\"{match.MatchContext}\",{match.Confidence:F2},\"{match.IOC.ThreatName}\",\"{match.IOC.Severity}\",\"{match.IOC.Source}\"");
            }

            return sb.ToString();
        }

        #endregion
    }
}
