using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Utilities
{
    /// <summary>
    /// Utility for profiling and optimizing startup time.
    /// Provides timing information for initialization phases.
    /// Phase 6.3 — also writes startup-profile.json on Finish().
    /// </summary>
    public static class StartupProfiler
    {
        private static readonly Stopwatch _totalTimer = new();
        private static readonly Stopwatch _phaseTimer = new();
        private static string _currentPhase = string.Empty;
        private static readonly List<(string Phase, long ElapsedMs)> _phases = new();
        private static long _totalMs;

        /// <summary>
        /// Starts the overall startup timer.
        /// </summary>
        public static void Start()
        {
            _totalTimer.Restart();
            SimpleLogger.Debug("[STARTUP] Profiler started");
        }

        /// <summary>
        /// Begins timing a specific startup phase.
        /// </summary>
        /// <param name="phaseName">Name of the phase being timed</param>
        public static void BeginPhase(string phaseName)
        {
            if (!string.IsNullOrEmpty(_currentPhase))
            {
                EndPhase();
            }
            
            _currentPhase = phaseName;
            _phaseTimer.Restart();
            SimpleLogger.Debug($"[STARTUP] BEGIN: {phaseName}");
        }

        /// <summary>
        /// Ends the current phase timing and logs the duration.
        /// </summary>
        public static void EndPhase()
        {
            if (string.IsNullOrEmpty(_currentPhase))
                return;

            _phaseTimer.Stop();
            _phases.Add((_currentPhase, _phaseTimer.ElapsedMilliseconds));
            SimpleLogger.Debug($"[STARTUP] END: {_currentPhase} ({_phaseTimer.ElapsedMilliseconds}ms)");
            _currentPhase = string.Empty;
        }

        /// <summary>
        /// Ends profiling and logs total startup time.
        /// </summary>
        public static void Finish()
        {
            EndPhase();
            _totalTimer.Stop();
            _totalMs = _totalTimer.ElapsedMilliseconds;
            SimpleLogger.Info($"[STARTUP] Total startup time: {_totalMs}ms");
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "startup-profile.json");
                var payload = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    totalMs = _totalMs,
                    phases = _phases
                };
                File.WriteAllText(path, JsonSerializer.Serialize(payload,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"[STARTUP] Failed to write startup-profile.json: {ex.Message}");
            }
        }

        /// <summary>Phase 6.3 — returns a human-readable summary of startup phases for the About dialog.</summary>
        public static string GetReportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Total startup: {_totalMs}ms");
            sb.AppendLine();
            sb.AppendLine("Phase                                          Elapsed");
            sb.AppendLine("------------------------------------------------------");
            foreach (var (name, ms) in _phases)
                sb.AppendLine($"{name,-46} {ms,6}ms");
            return sb.ToString();
        }

        /// <summary>Path to the persisted startup-profile.json (may not yet exist).</summary>
        public static string ReportFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "startup-profile.json");

        /// <summary>
        /// Gets the total elapsed time since Start() was called.
        /// </summary>
        public static TimeSpan TotalElapsed => _totalTimer.Elapsed;

        /// <summary>
        /// Times an async action and logs its duration.
        /// </summary>
        /// <param name="actionName">Name of the action</param>
        /// <param name="action">The async action to time</param>
        public static async Task TimeAsync(string actionName, Func<Task> action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await action();
            }
            finally
            {
                sw.Stop();
                _phases.Add((actionName, sw.ElapsedMilliseconds));
                SimpleLogger.Debug($"[STARTUP] {actionName}: {sw.ElapsedMilliseconds}ms");
            }
        }

        /// <summary>
        /// Times a synchronous action and logs its duration.
        /// </summary>
        /// <param name="actionName">Name of the action</param>
        /// <param name="action">The action to time</param>
        public static void Time(string actionName, Action action)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                action();
            }
            finally
            {
                sw.Stop();
                _phases.Add((actionName, sw.ElapsedMilliseconds));
                SimpleLogger.Debug($"[STARTUP] {actionName}: {sw.ElapsedMilliseconds}ms");
            }
        }
    }

    /// <summary>
    /// Optimization hints for startup performance.
    /// </summary>
    public static class StartupOptimizations
    {
        /// <summary>
        /// Schedules non-critical initialization to run after the UI is visible.
        /// Uses lower priority dispatcher to avoid blocking UI rendering.
        /// </summary>
        /// <param name="action">The action to run after UI is ready</param>
        /// <param name="delayMs">Optional delay in milliseconds</param>
        public static async void RunAfterUIReady(Action action, int delayMs = 100)
        {
            await Task.Delay(delayMs);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                action, 
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Schedules non-critical async initialization to run after the UI is visible.
        /// </summary>
        /// <param name="asyncAction">The async action to run after UI is ready</param>
        /// <param name="delayMs">Optional delay in milliseconds</param>
        public static async void RunAfterUIReadyAsync(Func<Task> asyncAction, int delayMs = 100)
        {
            await Task.Delay(delayMs);
            try
            {
                await asyncAction();
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"Deferred initialization failed: {ex.Message}");
            }
        }
    }
}
