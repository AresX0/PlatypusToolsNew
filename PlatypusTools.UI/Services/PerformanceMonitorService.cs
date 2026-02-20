using System;
using System.Diagnostics;
using System.Windows.Threading;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Monitors application performance metrics: CPU usage, memory, and FPS.
    /// Singleton service that updates the status bar with live stats. (IDEA-012)
    /// </summary>
    public class PerformanceMonitorService : BindableBase, IDisposable
    {
        private static PerformanceMonitorService? _instance;
        public static PerformanceMonitorService Instance => _instance ??= new PerformanceMonitorService();

        private readonly DispatcherTimer _timer;
        private readonly Process _currentProcess;
        private TimeSpan _lastCpuTime;
        private DateTime _lastCheckTime;
        private bool _isEnabled;

        private double _cpuPercent;
        private long _memoryMB;
        private long _peakMemoryMB;
        private int _fps;
        private int _frameCount;
        private DateTime _lastFpsCheckTime;
        private string _displayText = "";

        public double CpuPercent
        {
            get => _cpuPercent;
            private set { _cpuPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public long MemoryMB
        {
            get => _memoryMB;
            private set { _memoryMB = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public long PeakMemoryMB
        {
            get => _peakMemoryMB;
            private set { _peakMemoryMB = value; OnPropertyChanged(); }
        }

        public int Fps
        {
            get => _fps;
            private set { _fps = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    if (_isEnabled) Start(); else Stop();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public string DisplayText
        {
            get
            {
                if (!_isEnabled) return "";
                return $"CPU: {_cpuPercent:F0}%  |  RAM: {_memoryMB} MB  |  FPS: {_fps}";
            }
        }

        private PerformanceMonitorService()
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastCpuTime = _currentProcess.TotalProcessorTime;
            _lastCheckTime = DateTime.UtcNow;
            _lastFpsCheckTime = DateTime.UtcNow;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            // Enable by default so users see CPU/RAM in status bar
            IsEnabled = true;
        }

        private void Start()
        {
            _lastCpuTime = _currentProcess.TotalProcessorTime;
            _lastCheckTime = DateTime.UtcNow;
            _lastFpsCheckTime = DateTime.UtcNow;
            _frameCount = 0;
            _timer.Start();
        }

        private void Stop()
        {
            _timer.Stop();
        }

        /// <summary>
        /// Call this from rendering loops to track FPS.
        /// </summary>
        public void RecordFrame()
        {
            if (!_isEnabled) return;
            _frameCount++;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _currentProcess.Refresh();

                // CPU usage
                var now = DateTime.UtcNow;
                var cpuTime = _currentProcess.TotalProcessorTime;
                var elapsed = (now - _lastCheckTime).TotalMilliseconds;
                if (elapsed > 0)
                {
                    var cpuUsed = (cpuTime - _lastCpuTime).TotalMilliseconds;
                    CpuPercent = Math.Min(100, cpuUsed / (elapsed * Environment.ProcessorCount) * 100);
                }
                _lastCpuTime = cpuTime;
                _lastCheckTime = now;

                // Memory
                MemoryMB = _currentProcess.WorkingSet64 / (1024 * 1024);
                var peakMB = _currentProcess.PeakWorkingSet64 / (1024 * 1024);
                if (peakMB > PeakMemoryMB) PeakMemoryMB = peakMB;

                // FPS (frames recorded since last check)
                var fpsElapsed = (now - _lastFpsCheckTime).TotalSeconds;
                if (fpsElapsed >= 0.9)
                {
                    Fps = (int)(_frameCount / fpsElapsed);
                    _frameCount = 0;
                    _lastFpsCheckTime = now;
                }
            }
            catch
            {
                // Ignore errors during monitoring
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            GC.SuppressFinalize(this);
        }
    }
}
