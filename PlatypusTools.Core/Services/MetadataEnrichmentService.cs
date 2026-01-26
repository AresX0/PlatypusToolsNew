using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for enriching media metadata from online sources.
    /// Supports MusicBrainz, Discogs, TheAudioDB, and more.
    /// </summary>
    public class MetadataEnrichmentService
    {
        private static readonly Lazy<MetadataEnrichmentService> _instance = new(() => new MetadataEnrichmentService());
        public static MetadataEnrichmentService Instance => _instance.Value;

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static MetadataEnrichmentService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatypusTools/3.2.0 ( https://github.com/platypustools )");
        }

        public event EventHandler<MetadataEnrichmentProgress>? ProgressChanged;

        #region Models

        public class EnrichedMetadata
        {
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public string Album { get; set; } = string.Empty;
            public string AlbumArtist { get; set; } = string.Empty;
            public int? Year { get; set; }
            public int? TrackNumber { get; set; }
            public int? TrackCount { get; set; }
            public int? DiscNumber { get; set; }
            public int? DiscCount { get; set; }
            public string Genre { get; set; } = string.Empty;
            public string Composer { get; set; } = string.Empty;
            public string Publisher { get; set; } = string.Empty;
            public string Copyright { get; set; } = string.Empty;
            public string ISRC { get; set; } = string.Empty;
            public string MusicBrainzId { get; set; } = string.Empty;
            public string DiscogsId { get; set; } = string.Empty;
            public string CoverArtUrl { get; set; } = string.Empty;
            public byte[]? CoverArtData { get; set; }
            public string Lyrics { get; set; } = string.Empty;
            public double? BPM { get; set; }
            public string Key { get; set; } = string.Empty;
            public double Confidence { get; set; }
            public string Source { get; set; } = string.Empty;
            public Dictionary<string, string> AdditionalTags { get; set; } = new();
        }

        public class MetadataEnrichmentProgress
        {
            public string CurrentFile { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int FilesProcessed { get; set; }
            public int TotalFiles { get; set; }
            public int SuccessCount { get; set; }
            public int FailCount { get; set; }
        }

        public class EnrichmentOptions
        {
            public bool SearchMusicBrainz { get; set; } = true;
            public bool SearchDiscogs { get; set; }
            public bool SearchTheAudioDB { get; set; }
            public bool FetchCoverArt { get; set; } = true;
            public bool FetchLyrics { get; set; }
            public bool OverwriteExisting { get; set; }
            public double MinimumConfidence { get; set; } = 0.8;
            public string? DiscogsApiToken { get; set; }
        }

        #endregion

        #region MusicBrainz

        /// <summary>
        /// Searches MusicBrainz for track metadata.
        /// </summary>
        public async Task<EnrichedMetadata?> SearchMusicBrainzAsync(string artist, string title, string? album = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var query = $"artist:\"{Uri.EscapeDataString(artist)}\" AND recording:\"{Uri.EscapeDataString(title)}\"";
                if (!string.IsNullOrEmpty(album))
                    query += $" AND release:\"{Uri.EscapeDataString(album)}\"";

                var url = $"https://musicbrainz.org/ws/2/recording/?query={query}&fmt=json&limit=5";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("recordings", out var recordings))
                    return null;

                foreach (var recording in recordings.EnumerateArray())
                {
                    var result = new EnrichedMetadata
                    {
                        Source = "MusicBrainz",
                        Confidence = 0.9
                    };

                    if (recording.TryGetProperty("title", out var titleProp))
                        result.Title = titleProp.GetString() ?? "";

                    if (recording.TryGetProperty("id", out var idProp))
                        result.MusicBrainzId = idProp.GetString() ?? "";

                    // Get artist
                    if (recording.TryGetProperty("artist-credit", out var artistCredit))
                    {
                        foreach (var credit in artistCredit.EnumerateArray())
                        {
                            if (credit.TryGetProperty("artist", out var artistObj) &&
                                artistObj.TryGetProperty("name", out var artistName))
                            {
                                result.Artist = artistName.GetString() ?? "";
                                break;
                            }
                        }
                    }

                    // Get release (album) info
                    if (recording.TryGetProperty("releases", out var releases))
                    {
                        foreach (var release in releases.EnumerateArray())
                        {
                            if (release.TryGetProperty("title", out var relTitle))
                                result.Album = relTitle.GetString() ?? "";

                            if (release.TryGetProperty("date", out var dateProp))
                            {
                                var dateStr = dateProp.GetString();
                                if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4)
                                {
                                    if (int.TryParse(dateStr.Substring(0, 4), out var year))
                                        result.Year = year;
                                }
                            }

                            if (release.TryGetProperty("id", out var releaseId))
                            {
                                // Fetch cover art
                                result.CoverArtUrl = $"https://coverartarchive.org/release/{releaseId.GetString()}/front-250";
                            }

                            break;
                        }
                    }

                    // Get ISRC
                    if (recording.TryGetProperty("isrcs", out var isrcs))
                    {
                        foreach (var isrc in isrcs.EnumerateArray())
                        {
                            result.ISRC = isrc.GetString() ?? "";
                            break;
                        }
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MusicBrainz search error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets album details from MusicBrainz.
        /// </summary>
        public async Task<EnrichedMetadata?> GetMusicBrainzAlbumAsync(string albumId, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"https://musicbrainz.org/ws/2/release/{albumId}?inc=recordings+artists&fmt=json";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                var result = new EnrichedMetadata
                {
                    Source = "MusicBrainz",
                    MusicBrainzId = albumId
                };

                if (doc.RootElement.TryGetProperty("title", out var title))
                    result.Album = title.GetString() ?? "";

                if (doc.RootElement.TryGetProperty("date", out var date))
                {
                    var dateStr = date.GetString();
                    if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 4)
                    {
                        if (int.TryParse(dateStr.Substring(0, 4), out var year))
                            result.Year = year;
                    }
                }

                result.CoverArtUrl = $"https://coverartarchive.org/release/{albumId}/front-500";

                return result;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Cover Art

        /// <summary>
        /// Fetches cover art from Cover Art Archive.
        /// </summary>
        public async Task<byte[]?> FetchCoverArtAsync(string mbReleaseId, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"https://coverartarchive.org/release/{mbReleaseId}/front-500";
                var response = await _httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }

                // Try smaller size
                url = $"https://coverartarchive.org/release/{mbReleaseId}/front-250";
                response = await _httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Searches for cover art using artist and album name.
        /// </summary>
        public async Task<string?> SearchCoverArtUrlAsync(string artist, string album, CancellationToken cancellationToken = default)
        {
            try
            {
                // Search MusicBrainz for the release
                var query = $"artist:\"{Uri.EscapeDataString(artist)}\" AND release:\"{Uri.EscapeDataString(album)}\"";
                var url = $"https://musicbrainz.org/ws/2/release/?query={query}&fmt=json&limit=1";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("releases", out var releases))
                {
                    foreach (var release in releases.EnumerateArray())
                    {
                        if (release.TryGetProperty("id", out var id))
                        {
                            return $"https://coverartarchive.org/release/{id.GetString()}/front-500";
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        #endregion

        #region Lyrics

        /// <summary>
        /// Fetches lyrics from LRCLib (free lyrics API).
        /// </summary>
        public async Task<string?> FetchLyricsAsync(string artist, string title, CancellationToken cancellationToken = default)
        {
            try
            {
                var url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                // Prefer synced lyrics
                if (doc.RootElement.TryGetProperty("syncedLyrics", out var synced))
                {
                    var lyrics = synced.GetString();
                    if (!string.IsNullOrEmpty(lyrics))
                        return lyrics;
                }

                // Fall back to plain lyrics
                if (doc.RootElement.TryGetProperty("plainLyrics", out var plain))
                {
                    return plain.GetString();
                }
            }
            catch
            {
            }

            return null;
        }

        #endregion

        #region AcoustID / Fingerprinting

        /// <summary>
        /// Identifies a track using AcoustID fingerprinting.
        /// Requires fpcalc to generate fingerprint.
        /// </summary>
        public async Task<EnrichedMetadata?> IdentifyByFingerprintAsync(string audioFilePath, string? acoustIdApiKey = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(acoustIdApiKey))
                return null;

            try
            {
                // Generate fingerprint using fpcalc
                var fingerprint = await GenerateFingerprintAsync(audioFilePath, cancellationToken);
                if (fingerprint == null)
                    return null;

                var url = $"https://api.acoustid.org/v2/lookup?client={acoustIdApiKey}&meta=recordings+releases&fingerprint={fingerprint.Value.Fingerprint}&duration={fingerprint.Value.Duration}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    foreach (var result in results.EnumerateArray())
                    {
                        if (!result.TryGetProperty("recordings", out var recordings))
                            continue;

                        foreach (var recording in recordings.EnumerateArray())
                        {
                            var metadata = new EnrichedMetadata
                            {
                                Source = "AcoustID"
                            };

                            if (result.TryGetProperty("score", out var score))
                                metadata.Confidence = score.GetDouble();

                            if (recording.TryGetProperty("title", out var title))
                                metadata.Title = title.GetString() ?? "";

                            if (recording.TryGetProperty("id", out var mbid))
                                metadata.MusicBrainzId = mbid.GetString() ?? "";

                            if (recording.TryGetProperty("artists", out var artists))
                            {
                                foreach (var artist in artists.EnumerateArray())
                                {
                                    if (artist.TryGetProperty("name", out var name))
                                    {
                                        metadata.Artist = name.GetString() ?? "";
                                        break;
                                    }
                                }
                            }

                            return metadata;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private async Task<(string Fingerprint, int Duration)?> GenerateFingerprintAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                var fpcalcPath = "fpcalc"; // Assumes fpcalc is in PATH

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fpcalcPath,
                    Arguments = $"\"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                    return null;

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                string? fingerprint = null;
                int duration = 0;

                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("FINGERPRINT="))
                        fingerprint = line.Substring(12).Trim();
                    else if (line.StartsWith("DURATION="))
                        int.TryParse(line.Substring(9).Trim(), out duration);
                }

                if (!string.IsNullOrEmpty(fingerprint) && duration > 0)
                    return (fingerprint, duration);
            }
            catch
            {
            }

            return null;
        }

        #endregion

        #region Batch Enrichment

        /// <summary>
        /// Enriches metadata for multiple files.
        /// </summary>
        public async Task<Dictionary<string, EnrichedMetadata?>> EnrichBatchAsync(
            IEnumerable<(string FilePath, string Artist, string Title, string? Album)> files,
            EnrichmentOptions options,
            CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, EnrichedMetadata?>();
            var fileList = new List<(string FilePath, string Artist, string Title, string? Album)>(files);

            var progress = new MetadataEnrichmentProgress
            {
                TotalFiles = fileList.Count
            };

            foreach (var file in fileList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                progress.CurrentFile = file.FilePath;
                progress.Status = "Searching...";
                ProgressChanged?.Invoke(this, progress);

                try
                {
                    EnrichedMetadata? metadata = null;

                    if (options.SearchMusicBrainz)
                    {
                        metadata = await SearchMusicBrainzAsync(file.Artist, file.Title, file.Album, cancellationToken);
                    }

                    if (metadata != null)
                    {
                        if (options.FetchCoverArt && !string.IsNullOrEmpty(metadata.CoverArtUrl))
                        {
                            progress.Status = "Fetching cover art...";
                            ProgressChanged?.Invoke(this, progress);

                            try
                            {
                                var response = await _httpClient.GetAsync(metadata.CoverArtUrl, cancellationToken);
                                if (response.IsSuccessStatusCode)
                                {
                                    metadata.CoverArtData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                                }
                            }
                            catch { }
                        }

                        if (options.FetchLyrics)
                        {
                            progress.Status = "Fetching lyrics...";
                            ProgressChanged?.Invoke(this, progress);

                            metadata.Lyrics = await FetchLyricsAsync(metadata.Artist, metadata.Title, cancellationToken) ?? "";
                        }

                        progress.SuccessCount++;
                    }
                    else
                    {
                        progress.FailCount++;
                    }

                    results[file.FilePath] = metadata;
                }
                catch
                {
                    results[file.FilePath] = null;
                    progress.FailCount++;
                }

                progress.FilesProcessed++;
                ProgressChanged?.Invoke(this, progress);

                // Rate limiting (1 request per second for MusicBrainz)
                await Task.Delay(1100, cancellationToken);
            }

            return results;
        }

        #endregion
    }
}
