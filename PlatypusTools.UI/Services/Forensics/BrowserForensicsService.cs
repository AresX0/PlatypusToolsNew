using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    #region Models

    /// <summary>
    /// Browser history entry.
    /// </summary>
    public class BrowserHistoryEntry
    {
        public string Browser { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime VisitTime { get; set; }
        public int VisitCount { get; set; }
        public string? Referrer { get; set; }
    }

    /// <summary>
    /// Browser cookie.
    /// </summary>
    public class BrowserCookie
    {
        public string Browser { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
        public DateTime ExpiryTime { get; set; }
        public DateTime LastAccessTime { get; set; }
        public bool IsSecure { get; set; }
        public bool IsHttpOnly { get; set; }
        public bool IsPersistent { get; set; }
    }

    /// <summary>
    /// Browser download entry.
    /// </summary>
    public class BrowserDownload
    {
        public string Browser { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DownloadState State { get; set; }
        public string? Referrer { get; set; }
    }

    public enum DownloadState
    {
        InProgress,
        Complete,
        Cancelled,
        Interrupted,
        Unknown
    }

    /// <summary>
    /// Browser login/credential entry.
    /// </summary>
    public class BrowserCredential
    {
        public string Browser { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; }
        public DateTime? DateLastUsed { get; set; }
        public int TimesUsed { get; set; }
        // Note: Password is encrypted with DPAPI, not included for security
    }

    /// <summary>
    /// Browser bookmark entry.
    /// </summary>
    public class BrowserBookmark
    {
        public string Browser { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; }
    }

    /// <summary>
    /// Complete browser forensics result.
    /// </summary>
    public class BrowserForensicsResult
    {
        public List<BrowserHistoryEntry> History { get; } = new();
        public List<BrowserCookie> Cookies { get; } = new();
        public List<BrowserDownload> Downloads { get; } = new();
        public List<BrowserCredential> Credentials { get; } = new();
        public List<BrowserBookmark> Bookmarks { get; } = new();
        public List<string> Errors { get; } = new();

        public int TotalArtifacts => History.Count + Cookies.Count + Downloads.Count + Credentials.Count + Bookmarks.Count;
    }

    #endregion

    /// <summary>
    /// Browser forensics service for extracting artifacts from Chrome, Edge, and Firefox.
    /// Parses history, cookies, downloads, bookmarks, and login data.
    /// </summary>
    public class BrowserForensicsService : ForensicOperationBase
    {
        private readonly string _localAppData;
        private readonly string _appData;
        private readonly string _userProfile;

        public override string OperationName => "Browser Forensics";

        // Browser profile paths
        private readonly Dictionary<string, string[]> _browserPaths = new();

        // Options
        public bool ExtractHistory { get; set; } = true;
        public bool ExtractCookies { get; set; } = true;
        public bool ExtractDownloads { get; set; } = true;
        public bool ExtractCredentials { get; set; } = true;
        public bool ExtractBookmarks { get; set; } = true;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public BrowserForensicsService()
        {
            _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            InitializeBrowserPaths();
        }

        private void InitializeBrowserPaths()
        {
            _browserPaths["Chrome"] = new[]
            {
                Path.Combine(_localAppData, "Google", "Chrome", "User Data")
            };

            _browserPaths["Edge"] = new[]
            {
                Path.Combine(_localAppData, "Microsoft", "Edge", "User Data")
            };

            _browserPaths["Firefox"] = new[]
            {
                Path.Combine(_appData, "Mozilla", "Firefox", "Profiles")
            };

            _browserPaths["Brave"] = new[]
            {
                Path.Combine(_localAppData, "BraveSoftware", "Brave-Browser", "User Data")
            };

            _browserPaths["Vivaldi"] = new[]
            {
                Path.Combine(_localAppData, "Vivaldi", "User Data")
            };

            _browserPaths["Opera"] = new[]
            {
                Path.Combine(_appData, "Opera Software", "Opera Stable")
            };
        }

        /// <summary>
        /// Performs a complete browser forensics extraction.
        /// </summary>
        public async Task<BrowserForensicsResult> ExtractArtifactsAsync(CancellationToken cancellationToken = default)
        {
            var result = new BrowserForensicsResult();

            LogHeader("Starting Browser Forensics Extraction");
            Log($"History: {ExtractHistory}, Cookies: {ExtractCookies}, Downloads: {ExtractDownloads}");

            foreach (var (browser, paths) in _browserPaths)
            {
                foreach (var basePath in paths)
                {
                    if (!Directory.Exists(basePath)) continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        Log($"Processing {browser}: {basePath}");

                        if (browser == "Firefox")
                        {
                            await ExtractFirefoxArtifactsAsync(browser, basePath, result, cancellationToken);
                        }
                        else
                        {
                            await ExtractChromiumArtifactsAsync(browser, basePath, result, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{browser}: {ex.Message}");
                        LogError($"Error processing {browser}: {ex.Message}");
                    }
                }
            }

            LogSuccess($"Extraction complete: {result.TotalArtifacts} artifacts found");
            return result;
        }

        #region Chromium-based Browsers (Chrome, Edge, Brave, etc.)

        private async Task ExtractChromiumArtifactsAsync(string browser, string basePath, BrowserForensicsResult result, CancellationToken token)
        {
            var profiles = GetChromiumProfiles(basePath);

            foreach (var profile in profiles)
            {
                token.ThrowIfCancellationRequested();
                var profileName = Path.GetFileName(profile);

                if (ExtractHistory)
                {
                    await ExtractChromiumHistoryAsync(browser, profileName, profile, result, token);
                }

                if (ExtractCookies)
                {
                    await ExtractChromiumCookiesAsync(browser, profileName, profile, result, token);
                }

                if (ExtractDownloads)
                {
                    await ExtractChromiumDownloadsAsync(browser, profileName, profile, result, token);
                }

                if (ExtractCredentials)
                {
                    await ExtractChromiumLoginsAsync(browser, profileName, profile, result, token);
                }

                if (ExtractBookmarks)
                {
                    ExtractChromiumBookmarks(browser, profileName, profile, result);
                }
            }
        }

        private IEnumerable<string> GetChromiumProfiles(string basePath)
        {
            var profiles = new List<string>();

            // Default profile
            var defaultProfile = Path.Combine(basePath, "Default");
            if (Directory.Exists(defaultProfile))
                profiles.Add(defaultProfile);

            // Numbered profiles (Profile 1, Profile 2, etc.)
            var profileDirs = Directory.GetDirectories(basePath, "Profile *");
            profiles.AddRange(profileDirs);

            return profiles;
        }

        private async Task ExtractChromiumHistoryAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var historyDb = Path.Combine(profilePath, "History");
            if (!File.Exists(historyDb)) return;

            var tempDb = await CopyToTempAsync(historyDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT url, title, visit_count, last_visit_time
                    FROM urls
                    ORDER BY last_visit_time DESC
                    LIMIT 10000";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    var visitTime = ChromiumTimeToDateTime(reader.GetInt64(3));
                    
                    if (StartDate.HasValue && visitTime < StartDate.Value) continue;
                    if (EndDate.HasValue && visitTime > EndDate.Value) continue;

                    result.History.Add(new BrowserHistoryEntry
                    {
                        Browser = browser,
                        Profile = profile,
                        Url = reader.GetString(0),
                        Title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        VisitCount = reader.GetInt32(2),
                        VisitTime = visitTime
                    });
                }

                Log($"  {browser}/{profile}: {result.History.Count(h => h.Profile == profile)} history entries");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        private async Task ExtractChromiumCookiesAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var cookiesDb = Path.Combine(profilePath, "Network", "Cookies");
            if (!File.Exists(cookiesDb))
            {
                cookiesDb = Path.Combine(profilePath, "Cookies");
                if (!File.Exists(cookiesDb)) return;
            }

            var tempDb = await CopyToTempAsync(cookiesDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT host_key, name, path, creation_utc, expires_utc, last_access_utc, 
                           is_secure, is_httponly, is_persistent
                    FROM cookies
                    LIMIT 50000";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    result.Cookies.Add(new BrowserCookie
                    {
                        Browser = browser,
                        Profile = profile,
                        Host = reader.GetString(0),
                        Name = reader.GetString(1),
                        Path = reader.GetString(2),
                        CreationTime = ChromiumTimeToDateTime(reader.GetInt64(3)),
                        ExpiryTime = ChromiumTimeToDateTime(reader.GetInt64(4)),
                        LastAccessTime = ChromiumTimeToDateTime(reader.GetInt64(5)),
                        IsSecure = reader.GetInt32(6) == 1,
                        IsHttpOnly = reader.GetInt32(7) == 1,
                        IsPersistent = reader.GetInt32(8) == 1
                    });
                }

                Log($"  {browser}/{profile}: {result.Cookies.Count(c => c.Profile == profile)} cookies");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        private async Task ExtractChromiumDownloadsAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var historyDb = Path.Combine(profilePath, "History");
            if (!File.Exists(historyDb)) return;

            var tempDb = await CopyToTempAsync(historyDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT tab_url, target_path, mime_type, total_bytes, start_time, end_time, state, referrer
                    FROM downloads
                    ORDER BY start_time DESC
                    LIMIT 5000";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    var startTime = ChromiumTimeToDateTime(reader.GetInt64(4));
                    
                    if (StartDate.HasValue && startTime < StartDate.Value) continue;
                    if (EndDate.HasValue && startTime > EndDate.Value) continue;

                    result.Downloads.Add(new BrowserDownload
                    {
                        Browser = browser,
                        Profile = profile,
                        Url = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        TargetPath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        MimeType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        TotalBytes = reader.GetInt64(3),
                        StartTime = startTime,
                        EndTime = reader.IsDBNull(5) ? null : ChromiumTimeToDateTime(reader.GetInt64(5)),
                        State = (DownloadState)reader.GetInt32(6),
                        Referrer = reader.IsDBNull(7) ? null : reader.GetString(7)
                    });
                }

                Log($"  {browser}/{profile}: {result.Downloads.Count(d => d.Profile == profile)} downloads");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        private async Task ExtractChromiumLoginsAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var loginDb = Path.Combine(profilePath, "Login Data");
            if (!File.Exists(loginDb)) return;

            var tempDb = await CopyToTempAsync(loginDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT origin_url, username_value, date_created, date_last_used, times_used
                    FROM logins
                    WHERE username_value != ''
                    ORDER BY times_used DESC";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    result.Credentials.Add(new BrowserCredential
                    {
                        Browser = browser,
                        Profile = profile,
                        Url = reader.GetString(0),
                        Username = reader.GetString(1),
                        DateCreated = ChromiumTimeToDateTime(reader.GetInt64(2)),
                        DateLastUsed = reader.IsDBNull(3) ? null : ChromiumTimeToDateTime(reader.GetInt64(3)),
                        TimesUsed = reader.GetInt32(4)
                    });
                }

                Log($"  {browser}/{profile}: {result.Credentials.Count(c => c.Profile == profile)} saved logins");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        private void ExtractChromiumBookmarks(string browser, string profile, string profilePath, BrowserForensicsResult result)
        {
            var bookmarksFile = Path.Combine(profilePath, "Bookmarks");
            if (!File.Exists(bookmarksFile)) return;

            try
            {
                var json = File.ReadAllText(bookmarksFile);
                using var doc = JsonDocument.Parse(json);

                var roots = doc.RootElement.GetProperty("roots");
                foreach (var root in roots.EnumerateObject())
                {
                    if (root.Value.ValueKind == JsonValueKind.Object)
                    {
                        ExtractBookmarksRecursive(browser, profile, root.Name, root.Value, result);
                    }
                }

                Log($"  {browser}/{profile}: {result.Bookmarks.Count(b => b.Profile == profile)} bookmarks");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Bookmarks ({browser}/{profile}): {ex.Message}");
            }
        }

        private void ExtractBookmarksRecursive(string browser, string profile, string folder, JsonElement element, BrowserForensicsResult result)
        {
            if (element.TryGetProperty("type", out var typeElement))
            {
                var type = typeElement.GetString();
                if (type == "url")
                {
                    result.Bookmarks.Add(new BrowserBookmark
                    {
                        Browser = browser,
                        Profile = profile,
                        Folder = folder,
                        Url = element.GetProperty("url").GetString() ?? string.Empty,
                        Title = element.GetProperty("name").GetString() ?? string.Empty,
                        DateAdded = element.TryGetProperty("date_added", out var dateAdded) 
                            ? ChromiumTimeToDateTime(long.Parse(dateAdded.GetString() ?? "0"))
                            : DateTime.MinValue
                    });
                }
                else if (type == "folder" && element.TryGetProperty("children", out var children))
                {
                    var folderName = element.TryGetProperty("name", out var name) ? name.GetString() ?? folder : folder;
                    foreach (var child in children.EnumerateArray())
                    {
                        ExtractBookmarksRecursive(browser, profile, $"{folder}/{folderName}", child, result);
                    }
                }
            }
        }

        #endregion

        #region Firefox

        private async Task ExtractFirefoxArtifactsAsync(string browser, string basePath, BrowserForensicsResult result, CancellationToken token)
        {
            var profiles = Directory.GetDirectories(basePath);

            foreach (var profile in profiles)
            {
                token.ThrowIfCancellationRequested();
                var profileName = Path.GetFileName(profile);

                if (ExtractHistory)
                {
                    await ExtractFirefoxHistoryAsync(browser, profileName, profile, result, token);
                }

                if (ExtractCookies)
                {
                    await ExtractFirefoxCookiesAsync(browser, profileName, profile, result, token);
                }

                if (ExtractDownloads)
                {
                    await ExtractFirefoxDownloadsAsync(browser, profileName, profile, result, token);
                }

                if (ExtractCredentials)
                {
                    await ExtractFirefoxLoginsAsync(browser, profileName, profile, result, token);
                }

                if (ExtractBookmarks)
                {
                    await ExtractFirefoxBookmarksAsync(browser, profileName, profile, result, token);
                }
            }
        }

        private async Task ExtractFirefoxHistoryAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var placesDb = Path.Combine(profilePath, "places.sqlite");
            if (!File.Exists(placesDb)) return;

            var tempDb = await CopyToTempAsync(placesDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT p.url, p.title, p.visit_count, h.visit_date
                    FROM moz_places p
                    JOIN moz_historyvisits h ON p.id = h.place_id
                    ORDER BY h.visit_date DESC
                    LIMIT 10000";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    var visitTime = FirefoxTimeToDateTime(reader.GetInt64(3));
                    
                    if (StartDate.HasValue && visitTime < StartDate.Value) continue;
                    if (EndDate.HasValue && visitTime > EndDate.Value) continue;

                    result.History.Add(new BrowserHistoryEntry
                    {
                        Browser = browser,
                        Profile = profile,
                        Url = reader.GetString(0),
                        Title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        VisitCount = reader.GetInt32(2),
                        VisitTime = visitTime
                    });
                }

                Log($"  {browser}/{profile}: {result.History.Count(h => h.Profile == profile)} history entries");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        private async Task ExtractFirefoxCookiesAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var cookiesDb = Path.Combine(profilePath, "cookies.sqlite");
            if (!File.Exists(cookiesDb)) return;

            var tempDb = await CopyToTempAsync(cookiesDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT host, name, path, creationTime, expiry, lastAccessed, isSecure, isHttpOnly
                    FROM moz_cookies
                    LIMIT 50000";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    result.Cookies.Add(new BrowserCookie
                    {
                        Browser = browser,
                        Profile = profile,
                        Host = reader.GetString(0),
                        Name = reader.GetString(1),
                        Path = reader.GetString(2),
                        CreationTime = FirefoxTimeToDateTime(reader.GetInt64(3)),
                        ExpiryTime = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)).DateTime,
                        LastAccessTime = FirefoxTimeToDateTime(reader.GetInt64(5)),
                        IsSecure = reader.GetInt32(6) == 1,
                        IsHttpOnly = reader.GetInt32(7) == 1
                    });
                }

                Log($"  {browser}/{profile}: {result.Cookies.Count(c => c.Profile == profile)} cookies");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        private async Task ExtractFirefoxDownloadsAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var placesDb = Path.Combine(profilePath, "places.sqlite");
            if (!File.Exists(placesDb)) return;

            var tempDb = await CopyToTempAsync(placesDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT a.content, p.url, a.dateAdded
                    FROM moz_annos a
                    JOIN moz_places p ON a.place_id = p.id
                    WHERE a.anno_attribute_id IN (SELECT id FROM moz_anno_attributes WHERE name = 'downloads/destinationFileURI')
                    ORDER BY a.dateAdded DESC
                    LIMIT 5000";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    var downloadTime = FirefoxTimeToDateTime(reader.GetInt64(2));
                    
                    if (StartDate.HasValue && downloadTime < StartDate.Value) continue;
                    if (EndDate.HasValue && downloadTime > EndDate.Value) continue;

                    result.Downloads.Add(new BrowserDownload
                    {
                        Browser = browser,
                        Profile = profile,
                        TargetPath = reader.GetString(0),
                        Url = reader.GetString(1),
                        StartTime = downloadTime,
                        State = DownloadState.Complete
                    });
                }

                Log($"  {browser}/{profile}: {result.Downloads.Count(d => d.Profile == profile)} downloads");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        private async Task ExtractFirefoxLoginsAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var loginsFile = Path.Combine(profilePath, "logins.json");
            if (!File.Exists(loginsFile)) return;

            await Task.Run(() =>
            {
                try
                {
                    var json = File.ReadAllText(loginsFile);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("logins", out var logins))
                    {
                        foreach (var login in logins.EnumerateArray())
                        {
                            result.Credentials.Add(new BrowserCredential
                            {
                                Browser = browser,
                                Profile = profile,
                                Url = login.GetProperty("hostname").GetString() ?? string.Empty,
                                Username = login.GetProperty("encryptedUsername").GetString() ?? "[encrypted]",
                                DateCreated = DateTimeOffset.FromUnixTimeMilliseconds(login.GetProperty("timeCreated").GetInt64()).DateTime,
                                DateLastUsed = login.TryGetProperty("timeLastUsed", out var lastUsed) 
                                    ? DateTimeOffset.FromUnixTimeMilliseconds(lastUsed.GetInt64()).DateTime 
                                    : null,
                                TimesUsed = login.TryGetProperty("timesUsed", out var times) ? times.GetInt32() : 0
                            });
                        }
                    }

                    Log($"  {browser}/{profile}: {result.Credentials.Count(c => c.Profile == profile)} saved logins");
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Firefox logins ({profile}): {ex.Message}");
                }
            }, token);
        }

        private async Task ExtractFirefoxBookmarksAsync(string browser, string profile, string profilePath, BrowserForensicsResult result, CancellationToken token)
        {
            var placesDb = Path.Combine(profilePath, "places.sqlite");
            if (!File.Exists(placesDb)) return;

            var tempDb = await CopyToTempAsync(placesDb, token);
            try
            {
                using var connection = new SQLiteConnection($"Data Source={tempDb};Mode=ReadOnly;");
                await connection.OpenAsync(token);

                var query = @"
                    SELECT b.title, p.url, b.dateAdded, parent.title as folder
                    FROM moz_bookmarks b
                    JOIN moz_places p ON b.fk = p.id
                    LEFT JOIN moz_bookmarks parent ON b.parent = parent.id
                    WHERE b.type = 1
                    LIMIT 10000";

                using var cmd = new SQLiteCommand(query, connection);
                using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    result.Bookmarks.Add(new BrowserBookmark
                    {
                        Browser = browser,
                        Profile = profile,
                        Title = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        Url = reader.GetString(1),
                        DateAdded = FirefoxTimeToDateTime(reader.GetInt64(2)),
                        Folder = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                    });
                }

                Log($"  {browser}/{profile}: {result.Bookmarks.Count(b => b.Profile == profile)} bookmarks");
            }
            finally
            {
                TryDeleteTemp(tempDb);
            }
        }

        #endregion

        #region Helpers

        private async Task<string> CopyToTempAsync(string sourcePath, CancellationToken token)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"ptb_{Guid.NewGuid()}.db");
            await Task.Run(() => File.Copy(sourcePath, tempPath, true), token);
            return tempPath;
        }

        private void TryDeleteTemp(string tempPath)
        {
            try { File.Delete(tempPath); } catch { }
        }

        private static DateTime ChromiumTimeToDateTime(long chromiumTime)
        {
            if (chromiumTime == 0) return DateTime.MinValue;
            // Chromium time is microseconds since Jan 1, 1601
            var epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddTicks(chromiumTime * 10);
        }

        private static DateTime FirefoxTimeToDateTime(long firefoxTime)
        {
            if (firefoxTime == 0) return DateTime.MinValue;
            // Firefox time is microseconds since Unix epoch
            return DateTimeOffset.FromUnixTimeMilliseconds(firefoxTime / 1000).DateTime;
        }

        /// <summary>
        /// Exports results to JSON.
        /// </summary>
        public string ExportToJson(BrowserForensicsResult result)
        {
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Exports history to CSV format.
        /// </summary>
        public string ExportHistoryToCsv(BrowserForensicsResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Browser,Profile,URL,Title,VisitTime,VisitCount");
            
            foreach (var entry in result.History.OrderByDescending(h => h.VisitTime))
            {
                sb.AppendLine($"\"{entry.Browser}\",\"{entry.Profile}\",\"{entry.Url.Replace("\"", "\"\"")}\",\"{entry.Title.Replace("\"", "\"\"")}\",\"{entry.VisitTime:yyyy-MM-dd HH:mm:ss}\",{entry.VisitCount}");
            }

            return sb.ToString();
        }

        #endregion
    }
}
