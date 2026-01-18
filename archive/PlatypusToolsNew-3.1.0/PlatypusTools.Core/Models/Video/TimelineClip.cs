using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a clip on a timeline track.
    /// A clip is a segment of a source file placed at a specific position and duration.
    /// </summary>
    public class TimelineClip
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Display name for the clip.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Path to the source media file.
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;
        
        /// <summary>
        /// Type of clip content.
        /// </summary>
        public ClipType Type { get; set; } = ClipType.Video;
        
        /// <summary>
        /// Start position of this clip on the timeline.
        /// </summary>
        public TimeSpan StartPosition { get; set; }
        
        /// <summary>
        /// Duration of the clip on the timeline.
        /// </summary>
        public TimeSpan Duration { get; set; }
        
        /// <summary>
        /// Start point within the source file (in point).
        /// </summary>
        public TimeSpan SourceStart { get; set; }
        
        /// <summary>
        /// End point within the source file (out point).
        /// </summary>
        public TimeSpan SourceEnd { get; set; }
        
        /// <summary>
        /// Original duration of the source file.
        /// </summary>
        public TimeSpan SourceDuration { get; set; }
        
        /// <summary>
        /// Speed multiplier (1.0 = normal, 2.0 = 2x, 0.5 = half speed).
        /// </summary>
        public double Speed { get; set; } = 1.0;
        
        /// <summary>
        /// Volume of the clip (0.0 - 2.0).
        /// </summary>
        public double Volume { get; set; } = 1.0;
        
        /// <summary>
        /// Opacity of the clip (0.0 - 1.0).
        /// </summary>
        public double Opacity { get; set; } = 1.0;
        
        /// <summary>
        /// Whether the clip is selected in the UI.
        /// </summary>
        public bool IsSelected { get; set; }
        
        /// <summary>
        /// Whether the audio is muted for this clip.
        /// </summary>
        public bool IsMuted { get; set; }
        
        /// <summary>
        /// Thumbnail path for preview.
        /// </summary>
        public string ThumbnailPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Color for the clip in the timeline UI.
        /// </summary>
        public string Color { get; set; } = "#5B9BD5";
        
        /// <summary>
        /// Transition at the start of this clip.
        /// </summary>
        public Transition? TransitionIn { get; set; }
        
        /// <summary>
        /// Transition at the end of this clip.
        /// </summary>
        public Transition? TransitionOut { get; set; }
        
        /// <summary>
        /// Applied effects/filters.
        /// </summary>
        public List<ClipEffect> Effects { get; set; } = new();
        
        /// <summary>
        /// Keyframes for animation.
        /// </summary>
        public List<Keyframe> Keyframes { get; set; } = new();
        
        /// <summary>
        /// End position on the timeline (calculated).
        /// </summary>
        public TimeSpan EndPosition => StartPosition + Duration;
    }

    /// <summary>
    /// Types of clip content.
    /// </summary>
    public enum ClipType
    {
        Video,
        Audio,
        Image,
        Title,
        ColorMatte,
        Adjustment
    }

    /// <summary>
    /// Represents an effect applied to a clip.
    /// </summary>
    public class ClipEffect
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string EffectType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Represents a keyframe for animating clip properties.
    /// </summary>
    public class Keyframe
    {
        public TimeSpan Time { get; set; }
        public string Property { get; set; } = string.Empty;
        public object Value { get; set; } = default!;
        public EasingType Easing { get; set; } = EasingType.Linear;
    }

    /// <summary>
    /// Easing functions for keyframe interpolation.
    /// </summary>
    public enum EasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
        Bezier
    }
}
