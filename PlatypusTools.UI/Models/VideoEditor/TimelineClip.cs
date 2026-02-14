using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.UI.Models.VideoEditor
{
    /// <summary>
    /// Represents a clip on the timeline.
    /// Modeled after Shotcut's clip handling in multitrackmodel.cpp
    /// </summary>
    public class TimelineClip : BindableModel
    {
        private string _name = string.Empty;
        private string _sourcePath = string.Empty;
        private TimeSpan _startTime;
        private TimeSpan _duration;
        private TimeSpan _inPoint;
        private TimeSpan _outPoint;
        private bool _isSelected;
        private double _gain = 1.0;
        private TimeSpan _fadeIn;
        private TimeSpan _fadeOut;
        private Color _color = Colors.DodgerBlue;
        private ImageSource? _thumbnail;

        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string SourcePath
        {
            get => _sourcePath;
            set { _sourcePath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Position on the timeline where this clip starts
        /// </summary>
        public TimeSpan StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(EndTime)); }
        }

        /// <summary>
        /// Duration of the clip on the timeline
        /// </summary>
        public TimeSpan Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(EndTime)); }
        }

        /// <summary>
        /// End time on the timeline
        /// </summary>
        public TimeSpan EndTime => StartTime + Duration;

        /// <summary>
        /// In point within the source media
        /// </summary>
        public TimeSpan InPoint
        {
            get => _inPoint;
            set { _inPoint = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Out point within the source media
        /// </summary>
        public TimeSpan OutPoint
        {
            get => _outPoint;
            set { _outPoint = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public double Gain
        {
            get => _gain;
            set { _gain = Math.Clamp(value, 0, 10); OnPropertyChanged(); }
        }

        public TimeSpan FadeIn
        {
            get => _fadeIn;
            set { _fadeIn = value; OnPropertyChanged(); }
        }

        public TimeSpan FadeOut
        {
            get => _fadeOut;
            set { _fadeOut = value; OnPropertyChanged(); }
        }

        public Color Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); }
        }

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Source media duration (full length before trimming)
        /// </summary>
        public TimeSpan SourceDuration { get; set; }

        /// <summary>
        /// Indicates if this clip represents audio only (extracted from video)
        /// </summary>
        public bool IsAudioOnly { get; set; }

        /// <summary>
        /// Speed multiplier for the clip (1.0 = normal, 0.5 = half, 2.0 = double).
        /// </summary>
        private double _speed = 1.0;
        public double Speed
        {
            get => _speed;
            set { _speed = Math.Clamp(value, 0.1, 100); OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether audio pitch should be preserved when speed != 1.0.
        /// </summary>
        public bool PreservePitch { get; set; } = true;

        /// <summary>
        /// Whether this clip has a freeze frame applied.
        /// </summary>
        public bool IsFreezeFrame { get; set; }

        /// <summary>
        /// Position within the clip to freeze at (when IsFreezeFrame is true).
        /// </summary>
        public TimeSpan FreezeAt { get; set; }
        
        /// <summary>
        /// Applied filters for this clip
        /// </summary>
        public ObservableCollection<Filter> Filters { get; } = new();
    }
}
