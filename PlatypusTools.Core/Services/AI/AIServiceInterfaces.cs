using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.AI
{
    /// <summary>
    /// Interface for Speech-to-Text transcription.
    /// Default: Local Whisper ONNX provider.
    /// </summary>
    public interface IAISpeechToText
    {
        /// <summary>
        /// Transcribes audio to captions.
        /// </summary>
        /// <param name="audioPath">Path to audio file.</param>
        /// <param name="language">Language code (e.g., "en", "auto").</param>
        /// <param name="progress">Progress reporter (0-1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of caption segments.</returns>
        Task<List<Caption>> TranscribeAsync(
            string audioPath,
            string language = "auto",
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Supported languages.
        /// </summary>
        IReadOnlyList<string> SupportedLanguages { get; }

        /// <summary>
        /// Whether the provider is available/configured.
        /// </summary>
        bool IsAvailable { get; }
    }

    /// <summary>
    /// Interface for Text-to-Speech synthesis.
    /// Default: Local Windows TTS.
    /// </summary>
    public interface IAITextToSpeech
    {
        /// <summary>
        /// Synthesizes speech from text.
        /// </summary>
        /// <param name="text">Text to speak.</param>
        /// <param name="outputPath">Output audio file path.</param>
        /// <param name="options">TTS options.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if successful.</returns>
        Task<bool> SynthesizeAsync(
            string text,
            string outputPath,
            TtsOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Available voices.
        /// </summary>
        IReadOnlyList<TtsVoice> AvailableVoices { get; }

        /// <summary>
        /// Whether the provider is available.
        /// </summary>
        bool IsAvailable { get; }
    }

    /// <summary>
    /// Interface for AI background removal (matting).
    /// Default: Local RVM ONNX.
    /// </summary>
    public interface IAIBackgroundMatting
    {
        /// <summary>
        /// Generates an alpha matte for a frame.
        /// </summary>
        /// <param name="frameData">RGBA frame data.</param>
        /// <param name="width">Frame width.</param>
        /// <param name="height">Frame height.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Alpha matte (grayscale, same dimensions).</returns>
        Task<byte[]> GenerateMaskAsync(
            byte[] frameData,
            int width,
            int height,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Processes an entire video.
        /// </summary>
        /// <param name="inputPath">Input video path.</param>
        /// <param name="outputPath">Output video with alpha.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task ProcessVideoAsync(
            string inputPath,
            string outputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether the provider is available.
        /// </summary>
        bool IsAvailable { get; }
    }

    /// <summary>
    /// Interface for motion tracking.
    /// Default: Local OpenCV CSRT tracker.
    /// </summary>
    public interface IAIMotionTracker
    {
        /// <summary>
        /// Tracks an object through a video.
        /// </summary>
        /// <param name="videoPath">Video file path.</param>
        /// <param name="initialBbox">Initial bounding box (x, y, width, height).</param>
        /// <param name="startFrame">Starting frame number.</param>
        /// <param name="endFrame">Ending frame number (-1 for end of video).</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Tracking result with positions per frame.</returns>
        Task<TrackingResult> TrackObjectAsync(
            string videoPath,
            Rect initialBbox,
            int startFrame = 0,
            int endFrame = -1,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Available tracker algorithms.
        /// </summary>
        IReadOnlyList<string> AvailableAlgorithms { get; }

        /// <summary>
        /// Whether the provider is available.
        /// </summary>
        bool IsAvailable { get; }
    }

    /// <summary>
    /// Interface for auto-reframing (subject-aware crop).
    /// </summary>
    public interface IAIAutoReframe
    {
        /// <summary>
        /// Reframes video to a target aspect ratio.
        /// </summary>
        /// <param name="videoPath">Input video path.</param>
        /// <param name="outputPath">Output video path.</param>
        /// <param name="targetAspect">Target aspect ratio.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task<ReframeResult> ReframeAsync(
            string videoPath,
            string outputPath,
            AspectRatio targetAspect,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether the provider is available.
        /// </summary>
        bool IsAvailable { get; }
    }

    /// <summary>
    /// Interface for AI video enhancement/upscaling.
    /// Default: Local Real-ESRGAN ONNX.
    /// </summary>
    public interface IAIVideoEnhance
    {
        /// <summary>
        /// Enhances/upscales a video.
        /// </summary>
        /// <param name="inputPath">Input video path.</param>
        /// <param name="outputPath">Output video path.</param>
        /// <param name="options">Enhancement options.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task EnhanceAsync(
            string inputPath,
            string outputPath,
            VideoEnhanceOptions options,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Available enhancement models.
        /// </summary>
        IReadOnlyList<string> AvailableModels { get; }

        /// <summary>
        /// Whether the provider is available.
        /// </summary>
        bool IsAvailable { get; }
    }

    #region Support Types

    /// <summary>
    /// TTS synthesis options.
    /// </summary>
    public class TtsOptions
    {
        public string Voice { get; set; } = "default";
        public double Speed { get; set; } = 1.0;
        public double Pitch { get; set; } = 1.0;
        public double Volume { get; set; } = 1.0;
    }

    /// <summary>
    /// Available TTS voice.
    /// </summary>
    public class TtsVoice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
    }

    /// <summary>
    /// Rectangle for tracking/selection.
    /// </summary>
    public struct Rect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public Rect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;
    }

    /// <summary>
    /// Result of motion tracking.
    /// </summary>
    public class TrackingResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        
        /// <summary>
        /// Tracked positions per frame.
        /// </summary>
        public List<TrackingFrame> Frames { get; set; } = new();
        
        /// <summary>
        /// Frames where tracking was lost.
        /// </summary>
        public List<int> LostFrames { get; set; } = new();
    }

    /// <summary>
    /// Single frame of tracking data.
    /// </summary>
    public class TrackingFrame
    {
        public int FrameNumber { get; set; }
        public TimeSpan Time { get; set; }
        public Rect BoundingBox { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>
    /// Aspect ratio for reframing.
    /// </summary>
    public struct AspectRatio
    {
        public int Width { get; set; }
        public int Height { get; set; }

        public AspectRatio(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public double Value => (double)Width / Height;

        public static AspectRatio Landscape16x9 => new(16, 9);
        public static AspectRatio Portrait9x16 => new(9, 16);
        public static AspectRatio Square1x1 => new(1, 1);
        public static AspectRatio Cinematic21x9 => new(21, 9);
        public static AspectRatio Standard4x3 => new(4, 3);
    }

    /// <summary>
    /// Result of auto-reframing.
    /// </summary>
    public class ReframeResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Crop rectangles per frame.
        /// </summary>
        public List<Rect> CropPath { get; set; } = new();
    }

    /// <summary>
    /// Video enhancement options.
    /// </summary>
    public class VideoEnhanceOptions
    {
        /// <summary>
        /// Enhancement model name.
        /// </summary>
        public string Model { get; set; } = "realesrgan-x4";
        
        /// <summary>
        /// Upscale factor (2 or 4).
        /// </summary>
        public int ScaleFactor { get; set; } = 4;
        
        /// <summary>
        /// Denoise strength (0-1).
        /// </summary>
        public double DenoiseStrength { get; set; } = 0.5;
        
        /// <summary>
        /// Whether to apply deblur.
        /// </summary>
        public bool Deblur { get; set; }
        
        /// <summary>
        /// Use GPU (DirectML).
        /// </summary>
        public bool UseGpu { get; set; } = true;
    }

    #endregion
}
