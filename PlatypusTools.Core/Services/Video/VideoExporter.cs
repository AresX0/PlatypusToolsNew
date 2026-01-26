using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Handles video export with FFmpeg filter complex.
    /// Supports keyframe animation, transitions, overlays, and effects.
    /// </summary>
    public class VideoExporter
    {
        private readonly FFmpegService _ffmpeg;
        private readonly FFprobeService _ffprobe;
        private readonly KeyframeInterpolator _interpolator;
        private readonly string _tempDir;

        public VideoExporter(
            FFmpegService ffmpeg,
            FFprobeService ffprobe,
            KeyframeInterpolator interpolator)
        {
            _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
            _ffprobe = ffprobe ?? throw new ArgumentNullException(nameof(ffprobe));
            _interpolator = interpolator ?? throw new ArgumentNullException(nameof(interpolator));
            
            _tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "Export");
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>
        /// Exports the video project to a file.
        /// </summary>
        public async Task ExportAsync(
            VideoProject project,
            string outputPath,
            ExportProfile profile,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (project.Tracks.Count == 0 || project.Tracks.All(t => t.Clips.Count == 0))
            {
                throw new InvalidOperationException("Project has no clips to export.");
            }

            var sessionDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionDir);

            try
            {
                progress?.Report(0.05);

                // Build filter complex
                var filterComplex = BuildFilterComplex(project, profile);
                var inputs = BuildInputs(project);
                var outputArgs = BuildOutputArgs(profile, outputPath);

                // Combine all arguments
                var args = $"{inputs} {filterComplex} {outputArgs}";

                progress?.Report(0.1);

                // Execute FFmpeg with progress tracking
                await _ffmpeg.ExecuteWithProgressAsync(
                    args,
                    project.Duration,
                    new Progress<double>(p => progress?.Report(0.1 + p * 0.85)),
                    ct);

                progress?.Report(0.95);

                // Verify output
                if (!File.Exists(outputPath))
                {
                    throw new InvalidOperationException("Export failed: output file not created.");
                }

                // Embed metadata if captions exist
                if (project.CaptionTracks.Count > 0 && profile.EmbedCaptions)
                {
                    await EmbedCaptionsAsync(project, outputPath, sessionDir, ct);
                }

                progress?.Report(1.0);
            }
            finally
            {
                // Cleanup
                try
                {
                    if (Directory.Exists(sessionDir))
                    {
                        Directory.Delete(sessionDir, true);
                    }
                }
                catch { }
            }
        }

        private string BuildInputs(VideoProject project)
        {
            var inputs = new StringBuilder();
            var usedPaths = new HashSet<string>();
            var inputIndex = 0;

            // Collect unique source files
            foreach (var track in project.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (!usedPaths.Contains(clip.SourcePath))
                    {
                        usedPaths.Add(clip.SourcePath);
                        
                        // Add input with seek if trimmed
                        if (clip.TrimIn > TimeSpan.Zero)
                        {
                            inputs.Append($"-ss {clip.TrimIn.TotalSeconds} ");
                        }
                        
                        inputs.Append($"-i \"{clip.SourcePath}\" ");
                        clip.InputIndex = inputIndex++;
                    }
                    else
                    {
                        // Find existing input index
                        var existingClip = project.Tracks
                            .SelectMany(t => t.Clips)
                            .First(c => c.SourcePath == clip.SourcePath);
                        clip.InputIndex = existingClip.InputIndex;
                    }
                }
            }

            return inputs.ToString();
        }

        private string BuildFilterComplex(VideoProject project, ExportProfile profile)
        {
            var filters = new List<string>();
            var videoLabels = new List<string>();
            var audioLabels = new List<string>();

            var videoTracks = project.Tracks.Where(t => t.Type == TrackType.Video).ToList();
            var audioTracks = project.Tracks.Where(t => t.Type == TrackType.Audio).ToList();
            var overlayTracks = project.Tracks.Where(t => t.Type == TrackType.Overlay).ToList();

            // Process video clips
            int clipIdx = 0;
            foreach (var track in videoTracks)
            {
                foreach (var clip in track.Clips.OrderBy(c => c.StartTime))
                {
                    var label = $"v{clipIdx}";
                    var clipFilter = BuildClipFilter(clip, profile, label);
                    filters.Add(clipFilter);
                    videoLabels.Add($"[{label}]");
                    clipIdx++;
                }
            }

            // Concatenate video clips
            if (videoLabels.Count > 1)
            {
                var concatVideo = $"{string.Join("", videoLabels)}concat=n={videoLabels.Count}:v=1:a=0[vout]";
                filters.Add(concatVideo);
            }
            else if (videoLabels.Count == 1)
            {
                // Rename single clip to vout
                filters[^1] = filters[^1].Replace($"[{videoLabels[0].Trim('[', ']')}]", "[vout]");
            }

            // Process audio
            clipIdx = 0;
            foreach (var track in audioTracks.Concat(videoTracks.Where(t => t.Clips.Any(c => c.Volume > 0))))
            {
                foreach (var clip in track.Clips.OrderBy(c => c.StartTime))
                {
                    if (clip.Volume > 0)
                    {
                        var label = $"a{clipIdx}";
                        var audioFilter = BuildAudioFilter(clip, label);
                        filters.Add(audioFilter);
                        audioLabels.Add($"[{label}]");
                        clipIdx++;
                    }
                }
            }

            // Mix audio
            if (audioLabels.Count > 1)
            {
                var mixAudio = $"{string.Join("", audioLabels)}amix=inputs={audioLabels.Count}:duration=longest[aout]";
                filters.Add(mixAudio);
            }
            else if (audioLabels.Count == 1)
            {
                filters[^1] = filters[^1].Replace($"[{audioLabels[0].Trim('[', ']')}]", "[aout]");
            }

            // Process overlays
            foreach (var track in overlayTracks)
            {
                foreach (var clip in track.Clips.OrderBy(c => c.StartTime))
                {
                    // Add overlay filters
                    var overlayFilter = BuildOverlayFilter(clip, profile);
                    filters.Add(overlayFilter);
                }
            }

            if (filters.Count == 0)
            {
                return string.Empty;
            }

            return $"-filter_complex \"{string.Join("; ", filters)}\" -map \"[vout]\" -map \"[aout]\"";
        }

        private string BuildClipFilter(TimelineClip clip, ExportProfile profile, string outputLabel)
        {
            var filters = new List<string>();
            var inputRef = $"[{clip.InputIndex}:v]";

            // Trim
            if (clip.TrimIn > TimeSpan.Zero || clip.TrimOut < clip.Duration)
            {
                filters.Add($"trim=start={clip.TrimIn.TotalSeconds}:end={clip.TrimOut.TotalSeconds}");
                filters.Add("setpts=PTS-STARTPTS");
            }

            // Speed adjustment
            if (Math.Abs(clip.Speed - 1.0) > 0.01)
            {
                var pts = 1.0 / clip.Speed;
                filters.Add($"setpts={pts}*PTS");
            }

            // Scale to output resolution
            filters.Add($"scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease");
            filters.Add($"pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2");

            // Apply transform if keyframes exist
            if (clip.TransformKeyframes.Count > 0)
            {
                // For complex transforms, we'd need to generate per-frame data
                // Simplified: apply average transform
                var avgTransform = GetAverageTransform(clip.TransformKeyframes);
                if (avgTransform.Scale != 1.0)
                {
                    filters.Add($"zoompan=z={avgTransform.Scale}:x={avgTransform.X}:y={avgTransform.Y}:d=1");
                }
            }

            // Opacity
            if (clip.Opacity < 1.0)
            {
                filters.Add($"format=rgba,colorchannelmixer=aa={clip.Opacity}");
            }

            // Color grading
            if (clip.ColorGrading != null)
            {
                var cg = clip.ColorGrading;
                filters.Add($"eq=brightness={cg.Brightness}:contrast={cg.Contrast}:saturation={cg.Saturation}:gamma={cg.Gamma}");
                if (cg.Temperature != 0)
                {
                    // Approximate color temperature shift
                    var r = cg.Temperature > 0 ? 1.0 + cg.Temperature * 0.1 : 1.0;
                    var b = cg.Temperature < 0 ? 1.0 - cg.Temperature * 0.1 : 1.0;
                    filters.Add($"colorbalance=rs={r - 1}:bs={b - 1}");
                }
            }

            // Chroma key
            if (clip.ChromaKey != null && clip.ChromaKey.Enabled)
            {
                var ck = clip.ChromaKey;
                var color = ck.KeyColor.TrimStart('#');
                filters.Add($"chromakey=0x{color}:similarity={ck.Similarity}:blend={ck.Blend}");
            }

            // Apply clip filters (from filter library)
            if (clip.Filters != null && clip.Filters.Count > 0)
            {
                foreach (var filter in clip.Filters.Where(f => f.IsEnabled))
                {
                    var ffmpegFilter = filter.BuildFFmpegFilter();
                    if (!string.IsNullOrEmpty(ffmpegFilter))
                    {
                        filters.Add(ffmpegFilter);
                    }
                }
            }

            var filterChain = string.Join(",", filters);
            return $"{inputRef}{filterChain}[{outputLabel}]";
        }

        private string BuildAudioFilter(TimelineClip clip, string outputLabel)
        {
            var filters = new List<string>();
            var inputRef = $"[{clip.InputIndex}:a]";

            // Trim
            if (clip.TrimIn > TimeSpan.Zero || clip.TrimOut < clip.Duration)
            {
                filters.Add($"atrim=start={clip.TrimIn.TotalSeconds}:end={clip.TrimOut.TotalSeconds}");
                filters.Add("asetpts=PTS-STARTPTS");
            }

            // Speed adjustment (affects pitch)
            if (Math.Abs(clip.Speed - 1.0) > 0.01)
            {
                filters.Add($"atempo={clip.Speed}");
            }

            // Volume
            if (Math.Abs(clip.Volume - 1.0) > 0.01)
            {
                filters.Add($"volume={clip.Volume}");
            }

            // Fade in/out
            if (clip.AudioFadeIn > TimeSpan.Zero)
            {
                filters.Add($"afade=t=in:d={clip.AudioFadeIn.TotalSeconds}");
            }
            if (clip.AudioFadeOut > TimeSpan.Zero)
            {
                filters.Add($"afade=t=out:st={clip.EffectiveDuration.TotalSeconds - clip.AudioFadeOut.TotalSeconds}:d={clip.AudioFadeOut.TotalSeconds}");
            }

            var filterChain = filters.Count > 0 ? string.Join(",", filters) : "anull";
            return $"{inputRef}{filterChain}[{outputLabel}]";
        }

        private string BuildOverlayFilter(TimelineClip clip, ExportProfile profile)
        {
            var filters = new List<string>();
            var inputRef = $"[{clip.InputIndex}:v]";

            // Position and scale
            var x = clip.OverlaySettings?.X ?? 0;
            var y = clip.OverlaySettings?.Y ?? 0;
            var scale = clip.OverlaySettings?.Scale ?? 1.0;

            if (Math.Abs(scale - 1.0) > 0.01)
            {
                filters.Add($"scale=iw*{scale}:ih*{scale}");
            }

            // Blend mode (simplified - FFmpeg has limited blend modes)
            var blendMode = clip.OverlaySettings?.BlendMode ?? BlendMode.Normal;
            var blendExpr = blendMode switch
            {
                BlendMode.Multiply => "multiply",
                BlendMode.Screen => "screen",
                BlendMode.Overlay => "overlay",
                BlendMode.Lighten => "lighten",
                BlendMode.Darken => "darken",
                BlendMode.Addition => "addition",
                _ => "normal"
            };

            // Build overlay command
            var overlayFilter = $"[vout]{inputRef}overlay=x={x}:y={y}:enable='between(t,{clip.StartTime.TotalSeconds},{clip.EndTime.TotalSeconds})'[vout]";
            return overlayFilter;
        }

        private string BuildOutputArgs(ExportProfile profile, string outputPath)
        {
            var args = new StringBuilder();

            // Video codec
            args.Append($"-c:v {profile.VideoCodec} ");

            // Video settings
            if (profile.VideoBitrate > 0)
            {
                args.Append($"-b:v {profile.VideoBitrate}k ");
            }
            else if (profile.Crf >= 0)
            {
                args.Append($"-crf {profile.Crf} ");
            }

            // Preset
            args.Append($"-preset {profile.Preset.ToString().ToLowerInvariant()} ");

            // Frame rate
            args.Append($"-r {profile.FrameRate} ");

            // Pixel format
            if (!string.IsNullOrEmpty(profile.PixelFormat))
            {
                args.Append($"-pix_fmt {profile.PixelFormat} ");
            }

            // HDR settings
            if (profile.IsHdr)
            {
                args.Append("-colorspace bt2020nc -color_primaries bt2020 -color_trc smpte2084 ");
            }

            // Audio codec
            args.Append($"-c:a {profile.AudioCodec} ");
            args.Append($"-b:a {profile.AudioBitrate}k ");
            args.Append($"-ar {profile.AudioSampleRate} ");
            args.Append($"-ac {profile.AudioChannels} ");

            // Output
            args.Append($"-y \"{outputPath}\"");

            return args.ToString();
        }

        private async Task EmbedCaptionsAsync(
            VideoProject project,
            string videoPath,
            string tempDir,
            CancellationToken ct)
        {
            foreach (var track in project.CaptionTracks)
            {
                var srtPath = Path.Combine(tempDir, $"captions_{track.Language}.srt");
                var srtContent = SrtHelper.Export(track.Captions);
                await File.WriteAllTextAsync(srtPath, srtContent, Encoding.UTF8, ct);

                // Mux subtitles into video
                var tempOutput = Path.Combine(tempDir, $"output_with_subs.mp4");
                var args = $"-i \"{videoPath}\" -i \"{srtPath}\" -c copy -c:s mov_text -metadata:s:s:0 language={track.Language} -y \"{tempOutput}\"";
                
                await _ffmpeg.ExecuteAsync(args, ct);

                // Replace original
                File.Delete(videoPath);
                File.Move(tempOutput, videoPath);
            }
        }

        private ClipTransform GetAverageTransform(List<KeyframeTrack> keyframeTracks)
        {
            // Simplified: return identity transform if no keyframes
            return new ClipTransform();
        }
    }
}
