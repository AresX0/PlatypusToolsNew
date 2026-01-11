using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for managing system processes
    /// </summary>
    public interface IProcessManagerService
    {
        Task<List<ProcessInfo>> GetProcesses();
        Task<bool> KillProcess(int processId);
        Task<ProcessInfo?> GetProcessDetails(int processId);
    }

    /// <summary>
    /// Represents detailed information about a process
    /// </summary>
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long MemoryUsage { get; set; }
        public double CpuUsage { get; set; }
        public string Path { get; set; } = string.Empty;
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan CpuTime { get; set; }
        public string UserName { get; set; } = string.Empty;
        public long WorkingSet { get; set; }
        public long VirtualMemory { get; set; }
        public long PrivateMemory { get; set; }
        public string FormattedMemory => FormatBytes(MemoryUsage);

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

    /// <summary>
    /// Implementation of process manager service
    /// </summary>
    public class ProcessManagerService : IProcessManagerService
    {
        private readonly Dictionary<int, DateTime> _lastCpuTimes = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, TimeSpan> _lastProcessorTimes = new Dictionary<int, TimeSpan>();

        /// <summary>
        /// Gets a list of all running processes
        /// </summary>
        /// <returns>List of process information</returns>
        public async Task<List<ProcessInfo>> GetProcesses()
        {
            return await Task.Run(() =>
            {
                var processList = new List<ProcessInfo>();

                try
                {
                    var processes = Process.GetProcesses();

                    foreach (var process in processes)
                    {
                        try
                        {
                            var processInfo = CreateProcessInfo(process);
                            processList.Add(processInfo);
                        }
                        catch
                        {
                            // Skip processes we can't access
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to get processes: {ex.Message}", ex);
                }

                return processList.OrderByDescending(p => p.MemoryUsage).ToList();
            });
        }

        /// <summary>
        /// Terminates a process by ID
        /// </summary>
        /// <param name="processId">Process ID to terminate</param>
        /// <returns>True if successful</returns>
        public async Task<bool> KillProcess(int processId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (process != null)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                        return true;
                    }
                    return false;
                }
                catch (ArgumentException)
                {
                    // Process doesn't exist
                    return false;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to kill process {processId}: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Gets detailed information about a specific process
        /// </summary>
        /// <param name="processId">Process ID</param>
        /// <returns>Detailed process information or null if not found</returns>
        public async Task<ProcessInfo?> GetProcessDetails(int processId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    return CreateProcessInfo(process, includeDetails: true);
                }
                catch (ArgumentException)
                {
                    // Process doesn't exist
                    return null;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to get process details for {processId}: {ex.Message}", ex);
                }
            });
        }

        private ProcessInfo CreateProcessInfo(Process process, bool includeDetails = false)
        {
            var info = new ProcessInfo
            {
                Id = process.Id,
                Name = process.ProcessName
            };

            try
            {
                info.MemoryUsage = process.WorkingSet64;
                info.WorkingSet = process.WorkingSet64;
                info.VirtualMemory = process.VirtualMemorySize64;
                info.PrivateMemory = process.PrivateMemorySize64;
                info.ThreadCount = process.Threads.Count;
                info.HandleCount = process.HandleCount;

                try
                {
                    info.Path = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    info.Path = string.Empty;
                }

                try
                {
                    info.StartTime = process.StartTime;
                }
                catch
                {
                    info.StartTime = DateTime.MinValue;
                }

                info.CpuTime = process.TotalProcessorTime;

                // Calculate CPU usage
                if (_lastCpuTimes.ContainsKey(process.Id) && _lastProcessorTimes.ContainsKey(process.Id))
                {
                    var currentTime = DateTime.Now;
                    var elapsedTime = currentTime - _lastCpuTimes[process.Id];
                    var cpuTime = process.TotalProcessorTime - _lastProcessorTimes[process.Id];

                    if (elapsedTime.TotalMilliseconds > 0)
                    {
                        info.CpuUsage = (cpuTime.TotalMilliseconds / elapsedTime.TotalMilliseconds) * 100.0;
                        info.CpuUsage = Math.Min(100.0, Math.Max(0.0, info.CpuUsage));
                    }
                }

                _lastCpuTimes[process.Id] = DateTime.Now;
                _lastProcessorTimes[process.Id] = process.TotalProcessorTime;

                // Get username if detailed
                if (includeDetails)
                {
                    try
                    {
                        var query = $"SELECT * FROM Win32_Process WHERE ProcessId = {process.Id}";
                        using var searcher = new System.Management.ManagementObjectSearcher(query);
                        foreach (System.Management.ManagementObject obj in searcher.Get())
                        {
                            var owner = new string[2];
                            obj.InvokeMethod("GetOwner", (object[])owner);
                            info.UserName = owner[0] ?? string.Empty;
                            break;
                        }
                    }
                    catch
                    {
                        info.UserName = string.Empty;
                    }
                }
            }
            catch
            {
                // If we can't get some info, that's okay
            }

            return info;
        }
    }
}
