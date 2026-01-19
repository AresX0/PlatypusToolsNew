using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Export presets matching Shotcut's export options.
    /// Includes presets for YouTube, social media, devices, and professional formats.
    /// </summary>
    public static class ExportPresets
    {
        private static List<ExportPreset>? _allPresets;

        /// <summary>
        /// Gets all available export presets.
        /// </summary>
        public static IReadOnlyList<ExportPreset> GetAll()
        {
            _allPresets ??= CreateAllPresets();
            return _allPresets;
        }

        /// <summary>
        /// Gets presets by category.
        /// </summary>
        public static IEnumerable<ExportPreset> GetByCategory(ExportCategory category)
        {
            return GetAll().Where(p => p.Category == category);
        }

        /// <summary>
        /// Gets a preset by name.
        /// </summary>
        public static ExportPreset? GetByName(string name)
        {
            return GetAll().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static List<ExportPreset> CreateAllPresets()
        {
            var presets = new List<ExportPreset>();

            // === STOCK PRESETS ===
            presets.Add(new ExportPreset
            {
                Name = "Default",
                Category = ExportCategory.Stock,
                Description = "Automatic settings based on source",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Quality = ExportQuality.High,
                Container = "mp4"
            });

            // === YOUTUBE / WEB ===
            presets.Add(new ExportPreset
            {
                Name = "YouTube HD 1080p",
                Category = ExportCategory.Web,
                Description = "Optimized for YouTube at 1080p",
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "12M",
                AudioBitrate = "384k",
                AudioSampleRate = 48000,
                Profile = "high",
                Level = "4.2",
                Container = "mp4",
                PixelFormat = "yuv420p",
                Quality = ExportQuality.High,
                TwoPass = true
            });

            presets.Add(new ExportPreset
            {
                Name = "YouTube 4K",
                Category = ExportCategory.Web,
                Description = "Optimized for YouTube at 4K UHD",
                Width = 3840,
                Height = 2160,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "45M",
                AudioBitrate = "384k",
                AudioSampleRate = 48000,
                Profile = "high",
                Level = "5.1",
                Container = "mp4",
                PixelFormat = "yuv420p",
                Quality = ExportQuality.High,
                TwoPass = true
            });

            presets.Add(new ExportPreset
            {
                Name = "YouTube Shorts / TikTok",
                Category = ExportCategory.Web,
                Description = "Vertical video for shorts",
                Width = 1080,
                Height = 1920,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "8M",
                AudioBitrate = "256k",
                Container = "mp4",
                Quality = ExportQuality.High
            });

            presets.Add(new ExportPreset
            {
                Name = "Vimeo HD",
                Category = ExportCategory.Web,
                Description = "Optimized for Vimeo",
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "20M",
                AudioBitrate = "320k",
                Profile = "high",
                Container = "mp4",
                Quality = ExportQuality.High
            });

            presets.Add(new ExportPreset
            {
                Name = "Twitter / X",
                Category = ExportCategory.Web,
                Description = "Optimized for Twitter",
                Width = 1280,
                Height = 720,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "5M",
                AudioBitrate = "128k",
                Container = "mp4",
                MaxDuration = TimeSpan.FromMinutes(2.5),
                Quality = ExportQuality.Medium
            });

            presets.Add(new ExportPreset
            {
                Name = "Instagram Reels",
                Category = ExportCategory.Web,
                Description = "Vertical video for Instagram",
                Width = 1080,
                Height = 1920,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "6M",
                AudioBitrate = "256k",
                Container = "mp4",
                MaxDuration = TimeSpan.FromSeconds(90),
                Quality = ExportQuality.Medium
            });

            presets.Add(new ExportPreset
            {
                Name = "Instagram Feed (Square)",
                Category = ExportCategory.Web,
                Description = "Square video for Instagram feed",
                Width = 1080,
                Height = 1080,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "6M",
                AudioBitrate = "256k",
                Container = "mp4",
                MaxDuration = TimeSpan.FromMinutes(1),
                Quality = ExportQuality.Medium
            });

            presets.Add(new ExportPreset
            {
                Name = "Discord",
                Category = ExportCategory.Web,
                Description = "Small file size for Discord (< 8MB)",
                Width = 1280,
                Height = 720,
                FrameRate = 30,
                VideoCodec = "libx264",
                AudioCodec = "aac",
                VideoBitrate = "1M",
                AudioBitrate = "96k",
                Container = "mp4",
                Crf = 28,
                Quality = ExportQuality.Low
            });

            // === H.264 PRESETS ===
            presets.Add(new ExportPreset
            {
                Name = "H.264 High Quality",
                Category = ExportCategory.H264,
                Description = "High quality H.264 encoding",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Crf = 18,
                Profile = "high",
                Preset = "slow",
                Container = "mp4",
                Quality = ExportQuality.High
            });

            presets.Add(new ExportPreset
            {
                Name = "H.264 Main Profile",
                Category = ExportCategory.H264,
                Description = "Compatible H.264 for most devices",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Crf = 23,
                Profile = "main",
                Preset = "medium",
                Container = "mp4",
                Quality = ExportQuality.Medium
            });

            presets.Add(new ExportPreset
            {
                Name = "H.264 Baseline (Mobile)",
                Category = ExportCategory.H264,
                Description = "Maximum compatibility for older devices",
                VideoCodec = "libx264",
                AudioCodec = "aac",
                Crf = 23,
                Profile = "baseline",
                Level = "3.0",
                Preset = "fast",
                Container = "mp4",
                Quality = ExportQuality.Medium
            });

            // === H.265 / HEVC PRESETS ===
            presets.Add(new ExportPreset
            {
                Name = "H.265 High Quality",
                Category = ExportCategory.H265,
                Description = "HEVC with excellent compression",
                VideoCodec = "libx265",
                AudioCodec = "aac",
                Crf = 22,
                Preset = "slow",
                Container = "mp4",
                Quality = ExportQuality.High
            });

            presets.Add(new ExportPreset
            {
                Name = "H.265 Main",
                Category = ExportCategory.H265,
                Description = "Balanced HEVC encoding",
                VideoCodec = "libx265",
                AudioCodec = "aac",
                Crf = 26,
                Preset = "medium",
                Container = "mp4",
                Quality = ExportQuality.Medium
            });

            presets.Add(new ExportPreset
            {
                Name = "H.265 HDR (10-bit)",
                Category = ExportCategory.H265,
                Description = "HDR video with 10-bit color",
                VideoCodec = "libx265",
                AudioCodec = "aac",
                Crf = 22,
                PixelFormat = "yuv420p10le",
                Container = "mp4",
                Quality = ExportQuality.High,
                ExtraParams = "-x265-params hdr-opt=1:repeat-headers=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc"
            });

            // === AV1 PRESETS ===
            presets.Add(new ExportPreset
            {
                Name = "AV1 High Quality",
                Category = ExportCategory.AV1,
                Description = "AV1 with best compression (slow)",
                VideoCodec = "libaom-av1",
                AudioCodec = "libopus",
                Crf = 30,
                Container = "webm",
                Quality = ExportQuality.High,
                ExtraParams = "-cpu-used 4 -row-mt 1"
            });

            presets.Add(new ExportPreset
            {
                Name = "AV1 SVT (Fast)",
                Category = ExportCategory.AV1,
                Description = "Fast AV1 encoding with SVT-AV1",
                VideoCodec = "libsvtav1",
                AudioCodec = "libopus",
                Crf = 35,
                Container = "webm",
                Quality = ExportQuality.Medium,
                ExtraParams = "-preset 6"
            });

            // === VP9 / WEBM ===
            presets.Add(new ExportPreset
            {
                Name = "VP9 WebM",
                Category = ExportCategory.WebM,
                Description = "VP9 for web streaming",
                VideoCodec = "libvpx-vp9",
                AudioCodec = "libopus",
                Crf = 31,
                Container = "webm",
                Quality = ExportQuality.Medium
            });

            // === PRORES ===
            presets.Add(new ExportPreset
            {
                Name = "ProRes 422",
                Category = ExportCategory.ProRes,
                Description = "Apple ProRes 422 for editing",
                VideoCodec = "prores_ks",
                AudioCodec = "pcm_s16le",
                Container = "mov",
                Quality = ExportQuality.High,
                ExtraParams = "-profile:v 2"
            });

            presets.Add(new ExportPreset
            {
                Name = "ProRes 422 HQ",
                Category = ExportCategory.ProRes,
                Description = "High quality ProRes for grading",
                VideoCodec = "prores_ks",
                AudioCodec = "pcm_s24le",
                Container = "mov",
                Quality = ExportQuality.High,
                ExtraParams = "-profile:v 3"
            });

            presets.Add(new ExportPreset
            {
                Name = "ProRes 4444",
                Category = ExportCategory.ProRes,
                Description = "ProRes with alpha channel",
                VideoCodec = "prores_ks",
                AudioCodec = "pcm_s24le",
                Container = "mov",
                Quality = ExportQuality.High,
                ExtraParams = "-profile:v 4 -pix_fmt yuva444p10le"
            });

            // === DNxHD / DNxHR ===
            presets.Add(new ExportPreset
            {
                Name = "DNxHD 145",
                Category = ExportCategory.DNx,
                Description = "Avid DNxHD 145 Mbps",
                VideoCodec = "dnxhd",
                AudioCodec = "pcm_s16le",
                VideoBitrate = "145M",
                Container = "mxf",
                Quality = ExportQuality.High
            });

            presets.Add(new ExportPreset
            {
                Name = "DNxHR HQ",
                Category = ExportCategory.DNx,
                Description = "DNxHR High Quality",
                VideoCodec = "dnxhd",
                AudioCodec = "pcm_s16le",
                Container = "mxf",
                Quality = ExportQuality.High,
                ExtraParams = "-profile:v dnxhr_hq"
            });

            // === LOSSLESS ===
            presets.Add(new ExportPreset
            {
                Name = "FFV1 Lossless",
                Category = ExportCategory.Lossless,
                Description = "Lossless archival format",
                VideoCodec = "ffv1",
                AudioCodec = "flac",
                Container = "mkv",
                Quality = ExportQuality.Lossless,
                ExtraParams = "-level 3 -coder 1 -context 1 -slicecrc 1"
            });

            presets.Add(new ExportPreset
            {
                Name = "H.264 Lossless",
                Category = ExportCategory.Lossless,
                Description = "Lossless H.264 (large files)",
                VideoCodec = "libx264",
                AudioCodec = "flac",
                Container = "mkv",
                Quality = ExportQuality.Lossless,
                ExtraParams = "-crf 0 -preset ultrafast"
            });

            // === HARDWARE ENCODING ===
            presets.Add(new ExportPreset
            {
                Name = "NVIDIA NVENC H.264",
                Category = ExportCategory.Hardware,
                Description = "GPU-accelerated H.264 (NVIDIA)",
                VideoCodec = "h264_nvenc",
                AudioCodec = "aac",
                Container = "mp4",
                Quality = ExportQuality.High,
                ExtraParams = "-preset p4 -rc vbr -cq 23"
            });

            presets.Add(new ExportPreset
            {
                Name = "NVIDIA NVENC H.265",
                Category = ExportCategory.Hardware,
                Description = "GPU-accelerated HEVC (NVIDIA)",
                VideoCodec = "hevc_nvenc",
                AudioCodec = "aac",
                Container = "mp4",
                Quality = ExportQuality.High,
                ExtraParams = "-preset p4 -rc vbr -cq 25"
            });

            presets.Add(new ExportPreset
            {
                Name = "AMD AMF H.264",
                Category = ExportCategory.Hardware,
                Description = "GPU-accelerated H.264 (AMD)",
                VideoCodec = "h264_amf",
                AudioCodec = "aac",
                Container = "mp4",
                Quality = ExportQuality.High,
                ExtraParams = "-quality balanced"
            });

            presets.Add(new ExportPreset
            {
                Name = "Intel QuickSync H.264",
                Category = ExportCategory.Hardware,
                Description = "GPU-accelerated H.264 (Intel)",
                VideoCodec = "h264_qsv",
                AudioCodec = "aac",
                Container = "mp4",
                Quality = ExportQuality.High,
                ExtraParams = "-preset medium"
            });

            // === GIF / ANIMATED ===
            presets.Add(new ExportPreset
            {
                Name = "Animated GIF",
                Category = ExportCategory.Animated,
                Description = "GIF animation",
                VideoCodec = "gif",
                AudioCodec = "",
                FrameRate = 15,
                Width = 480,
                Height = 270,
                Container = "gif",
                Quality = ExportQuality.Low
            });

            presets.Add(new ExportPreset
            {
                Name = "Animated WebP",
                Category = ExportCategory.Animated,
                Description = "Animated WebP image",
                VideoCodec = "libwebp",
                AudioCodec = "",
                Container = "webp",
                Quality = ExportQuality.Medium
            });

            // === AUDIO ONLY ===
            presets.Add(new ExportPreset
            {
                Name = "MP3 320kbps",
                Category = ExportCategory.AudioOnly,
                Description = "High quality MP3",
                AudioCodec = "libmp3lame",
                AudioBitrate = "320k",
                AudioSampleRate = 48000,
                Container = "mp3",
                VideoCodec = ""
            });

            presets.Add(new ExportPreset
            {
                Name = "AAC 256kbps",
                Category = ExportCategory.AudioOnly,
                Description = "High quality AAC",
                AudioCodec = "aac",
                AudioBitrate = "256k",
                AudioSampleRate = 48000,
                Container = "m4a",
                VideoCodec = ""
            });

            presets.Add(new ExportPreset
            {
                Name = "FLAC Lossless",
                Category = ExportCategory.AudioOnly,
                Description = "Lossless audio",
                AudioCodec = "flac",
                AudioSampleRate = 48000,
                Container = "flac",
                VideoCodec = ""
            });

            presets.Add(new ExportPreset
            {
                Name = "WAV (PCM)",
                Category = ExportCategory.AudioOnly,
                Description = "Uncompressed WAV",
                AudioCodec = "pcm_s16le",
                AudioSampleRate = 48000,
                Container = "wav",
                VideoCodec = ""
            });

            // === IMAGE SEQUENCE ===
            presets.Add(new ExportPreset
            {
                Name = "PNG Sequence",
                Category = ExportCategory.ImageSequence,
                Description = "PNG image sequence",
                VideoCodec = "png",
                Container = "png",
                Quality = ExportQuality.Lossless,
                IsImageSequence = true
            });

            presets.Add(new ExportPreset
            {
                Name = "JPEG Sequence",
                Category = ExportCategory.ImageSequence,
                Description = "JPEG image sequence",
                VideoCodec = "mjpeg",
                Container = "jpg",
                Quality = ExportQuality.High,
                IsImageSequence = true,
                ExtraParams = "-q:v 2"
            });

            presets.Add(new ExportPreset
            {
                Name = "EXR Sequence (HDR)",
                Category = ExportCategory.ImageSequence,
                Description = "OpenEXR sequence for VFX",
                VideoCodec = "exr",
                Container = "exr",
                Quality = ExportQuality.Lossless,
                IsImageSequence = true
            });

            return presets;
        }
    }

    /// <summary>
    /// An export preset configuration.
    /// </summary>
    public class ExportPreset
    {
        public string Name { get; set; } = string.Empty;
        public ExportCategory Category { get; set; }
        public string Description { get; set; } = string.Empty;
        
        // Video settings
        public string VideoCodec { get; set; } = "libx264";
        public int? Width { get; set; }
        public int? Height { get; set; }
        public double? FrameRate { get; set; }
        public string? VideoBitrate { get; set; }
        public int? Crf { get; set; }
        public string? Profile { get; set; }
        public string? Level { get; set; }
        public string? Preset { get; set; }
        public string PixelFormat { get; set; } = "yuv420p";
        
        // Audio settings
        public string AudioCodec { get; set; } = "aac";
        public string? AudioBitrate { get; set; }
        public int AudioSampleRate { get; set; } = 48000;
        public int AudioChannels { get; set; } = 2;
        
        // Container
        public string Container { get; set; } = "mp4";
        
        // Format helpers
        /// <summary>
        /// Gets the format name (derived from container).
        /// </summary>
        public string Format => Container.ToUpperInvariant() switch
        {
            "MP4" => "MPEG-4 Video",
            "MOV" => "QuickTime Movie",
            "MKV" => "Matroska Video",
            "WEBM" => "WebM Video",
            "AVI" => "AVI Video",
            "GIF" => "Animated GIF",
            "MP3" => "MP3 Audio",
            "WAV" => "WAV Audio",
            "FLAC" => "FLAC Audio",
            "PNG" => "PNG Image Sequence",
            "JPG" or "JPEG" => "JPEG Image Sequence",
            _ => Container.ToUpperInvariant()
        };
        
        /// <summary>
        /// Gets the file extension (derived from container).
        /// </summary>
        public string Extension => Container.ToLowerInvariant();
        
        // Quality
        public ExportQuality Quality { get; set; } = ExportQuality.Medium;
        
        // Special options
        public bool TwoPass { get; set; }
        public bool IsImageSequence { get; set; }
        public TimeSpan? MaxDuration { get; set; }
        public string? ExtraParams { get; set; }

        /// <summary>
        /// Builds FFmpeg arguments from this preset.
        /// </summary>
        public string BuildFFmpegArgs(int sourceWidth, int sourceHeight, string outputPath)
        {
            var args = new List<string>();

            // Video codec
            if (!string.IsNullOrEmpty(VideoCodec))
            {
                args.Add($"-c:v {VideoCodec}");

                // Resolution
                var width = Width ?? sourceWidth;
                var height = Height ?? sourceHeight;
                // Ensure even dimensions
                width = (width / 2) * 2;
                height = (height / 2) * 2;
                args.Add($"-s {width}x{height}");

                // Frame rate
                if (FrameRate.HasValue)
                    args.Add($"-r {FrameRate}");

                // Quality settings
                if (!string.IsNullOrEmpty(VideoBitrate))
                    args.Add($"-b:v {VideoBitrate}");
                else if (Crf.HasValue)
                    args.Add($"-crf {Crf}");

                // Codec options
                if (!string.IsNullOrEmpty(Profile))
                    args.Add($"-profile:v {Profile}");
                if (!string.IsNullOrEmpty(Level))
                    args.Add($"-level {Level}");
                if (!string.IsNullOrEmpty(Preset))
                    args.Add($"-preset {Preset}");
                if (!string.IsNullOrEmpty(PixelFormat))
                    args.Add($"-pix_fmt {PixelFormat}");
            }
            else
            {
                args.Add("-vn"); // No video
            }

            // Audio codec
            if (!string.IsNullOrEmpty(AudioCodec))
            {
                args.Add($"-c:a {AudioCodec}");
                
                if (!string.IsNullOrEmpty(AudioBitrate))
                    args.Add($"-b:a {AudioBitrate}");
                
                args.Add($"-ar {AudioSampleRate}");
                args.Add($"-ac {AudioChannels}");
            }
            else
            {
                args.Add("-an"); // No audio
            }

            // Extra params
            if (!string.IsNullOrEmpty(ExtraParams))
                args.Add(ExtraParams);

            // Output
            args.Add($"-y \"{outputPath}\"");

            return string.Join(" ", args);
        }
    }

    /// <summary>
    /// Export preset categories.
    /// </summary>
    public enum ExportCategory
    {
        Stock,
        Web,
        H264,
        H265,
        AV1,
        WebM,
        ProRes,
        DNx,
        Lossless,
        Hardware,
        Animated,
        AudioOnly,
        ImageSequence
    }

    /// <summary>
    /// Export quality levels.
    /// </summary>
    public enum ExportQuality
    {
        Low,
        Medium,
        High,
        Lossless
    }
}
