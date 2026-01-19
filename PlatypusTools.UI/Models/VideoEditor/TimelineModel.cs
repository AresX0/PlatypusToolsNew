using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PlatypusTools.UI.Models.VideoEditor
{
    /// <summary>
    /// Main timeline model containing all tracks and clips.
    /// Modeled after Shotcut's MultitrackModel.
    /// </summary>
    public class TimelineModel : INotifyPropertyChanged
    {
        private TimeSpan _position;
        private TimeSpan _duration;
        private double _zoom = 1.0;
        private double _pixelsPerSecond = 100;
        private TimeSpan _inPoint;
        private TimeSpan _outPoint;
        private TimeSpan _loopStart;
        private TimeSpan _loopEnd;
        private bool _isLooping;
        private double _frameRate = 30;

        public ObservableCollection<TimelineTrack> Tracks { get; } = new();

        /// <summary>
        /// Current playhead position
        /// </summary>
        public TimeSpan Position
        {
            get => _position;
            set
            {
                if (value < TimeSpan.Zero) value = TimeSpan.Zero;
                if (value > Duration) value = Duration;
                _position = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Total duration of the timeline
        /// </summary>
        public TimeSpan Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); }
        }

        public double Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Clamp(value, 0.1, 10.0);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectivePixelsPerSecond));
            }
        }

        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set
            {
                _pixelsPerSecond = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectivePixelsPerSecond));
            }
        }

        public double EffectivePixelsPerSecond => _pixelsPerSecond * _zoom;

        public TimeSpan InPoint
        {
            get => _inPoint;
            set { _inPoint = value; OnPropertyChanged(); }
        }

        public TimeSpan OutPoint
        {
            get => _outPoint;
            set { _outPoint = value; OnPropertyChanged(); }
        }

        public TimeSpan LoopStart
        {
            get => _loopStart;
            set { _loopStart = value; OnPropertyChanged(); }
        }

        public TimeSpan LoopEnd
        {
            get => _loopEnd;
            set { _loopEnd = value; OnPropertyChanged(); }
        }

        public bool IsLooping
        {
            get => _isLooping;
            set { _isLooping = value; OnPropertyChanged(); }
        }

        public double FrameRate
        {
            get => _frameRate;
            set { _frameRate = value; OnPropertyChanged(); }
        }

        public TimelineModel()
        {
            // Create default tracks like Shotcut
            AddVideoTrack("V1");
            AddAudioTrack("A1");
        }

        public TimelineTrack AddVideoTrack(string? name = null)
        {
            var track = new TimelineTrack
            {
                Name = name ?? $"V{Tracks.Count(t => t.Type == TrackType.Video) + 1}",
                Type = TrackType.Video
            };
            Tracks.Insert(0, track); // Video tracks at top
            return track;
        }

        public TimelineTrack AddAudioTrack(string? name = null)
        {
            var track = new TimelineTrack
            {
                Name = name ?? $"A{Tracks.Count(t => t.Type == TrackType.Audio) + 1}",
                Type = TrackType.Audio
            };
            Tracks.Add(track); // Audio tracks at bottom
            return track;
        }

        public void RemoveTrack(TimelineTrack track)
        {
            Tracks.Remove(track);
        }

        public void RecalculateDuration()
        {
            TimeSpan maxEnd = TimeSpan.Zero;
            foreach (var track in Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.EndTime > maxEnd)
                        maxEnd = clip.EndTime;
                }
            }
            Duration = maxEnd > TimeSpan.Zero ? maxEnd : TimeSpan.FromSeconds(60);
        }

        public TimelineClip? GetClipAtPosition(TimeSpan position)
        {
            foreach (var track in Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (position >= clip.StartTime && position < clip.EndTime)
                        return clip;
                }
            }
            return null;
        }

        public void ClearSelection()
        {
            foreach (var track in Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    clip.IsSelected = false;
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
