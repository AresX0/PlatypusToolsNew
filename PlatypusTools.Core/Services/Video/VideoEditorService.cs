using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Main video editor orchestration service.
    /// Coordinates timeline, AI services, and export.
    /// </summary>
    public class VideoEditorService : IDisposable
    {
        private readonly FFmpegService _ffmpeg;
        private readonly FFprobeService _ffprobe;
        private readonly BeatDetectionService _beatDetection;
        private readonly KeyframeInterpolator _keyframeInterpolator;
        
        private readonly string _tempDir;
        private bool _disposed;

        public VideoEditorService(
            FFmpegService ffmpeg,
            FFprobeService ffprobe)
        {
            _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
            _ffprobe = ffprobe ?? throw new ArgumentNullException(nameof(ffprobe));
            
            _beatDetection = new BeatDetectionService();
            _keyframeInterpolator = new KeyframeInterpolator();
            
            _tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "VideoEditor");
            Directory.CreateDirectory(_tempDir);
        }

        #region Project Management

        /// <summary>
        /// Creates a new video editor project.
        /// </summary>
        public VideoProject CreateProject(string name, int width = 1920, int height = 1080, double fps = 30)
        {
            return new VideoProject
            {
                Id = Guid.NewGuid(),
                Name = name,
                Width = width,
                Height = height,
                FrameRate = fps,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now
            };
        }

        /// <summary>
        /// Loads a project from file.
        /// </summary>
        public async Task<VideoProject> LoadProjectAsync(string path, CancellationToken ct = default)
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var project = System.Text.Json.JsonSerializer.Deserialize<VideoProject>(json) 
                ?? throw new InvalidOperationException("Failed to load project");
            project.FilePath = path;
            return project;
        }

        /// <summary>
        /// Saves a project to file.
        /// </summary>
        public async Task SaveProjectAsync(VideoProject project, string? path = null, CancellationToken ct = default)
        {
            path ??= project.FilePath ?? throw new ArgumentNullException(nameof(path));
            project.FilePath = path;
            project.ModifiedAt = DateTime.Now;
            
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };
            var json = System.Text.Json.JsonSerializer.Serialize(project, options);
            await File.WriteAllTextAsync(path, json, ct);
        }

        #endregion

        #region Media Import

        /// <summary>
        /// Imports a media file into the project.
        /// </summary>
        public async Task<MediaAsset> ImportMediaAsync(
            string path, 
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Media file not found", path);

            var info = await _ffprobe.ProbeAsync(path, ct);
            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                FilePath = path,
                FileName = Path.GetFileName(path),
                Duration = info.Duration,
                ImportedAt = DateTime.Now
            };

            // Determine media type
            if (info.HasVideo)
            {
                asset.Type = MediaType.Video;
                asset.Width = info.Width;
                asset.Height = info.Height;
                asset.FrameRate = info.FrameRate;
                asset.HasAudio = info.HasAudio;
                
                // Generate thumbnail
                progress?.Report(0.5);
                var thumbPath = Path.Combine(_tempDir, $"thumb_{asset.Id:N}.jpg");
                await GenerateThumbnailAsync(path, thumbPath, TimeSpan.FromSeconds(1), ct);
                asset.ThumbnailPath = thumbPath;
            }
            else if (info.HasAudio)
            {
                asset.Type = MediaType.Audio;
                asset.HasAudio = true;
                
                // Generate waveform
                progress?.Report(0.5);
                asset.Waveform = await _beatDetection.GenerateWaveformAsync(path, 1000, ct);
            }
            else if (IsImageFile(path))
            {
                asset.Type = MediaType.Image;
                // Get image dimensions
                var imageInfo = await _ffprobe.ProbeAsync(path, ct);
                asset.Width = imageInfo.Width;
                asset.Height = imageInfo.Height;
            }

            progress?.Report(1.0);
            return asset;
        }

        private async Task GenerateThumbnailAsync(string videoPath, string outputPath, TimeSpan time, CancellationToken ct)
        {
            var args = $"-i \"{videoPath}\" -ss {time.TotalSeconds} -vframes 1 -s 320x180 -y \"{outputPath}\"";
            await _ffmpeg.ExecuteAsync(args, ct);
        }
        
        /// <summary>
        /// Extracts a single frame from a video file at the specified time for preview.
        /// Returns the path to the extracted frame image file.
        /// </summary>
        public async Task<string?> ExtractPreviewFrameAsync(
            string videoPath, 
            TimeSpan time, 
            int width = 640, 
            int height = 360,
            CancellationToken ct = default)
        {
            if (!File.Exists(videoPath))
                return null;
                
            try
            {
                // Create a cached frame path based on video and time
                var hash = $"{videoPath}_{time.TotalSeconds:F2}".GetHashCode();
                var framePath = Path.Combine(_tempDir, "frames", $"frame_{hash:X8}.jpg");
                
                Directory.CreateDirectory(Path.GetDirectoryName(framePath)!);
                
                // If frame already exists, return it
                if (File.Exists(framePath))
                    return framePath;
                
                // Use FFmpeg to extract the frame
                // -ss before -i is faster for seeking
                var args = $"-ss {time.TotalSeconds:F3} -i \"{videoPath}\" -vframes 1 -s {width}x{height} -q:v 2 -y \"{framePath}\"";
                var result = await _ffmpeg.ExecuteAsync(args, ct);
                
                return File.Exists(framePath) ? framePath : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VideoEditorService] Frame extraction failed: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Clears the preview frame cache.
        /// </summary>
        public void ClearPreviewFrameCache()
        {
            try
            {
                var framesDir = Path.Combine(_tempDir, "frames");
                if (Directory.Exists(framesDir))
                {
                    Directory.Delete(framesDir, true);
                    Directory.CreateDirectory(framesDir);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }

        private static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff";
        }

        #endregion

        #region Timeline Operations

        /// <summary>
        /// Adds a clip to the timeline.
        /// </summary>
        public TimelineClip AddClipToTimeline(
            VideoProject project,
            MediaAsset asset,
            int trackIndex,
            TimeSpan startTime)
        {
            var clip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                SourcePath = asset.FilePath,
                StartTime = startTime,
                EndTime = startTime + asset.Duration,
                TrimIn = TimeSpan.Zero,
                TrimOut = asset.Duration,
                Speed = 1.0,
                Opacity = 1.0,
                Volume = 1.0
            };

            // Ensure track exists
            while (project.Tracks.Count <= trackIndex)
            {
                project.Tracks.Add(new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Name = $"Track {project.Tracks.Count + 1}",
                    Type = project.Tracks.Count == 0 ? TrackType.Video : TrackType.Audio
                });
            }

            project.Tracks[trackIndex].Clips.Add(clip);
            return clip;
        }

        /// <summary>
        /// Applies beat sync to clips on a track.
        /// </summary>
        public async Task ApplyBeatSyncAsync(
            VideoProject project,
            int videoTrackIndex,
            MediaAsset audioAsset,
            BeatDetectionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            // Detect beats
            progress?.Report(0.1);
            var beatResult = await _beatDetection.DetectBeatsAsync(audioAsset.FilePath, options, progress, ct);
            
            if (!beatResult.Success || beatResult.BeatMarkers.Count == 0)
            {
                throw new InvalidOperationException($"Beat detection failed: {beatResult.Error}");
            }

            // Store beats in project
            project.BeatMarkers = beatResult.BeatMarkers;
            project.Bpm = beatResult.Bpm;

            // Snap clips to beats
            progress?.Report(0.8);
            var track = project.Tracks[videoTrackIndex];
            
            for (int i = 0; i < track.Clips.Count; i++)
            {
                var clip = track.Clips[i];
                var snappedStart = _beatDetection.SnapToBeats(beatResult.BeatMarkers, clip.StartTime);
                var offset = snappedStart - clip.StartTime;
                
                clip.StartTime = snappedStart;
                clip.EndTime += offset;
            }

            progress?.Report(1.0);
        }

        /// <summary>
        /// Applies a transition between two clips.
        /// </summary>
        public void ApplyTransition(TimelineClip fromClip, TimelineClip toClip, Transition transition)
        {
            // Ensure clips overlap for the transition duration
            var transitionDuration = transition.Duration;
            
            // Adjust clip timings if needed
            if (toClip.StartTime > fromClip.EndTime)
            {
                // Gap between clips - move toClip earlier
                toClip.StartTime = fromClip.EndTime - transitionDuration;
            }
            else if (toClip.StartTime < fromClip.EndTime - transitionDuration)
            {
                // Already overlapping more than transition - that's fine
            }
            else
            {
                // Partial overlap - extend overlap to match transition
                toClip.StartTime = fromClip.EndTime - transitionDuration;
            }

            fromClip.OutTransition = transition;
            toClip.InTransition = transition;
        }

        #endregion

        #region Keyframe Animation

        /// <summary>
        /// Gets the interpolated transform at a given time.
        /// </summary>
        public ClipTransform GetTransformAtTime(TimelineClip clip, TimeSpan time)
        {
            var clipTime = (time - clip.StartTime).TotalSeconds;
            return _keyframeInterpolator.InterpolateTransform(clip.TransformKeyframes, clipTime);
        }

        /// <summary>
        /// Gets the effective speed at a given time (with speed curves).
        /// </summary>
        public double GetSpeedAtTime(TimelineClip clip, TimeSpan time)
        {
            if (clip.SpeedCurve == null || clip.SpeedCurve.Keyframes.Count == 0)
            {
                return clip.Speed;
            }

            var clipProgress = (time - clip.StartTime).TotalSeconds / clip.Duration.TotalSeconds;
            return _keyframeInterpolator.GetValue(clip.SpeedCurve.Keyframes, clipProgress);
        }

        #endregion

        #region Export

        /// <summary>
        /// Exports the project to a video file.
        /// </summary>
        public async Task ExportAsync(
            VideoProject project,
            string outputPath,
            ExportProfile? profile = null,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            profile ??= ExportProfiles.HD1080p30;
            
            var exporter = new VideoExporter(_ffmpeg, _ffprobe, _keyframeInterpolator);
            await exporter.ExportAsync(project, outputPath, profile, progress, ct);
        }

        #endregion

        #region Cleanup

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            // Clean up temp files
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #endregion
    }

    #region Project Models

    /// <summary>
    /// Video editor project containing all timeline and asset data.
    /// </summary>
    public class VideoProject
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "Untitled";
        public string? FilePath { get; set; }
        
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public double FrameRate { get; set; } = 30;
        
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        
        /// <summary>
        /// All imported media assets.
        /// </summary>
        public List<MediaAsset> Assets { get; set; } = new();
        
        /// <summary>
        /// Timeline tracks (video, audio, overlay).
        /// </summary>
        public List<TimelineTrack> Tracks { get; set; } = new();
        
        /// <summary>
        /// Caption tracks.
        /// </summary>
        public List<CaptionTrack> CaptionTracks { get; set; } = new();
        
        /// <summary>
        /// Beat markers from audio analysis.
        /// </summary>
        public List<BeatMarker> BeatMarkers { get; set; } = new();
        
        /// <summary>
        /// Detected BPM from audio.
        /// </summary>
        public double? Bpm { get; set; }

        /// <summary>
        /// Project duration based on timeline content.
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                var maxEnd = TimeSpan.Zero;
                foreach (var track in Tracks)
                {
                    foreach (var clip in track.Clips)
                    {
                        if (clip.EndTime > maxEnd)
                            maxEnd = clip.EndTime;
                    }
                }
                return maxEnd;
            }
        }
    }

    /// <summary>
    /// Imported media asset.
    /// </summary>
    public class MediaAsset
    {
        public Guid Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public TimeSpan Duration { get; set; }
        public bool HasAudio { get; set; }
        
        public string? ThumbnailPath { get; set; }
        public WaveformData? Waveform { get; set; }
        
        public DateTime ImportedAt { get; set; }
    }

    public enum MediaType
    {
        Video,
        Audio,
        Image
    }

    #endregion
}
