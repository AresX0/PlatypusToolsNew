using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for managing Windows system restore points
    /// </summary>
    public interface ISystemRestoreService
    {
        Task<List<RestorePoint>> GetRestorePoints();
        Task<bool> CreateRestorePoint(string description, RestorePointType type = RestorePointType.ApplicationInstall);
        Task<bool> RestoreToPoint(int sequenceNumber);
        Task<bool> DeleteRestorePoint(int sequenceNumber);
    }

    /// <summary>
    /// Represents a system restore point
    /// </summary>
    public class RestorePoint
    {
        public int SequenceNumber { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
        public RestorePointType Type { get; set; }
        public string TypeDescription => Type.ToString();
    }

    /// <summary>
    /// Types of restore points
    /// </summary>
    public enum RestorePointType
    {
        ApplicationInstall = 0,
        ApplicationUninstall = 1,
        ModifySettings = 12,
        CancelledOperation = 13
    }

    /// <summary>
    /// Implementation of system restore service
    /// </summary>
    public class SystemRestoreService : ISystemRestoreService
    {
        /// <summary>
        /// Gets all available system restore points
        /// </summary>
        /// <returns>List of restore points</returns>
        public async Task<List<RestorePoint>> GetRestorePoints()
        {
            return await Task.Run(() =>
            {
                var restorePoints = new List<RestorePoint>();

                try
                {
                    // Use WMI to query restore points
                    var scope = new ManagementScope("\\\\localhost\\root\\default");
                    scope.Connect();

                    var query = new ObjectQuery("SELECT * FROM SystemRestore");
                    using var searcher = new ManagementObjectSearcher(scope, query);

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            var restorePoint = new RestorePoint
                            {
                                SequenceNumber = Convert.ToInt32(obj["SequenceNumber"]),
                                Description = obj["Description"]?.ToString() ?? string.Empty,
                                Type = (RestorePointType)Convert.ToInt32(obj["RestorePointType"])
                            };

                            // Parse creation time
                            var creationTime = obj["CreationTime"]?.ToString();
                            if (!string.IsNullOrEmpty(creationTime))
                            {
                                restorePoint.CreationTime = ManagementDateTimeConverter.ToDateTime(creationTime);
                            }

                            restorePoints.Add(restorePoint);
                        }
                        catch
                        {
                            // Skip malformed entries
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to access restore points");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to get restore points: {ex.Message}", ex);
                }

                return restorePoints.OrderByDescending(rp => rp.CreationTime).ToList();
            });
        }

        /// <summary>
        /// Creates a new system restore point
        /// </summary>
        /// <param name="description">Description for the restore point</param>
        /// <param name="type">Type of restore point</param>
        /// <returns>True if successful</returns>
        public async Task<bool> CreateRestorePoint(string description, RestorePointType type = RestorePointType.ApplicationInstall)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use WMI to create restore point
                    var scope = new ManagementScope("\\\\localhost\\root\\default");
                    scope.Connect();

                    var path = new ManagementPath("SystemRestore");
                    using var restoreClass = new ManagementClass(scope, path, null);

                    var inParams = restoreClass.GetMethodParameters("CreateRestorePoint");
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = (int)type;
                    inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE

                    var outParams = restoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
                    
                    if (outParams != null && outParams["ReturnValue"] != null)
                    {
                        var result = Convert.ToInt32(outParams["ReturnValue"]);
                        return result == 0;
                    }

                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to create restore points");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to create restore point: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Restores the system to a specific restore point
        /// </summary>
        /// <param name="sequenceNumber">Sequence number of the restore point</param>
        /// <returns>True if restoration initiated successfully</returns>
        public async Task<bool> RestoreToPoint(int sequenceNumber)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use WMI to initiate restore
                    var scope = new ManagementScope("\\\\localhost\\root\\default");
                    scope.Connect();

                    var path = new ManagementPath("SystemRestore");
                    using var restoreClass = new ManagementClass(scope, path, null);

                    var inParams = restoreClass.GetMethodParameters("Restore");
                    inParams["SequenceNumber"] = sequenceNumber;

                    var outParams = restoreClass.InvokeMethod("Restore", inParams, null);
                    
                    if (outParams != null && outParams["ReturnValue"] != null)
                    {
                        var result = Convert.ToInt32(outParams["ReturnValue"]);
                        return result == 0;
                    }

                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to restore system");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to restore to point {sequenceNumber}: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Deletes a system restore point
        /// </summary>
        /// <param name="sequenceNumber">Sequence number of the restore point to delete</param>
        /// <returns>True if successful</returns>
        public async Task<bool> DeleteRestorePoint(int sequenceNumber)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use PowerShell to delete restore point (more reliable than WMI)
                    var script = $@"
                        $ErrorActionPreference = 'Stop'
                        try {{
                            vssadmin delete shadows /for=C: /shadow={sequenceNumber} /quiet
                            exit 0
                        }} catch {{
                            exit 1
                        }}
                    ";

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas" // Requires elevation
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        return false;
                    }

                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to delete restore points");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to delete restore point {sequenceNumber}: {ex.Message}", ex);
                }
            });
        }
    }
}
