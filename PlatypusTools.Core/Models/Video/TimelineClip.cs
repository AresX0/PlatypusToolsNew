using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a clip on a timeline track.
    /// A clip is a segment of a source file placed at a specific position and duration.
    /// </summary>
    public class TimelineClip : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // Support both string and Guid IDs for compatibility
        private Guid _guidId;
        private string _stringId = string.Empty;

        [JsonIgnore]
        public Guid Id
        {
            get => _guidId;
            set
            {
                _guidId = value;
                _stringId = value.ToString();
            }
        }

        [JsonPropertyName("id")]
        public string StringId
        {
            get => _stringId.Length > 0 ? _stringId : _guidId.ToString();
            set
            {
                _stringId = value;
                if (Guid.TryParse(value, out var guid))
                    _guidId = guid;
                else
                    _guidId = Guid.NewGuid();
            }
        }
        
        private string _name = string.Empty;
        /// <summary>
        /// Display name for the clip.
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }
        
        /// <summary>
        /// Path to the source media file.
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;
        
        /// <summary>
        /// Type of clip content.
        /// </summary>
        public ClipType Type { get; set; } = ClipType.Video;
        
        private TimeSpan _startPosition;
        /// <summary>
        /// Start position of this clip on the timeline.
        /// </summary>
        public TimeSpan StartPosition
        {
            get => _startPosition;
            set
            {
                if (SetField(ref _startPosition, value))
                {
                    OnPropertyChanged(nameof(StartTime));
                    OnPropertyChanged(nameof(EndPosition));
                    OnPropertyChanged(nameof(EndTime));
                }
            }
        }

        /// <summary>
        /// Alias for StartPosition for export compatibility.
        /// </summary>
        [JsonIgnore]
        public TimeSpan StartTime
        {
            get => StartPosition;
            set => StartPosition = value;
        }

        /// <summary>
        /// End time on timeline for export compatibility.
        /// </summary>
        [JsonIgnore]
        public TimeSpan EndTime
        {
            get => EndPosition;
            set => Duration = value - StartPosition;
        }
        
        private TimeSpan _duration;
        /// <summary>
        /// Duration of the clip on the timeline.
        /// </summary>
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (SetField(ref _duration, value))
                {
                    OnPropertyChanged(nameof(EndPosition));
                    OnPropertyChanged(nameof(EndTime));
                }
            }
        }
        
        /// <summary>
        /// Start point within the source file (in point).
        /// </summary>
        public TimeSpan SourceStart { get; set; }

        /// <summary>
        /// Alias for SourceStart for export compatibility.
        /// </summary>
        [JsonIgnore]
        public TimeSpan TrimIn
        {
            get => SourceStart;
            set => SourceStart = value;
        }
        
        /// <summary>
        /// End point within the source file (out point).
        /// </summary>
        public TimeSpan SourceEnd { get; set; }

        /// <summary>
        /// Alias for SourceEnd for export compatibility.
        /// </summary>
        [JsonIgnore]
        public TimeSpan TrimOut
        {
            get => SourceEnd;
            set => SourceEnd = value;
        }
        
        /// <summary>
        /// Original duration of the source file.
        /// </summary>
        public TimeSpan SourceDuration { get; set; }
        
        /// <summary>
        /// Speed multiplier (1.0 = normal, 2.0 = 2x, 0.5 = half speed).
        /// Range: 0.1x to 100x
        /// </summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// Speed curve for variable speed (slow-mo ramps).
        /// </summary>
        public SpeedCurve? SpeedCurve { get; set; }
        
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
        [JsonIgnore]
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
        /// Alias for TransitionIn.
        /// </summary>
        [JsonIgnore]
        public Transition? InTransition
        {
            get => TransitionIn;
            set => TransitionIn = value;
        }

        /// <summary>
        /// Alias for TransitionOut.
        /// </summary>
        [JsonIgnore]
        public Transition? OutTransition
        {
            get => TransitionOut;
            set => TransitionOut = value;
        }
        
        /// <summary>
        /// Applied effects/filters (legacy).
        /// </summary>
        public List<ClipEffect> Effects { get; set; } = new();

        /// <summary>
        /// Applied filters with keyframeable parameters (Shotcut-style).
        /// </summary>
        public List<Filter> Filters { get; set; } = new();

        /// <summary>
        /// Text overlay for title/subtitle clips.
        /// </summary>
        public TextElement? TextOverlay { get; set; }

        /// <summary>
        /// Proxy file path for high-resolution footage.
        /// </summary>
        public string? ProxyPath { get; set; }

        /// <summary>
        /// Whether to use proxy file for preview.
        /// </summary>
        public bool UseProxy { get; set; } = true;
        
        /// <summary>
        /// Keyframes for animation (legacy).
        /// </summary>
        public List<Keyframe> Keyframes { get; set; } = new();

        /// <summary>
        /// Transform keyframes for position, scale, rotation animation.
        /// </summary>
        public List<KeyframeTrack> TransformKeyframes { get; set; } = new();

        /// <summary>
        /// Color grading settings.
        /// </summary>
        public ColorGradingSettings? ColorGrading { get; set; }

        /// <summary>
        /// Markers within this clip for sync points and notes.
        /// </summary>
        public List<ClipMarker> Markers { get; set; } = new();

        /// <summary>
        /// Chroma key (green screen) settings.
        /// </summary>
        public ChromaKeySettings? ChromaKey { get; set; }

        /// <summary>
        /// Overlay settings (for overlay tracks).
        /// </summary>
        public OverlaySettings? OverlaySettings { get; set; }

        /// <summary>
        /// Audio fade in duration.
        /// </summary>
        public TimeSpan AudioFadeIn { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Audio fade in duration in seconds (for binding).
        /// </summary>
        [JsonIgnore]
        public double AudioFadeInSeconds
        {
            get => AudioFadeIn.TotalSeconds;
            set => AudioFadeIn = TimeSpan.FromSeconds(value);
        }

        /// <summary>
        /// Audio fade out duration.
        /// </summary>
        public TimeSpan AudioFadeOut { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Audio fade out duration in seconds (for binding).
        /// </summary>
        [JsonIgnore]
        public double AudioFadeOutSeconds
        {
            get => AudioFadeOut.TotalSeconds;
            set => AudioFadeOut = TimeSpan.FromSeconds(value);
        }

        /// <summary>
        /// FFmpeg input index (used during export).
        /// </summary>
        [JsonIgnore]
        public int InputIndex { get; set; }
        
        /// <summary>
        /// End position on the timeline (calculated).
        /// </summary>
        [JsonIgnore]
        public TimeSpan EndPosition => StartPosition + Duration;

        /// <summary>
        /// Effective duration after speed adjustment.
        /// </summary>
        [JsonIgnore]
        public TimeSpan EffectiveDuration => TimeSpan.FromTicks((long)(Duration.Ticks / Speed));
    }

    /// <summary>
    /// Color grading settings for a clip.
    /// </summary>
    public class ColorGradingSettings
    {
        /// <summary>
        /// Brightness adjustment (-1 to 1, 0 = no change).
        /// </summary>
        public double Brightness { get; set; } = 0;

        /// <summary>
        /// Contrast adjustment (0 to 2, 1 = no change).
        /// </summary>
        public double Contrast { get; set; } = 1;

        /// <summary>
        /// Saturation adjustment (0 to 2, 1 = no change).
        /// </summary>
        public double Saturation { get; set; } = 1;

        /// <summary>
        /// Gamma adjustment (0.1 to 10, 1 = no change).
        /// </summary>
        public double Gamma { get; set; } = 1;

        /// <summary>
        /// Color temperature shift (-1 = cool, 0 = neutral, 1 = warm).
        /// </summary>
        public double Temperature { get; set; } = 0;

        /// <summary>
        /// Tint adjustment (-1 = green, 0 = neutral, 1 = magenta).
        /// </summary>
        public double Tint { get; set; } = 0;

        /// <summary>
        /// Vibrance adjustment (0 to 2, 1 = no change).
        /// </summary>
        public double Vibrance { get; set; } = 1;

        /// <summary>
        /// Shadows adjustment (-1 to 1).
        /// </summary>
        public double Shadows { get; set; } = 0;

        /// <summary>
        /// Highlights adjustment (-1 to 1).
        /// </summary>
        public double Highlights { get; set; } = 0;

        /// <summary>
        /// Blacks level adjustment (-1 to 1).
        /// </summary>
        public double Blacks { get; set; } = 0;

        /// <summary>
        /// Whites level adjustment (-1 to 1).
        /// </summary>
        public double Whites { get; set; } = 0;
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
        /// <summary>
        /// Easing type - uses simple legacy types for compatibility.
        /// </summary>
        public LegacyEasingType Easing { get; set; } = LegacyEasingType.Linear;
    }

    /// <summary>
    /// Legacy easing functions for keyframe interpolation (kept for backward compatibility).
    /// For new code, use EasingType from Filter.cs which has more options.
    /// </summary>
    public enum LegacyEasingType
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut,
        Bezier
    }

    /// <summary>
    /// Represents a marker within a clip for sync points and notes.
    /// </summary>
    public class ClipMarker
    {
        /// <summary>
        /// Unique identifier for the marker.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Position within the clip (relative to clip start).
        /// </summary>
        public TimeSpan Position { get; set; }
        
        /// <summary>
        /// User-defined name/label for the marker.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Optional notes or description.
        /// </summary>
        public string Notes { get; set; } = string.Empty;
        
        /// <summary>
        /// Color for the marker in the timeline.
        /// </summary>
        public string Color { get; set; } = "#FF6B00";
        
        /// <summary>
        /// Type of marker.
        /// </summary>
        public ClipMarkerType Type { get; set; } = ClipMarkerType.Generic;
    }
    
    /// <summary>
    /// Types of clip markers (distinct from timeline MarkerType).
    /// </summary>
    public enum ClipMarkerType
    {
        /// <summary>Generic marker.</summary>
        Generic,
        /// <summary>Sync point for audio/video alignment.</summary>
        SyncPoint,
        /// <summary>Beat marker for music sync.</summary>
        Beat,
        /// <summary>Cue point for important moments.</summary>
        CuePoint,
        /// <summary>Chapter marker for navigation.</summary>
        Chapter,
        /// <summary>Note/comment marker.</summary>
        Note,
        /// <summary>Todo/action item marker.</summary>
        Todo
    }
}
