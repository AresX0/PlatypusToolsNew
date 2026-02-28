using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Represents a TV series with seasons and episodes.
    /// </summary>
    public class TvSeries
    {
        public string Name { get; set; } = string.Empty;
        public string? PosterUrl { get; set; }
        public int? Year { get; set; }
        public string? Description { get; set; }
        public double? Rating { get; set; }
        public List<TvSeason> Seasons { get; set; } = new();
        public int TotalEpisodes => Seasons.Sum(s => s.Episodes.Count);
        public string? TmdbId { get; set; }
    }

    /// <summary>
    /// Represents a season within a TV series.
    /// </summary>
    public class TvSeason
    {
        public int SeasonNumber { get; set; }
        public string? PosterUrl { get; set; }
        public List<TvEpisode> Episodes { get; set; } = new();
    }

    /// <summary>
    /// Represents a single episode.
    /// </summary>
    public class TvEpisode
    {
        public int EpisodeNumber { get; set; }
        public string? EpisodeTitle { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; }
        public bool IsWatched { get; set; }
        public double ResumePositionSeconds { get; set; }
        public string? ThumbnailBase64 { get; set; }
    }

    /// <summary>
    /// Options for content organization including folder type hints and manual overrides.
    /// </summary>
    public class ContentOrganizationOptions
    {
        /// <summary>Folder paths (or folder names) that should always be classified as TV shows.</summary>
        public List<string> TvShowFolders { get; set; } = new();

        /// <summary>Folder paths (or folder names) that should always be classified as movies.</summary>
        public List<string> MovieFolders { get; set; } = new();

        /// <summary>
        /// Manual overrides: filePath -> "tv:SeriesName" to force as TV show,
        /// or "movie" to force as standalone movie.
        /// </summary>
        public Dictionary<string, string> ManualOverrides { get; set; } = new();

        /// <summary>
        /// Custom TV shows created manually: seriesName -> list of file paths.
        /// </summary>
        public Dictionary<string, List<string>> CustomTvShows { get; set; } = new();
    }

    /// <summary>
    /// Organizes video files into a content hierarchy (Series > Season > Episode).
    /// Parses filenames to detect TV shows vs movies and sorts them accordingly.
    /// </summary>
    public class ContentOrganizationService
    {
        /// <summary>
        /// Organize a flat list of video files into TV series groups and standalone movies.
        /// </summary>
        public ContentLibrary OrganizeContent(IEnumerable<VideoFileInfo> videoFiles)
        {
            return OrganizeContent(videoFiles, null);
        }

        /// <summary>
        /// Organize with folder type hints, manual overrides, and custom TV shows.
        /// </summary>
        public ContentLibrary OrganizeContent(IEnumerable<VideoFileInfo> videoFiles, ContentOrganizationOptions? options)
        {
            var library = new ContentLibrary();
            options ??= new ContentOrganizationOptions();

            // Build a set for custom TV show file paths for quick lookup
            var customTvFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in options.CustomTvShows)
            {
                foreach (var fp in kvp.Value)
                    customTvFiles.Add(fp);
            }

            // First, add custom TV show entries
            foreach (var kvp in options.CustomTvShows)
            {
                var seriesName = kvp.Key;
                var series = new TvSeries { Name = seriesName };
                var season = new TvSeason { SeasonNumber = 1 };
                int epNum = 1;
                foreach (var fp in kvp.Value)
                {
                    if (!File.Exists(fp)) continue;
                    season.Episodes.Add(new TvEpisode
                    {
                        EpisodeNumber = epNum++,
                        EpisodeTitle = Path.GetFileNameWithoutExtension(fp),
                        FilePath = fp,
                        FileName = Path.GetFileName(fp),
                        FileSizeBytes = new FileInfo(fp).Length
                    });
                }
                if (season.Episodes.Count > 0)
                {
                    series.Seasons.Add(season);
                    library.TvSeries.Add(series);
                }
            }

            foreach (var file in videoFiles)
            {
                // Skip files already in custom TV shows
                if (customTvFiles.Contains(file.FilePath)) continue;

                // Check manual overrides first
                if (options.ManualOverrides.TryGetValue(file.FilePath, out var overrideValue))
                {
                    if (overrideValue.StartsWith("tv:", StringComparison.OrdinalIgnoreCase))
                    {
                        var seriesName = overrideValue.Substring(3).Trim();
                        AddToSeries(library, file, seriesName, null, null);
                        continue;
                    }
                    else if (overrideValue.Equals("movie", StringComparison.OrdinalIgnoreCase))
                    {
                        AddAsMovie(library, file);
                        continue;
                    }
                }

                // Check folder type hints
                var folderHint = GetFolderHint(file.FilePath, options);
                var parsed = MetadataEnrichmentService.ParseVideoFileName(
                    Path.GetFileNameWithoutExtension(file.FilePath));

                if (folderHint == "tv")
                {
                    // Force as TV show - use parsed info if available, else use filename as series name
                    if (parsed.IsTvShow && !string.IsNullOrWhiteSpace(parsed.CleanTitle))
                    {
                        AddToSeries(library, file, parsed.CleanTitle, parsed.SeasonNumber, parsed.EpisodeNumber);
                    }
                    else
                    {
                        // Use parent folder name as series name for non-episodic files in TV folders
                        var parentDir = Path.GetDirectoryName(file.FilePath);
                        var seriesName = !string.IsNullOrWhiteSpace(parentDir)
                            ? Path.GetFileName(parentDir)
                            : Path.GetFileNameWithoutExtension(file.FilePath);
                        AddToSeries(library, file, seriesName ?? "Unknown", null, null);
                    }
                }
                else if (folderHint == "movie")
                {
                    // Force as movie regardless of filename pattern
                    AddAsMovie(library, file);
                }
                else if (parsed.IsTvShow && !string.IsNullOrWhiteSpace(parsed.CleanTitle))
                {
                    // Default: filename-based detection
                    AddToSeries(library, file, parsed.CleanTitle, parsed.SeasonNumber, parsed.EpisodeNumber);
                }
                else
                {
                    AddAsMovie(library, file);
                }
            }

            // Sort: series alphabetically, movies alphabetically
            library.TvSeries = library.TvSeries.OrderBy(s => s.Name).ToList();
            library.Movies = library.Movies.OrderBy(m => m.Title).ToList();

            return library;
        }

        private static void AddToSeries(ContentLibrary library, VideoFileInfo file, string seriesName, int? seasonNum, int? episodeNum)
        {
            var normalizedName = NormalizeSeriesName(seriesName);
            var series = library.TvSeries.FirstOrDefault(s =>
                string.Equals(NormalizeSeriesName(s.Name), normalizedName, StringComparison.OrdinalIgnoreCase));

            if (series == null)
            {
                series = new TvSeries { Name = seriesName };
                library.TvSeries.Add(series);
            }

            var sn = seasonNum ?? 1;
            var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == sn);
            if (season == null)
            {
                season = new TvSeason { SeasonNumber = sn };
                series.Seasons.Add(season);
                series.Seasons = series.Seasons.OrderBy(s => s.SeasonNumber).ToList();
            }

            var en = episodeNum ?? season.Episodes.Count + 1;
            if (!season.Episodes.Any(e => e.EpisodeNumber == en && e.FilePath == file.FilePath))
            {
                season.Episodes.Add(new TvEpisode
                {
                    EpisodeNumber = en,
                    FilePath = file.FilePath,
                    FileName = Path.GetFileName(file.FilePath),
                    DurationSeconds = file.DurationSeconds,
                    FileSizeBytes = file.FileSizeBytes,
                    ThumbnailBase64 = file.ThumbnailBase64
                });
                season.Episodes = season.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
            }
        }

        private static void AddAsMovie(ContentLibrary library, VideoFileInfo file)
        {
            var parsed = MetadataEnrichmentService.ParseVideoFileName(
                Path.GetFileNameWithoutExtension(file.FilePath));

            library.Movies.Add(new MovieItem
            {
                Title = !string.IsNullOrWhiteSpace(parsed.CleanTitle) ? parsed.CleanTitle : Path.GetFileNameWithoutExtension(file.FilePath),
                Year = parsed.Year,
                FilePath = file.FilePath,
                FileName = Path.GetFileName(file.FilePath),
                DurationSeconds = file.DurationSeconds,
                FileSizeBytes = file.FileSizeBytes,
                ThumbnailBase64 = file.ThumbnailBase64
            });
        }

        /// <summary>
        /// Determine if a file falls under a designated TV or Movie folder.
        /// Returns "tv", "movie", or null (no hint).
        /// </summary>
        private static string? GetFolderHint(string filePath, ContentOrganizationOptions options)
        {
            // Check TV show folders
            foreach (var tvFolder in options.TvShowFolders)
            {
                if (string.IsNullOrWhiteSpace(tvFolder)) continue;

                // Check as full path prefix
                if (filePath.StartsWith(tvFolder, StringComparison.OrdinalIgnoreCase))
                    return "tv";

                // Check as folder name match in any ancestor
                var folderName = Path.GetFileName(tvFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (IsInNamedFolder(filePath, folderName))
                    return "tv";
            }

            // Check movie folders
            foreach (var movieFolder in options.MovieFolders)
            {
                if (string.IsNullOrWhiteSpace(movieFolder)) continue;

                if (filePath.StartsWith(movieFolder, StringComparison.OrdinalIgnoreCase))
                    return "movie";

                var folderName = Path.GetFileName(movieFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (IsInNamedFolder(filePath, folderName))
                    return "movie";
            }

            return null;
        }

        /// <summary>
        /// Check if a file path has an ancestor folder with the given name.
        /// </summary>
        private static bool IsInNamedFolder(string filePath, string folderName)
        {
            var dir = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(dir))
            {
                var current = Path.GetFileName(dir);
                if (string.Equals(current, folderName, StringComparison.OrdinalIgnoreCase))
                    return true;
                dir = Path.GetDirectoryName(dir);
            }
            return false;
        }

        /// <summary>
        /// Get a flat list of "up next" episodes across all series.
        /// Returns the first unwatched episode for each series that has been started.
        /// </summary>
        public List<UpNextItem> GetUpNextItems(ContentLibrary library, PlaybackHistoryService? historyService = null)
        {
            var upNext = new List<UpNextItem>();

            foreach (var series in library.TvSeries)
            {
                // Find the first unwatched episode
                foreach (var season in series.Seasons.OrderBy(s => s.SeasonNumber))
                {
                    foreach (var episode in season.Episodes.OrderBy(e => e.EpisodeNumber))
                    {
                        if (!episode.IsWatched)
                        {
                            upNext.Add(new UpNextItem
                            {
                                SeriesName = series.Name,
                                SeasonNumber = season.SeasonNumber,
                                EpisodeNumber = episode.EpisodeNumber,
                                EpisodeTitle = episode.EpisodeTitle,
                                FilePath = episode.FilePath,
                                ResumePositionSeconds = episode.ResumePositionSeconds,
                                PosterUrl = series.PosterUrl,
                                ThumbnailBase64 = episode.ThumbnailBase64
                            });
                            break; // Only the first unwatched per season, then break to next series
                        }
                    }
                    if (upNext.Any(u => u.SeriesName == series.Name))
                        break; // Already found an unwatched episode for this series
                }
            }

            return upNext;
        }

        private static string NormalizeSeriesName(string name)
        {
            // Remove common articles for matching
            var normalized = name.Trim().ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"^(the|a|an)\s+", "");
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized;
        }
    }

    /// <summary>
    /// The organized content library containing TV series and movies.
    /// </summary>
    public class ContentLibrary
    {
        public List<TvSeries> TvSeries { get; set; } = new();
        public List<MovieItem> Movies { get; set; } = new();
        public int TotalSeries => TvSeries.Count;
        public int TotalMovies => Movies.Count;
        public int TotalItems => TvSeries.Sum(s => s.TotalEpisodes) + Movies.Count;
    }

    /// <summary>
    /// A standalone movie/video item.
    /// </summary>
    public class MovieItem
    {
        public string Title { get; set; } = string.Empty;
        public int? Year { get; set; }
        public string? PosterUrl { get; set; }
        public string? Description { get; set; }
        public double? Rating { get; set; }
        public string? Genre { get; set; }
        public string? Director { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; }
        public bool IsWatched { get; set; }
        public double ResumePositionSeconds { get; set; }
        public string? ThumbnailBase64 { get; set; }
        public string? TmdbId { get; set; }
    }

    /// <summary>
    /// An "Up Next" recommendation for continuing a series.
    /// </summary>
    public class UpNextItem
    {
        public string SeriesName { get; set; } = string.Empty;
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public string? EpisodeTitle { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public double ResumePositionSeconds { get; set; }
        public string? PosterUrl { get; set; }
        public string? ThumbnailBase64 { get; set; }
        public string DisplayTitle => $"{SeriesName} - S{SeasonNumber:D2}E{EpisodeNumber:D2}" +
            (!string.IsNullOrEmpty(EpisodeTitle) ? $" - {EpisodeTitle}" : "");
    }

    /// <summary>
    /// Basic video file info for content organization.
    /// </summary>
    public class VideoFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; }
        public string? ThumbnailBase64 { get; set; }
    }
}
