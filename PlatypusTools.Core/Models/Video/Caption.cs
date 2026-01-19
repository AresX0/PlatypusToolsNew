using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a caption/subtitle entry for auto-captions feature.
    /// </summary>
    public class Caption
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Start time of the caption.
        /// </summary>
        public TimeSpan StartTime { get; set; }
        
        /// <summary>
        /// End time of the caption.
        /// </summary>
        public TimeSpan EndTime { get; set; }
        
        /// <summary>
        /// Caption text content.
        /// </summary>
        public string Text { get; set; } = string.Empty;
        
        /// <summary>
        /// Speaker identifier (for multi-speaker scenarios).
        /// </summary>
        public string Speaker { get; set; } = string.Empty;
        
        /// <summary>
        /// Confidence score from STT (0-1).
        /// </summary>
        public double Confidence { get; set; } = 1.0;
        
        /// <summary>
        /// Word-level timings for karaoke-style display.
        /// </summary>
        public List<CaptionWord> Words { get; set; } = new();
        
        /// <summary>
        /// Style preset name.
        /// </summary>
        public string StylePreset { get; set; } = "Default";
        
        /// <summary>
        /// Whether this caption is selected in the UI.
        /// </summary>
        public bool IsSelected { get; set; }
        
        /// <summary>
        /// Duration of the caption.
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;
    }

    /// <summary>
    /// Word-level timing for synchronized caption display.
    /// </summary>
    public class CaptionWord
    {
        /// <summary>
        /// The word text.
        /// </summary>
        public string Word { get; set; } = string.Empty;

        /// <summary>
        /// Alias for Word property.
        /// </summary>
        public string Text
        {
            get => Word;
            set => Word = value;
        }
        
        /// <summary>
        /// Start time of the word.
        /// </summary>
        public TimeSpan StartTime { get; set; }
        
        /// <summary>
        /// End time of the word.
        /// </summary>
        public TimeSpan EndTime { get; set; }
        
        /// <summary>
        /// Confidence score (0-1).
        /// </summary>
        public double Confidence { get; set; } = 1.0;
    }

    /// <summary>
    /// Style settings for caption display.
    /// </summary>
    public class CaptionStyle
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Display name for the style.
        /// </summary>
        public string Name { get; set; } = "Default";
        
        /// <summary>
        /// Font family name.
        /// </summary>
        public string FontFamily { get; set; } = "Segoe UI";
        
        /// <summary>
        /// Font size in points.
        /// </summary>
        public double FontSize { get; set; } = 48;
        
        /// <summary>
        /// Font weight (Normal, Bold, etc.).
        /// </summary>
        public string FontWeight { get; set; } = "Bold";
        
        /// <summary>
        /// Text color (hex).
        /// </summary>
        public string TextColor { get; set; } = "#FFFFFF";
        
        /// <summary>
        /// Background color (hex, with alpha).
        /// </summary>
        public string BackgroundColor { get; set; } = "#80000000";
        
        /// <summary>
        /// Outline/stroke color.
        /// </summary>
        public string OutlineColor { get; set; } = "#000000";
        
        /// <summary>
        /// Outline width in pixels.
        /// </summary>
        public double OutlineWidth { get; set; } = 2;
        
        /// <summary>
        /// Shadow offset X.
        /// </summary>
        public double ShadowX { get; set; } = 2;
        
        /// <summary>
        /// Shadow offset Y.
        /// </summary>
        public double ShadowY { get; set; } = 2;
        
        /// <summary>
        /// Shadow color.
        /// </summary>
        public string ShadowColor { get; set; } = "#80000000";
        
        /// <summary>
        /// Vertical position (0 = top, 0.5 = middle, 1 = bottom).
        /// </summary>
        public double VerticalPosition { get; set; } = 0.9;
        
        /// <summary>
        /// Text alignment.
        /// </summary>
        public string Alignment { get; set; } = "Center";
        
        /// <summary>
        /// Margin from edges in percentage.
        /// </summary>
        public double Margin { get; set; } = 0.05;
        
        /// <summary>
        /// Whether to use word-by-word highlight.
        /// </summary>
        public bool WordHighlight { get; set; }
        
        /// <summary>
        /// Highlight color for current word.
        /// </summary>
        public string HighlightColor { get; set; } = "#FFD700";
    }

    /// <summary>
    /// Caption track containing multiple captions.
    /// </summary>
    public class CaptionTrack
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Track name.
        /// </summary>
        public string Name { get; set; } = "Captions";
        
        /// <summary>
        /// Language code (e.g., "en", "es", "ja").
        /// </summary>
        public string Language { get; set; } = "en";
        
        /// <summary>
        /// All captions in this track.
        /// </summary>
        public List<Caption> Captions { get; set; } = new();
        
        /// <summary>
        /// Style applied to this track.
        /// </summary>
        public CaptionStyle Style { get; set; } = new();
        
        /// <summary>
        /// Whether this track is visible.
        /// </summary>
        public bool IsVisible { get; set; } = true;
    }

    /// <summary>
    /// SRT import/export helper.
    /// </summary>
    public static class SrtHelper
    {
        /// <summary>
        /// Parse SRT file content into captions.
        /// </summary>
        public static List<Caption> Parse(string srtContent) => ParseSrt(srtContent);

        /// <summary>
        /// Export captions to SRT format.
        /// </summary>
        public static string Export(List<Caption> captions) => ExportSrt(captions);

        /// <summary>
        /// Parse SRT file content into captions.
        /// </summary>
        public static List<Caption> ParseSrt(string srtContent)
        {
            var captions = new List<Caption>();
            var lines = srtContent.Split('\n');
            
            int i = 0;
            while (i < lines.Length)
            {
                // Skip empty lines
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                    i++;
                
                if (i >= lines.Length) break;
                
                // Skip sequence number
                if (int.TryParse(lines[i].Trim(), out _))
                    i++;
                
                if (i >= lines.Length) break;
                
                // Parse timecode line
                var timeLine = lines[i].Trim();
                if (timeLine.Contains("-->"))
                {
                    var parts = timeLine.Split(new[] { "-->" }, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        var caption = new Caption
                        {
                            StartTime = ParseSrtTime(parts[0].Trim()),
                            EndTime = ParseSrtTime(parts[1].Trim())
                        };
                        
                        i++;
                        
                        // Collect text lines until empty line
                        var textLines = new List<string>();
                        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                        {
                            textLines.Add(lines[i].Trim());
                            i++;
                        }
                        
                        caption.Text = string.Join("\n", textLines);
                        captions.Add(caption);
                    }
                }
                else
                {
                    i++;
                }
            }
            
            return captions;
        }

        /// <summary>
        /// Export captions to SRT format.
        /// </summary>
        public static string ExportSrt(List<Caption> captions)
        {
            var sb = new System.Text.StringBuilder();
            
            for (int i = 0; i < captions.Count; i++)
            {
                var cap = captions[i];
                sb.AppendLine((i + 1).ToString());
                sb.AppendLine($"{FormatSrtTime(cap.StartTime)} --> {FormatSrtTime(cap.EndTime)}");
                sb.AppendLine(cap.Text);
                sb.AppendLine();
            }
            
            return sb.ToString();
        }

        private static TimeSpan ParseSrtTime(string time)
        {
            // Format: 00:00:00,000
            time = time.Replace(',', '.');
            if (TimeSpan.TryParse(time, out var result))
                return result;
            return TimeSpan.Zero;
        }

        private static string FormatSrtTime(TimeSpan time)
        {
            return $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
        }
    }
}
