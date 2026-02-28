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
            var library = new ContentLibrary();

            foreach (var file in videoFiles)
            {
                var parsed = MetadataEnrichmentService.ParseVideoFileName(
                    Path.GetFileNameWithoutExtension(file.FilePath));

                if (parsed.IsTvShow && !string.IsNullOrWhiteSpace(parsed.CleanTitle))
                {
                    // Find or create series
                    var seriesName = NormalizeSeriesName(parsed.CleanTitle);
                    var series = library.TvSeries.FirstOrDefault(s =>
                        string.Equals(NormalizeSeriesName(s.Name), seriesName, StringComparison.OrdinalIgnoreCase));

                    if (series == null)
                    {
                        series = new TvSeries { Name = parsed.CleanTitle };
                        library.TvSeries.Add(series);
                    }

                    // Find or create season
                    var seasonNum = parsed.SeasonNumber ?? 1;
                    var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNum);
                    if (season == null)
                    {
                        season = new TvSeason { SeasonNumber = seasonNum };
                        series.Seasons.Add(season);
                        series.Seasons = series.Seasons.OrderBy(s => s.SeasonNumber).ToList();
                    }

                    // Add episode
                    var episodeNum = parsed.EpisodeNumber ?? season.Episodes.Count + 1;
                    if (!season.Episodes.Any(e => e.EpisodeNumber == episodeNum))
                    {
                        season.Episodes.Add(new TvEpisode
                        {
                            EpisodeNumber = episodeNum,
                            FilePath = file.FilePath,
                            FileName = Path.GetFileName(file.FilePath),
                            DurationSeconds = file.DurationSeconds,
                            FileSizeBytes = file.FileSizeBytes,
                            ThumbnailBase64 = file.ThumbnailBase64
                        });
                        season.Episodes = season.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
                    }
                }
                else
                {
                    // Movie / standalone video
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
            }

            // Sort: series alphabetically, movies alphabetically
            library.TvSeries = library.TvSeries.OrderBy(s => s.Name).ToList();
            library.Movies = library.Movies.OrderBy(m => m.Title).ToList();

            return library;
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
