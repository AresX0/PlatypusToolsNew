using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents an animated keyframe with Bezier curve interpolation.
    /// Supports transform, opacity, speed, and effect property animation.
    /// </summary>
    public class AnimatedKeyframe
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Time position of the keyframe relative to clip start.
        /// </summary>
        public TimeSpan Time { get; set; }
        
        /// <summary>
        /// Property being animated (e.g., "Position.X", "Opacity", "Scale", "Speed").
        /// </summary>
        public string Property { get; set; } = string.Empty;
        
        /// <summary>
        /// Value at this keyframe.
        /// </summary>
        public double Value { get; set; }
        
        /// <summary>
        /// Easing type for interpolation to next keyframe.
        /// </summary>
        public KeyframeEasing Easing { get; set; } = KeyframeEasing.Linear;
        
        /// <summary>
        /// Bezier control point 1 (for custom curves). Range 0-1.
        /// </summary>
        public BezierPoint ControlPoint1 { get; set; } = new(0.25, 0.1);
        
        /// <summary>
        /// Bezier control point 2 (for custom curves). Range 0-1.
        /// </summary>
        public BezierPoint ControlPoint2 { get; set; } = new(0.75, 0.9);
        
        /// <summary>
        /// Whether this keyframe is selected in the UI.
        /// </summary>
        public bool IsSelected { get; set; }
    }

    /// <summary>
    /// A point in normalized Bezier space (0-1 range).
    /// </summary>
    public struct BezierPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public BezierPoint(double x, double y)
        {
            X = Math.Clamp(x, 0, 1);
            Y = y; // Y can go outside 0-1 for overshoot
        }
    }

    /// <summary>
    /// Keyframe easing presets and custom Bezier.
    /// </summary>
    public enum KeyframeEasing
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
        EaseInQuad,
        EaseOutQuad,
        EaseInOutQuad,
        EaseInCubic,
        EaseOutCubic,
        EaseInOutCubic,
        EaseInBack,
        EaseOutBack,
        EaseInOutBack,
        EaseInElastic,
        EaseOutElastic,
        EaseInBounce,
        EaseOutBounce,
        Bezier // Custom curve using ControlPoint1/2
    }

    /// <summary>
    /// A collection of keyframes for a single property.
    /// </summary>
    public class KeyframeTrack
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Property this track animates.
        /// </summary>
        public string Property { get; set; } = string.Empty;
        
        /// <summary>
        /// Display name for the property.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
        
        /// <summary>
        /// Minimum allowed value.
        /// </summary>
        public double MinValue { get; set; } = double.MinValue;
        
        /// <summary>
        /// Maximum allowed value.
        /// </summary>
        public double MaxValue { get; set; } = double.MaxValue;
        
        /// <summary>
        /// Default value when no keyframes exist.
        /// </summary>
        public double DefaultValue { get; set; }
        
        /// <summary>
        /// Keyframes in chronological order.
        /// </summary>
        public List<AnimatedKeyframe> Keyframes { get; set; } = new();
        
        /// <summary>
        /// Whether this track is expanded in the curve editor.
        /// </summary>
        public bool IsExpanded { get; set; } = true;
        
        /// <summary>
        /// Color for the curve in the editor.
        /// </summary>
        public string CurveColor { get; set; } = "#4A90D9";
    }

    /// <summary>
    /// Transform properties for overlay positioning.
    /// </summary>
    public class ClipTransform
    {
        /// <summary>
        /// X position (0 = center, -1 = left, 1 = right).
        /// </summary>
        public double PositionX { get; set; }

        /// <summary>
        /// Alias for PositionX.
        /// </summary>
        [JsonIgnore]
        public double X
        {
            get => PositionX;
            set => PositionX = value;
        }
        
        /// <summary>
        /// Y position (0 = center, -1 = top, 1 = bottom).
        /// </summary>
        public double PositionY { get; set; }

        /// <summary>
        /// Alias for PositionY.
        /// </summary>
        [JsonIgnore]
        public double Y
        {
            get => PositionY;
            set => PositionY = value;
        }
        
        /// <summary>
        /// Scale factor (1.0 = 100%).
        /// </summary>
        public double Scale { get; set; } = 1.0;
        
        /// <summary>
        /// Scale X factor (1.0 = 100%).
        /// </summary>
        public double ScaleX { get; set; } = 1.0;
        
        /// <summary>
        /// Scale Y factor (1.0 = 100%).
        /// </summary>
        public double ScaleY { get; set; } = 1.0;
        
        /// <summary>
        /// Rotation in degrees.
        /// </summary>
        public double Rotation { get; set; }
        
        /// <summary>
        /// Anchor point X (0-1, 0.5 = center).
        /// </summary>
        public double AnchorX { get; set; } = 0.5;
        
        /// <summary>
        /// Anchor point Y (0-1, 0.5 = center).
        /// </summary>
        public double AnchorY { get; set; } = 0.5;
    }

    /// <summary>
    /// Speed curve definition for variable playback speed.
    /// </summary>
    public class SpeedCurve
    {
        /// <summary>
        /// Whether speed curve is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }
        
        /// <summary>
        /// Keyframes defining speed over time.
        /// Value = speed multiplier (0.1 to 100).
        /// </summary>
        public List<AnimatedKeyframe> SpeedKeyframes { get; set; } = new();

        /// <summary>
        /// Alias for SpeedKeyframes.
        /// </summary>
        [JsonIgnore]
        public List<AnimatedKeyframe> Keyframes
        {
            get => SpeedKeyframes;
            set => SpeedKeyframes = value;
        }
        
        /// <summary>
        /// Whether to preserve audio pitch during speed changes.
        /// </summary>
        public bool PreservePitch { get; set; } = true;
    }
}
