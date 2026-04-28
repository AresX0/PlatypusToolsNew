using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PlatypusTools.UI.Services.Performance
{
    /// <summary>
    /// Phase 6.2 — Win32 Job Object wrapper that enforces a CPU rate cap on a child process
    /// via <c>JOB_OBJECT_LIMIT_CPU_RATE_CONTROL</c>. Use it to throttle long-running ffmpeg
    /// or analysis processes when the user enables CPU budgeting.
    ///
    /// Usage:
    ///   using var job = JobObjectThrottler.CreateThrottled(50);  // 50%
    ///   var p = Process.Start(...);
    ///   job.Attach(p);
    /// </summary>
    public sealed class JobObjectThrottler : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        private JobObjectThrottler(IntPtr handle) { _handle = handle; }

        /// <summary>
        /// Create a job object that caps CPU usage to <paramref name="cpuRatePercent"/> percent
        /// (1-100). Returns null if the OS rejects the request (e.g., older Windows).
        /// </summary>
        public static JobObjectThrottler? CreateThrottled(int cpuRatePercent)
        {
            if (cpuRatePercent <= 0 || cpuRatePercent >= 100) return null;
            var h = CreateJobObjectW(IntPtr.Zero, null);
            if (h == IntPtr.Zero) return null;
            try
            {
                var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
                {
                    ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
                    // Rate is expressed in 1/100ths of a percent: 50% -> 5000.
                    CpuRate = (uint)(cpuRatePercent * 100)
                };
                int size = Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(h, JobObjectInfoClass.CpuRateControlInformation, ptr, (uint)size))
                    {
                        CloseHandle(h);
                        return null;
                    }
                }
                finally { Marshal.FreeHGlobal(ptr); }
                return new JobObjectThrottler(h);
            }
            catch
            {
                CloseHandle(h);
                return null;
            }
        }

        /// <summary>Assigns the process to the job. Best-effort; failures are swallowed.</summary>
        public bool Attach(Process process)
        {
            if (_disposed || process == null) return false;
            try { return AssignProcessToJobObject(_handle, process.Handle); }
            catch { return false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_handle != IntPtr.Zero) CloseHandle(_handle); } catch { }
            _handle = IntPtr.Zero;
        }

        // --- P/Invoke ---
        private const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
        private const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

        private enum JobObjectInfoClass { CpuRateControlInformation = 15 }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            public uint ControlFlags;
            public uint CpuRate;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoClass infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
