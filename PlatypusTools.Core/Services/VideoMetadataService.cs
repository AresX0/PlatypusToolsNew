using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for batch video metadata tagging using FFmpeg/FFprobe.
    /// Supports MP4, MKV, AVI, MOV containers.
    /// </summary>
    public class VideoMetadataService
    {
        public class VideoMetadata
        {
            public string FilePath { get; set; } = "";
            public string Title { get; set; } = "";
            public string Artist { get; set; } = "";
            public string Album { get; set; } = "";
            public string Genre { get; set; } = "";
            public string Description { get; set; } = "";
            public string Comment { get; set; } = "";
            public int? Year { get; set; }
            public int? Track { get; set; }
            public string Duration { get; set; } = "";
            public string Resolution { get; set; } = "";
            public string Codec { get; set; } = "";
            public long FileSize { get; set; }
            public Dictionary<string, string> Custom { get; set; } = new();
        }

        public VideoMetadataService()
        {
        }

        /// <summary>
        /// Read metadata from a video file.
        /// </summary>
        public async Task<VideoMetadata> ReadMetadataAsync(string filePath)
        {
            var meta = new VideoMetadata { FilePath = filePath };
            if (!File.Exists(filePath)) return meta;

            meta.FileSize = new FileInfo(filePath).Length;

            try
            {
                var ffprobePath = FFprobeService.FindFfprobe();
                if (string.IsNullOrEmpty(ffprobePath)) return meta;

                var args = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";
                var psi = new ProcessStartInfo(ffprobePath, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return meta;

                var json = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (string.IsNullOrEmpty(json)) return meta;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("format", out var format))
                {
                    if (format.TryGetProperty("duration", out var dur))
                        meta.Duration = FormatDuration(dur.GetString());

                    if (format.TryGetProperty("tags", out var tags))
                    {
                        meta.Title = GetTag(tags, "title");
                        meta.Artist = GetTag(tags, "artist", "album_artist");
                        meta.Album = GetTag(tags, "album");
                        meta.Genre = GetTag(tags, "genre");
                        meta.Description = GetTag(tags, "description", "synopsis");
                        meta.Comment = GetTag(tags, "comment");
                        var yearStr = GetTag(tags, "date", "year");
                        if (int.TryParse(yearStr?.Split('-').FirstOrDefault(), out var year))
                            meta.Year = year;
                        var trackStr = GetTag(tags, "track");
                        if (int.TryParse(trackStr?.Split('/').FirstOrDefault(), out var track))
                            meta.Track = track;
                    }
                }

                if (root.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (stream.TryGetProperty("codec_type", out var ct) && ct.GetString() == "video")
                        {
                            var w = stream.TryGetProperty("width", out var wp) ? wp.GetInt32() : 0;
                            var h = stream.TryGetProperty("height", out var hp) ? hp.GetInt32() : 0;
                            if (w > 0 && h > 0) meta.Resolution = $"{w}x{h}";
                            meta.Codec = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";
                            break;
                        }
                    }
                }
            }
            catch { }

            return meta;
        }

        /// <summary>
        /// Write metadata to a video file using FFmpeg.
        /// </summary>
        public async Task<bool> WriteMetadataAsync(VideoMetadata meta, CancellationToken ct = default)
        {
            try
            {
                var ffmpegPath = FFmpegService.FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpegPath)) return false;

                var tempPath = meta.FilePath + ".tmp" + Path.GetExtension(meta.FilePath);

                var metaArgs = new List<string>();
                if (!string.IsNullOrEmpty(meta.Title)) metaArgs.Add($"-metadata title=\"{Escape(meta.Title)}\"");
                if (!string.IsNullOrEmpty(meta.Artist)) metaArgs.Add($"-metadata artist=\"{Escape(meta.Artist)}\"");
                if (!string.IsNullOrEmpty(meta.Album)) metaArgs.Add($"-metadata album=\"{Escape(meta.Album)}\"");
                if (!string.IsNullOrEmpty(meta.Genre)) metaArgs.Add($"-metadata genre=\"{Escape(meta.Genre)}\"");
                if (!string.IsNullOrEmpty(meta.Description)) metaArgs.Add($"-metadata description=\"{Escape(meta.Description)}\"");
                if (!string.IsNullOrEmpty(meta.Comment)) metaArgs.Add($"-metadata comment=\"{Escape(meta.Comment)}\"");
                if (meta.Year.HasValue) metaArgs.Add($"-metadata date=\"{meta.Year}\"");
                if (meta.Track.HasValue) metaArgs.Add($"-metadata track=\"{meta.Track}\"");

                foreach (var kv in meta.Custom)
                    metaArgs.Add($"-metadata {Escape(kv.Key)}=\"{Escape(kv.Value)}\"");

                var args = $"-i \"{meta.FilePath}\" -c copy {string.Join(" ", metaArgs)} -y \"{tempPath}\"";

                var psi = new ProcessStartInfo(ffmpegPath, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;
                await p.WaitForExitAsync(ct);

                if (p.ExitCode == 0 && File.Exists(tempPath))
                {
                    File.Delete(meta.FilePath);
                    File.Move(tempPath, meta.FilePath);
                    return true;
                }

                if (File.Exists(tempPath)) File.Delete(tempPath);
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Batch read metadata from multiple files.
        /// </summary>
        public async Task<List<VideoMetadata>> BatchReadAsync(IEnumerable<string> files, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            var results = new List<VideoMetadata>();
            var fileList = files.ToList();

            for (int i = 0; i < fileList.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await ReadMetadataAsync(fileList[i]));
                progress?.Report((int)((i + 1.0) / fileList.Count * 100));
            }

            return results;
        }

        /// <summary>
        /// Apply the same metadata values to multiple files.
        /// </summary>
        public async Task<int> BatchWriteAsync(List<VideoMetadata> items, CancellationToken ct = default)
        {
            int success = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (await WriteMetadataAsync(item, ct))
                    success++;
            }
            return success;
        }

        private static string GetTag(JsonElement tags, params string[] names)
        {
            foreach (var name in names)
            {
                if (tags.TryGetProperty(name, out var val))
                    return val.GetString() ?? "";
                // Try case-insensitive
                foreach (var prop in tags.EnumerateObject())
                {
                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return prop.Value.GetString() ?? "";
                }
            }
            return "";
        }

        private static string FormatDuration(string? seconds)
        {
            if (string.IsNullOrEmpty(seconds)) return "";
            if (!double.TryParse(seconds, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s)) return seconds;
            var ts = TimeSpan.FromSeconds(s);
            return ts.Hours > 0 ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        private static string Escape(string text) => text.Replace("\"", "\\\"").Replace("\n", " ");
    }
}
