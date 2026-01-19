using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a video/audio filter that can be applied to clips.
    /// Shotcut-style filter with keyframeable parameters.
    /// </summary>
    public class Filter
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Internal name/identifier for the filter.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Display name for the UI.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
        
        /// <summary>
        /// Category of the filter.
        /// </summary>
        public FilterCategory Category { get; set; }
        
        /// <summary>
        /// Whether this is a video or audio filter.
        /// </summary>
        public FilterMediaType MediaType { get; set; } = FilterMediaType.Video;
        
        /// <summary>
        /// Whether the filter is enabled.
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// Order in the filter chain (lower = applied first).
        /// </summary>
        public int Order { get; set; }
        
        /// <summary>
        /// Filter parameters with keyframe support.
        /// </summary>
        public List<FilterParameter> Parameters { get; set; } = new();
        
        /// <summary>
        /// FFmpeg filter name.
        /// </summary>
        public string FFmpegFilterName { get; set; } = string.Empty;
        
        /// <summary>
        /// Description of what the filter does.
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Icon/emoji for the filter.
        /// </summary>
        public string Icon { get; set; } = "🎨";
        
        /// <summary>
        /// Creates a deep copy of the filter.
        /// </summary>
        public Filter Clone()
        {
            return new Filter
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name,
                DisplayName = DisplayName,
                Category = Category,
                MediaType = MediaType,
                IsEnabled = IsEnabled,
                Order = Order,
                FFmpegFilterName = FFmpegFilterName,
                Description = Description,
                Icon = Icon,
                Parameters = Parameters.ConvertAll(p => p.Clone())
            };
        }
    }

    /// <summary>
    /// A parameter for a filter with optional keyframe animation.
    /// </summary>
    public class FilterParameter
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public FilterParameterType Type { get; set; }
        
        /// <summary>
        /// Current value (if not using keyframes).
        /// </summary>
        public object? Value { get; set; }
        
        /// <summary>
        /// Default value.
        /// </summary>
        public object? DefaultValue { get; set; }
        
        /// <summary>
        /// Minimum value (for numeric types).
        /// </summary>
        public double? MinValue { get; set; }
        
        /// <summary>
        /// Maximum value (for numeric types).
        /// </summary>
        public double? MaxValue { get; set; }
        
        /// <summary>
        /// Step size for slider controls.
        /// </summary>
        public double Step { get; set; } = 0.01;
        
        /// <summary>
        /// Unit label (e.g., "px", "%", "°").
        /// </summary>
        public string Unit { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether this parameter supports keyframe animation.
        /// </summary>
        public bool IsKeyframeable { get; set; } = true;
        
        /// <summary>
        /// Keyframes for animated values.
        /// </summary>
        public List<ParameterKeyframe> Keyframes { get; set; } = new();
        
        /// <summary>
        /// Options for enum/dropdown types.
        /// </summary>
        public List<string> Options { get; set; } = new();
        
        /// <summary>
        /// Gets the value at a specific time, interpolating keyframes if present.
        /// </summary>
        public double GetValueAtTime(double time)
        {
            if (Keyframes.Count == 0)
            {
                return Convert.ToDouble(Value ?? DefaultValue ?? 0);
            }
            
            if (Keyframes.Count == 1)
            {
                return Keyframes[0].Value;
            }
            
            // Find surrounding keyframes
            var before = Keyframes.FindLast(k => k.Time <= time);
            var after = Keyframes.Find(k => k.Time > time);
            
            if (before == null) return Keyframes[0].Value;
            if (after == null) return Keyframes[^1].Value;
            
            // Interpolate
            var t = (time - before.Time) / (after.Time - before.Time);
            t = ApplyEasing(t, before.Easing);
            
            return before.Value + (after.Value - before.Value) * t;
        }
        
        private double ApplyEasing(double t, EasingType easing)
        {
            return easing switch
            {
                EasingType.Linear => t,
                EasingType.EaseIn => t * t,
                EasingType.EaseOut => 1 - (1 - t) * (1 - t),
                EasingType.EaseInOut => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
                EasingType.EaseInCubic => t * t * t,
                EasingType.EaseOutCubic => 1 - Math.Pow(1 - t, 3),
                EasingType.EaseInOutCubic => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
                EasingType.EaseInQuart => t * t * t * t,
                EasingType.EaseOutQuart => 1 - Math.Pow(1 - t, 4),
                EasingType.EaseInOutQuart => t < 0.5 ? 8 * t * t * t * t : 1 - Math.Pow(-2 * t + 2, 4) / 2,
                EasingType.EaseInExpo => t == 0 ? 0 : Math.Pow(2, 10 * t - 10),
                EasingType.EaseOutExpo => t == 1 ? 1 : 1 - Math.Pow(2, -10 * t),
                EasingType.EaseInOutExpo => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2,
                EasingType.Bounce => BounceEase(t),
                EasingType.Elastic => ElasticEase(t),
                _ => t
            };
        }
        
        private double BounceEase(double t)
        {
            if (t < 1 / 2.75) return 7.5625 * t * t;
            if (t < 2 / 2.75) return 7.5625 * (t -= 1.5 / 2.75) * t + 0.75;
            if (t < 2.5 / 2.75) return 7.5625 * (t -= 2.25 / 2.75) * t + 0.9375;
            return 7.5625 * (t -= 2.625 / 2.75) * t + 0.984375;
        }
        
        private double ElasticEase(double t)
        {
            const double c4 = (2 * Math.PI) / 3;
            return t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
        }
        
        public FilterParameter Clone()
        {
            return new FilterParameter
            {
                Name = Name,
                DisplayName = DisplayName,
                Type = Type,
                Value = Value,
                DefaultValue = DefaultValue,
                MinValue = MinValue,
                MaxValue = MaxValue,
                Step = Step,
                Unit = Unit,
                IsKeyframeable = IsKeyframeable,
                Options = new List<string>(Options),
                Keyframes = Keyframes.ConvertAll(k => new ParameterKeyframe
                {
                    Time = k.Time,
                    Value = k.Value,
                    Easing = k.Easing
                })
            };
        }
    }

    /// <summary>
    /// A keyframe for a filter parameter.
    /// </summary>
    public class ParameterKeyframe
    {
        /// <summary>
        /// Time in seconds relative to clip start.
        /// </summary>
        public double Time { get; set; }
        
        /// <summary>
        /// Value at this keyframe.
        /// </summary>
        public double Value { get; set; }
        
        /// <summary>
        /// Easing to the next keyframe.
        /// </summary>
        public EasingType Easing { get; set; } = EasingType.Linear;
    }

    /// <summary>
    /// Categories of filters (matching Shotcut's organization).
    /// </summary>
    public enum FilterCategory
    {
        // Video filters
        ColorCorrection,
        ColorGrading,
        Blur,
        Sharpen,
        Distort,
        Generate,
        Stylize,
        Time,
        Transform,
        Overlay,
        Transition,
        
        // Audio filters
        AudioLevel,
        AudioEQ,
        AudioEffect,
        AudioDynamics,
        AudioSpatial,
        
        // Special
        Favorite,
        Custom
    }

    /// <summary>
    /// Types of filter parameters.
    /// </summary>
    public enum FilterParameterType
    {
        Double,
        Integer,
        Boolean,
        String,
        Color,
        Point,
        Size,
        Rect,
        Enum,
        File,
        Font
    }

    /// <summary>
    /// Whether filter applies to video or audio.
    /// </summary>
    public enum FilterMediaType
    {
        Video,
        Audio,
        Both
    }

    /// <summary>
    /// Extended easing types for animations.
    /// </summary>
    public enum EasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
        EaseInCubic,
        EaseOutCubic,
        EaseInOutCubic,
        EaseInQuart,
        EaseOutQuart,
        EaseInOutQuart,
        EaseInExpo,
        EaseOutExpo,
        EaseInOutExpo,
        Bounce,
        Elastic,
        Step
    }
}
