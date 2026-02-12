using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a track in the video editor timeline.
    /// Tracks can contain video clips, audio clips, titles, or effects.
    /// </summary>
    public class TimelineTrack
    {
        // Support both string and Guid IDs
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

        public string Name { get; set; } = string.Empty;
        public TrackType Type { get; set; } = TrackType.Video;
        public int Order { get; set; }
        
        /// <summary>
        /// Whether the track is visible in output.
        /// </summary>
        public bool IsVisible { get; set; } = true;
        
        /// <summary>
        /// Whether the track's audio is muted.
        /// </summary>
        public bool IsMuted { get; set; } = false;
        
        /// <summary>
        /// Whether the track is soloed (only this track plays).
        /// </summary>
        public bool IsSolo { get; set; } = false;
        
        /// <summary>
        /// Whether the track is locked (prevents editing).
        /// </summary>
        public bool IsLocked { get; set; } = false;
        
        /// <summary>
        /// Track opacity (0.0 - 1.0).
        /// </summary>
        public double Opacity { get; set; } = 1.0;
        
        /// <summary>
        /// Blend mode for track compositing (TASK-329).
        /// </summary>
        public string BlendMode { get; set; } = "Normal";
        
        /// <summary>
        /// Track volume (0.0 - 2.0, 1.0 = 100%).
        /// </summary>
        public double Volume { get; set; } = 1.0;
        
        /// <summary>
        /// Height of the track in the timeline UI.
        /// </summary>
        public double Height { get; set; } = 60;
        
        /// <summary>
        /// Clips on this track.
        /// </summary>
        public ObservableCollection<TimelineClip> Clips { get; set; } = new();
        
        /// <summary>
        /// Color for the track header.
        /// </summary>
        public string Color { get; set; } = "#4A90D9";
    }

    /// <summary>
    /// Types of timeline tracks.
    /// </summary>
    public enum TrackType
    {
        Video,
        Audio,
        Title,
        Effects,
        Adjustment,
        Overlay
    }
}
