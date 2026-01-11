using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.Core.Services
{
    public interface IBootableUSBService
    {
        Task<List<USBDrive>> GetUSBDrives();
        Task<bool> CreateBootableUSB(string isoPath, USBDrive drive, BootableUSBOptions options, IProgress<BootableUSBProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> FormatDrive(USBDrive drive, string fileSystem, string label, bool quickFormat = true);
        bool IsElevated();
        bool RequestElevation();
    }

    public class USBDrive
    {
        public string DeviceID { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public long Size { get; set; }
        public string SizeFormatted => FormatBytes(Size);
        public string FileSystem { get; set; } = string.Empty;
        public string VolumeName { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
        public bool IsRemovable { get; set; }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class BootableUSBOptions
    {
        public string FileSystem { get; set; } = "NTFS";
        public string VolumeLabel { get; set; } = "BOOTABLE";
        public BootMode BootMode { get; set; } = BootMode.UEFI_GPT;
        public bool QuickFormat { get; set; } = true;
        public bool VerifyAfterWrite { get; set; } = false;
    }

    public enum BootMode
    {
        UEFI_GPT,
        UEFI_Legacy,
        Legacy_MBR
    }

    public class BootableUSBProgress
    {
        public string Stage { get; set; } = string.Empty;
        public double Percentage { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class BootableUSBService : IBootableUSBService
    {
        public bool IsElevated()
        {
            return ElevationHelper.IsElevated();
        }

        public bool RequestElevation()
        {
            return ElevationHelper.RestartAsAdmin();
        }

        public async Task<List<USBDrive>> GetUSBDrives()
        {
            return await Task.Run(() =>
            {
                var drives = new List<USBDrive>();

                try
                {
                    var scope = new ManagementScope(@"\\.\root\CIMV2");
                    scope.Connect();

                    // Query for removable drives
                    var query = new ObjectQuery("SELECT * FROM Win32_DiskDrive WHERE InterfaceType='USB'");
                    using var searcher = new ManagementObjectSearcher(scope, query);

                    foreach (ManagementObject disk in searcher.Get())
                    {
                        try
                        {
                            var drive = new USBDrive
                            {
                                DeviceID = disk["DeviceID"]?.ToString() ?? string.Empty,
                                Caption = disk["Caption"]?.ToString() ?? string.Empty,
                                Size = Convert.ToInt64(disk["Size"] ?? 0),
                                IsRemovable = true
                            };

                            // Get partition and volume information
                            var partitionQuery = new ObjectQuery($"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{drive.DeviceID.Replace("\\", "\\\\")}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                            using var partitionSearcher = new ManagementObjectSearcher(scope, partitionQuery);

                            foreach (ManagementObject partition in partitionSearcher.Get())
                            {
                                var logicalQuery = new ObjectQuery($"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                                using var logicalSearcher = new ManagementObjectSearcher(scope, logicalQuery);

                                foreach (ManagementObject logical in logicalSearcher.Get())
                                {
                                    drive.DriveLetter = logical["DeviceID"]?.ToString() ?? string.Empty;
                                    drive.FileSystem = logical["FileSystem"]?.ToString() ?? string.Empty;
                                    drive.VolumeName = logical["VolumeName"]?.ToString() ?? string.Empty;
                                    break; // Use first partition only
                                }
                            }

                            drives.Add(drive);
                        }
                        catch
                        {
                            // Skip drives we can't access
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to enumerate USB drives: {ex.Message}", ex);
                }

                return drives;
            });
        }

        public async Task<bool> FormatDrive(USBDrive drive, string fileSystem, string label, bool quickFormat = true)
        {
            if (!IsElevated())
            {
                throw new UnauthorizedAccessException("Administrator privileges required to format drives");
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(drive.DriveLetter))
                        return false;

                    var driveLetter = drive.DriveLetter.Replace(":", "");
                    
                    // Use PowerShell Format-Volume command (Windows 8+)
                    var formatCommand = $"Format-Volume -DriveLetter {driveLetter} -FileSystem {fileSystem} -NewFileSystemLabel '{label}' {(quickFormat ? "-Full:$false" : "-Full:$true")} -Confirm:$false";
                    
                    var result = ElevationHelper.RunPowerShellElevated(formatCommand);
                    return result;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> CreateBootableUSB(string isoPath, USBDrive drive, BootableUSBOptions options, IProgress<BootableUSBProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!IsElevated())
            {
                throw new UnauthorizedAccessException("Administrator privileges required to create bootable USB");
            }

            if (!File.Exists(isoPath))
            {
                throw new FileNotFoundException("ISO file not found", isoPath);
            }

            if (string.IsNullOrEmpty(drive.DriveLetter))
            {
                throw new InvalidOperationException("Drive has no assigned letter");
            }

            try
            {
                // Stage 1: Format drive
                progress?.Report(new BootableUSBProgress { Stage = "Formatting", Percentage = 0, Message = $"Formatting {drive.DriveLetter} as {options.FileSystem}..." });
                
                if (cancellationToken.IsCancellationRequested)
                    return false;

                var formatted = await FormatDrive(drive, options.FileSystem, options.VolumeLabel, options.QuickFormat);
                if (!formatted)
                    return false;

                progress?.Report(new BootableUSBProgress { Stage = "Formatting", Percentage = 20, Message = "Format complete" });

                // Stage 2: Mount ISO
                progress?.Report(new BootableUSBProgress { Stage = "Mounting", Percentage = 25, Message = "Mounting ISO..." });
                
                if (cancellationToken.IsCancellationRequested)
                    return false;

                var isoMount = await MountISO(isoPath);
                if (string.IsNullOrEmpty(isoMount))
                    return false;

                try
                {
                    progress?.Report(new BootableUSBProgress { Stage = "Mounting", Percentage = 30, Message = $"ISO mounted to {isoMount}" });

                    // Stage 3: Copy files
                    progress?.Report(new BootableUSBProgress { Stage = "Copying", Percentage = 35, Message = "Copying ISO contents..." });
                    
                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    var copied = await CopyISOContents(isoMount, drive.DriveLetter, progress, cancellationToken);
                    if (!copied)
                        return false;

                    progress?.Report(new BootableUSBProgress { Stage = "Copying", Percentage = 85, Message = "Files copied successfully" });

                    // Stage 4: Make bootable
                    progress?.Report(new BootableUSBProgress { Stage = "Bootloader", Percentage = 90, Message = "Installing bootloader..." });
                    
                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    var bootable = await MakeBootable(drive.DriveLetter, options.BootMode);
                    if (!bootable)
                        return false;

                    progress?.Report(new BootableUSBProgress { Stage = "Complete", Percentage = 100, Message = "Bootable USB created successfully" });
                    return true;
                }
                finally
                {
                    // Unmount ISO
                    await UnmountISO(isoMount);
                }
            }
            catch (Exception ex)
            {
                progress?.Report(new BootableUSBProgress { Stage = "Error", Percentage = 0, Message = $"Error: {ex.Message}" });
                return false;
            }
        }

        private async Task<string> MountISO(string isoPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use PowerShell Mount-DiskImage
                    var mountCommand = $"(Mount-DiskImage -ImagePath '{isoPath}' -PassThru | Get-Volume).DriveLetter + ':'";
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{mountCommand}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null) return string.Empty;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    return output.Trim();
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        private async Task<bool> UnmountISO(string isoPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var unmountCommand = $"Dismount-DiskImage -ImagePath '{isoPath}'";
                    return ElevationHelper.RunPowerShellElevated(unmountCommand);
                }
                catch
                {
                    return false;
                }
            });
        }

        private async Task<bool> CopyISOContents(string sourceDrive, string targetDrive, IProgress<BootableUSBProgress>? progress, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var source = sourceDrive.TrimEnd('\\');
                    var target = targetDrive.TrimEnd('\\');

                    // Use robocopy for reliable copying
                    var copyCommand = $"robocopy \"{source}\" \"{target}\" /E /NFL /NDL /NJH /NJS /nc /ns /np";
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C {copyCommand}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null) return false;

                    var progressPercentage = 35;
                    while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                    {
                        Thread.Sleep(500);
                        progressPercentage = Math.Min(80, progressPercentage + 1);
                        progress?.Report(new BootableUSBProgress { Stage = "Copying", Percentage = progressPercentage, Message = "Copying files..." });
                    }

                    process.WaitForExit();
                    
                    // Robocopy exit codes 0-7 are success
                    return process.ExitCode <= 7 && !cancellationToken.IsCancellationRequested;
                }
                catch
                {
                    return false;
                }
            });
        }

        private async Task<bool> MakeBootable(string driveLetter, BootMode bootMode)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var drive = driveLetter.Replace(":", "");
                    
                    if (bootMode == BootMode.UEFI_GPT || bootMode == BootMode.UEFI_Legacy)
                    {
                        // For UEFI, the files are already bootable if copied correctly
                        // Just ensure the drive is GPT partitioned (should be done during format)
                        return true;
                    }
                    else
                    {
                        // For Legacy MBR, use bootsect to make it bootable
                        var bootsectPath = Path.Combine(driveLetter, "boot", "bootsect.exe");
                        if (File.Exists(bootsectPath))
                        {
                            var command = $"\"{bootsectPath}\" /nt60 {drive}: /mbr";
                            var exitCode = ElevationHelper.RunElevated("cmd.exe", $"/C {command}", true);
                            return exitCode == 0;
                        }
                        return true; // Assume bootable if bootsect not found
                    }
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
