using System;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a marker on the timeline.
    /// Markers can be used for navigation, chapter points, or beat sync.
    /// </summary>
    public class TimelineMarker
    {
        /// <summary>
        /// Unique identifier for the marker.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Position of the marker on the timeline.
        /// </summary>
        public TimeSpan Position { get; set; }

        /// <summary>
        /// Display name/label for the marker.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Color of the marker (hex format).
        /// </summary>
        public string Color { get; set; } = "#F472B6"; // Pink by default

        /// <summary>
        /// Type of marker.
        /// </summary>
        public MarkerType Type { get; set; } = MarkerType.Standard;

        /// <summary>
        /// Optional notes/description.
        /// </summary>
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// Whether this marker is a chapter point (for export).
        /// </summary>
        public bool IsChapter { get; set; }

        /// <summary>
        /// Position in seconds (for display).
        /// </summary>
        [JsonIgnore]
        public double PositionSeconds => Position.TotalSeconds;

        /// <summary>
        /// Formatted time string.
        /// </summary>
        [JsonIgnore]
        public string FormattedTime => $"{Position.Hours:D2}:{Position.Minutes:D2}:{Position.Seconds:D2}.{Position.Milliseconds:D3}";
    }

    /// <summary>
    /// Types of timeline markers.
    /// </summary>
    public enum MarkerType
    {
        /// <summary>Standard navigation marker.</summary>
        Standard,
        
        /// <summary>Beat marker from audio analysis.</summary>
        Beat,
        
        /// <summary>Chapter marker for export.</summary>
        Chapter,
        
        /// <summary>In point for loop/trim.</summary>
        InPoint,
        
        /// <summary>Out point for loop/trim.</summary>
        OutPoint,
        
        /// <summary>Comment/annotation marker.</summary>
        Comment
    }
}
