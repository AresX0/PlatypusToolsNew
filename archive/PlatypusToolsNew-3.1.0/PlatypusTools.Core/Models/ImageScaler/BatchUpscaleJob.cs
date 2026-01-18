using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlatypusTools.Core.Models.ImageScaler
{
    /// <summary>
    /// Status of a batch upscale job.
    /// </summary>
    public enum BatchJobStatus
    {
        Queued,
        Processing,
        Completed,
        Failed,
        Cancelled
    }
    
    /// <summary>
    /// Upscale mode/algorithm.
    /// </summary>
    public enum UpscaleMode
    {
        // AI-based
        ESRGAN,
        RealESRGAN,
        BSRGAN,
        SwinIR,
        GFPGAN,
        CodeFormer,
        
        // Traditional
        Lanczos,
        Bicubic,
        Bilinear,
        NearestNeighbor
    }
    
    /// <summary>
    /// Represents a single image in a batch upscale job.
    /// </summary>
    public class BatchUpscaleItem : INotifyPropertyChanged
    {
        private string _sourcePath = string.Empty;
        private string _outputPath = string.Empty;
        private BatchJobStatus _status = BatchJobStatus.Queued;
        private double _progress;
        private string? _errorMessage;
        private DateTime? _startedAt;
        private DateTime? _completedAt;
        private long _originalSize;
        private long _outputSize;
        private int _originalWidth;
        private int _originalHeight;
        private int _outputWidth;
        private int _outputHeight;
        
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string SourcePath
        {
            get => _sourcePath;
            set { _sourcePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileName)); }
        }
        
        public string FileName => System.IO.Path.GetFileName(SourcePath);
        
        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(); }
        }
        
        public BatchJobStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }
        
        public string StatusText => Status switch
        {
            BatchJobStatus.Queued => "Queued",
            BatchJobStatus.Processing => $"Processing... {Progress:F0}%",
            BatchJobStatus.Completed => "Completed",
            BatchJobStatus.Failed => "Failed",
            BatchJobStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };
        
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }
        
        public string? ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }
        
        public DateTime? StartedAt
        {
            get => _startedAt;
            set { _startedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProcessingTime)); }
        }
        
        public DateTime? CompletedAt
        {
            get => _completedAt;
            set { _completedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProcessingTime)); }
        }
        
        public TimeSpan? ProcessingTime =>
            StartedAt.HasValue && CompletedAt.HasValue 
                ? CompletedAt.Value - StartedAt.Value 
                : null;
        
        public long OriginalSize
        {
            get => _originalSize;
            set { _originalSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(OriginalSizeDisplay)); }
        }
        
        public string OriginalSizeDisplay => FormatFileSize(OriginalSize);

        public long OutputSize
        {
            get => _outputSize;
            set { _outputSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputSizeDisplay)); }
        }
        
        public string OutputSizeDisplay => FormatFileSize(OutputSize);
        
        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "-";
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
        
        public int OriginalWidth
        {
            get => _originalWidth;
            set { _originalWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(OriginalDimensions)); }
        }
        
        public int OriginalHeight
        {
            get => _originalHeight;
            set { _originalHeight = value; OnPropertyChanged(); OnPropertyChanged(nameof(OriginalDimensions)); }
        }
        
        public string OriginalDimensions => $"{OriginalWidth} x {OriginalHeight}";
        
        public int OutputWidth
        {
            get => _outputWidth;
            set { _outputWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputDimensions)); }
        }
        
        public int OutputHeight
        {
            get => _outputHeight;
            set { _outputHeight = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputDimensions)); }
        }
        
        public string OutputDimensions => $"{OutputWidth} x {OutputHeight}";
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// Represents a batch upscale job with multiple images.
    /// </summary>
    public class BatchUpscaleJob : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private BatchJobStatus _status = BatchJobStatus.Queued;
        private double _overallProgress;
        private int _completedCount;
        private int _failedCount;
        private DateTime _createdAt = DateTime.Now;
        private DateTime? _startedAt;
        private DateTime? _completedAt;
        
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        
        public BatchJobStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }
        
        public double OverallProgress
        {
            get => _overallProgress;
            set { _overallProgress = value; OnPropertyChanged(); }
        }
        
        public int CompletedCount
        {
            get => _completedCount;
            set { _completedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
        }
        
        public int FailedCount
        {
            get => _failedCount;
            set { _failedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
        }
        
        public string ProgressText => $"{CompletedCount}/{Items.Count} completed" + 
            (FailedCount > 0 ? $" ({FailedCount} failed)" : "");
        
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }
        
        public DateTime? StartedAt
        {
            get => _startedAt;
            set { _startedAt = value; OnPropertyChanged(); }
        }
        
        public DateTime? CompletedAt
        {
            get => _completedAt;
            set { _completedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalProcessingTime)); }
        }
        
        public TimeSpan? TotalProcessingTime =>
            StartedAt.HasValue && CompletedAt.HasValue 
                ? CompletedAt.Value - StartedAt.Value 
                : null;
        
        /// <summary>
        /// Items in this batch job.
        /// </summary>
        public List<BatchUpscaleItem> Items { get; set; } = new();
        
        /// <summary>
        /// Settings for this batch job.
        /// </summary>
        public BatchUpscaleSettings Settings { get; set; } = new();
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// Settings for batch upscaling.
    /// </summary>
    public class BatchUpscaleSettings
    {
        public UpscaleMode Mode { get; set; } = UpscaleMode.RealESRGAN;
        public double ScaleFactor { get; set; } = 2.0;
        public int? TargetWidth { get; set; }
        public int? TargetHeight { get; set; }
        public bool MaintainAspectRatio { get; set; } = true;
        public string OutputFormat { get; set; } = "png";
        public int JpegQuality { get; set; } = 95;
        public int PngCompression { get; set; } = 6;
        public string OutputDirectory { get; set; } = string.Empty;
        public string OutputNamingPattern { get; set; } = "{name}_upscaled{ext}";
        public bool OverwriteExisting { get; set; } = false;
        public int MaxConcurrentJobs { get; set; } = 2;
        public bool UseGpu { get; set; } = true;
        public int GpuId { get; set; } = 0;
        
        /// <summary>
        /// Generates output filename from pattern.
        /// </summary>
        public string GetOutputFileName(string sourcePath)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            var ext = OutputFormat.StartsWith(".") ? OutputFormat : "." + OutputFormat;
            
            return OutputNamingPattern
                .Replace("{name}", name)
                .Replace("{ext}", ext)
                .Replace("{width}", TargetWidth?.ToString() ?? "")
                .Replace("{height}", TargetHeight?.ToString() ?? "")
                .Replace("{scale}", ScaleFactor.ToString("F1"))
                .Replace("{mode}", Mode.ToString());
        }
    }
}
