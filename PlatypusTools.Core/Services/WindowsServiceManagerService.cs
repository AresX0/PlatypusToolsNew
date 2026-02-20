using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for viewing and managing Windows services.
    /// </summary>
    public class WindowsServiceManagerService
    {
        public class ServiceInfo
        {
            public string Name { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Description { get; set; } = "";
            public string Status { get; set; } = "";
            public string StartType { get; set; } = "";
            public string Account { get; set; } = "";
            public string Path { get; set; } = "";
            public bool CanStop { get; set; }
            public bool CanPause { get; set; }
            public string[] DependsOn { get; set; } = Array.Empty<string>();
            public string[] DependedBy { get; set; } = Array.Empty<string>();
        }

        /// <summary>
        /// Get all Windows services.
        /// </summary>
        public List<ServiceInfo> GetAllServices()
        {
            var result = new List<ServiceInfo>();

            try
            {
                var services = ServiceController.GetServices();
                foreach (var svc in services)
                {
                    try
                    {
                        var info = new ServiceInfo
                        {
                            Name = svc.ServiceName,
                            DisplayName = svc.DisplayName,
                            Status = svc.Status.ToString(),
                            CanStop = svc.CanStop,
                            CanPause = svc.CanPauseAndContinue,
                            DependsOn = svc.ServicesDependedOn?.Select(s => s.ServiceName).ToArray() ?? Array.Empty<string>(),
                            DependedBy = svc.DependentServices?.Select(s => s.ServiceName).ToArray() ?? Array.Empty<string>()
                        };

                        // Get additional info from registry
                        try
                        {
                            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}");
                            if (key != null)
                            {
                                info.Description = key.GetValue("Description")?.ToString() ?? "";
                                info.Path = key.GetValue("ImagePath")?.ToString() ?? "";
                                var startType = key.GetValue("Start");
                                info.StartType = startType switch
                                {
                                    0 => "Boot",
                                    1 => "System",
                                    2 => "Automatic",
                                    3 => "Manual",
                                    4 => "Disabled",
                                    _ => "Unknown"
                                };
                                info.Account = key.GetValue("ObjectName")?.ToString() ?? "";

                                // Check if delayed auto start
                                var delayedStart = key.GetValue("DelayedAutostart");
                                if (info.StartType == "Automatic" && delayedStart != null && (int)delayedStart == 1)
                                    info.StartType = "Automatic (Delayed)";
                            }
                        }
                        catch { }

                        result.Add(info);
                    }
                    catch { }
                    finally
                    {
                        svc.Dispose();
                    }
                }
            }
            catch { }

            return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Start a service.
        /// </summary>
        public async Task<bool> StartServiceAsync(string serviceName, int timeoutSeconds = 30)
        {
            try
            {
                using var svc = new ServiceController(serviceName);
                if (svc.Status == ServiceControllerStatus.Running) return true;
                svc.Start();
                await Task.Run(() => svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeoutSeconds)));
                return svc.Status == ServiceControllerStatus.Running;
            }
            catch { return false; }
        }

        /// <summary>
        /// Stop a service.
        /// </summary>
        public async Task<bool> StopServiceAsync(string serviceName, int timeoutSeconds = 30)
        {
            try
            {
                using var svc = new ServiceController(serviceName);
                if (svc.Status == ServiceControllerStatus.Stopped) return true;
                if (!svc.CanStop) return false;
                svc.Stop();
                await Task.Run(() => svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(timeoutSeconds)));
                return svc.Status == ServiceControllerStatus.Stopped;
            }
            catch { return false; }
        }

        /// <summary>
        /// Restart a service.
        /// </summary>
        public async Task<bool> RestartServiceAsync(string serviceName, int timeoutSeconds = 30)
        {
            await StopServiceAsync(serviceName, timeoutSeconds);
            return await StartServiceAsync(serviceName, timeoutSeconds);
        }

        /// <summary>
        /// Pause a service.
        /// </summary>
        public async Task<bool> PauseServiceAsync(string serviceName, int timeoutSeconds = 30)
        {
            try
            {
                using var svc = new ServiceController(serviceName);
                if (!svc.CanPauseAndContinue) return false;
                svc.Pause();
                await Task.Run(() => svc.WaitForStatus(ServiceControllerStatus.Paused, TimeSpan.FromSeconds(timeoutSeconds)));
                return svc.Status == ServiceControllerStatus.Paused;
            }
            catch { return false; }
        }

        /// <summary>
        /// Resume a paused service.
        /// </summary>
        public async Task<bool> ResumeServiceAsync(string serviceName, int timeoutSeconds = 30)
        {
            try
            {
                using var svc = new ServiceController(serviceName);
                if (svc.Status != ServiceControllerStatus.Paused) return false;
                svc.Continue();
                await Task.Run(() => svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeoutSeconds)));
                return svc.Status == ServiceControllerStatus.Running;
            }
            catch { return false; }
        }

        /// <summary>
        /// Change the startup type of a service.
        /// </summary>
        public bool SetStartupType(string serviceName, string startupType)
        {
            try
            {
                var scStartType = startupType.ToLowerInvariant() switch
                {
                    "automatic" => "auto",
                    "automatic (delayed)" => "delayed-auto",
                    "manual" => "demand",
                    "disabled" => "disabled",
                    _ => startupType.ToLowerInvariant()
                };

                var psi = new System.Diagnostics.ProcessStartInfo("sc.exe", $"config \"{serviceName}\" start= {scStartType}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    Verb = "runas"
                };

                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(10000);
                return p?.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Search services by name or display name.
        /// </summary>
        public List<ServiceInfo> Search(string query)
        {
            if (string.IsNullOrEmpty(query)) return GetAllServices();
            return GetAllServices().Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
    }
}
