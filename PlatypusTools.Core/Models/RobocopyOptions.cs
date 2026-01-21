using System;
using System.Collections.Generic;
using System.Text;

namespace PlatypusTools.Core.Models
{
    /// <summary>
    /// Represents a Robocopy switch with its documentation.
    /// </summary>
    public class RobocopySwitch
    {
        public string Switch { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public string? Value { get; set; }
        public bool RequiresValue { get; set; }
        public string? ValueHint { get; set; }
    }

    /// <summary>
    /// Result of a Robocopy operation.
    /// </summary>
    public class RobocopyResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string ExitCodeDescription { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public string SourceDirectory { get; set; } = string.Empty;
        public string DestinationDirectory { get; set; } = string.Empty;
        public string CommandLine { get; set; } = string.Empty;
        public List<string> Output { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<RobocopiedFile> FilesCopied { get; set; } = new();
        public List<RobocopyFailedFile> FilesFailed { get; set; } = new();
        public RobocopyStatistics Statistics { get; set; } = new();
    }

    /// <summary>
    /// A file that was copied by Robocopy.
    /// </summary>
    public class RobocopiedFile
    {
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// A file that failed to copy.
    /// </summary>
    public class RobocopyFailedFile
    {
        public string Path { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int RetryCount { get; set; }
    }

    /// <summary>
    /// Statistics from a Robocopy operation.
    /// </summary>
    public class RobocopyStatistics
    {
        public int DirectoriesTotal { get; set; }
        public int DirectoriesCopied { get; set; }
        public int DirectoriesSkipped { get; set; }
        public int DirectoriesFailed { get; set; }
        public int FilesTotal { get; set; }
        public int FilesCopied { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesFailed { get; set; }
        public long BytesTotal { get; set; }
        public long BytesCopied { get; set; }
        public long BytesSkipped { get; set; }
        public long BytesFailed { get; set; }

        public string BytesTotalFormatted => FormatBytes(BytesTotal);
        public string BytesCopiedFormatted => FormatBytes(BytesCopied);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// Provides all available Robocopy switches organized by category.
    /// </summary>
    public static class RobocopySwitches
    {
        public static List<RobocopySwitch> GetAllSwitches()
        {
            return new List<RobocopySwitch>
            {
                // Copy Options
                new() { Switch = "/S", Category = "Copy Options", Description = "Copy subdirectories, but not empty ones" },
                new() { Switch = "/E", Category = "Copy Options", Description = "Copy subdirectories, including empty ones" },
                new() { Switch = "/LEV:n", Category = "Copy Options", Description = "Only copy the top n levels of the source directory tree", RequiresValue = true, ValueHint = "n (depth)" },
                new() { Switch = "/Z", Category = "Copy Options", Description = "Copy files in restartable mode (survive network glitch)" },
                new() { Switch = "/B", Category = "Copy Options", Description = "Copy files in Backup mode (requires backup privileges)" },
                new() { Switch = "/ZB", Category = "Copy Options", Description = "Use restartable mode; if access denied use Backup mode" },
                new() { Switch = "/J", Category = "Copy Options", Description = "Copy using unbuffered I/O (recommended for large files)" },
                new() { Switch = "/EFSRAW", Category = "Copy Options", Description = "Copy all encrypted files in EFS RAW mode" },
                new() { Switch = "/COPY:copyflag[s]", Category = "Copy Options", Description = "What to copy (D=Data, A=Attributes, T=Timestamps, S=Security, O=Owner, U=Auditing)", RequiresValue = true, ValueHint = "DAT, DATS, DATSO, etc." },
                new() { Switch = "/DCOPY:copyflag[s]", Category = "Copy Options", Description = "What to copy for directories (D=Data, A=Attributes, T=Timestamps)", RequiresValue = true, ValueHint = "DA, DAT, etc." },
                new() { Switch = "/SEC", Category = "Copy Options", Description = "Copy files with security (equivalent to /COPY:DATS)" },
                new() { Switch = "/COPYALL", Category = "Copy Options", Description = "Copy ALL file info (equivalent to /COPY:DATSOU)" },
                new() { Switch = "/NOCOPY", Category = "Copy Options", Description = "Copy NO file info (useful with /PURGE)" },
                new() { Switch = "/SECFIX", Category = "Copy Options", Description = "Fix file security on all files, even skipped files" },
                new() { Switch = "/TIMFIX", Category = "Copy Options", Description = "Fix file times on all files, even skipped files" },
                new() { Switch = "/PURGE", Category = "Copy Options", Description = "Delete dest files/dirs that no longer exist in source" },
                new() { Switch = "/MIR", Category = "Copy Options", Description = "Mirror a directory tree (equivalent to /E plus /PURGE)" },
                new() { Switch = "/MOV", Category = "Copy Options", Description = "Move files (delete from source after copying)" },
                new() { Switch = "/MOVE", Category = "Copy Options", Description = "Move files AND dirs (delete from source after copying)" },
                new() { Switch = "/A+:[RASHCNET]", Category = "Copy Options", Description = "Add the given attributes to copied files", RequiresValue = true, ValueHint = "R, A, S, H, C, N, E, T" },
                new() { Switch = "/A-:[RASHCNET]", Category = "Copy Options", Description = "Remove the given attributes from copied files", RequiresValue = true, ValueHint = "R, A, S, H, C, N, E, T" },
                new() { Switch = "/CREATE", Category = "Copy Options", Description = "Create directory tree and zero-length files only" },
                new() { Switch = "/FAT", Category = "Copy Options", Description = "Create destination files using 8.3 FAT file names only" },
                new() { Switch = "/256", Category = "Copy Options", Description = "Turn off very long path (>256 characters) support" },
                new() { Switch = "/MON:n", Category = "Copy Options", Description = "Monitor source; run again when more than n changes seen", RequiresValue = true, ValueHint = "n (changes)" },
                new() { Switch = "/MOT:m", Category = "Copy Options", Description = "Monitor source; run again in m minutes time, if changed", RequiresValue = true, ValueHint = "m (minutes)" },
                new() { Switch = "/RH:hhmm-hhmm", Category = "Copy Options", Description = "Run hours - times when new copies may be started", RequiresValue = true, ValueHint = "hhmm-hhmm" },
                new() { Switch = "/PF", Category = "Copy Options", Description = "Check run hours on a per file (not per pass) basis" },
                new() { Switch = "/IPG:n", Category = "Copy Options", Description = "Inter-packet gap (ms), to free bandwidth on slow lines", RequiresValue = true, ValueHint = "n (milliseconds)" },
                new() { Switch = "/SL", Category = "Copy Options", Description = "Copy symbolic links versus the target" },
                new() { Switch = "/MT[:n]", Category = "Copy Options", Description = "Multi-threaded copying, n = number of threads (default 8)", RequiresValue = true, ValueHint = "n (1-128, default 8)" },
                new() { Switch = "/NODCOPY", Category = "Copy Options", Description = "Copy NO directory info" },
                new() { Switch = "/NOOFFLOAD", Category = "Copy Options", Description = "Copy files without using the Windows Copy Offload mechanism" },
                new() { Switch = "/COMPRESS", Category = "Copy Options", Description = "Request network compression during file transfer (if applicable)" },

                // File Selection Options
                new() { Switch = "/A", Category = "File Selection", Description = "Copy only files with the Archive attribute set" },
                new() { Switch = "/M", Category = "File Selection", Description = "Copy only files with Archive attribute and reset it" },
                new() { Switch = "/IA:[RASHCNETO]", Category = "File Selection", Description = "Include only files with any of the given attributes", RequiresValue = true, ValueHint = "R, A, S, H, C, N, E, T, O" },
                new() { Switch = "/XA:[RASHCNETO]", Category = "File Selection", Description = "Exclude files with any of the given attributes", RequiresValue = true, ValueHint = "R, A, S, H, C, N, E, T, O" },
                new() { Switch = "/XF file [file]...", Category = "File Selection", Description = "Exclude files matching given names/paths/wildcards", RequiresValue = true, ValueHint = "*.tmp *.log" },
                new() { Switch = "/XD dirs [dirs]...", Category = "File Selection", Description = "Exclude directories matching given names/paths", RequiresValue = true, ValueHint = "temp backup" },
                new() { Switch = "/XC", Category = "File Selection", Description = "Exclude changed files" },
                new() { Switch = "/XN", Category = "File Selection", Description = "Exclude newer files" },
                new() { Switch = "/XO", Category = "File Selection", Description = "Exclude older files" },
                new() { Switch = "/XX", Category = "File Selection", Description = "Exclude extra files and directories" },
                new() { Switch = "/XL", Category = "File Selection", Description = "Exclude lonely files and directories" },
                new() { Switch = "/IS", Category = "File Selection", Description = "Include same files" },
                new() { Switch = "/IT", Category = "File Selection", Description = "Include tweaked files" },
                new() { Switch = "/MAX:n", Category = "File Selection", Description = "Maximum file size - exclude files bigger than n bytes", RequiresValue = true, ValueHint = "n (bytes)" },
                new() { Switch = "/MIN:n", Category = "File Selection", Description = "Minimum file size - exclude files smaller than n bytes", RequiresValue = true, ValueHint = "n (bytes)" },
                new() { Switch = "/MAXAGE:n", Category = "File Selection", Description = "Maximum file age - exclude files older than n days/date", RequiresValue = true, ValueHint = "n (days) or YYYYMMDD" },
                new() { Switch = "/MINAGE:n", Category = "File Selection", Description = "Minimum file age - exclude files newer than n days/date", RequiresValue = true, ValueHint = "n (days) or YYYYMMDD" },
                new() { Switch = "/MAXLAD:n", Category = "File Selection", Description = "Maximum last access date - exclude files unused since n", RequiresValue = true, ValueHint = "n (days) or YYYYMMDD" },
                new() { Switch = "/MINLAD:n", Category = "File Selection", Description = "Minimum last access date - exclude files used since n", RequiresValue = true, ValueHint = "n (days) or YYYYMMDD" },
                new() { Switch = "/FFT", Category = "File Selection", Description = "Assume FAT file times (2-second granularity)" },
                new() { Switch = "/DST", Category = "File Selection", Description = "Compensate for one-hour DST time differences" },
                new() { Switch = "/XJ", Category = "File Selection", Description = "Exclude junction points (normally included by default)" },
                new() { Switch = "/XJD", Category = "File Selection", Description = "Exclude junction points for directories" },
                new() { Switch = "/XJF", Category = "File Selection", Description = "Exclude junction points for files" },
                new() { Switch = "/IM", Category = "File Selection", Description = "Include modified files (differing change times)" },

                // Retry Options
                new() { Switch = "/R:n", Category = "Retry Options", Description = "Number of retries on failed copies (default is 1 million)", RequiresValue = true, ValueHint = "n (retries, 0-999999)" },
                new() { Switch = "/W:n", Category = "Retry Options", Description = "Wait time between retries in seconds (default is 30)", RequiresValue = true, ValueHint = "n (seconds)" },
                new() { Switch = "/REG", Category = "Retry Options", Description = "Save /R:n and /W:n in the registry as default settings" },
                new() { Switch = "/TBD", Category = "Retry Options", Description = "Wait for sharenames to be defined (retry error 67)" },
                new() { Switch = "/LFSM", Category = "Retry Options", Description = "Operate in low free space mode, enabling copy pause and resume" },
                new() { Switch = "/LFSM:n[KMG]", Category = "Retry Options", Description = "Low free space mode with floor specified", RequiresValue = true, ValueHint = "n (size in K/M/G)" },

                // Logging Options
                new() { Switch = "/L", Category = "Logging Options", Description = "List only - don't copy, timestamp or delete any files" },
                new() { Switch = "/X", Category = "Logging Options", Description = "Report all extra files, not just those selected" },
                new() { Switch = "/V", Category = "Logging Options", Description = "Produce verbose output, showing skipped files" },
                new() { Switch = "/TS", Category = "Logging Options", Description = "Include source file timestamps in the output" },
                new() { Switch = "/FP", Category = "Logging Options", Description = "Include full pathname of files in the output" },
                new() { Switch = "/BYTES", Category = "Logging Options", Description = "Print sizes as bytes" },
                new() { Switch = "/NS", Category = "Logging Options", Description = "No size - don't log file sizes" },
                new() { Switch = "/NC", Category = "Logging Options", Description = "No class - don't log file classes" },
                new() { Switch = "/NFL", Category = "Logging Options", Description = "No file list - don't log file names" },
                new() { Switch = "/NDL", Category = "Logging Options", Description = "No directory list - don't log directory names" },
                new() { Switch = "/NP", Category = "Logging Options", Description = "No progress - don't display percentage copied" },
                new() { Switch = "/ETA", Category = "Logging Options", Description = "Show estimated time of arrival of copied files" },
                new() { Switch = "/LOG:file", Category = "Logging Options", Description = "Output status to log file (overwrite existing log)", RequiresValue = true, ValueHint = "filepath" },
                new() { Switch = "/LOG+:file", Category = "Logging Options", Description = "Output status to log file (append to existing log)", RequiresValue = true, ValueHint = "filepath" },
                new() { Switch = "/UNILOG:file", Category = "Logging Options", Description = "Output status to Unicode log file (overwrite)", RequiresValue = true, ValueHint = "filepath" },
                new() { Switch = "/UNILOG+:file", Category = "Logging Options", Description = "Output status to Unicode log file (append)", RequiresValue = true, ValueHint = "filepath" },
                new() { Switch = "/TEE", Category = "Logging Options", Description = "Output to console window, as well as the log file" },
                new() { Switch = "/NJH", Category = "Logging Options", Description = "No job header" },
                new() { Switch = "/NJS", Category = "Logging Options", Description = "No job summary" },
                new() { Switch = "/UNICODE", Category = "Logging Options", Description = "Output status as Unicode" },

                // Job Options
                new() { Switch = "/JOB:jobname", Category = "Job Options", Description = "Take parameters from the named job file", RequiresValue = true, ValueHint = "jobname" },
                new() { Switch = "/SAVE:jobname", Category = "Job Options", Description = "Save parameters to the named job file", RequiresValue = true, ValueHint = "jobname" },
                new() { Switch = "/QUIT", Category = "Job Options", Description = "Quit after processing command line (to view parameters)" },
                new() { Switch = "/NOSD", Category = "Job Options", Description = "No source directory is specified" },
                new() { Switch = "/NODD", Category = "Job Options", Description = "No destination directory is specified" },
                new() { Switch = "/IF", Category = "Job Options", Description = "Include the following files" },
            };
        }

        /// <summary>
        /// Gets exit code description for Robocopy.
        /// </summary>
        public static string GetExitCodeDescription(int exitCode)
        {
            return exitCode switch
            {
                0 => "No files were copied. No errors occurred. No files were mismatched.",
                1 => "All files were copied successfully.",
                2 => "Extra files or directories were detected. No files were copied.",
                3 => "Some files were copied. Additional files were present.",
                4 => "Mismatched files or directories were detected.",
                5 => "Some files were copied. Some files were mismatched.",
                6 => "Additional files and mismatched files exist.",
                7 => "Files were copied, a file mismatch was present, and additional files were present.",
                8 => "Several files did not copy.",
                9 => "Some files were copied. Some files failed to copy.",
                10 => "Some files failed to copy. Extra files or directories were detected.",
                11 => "Some files were copied. Some files failed. Extra files were detected.",
                12 => "Some files failed. Mismatched files were detected.",
                13 => "Some files were copied. Some files failed. Mismatched files were detected.",
                14 => "Some files failed. Extra files and mismatched files were detected.",
                15 => "Some files were copied. Some files failed. Extra and mismatched files were detected.",
                16 => "Serious error. No files were copied.",
                _ => $"Unknown exit code: {exitCode}"
            };
        }

        /// <summary>
        /// Determines if the exit code indicates success (no serious errors).
        /// </summary>
        public static bool IsSuccessExitCode(int exitCode)
        {
            // Exit codes 0-7 are generally considered success
            // Exit codes 8+ indicate some files failed to copy
            // Exit code 16 is a serious error
            return exitCode >= 0 && exitCode <= 7;
        }
    }
}
