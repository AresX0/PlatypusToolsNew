namespace PlatypusTools.Core.Models
{
    public class VideoConversionTask
    {
        public string SourcePath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public VideoFormat TargetFormat { get; set; }
        public VideoQuality Quality { get; set; }
        public ConversionStatus Status { get; set; }
        public double Progress { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public TimeSpan? Duration { get; set; }
        public TimeSpan? Elapsed { get; set; }
        public long? FileSizeBytes { get; set; }
        public bool IsSelected { get; set; } = true;
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
