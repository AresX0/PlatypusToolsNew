using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service to check if the system meets minimum requirements to run PlatypusTools.
    /// Should be called before GUI initialization.
    /// </summary>
    public class SystemRequirementsService
    {
        #region Minimum Requirements Constants

        /// <summary>Minimum Windows 10 build (19041 = 2004/May 2020 Update)</summary>
        public const int MinWindowsBuild = 19041;

        /// <summary>Minimum RAM in GB</summary>
        public const int MinRamGB = 4;

        /// <summary>Recommended RAM in GB</summary>
        public const int RecommendedRamGB = 8;

        /// <summary>Minimum CPU cores</summary>
        public const int MinCpuCores = 2;

        /// <summary>Recommended CPU cores</summary>
        public const int RecommendedCpuCores = 4;

        /// <summary>Minimum disk space in MB</summary>
        public const int MinDiskSpaceMB = 500;

        /// <summary>Minimum screen width</summary>
        public const int MinScreenWidth = 1280;

        /// <summary>Minimum screen height</summary>
        public const int MinScreenHeight = 720;

        #endregion

        #region Result Classes

        public class RequirementCheckResult
        {
            public string Name { get; set; } = string.Empty;
            public bool IsMet { get; set; }
            public bool IsRecommendation { get; set; }
            public string CurrentValue { get; set; } = string.Empty;
            public string RequiredValue { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public RequirementSeverity Severity { get; set; } = RequirementSeverity.Info;
        }

        public enum RequirementSeverity
        {
            Info,
            Warning,
            Critical
        }

        public class SystemRequirementsResult
        {
            public bool MeetsMinimumRequirements { get; set; }
            public bool MeetsRecommendedRequirements { get; set; }
            public List<RequirementCheckResult> Checks { get; set; } = new();
            public List<RequirementCheckResult> FailedChecks => Checks.FindAll(c => !c.IsMet && !c.IsRecommendation);
            public List<RequirementCheckResult> Warnings => Checks.FindAll(c => !c.IsMet && c.IsRecommendation);
            public SystemInfo DetectedSystem { get; set; } = new();
        }

        public class SystemInfo
        {
            public string OsName { get; set; } = string.Empty;
            public string OsVersion { get; set; } = string.Empty;
            public int OsBuild { get; set; }
            public string Architecture { get; set; } = string.Empty;
            public string CpuName { get; set; } = string.Empty;
            public int CpuCores { get; set; }
            public int CpuThreads { get; set; }
            public double RamGB { get; set; }
            public string GpuName { get; set; } = string.Empty;
            public long GpuMemoryMB { get; set; }
            public bool HasDirectX11 { get; set; }
            public long AvailableDiskSpaceMB { get; set; }
            public int ScreenWidth { get; set; }
            public int ScreenHeight { get; set; }
        }

        #endregion

        #region Main Check Method

        /// <summary>
        /// Performs all system requirement checks.
        /// </summary>
        public SystemRequirementsResult CheckRequirements()
        {
            var result = new SystemRequirementsResult();

            // Gather system info
            result.DetectedSystem = GatherSystemInfo();

            // Perform checks
            result.Checks.Add(CheckArchitecture(result.DetectedSystem));
            result.Checks.Add(CheckWindowsVersion(result.DetectedSystem));
            result.Checks.Add(CheckRamMinimum(result.DetectedSystem));
            result.Checks.Add(CheckRamRecommended(result.DetectedSystem));
            result.Checks.Add(CheckCpuCoresMinimum(result.DetectedSystem));
            result.Checks.Add(CheckCpuCoresRecommended(result.DetectedSystem));
            result.Checks.Add(CheckDiskSpace(result.DetectedSystem));
            result.Checks.Add(CheckGpu(result.DetectedSystem));
            result.Checks.Add(CheckScreenResolution(result.DetectedSystem));

            // Determine overall result
            result.MeetsMinimumRequirements = result.FailedChecks.Count == 0;
            result.MeetsRecommendedRequirements = result.MeetsMinimumRequirements && result.Warnings.Count == 0;

            return result;
        }

        #endregion

        #region System Info Gathering

        private SystemInfo GatherSystemInfo()
        {
            var info = new SystemInfo();

            try
            {
                // OS Info
                info.OsName = Environment.OSVersion.ToString();
                info.OsVersion = Environment.OSVersion.Version.ToString();
                info.OsBuild = Environment.OSVersion.Version.Build;
                info.Architecture = RuntimeInformation.OSArchitecture.ToString();

                // CPU Info
                info.CpuThreads = Environment.ProcessorCount;
                info.CpuCores = GetPhysicalCoreCount();
                info.CpuName = GetCpuName();

                // RAM Info
                info.RamGB = GetTotalRamGB();

                // GPU Info
                (info.GpuName, info.GpuMemoryMB) = GetGpuInfo();
                info.HasDirectX11 = CheckDirectX11Support();

                // Disk Space
                info.AvailableDiskSpaceMB = GetAvailableDiskSpaceMB();

                // Screen Resolution
                (info.ScreenWidth, info.ScreenHeight) = GetPrimaryScreenResolution();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error gathering system info: {ex.Message}");
            }

            return info;
        }

        private int GetPhysicalCoreCount()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    return Convert.ToInt32(obj["NumberOfCores"]);
                }
            }
            catch { }
            
            // Fallback: assume half of logical processors
            return Math.Max(1, Environment.ProcessorCount / 2);
        }

        private string GetCpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    return obj["Name"]?.ToString() ?? "Unknown CPU";
                }
            }
            catch { }
            return "Unknown CPU";
        }

        private double GetTotalRamGB()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    var bytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                    return Math.Round(bytes / (1024.0 * 1024.0 * 1024.0), 2);
                }
            }
            catch { }
            return 0;
        }

        private (string Name, long MemoryMB) GetGpuInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                    var ram = Convert.ToInt64(obj["AdapterRAM"] ?? 0) / (1024 * 1024);
                    return (name, ram);
                }
            }
            catch { }
            return ("Unknown GPU", 0);
        }

        private bool CheckDirectX11Support()
        {
            // Check if D3D11.dll exists (indicates DirectX 11 support)
            var d3d11Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "d3d11.dll");
            return File.Exists(d3d11Path);
        }

        private long GetAvailableDiskSpaceMB()
        {
            try
            {
                var appPath = AppDomain.CurrentDomain.BaseDirectory;
                var drive = new DriveInfo(Path.GetPathRoot(appPath) ?? "C:\\");
                return drive.AvailableFreeSpace / (1024 * 1024);
            }
            catch { }
            return 0;
        }

        private (int Width, int Height) GetPrimaryScreenResolution()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController WHERE CurrentHorizontalResolution IS NOT NULL");
                foreach (var obj in searcher.Get())
                {
                    var width = Convert.ToInt32(obj["CurrentHorizontalResolution"] ?? 0);
                    var height = Convert.ToInt32(obj["CurrentVerticalResolution"] ?? 0);
                    if (width > 0 && height > 0)
                        return (width, height);
                }
            }
            catch { }
            
            // Fallback to a reasonable default
            return (1920, 1080);
        }

        #endregion

        #region Individual Checks

        private RequirementCheckResult CheckArchitecture(SystemInfo info)
        {
            var is64Bit = info.Architecture == "X64" || info.Architecture == "Arm64";
            return new RequirementCheckResult
            {
                Name = "64-bit Architecture",
                IsMet = is64Bit,
                IsRecommendation = false,
                CurrentValue = info.Architecture,
                RequiredValue = "x64 or ARM64",
                Message = is64Bit 
                    ? $"Running on {info.Architecture}" 
                    : "PlatypusTools requires a 64-bit operating system",
                Severity = is64Bit ? RequirementSeverity.Info : RequirementSeverity.Critical
            };
        }

        private RequirementCheckResult CheckWindowsVersion(SystemInfo info)
        {
            var isMet = info.OsBuild >= MinWindowsBuild;
            return new RequirementCheckResult
            {
                Name = "Windows Version",
                IsMet = isMet,
                IsRecommendation = false,
                CurrentValue = $"Build {info.OsBuild}",
                RequiredValue = $"Build {MinWindowsBuild}+ (Windows 10 2004 or later)",
                Message = isMet 
                    ? $"Windows build {info.OsBuild} detected" 
                    : $"Windows 10 build {MinWindowsBuild} or later is required. Current build: {info.OsBuild}",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Critical
            };
        }

        private RequirementCheckResult CheckRamMinimum(SystemInfo info)
        {
            var isMet = info.RamGB >= MinRamGB;
            return new RequirementCheckResult
            {
                Name = "RAM (Minimum)",
                IsMet = isMet,
                IsRecommendation = false,
                CurrentValue = $"{info.RamGB:F1} GB",
                RequiredValue = $"{MinRamGB} GB minimum",
                Message = isMet 
                    ? $"{info.RamGB:F1} GB RAM detected" 
                    : $"At least {MinRamGB} GB RAM is required. You have {info.RamGB:F1} GB",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Critical
            };
        }

        private RequirementCheckResult CheckRamRecommended(SystemInfo info)
        {
            var isMet = info.RamGB >= RecommendedRamGB;
            return new RequirementCheckResult
            {
                Name = "RAM (Recommended)",
                IsMet = isMet,
                IsRecommendation = true,
                CurrentValue = $"{info.RamGB:F1} GB",
                RequiredValue = $"{RecommendedRamGB} GB recommended",
                Message = isMet 
                    ? $"{info.RamGB:F1} GB RAM detected" 
                    : $"{RecommendedRamGB} GB RAM is recommended for best performance. You have {info.RamGB:F1} GB",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Warning
            };
        }

        private RequirementCheckResult CheckCpuCoresMinimum(SystemInfo info)
        {
            var isMet = info.CpuCores >= MinCpuCores;
            return new RequirementCheckResult
            {
                Name = "CPU Cores (Minimum)",
                IsMet = isMet,
                IsRecommendation = false,
                CurrentValue = $"{info.CpuCores} cores ({info.CpuThreads} threads)",
                RequiredValue = $"{MinCpuCores} cores minimum",
                Message = isMet 
                    ? $"{info.CpuName} with {info.CpuCores} cores detected" 
                    : $"At least {MinCpuCores} CPU cores required. You have {info.CpuCores}",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Critical
            };
        }

        private RequirementCheckResult CheckCpuCoresRecommended(SystemInfo info)
        {
            var isMet = info.CpuCores >= RecommendedCpuCores;
            return new RequirementCheckResult
            {
                Name = "CPU Cores (Recommended)",
                IsMet = isMet,
                IsRecommendation = true,
                CurrentValue = $"{info.CpuCores} cores",
                RequiredValue = $"{RecommendedCpuCores}+ cores recommended",
                Message = isMet 
                    ? $"{info.CpuCores} cores - good for parallel processing" 
                    : $"{RecommendedCpuCores}+ cores recommended for best performance with parallel features",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Warning
            };
        }

        private RequirementCheckResult CheckDiskSpace(SystemInfo info)
        {
            var isMet = info.AvailableDiskSpaceMB >= MinDiskSpaceMB;
            var spaceGB = info.AvailableDiskSpaceMB / 1024.0;
            return new RequirementCheckResult
            {
                Name = "Disk Space",
                IsMet = isMet,
                IsRecommendation = false,
                CurrentValue = $"{spaceGB:F1} GB available",
                RequiredValue = $"{MinDiskSpaceMB} MB minimum",
                Message = isMet 
                    ? $"{spaceGB:F1} GB available" 
                    : $"At least {MinDiskSpaceMB} MB disk space required. You have {info.AvailableDiskSpaceMB} MB",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Critical
            };
        }

        private RequirementCheckResult CheckGpu(SystemInfo info)
        {
            var isMet = info.HasDirectX11;
            return new RequirementCheckResult
            {
                Name = "GPU / DirectX 11",
                IsMet = isMet,
                IsRecommendation = false,
                CurrentValue = info.GpuName,
                RequiredValue = "DirectX 11 compatible GPU",
                Message = isMet 
                    ? $"{info.GpuName} - DirectX 11 supported" 
                    : "DirectX 11 compatible GPU is required for rendering",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Critical
            };
        }

        private RequirementCheckResult CheckScreenResolution(SystemInfo info)
        {
            var isMet = info.ScreenWidth >= MinScreenWidth && info.ScreenHeight >= MinScreenHeight;
            return new RequirementCheckResult
            {
                Name = "Screen Resolution",
                IsMet = isMet,
                IsRecommendation = true,
                CurrentValue = $"{info.ScreenWidth}×{info.ScreenHeight}",
                RequiredValue = $"{MinScreenWidth}×{MinScreenHeight} minimum",
                Message = isMet 
                    ? $"Screen resolution: {info.ScreenWidth}×{info.ScreenHeight}" 
                    : $"Screen resolution of {MinScreenWidth}×{MinScreenHeight} or higher recommended",
                Severity = isMet ? RequirementSeverity.Info : RequirementSeverity.Warning
            };
        }

        #endregion

        #region Quick Check (Static)

        /// <summary>
        /// Quick check for critical requirements only. Returns true if can run.
        /// </summary>
        public static bool QuickCheck(out string failureReason)
        {
            failureReason = string.Empty;

            // Check 64-bit
            if (RuntimeInformation.OSArchitecture != Architecture.X64 && 
                RuntimeInformation.OSArchitecture != Architecture.Arm64)
            {
                failureReason = "PlatypusTools requires a 64-bit operating system.";
                return false;
            }

            // Check Windows version
            if (Environment.OSVersion.Version.Build < MinWindowsBuild)
            {
                failureReason = $"Windows 10 build {MinWindowsBuild} or later is required.";
                return false;
            }

            // Check DirectX 11
            var d3d11Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "d3d11.dll");
            if (!File.Exists(d3d11Path))
            {
                failureReason = "DirectX 11 is required but not detected.";
                return false;
            }

            return true;
        }

        #endregion
    }
}
