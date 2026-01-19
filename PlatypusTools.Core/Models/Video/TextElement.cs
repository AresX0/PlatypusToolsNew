using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a text/title element on the timeline.
    /// Shotcut-style text with rich formatting and animation support.
    /// </summary>
    public class TextElement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// The text content (supports multi-line).
        /// </summary>
        public string Text { get; set; } = string.Empty;
        
        /// <summary>
        /// Font family name.
        /// </summary>
        public string FontFamily { get; set; } = "Arial";
        
        /// <summary>
        /// Font size in points.
        /// </summary>
        public double FontSize { get; set; } = 72;
        
        /// <summary>
        /// Font weight (100-900, 400=normal, 700=bold).
        /// </summary>
        public int FontWeight { get; set; } = 400;
        
        /// <summary>
        /// Whether the text is italic.
        /// </summary>
        public bool IsItalic { get; set; }
        
        /// <summary>
        /// Whether the text is underlined.
        /// </summary>
        public bool IsUnderline { get; set; }
        
        /// <summary>
        /// Text color in hex format.
        /// </summary>
        public string Color { get; set; } = "#FFFFFF";
        
        /// <summary>
        /// Text opacity (0-1).
        /// </summary>
        public double Opacity { get; set; } = 1.0;
        
        /// <summary>
        /// Background color (null = transparent).
        /// </summary>
        public string? BackgroundColor { get; set; }
        
        /// <summary>
        /// Background padding in pixels.
        /// </summary>
        public double BackgroundPadding { get; set; } = 10;
        
        /// <summary>
        /// Background corner radius.
        /// </summary>
        public double BackgroundCornerRadius { get; set; } = 0;
        
        /// <summary>
        /// Outline/stroke color.
        /// </summary>
        public string? OutlineColor { get; set; } = "#000000";
        
        /// <summary>
        /// Outline thickness in pixels.
        /// </summary>
        public double OutlineWidth { get; set; } = 2;
        
        /// <summary>
        /// Shadow color.
        /// </summary>
        public string? ShadowColor { get; set; }
        
        /// <summary>
        /// Shadow offset X.
        /// </summary>
        public double ShadowOffsetX { get; set; } = 3;
        
        /// <summary>
        /// Shadow offset Y.
        /// </summary>
        public double ShadowOffsetY { get; set; } = 3;
        
        /// <summary>
        /// Shadow blur radius.
        /// </summary>
        public double ShadowBlur { get; set; } = 5;
        
        /// <summary>
        /// Horizontal alignment.
        /// </summary>
        public TextAlignment HorizontalAlignment { get; set; } = TextAlignment.Center;
        
        /// <summary>
        /// Vertical alignment.
        /// </summary>
        public TextAlignment VerticalAlignment { get; set; } = TextAlignment.Center;
        
        /// <summary>
        /// Position X (0-1, relative to frame).
        /// </summary>
        public double PositionX { get; set; } = 0.5;
        
        /// <summary>
        /// Position Y (0-1, relative to frame).
        /// </summary>
        public double PositionY { get; set; } = 0.5;
        
        /// <summary>
        /// Rotation in degrees.
        /// </summary>
        public double Rotation { get; set; }
        
        /// <summary>
        /// Scale factor.
        /// </summary>
        public double Scale { get; set; } = 1.0;
        
        /// <summary>
        /// Preset style applied.
        /// </summary>
        public string? PresetName { get; set; }
        
        /// <summary>
        /// Animation for text entrance.
        /// </summary>
        public TextAnimation? InAnimation { get; set; }
        
        /// <summary>
        /// Animation for text exit.
        /// </summary>
        public TextAnimation? OutAnimation { get; set; }
        
        /// <summary>
        /// Per-character animation settings.
        /// </summary>
        public CharacterAnimation? CharacterAnimation { get; set; }
        
        /// <summary>
        /// Keyframes for animated properties.
        /// </summary>
        public List<TextKeyframe> Keyframes { get; set; } = new();
        
        /// <summary>
        /// Letter spacing adjustment.
        /// </summary>
        public double LetterSpacing { get; set; } = 0;
        
        /// <summary>
        /// Line height multiplier.
        /// </summary>
        public double LineHeight { get; set; } = 1.2;
    }

    /// <summary>
    /// Text alignment options.
    /// </summary>
    public enum TextAlignment
    {
        Left,
        Center,
        Right,
        Top,
        Middle,
        Bottom
    }

    /// <summary>
    /// Animation settings for text elements.
    /// </summary>
    public class TextAnimation
    {
        public TextAnimationType Type { get; set; } = TextAnimationType.FadeIn;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(0.5);
        public EasingType Easing { get; set; } = EasingType.EaseOut;
        public double StartValue { get; set; }
        public double EndValue { get; set; } = 1.0;
    }

    /// <summary>
    /// Types of text animations.
    /// </summary>
    public enum TextAnimationType
    {
        // Fade
        FadeIn,
        FadeOut,
        
        // Slide
        SlideUp,
        SlideDown,
        SlideLeft,
        SlideRight,
        
        // Scale
        ZoomIn,
        ZoomOut,
        
        // Rotation
        SpinIn,
        SpinOut,
        
        // Bounce
        BounceIn,
        BounceOut,
        
        // Typewriter
        Typewriter,
        TypewriterCursor,
        
        // Character-based
        WaveIn,
        CharacterFade,
        CharacterScale,
        
        // Special
        GlitchIn,
        FlickerIn,
        BlurIn,
        BlurOut,
        
        // None
        None
    }

    /// <summary>
    /// Per-character animation settings.
    /// </summary>
    public class CharacterAnimation
    {
        public CharacterAnimationType Type { get; set; }
        public double Delay { get; set; } = 0.05; // Delay between characters
        public double Amplitude { get; set; } = 10; // For wave animations
        public double Frequency { get; set; } = 2; // For wave animations
    }

    /// <summary>
    /// Types of per-character animations.
    /// </summary>
    public enum CharacterAnimationType
    {
        None,
        Wave,
        Bounce,
        Shake,
        Fade,
        Scale,
        Rotate
    }

    /// <summary>
    /// Keyframe for animating text properties.
    /// </summary>
    public class TextKeyframe
    {
        public double Time { get; set; }
        public double? PositionX { get; set; }
        public double? PositionY { get; set; }
        public double? Scale { get; set; }
        public double? Rotation { get; set; }
        public double? Opacity { get; set; }
        public string? Color { get; set; }
        public EasingType Easing { get; set; } = EasingType.Linear;
    }

    /// <summary>
    /// Preset text styles for quick application.
    /// </summary>
    public static class TextPresets
    {
        public static TextElement CreateSubtitle() => new()
        {
            FontFamily = "Arial",
            FontSize = 48,
            Color = "#FFFFFF",
            OutlineColor = "#000000",
            OutlineWidth = 2,
            PositionY = 0.85,
            PresetName = "Subtitle"
        };

        public static TextElement CreateTitle() => new()
        {
            FontFamily = "Arial Black",
            FontSize = 96,
            Color = "#FFFFFF",
            ShadowColor = "#80000000",
            ShadowOffsetX = 4,
            ShadowOffsetY = 4,
            ShadowBlur = 8,
            PresetName = "Title",
            InAnimation = new TextAnimation { Type = TextAnimationType.FadeIn, Duration = TimeSpan.FromSeconds(0.5) }
        };

        public static TextElement CreateLowerThird() => new()
        {
            FontFamily = "Segoe UI",
            FontSize = 36,
            Color = "#FFFFFF",
            BackgroundColor = "#CC000000",
            BackgroundPadding = 15,
            PositionX = 0.1,
            PositionY = 0.8,
            HorizontalAlignment = TextAlignment.Left,
            PresetName = "Lower Third",
            InAnimation = new TextAnimation { Type = TextAnimationType.SlideLeft, Duration = TimeSpan.FromSeconds(0.3) }
        };

        public static TextElement CreateCallout() => new()
        {
            FontFamily = "Segoe UI Semibold",
            FontSize = 32,
            Color = "#000000",
            BackgroundColor = "#FFFFFF",
            BackgroundPadding = 20,
            BackgroundCornerRadius = 10,
            PresetName = "Callout"
        };

        public static TextElement CreateSocialHandle() => new()
        {
            FontFamily = "Segoe UI",
            FontSize = 28,
            Color = "#FFFFFF",
            OutlineColor = "#000000",
            OutlineWidth = 1,
            PositionX = 0.9,
            PositionY = 0.95,
            HorizontalAlignment = TextAlignment.Right,
            PresetName = "Social Handle"
        };

        public static TextElement CreateCountdown() => new()
        {
            FontFamily = "Impact",
            FontSize = 200,
            Color = "#FFFFFF",
            OutlineColor = "#FF0000",
            OutlineWidth = 5,
            PresetName = "Countdown",
            InAnimation = new TextAnimation { Type = TextAnimationType.ZoomIn, Duration = TimeSpan.FromSeconds(0.2) },
            OutAnimation = new TextAnimation { Type = TextAnimationType.ZoomOut, Duration = TimeSpan.FromSeconds(0.2) }
        };

        public static TextElement CreateGlitchTitle() => new()
        {
            FontFamily = "Consolas",
            FontSize = 72,
            Color = "#00FF00",
            OutlineColor = "#FF0000",
            OutlineWidth = 2,
            PresetName = "Glitch",
            InAnimation = new TextAnimation { Type = TextAnimationType.GlitchIn, Duration = TimeSpan.FromSeconds(0.5) }
        };

        public static TextElement CreateTypewriter() => new()
        {
            FontFamily = "Courier New",
            FontSize = 48,
            Color = "#00FF00",
            BackgroundColor = "#000000",
            BackgroundPadding = 20,
            PresetName = "Typewriter",
            InAnimation = new TextAnimation { Type = TextAnimationType.Typewriter, Duration = TimeSpan.FromSeconds(2) }
        };
    }
}
