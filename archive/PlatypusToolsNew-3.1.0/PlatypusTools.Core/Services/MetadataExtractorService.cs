using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TagLib;
using PlatypusTools.Core.Models.Audio;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for extracting audio metadata from files using TagLib#.
    /// Supports MP3, FLAC, OGG, M4A, WMA, and other formats.
    /// </summary>
    public class MetadataExtractorService
    {
        /// <summary>
        /// Supported audio file extensions.
        /// </summary>
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".wav", ".opus", ".ape"
        };

        /// <summary>
        /// Extract metadata from an audio file.
        /// </summary>
        /// <param name="filePath">Path to the audio file.</param>
        /// <returns>Track with extracted metadata, or null if extraction fails.</returns>
        public static async Task<Track?> ExtractMetadataAsync(string filePath)
        {
            return await Task.Run(() => ExtractMetadata(filePath));
        }

        /// <summary>
        /// Extract metadata synchronously (may block if file is large).
        /// </summary>
        public static Track? ExtractMetadata(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                return null;

            TagLib.File? tagFile = null;
            try
            {
                tagFile = TagLib.File.Create(filePath);
                if (tagFile == null)
                    return null;

                var info = new FileInfo(filePath);
                var track = new Track
                {
                    FilePath = filePath,
                    FileSize = info.Length,
                    LastModified = info.LastWriteTimeUtc,
                    MetadataExtractedAt = DateTime.UtcNow,
                    Id = Guid.NewGuid().ToString(),
                };

                // Basic metadata
                var tag = tagFile.Tag;
                track.Title = tag.Title ?? string.Empty;
                track.Artist = tag.FirstPerformer ?? string.Empty;
                track.Album = tag.Album ?? string.Empty;
                track.AlbumArtist = tag.FirstAlbumArtist ?? string.Empty;
                track.Genre = tag.FirstGenre ?? string.Empty;
                track.Year = tag.Year != 0 ? (int?)tag.Year : null;
                track.TrackNumber = tag.Track != 0 ? (int?)tag.Track : null;
                track.TotalTracks = tag.TrackCount != 0 ? (int?)tag.TrackCount : null;
                track.DiscNumber = tag.Disc != 0 ? (int?)tag.Disc : null;
                track.Comments = tag.Comment ?? string.Empty;
                track.Composer = tag.FirstComposer ?? string.Empty;
                track.Lyrics = tag.Lyrics ?? string.Empty;

                // Multiple genres
                if (tag.Genres?.Any() == true)
                    track.Genres = tag.Genres.ToList();

                // Duration and audio properties
                var props = tagFile.Properties;
                if (props != null)
                {
                    track.DurationMs = (long)props.Duration.TotalMilliseconds;
                    track.Bitrate = props.AudioBitrate;
                    track.SampleRate = props.AudioSampleRate;
                    track.Channels = props.AudioChannels;
                    track.Codec = GetCodecFromExtension(filePath);
                }

                // Artwork
                if (tag.Pictures?.Any() == true)
                {
                    track.HasArtwork = true;
                    try
                    {
                        var picture = tag.Pictures[0];
                        var data = picture?.Data?.Data;
                        if (data != null && data.Length > 0 && data.Length <= 51200)
                        {
                            track.ArtworkThumbnail = Convert.ToBase64String(data);
                        }
                        else
                        {
                            track.HasArtwork = false;
                        }
                    }
                    catch { }
                }

                return track;
            }
            catch (Exception ex)
            {
                // Log error but don't throw - create minimal track
                System.Diagnostics.Debug.WriteLine($"Metadata extraction error for {filePath}: {ex.Message}");

                try
                {
                    var info = new FileInfo(filePath);
                    return new Track
                    {
                        FilePath = filePath,
                        FileSize = info.Length,
                        LastModified = info.LastWriteTimeUtc,
                        Title = Path.GetFileNameWithoutExtension(filePath),
                        Codec = GetCodecFromExtension(filePath),
                        MetadataExtractedAt = DateTime.UtcNow,
                        Id = Guid.NewGuid().ToString(),
                    };
                }
                catch
                {
                    return null;
                }
            }
            finally
            {
                tagFile?.Dispose();
            }
        }

        /// <summary>
        /// Extract metadata for multiple files in parallel.
        /// </summary>
        public static async Task<List<Track>> ExtractMetadataAsync(IEnumerable<string> filePaths, int maxDegreeOfParallelism = 4)
        {
            var results = new List<Track>();
            var files = filePaths.ToList();
            var tasks = new List<Task>();

            using (var semaphore = new System.Threading.SemaphoreSlim(maxDegreeOfParallelism))
            {
                foreach (var filePath in files)
                {
                    await semaphore.WaitAsync();
                    var task = ExtractMetadataAsync(filePath)
                        .ContinueWith(t =>
                        {
                            if (t.Result != null)
                                lock (results)
                                    results.Add(t.Result);

                            semaphore.Release();
                        });
                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);
            }

            return results;
        }

        /// <summary>
        /// Check if a file is likely an audio file (by extension).
        /// </summary>
        public static bool IsAudioFile(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return SupportedExtensions.Contains(ext);
        }

        /// <summary>
        /// Get codec name from file extension.
        /// </summary>
        private static string GetCodecFromExtension(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".mp3" => "MP3",
                ".flac" => "FLAC",
                ".ogg" => "OGG Vorbis",
                ".m4a" or ".aac" => "AAC",
                ".wma" => "WMA",
                ".wav" => "WAV",
                ".opus" => "Opus",
                ".ape" => "APE",
                _ => "Unknown",
            };
        }

        /// <summary>
        /// Get file information without full tag extraction (faster).
        /// </summary>
        public static (long DurationMs, int Bitrate, int SampleRate, int Channels)? GetQuickInfo(string filePath)
        {
            TagLib.File? tagFile = null;
            try
            {
                tagFile = TagLib.File.Create(filePath);
                if (tagFile?.Properties == null)
                    return null;

                return (
                    (long)tagFile.Properties.Duration.TotalMilliseconds,
                    tagFile.Properties.AudioBitrate,
                    tagFile.Properties.AudioSampleRate,
                    tagFile.Properties.AudioChannels
                );
            }
            catch
            {
                return null;
            }
            finally
            {
                tagFile?.Dispose();
            }
        }
    }
}
