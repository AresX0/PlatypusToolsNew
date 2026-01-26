using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Forensics
{
    /// <summary>
    /// Service for parsing browser artifacts (Chrome, Firefox, Edge).
    /// Extracts history, cookies, bookmarks, downloads, and other forensic data.
    /// </summary>
    public class BrowserForensicsService
    {
        private static readonly Lazy<BrowserForensicsService> _instance = new(() => new BrowserForensicsService());
        public static BrowserForensicsService Instance => _instance.Value;

        public event EventHandler<BrowserForensicsProgress>? ProgressChanged;
        public event EventHandler<BrowserArtifact>? ArtifactFound;

        #region Models

        public enum BrowserType
        {
            Chrome,
            Firefox,
            Edge,
            Brave,
            Opera,
            Vivaldi,
            Unknown
        }

        public enum ArtifactType
        {
            History,
            Bookmark,
            Cookie,
            Download,
            Form,
            Login,
            Extension,
            Session,
            Cache,
            Preference
        }

        public class BrowserProfile
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public BrowserType Browser { get; set; }
            public string ProfileName { get; set; } = string.Empty;
            public string ProfilePath { get; set; } = string.Empty;
            public bool IsDefault { get; set; }
            public DateTime? LastUsed { get; set; }
        }

        public class BrowserArtifact
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public BrowserType Browser { get; set; }
            public string ProfileName { get; set; } = string.Empty;
            public ArtifactType Type { get; set; }
            public DateTime Timestamp { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public int VisitCount { get; set; }
            public Dictionary<string, string> Metadata { get; set; } = new();
        }

        public class HistoryEntry : BrowserArtifact
        {
            public DateTime? LastVisit { get; set; }
            public TimeSpan? VisitDuration { get; set; }
            public string TransitionType { get; set; } = string.Empty;
        }

        public class CookieEntry : BrowserArtifact
        {
            public string Name { get; set; } = string.Empty;
            public string Domain { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public DateTime? Expires { get; set; }
            public bool IsSecure { get; set; }
            public bool IsHttpOnly { get; set; }
            public bool IsPersistent { get; set; }
        }

        public class DownloadEntry : BrowserArtifact
        {
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public long FileSize { get; set; }
            public string MimeType { get; set; } = string.Empty;
            public DateTime? StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string State { get; set; } = string.Empty;
            public string ReferrerUrl { get; set; } = string.Empty;
        }

        public class LoginEntry : BrowserArtifact
        {
            public string Username { get; set; } = string.Empty;
            public bool HasPassword { get; set; }
            public DateTime? DateCreated { get; set; }
            public DateTime? DateLastUsed { get; set; }
            public int TimesUsed { get; set; }
        }

        public class BookmarkEntry : BrowserArtifact
        {
            public string ParentFolder { get; set; } = string.Empty;
            public DateTime? DateAdded { get; set; }
            public DateTime? DateModified { get; set; }
            public int SortIndex { get; set; }
        }

        public class BrowserForensicsProgress
        {
            public BrowserType Browser { get; set; }
            public string ProfileName { get; set; } = string.Empty;
            public ArtifactType CurrentArtifactType { get; set; }
            public int ArtifactsFound { get; set; }
            public int ProfilesProcessed { get; set; }
            public int TotalProfiles { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        public class BrowserForensicsResult
        {
            public List<BrowserProfile> Profiles { get; set; } = new();
            public List<HistoryEntry> History { get; set; } = new();
            public List<CookieEntry> Cookies { get; set; } = new();
            public List<DownloadEntry> Downloads { get; set; } = new();
            public List<LoginEntry> Logins { get; set; } = new();
            public List<BookmarkEntry> Bookmarks { get; set; } = new();
            public List<BrowserArtifact> Extensions { get; set; } = new();
            public List<string> Errors { get; set; } = new();
            public TimeSpan Duration { get; set; }
            public Dictionary<BrowserType, int> ArtifactCounts { get; set; } = new();
        }

        #endregion

        #region Profile Discovery

        /// <summary>
        /// Discovers all browser profiles on the system.
        /// </summary>
        public async Task<List<BrowserProfile>> DiscoverProfilesAsync(CancellationToken cancellationToken = default)
        {
            var profiles = new List<BrowserProfile>();

            var tasks = new List<Task<List<BrowserProfile>>>
            {
                Task.Run(() => DiscoverChromeProfiles(), cancellationToken),
                Task.Run(() => DiscoverFirefoxProfiles(), cancellationToken),
                Task.Run(() => DiscoverEdgeProfiles(), cancellationToken),
                Task.Run(() => DiscoverBraveProfiles(), cancellationToken)
            };

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                profiles.AddRange(result);
            }

            return profiles;
        }

        private List<BrowserProfile> DiscoverChromeProfiles()
        {
            var profiles = new List<BrowserProfile>();
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data");

            if (!Directory.Exists(basePath))
                return profiles;

            // Default profile
            var defaultPath = Path.Combine(basePath, "Default");
            if (Directory.Exists(defaultPath))
            {
                profiles.Add(new BrowserProfile
                {
                    Browser = BrowserType.Chrome,
                    ProfileName = "Default",
                    ProfilePath = defaultPath,
                    IsDefault = true
                });
            }

            // Additional profiles (Profile 1, Profile 2, etc.)
            foreach (var dir in Directory.GetDirectories(basePath, "Profile *"))
            {
                var name = Path.GetFileName(dir);
                profiles.Add(new BrowserProfile
                {
                    Browser = BrowserType.Chrome,
                    ProfileName = name,
                    ProfilePath = dir
                });
            }

            return profiles;
        }

        private List<BrowserProfile> DiscoverFirefoxProfiles()
        {
            var profiles = new List<BrowserProfile>();
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");

            if (!Directory.Exists(basePath))
                return profiles;

            foreach (var dir in Directory.GetDirectories(basePath))
            {
                var name = Path.GetFileName(dir);
                var isDefault = name.Contains(".default");
                profiles.Add(new BrowserProfile
                {
                    Browser = BrowserType.Firefox,
                    ProfileName = name,
                    ProfilePath = dir,
                    IsDefault = isDefault
                });
            }

            return profiles;
        }

        private List<BrowserProfile> DiscoverEdgeProfiles()
        {
            var profiles = new List<BrowserProfile>();
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data");

            if (!Directory.Exists(basePath))
                return profiles;

            var defaultPath = Path.Combine(basePath, "Default");
            if (Directory.Exists(defaultPath))
            {
                profiles.Add(new BrowserProfile
                {
                    Browser = BrowserType.Edge,
                    ProfileName = "Default",
                    ProfilePath = defaultPath,
                    IsDefault = true
                });
            }

            foreach (var dir in Directory.GetDirectories(basePath, "Profile *"))
            {
                var name = Path.GetFileName(dir);
                profiles.Add(new BrowserProfile
                {
                    Browser = BrowserType.Edge,
                    ProfileName = name,
                    ProfilePath = dir
                });
            }

            return profiles;
        }

        private List<BrowserProfile> DiscoverBraveProfiles()
        {
            var profiles = new List<BrowserProfile>();
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BraveSoftware", "Brave-Browser", "User Data");

            if (!Directory.Exists(basePath))
                return profiles;

            var defaultPath = Path.Combine(basePath, "Default");
            if (Directory.Exists(defaultPath))
            {
                profiles.Add(new BrowserProfile
                {
                    Browser = BrowserType.Brave,
                    ProfileName = "Default",
                    ProfilePath = defaultPath,
                    IsDefault = true
                });
            }

            return profiles;
        }

        #endregion

        #region Full Analysis

        /// <summary>
        /// Performs full forensic analysis of all browsers.
        /// </summary>
        public async Task<BrowserForensicsResult> AnalyzeAllBrowsersAsync(CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var result = new BrowserForensicsResult();

            var profiles = await DiscoverProfilesAsync(cancellationToken);
            result.Profiles = profiles;

            var progress = new BrowserForensicsProgress
            {
                TotalProfiles = profiles.Count
            };

            foreach (var profile in profiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                progress.ProfileName = profile.ProfileName;
                progress.Browser = profile.Browser;
                progress.ProfilesProcessed++;
                ProgressChanged?.Invoke(this, progress);

                try
                {
                    // Parse history
                    progress.CurrentArtifactType = ArtifactType.History;
                    progress.Status = "Parsing history...";
                    ProgressChanged?.Invoke(this, progress);
                    var history = await ParseHistoryAsync(profile, cancellationToken);
                    result.History.AddRange(history);
                    progress.ArtifactsFound += history.Count;

                    // Parse bookmarks
                    progress.CurrentArtifactType = ArtifactType.Bookmark;
                    progress.Status = "Parsing bookmarks...";
                    ProgressChanged?.Invoke(this, progress);
                    var bookmarks = await ParseBookmarksAsync(profile, cancellationToken);
                    result.Bookmarks.AddRange(bookmarks);
                    progress.ArtifactsFound += bookmarks.Count;

                    // Parse downloads
                    progress.CurrentArtifactType = ArtifactType.Download;
                    progress.Status = "Parsing downloads...";
                    ProgressChanged?.Invoke(this, progress);
                    var downloads = await ParseDownloadsAsync(profile, cancellationToken);
                    result.Downloads.AddRange(downloads);
                    progress.ArtifactsFound += downloads.Count;

                    // Parse logins (metadata only, not actual passwords)
                    progress.CurrentArtifactType = ArtifactType.Login;
                    progress.Status = "Parsing login data...";
                    ProgressChanged?.Invoke(this, progress);
                    var logins = await ParseLoginsAsync(profile, cancellationToken);
                    result.Logins.AddRange(logins);
                    progress.ArtifactsFound += logins.Count;

                    // Track counts
                    if (!result.ArtifactCounts.ContainsKey(profile.Browser))
                        result.ArtifactCounts[profile.Browser] = 0;
                    result.ArtifactCounts[profile.Browser] += history.Count + bookmarks.Count + downloads.Count + logins.Count;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{profile.Browser}/{profile.ProfileName}: {ex.Message}");
                }
            }

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }

        #endregion

        #region History Parsing

        /// <summary>
        /// Parses browser history for a profile.
        /// </summary>
        public async Task<List<HistoryEntry>> ParseHistoryAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
        {
            var entries = new List<HistoryEntry>();

            return profile.Browser switch
            {
                BrowserType.Chrome or BrowserType.Edge or BrowserType.Brave => await ParseChromiumHistoryAsync(profile, cancellationToken),
                BrowserType.Firefox => await ParseFirefoxHistoryAsync(profile, cancellationToken),
                _ => entries
            };
        }

        private async Task<List<HistoryEntry>> ParseChromiumHistoryAsync(BrowserProfile profile, CancellationToken cancellationToken)
        {
            var entries = new List<HistoryEntry>();
            var historyPath = Path.Combine(profile.ProfilePath, "History");

            if (!File.Exists(historyPath))
                return entries;

            try
            {
                // Copy database to temp (it may be locked by browser)
                var tempPath = Path.Combine(Path.GetTempPath(), $"history_{Guid.NewGuid()}.db");
                File.Copy(historyPath, tempPath, true);

                try
                {
                    // In production, use Microsoft.Data.Sqlite or System.Data.SQLite
                    // This is a simplified version that reads raw bytes for demonstration
                    var content = await File.ReadAllTextAsync(tempPath, cancellationToken);

                    // Extract URLs using regex (simplified)
                    var urlPattern = new System.Text.RegularExpressions.Regex(@"https?://[^\s\x00-\x1F]+");
                    var matches = urlPattern.Matches(content);

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var entry = new HistoryEntry
                        {
                            Browser = profile.Browser,
                            ProfileName = profile.ProfileName,
                            Type = ArtifactType.History,
                            Url = match.Value.TrimEnd('\0', ' ', '\r', '\n'),
                            Timestamp = DateTime.UtcNow // Would be parsed from DB
                        };

                        if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
                        {
                            entry.Title = uri.Host;
                            entries.Add(entry);
                            ArtifactFound?.Invoke(this, entry);
                        }
                    }
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"History parse error: {ex.Message}");
            }

            return entries.DistinctBy(e => e.Url).Take(1000).ToList();
        }

        private async Task<List<HistoryEntry>> ParseFirefoxHistoryAsync(BrowserProfile profile, CancellationToken cancellationToken)
        {
            var entries = new List<HistoryEntry>();
            var placesPath = Path.Combine(profile.ProfilePath, "places.sqlite");

            if (!File.Exists(placesPath))
                return entries;

            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"places_{Guid.NewGuid()}.db");
                File.Copy(placesPath, tempPath, true);

                try
                {
                    var content = await File.ReadAllTextAsync(tempPath, cancellationToken);
                    var urlPattern = new System.Text.RegularExpressions.Regex(@"https?://[^\s\x00-\x1F]+");
                    var matches = urlPattern.Matches(content);

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var entry = new HistoryEntry
                        {
                            Browser = profile.Browser,
                            ProfileName = profile.ProfileName,
                            Type = ArtifactType.History,
                            Url = match.Value.TrimEnd('\0', ' '),
                            Timestamp = DateTime.UtcNow
                        };

                        if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
                        {
                            entry.Title = uri.Host;
                            entries.Add(entry);
                            ArtifactFound?.Invoke(this, entry);
                        }
                    }
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
            catch { }

            return entries.DistinctBy(e => e.Url).Take(1000).ToList();
        }

        #endregion

        #region Bookmarks Parsing

        /// <summary>
        /// Parses bookmarks for a profile.
        /// </summary>
        public async Task<List<BookmarkEntry>> ParseBookmarksAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
        {
            return profile.Browser switch
            {
                BrowserType.Chrome or BrowserType.Edge or BrowserType.Brave => await ParseChromiumBookmarksAsync(profile, cancellationToken),
                BrowserType.Firefox => await ParseFirefoxBookmarksAsync(profile, cancellationToken),
                _ => new List<BookmarkEntry>()
            };
        }

        private async Task<List<BookmarkEntry>> ParseChromiumBookmarksAsync(BrowserProfile profile, CancellationToken cancellationToken)
        {
            var entries = new List<BookmarkEntry>();
            var bookmarksPath = Path.Combine(profile.ProfilePath, "Bookmarks");

            if (!File.Exists(bookmarksPath))
                return entries;

            try
            {
                var json = await File.ReadAllTextAsync(bookmarksPath, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("roots", out var roots))
                {
                    foreach (var folder in new[] { "bookmark_bar", "other", "synced" })
                    {
                        if (roots.TryGetProperty(folder, out var folderElement))
                        {
                            ParseBookmarkFolder(folderElement, folder, profile, entries);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bookmarks parse error: {ex.Message}");
            }

            return entries;
        }

        private void ParseBookmarkFolder(JsonElement element, string parentFolder, BrowserProfile profile, List<BookmarkEntry> entries)
        {
            if (element.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    if (child.TryGetProperty("type", out var typeElement))
                    {
                        var type = typeElement.GetString();

                        if (type == "url")
                        {
                            var entry = new BookmarkEntry
                            {
                                Browser = profile.Browser,
                                ProfileName = profile.ProfileName,
                                Type = ArtifactType.Bookmark,
                                ParentFolder = parentFolder,
                                Title = child.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                                Url = child.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                                Timestamp = DateTime.UtcNow
                            };

                            if (child.TryGetProperty("date_added", out var dateAdded))
                            {
                                if (long.TryParse(dateAdded.GetString(), out var ticks))
                                {
                                    // Chrome uses microseconds since 1601-01-01
                                    entry.DateAdded = new DateTime(1601, 1, 1).AddMicroseconds(ticks);
                                }
                            }

                            entries.Add(entry);
                            ArtifactFound?.Invoke(this, entry);
                        }
                        else if (type == "folder")
                        {
                            var folderName = child.TryGetProperty("name", out var fn) ? fn.GetString() ?? parentFolder : parentFolder;
                            ParseBookmarkFolder(child, $"{parentFolder}/{folderName}", profile, entries);
                        }
                    }
                }
            }
        }

        private async Task<List<BookmarkEntry>> ParseFirefoxBookmarksAsync(BrowserProfile profile, CancellationToken cancellationToken)
        {
            // Firefox bookmarks are in places.sqlite (moz_bookmarks table)
            // For simplicity, returning empty - full impl would use SQLite
            await Task.CompletedTask;
            return new List<BookmarkEntry>();
        }

        #endregion

        #region Downloads Parsing

        /// <summary>
        /// Parses download history for a profile.
        /// </summary>
        public async Task<List<DownloadEntry>> ParseDownloadsAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
        {
            var entries = new List<DownloadEntry>();

            // Chromium browsers store downloads in History database
            // Firefox stores in places.sqlite
            // This is a simplified implementation

            await Task.CompletedTask;
            return entries;
        }

        #endregion

        #region Logins Parsing

        /// <summary>
        /// Parses saved login entries (metadata only, not passwords).
        /// </summary>
        public async Task<List<LoginEntry>> ParseLoginsAsync(BrowserProfile profile, CancellationToken cancellationToken = default)
        {
            var entries = new List<LoginEntry>();

            var loginDataPath = profile.Browser switch
            {
                BrowserType.Chrome or BrowserType.Edge or BrowserType.Brave =>
                    Path.Combine(profile.ProfilePath, "Login Data"),
                BrowserType.Firefox =>
                    Path.Combine(profile.ProfilePath, "logins.json"),
                _ => null
            };

            if (loginDataPath == null || !File.Exists(loginDataPath))
                return entries;

            if (profile.Browser == BrowserType.Firefox)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(loginDataPath, cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("logins", out var logins))
                    {
                        foreach (var login in logins.EnumerateArray())
                        {
                            var entry = new LoginEntry
                            {
                                Browser = profile.Browser,
                                ProfileName = profile.ProfileName,
                                Type = ArtifactType.Login,
                                Url = login.TryGetProperty("hostname", out var host) ? host.GetString() ?? "" : "",
                                Username = login.TryGetProperty("username", out var user) ? user.GetString() ?? "" : "",
                                HasPassword = true,
                                Timestamp = DateTime.UtcNow
                            };

                            entries.Add(entry);
                            ArtifactFound?.Invoke(this, entry);
                        }
                    }
                }
                catch { }
            }

            return entries;
        }

        #endregion

        #region Export

        /// <summary>
        /// Exports forensic results to various formats.
        /// </summary>
        public async Task ExportResultsAsync(BrowserForensicsResult result, string outputPath, string format, CancellationToken cancellationToken = default)
        {
            switch (format.ToLower())
            {
                case "json":
                    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(outputPath, json, cancellationToken);
                    break;

                case "csv":
                    await ExportToCsvAsync(result, outputPath, cancellationToken);
                    break;

                case "html":
                    await ExportToHtmlAsync(result, outputPath, cancellationToken);
                    break;
            }
        }

        private async Task ExportToCsvAsync(BrowserForensicsResult result, string outputPath, CancellationToken cancellationToken)
        {
            var lines = new List<string> { "Browser,Profile,Type,Timestamp,Title,URL,Value" };

            foreach (var h in result.History)
                lines.Add($"{h.Browser},{h.ProfileName},History,{h.Timestamp:O},\"{h.Title}\",\"{h.Url}\",");

            foreach (var b in result.Bookmarks)
                lines.Add($"{b.Browser},{b.ProfileName},Bookmark,{b.Timestamp:O},\"{b.Title}\",\"{b.Url}\",\"{b.ParentFolder}\"");

            foreach (var l in result.Logins)
                lines.Add($"{l.Browser},{l.ProfileName},Login,{l.Timestamp:O},,\"{l.Url}\",\"{l.Username}\"");

            await File.WriteAllLinesAsync(outputPath, lines, cancellationToken);
        }

        private async Task ExportToHtmlAsync(BrowserForensicsResult result, string outputPath, CancellationToken cancellationToken)
        {
            var html = $@"<!DOCTYPE html>
<html><head><title>Browser Forensics Report</title>
<style>body{{font-family:sans-serif;margin:20px}}table{{border-collapse:collapse;width:100%}}th,td{{border:1px solid #ddd;padding:8px}}th{{background:#4CAF50;color:white}}.section{{margin:20px 0}}</style></head>
<body>
<h1>Browser Forensics Report</h1>
<p>Generated: {DateTime.Now:g} | Duration: {result.Duration.TotalSeconds:F1}s</p>
<p>Profiles: {result.Profiles.Count} | History: {result.History.Count} | Bookmarks: {result.Bookmarks.Count} | Logins: {result.Logins.Count}</p>

<h2>History (Top 100)</h2>
<table><tr><th>Browser</th><th>Profile</th><th>Title</th><th>URL</th><th>Timestamp</th></tr>
{string.Join("\n", result.History.Take(100).Select(h => $"<tr><td>{h.Browser}</td><td>{h.ProfileName}</td><td>{System.Web.HttpUtility.HtmlEncode(h.Title)}</td><td>{System.Web.HttpUtility.HtmlEncode(h.Url)}</td><td>{h.Timestamp:g}</td></tr>"))}
</table>

<h2>Bookmarks (Top 100)</h2>
<table><tr><th>Browser</th><th>Profile</th><th>Title</th><th>URL</th><th>Folder</th></tr>
{string.Join("\n", result.Bookmarks.Take(100).Select(b => $"<tr><td>{b.Browser}</td><td>{b.ProfileName}</td><td>{System.Web.HttpUtility.HtmlEncode(b.Title)}</td><td>{System.Web.HttpUtility.HtmlEncode(b.Url)}</td><td>{b.ParentFolder}</td></tr>"))}
</table>

<h2>Saved Logins</h2>
<table><tr><th>Browser</th><th>Profile</th><th>URL</th><th>Username</th></tr>
{string.Join("\n", result.Logins.Select(l => $"<tr><td>{l.Browser}</td><td>{l.ProfileName}</td><td>{System.Web.HttpUtility.HtmlEncode(l.Url)}</td><td>{System.Web.HttpUtility.HtmlEncode(l.Username)}</td></tr>"))}
</table>
</body></html>";

            await File.WriteAllTextAsync(outputPath, html, cancellationToken);
        }

        #endregion
    }
}
