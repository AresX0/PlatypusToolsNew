using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Proxy editing settings for high-resolution footage.
    /// </summary>
    public class ProxySettings
    {
        /// <summary>
        /// Whether proxy editing is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }
        
        /// <summary>
        /// Resolution for proxy files.
        /// </summary>
        public ProxyResolution Resolution { get; set; } = ProxyResolution.HD720;
        
        /// <summary>
        /// Quality preset for proxy encoding.
        /// </summary>
        public ProxyQuality Quality { get; set; } = ProxyQuality.Medium;
        
        /// <summary>
        /// Directory for storing proxy files.
        /// </summary>
        public string ProxyDirectory { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether to auto-generate proxies for imported media.
        /// </summary>
        public bool AutoGenerateProxies { get; set; } = true;
        
        /// <summary>
        /// Minimum resolution to trigger proxy generation.
        /// </summary>
        public int MinResolutionForProxy { get; set; } = 1920;
    }

    /// <summary>
    /// Proxy resolution options.
    /// </summary>
    public enum ProxyResolution
    {
        SD480,      // 854x480
        HD720,      // 1280x720
        HD1080,     // 1920x1080 (for 4K+ sources)
        Quarter     // 1/4 of original
    }

    /// <summary>
    /// Proxy encoding quality.
    /// </summary>
    public enum ProxyQuality
    {
        Low,        // Fast encoding, larger files
        Medium,     // Balanced
        High        // Slower encoding, smaller files
    }

    /// <summary>
    /// Represents a proxy file for high-res media.
    /// </summary>
    public class ProxyFile
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string ProxyPath { get; set; } = string.Empty;
        public ProxyResolution Resolution { get; set; }
        public DateTime CreatedAt { get; set; }
        public long OriginalFileSize { get; set; }
        public long ProxyFileSize { get; set; }
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// Timeline playback and preview settings.
    /// </summary>
    public class PlaybackSettings
    {
        /// <summary>
        /// Preview resolution.
        /// </summary>
        public PreviewResolution Resolution { get; set; } = PreviewResolution.Auto;
        
        /// <summary>
        /// Preview quality.
        /// </summary>
        public PreviewQuality Quality { get; set; } = PreviewQuality.Full;
        
        /// <summary>
        /// Whether to use GPU acceleration.
        /// </summary>
        public bool UseHardwareAcceleration { get; set; } = true;
        
        /// <summary>
        /// Whether to enable real-time effects preview.
        /// </summary>
        public bool EnableEffectsPreview { get; set; } = true;
        
        /// <summary>
        /// Whether to enable audio scrubbing.
        /// </summary>
        public bool EnableAudioScrubbing { get; set; } = true;
        
        /// <summary>
        /// JKL shuttle speed multiplier.
        /// </summary>
        public double ShuttleSpeed { get; set; } = 2.0;
        
        /// <summary>
        /// Whether to show safe areas.
        /// </summary>
        public bool ShowSafeAreas { get; set; } = false;
        
        /// <summary>
        /// Whether to show grid overlay.
        /// </summary>
        public bool ShowGrid { get; set; } = false;
        
        /// <summary>
        /// Grid size in pixels.
        /// </summary>
        public int GridSize { get; set; } = 64;
    }

    /// <summary>
    /// Preview resolution options.
    /// </summary>
    public enum PreviewResolution
    {
        Auto,
        Full,
        Half,
        Quarter,
        Eighth
    }

    /// <summary>
    /// Preview quality options.
    /// </summary>
    public enum PreviewQuality
    {
        Full,
        High,
        Medium,
        Low,
        Draft
    }

    /// <summary>
    /// Project settings and metadata.
    /// </summary>
    public class ProjectSettings
    {
        /// <summary>
        /// Video mode/resolution preset.
        /// </summary>
        public VideoMode VideoMode { get; set; } = VideoMode.HD1080p30;
        
        /// <summary>
        /// Custom width if VideoMode is Custom.
        /// </summary>
        public int CustomWidth { get; set; } = 1920;
        
        /// <summary>
        /// Custom height if VideoMode is Custom.
        /// </summary>
        public int CustomHeight { get; set; } = 1080;
        
        /// <summary>
        /// Custom frame rate if VideoMode is Custom.
        /// </summary>
        public double CustomFrameRate { get; set; } = 30;
        
        /// <summary>
        /// Aspect ratio display mode.
        /// </summary>
        public AspectRatioMode AspectRatioMode { get; set; } = AspectRatioMode.SampleAspectRatio;
        
        /// <summary>
        /// Color space.
        /// </summary>
        public ColorSpace ColorSpace { get; set; } = ColorSpace.BT709;
        
        /// <summary>
        /// Whether project uses proxy editing.
        /// </summary>
        public ProxySettings ProxySettings { get; set; } = new();
        
        /// <summary>
        /// Playback settings.
        /// </summary>
        public PlaybackSettings PlaybackSettings { get; set; } = new();
        
        /// <summary>
        /// Project author.
        /// </summary>
        public string Author { get; set; } = string.Empty;
        
        /// <summary>
        /// Project notes.
        /// </summary>
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Standard video modes (matching Shotcut's presets).
    /// </summary>
    public enum VideoMode
    {
        // HD
        HD720p24,
        HD720p25,
        HD720p30,
        HD720p50,
        HD720p60,
        
        HD1080p24,
        HD1080p25,
        HD1080p30,
        HD1080p50,
        HD1080p60,
        
        // 4K
        UHD4K24,
        UHD4K25,
        UHD4K30,
        UHD4K50,
        UHD4K60,
        
        // Vertical (mobile)
        Vertical1080x1920p30,
        Vertical1080x1920p60,
        
        // Square (social)
        Square1080p30,
        Square1080p60,
        
        // SD
        SD480p30,
        SD576p25,
        
        // Custom
        Custom
    }

    /// <summary>
    /// Aspect ratio handling modes.
    /// </summary>
    public enum AspectRatioMode
    {
        SampleAspectRatio,  // Default
        DisplayAspectRatio,
        Custom
    }

    /// <summary>
    /// Color space options.
    /// </summary>
    public enum ColorSpace
    {
        BT601,      // SD
        BT709,      // HD (default)
        BT2020      // HDR/UHD
    }

    /// <summary>
    /// Helper to get video mode dimensions.
    /// </summary>
    public static class VideoModeHelper
    {
        public static (int Width, int Height, double FrameRate) GetModeSettings(VideoMode mode) => mode switch
        {
            VideoMode.HD720p24 => (1280, 720, 24),
            VideoMode.HD720p25 => (1280, 720, 25),
            VideoMode.HD720p30 => (1280, 720, 30),
            VideoMode.HD720p50 => (1280, 720, 50),
            VideoMode.HD720p60 => (1280, 720, 60),
            
            VideoMode.HD1080p24 => (1920, 1080, 24),
            VideoMode.HD1080p25 => (1920, 1080, 25),
            VideoMode.HD1080p30 => (1920, 1080, 30),
            VideoMode.HD1080p50 => (1920, 1080, 50),
            VideoMode.HD1080p60 => (1920, 1080, 60),
            
            VideoMode.UHD4K24 => (3840, 2160, 24),
            VideoMode.UHD4K25 => (3840, 2160, 25),
            VideoMode.UHD4K30 => (3840, 2160, 30),
            VideoMode.UHD4K50 => (3840, 2160, 50),
            VideoMode.UHD4K60 => (3840, 2160, 60),
            
            VideoMode.Vertical1080x1920p30 => (1080, 1920, 30),
            VideoMode.Vertical1080x1920p60 => (1080, 1920, 60),
            
            VideoMode.Square1080p30 => (1080, 1080, 30),
            VideoMode.Square1080p60 => (1080, 1080, 60),
            
            VideoMode.SD480p30 => (720, 480, 30),
            VideoMode.SD576p25 => (720, 576, 25),
            
            _ => (1920, 1080, 30)
        };

        public static string GetDisplayName(VideoMode mode) => mode switch
        {
            VideoMode.HD720p24 => "HD 720p 24fps",
            VideoMode.HD720p25 => "HD 720p 25fps",
            VideoMode.HD720p30 => "HD 720p 30fps",
            VideoMode.HD720p50 => "HD 720p 50fps",
            VideoMode.HD720p60 => "HD 720p 60fps",
            
            VideoMode.HD1080p24 => "HD 1080p 24fps",
            VideoMode.HD1080p25 => "HD 1080p 25fps",
            VideoMode.HD1080p30 => "HD 1080p 30fps",
            VideoMode.HD1080p50 => "HD 1080p 50fps",
            VideoMode.HD1080p60 => "HD 1080p 60fps",
            
            VideoMode.UHD4K24 => "4K UHD 24fps",
            VideoMode.UHD4K25 => "4K UHD 25fps",
            VideoMode.UHD4K30 => "4K UHD 30fps",
            VideoMode.UHD4K50 => "4K UHD 50fps",
            VideoMode.UHD4K60 => "4K UHD 60fps",
            
            VideoMode.Vertical1080x1920p30 => "Vertical 9:16 30fps",
            VideoMode.Vertical1080x1920p60 => "Vertical 9:16 60fps",
            
            VideoMode.Square1080p30 => "Square 1:1 30fps",
            VideoMode.Square1080p60 => "Square 1:1 60fps",
            
            VideoMode.SD480p30 => "SD 480p 30fps",
            VideoMode.SD576p25 => "SD 576p 25fps",
            
            VideoMode.Custom => "Custom",
            
            _ => mode.ToString()
        };
    }
}
