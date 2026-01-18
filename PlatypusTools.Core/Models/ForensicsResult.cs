using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models
{
    /// <summary>
    /// Represents the analysis mode for forensic operations.
    /// </summary>
    public enum ForensicsMode
    {
        /// <summary>
        /// Quick scan using single core, ~100MB output.
        /// </summary>
        Lightweight,

        /// <summary>
        /// Deep analysis using multiple cores, several GB output.
        /// </summary>
        Deep
    }

    /// <summary>
    /// Represents the type of forensic finding.
    /// </summary>
    public enum ForensicsFindingType
    {
        Memory,
        FileSystem,
        Registry,
        EventLog,
        Network,
        Process,
        Service,
        Startup,
        Scheduled,
        Anomaly
    }

    /// <summary>
    /// Severity level of a forensic finding.
    /// </summary>
    public enum ForensicsSeverity
    {
        Info,
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Represents a single forensic finding.
    /// </summary>
    public class ForensicsFinding
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
        public ForensicsFindingType Type { get; set; }
        public ForensicsSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Hash { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();

        // Display properties
        public string TypeIcon => Type switch
        {
            ForensicsFindingType.Memory => "🧠",
            ForensicsFindingType.FileSystem => "📁",
            ForensicsFindingType.Registry => "📝",
            ForensicsFindingType.EventLog => "📋",
            ForensicsFindingType.Network => "🌐",
            ForensicsFindingType.Process => "⚙️",
            ForensicsFindingType.Service => "🔧",
            ForensicsFindingType.Startup => "🚀",
            ForensicsFindingType.Scheduled => "📅",
            ForensicsFindingType.Anomaly => "⚠️",
            _ => "❓"
        };

        public string SeverityDisplay => Severity switch
        {
            ForensicsSeverity.Critical => "🔴 Critical",
            ForensicsSeverity.High => "🟠 High",
            ForensicsSeverity.Medium => "🟡 Medium",
            ForensicsSeverity.Low => "🟢 Low",
            ForensicsSeverity.Info => "🔵 Info",
            _ => "⚪ Unknown"
        };
    }

    /// <summary>
    /// Memory analysis results.
    /// </summary>
    public class MemoryAnalysisResult
    {
        public long TotalPhysicalMemory { get; set; }
        public long AvailableMemory { get; set; }
        public long UsedMemory => TotalPhysicalMemory - AvailableMemory;
        public double UsagePercent => TotalPhysicalMemory > 0 ? (double)UsedMemory / TotalPhysicalMemory * 100 : 0;
        public List<ProcessMemoryInfo> TopProcesses { get; set; } = new();
        public List<ProcessMemoryInfo> SuspiciousProcesses { get; set; } = new();
        public List<ForensicsFinding> Findings { get; set; } = new();
    }

    /// <summary>
    /// Process memory information.
    /// </summary>
    public class ProcessMemoryInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public long WorkingSet { get; set; }
        public long PrivateBytes { get; set; }
        public long VirtualBytes { get; set; }
        public string CommandLine { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public bool IsSuspicious { get; set; }
        public string SuspicionReason { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;

        public string WorkingSetFormatted => FormatBytes(WorkingSet);
        public string PrivateBytesFormatted => FormatBytes(PrivateBytes);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// File system examination results.
    /// </summary>
    public class FileSystemAnalysisResult
    {
        public int TotalFilesScanned { get; set; }
        public int SuspiciousFilesFound { get; set; }
        public int RecentlyModifiedFiles { get; set; }
        public int HiddenFilesFound { get; set; }
        public int ExecutablesInTemp { get; set; }
        public List<SuspiciousFileInfo> SuspiciousFiles { get; set; } = new();
        public List<RecentFileInfo> RecentlyModified { get; set; } = new();
        public List<ForensicsFinding> Findings { get; set; } = new();
    }

    /// <summary>
    /// Suspicious file information.
    /// </summary>
    public class SuspiciousFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ModifiedTime { get; set; }
        public DateTime AccessedTime { get; set; }
        public string Hash { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public bool IsSystem { get; set; }
        public bool HasValidSignature { get; set; }
        public string SignerName { get; set; } = string.Empty;

        public string FileSizeFormatted => FormatBytes(FileSize);

        private static string FormatBytes(long bytes)
        {
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
    }

    /// <summary>
    /// Recently modified file information.
    /// </summary>
    public class RecentFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime ModifiedTime { get; set; }
        public long FileSize { get; set; }
        public string ChangeType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Registry analysis results.
    /// </summary>
    public class RegistryAnalysisResult
    {
        public int TotalKeysScanned { get; set; }
        public int SuspiciousEntriesFound { get; set; }
        public int StartupEntriesFound { get; set; }
        public int RecentlyModifiedKeys { get; set; }
        public List<RegistryEntryInfo> StartupEntries { get; set; } = new();
        public List<RegistryEntryInfo> SuspiciousEntries { get; set; } = new();
        public List<RegistryEntryInfo> RecentlyModified { get; set; } = new();
        public List<ForensicsFinding> Findings { get; set; } = new();
    }

    /// <summary>
    /// Registry entry information.
    /// </summary>
    public class RegistryEntryInfo
    {
        public string KeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string ValueData { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
        public DateTime? LastModified { get; set; }
        public bool IsSuspicious { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Log aggregation results.
    /// </summary>
    public class LogAnalysisResult
    {
        public int TotalEventsScanned { get; set; }
        public int SecurityEventsFound { get; set; }
        public int ErrorEventsFound { get; set; }
        public int WarningEventsFound { get; set; }
        public int SuspiciousEventsFound { get; set; }
        public List<EventLogEntry> SecurityEvents { get; set; } = new();
        public List<EventLogEntry> SuspiciousEvents { get; set; } = new();
        public List<EventLogEntry> RecentErrors { get; set; } = new();
        public List<ForensicsFinding> Findings { get; set; } = new();
    }

    /// <summary>
    /// Event log entry information.
    /// </summary>
    public class EventLogEntry
    {
        public long RecordId { get; set; }
        public string LogName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public int EventId { get; set; }
        public string Level { get; set; } = string.Empty;
        public DateTime TimeGenerated { get; set; }
        public string Message { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Computer { get; set; } = string.Empty;
        public bool IsSuspicious { get; set; }
        public string SuspicionReason { get; set; } = string.Empty;

        public string LevelIcon => Level switch
        {
            "Error" => "❌",
            "Warning" => "⚠️",
            "Information" => "ℹ️",
            "Critical" => "🔴",
            "Verbose" => "📝",
            _ => "❓"
        };
    }

    /// <summary>
    /// Complete forensics analysis result.
    /// </summary>
    public class ForensicsAnalysisResult
    {
        public string AnalysisId { get; set; } = Guid.NewGuid().ToString("N")[..12].ToUpper();
        public ForensicsMode Mode { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public string ComputerName { get; set; } = Environment.MachineName;
        public string UserName { get; set; } = Environment.UserName;
        public string OsVersion { get; set; } = Environment.OSVersion.ToString();

        // Component Results
        public MemoryAnalysisResult? MemoryAnalysis { get; set; }
        public FileSystemAnalysisResult? FileSystemAnalysis { get; set; }
        public RegistryAnalysisResult? RegistryAnalysis { get; set; }
        public LogAnalysisResult? LogAnalysis { get; set; }

        // Aggregated Findings
        public List<ForensicsFinding> AllFindings { get; set; } = new();

        // Summary Statistics
        public int TotalFindings => AllFindings.Count;
        public int CriticalFindings => AllFindings.Count(f => f.Severity == ForensicsSeverity.Critical);
        public int HighFindings => AllFindings.Count(f => f.Severity == ForensicsSeverity.High);
        public int MediumFindings => AllFindings.Count(f => f.Severity == ForensicsSeverity.Medium);
        public int LowFindings => AllFindings.Count(f => f.Severity == ForensicsSeverity.Low);
        public int InfoFindings => AllFindings.Count(f => f.Severity == ForensicsSeverity.Info);

        // Output file info
        public string OutputPath { get; set; } = string.Empty;
        public long OutputSizeBytes { get; set; }
        public string OutputSizeFormatted => FormatBytes(OutputSizeBytes);

        private static string FormatBytes(long bytes)
        {
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
    }
}
