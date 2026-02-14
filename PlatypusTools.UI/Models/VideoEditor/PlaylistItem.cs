using System;
using System.Windows.Media;
using PlatypusTools.Core.Models;

namespace PlatypusTools.UI.Models.VideoEditor
{
    /// <summary>
    /// Represents a source media item in the playlist/bin.
    /// Modeled after Shotcut's PlaylistModel.
    /// </summary>
    public class PlaylistItem : BindableModel
    {
        private string _name = string.Empty;
        private string _filePath = string.Empty;
        private TimeSpan _duration;
        private TimeSpan _inPoint;
        private TimeSpan _outPoint;
        private ImageSource? _thumbnail;
        private bool _isSelected;
        private MediaType _mediaType;
        private int _width;
        private int _height;
        private double _frameRate;
        private string _videoCodec = string.Empty;
        private string _audioCodec = string.Empty;
        private long _fileSize;

        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public TimeSpan Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); }
        }

        public string DurationText => Duration.ToString(@"hh\:mm\:ss\.ff");

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

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public MediaType Type
        {
            get => _mediaType;
            set { _mediaType = value; OnPropertyChanged(); }
        }

        public int Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(Resolution)); }
        }

        public int Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(Resolution)); }
        }

        public string Resolution => Width > 0 && Height > 0 ? $"{Width}x{Height}" : string.Empty;

        public double FrameRate
        {
            get => _frameRate;
            set { _frameRate = value; OnPropertyChanged(); }
        }

        public string VideoCodec
        {
            get => _videoCodec;
            set { _videoCodec = value; OnPropertyChanged(); }
        }

        public string AudioCodec
        {
            get => _audioCodec;
            set { _audioCodec = value; OnPropertyChanged(); }
        }

        public long FileSize
        {
            get => _fileSize;
            set { _fileSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileSizeText)); }
        }

        public string FileSizeText
        {
            get
            {
                if (_fileSize < 1024) return $"{_fileSize} B";
                if (_fileSize < 1024 * 1024) return $"{_fileSize / 1024.0:F1} KB";
                if (_fileSize < 1024 * 1024 * 1024) return $"{_fileSize / (1024.0 * 1024):F1} MB";
                return $"{_fileSize / (1024.0 * 1024 * 1024):F2} GB";
            }
        }
    }

    public enum MediaType
    {
        Video,
        Audio,
        Image
    }
}
