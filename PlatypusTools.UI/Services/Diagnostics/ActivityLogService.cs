using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlatypusTools.UI.Services.Diagnostics
{
    /// <summary>
    /// Phase 1.2 — unified activity log. Singleton, in-memory ring buffer
    /// (capped at 2,000 entries) plus append-only JSONL file under
    /// %APPDATA%/PlatypusTools/activity-log/. Wraps ToastNotificationService —
    /// every toast also lands in the log (no behaviour change for callers
    /// that already use ToastNotificationService directly).
    /// </summary>
    public sealed class ActivityLogService
    {
        public enum Level { Debug, Info, Warning, Error }

        public sealed class Entry
        {
            public DateTime UtcStamp { get; set; } = DateTime.UtcNow;
            public Level Level { get; set; }
            public string Category { get; set; } = "";
            public string Message { get; set; } = "";
        }

        private static readonly Lazy<ActivityLogService> _instance = new(() => new ActivityLogService());
        public static ActivityLogService Instance => _instance.Value;

        private const int Cap = 2000;
        private readonly ConcurrentQueue<Entry> _ring = new();
        private readonly string _logDir;
        private readonly string _logFile;
        private readonly object _fileLock = new();

        public ObservableCollection<Entry> Recent { get; } = new();

        public event EventHandler<Entry>? EntryAdded;

        private ActivityLogService()
        {
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "activity-log");
            try { Directory.CreateDirectory(_logDir); } catch { }
            _logFile = Path.Combine(_logDir, $"activity-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        }

        public void Debug(string category, string message) => Log(Level.Debug, category, message);
        public void Info(string category, string message) => Log(Level.Info, category, message);
        public void Warn(string category, string message) => Log(Level.Warning, category, message);
        public void Error(string category, string message) => Log(Level.Error, category, message);

        public void Log(Level level, string category, string message)
        {
            var e = new Entry { Level = level, Category = category ?? "", Message = message ?? "" };
            _ring.Enqueue(e);
            while (_ring.Count > Cap && _ring.TryDequeue(out _)) { }

            // UI list (cap at 500)
            try
            {
                if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
                {
                    Recent.Add(e);
                    while (Recent.Count > 500) Recent.RemoveAt(0);
                }
                else
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Recent.Add(e);
                        while (Recent.Count > 500) Recent.RemoveAt(0);
                    }));
                }
            }
            catch { }

            // Append to JSONL — best effort
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_logFile,
                        JsonSerializer.Serialize(e) + Environment.NewLine);
                }
            }
            catch { }

            EntryAdded?.Invoke(this, e);
        }

        public IReadOnlyList<Entry> Snapshot() => _ring.ToArray();

        public IEnumerable<Entry> ReadFromDisk(int maxEntries = 5000)
        {
            if (!File.Exists(_logFile)) yield break;
            string[] lines;
            try { lines = File.ReadAllLines(_logFile); }
            catch { yield break; }
            int start = Math.Max(0, lines.Length - maxEntries);
            for (int i = start; i < lines.Length; i++)
            {
                Entry? e = null;
                try { e = JsonSerializer.Deserialize<Entry>(lines[i]); } catch { }
                if (e != null) yield return e;
            }
        }

        public string LogFilePath => _logFile;

        public void Clear()
        {
            while (_ring.TryDequeue(out _)) { }
            try
            {
                if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
                    Recent.Clear();
                else
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Recent.Clear()));
            }
            catch { }
        }
    }
}
