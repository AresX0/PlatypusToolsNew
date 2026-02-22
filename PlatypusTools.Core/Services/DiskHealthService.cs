using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Provides SMART health data and disk information via WMI/CIM.
    /// </summary>
    public class DiskHealthService
    {
        /// <summary>
        /// Gets health information for all physical disks.
        /// </summary>
        public async Task<List<DiskHealthInfo>> GetDiskHealthAsync(CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var disks = new List<DiskHealthInfo>();

                try
                {
                    // Get physical disk info via Win32_DiskDrive
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT * FROM Win32_DiskDrive");

                    foreach (ManagementObject disk in searcher.Get())
                    {
                        ct.ThrowIfCancellationRequested();

                        var info = new DiskHealthInfo
                        {
                            DeviceId = disk["DeviceID"]?.ToString() ?? "",
                            Model = disk["Model"]?.ToString() ?? "Unknown",
                            SerialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "",
                            InterfaceType = disk["InterfaceType"]?.ToString() ?? "",
                            MediaType = disk["MediaType"]?.ToString() ?? "",
                            SizeBytes = Convert.ToInt64(disk["Size"] ?? 0),
                            Partitions = Convert.ToInt32(disk["Partitions"] ?? 0),
                            FirmwareRevision = disk["FirmwareRevision"]?.ToString() ?? "",
                            Status = disk["Status"]?.ToString() ?? "Unknown"
                        };

                        disks.Add(info);
                    }

                    // Get SMART status via MSFT_PhysicalDisk (Storage namespace)
                    try
                    {
                        using var storageSearcher = new ManagementObjectSearcher(
                            @"root\Microsoft\Windows\Storage",
                            "SELECT * FROM MSFT_PhysicalDisk");

                        foreach (ManagementObject storageDisk in storageSearcher.Get())
                        {
                            ct.ThrowIfCancellationRequested();

                            var model = storageDisk["FriendlyName"]?.ToString() ?? "";
                            var match = disks.FirstOrDefault(d =>
                                d.Model.Contains(model, StringComparison.OrdinalIgnoreCase) ||
                                model.Contains(d.Model, StringComparison.OrdinalIgnoreCase));

                            if (match == null && disks.Count > 0)
                            {
                                var deviceId = Convert.ToInt32(storageDisk["DeviceId"] ?? -1);
                                if (deviceId >= 0 && deviceId < disks.Count)
                                    match = disks[deviceId];
                            }

                            if (match != null)
                            {
                                match.HealthStatus = Convert.ToUInt16(storageDisk["HealthStatus"] ?? 0) switch
                                {
                                    0 => "Healthy",
                                    1 => "Warning",
                                    2 => "Unhealthy",
                                    5 => "Unknown",
                                    _ => "Unknown"
                                };

                                match.OperationalStatus = Convert.ToUInt16(storageDisk["OperationalStatus"] ?? 0) switch
                                {
                                    0 => "Unknown",
                                    1 => "Other",
                                    2 => "OK",
                                    3 => "Degraded",
                                    5 => "Predictive Failure",
                                    6 => "Error",
                                    10 => "Stopped",
                                    15 => "Lost Communication",
                                    _ => "OK"
                                };

                                match.BusType = Convert.ToUInt16(storageDisk["BusType"] ?? 0) switch
                                {
                                    3 => "ATA",
                                    7 => "USB",
                                    8 => "SATA",
                                    9 => "SAS",
                                    11 => "SATA",
                                    17 => "NVMe",
                                    _ => match.InterfaceType
                                };

                                match.MediaTypeEnum = Convert.ToUInt16(storageDisk["MediaType"] ?? 0) switch
                                {
                                    3 => "HDD",
                                    4 => "SSD",
                                    5 => "SCM",
                                    _ => "Unknown"
                                };

                                match.SpindleSpeed = Convert.ToUInt32(storageDisk["SpindleSpeed"] ?? 0);
                                match.Temperature = GetDiskTemperature(storageDisk);
                            }
                        }
                    }
                    catch (ManagementException)
                    {
                        // Storage namespace may not be available
                    }

                    // Get disk reliability/SMART counters
                    try
                    {
                        using var reliabilitySearcher = new ManagementObjectSearcher(
                            @"root\WMI",
                            "SELECT * FROM MSStorageDriver_FailurePredictStatus");

                        int idx = 0;
                        foreach (ManagementObject reliability in reliabilitySearcher.Get())
                        {
                            if (idx < disks.Count)
                            {
                                disks[idx].PredictFailure = Convert.ToBoolean(reliability["PredictFailure"] ?? false);
                                disks[idx].SmartAvailable = true;
                            }
                            idx++;
                        }
                    }
                    catch (ManagementException)
                    {
                        // SMART may not be available
                    }

                    // Get partition/volume info for space usage
                    try
                    {
                        using var volumeSearcher = new ManagementObjectSearcher(
                            "SELECT * FROM Win32_LogicalDisk WHERE DriveType = 3");

                        foreach (ManagementObject vol in volumeSearcher.Get())
                        {
                            ct.ThrowIfCancellationRequested();

                            var volumeInfo = new VolumeInfo
                            {
                                DriveLetter = vol["DeviceID"]?.ToString() ?? "",
                                VolumeName = vol["VolumeName"]?.ToString() ?? "",
                                FileSystem = vol["FileSystem"]?.ToString() ?? "",
                                TotalBytes = Convert.ToInt64(vol["Size"] ?? 0),
                                FreeBytes = Convert.ToInt64(vol["FreeSpace"] ?? 0)
                            };

                            // Associate with physical disk via Win32_DiskDriveToDiskPartition
                            foreach (var disk in disks)
                            {
                                disk.Volumes.Add(volumeInfo);
                            }
                        }
                    }
                    catch (ManagementException)
                    {
                        // Volume info may not be available
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting disk health: {ex.Message}");
                }

                return disks;
            }, ct);
        }

        private static int GetDiskTemperature(ManagementObject storageDisk)
        {
            try
            {
                // Try to get temperature from storage reliability counters
                using var tempSearcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage",
                    "SELECT * FROM MSFT_StorageReliabilityCounter");

                foreach (ManagementObject counter in tempSearcher.Get())
                {
                    var temp = Convert.ToInt32(counter["Temperature"] ?? 0);
                    if (temp > 0 && temp < 100)
                        return temp;
                }
            }
            catch { }
            return 0;
        }
    }

    public class DiskHealthInfo
    {
        public string DeviceId { get; set; } = "";
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string InterfaceType { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string MediaTypeEnum { get; set; } = "";
        public string BusType { get; set; } = "";
        public long SizeBytes { get; set; }
        public int Partitions { get; set; }
        public string FirmwareRevision { get; set; } = "";
        public string Status { get; set; } = "Unknown";
        public string HealthStatus { get; set; } = "Unknown";
        public string OperationalStatus { get; set; } = "Unknown";
        public bool PredictFailure { get; set; }
        public bool SmartAvailable { get; set; }
        public uint SpindleSpeed { get; set; }
        public int Temperature { get; set; }
        public List<VolumeInfo> Volumes { get; set; } = new();

        public string SizeDisplay => SizeBytes switch
        {
            >= 1_099_511_627_776 => $"{SizeBytes / 1_099_511_627_776.0:F1} TB",
            >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F1} GB",
            _ => $"{SizeBytes / 1_048_576.0:F1} MB"
        };

        public string TemperatureDisplay => Temperature > 0 ? $"{Temperature}°C" : "N/A";

        public string HealthIcon => HealthStatus switch
        {
            "Healthy" => "✅",
            "Warning" => "⚠️",
            "Unhealthy" => "❌",
            _ => "❓"
        };
    }

    public class VolumeInfo
    {
        public string DriveLetter { get; set; } = "";
        public string VolumeName { get; set; } = "";
        public string FileSystem { get; set; } = "";
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public long UsedBytes => TotalBytes - FreeBytes;
        public double UsedPercent => TotalBytes > 0 ? (UsedBytes * 100.0 / TotalBytes) : 0;

        public string TotalDisplay => FormatBytes(TotalBytes);
        public string FreeDisplay => FormatBytes(FreeBytes);
        public string UsedDisplay => FormatBytes(UsedBytes);

        private static string FormatBytes(long bytes) => bytes switch
        {
            >= 1_099_511_627_776 => $"{bytes / 1_099_511_627_776.0:F1} TB",
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            _ => $"{bytes / 1024.0:F1} KB"
        };
    }
}
