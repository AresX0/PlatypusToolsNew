using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for enriching media metadata from online sources.
    /// Supports TMDb (movies/TV), MusicBrainz, Cover Art Archive, lyrics, and AcoustID.
    /// </summary>
    public class MetadataEnrichmentService
    {
        private static readonly Lazy<MetadataEnrichmentService> _instance = new(() => new MetadataEnrichmentService());
        public static MetadataEnrichmentService Instance => _instance.Value;

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const string TmdbBaseUrl = "https://api.themoviedb.org/3";
        private const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p";

        static MetadataEnrichmentService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatypusTools/3.4.0 ( https://github.com/platypustools )");
        }

        private string? _tmdbApiKey;

        /// <summary>Set the TMDb API key for movie/TV metadata lookups.</summary>
        public void SetTmdbApiKey(string apiKey) => _tmdbApiKey = apiKey;

        /// <summary>Whether TMDb is configured.</summary>
        public bool IsTmdbConfigured => !string.IsNullOrEmpty(_tmdbApiKey);

        public event EventHandler<MetadataEnrichmentProgress>? ProgressChanged;

        #region Models

        public class EnrichedMetadata
        {
            // Common
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

            // TMDb / Video enrichment
            public string? Description { get; set; }
            public string? PosterUrl { get; set; }
            public string? BackdropUrl { get; set; }
            public double? Rating { get; set; }
            public int? VoteCount { get; set; }
            public string? ReleaseDate { get; set; }
            public List<string> CastList { get; set; } = new();
            public string? Director { get; set; }
            public string? Studio { get; set; }
            public string? ContentRating { get; set; }
            public string? TmdbId { get; set; }
            public string? Network { get; set; }

            // TV Series
            public string? SeriesName { get; set; }
            public int? SeasonNumber { get; set; }
            public int? EpisodeNumber { get; set; }
            public string? EpisodeTitle { get; set; }
            public int? TotalSeasons { get; set; }
            public int? TotalEpisodes { get; set; }
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

        #region TMDb Movie/TV Metadata

        /// <summary>
        /// Search TMDb for movie metadata by title and optional year.
        /// </summary>
        public async Task<EnrichedMetadata?> SearchTmdbMovieAsync(string title, int? year = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tmdbApiKey)) return null;

            try
            {
                var url = $"{TmdbBaseUrl}/search/movie?api_key={_tmdbApiKey}&query={Uri.EscapeDataString(title)}";
                if (year.HasValue) url += $"&year={year.Value}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("results", out var results) ||
                    results.GetArrayLength() == 0)
                    return null;

                var movie = results[0];
                var movieId = movie.GetProperty("id").GetInt32();

                var metadata = new EnrichedMetadata
                {
                    Source = "TMDb",
                    Confidence = 0.85
                };

                if (movie.TryGetProperty("title", out var t)) metadata.Title = t.GetString() ?? title;
                if (movie.TryGetProperty("overview", out var o)) metadata.Description = o.GetString() ?? "";
                if (movie.TryGetProperty("poster_path", out var pp) && !string.IsNullOrEmpty(pp.GetString()))
                    metadata.PosterUrl = $"{TmdbImageBaseUrl}/w500{pp.GetString()}";
                if (movie.TryGetProperty("backdrop_path", out var bp) && !string.IsNullOrEmpty(bp.GetString()))
                    metadata.BackdropUrl = $"{TmdbImageBaseUrl}/w1280{bp.GetString()}";
                if (movie.TryGetProperty("vote_average", out var va)) metadata.Rating = va.GetDouble();
                if (movie.TryGetProperty("vote_count", out var vc)) metadata.VoteCount = vc.GetInt32();
                if (movie.TryGetProperty("release_date", out var rd))
                {
                    metadata.ReleaseDate = rd.GetString();
                    if (DateTime.TryParse(metadata.ReleaseDate, out var dt)) metadata.Year = dt.Year;
                }
                metadata.TmdbId = movieId.ToString();

                // Get credits (cast + director)
                await EnrichMovieCreditsAsync(movieId, metadata, cancellationToken);

                // Get genres from details
                await EnrichMovieDetailsAsync(movieId, metadata, cancellationToken);

                return metadata;
            }
            catch { return null; }
        }

        /// <summary>
        /// Search TMDb for TV show metadata.
        /// </summary>
        public async Task<EnrichedMetadata?> SearchTmdbTvShowAsync(string title, int? season = null, int? episode = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tmdbApiKey)) return null;

            try
            {
                var url = $"{TmdbBaseUrl}/search/tv?api_key={_tmdbApiKey}&query={Uri.EscapeDataString(title)}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("results", out var results) ||
                    results.GetArrayLength() == 0)
                    return null;

                var show = results[0];
                var showId = show.GetProperty("id").GetInt32();

                var metadata = new EnrichedMetadata
                {
                    Source = "TMDb",
                    Confidence = 0.85
                };

                if (show.TryGetProperty("name", out var n)) metadata.SeriesName = n.GetString() ?? title;
                metadata.Title = metadata.SeriesName;
                if (show.TryGetProperty("overview", out var o)) metadata.Description = o.GetString() ?? "";
                if (show.TryGetProperty("poster_path", out var pp) && !string.IsNullOrEmpty(pp.GetString()))
                    metadata.PosterUrl = $"{TmdbImageBaseUrl}/w500{pp.GetString()}";
                if (show.TryGetProperty("backdrop_path", out var bp) && !string.IsNullOrEmpty(bp.GetString()))
                    metadata.BackdropUrl = $"{TmdbImageBaseUrl}/w1280{bp.GetString()}";
                if (show.TryGetProperty("vote_average", out var va)) metadata.Rating = va.GetDouble();
                if (show.TryGetProperty("vote_count", out var vc)) metadata.VoteCount = vc.GetInt32();
                if (show.TryGetProperty("first_air_date", out var ad))
                {
                    metadata.ReleaseDate = ad.GetString();
                    if (DateTime.TryParse(metadata.ReleaseDate, out var dt)) metadata.Year = dt.Year;
                }
                if (show.TryGetProperty("number_of_seasons", out var ns)) metadata.TotalSeasons = ns.GetInt32();
                if (show.TryGetProperty("number_of_episodes", out var ne)) metadata.TotalEpisodes = ne.GetInt32();
                metadata.TmdbId = showId.ToString();
                metadata.SeasonNumber = season;
                metadata.EpisodeNumber = episode;

                // Get episode details if specified
                if (season.HasValue && episode.HasValue)
                {
                    await EnrichEpisodeDetailsAsync(showId, season.Value, episode.Value, metadata, cancellationToken);
                }

                return metadata;
            }
            catch { return null; }
        }

        /// <summary>
        /// Enrich video metadata by parsing the filename and searching TMDb.
        /// </summary>
        public async Task<EnrichedMetadata?> EnrichVideoFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tmdbApiKey)) return null;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parsed = ParseVideoFileName(fileName);

            EnrichedMetadata? metadata;
            if (parsed.IsTvShow)
            {
                metadata = await SearchTmdbTvShowAsync(parsed.CleanTitle, parsed.SeasonNumber, parsed.EpisodeNumber, cancellationToken);
            }
            else
            {
                metadata = await SearchTmdbMovieAsync(parsed.CleanTitle, parsed.Year, cancellationToken);
            }

            return metadata;
        }

        private async Task EnrichMovieCreditsAsync(int movieId, EnrichedMetadata metadata, CancellationToken ct)
        {
            try
            {
                var url = $"{TmdbBaseUrl}/movie/{movieId}/credits?api_key={_tmdbApiKey}";
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("cast", out var cast))
                {
                    foreach (var member in cast.EnumerateArray().Take(10))
                    {
                        if (member.TryGetProperty("name", out var name))
                            metadata.CastList.Add(name.GetString() ?? "");
                    }
                }

                if (doc.RootElement.TryGetProperty("crew", out var crew))
                {
                    foreach (var member in crew.EnumerateArray())
                    {
                        if (member.TryGetProperty("job", out var job) && job.GetString() == "Director" &&
                            member.TryGetProperty("name", out var name))
                        {
                            metadata.Director = name.GetString();
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private async Task EnrichMovieDetailsAsync(int movieId, EnrichedMetadata metadata, CancellationToken ct)
        {
            try
            {
                var url = $"{TmdbBaseUrl}/movie/{movieId}?api_key={_tmdbApiKey}";
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("genres", out var genres))
                {
                    var genreNames = new List<string>();
                    foreach (var g in genres.EnumerateArray())
                    {
                        if (g.TryGetProperty("name", out var gn))
                            genreNames.Add(gn.GetString() ?? "");
                    }
                    metadata.Genre = string.Join(", ", genreNames);
                }

                if (doc.RootElement.TryGetProperty("production_companies", out var companies) &&
                    companies.GetArrayLength() > 0)
                {
                    if (companies[0].TryGetProperty("name", out var cn))
                        metadata.Studio = cn.GetString();
                }
            }
            catch { }
        }

        private async Task EnrichEpisodeDetailsAsync(int showId, int season, int episode, EnrichedMetadata metadata, CancellationToken ct)
        {
            try
            {
                var url = $"{TmdbBaseUrl}/tv/{showId}/season/{season}/episode/{episode}?api_key={_tmdbApiKey}";
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("name", out var name))
                    metadata.EpisodeTitle = name.GetString();
                if (doc.RootElement.TryGetProperty("overview", out var overview))
                    metadata.Description = overview.GetString() ?? metadata.Description;
            }
            catch { }
        }

        #endregion

        #region Video Filename Parsing

        /// <summary>
        /// Parse video filename to extract title, year, season/episode info.
        /// Handles: "Show.Name.S01E02.720p", "Movie.Name.2023.1080p", etc.
        /// </summary>
        public static ParsedVideoFileName ParseVideoFileName(string fileName)
        {
            var result = new ParsedVideoFileName { OriginalFileName = fileName };

            var cleaned = fileName;
            cleaned = Regex.Replace(cleaned, @"\.(720|1080|2160|4320)[pi]", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\b(HDTV|WEBRip|WEB-DL|BluRay|BDRip|DVDRip|HDRip|HEVC|x264|x265|AAC|AC3|DTS|FLAC|PROPER|REPACK|INTERNAL)\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\[.*?\]", "");
            cleaned = Regex.Replace(cleaned, @"\(.*?\)", "");

            // TV show pattern: S01E02, 1x02
            var tvMatch = Regex.Match(fileName, @"[Ss](\d{1,2})[Ee](\d{1,3})");
            if (!tvMatch.Success)
                tvMatch = Regex.Match(fileName, @"(\d{1,2})[xX](\d{1,3})");

            if (tvMatch.Success)
            {
                result.IsTvShow = true;
                result.SeasonNumber = int.Parse(tvMatch.Groups[1].Value);
                result.EpisodeNumber = int.Parse(tvMatch.Groups[2].Value);

                var titlePart = fileName[..tvMatch.Index];
                titlePart = titlePart.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
                result.CleanTitle = titlePart;
            }
            else
            {
                var yearMatch = Regex.Match(cleaned, @"[\.\s_\-]?((?:19|20)\d{2})[\.\s_\-]?");
                if (yearMatch.Success)
                {
                    result.Year = int.Parse(yearMatch.Groups[1].Value);
                    var titlePart = cleaned[..yearMatch.Index];
                    titlePart = titlePart.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
                    result.CleanTitle = titlePart;
                }
                else
                {
                    result.CleanTitle = cleaned.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
                }
            }

            result.CleanTitle = Regex.Replace(result.CleanTitle, @"\s+", " ").Trim();
            return result;
        }

        #endregion
    }

    /// <summary>
    /// Result of parsing a video filename.
    /// </summary>
    public class ParsedVideoFileName
    {
        public string OriginalFileName { get; set; } = string.Empty;
        public string CleanTitle { get; set; } = string.Empty;
        public int? Year { get; set; }
        public bool IsTvShow { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
    }
}
