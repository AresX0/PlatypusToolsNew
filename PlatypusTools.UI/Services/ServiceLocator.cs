using System;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Services.Video;
using PlatypusTools.UI.Services.Forensics;
using VideoFFmpegService = PlatypusTools.Core.Services.Video.FFmpegService;
using VideoFFprobeService = PlatypusTools.Core.Services.Video.FFprobeService;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Provides singleton access to shared services.
    /// Optimizes memory usage by reusing stateless service instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>MIGRATION NOTE:</strong> This class is being gradually replaced by 
    /// <see cref="PlatypusTools.Core.Services.ServiceContainer"/> which provides dependency injection.
    /// </para>
    /// <para>
    /// New code should prefer resolving services via ServiceContainer:
    /// <code>
    /// var service = ServiceContainer.GetService&lt;VideoConverterService&gt;();
    /// </code>
    /// </para>
    /// <para>
    /// Or better yet, use constructor injection when creating new ViewModels:
    /// <code>
    /// public MyViewModel(IVideoConverterService videoConverter)
    /// {
    ///     _videoConverter = videoConverter;
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static class ServiceLocator
    {
        #region Video Services (Lazy Singletons)

        private static readonly Lazy<VideoFFmpegService> _ffmpegService = 
            new(() => new VideoFFmpegService());
        
        private static readonly Lazy<VideoFFprobeService> _ffprobeService = 
            new(() => new VideoFFprobeService());

        /// <summary>
        /// Gets the shared FFmpeg service instance for video operations.
        /// </summary>
        public static VideoFFmpegService FFmpeg => _ffmpegService.Value;

        /// <summary>
        /// Gets the shared FFprobe service instance for media probing.
        /// </summary>
        public static VideoFFprobeService FFprobe => _ffprobeService.Value;

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

        #region Forensic Services (Lazy Singletons)

        private static readonly Lazy<TaskSchedulerService> _taskSchedulerService = 
            new(() => ForensicsServiceFactory.CreateTaskSchedulerService());
        
        private static readonly Lazy<BrowserForensicsService> _browserForensicsService = 
            new(() => ForensicsServiceFactory.CreateBrowserForensicsService());
        
        private static readonly Lazy<IOCScannerService> _iocScannerService = 
            new(() => ForensicsServiceFactory.CreateIOCScannerService());
        
        private static readonly Lazy<RegistryDiffService> _registryDiffService = 
            new(() => ForensicsServiceFactory.CreateRegistryDiffService());
        
        private static readonly Lazy<PcapParserService> _pcapParserService = 
            new(() => ForensicsServiceFactory.CreatePcapParserService());
        
        private static readonly Lazy<YaraService> _yaraService = 
            new(() => ForensicsServiceFactory.CreateYaraService());

        /// <summary>
        /// Gets the shared task scheduler service instance.
        /// </summary>
        public static TaskSchedulerService TaskScheduler => _taskSchedulerService.Value;

        /// <summary>
        /// Gets the shared browser forensics service instance.
        /// </summary>
        public static BrowserForensicsService BrowserForensics => _browserForensicsService.Value;

        /// <summary>
        /// Gets the shared IOC scanner service instance.
        /// </summary>
        public static IOCScannerService IOCScanner => _iocScannerService.Value;

        /// <summary>
        /// Gets the shared registry diff service instance.
        /// </summary>
        public static RegistryDiffService RegistryDiff => _registryDiffService.Value;

        /// <summary>
        /// Gets the shared PCAP parser service instance.
        /// </summary>
        public static PcapParserService PcapParser => _pcapParserService.Value;

        /// <summary>
        /// Gets the shared YARA service instance.
        /// </summary>
        public static YaraService Yara => _yaraService.Value;

        #endregion
    }
}
