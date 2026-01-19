/*
 * MLT Exporter Service for PlatypusTools
 * Adapted from Shotcut (https://github.com/mltframework/shotcut)
 * Uses the MLT framework for professional-grade video rendering
 * 
 * Copyright (c) 2026 PlatypusTools
 * Licensed under GPL v3 (compatible with Shotcut's license)
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Export settings for MLT rendering.
    /// </summary>
    public class MltExportSettings
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public double FrameRate { get; set; } = 30.0;
        public string VideoCodec { get; set; } = "libx264";
        public string AudioCodec { get; set; } = "aac";
        public int VideoBitrate { get; set; } = 8000; // kbps
        public int AudioBitrate { get; set; } = 256; // kbps
        public int AudioSampleRate { get; set; } = 48000;
        public int AudioChannels { get; set; } = 2;
        public string Preset { get; set; } = "medium"; // x264 preset
        public int Crf { get; set; } = 18; // Quality (lower = better)
        public string Format { get; set; } = "mp4";
        public string Profile { get; set; } = "atsc_1080p_30"; // MLT profile
    }

    /// <summary>
    /// Export result information.
    /// </summary>
    public class MltExportResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string Log { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int ExitCode { get; set; }
    }

    /// <summary>
    /// MLT-based video exporter using melt command-line tool.
    /// This is adapted from Shotcut's approach to video rendering.
    /// </summary>
    public class MltExporter
    {
        private readonly StringBuilder _log = new();
        private string? _meltPath;
        private string? _mltDataPath;

        /// <summary>
        /// Find the melt executable.
        /// </summary>
        private string? FindMelt()
        {
            if (_meltPath != null && File.Exists(_meltPath))
                return _meltPath;

            var possiblePaths = new[]
            {
                // Bundled with Shotcut source in archive
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "melt", "melt.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shotcut", "melt.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "melt.exe"),
                
                // Shotcut Windows Code from archive (development)
                @"C:\Projects\PlatypusToolsNew\archive\shotcutsource\ShotcutWindowsCode\melt.exe",
                
                // Common Shotcut install locations
                @"C:\Program Files\Shotcut\melt.exe",
                @"C:\Program Files (x86)\Shotcut\melt.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shotcut", "melt.exe"),
                
                // PATH
                "melt.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _meltPath = path;
                    _mltDataPath = Path.Combine(Path.GetDirectoryName(path)!, "share", "mlt");
                    Log($"Found melt at: {path}");
                    return path;
                }
            }

            // Try PATH
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where.exe",
                        Arguments = "melt.exe",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                var output = proc.StandardOutput.ReadLine();
                proc.WaitForExit();
                if (!string.IsNullOrEmpty(output) && File.Exists(output))
                {
                    _meltPath = output;
                    _mltDataPath = Path.Combine(Path.GetDirectoryName(output)!, "share", "mlt");
                    return output;
                }
            }
            catch { }

            return null;
        }

        private void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _log.AppendLine($"[{timestamp}] {message}");
            Debug.WriteLine($"[MLT] {message}");
        }

        /// <summary>
        /// Export the timeline to a video file using MLT/melt.
        /// </summary>
        public async Task<MltExportResult> ExportAsync(
            IEnumerable<TimelineTrack> tracks,
            string outputPath,
            MltExportSettings settings,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new MltExportResult { OutputPath = outputPath };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                Log($"Starting MLT export to: {outputPath}");
                Log($"Settings: {settings.Width}x{settings.Height} @ {settings.FrameRate}fps, {settings.VideoCodec}");

                // Find melt
                var meltPath = FindMelt();
                if (string.IsNullOrEmpty(meltPath))
                {
                    result.ErrorMessage = "Could not find melt.exe. Please install Shotcut or copy melt.exe to the application folder.";
                    Log($"ERROR: {result.ErrorMessage}");
                    result.Log = _log.ToString();
                    return result;
                }

                // Collect clips
                var trackList = tracks.ToList();
                var videoTracks = trackList.Where(t => t.Type == TrackType.Video && t.IsVisible).OrderBy(t => t.Order).ToList();
                var audioTracks = trackList.Where(t => t.Type == TrackType.Audio && !t.IsMuted).OrderBy(t => t.Order).ToList();

                var allClips = videoTracks.SelectMany(t => t.Clips).Concat(audioTracks.SelectMany(t => t.Clips)).ToList();
                if (allClips.Count == 0)
                {
                    result.ErrorMessage = "No clips on timeline to export.";
                    Log($"ERROR: {result.ErrorMessage}");
                    result.Log = _log.ToString();
                    return result;
                }

                Log($"Found {videoTracks.Count} video tracks, {audioTracks.Count} audio tracks, {allClips.Count} total clips");

                // Calculate total duration
                var totalDuration = TimeSpan.Zero;
                foreach (var clip in allClips)
                {
                    var clipEnd = clip.StartPosition + clip.Duration;
                    if (clipEnd > totalDuration)
                        totalDuration = clipEnd;
                }
                Log($"Total duration: {totalDuration}");

                // Generate MLT XML
                var mltXml = GenerateMltXml(videoTracks, audioTracks, outputPath, settings, totalDuration);
                
                // Write to temp file
                var tempMltPath = Path.Combine(Path.GetTempPath(), $"platypus_export_{Guid.NewGuid():N}.mlt");
                await File.WriteAllTextAsync(tempMltPath, mltXml, cancellationToken);
                Log($"Wrote MLT XML to: {tempMltPath}");
                Log($"MLT XML:\n{mltXml}");

                // Run melt
                result = await RunMeltAsync(meltPath, tempMltPath, totalDuration, progress, cancellationToken);
                
                // Cleanup
                try { File.Delete(tempMltPath); } catch { }

                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                result.Log = _log.ToString();

                if (result.Success)
                {
                    Log($"Export completed successfully in {stopwatch.Elapsed.TotalSeconds:F1}s");
                }

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.ErrorMessage = $"Export failed: {ex.Message}";
                result.Log = _log.ToString() + $"\nException: {ex}";
                Log($"ERROR: {ex}");
                return result;
            }
        }

        /// <summary>
        /// Generate MLT XML document from timeline.
        /// </summary>
        private string GenerateMltXml(
            List<TimelineTrack> videoTracks,
            List<TimelineTrack> audioTracks,
            string outputPath,
            MltExportSettings settings,
            TimeSpan totalDuration)
        {
            var fps = settings.FrameRate;
            var totalFrames = (int)(totalDuration.TotalSeconds * fps);
            
            // Determine frame rate as fraction
            int fpsNum = (int)(fps * 1000);
            int fpsDen = 1000;
            // Simplify common rates
            if (Math.Abs(fps - 29.97) < 0.01) { fpsNum = 30000; fpsDen = 1001; }
            else if (Math.Abs(fps - 23.976) < 0.01) { fpsNum = 24000; fpsDen = 1001; }
            else if (Math.Abs(fps - 59.94) < 0.01) { fpsNum = 60000; fpsDen = 1001; }
            else if (fps == 30) { fpsNum = 30; fpsDen = 1; }
            else if (fps == 24) { fpsNum = 24; fpsDen = 1; }
            else if (fps == 25) { fpsNum = 25; fpsDen = 1; }
            else if (fps == 60) { fpsNum = 60; fpsDen = 1; }

            var mlt = new XElement("mlt",
                new XAttribute("LC_NUMERIC", "C"),
                new XAttribute("version", "7.0.0"),
                new XAttribute("producer", "main_tractor")
            );

            // Profile
            var profile = new XElement("profile",
                new XAttribute("description", $"{settings.Width}x{settings.Height} @ {fps}fps"),
                new XAttribute("width", settings.Width),
                new XAttribute("height", settings.Height),
                new XAttribute("progressive", "1"),
                new XAttribute("sample_aspect_num", "1"),
                new XAttribute("sample_aspect_den", "1"),
                new XAttribute("display_aspect_num", settings.Width),
                new XAttribute("display_aspect_den", settings.Height),
                new XAttribute("frame_rate_num", fpsNum),
                new XAttribute("frame_rate_den", fpsDen),
                new XAttribute("colorspace", "709")
            );
            mlt.Add(profile);

            // Collect all unique source files and create producers
            var producers = new Dictionary<string, string>(); // path -> producer_id
            var allClips = videoTracks.SelectMany(t => t.Clips).Concat(audioTracks.SelectMany(t => t.Clips)).ToList();
            int producerIdx = 0;

            foreach (var clip in allClips)
            {
                if (!string.IsNullOrEmpty(clip.SourcePath) && !producers.ContainsKey(clip.SourcePath))
                {
                    var producerId = $"producer{producerIdx++}";
                    producers[clip.SourcePath] = producerId;

                    var producer = new XElement("producer",
                        new XAttribute("id", producerId),
                        new XAttribute("in", "00:00:00.000"),
                        new XAttribute("out", FormatTime(totalDuration))
                    );

                    // Determine producer type based on file extension
                    var ext = Path.GetExtension(clip.SourcePath).ToLowerInvariant();
                    bool isImage = ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tiff";

                    producer.Add(new XElement("property", new XAttribute("name", "resource"), clip.SourcePath.Replace("\\", "/")));
                    
                    if (isImage)
                    {
                        producer.Add(new XElement("property", new XAttribute("name", "mlt_service"), "qimage"));
                        producer.Add(new XElement("property", new XAttribute("name", "ttl"), "1"));
                        // Set a very long length for images so they can be trimmed
                        producer.Add(new XElement("property", new XAttribute("name", "length"), totalFrames.ToString()));
                        producer.Add(new XElement("property", new XAttribute("name", "out"), (totalFrames - 1).ToString()));
                    }
                    else
                    {
                        producer.Add(new XElement("property", new XAttribute("name", "mlt_service"), "avformat-novalidate"));
                    }

                    mlt.Add(producer);
                }
            }

            // Create playlists for each track
            var trackPlaylists = new List<(string playlistId, TrackType type, TimelineTrack track)>();
            int playlistIdx = 0;

            foreach (var track in videoTracks)
            {
                var playlistId = $"playlist{playlistIdx++}";
                trackPlaylists.Add((playlistId, TrackType.Video, track));

                var playlist = new XElement("playlist", new XAttribute("id", playlistId));
                playlist.Add(new XElement("property", new XAttribute("name", "shotcut:video"), "1"));
                playlist.Add(new XElement("property", new XAttribute("name", "shotcut:name"), track.Name));

                // Sort clips by start position
                var sortedClips = track.Clips.OrderBy(c => c.StartPosition).ToList();
                
                var currentPos = TimeSpan.Zero;
                foreach (var clip in sortedClips)
                {
                    // Add blank if there's a gap
                    if (clip.StartPosition > currentPos)
                    {
                        var gapDuration = clip.StartPosition - currentPos;
                        var gapFrames = (int)(gapDuration.TotalSeconds * fps);
                        if (gapFrames > 0)
                        {
                            playlist.Add(new XElement("blank", new XAttribute("length", FormatTime(gapDuration))));
                        }
                    }

                    // Add clip entry
                    var producerId = producers.GetValueOrDefault(clip.SourcePath, "");
                    if (!string.IsNullOrEmpty(producerId))
                    {
                        var inPoint = clip.SourceStart;
                        var outPoint = clip.SourceStart + clip.Duration;
                        
                        var entry = new XElement("entry",
                            new XAttribute("producer", producerId),
                            new XAttribute("in", FormatTime(inPoint)),
                            new XAttribute("out", FormatTime(outPoint - TimeSpan.FromMilliseconds(1)))
                        );
                        playlist.Add(entry);
                    }

                    currentPos = clip.StartPosition + clip.Duration;
                }

                // Fill remaining with blank if needed
                if (currentPos < totalDuration)
                {
                    var remainingDuration = totalDuration - currentPos;
                    playlist.Add(new XElement("blank", new XAttribute("length", FormatTime(remainingDuration))));
                }

                mlt.Add(playlist);
            }

            // Audio tracks
            foreach (var track in audioTracks)
            {
                var playlistId = $"playlist{playlistIdx++}";
                trackPlaylists.Add((playlistId, TrackType.Audio, track));

                var playlist = new XElement("playlist", new XAttribute("id", playlistId));
                playlist.Add(new XElement("property", new XAttribute("name", "shotcut:audio"), "1"));
                playlist.Add(new XElement("property", new XAttribute("name", "shotcut:name"), track.Name));

                var sortedClips = track.Clips.OrderBy(c => c.StartPosition).ToList();
                
                var currentPos = TimeSpan.Zero;
                foreach (var clip in sortedClips)
                {
                    if (clip.StartPosition > currentPos)
                    {
                        var gapDuration = clip.StartPosition - currentPos;
                        playlist.Add(new XElement("blank", new XAttribute("length", FormatTime(gapDuration))));
                    }

                    var producerId = producers.GetValueOrDefault(clip.SourcePath, "");
                    if (!string.IsNullOrEmpty(producerId))
                    {
                        var inPoint = clip.SourceStart;
                        var outPoint = clip.SourceStart + clip.Duration;
                        
                        var entry = new XElement("entry",
                            new XAttribute("producer", producerId),
                            new XAttribute("in", FormatTime(inPoint)),
                            new XAttribute("out", FormatTime(outPoint - TimeSpan.FromMilliseconds(1)))
                        );
                        playlist.Add(entry);
                    }

                    currentPos = clip.StartPosition + clip.Duration;
                }

                if (currentPos < totalDuration)
                {
                    var remainingDuration = totalDuration - currentPos;
                    playlist.Add(new XElement("blank", new XAttribute("length", FormatTime(remainingDuration))));
                }

                mlt.Add(playlist);
            }

            // Add black background track
            var blackProducerId = "black";
            mlt.Add(new XElement("producer",
                new XAttribute("id", blackProducerId),
                new XAttribute("in", "00:00:00.000"),
                new XAttribute("out", FormatTime(totalDuration)),
                new XElement("property", new XAttribute("name", "mlt_service"), "color"),
                new XElement("property", new XAttribute("name", "resource"), "black")
            ));

            var backgroundPlaylistId = "background";
            mlt.Add(new XElement("playlist",
                new XAttribute("id", backgroundPlaylistId),
                new XElement("entry",
                    new XAttribute("producer", blackProducerId),
                    new XAttribute("in", "00:00:00.000"),
                    new XAttribute("out", FormatTime(totalDuration - TimeSpan.FromMilliseconds(1)))
                )
            ));

            // Main tractor (multitrack with transitions)
            var tractor = new XElement("tractor",
                new XAttribute("id", "main_tractor"),
                new XAttribute("in", "00:00:00.000"),
                new XAttribute("out", FormatTime(totalDuration - TimeSpan.FromMilliseconds(1)))
            );

            // Add background as first track
            tractor.Add(new XElement("track", new XAttribute("producer", backgroundPlaylistId)));

            // Add video tracks (bottom to top for proper compositing)
            foreach (var (playlistId, type, track) in trackPlaylists.Where(t => t.type == TrackType.Video).Reverse())
            {
                var trackElem = new XElement("track", new XAttribute("producer", playlistId));
                if (!track.IsVisible)
                    trackElem.Add(new XAttribute("hide", "video"));
                tractor.Add(trackElem);
            }

            // Add audio tracks
            foreach (var (playlistId, type, track) in trackPlaylists.Where(t => t.type == TrackType.Audio))
            {
                var trackElem = new XElement("track", 
                    new XAttribute("producer", playlistId),
                    new XAttribute("hide", "video"));
                if (track.IsMuted)
                    trackElem.Add(new XAttribute("hide", "both"));
                tractor.Add(trackElem);
            }

            // Add composite/blend transitions for video layers
            int videoTrackCount = trackPlaylists.Count(t => t.type == TrackType.Video);
            for (int i = 1; i <= videoTrackCount; i++)
            {
                var transition = new XElement("transition",
                    new XAttribute("id", $"transition{i}"),
                    new XElement("property", new XAttribute("name", "mlt_service"), "frei0r.cairoblend"),
                    new XElement("property", new XAttribute("name", "a_track"), "0"),
                    new XElement("property", new XAttribute("name", "b_track"), i.ToString()),
                    new XElement("property", new XAttribute("name", "blend_mode"), "normal"),
                    new XElement("property", new XAttribute("name", "always_active"), "1")
                );
                tractor.Add(transition);
            }

            // Add mix transitions for audio
            int audioStartTrack = 1 + videoTrackCount;
            int audioTrackCount = trackPlaylists.Count(t => t.type == TrackType.Audio);
            for (int i = 0; i < audioTrackCount; i++)
            {
                var transition = new XElement("transition",
                    new XAttribute("id", $"mix{i}"),
                    new XElement("property", new XAttribute("name", "mlt_service"), "mix"),
                    new XElement("property", new XAttribute("name", "a_track"), "0"),
                    new XElement("property", new XAttribute("name", "b_track"), (audioStartTrack + i).ToString()),
                    new XElement("property", new XAttribute("name", "always_active"), "1"),
                    new XElement("property", new XAttribute("name", "sum"), "1")
                );
                tractor.Add(transition);
            }

            mlt.Add(tractor);

            // Consumer (output)
            var consumer = new XElement("consumer",
                new XAttribute("id", "consumer0"),
                new XElement("property", new XAttribute("name", "mlt_service"), "avformat"),
                new XElement("property", new XAttribute("name", "target"), outputPath.Replace("\\", "/")),
                new XElement("property", new XAttribute("name", "real_time"), "-1"),
                new XElement("property", new XAttribute("name", "terminate_on_pause"), "1"),
                // Video settings
                new XElement("property", new XAttribute("name", "vcodec"), settings.VideoCodec),
                new XElement("property", new XAttribute("name", "crf"), settings.Crf.ToString()),
                new XElement("property", new XAttribute("name", "preset"), settings.Preset),
                new XElement("property", new XAttribute("name", "vprofile"), "high"),
                new XElement("property", new XAttribute("name", "pix_fmt"), "yuv420p"),
                // Audio settings
                new XElement("property", new XAttribute("name", "acodec"), settings.AudioCodec),
                new XElement("property", new XAttribute("name", "ab"), $"{settings.AudioBitrate}k"),
                new XElement("property", new XAttribute("name", "ar"), settings.AudioSampleRate.ToString()),
                new XElement("property", new XAttribute("name", "channels"), settings.AudioChannels.ToString()),
                // Container
                new XElement("property", new XAttribute("name", "f"), settings.Format),
                new XElement("property", new XAttribute("name", "movflags"), "+faststart")
            );
            mlt.Add(consumer);

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), mlt);
            return doc.ToString();
        }

        private static string FormatTime(TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Run melt to render the project.
        /// </summary>
        private async Task<MltExportResult> RunMeltAsync(
            string meltPath,
            string mltFilePath,
            TimeSpan totalDuration,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var result = new MltExportResult();
            var meltDir = Path.GetDirectoryName(meltPath)!;

            // Build arguments - use xml: prefix as Shotcut does
            // URL-encode the path for special characters
            var encodedPath = Uri.EscapeDataString(mltFilePath);
            var args = new StringBuilder();
            args.Append("-verbose ");
            args.Append("-progress2 ");
            args.Append("-abort ");
            args.Append($"\"xml:{encodedPath}\"");

            Log($"Running: \"{meltPath}\" {args}");

            var startInfo = new ProcessStartInfo
            {
                FileName = meltPath,
                Arguments = args.ToString(),
                WorkingDirectory = meltDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Set environment for melt to find its plugins
            if (_mltDataPath != null && Directory.Exists(_mltDataPath))
            {
                startInfo.Environment["MLT_DATA"] = _mltDataPath;
                startInfo.Environment["MLT_PROFILES_PATH"] = Path.Combine(_mltDataPath, "profiles");
                startInfo.Environment["MLT_PRESETS_PATH"] = Path.Combine(_mltDataPath, "presets");
            }

            // Add melt directory to PATH for DLL loading
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            startInfo.Environment["PATH"] = $"{meltDir};{pathEnv}";

            using var process = new Process { StartInfo = startInfo };
            var errorOutput = new StringBuilder();
            int previousPercent = 0;

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                
                Log($"[melt] {e.Data}");
                errorOutput.AppendLine(e.Data);

                // Parse progress: "percentage: XX"
                var line = e.Data;
                var percentIdx = line.IndexOf("percentage:", StringComparison.Ordinal);
                if (percentIdx >= 0)
                {
                    var percentStr = line.Substring(percentIdx + 11).Trim();
                    if (int.TryParse(percentStr, out int percent) && percent > previousPercent)
                    {
                        previousPercent = percent;
                        progress?.Report(percent / 100.0);
                    }
                }
            };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log($"[melt stdout] {e.Data}");
                }
            };

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            // Wait for completion or cancellation
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(true); } catch { }
                    result.ErrorMessage = "Export cancelled by user.";
                    result.ExitCode = -1;
                    return result;
                }
                await Task.Delay(100, CancellationToken.None);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            result.ExitCode = process.ExitCode;

            if (process.ExitCode == 0)
            {
                result.Success = true;
                progress?.Report(1.0);
                Log("Melt completed successfully.");
            }
            else
            {
                result.ErrorMessage = $"Melt failed with exit code {process.ExitCode}.\n{errorOutput}";
                Log($"Melt failed: {result.ErrorMessage}");
            }

            return result;
        }

        /// <summary>
        /// Check if melt is available.
        /// </summary>
        public bool IsMeltAvailable()
        {
            return FindMelt() != null;
        }

        /// <summary>
        /// Get the path to melt.exe.
        /// </summary>
        public string? GetMeltPath()
        {
            return FindMelt();
        }
    }
}
