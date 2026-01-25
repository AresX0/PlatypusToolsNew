using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Centralized logging service with file output, log levels, and rotation.
    /// </summary>
    public class LoggingService : IDisposable
    {
        private static LoggingService? _instance;
        public static LoggingService Instance => _instance ??= new LoggingService();

        private readonly ConcurrentQueue<LogEntry> _logQueue = new();
        private readonly ConcurrentBag<LogEntry> _inMemoryLogs = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly string _logDirectory;
        private readonly string _currentLogFile;
        private readonly Timer _flushTimer;
        private bool _disposed;

        public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
        public int MaxInMemoryLogs { get; set; } = 1000;
        public int MaxLogFileSize { get; set; } = 10 * 1024 * 1024; // 10MB
        public int MaxLogFiles { get; set; } = 5;

        public event EventHandler<LogEntry>? LogAdded;

        public LoggingService()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlatypusTools", "Logs");
            
            Directory.CreateDirectory(_logDirectory);
            _currentLogFile = Path.Combine(_logDirectory, $"platypus_{DateTime.Now:yyyyMMdd}.log");
            
            _flushTimer = new Timer(async _ => await FlushAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public void Log(LogLevel level, string message, string? category = null, Exception? exception = null)
        {
            if (level < MinimumLevel) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Category = category ?? "General",
                Exception = exception?.ToString()
            };

            _logQueue.Enqueue(entry);
            _inMemoryLogs.Add(entry);

            // Trim in-memory logs
            while (_inMemoryLogs.Count > MaxInMemoryLogs && _inMemoryLogs.TryTake(out _)) { }

            LogAdded?.Invoke(this, entry);
        }

        public void Debug(string message, string? category = null) => Log(LogLevel.Debug, message, category);
        public void Info(string message, string? category = null) => Log(LogLevel.Info, message, category);
        public void Warning(string message, string? category = null) => Log(LogLevel.Warning, message, category);
        public void Error(string message, string? category = null, Exception? ex = null) => Log(LogLevel.Error, message, category, ex);
        public void Critical(string message, string? category = null, Exception? ex = null) => Log(LogLevel.Critical, message, category, ex);

        public IEnumerable<LogEntry> GetLogs(LogLevel? minLevel = null, string? category = null, DateTime? since = null)
        {
            var logs = _inMemoryLogs.AsEnumerable();
            
            if (minLevel.HasValue)
                logs = logs.Where(l => l.Level >= minLevel.Value);
            if (!string.IsNullOrEmpty(category))
                logs = logs.Where(l => l.Category?.Equals(category, StringComparison.OrdinalIgnoreCase) == true);
            if (since.HasValue)
                logs = logs.Where(l => l.Timestamp >= since.Value);
            
            return logs.OrderByDescending(l => l.Timestamp);
        }

        public async Task FlushAsync()
        {
            if (_logQueue.IsEmpty) return;

            await _writeLock.WaitAsync();
            try
            {
                var entries = new List<string>();
                while (_logQueue.TryDequeue(out var entry))
                {
                    entries.Add(FormatLogEntry(entry));
                }

                if (entries.Count > 0)
                {
                    await RotateLogIfNeeded();
                    await File.AppendAllLinesAsync(_currentLogFile, entries);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task RotateLogIfNeeded()
        {
            if (!File.Exists(_currentLogFile)) return;
            
            var info = new FileInfo(_currentLogFile);
            if (info.Length < MaxLogFileSize) return;

            // Rotate logs
            var logFiles = Directory.GetFiles(_logDirectory, "platypus_*.log")
                .OrderByDescending(f => f)
                .ToList();

            while (logFiles.Count >= MaxLogFiles)
            {
                File.Delete(logFiles.Last());
                logFiles.RemoveAt(logFiles.Count - 1);
            }

            var archivePath = Path.Combine(_logDirectory, $"platypus_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.Move(_currentLogFile, archivePath);
            await Task.CompletedTask;
        }

        private string FormatLogEntry(LogEntry entry)
        {
            var level = entry.Level.ToString().ToUpper().PadRight(8);
            var message = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{entry.Category}] {entry.Message}";
            if (!string.IsNullOrEmpty(entry.Exception))
                message += $"\n  Exception: {entry.Exception}";
            return message;
        }

        public void ClearInMemoryLogs() => _inMemoryLogs.Clear();

        public async Task ExportLogsAsync(string filePath, LogLevel? minLevel = null)
        {
            var logs = GetLogs(minLevel).OrderBy(l => l.Timestamp);
            var lines = logs.Select(FormatLogEntry);
            await File.WriteAllLinesAsync(filePath, lines);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _flushTimer.Dispose();
            // Use GetAwaiter().GetResult() in Dispose - safer than Wait() for sync-over-async
            FlushAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            _writeLock.Dispose();
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
        public string? Category { get; set; }
        public string? Exception { get; set; }
    }

    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }
}
