using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Represents a subtitle track.
    /// </summary>
    public class SubtitleTrack
    {
        public int Index { get; set; }
        public string Language { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Codec { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsForced { get; set; }
        public bool IsExternal { get; set; }
        public string? ExternalFilePath { get; set; }
        public string DisplayName => !string.IsNullOrEmpty(Title) ? Title
            : !string.IsNullOrEmpty(Language) ? Language
            : $"Track {Index + 1}";
    }

    /// <summary>
    /// A single subtitle cue (text entry with timing).
    /// </summary>
    public class SubtitleCue
    {
        public int Index { get; set; }
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Detects, extracts, and serves subtitles from video files and external SRT/VTT files.
    /// Uses FFmpeg/FFprobe for embedded subtitle extraction.
    /// </summary>
    public class SubtitleService
    {
        private static readonly string[] SubtitleExtensions = { ".srt", ".vtt", ".ass", ".ssa", ".sub", ".idx" };

        /// <summary>
        /// Get all available subtitle tracks for a video file.
        /// Checks both embedded subtitles (via FFprobe) and external files in the same directory.
        /// </summary>
        public async Task<List<SubtitleTrack>> GetSubtitleTracksAsync(string videoFilePath, CancellationToken ct = default)
        {
            var tracks = new List<SubtitleTrack>();

            // Get embedded subtitles from video file
            var embedded = await GetEmbeddedSubtitlesAsync(videoFilePath, ct);
            tracks.AddRange(embedded);

            // Find external subtitle files
            var external = FindExternalSubtitles(videoFilePath);
            // Reindex external tracks after embedded
            var nextIndex = tracks.Count;
            foreach (var ext in external)
            {
                ext.Index = nextIndex++;
                tracks.Add(ext);
            }

            return tracks;
        }

        /// <summary>
        /// Extract an embedded subtitle track from a video file to WebVTT format.
        /// Returns the VTT content as a string.
        /// </summary>
        public async Task<string?> ExtractSubtitleAsync(string videoFilePath, int trackIndex, CancellationToken ct = default)
        {
            var ffmpegPath = FindFfmpeg();
            if (ffmpegPath == null || !File.Exists(videoFilePath))
                return null;

            var outputPath = Path.GetTempFileName() + ".vtt";

            try
            {
                var args = $"-i \"{videoFilePath}\" -map 0:s:{trackIndex} -c:s webvtt -y \"{outputPath}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                await process.WaitForExitAsync(ct);

                if (process.ExitCode == 0 && File.Exists(outputPath))
                {
                    var content = await File.ReadAllTextAsync(outputPath, ct);
                    TryDeleteFile(outputPath);
                    return content;
                }
            }
            catch { }
            finally
            {
                TryDeleteFile(outputPath);
            }

            return null;
        }

        /// <summary>
        /// Get subtitle content as WebVTT for a given track.
        /// Handles both embedded and external subtitles.
        /// </summary>
        public async Task<string?> GetSubtitleContentAsync(string videoFilePath, SubtitleTrack track, CancellationToken ct = default)
        {
            if (track.IsExternal && !string.IsNullOrEmpty(track.ExternalFilePath))
            {
                return await ReadExternalSubtitleAsVttAsync(track.ExternalFilePath, ct);
            }

            return await ExtractSubtitleAsync(videoFilePath, track.Index, ct);
        }

        /// <summary>
        /// Parse a WebVTT or SRT string into subtitle cues.
        /// </summary>
        public static List<SubtitleCue> ParseSubtitles(string content)
        {
            var cues = new List<SubtitleCue>();
            if (string.IsNullOrWhiteSpace(content)) return cues;

            // Detect format and normalize to VTT-style
            var isVtt = content.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase);

            // Split into blocks
            var blocks = Regex.Split(content, @"\r?\n\r?\n")
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList();

            int cueIndex = 0;
            foreach (var block in blocks)
            {
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();

                if (lines.Count < 2) continue;

                // Find the timing line (contains -->)
                int timingLine = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Contains("-->"))
                    {
                        timingLine = i;
                        break;
                    }
                }

                if (timingLine < 0) continue;

                var times = ParseTimingLine(lines[timingLine]);
                if (times == null) continue;

                var text = string.Join("\n", lines.Skip(timingLine + 1));
                // Strip HTML tags for plain text
                text = Regex.Replace(text, @"<[^>]+>", "");

                cues.Add(new SubtitleCue
                {
                    Index = cueIndex++,
                    StartSeconds = times.Value.Start,
                    EndSeconds = times.Value.End,
                    Text = text.Trim()
                });
            }

            return cues;
        }

        #region Private Methods

        private async Task<List<SubtitleTrack>> GetEmbeddedSubtitlesAsync(string videoFilePath, CancellationToken ct)
        {
            var tracks = new List<SubtitleTrack>();
            var ffprobePath = FindFfprobe();
            if (ffprobePath == null || !File.Exists(videoFilePath))
                return tracks;

            try
            {
                var args = $"-v quiet -print_format json -show_streams -select_streams s \"{videoFilePath}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return tracks;

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0) return tracks;

                using var doc = System.Text.Json.JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("streams", out var streams))
                {
                    int subIndex = 0;
                    foreach (var stream in streams.EnumerateArray())
                    {
                        var track = new SubtitleTrack
                        {
                            Index = subIndex++,
                            IsExternal = false
                        };

                        if (stream.TryGetProperty("codec_name", out var codec))
                            track.Codec = codec.GetString() ?? "";

                        if (stream.TryGetProperty("tags", out var tags))
                        {
                            if (tags.TryGetProperty("language", out var lang))
                                track.Language = lang.GetString() ?? "";
                            if (tags.TryGetProperty("title", out var title))
                                track.Title = title.GetString() ?? "";
                        }

                        if (stream.TryGetProperty("disposition", out var disp))
                        {
                            if (disp.TryGetProperty("default", out var def))
                                track.IsDefault = def.GetInt32() == 1;
                            if (disp.TryGetProperty("forced", out var forced))
                                track.IsForced = forced.GetInt32() == 1;
                        }

                        tracks.Add(track);
                    }
                }
            }
            catch { }

            return tracks;
        }

        private static List<SubtitleTrack> FindExternalSubtitles(string videoFilePath)
        {
            var tracks = new List<SubtitleTrack>();
            var dir = Path.GetDirectoryName(videoFilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return tracks;

            var videoName = Path.GetFileNameWithoutExtension(videoFilePath);

            foreach (var ext in SubtitleExtensions)
            {
                try
                {
                    var pattern = $"{videoName}*{ext}";
                    var files = Directory.GetFiles(dir, pattern);

                    foreach (var file in files)
                    {
                        var subName = Path.GetFileNameWithoutExtension(file);
                        var language = "";

                        // Try to extract language from filename: video.en.srt, video.english.srt
                        if (subName.Length > videoName.Length)
                        {
                            var suffix = subName[videoName.Length..].TrimStart('.', '-', '_');
                            if (!string.IsNullOrEmpty(suffix))
                                language = suffix;
                        }

                        tracks.Add(new SubtitleTrack
                        {
                            Language = language,
                            Title = Path.GetFileName(file),
                            Codec = ext.TrimStart('.'),
                            IsExternal = true,
                            ExternalFilePath = file
                        });
                    }
                }
                catch { }
            }

            return tracks;
        }

        private async Task<string?> ReadExternalSubtitleAsVttAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath)) return null;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var content = await File.ReadAllTextAsync(filePath, ct);

            return ext switch
            {
                ".vtt" => content, // Already VTT
                ".srt" => ConvertSrtToVtt(content),
                ".ass" or ".ssa" => ConvertAssToVtt(content),
                _ => null // Unsupported format
            };
        }

        /// <summary>
        /// Convert SRT format to WebVTT.
        /// </summary>
        public static string ConvertSrtToVtt(string srtContent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WEBVTT");
            sb.AppendLine();

            // SRT uses commas in timestamps, VTT uses dots
            var converted = srtContent.Replace(',', '.');

            // Remove BOM if present
            if (converted.Length > 0 && converted[0] == '\uFEFF')
                converted = converted[1..];

            sb.Append(converted);
            return sb.ToString();
        }

        /// <summary>
        /// Convert ASS/SSA format to WebVTT (basic conversion).
        /// </summary>
        public static string ConvertAssToVtt(string assContent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WEBVTT");
            sb.AppendLine();

            var lines = assContent.Split('\n');
            int cueIndex = 1;

            foreach (var line in lines)
            {
                if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = line["Dialogue:".Length..].Split(',');
                if (parts.Length < 10) continue;

                // ASS timing format: H:MM:SS.CC
                var start = ConvertAssTimestamp(parts[1].Trim());
                var end = ConvertAssTimestamp(parts[2].Trim());

                if (start == null || end == null) continue;

                // Text is everything after the 9th comma
                var text = string.Join(",", parts.Skip(9));
                // Remove ASS formatting tags
                text = Regex.Replace(text, @"\{[^}]*\}", "");
                text = text.Replace("\\N", "\n").Replace("\\n", "\n").Trim();

                sb.AppendLine($"{cueIndex++}");
                sb.AppendLine($"{start} --> {end}");
                sb.AppendLine(text);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string? ConvertAssTimestamp(string assTime)
        {
            // ASS: H:MM:SS.CC -> VTT: HH:MM:SS.MMM
            var match = Regex.Match(assTime, @"(\d+):(\d{2}):(\d{2})\.(\d{2})");
            if (!match.Success) return null;

            var h = int.Parse(match.Groups[1].Value);
            var m = int.Parse(match.Groups[2].Value);
            var s = int.Parse(match.Groups[3].Value);
            var cs = int.Parse(match.Groups[4].Value);

            return $"{h:D2}:{m:D2}:{s:D2}.{cs * 10:D3}";
        }

        private static (double Start, double End)? ParseTimingLine(string line)
        {
            // VTT/SRT: 00:00:00.000 --> 00:00:00.000
            var match = Regex.Match(line, @"(\d{1,2}:)?(\d{2}):(\d{2})[.,](\d{3})\s*-->\s*(\d{1,2}:)?(\d{2}):(\d{2})[.,](\d{3})");
            if (!match.Success) return null;

            double startH = match.Groups[1].Success ? double.Parse(match.Groups[1].Value.TrimEnd(':')) : 0;
            double startM = double.Parse(match.Groups[2].Value);
            double startS = double.Parse(match.Groups[3].Value);
            double startMs = double.Parse(match.Groups[4].Value);

            double endH = match.Groups[5].Success ? double.Parse(match.Groups[5].Value.TrimEnd(':')) : 0;
            double endM = double.Parse(match.Groups[6].Value);
            double endS = double.Parse(match.Groups[7].Value);
            double endMs = double.Parse(match.Groups[8].Value);

            var start = startH * 3600 + startM * 60 + startS + startMs / 1000;
            var end = endH * 3600 + endM * 60 + endS + endMs / 1000;

            return (start, end);
        }

        private static string? FindFfmpeg()
        {
            var candidates = new[] { "ffmpeg", @"C:\ffmpeg\bin\ffmpeg.exe" };
            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                    var psi = new ProcessStartInfo { FileName = c, Arguments = "-version", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    using var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(3000); if (p.ExitCode == 0) return c; }
                }
                catch { }
            }
            return null;
        }

        private static string? FindFfprobe()
        {
            var candidates = new[] { "ffprobe", @"C:\ffmpeg\bin\ffprobe.exe" };
            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                    var psi = new ProcessStartInfo { FileName = c, Arguments = "-version", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    using var p = Process.Start(psi);
                    if (p != null) { p.WaitForExit(3000); if (p.ExitCode == 0) return c; }
                }
                catch { }
            }
            return null;
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        #endregion
    }
}
