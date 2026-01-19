using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Settings for overlay clips (video/image overlays).
    /// </summary>
    public class OverlaySettings
    {
        /// <summary>
        /// Transform properties (position, scale, rotation).
        /// </summary>
        public ClipTransform Transform { get; set; } = new();

        /// <summary>
        /// X position (convenience accessor for Transform.X).
        /// </summary>
        [JsonIgnore]
        public double X
        {
            get => Transform.X;
            set => Transform.X = value;
        }

        /// <summary>
        /// Y position (convenience accessor for Transform.Y).
        /// </summary>
        [JsonIgnore]
        public double Y
        {
            get => Transform.Y;
            set => Transform.Y = value;
        }

        /// <summary>
        /// Scale (convenience accessor for Transform.Scale).
        /// </summary>
        [JsonIgnore]
        public double Scale
        {
            get => Transform.Scale;
            set => Transform.Scale = value;
        }
        
        /// <summary>
        /// Opacity (0-1).
        /// </summary>
        public double Opacity { get; set; } = 1.0;
        
        /// <summary>
        /// Blend mode for compositing.
        /// </summary>
        public BlendMode BlendMode { get; set; } = BlendMode.Normal;
        
        /// <summary>
        /// Whether background removal is enabled.
        /// </summary>
        public bool RemoveBackground { get; set; }
        
        /// <summary>
        /// Background removal method.
        /// </summary>
        public BackgroundRemovalMethod RemovalMethod { get; set; } = BackgroundRemovalMethod.AIMatting;
        
        /// <summary>
        /// Chroma key settings (when using chroma key method).
        /// </summary>
        public ChromaKeySettings ChromaKey { get; set; } = new();
        
        /// <summary>
        /// Edge refinement amount (0-1).
        /// </summary>
        public double EdgeRefine { get; set; } = 0.5;
        
        /// <summary>
        /// Edge feather amount in pixels.
        /// </summary>
        public double EdgeFeather { get; set; } = 2.0;
        
        /// <summary>
        /// Spill suppression strength (0-1).
        /// </summary>
        public double SpillSuppression { get; set; } = 0.5;
        
        /// <summary>
        /// Crop settings.
        /// </summary>
        public CropSettings Crop { get; set; } = new();
        
        /// <summary>
        /// Drop shadow settings.
        /// </summary>
        public ShadowSettings Shadow { get; set; } = new();
        
        /// <summary>
        /// Border/stroke settings.
        /// </summary>
        public BorderSettings Border { get; set; } = new();
    }

    /// <summary>
    /// Blend modes for overlay compositing.
    /// </summary>
    public enum BlendMode
    {
        Normal,
        Multiply,
        Screen,
        Overlay,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        HardLight,
        SoftLight,
        Difference,
        Exclusion,
        Hue,
        Saturation,
        Color,
        Luminosity,
        Addition
    }

    /// <summary>
    /// Methods for background removal.
    /// </summary>
    public enum BackgroundRemovalMethod
    {
        None,
        AIMatting,
        ChromaKey,
        LumaKey
    }

    /// <summary>
    /// Chroma key (green screen) settings.
    /// </summary>
    public class ChromaKeySettings
    {
        /// <summary>
        /// Whether chroma key is enabled.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Key color (hex).
        /// </summary>
        public string KeyColor { get; set; } = "#00FF00";
        
        /// <summary>
        /// Similarity threshold (0-1).
        /// </summary>
        public double Similarity { get; set; } = 0.4;
        
        /// <summary>
        /// Blend/smoothness (0-1).
        /// </summary>
        public double Blend { get; set; } = 0.1;
        
        /// <summary>
        /// Spill suppression (0-1).
        /// </summary>
        public double SpillSuppression { get; set; } = 0.5;
    }

    /// <summary>
    /// Crop settings for overlays.
    /// </summary>
    public class CropSettings
    {
        /// <summary>
        /// Left crop (0-1, percentage of width).
        /// </summary>
        public double Left { get; set; }
        
        /// <summary>
        /// Right crop (0-1, percentage of width).
        /// </summary>
        public double Right { get; set; }
        
        /// <summary>
        /// Top crop (0-1, percentage of height).
        /// </summary>
        public double Top { get; set; }
        
        /// <summary>
        /// Bottom crop (0-1, percentage of height).
        /// </summary>
        public double Bottom { get; set; }
    }

    /// <summary>
    /// Drop shadow settings.
    /// </summary>
    public class ShadowSettings
    {
        /// <summary>
        /// Whether shadow is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }
        
        /// <summary>
        /// Shadow color (hex with alpha).
        /// </summary>
        public string Color { get; set; } = "#80000000";
        
        /// <summary>
        /// Blur radius.
        /// </summary>
        public double BlurRadius { get; set; } = 10;
        
        /// <summary>
        /// X offset.
        /// </summary>
        public double OffsetX { get; set; } = 5;
        
        /// <summary>
        /// Y offset.
        /// </summary>
        public double OffsetY { get; set; } = 5;
    }

    /// <summary>
    /// Border/stroke settings.
    /// </summary>
    public class BorderSettings
    {
        /// <summary>
        /// Whether border is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }
        
        /// <summary>
        /// Border color (hex).
        /// </summary>
        public string Color { get; set; } = "#FFFFFF";
        
        /// <summary>
        /// Border width in pixels.
        /// </summary>
        public double Width { get; set; } = 2;
        
        /// <summary>
        /// Corner radius.
        /// </summary>
        public double CornerRadius { get; set; }
    }

    /// <summary>
    /// Export profile for video output.
    /// </summary>
    public class ExportProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Output width in pixels.
        /// </summary>
        public int Width { get; set; } = 1920;
        
        /// <summary>
        /// Output height in pixels.
        /// </summary>
        public int Height { get; set; } = 1080;
        
        /// <summary>
        /// Frame rate.
        /// </summary>
        public double Fps { get; set; } = 30;

        /// <summary>
        /// Alias for Fps.
        /// </summary>
        [JsonIgnore]
        public double FrameRate
        {
            get => Fps;
            set => Fps = value;
        }
        
        /// <summary>
        /// Whether HDR is enabled.
        /// </summary>
        public bool Hdr { get; set; }

        /// <summary>
        /// Alias for Hdr.
        /// </summary>
        [JsonIgnore]
        public bool IsHdr
        {
            get => Hdr;
            set => Hdr = value;
        }
        
        /// <summary>
        /// Video bitrate (e.g., "10M", "60M").
        /// </summary>
        public string Bitrate { get; set; } = "10M";

        /// <summary>
        /// Video bitrate in kbps (parsed from Bitrate string).
        /// </summary>
        [JsonIgnore]
        public int VideoBitrate
        {
            get
            {
                if (string.IsNullOrEmpty(Bitrate)) return 10000;
                var s = Bitrate.ToUpperInvariant().Trim();
                if (s.EndsWith("M"))
                {
                    if (double.TryParse(s.TrimEnd('M'), out var mbps))
                        return (int)(mbps * 1000);
                }
                if (s.EndsWith("K"))
                {
                    if (int.TryParse(s.TrimEnd('K'), out var kbps))
                        return kbps;
                }
                if (int.TryParse(s, out var val))
                    return val;
                return 10000;
            }
            set => Bitrate = $"{value}k";
        }

        /// <summary>
        /// CRF value for quality-based encoding (-1 = use bitrate).
        /// </summary>
        public int Crf { get; set; } = -1;

        /// <summary>
        /// Pixel format (e.g., "yuv420p", "yuv420p10le").
        /// </summary>
        public string PixelFormat { get; set; } = "yuv420p";

        /// <summary>
        /// Number of audio channels.
        /// </summary>
        public int AudioChannels { get; set; } = 2;

        /// <summary>
        /// Whether to embed captions in the output.
        /// </summary>
        public bool EmbedCaptions { get; set; } = true;
        
        /// <summary>
        /// Video codec (h264, h265, av1).
        /// </summary>
        public string VideoCodec { get; set; } = "h264";
        
        /// <summary>
        /// Audio codec (aac, opus, flac).
        /// </summary>
        public string AudioCodec { get; set; } = "aac";
        
        /// <summary>
        /// Audio bitrate (e.g., "192k").
        /// </summary>
        public string AudioBitrateStr { get; set; } = "192k";

        /// <summary>
        /// Audio bitrate in kbps.
        /// </summary>
        [JsonIgnore]
        public int AudioBitrate
        {
            get
            {
                if (string.IsNullOrEmpty(AudioBitrateStr)) return 192;
                var s = AudioBitrateStr.ToUpperInvariant().Trim().TrimEnd('K');
                if (int.TryParse(s, out var val)) return val;
                return 192;
            }
            set => AudioBitrateStr = $"{value}k";
        }
        
        /// <summary>
        /// Audio sample rate.
        /// </summary>
        public int AudioSampleRate { get; set; } = 48000;
        
        /// <summary>
        /// Output container format.
        /// </summary>
        public string Container { get; set; } = "mp4";
        
        /// <summary>
        /// Hardware acceleration method.
        /// </summary>
        public HardwareAcceleration HwAccel { get; set; } = HardwareAcceleration.Auto;
        
        /// <summary>
        /// Encoding preset (slower = better quality).
        /// </summary>
        public EncodingPreset Preset { get; set; } = EncodingPreset.Medium;
        
        /// <summary>
        /// Color space.
        /// </summary>
        public string ColorSpace { get; set; } = "bt709";
    }

    /// <summary>
    /// Hardware acceleration options.
    /// </summary>
    public enum HardwareAcceleration
    {
        None,
        Auto,
        Nvenc,      // NVIDIA
        Qsv,        // Intel Quick Sync
        Amf,        // AMD
        VideoToolbox // macOS
    }

    /// <summary>
    /// Encoding preset.
    /// </summary>
    public enum EncodingPreset
    {
        Ultrafast,
        Superfast,
        Veryfast,
        Faster,
        Fast,
        Medium,
        Slow,
        Slower,
        Veryslow,
        Placebo
    }

    /// <summary>
    /// Predefined export profiles.
    /// </summary>
    public static class ExportProfiles
    {
        public static ExportProfile HD720p30 => new()
        {
            Name = "HD 720p 30fps",
            Width = 1280,
            Height = 720,
            Fps = 30,
            Bitrate = "5M"
        };

        public static ExportProfile HD1080p30 => new()
        {
            Name = "Full HD 1080p 30fps",
            Width = 1920,
            Height = 1080,
            Fps = 30,
            Bitrate = "10M"
        };

        public static ExportProfile HD1080p60 => new()
        {
            Name = "Full HD 1080p 60fps",
            Width = 1920,
            Height = 1080,
            Fps = 60,
            Bitrate = "15M"
        };

        public static ExportProfile UHD4K30 => new()
        {
            Name = "4K UHD 30fps",
            Width = 3840,
            Height = 2160,
            Fps = 30,
            Bitrate = "35M"
        };

        public static ExportProfile UHD4K60 => new()
        {
            Name = "4K UHD 60fps",
            Width = 3840,
            Height = 2160,
            Fps = 60,
            Bitrate = "60M"
        };

        public static ExportProfile UHD4K60HDR => new()
        {
            Name = "4K UHD 60fps HDR",
            Width = 3840,
            Height = 2160,
            Fps = 60,
            Bitrate = "80M",
            Hdr = true,
            VideoCodec = "h265",
            ColorSpace = "bt2020"
        };

        public static ExportProfile Instagram => new()
        {
            Name = "Instagram Reels (9:16)",
            Width = 1080,
            Height = 1920,
            Fps = 30,
            Bitrate = "8M"
        };

        public static ExportProfile YouTube => new()
        {
            Name = "YouTube (16:9)",
            Width = 1920,
            Height = 1080,
            Fps = 30,
            Bitrate = "12M"
        };

        public static ExportProfile TikTok => new()
        {
            Name = "TikTok (9:16)",
            Width = 1080,
            Height = 1920,
            Fps = 30,
            Bitrate = "8M"
        };

        public static List<ExportProfile> All => new()
        {
            HD720p30,
            HD1080p30,
            HD1080p60,
            UHD4K30,
            UHD4K60,
            UHD4K60HDR,
            Instagram,
            YouTube,
            TikTok
        };
    }
}
