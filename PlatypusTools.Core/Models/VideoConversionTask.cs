using System;

namespace PlatypusTools.Core.Models
{
    public class VideoConversionTask : BindableModel
    {
        private string _sourcePath = string.Empty;
        private string _outputPath = string.Empty;
        private VideoFormat _targetFormat;
        private VideoQuality _quality;
        private ConversionStatus _status;
        private double _progress;
        private string _statusMessage = string.Empty;
        private TimeSpan? _duration;
        private TimeSpan? _elapsed;
        private long? _fileSizeBytes;
        private bool _isSelected = true;

        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        public VideoFormat TargetFormat
        {
            get => _targetFormat;
            set => SetProperty(ref _targetFormat, value);
        }

        public VideoQuality Quality
        {
            get => _quality;
            set => SetProperty(ref _quality, value);
        }

        public ConversionStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public TimeSpan? Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        public TimeSpan? Elapsed
        {
            get => _elapsed;
            set => SetProperty(ref _elapsed, value);
        }

        public long? FileSizeBytes
        {
            get => _fileSizeBytes;
            set => SetProperty(ref _fileSizeBytes, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public enum VideoFormat
    {
        MP4,
        MKV,
        AVI,
        MOV,
        WMV,
        FLV,
        WebM,
        MPEG
    }

    public enum VideoQuality
    {
        Source,      // Copy codec
        High,        // CRF 18
        Medium,      // CRF 23
        Low,         // CRF 28
        VeryLow      // CRF 32
    }

    public enum ConversionStatus
    {
        Pending,
        Converting,
        Completed,
        Failed,
        Cancelled
    }
}
