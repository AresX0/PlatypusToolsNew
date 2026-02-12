using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Abstractions
{
    /// <summary>
    /// Interface for video conversion service.
    /// </summary>
    public interface IVideoConverterService
    {
        /// <summary>
        /// Converts a video file to the specified format.
        /// </summary>
        Task ConvertAsync(string inputPath, string outputPath, string format, CancellationToken ct = default);
        
        /// <summary>
        /// Gets supported output formats.
        /// </summary>
        string[] SupportedFormats { get; }
    }

    /// <summary>
    /// Interface for video combining service.
    /// </summary>
    public interface IVideoCombinerService
    {
        /// <summary>
        /// Combines multiple video files into a single output file.
        /// </summary>
        Task CombineAsync(string[] inputFiles, string outputPath, CancellationToken ct = default);
    }

    /// <summary>
    /// Interface for video upscaling service.
    /// </summary>
    public interface IUpscalerService
    {
        /// <summary>
        /// Upscales a video to a higher resolution.
        /// </summary>
        Task UpscaleAsync(string inputPath, string outputPath, int targetWidth, int targetHeight, CancellationToken ct = default);
    }

    /// <summary>
    /// Interface for file renaming service.
    /// </summary>
    public interface IFileRenamerService
    {
        /// <summary>
        /// Previews rename operations without applying them.
        /// </summary>
        void PreviewRename(string[] files, string pattern);
        
        /// <summary>
        /// Applies rename operations.
        /// </summary>
        Task ApplyRenameAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Interface for disk space analysis service.
    /// </summary>
    public interface IDiskSpaceAnalyzerService
    {
        /// <summary>
        /// Analyzes disk space usage for a path.
        /// </summary>
        Task AnalyzeAsync(string path, CancellationToken ct = default);
    }

    /// <summary>
    /// Interface for process management service.
    /// </summary>
    public interface IProcessManagerService
    {
        /// <summary>
        /// Gets all running processes.
        /// </summary>
        Task<ProcessInfo[]> GetProcessesAsync(CancellationToken ct = default);
        
        /// <summary>
        /// Terminates a process by ID.
        /// </summary>
        Task<bool> TerminateAsync(int processId, CancellationToken ct = default);
    }

    /// <summary>
    /// Represents process information from IProcessManagerService.
    /// </summary>
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long MemoryUsage { get; set; }
        public double CpuUsage { get; set; }
    }

    /// <summary>
    /// Interface for PDF manipulation service.
    /// </summary>
    public interface IPdfService
    {
        /// <summary>
        /// Merges multiple PDF files into one.
        /// </summary>
        Task MergeAsync(string[] inputFiles, string outputPath, CancellationToken ct = default);
        
        /// <summary>
        /// Splits a PDF file into multiple files.
        /// </summary>
        Task SplitAsync(string inputFile, string outputFolder, int? pagesPerFile = null, CancellationToken ct = default);
    }

    /// <summary>
    /// Interface for startup item management service.
    /// </summary>
    public interface IStartupManagerService
    {
        /// <summary>
        /// Gets all startup items.
        /// </summary>
        Task<StartupItem[]> GetStartupItemsAsync(CancellationToken ct = default);
        
        /// <summary>
        /// Enables or disables a startup item.
        /// </summary>
        Task SetEnabledAsync(string itemId, bool enabled, CancellationToken ct = default);
    }

    /// <summary>
    /// Represents a startup item.
    /// </summary>
    public class StartupItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// Interface for logging service.
    /// </summary>
    public interface ILoggingService
    {
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message);
        void Error(string message, Exception ex);
    }
}
