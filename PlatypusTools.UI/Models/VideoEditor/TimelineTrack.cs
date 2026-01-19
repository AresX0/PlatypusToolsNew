using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlatypusTools.UI.Models.VideoEditor
{
    /// <summary>
    /// Represents a track in the timeline (video or audio).
    /// Modeled after Shotcut's multitrackmodel.h
    /// </summary>
    public class TimelineTrack : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private TrackType _type;
        private bool _isMuted;
        private bool _isHidden;
        private bool _isLocked;
        private bool _isComposite = true;
        private double _height = 50;

        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public TrackType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        public bool IsMuted
        {
            get => _isMuted;
            set { _isMuted = value; OnPropertyChanged(); }
        }

        public bool IsHidden
        {
            get => _isHidden;
            set { _isHidden = value; OnPropertyChanged(); }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set { _isLocked = value; OnPropertyChanged(); }
        }

        public bool IsComposite
        {
            get => _isComposite;
            set { _isComposite = value; OnPropertyChanged(); }
        }

        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TimelineClip> Clips { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum TrackType
    {
        Video,
        Audio
    }
}
