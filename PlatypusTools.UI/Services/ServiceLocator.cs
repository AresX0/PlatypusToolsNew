using System;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Services.Video;
using VideoFFmpegService = PlatypusTools.Core.Services.Video.FFmpegService;
using VideoFFprobeService = PlatypusTools.Core.Services.Video.FFprobeService;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Provides singleton access to shared services.
    /// Optimizes memory usage by reusing stateless service instances.
    /// </summary>
    public static class ServiceLocator
    {
        #region Video Services (Lazy Singletons)

        private static readonly Lazy<VideoFFmpegService> _ffmpegService = 
            new(() => new VideoFFmpegService());
        
        private static readonly Lazy<VideoFFprobeService> _ffprobeService = 
            new(() => new VideoFFprobeService());
        
        private static readonly Lazy<BeatDetectionService> _beatDetectionService = 
            new(() => new BeatDetectionService());
        
        private static readonly Lazy<TimelineOperationsService> _timelineOperationsService = 
            new(() => new TimelineOperationsService());
        
        private static readonly Lazy<KeyframeInterpolator> _keyframeInterpolator = 
            new(() => new KeyframeInterpolator());

        /// <summary>
        /// Gets the shared FFmpeg service instance for video operations.
        /// </summary>
        public static VideoFFmpegService FFmpeg => _ffmpegService.Value;

        /// <summary>
        /// Gets the shared FFprobe service instance for media probing.
        /// </summary>
        public static VideoFFprobeService FFprobe => _ffprobeService.Value;

        /// <summary>
        /// Gets the shared beat detection service instance.
        /// </summary>
        public static BeatDetectionService BeatDetection => _beatDetectionService.Value;

        /// <summary>
        /// Gets the shared timeline operations service instance.
        /// </summary>
        public static TimelineOperationsService TimelineOperations => _timelineOperationsService.Value;

        /// <summary>
        /// Gets the shared keyframe interpolator instance.
        /// </summary>
        public static KeyframeInterpolator KeyframeInterpolator => _keyframeInterpolator.Value;

        #endregion

        #region Core Services (Lazy Singletons)

        private static readonly Lazy<FileRenamerService> _fileRenamerService = 
            new(() => new FileRenamerService());
        
        private static readonly Lazy<VideoConverterService> _videoConverterService = 
            new(() => new VideoConverterService());
        
        private static readonly Lazy<VideoCombinerService> _videoCombinerService = 
            new(() => new VideoCombinerService());
        
        private static readonly Lazy<UpscalerService> _upscalerService = 
            new(() => new UpscalerService());
        
        private static readonly Lazy<DiskSpaceAnalyzerService> _diskSpaceAnalyzerService = 
            new(() => new DiskSpaceAnalyzerService());
        
        private static readonly Lazy<ProcessManagerService> _processManagerService = 
            new(() => new ProcessManagerService());
        
        private static readonly Lazy<ScheduledTasksService> _scheduledTasksService = 
            new(() => new ScheduledTasksService());
        
        private static readonly Lazy<StartupManagerService> _startupManagerService = 
            new(() => new StartupManagerService());
        
        private static readonly Lazy<SystemRestoreService> _systemRestoreService = 
            new(() => new SystemRestoreService());
        
        private static readonly Lazy<PdfService> _pdfService = 
            new(() => new PdfService());

        /// <summary>
        /// Gets the shared file renamer service instance.
        /// </summary>
        public static FileRenamerService FileRenamer => _fileRenamerService.Value;

        /// <summary>
        /// Gets the shared video converter service instance.
        /// </summary>
        public static VideoConverterService VideoConverter => _videoConverterService.Value;

        /// <summary>
        /// Gets the shared video combiner service instance.
        /// </summary>
        public static VideoCombinerService VideoCombiner => _videoCombinerService.Value;

        /// <summary>
        /// Gets the shared upscaler service instance.
        /// </summary>
        public static UpscalerService Upscaler => _upscalerService.Value;

        /// <summary>
        /// Gets the shared disk space analyzer service instance.
        /// </summary>
        public static DiskSpaceAnalyzerService DiskSpaceAnalyzer => _diskSpaceAnalyzerService.Value;

        /// <summary>
        /// Gets the shared process manager service instance.
        /// </summary>
        public static ProcessManagerService ProcessManager => _processManagerService.Value;

        /// <summary>
        /// Gets the shared scheduled tasks service instance.
        /// </summary>
        public static ScheduledTasksService ScheduledTasks => _scheduledTasksService.Value;

        /// <summary>
        /// Gets the shared startup manager service instance.
        /// </summary>
        public static StartupManagerService StartupManager => _startupManagerService.Value;

        /// <summary>
        /// Gets the shared system restore service instance.
        /// </summary>
        public static SystemRestoreService SystemRestore => _systemRestoreService.Value;

        /// <summary>
        /// Gets the shared PDF service instance.
        /// </summary>
        public static PdfService PdfTools => _pdfService.Value;

        #endregion

        #region UI Services

        /// <summary>
        /// Gets the TabVisibilityService singleton instance.
        /// </summary>
        public static TabVisibilityService TabVisibility => TabVisibilityService.Instance;

        #endregion
    }
}
