using System;
using System.Threading;

namespace PlatypusTools.UI.Services.Performance
{
    /// <summary>
    /// Phase 6.2 — co-operative concurrency budget for background tasks.
    /// Long-running services call <c>using var slot = ResourceGovernor.Instance.Acquire(category)</c>
    /// to throttle parallelism. Limits are conservative defaults; tune via Settings (deferred).
    /// </summary>
    public sealed class ResourceGovernor
    {
        private static readonly Lazy<ResourceGovernor> _instance = new(() => new ResourceGovernor());
        public static ResourceGovernor Instance => _instance.Value;

        private readonly SemaphoreSlim _io;
        private readonly SemaphoreSlim _cpu;
        private readonly SemaphoreSlim _network;

        public int IoLimit { get; }
        public int CpuLimit { get; }
        public int NetworkLimit { get; }

        /// <summary>
        /// Phase 6.2 — when 1..99, child processes started via <c>StartThrottledProcess</c>
        /// are wrapped in a Win32 job object that hard-caps CPU usage to this percent.
        /// 0 (default) disables CPU rate control. Honored across SceneDetectionService,
        /// ThumbnailCacheService, and any future ffmpeg shell-out.
        /// </summary>
        public int CpuThrottlePercent { get; set; }

        private ResourceGovernor()
        {
            CpuLimit = Math.Max(2, Environment.ProcessorCount / 2);
            IoLimit = 4;
            NetworkLimit = 8;
            _cpu = new SemaphoreSlim(CpuLimit, CpuLimit);
            _io = new SemaphoreSlim(IoLimit, IoLimit);
            _network = new SemaphoreSlim(NetworkLimit, NetworkLimit);
        }

        /// <summary>
        /// Wraps a started <see cref="System.Diagnostics.Process"/> in a job object that hard-caps
        /// CPU rate, when <see cref="CpuThrottlePercent"/> is set. Returns the job (or null) — the
        /// caller must keep it alive for the duration of the process and dispose it after exit.
        /// </summary>
        public JobObjectThrottler? AttachCpuCap(System.Diagnostics.Process process)
        {
            var pct = CpuThrottlePercent;
            if (pct <= 0 || pct >= 100) return null;
            var job = JobObjectThrottler.CreateThrottled(pct);
            if (job == null) return null;
            if (!job.Attach(process)) { job.Dispose(); return null; }
            return job;
        }

        public IDisposable Acquire(ResourceCategory category, CancellationToken ct = default)
        {
            var sem = Pick(category);
            sem.Wait(ct);
            return new Slot(sem);
        }

        public async System.Threading.Tasks.Task<IDisposable> AcquireAsync(ResourceCategory category, CancellationToken ct = default)
        {
            var sem = Pick(category);
            await sem.WaitAsync(ct).ConfigureAwait(false);
            return new Slot(sem);
        }

        private SemaphoreSlim Pick(ResourceCategory c) => c switch
        {
            ResourceCategory.Io => _io,
            ResourceCategory.Cpu => _cpu,
            ResourceCategory.Network => _network,
            _ => _cpu,
        };

        private sealed class Slot : IDisposable
        {
            private readonly SemaphoreSlim _sem;
            private bool _released;
            public Slot(SemaphoreSlim sem) { _sem = sem; }
            public void Dispose()
            {
                if (_released) return;
                _released = true;
                try { _sem.Release(); } catch { }
            }
        }
    }

    public enum ResourceCategory { Cpu, Io, Network }
}
