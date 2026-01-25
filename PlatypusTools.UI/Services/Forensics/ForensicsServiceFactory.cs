using System;
using System.IO;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Factory for creating forensic service instances with default configurations.
    /// </summary>
    public static class ForensicsServiceFactory
    {
        /// <summary>
        /// Gets the default output directory for forensic artifacts.
        /// </summary>
        public static string DefaultOutputPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PlatypusTools",
            "DFIR");

        /// <summary>
        /// Gets the default tools directory.
        /// </summary>
        public static string DefaultToolsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools",
            "Tools");

        /// <summary>
        /// Creates a new VolatilityAnalysisService with default configuration.
        /// </summary>
        public static VolatilityAnalysisService CreateVolatilityService(string? outputPath = null)
        {
            return new VolatilityAnalysisService
            {
                OutputPath = outputPath ?? Path.Combine(DefaultOutputPath, "Volatility"),
                VolatilityPath = Path.Combine(DefaultToolsPath, "volatility3")
            };
        }

        /// <summary>
        /// Creates a new KapeCollectionService with default configuration.
        /// </summary>
        public static KapeCollectionService CreateKapeService(string? outputPath = null)
        {
            return new KapeCollectionService
            {
                OutputPath = outputPath ?? Path.Combine(DefaultOutputPath, "KAPE"),
                TargetPath = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"
            };
        }

        /// <summary>
        /// Creates a new PlasoTimelineService with default configuration.
        /// </summary>
        public static PlasoTimelineService CreatePlasoService(string? outputPath = null)
        {
            var basePath = outputPath ?? Path.Combine(DefaultOutputPath, "Plaso");
            return new PlasoTimelineService
            {
                StorageFilePath = Path.Combine(basePath, $"timeline_{DateTime.Now:yyyyMMdd_HHmmss}.plaso"),
                PlasoPath = Path.Combine(DefaultToolsPath, "plaso")
            };
        }

        /// <summary>
        /// Creates a new VelociraptorService with default configuration.
        /// </summary>
        public static VelociraptorService CreateVelociraptorService(string? outputPath = null)
        {
            return new VelociraptorService
            {
                OutputPath = outputPath ?? Path.Combine(DefaultOutputPath, "Velociraptor"),
                VelociraptorPath = Path.Combine(DefaultToolsPath, "velociraptor", "velociraptor.exe")
            };
        }

        /// <summary>
        /// Creates a new BulkExtractorService with default configuration.
        /// </summary>
        public static BulkExtractorService CreateBulkExtractorService(string? outputPath = null)
        {
            return new BulkExtractorService
            {
                OutputPath = outputPath ?? Path.Combine(DefaultOutputPath, "BulkExtractor"),
                BulkExtractorPath = Path.Combine(DefaultToolsPath, "bulk_extractor", "bulk_extractor.exe")
            };
        }

        /// <summary>
        /// Creates a new MemoryAcquisitionService with default configuration.
        /// </summary>
        public static MemoryAcquisitionService CreateMemoryAcquisitionService(string? outputPath = null)
        {
            return new MemoryAcquisitionService
            {
                OutputPath = outputPath ?? Path.Combine(DefaultOutputPath, "MemoryDumps"),
                WinPmemPath = Path.Combine(DefaultToolsPath, "winpmem", "winpmem_mini_x64.exe")
            };
        }

        /// <summary>
        /// Creates a new MalwareAnalysisService with default configuration.
        /// </summary>
        public static MalwareAnalysisService CreateMalwareAnalysisService()
        {
            return new MalwareAnalysisService
            {
                PdfParserPath = Path.Combine(DefaultToolsPath, "pdf-parser", "pdf-parser.py")
            };
        }

        /// <summary>
        /// Creates a new MetadataExtractionService with default configuration.
        /// </summary>
        public static MetadataExtractionService CreateMetadataExtractionService()
        {
            // ExifTool should be in the install directory, not tools directory
            var installDir = AppDomain.CurrentDomain.BaseDirectory;
            return new MetadataExtractionService
            {
                ExifToolPath = Path.Combine(installDir, "exiftool.exe")
            };
        }

        /// <summary>
        /// Creates a new YaraService with default configuration.
        /// </summary>
        public static YaraService CreateYaraService()
        {
            return new YaraService
            {
                YaraPath = Path.Combine(DefaultToolsPath, "yara", "yara64.exe"),
                RulesDirectory = Path.Combine(DefaultToolsPath, "yara", "rules")
            };
        }

        /// <summary>
        /// Creates a new OpenSearchService with default configuration.
        /// </summary>
        public static OpenSearchService CreateOpenSearchService()
        {
            return new OpenSearchService
            {
                ServerUrl = "http://localhost:9200",
                IndexPrefix = "dfir"
            };
        }

        /// <summary>
        /// Creates a new TaskSchedulerService.
        /// </summary>
        public static TaskSchedulerService CreateTaskSchedulerService()
        {
            return new TaskSchedulerService();
        }

        /// <summary>
        /// Creates a new BrowserForensicsService with default configuration.
        /// </summary>
        public static BrowserForensicsService CreateBrowserForensicsService()
        {
            return new BrowserForensicsService();
        }

        /// <summary>
        /// Creates a new IOCScannerService with default configuration.
        /// </summary>
        public static IOCScannerService CreateIOCScannerService()
        {
            return new IOCScannerService();
        }

        /// <summary>
        /// Creates a new RegistryDiffService with default configuration.
        /// </summary>
        public static RegistryDiffService CreateRegistryDiffService()
        {
            return new RegistryDiffService();
        }

        /// <summary>
        /// Creates a new PcapParserService with default configuration.
        /// </summary>
        public static PcapParserService CreatePcapParserService()
        {
            return new PcapParserService();
        }

        /// <summary>
        /// Ensures all default directories exist.
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(DefaultOutputPath);
            Directory.CreateDirectory(DefaultToolsPath);
        }

        /// <summary>
        /// Gets the default path for a specific tool.
        /// </summary>
        public static string GetToolPath(string toolName)
        {
            return Path.Combine(DefaultToolsPath, toolName);
        }

        /// <summary>
        /// Gets the default output path for a specific artifact type.
        /// </summary>
        public static string GetOutputPath(string artifactType)
        {
            return Path.Combine(DefaultOutputPath, artifactType);
        }
    }
}
