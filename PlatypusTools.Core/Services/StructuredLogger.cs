using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// IDEA-018: Enhanced structured logging service with JSON output, log rotation, and multiple sinks.
    /// Drop-in replacement compatible with SimpleLogger API but with structured logging capabilities.
    /// </summary>
    public sealed class StructuredLogger : IDisposable
    {
        private static StructuredLogger? _instance;
        public static StructuredLogger Instance => _instance ??= new StructuredLogger();

        private readonly ConcurrentQueue<LogEntry> _queue = new();
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private string? _logDirectory;
        private string? _currentLogFile;
        private long _currentFileSize;
        private bool _isDisposed;

        // Configuration
        public LogLevel MinLevel { get; set; } = LogLevel.Info;
        public bool EnableJsonFormat { get; set; } = true;
        public bool EnableConsoleOutput { get; set; } = true;
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
        public int MaxLogFiles { get; set; } = 5;
        public string ApplicationName { get; set; } = "PlatypusTools";

        /// <summary>
        /// Event for real-time log monitoring (e.g., for in-app log viewer).
        /// </summary>
        public event EventHandler<LogEntry>? LogWritten;

        private StructuredLogger() { }

        /// <summary>
        /// Initializes the logger with a log directory.
        /// </summary>
        public void Initialize(string logDirectory)
        {
            _logDirectory = logDirectory;
            Directory.CreateDirectory(logDirectory);
            _currentLogFile = GetCurrentLogFilePath();
            
            // Also wire SimpleLogger to use this
            SimpleLogger.LogFile = Path.Combine(logDirectory, "platypustools-simple.log");
        }

        /// <summary>
        /// Logs a structured entry.
        /// </summary>
        public void Log(LogLevel level, string messageTemplate, params object[] args)
        {
            if (level < MinLevel) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                MessageTemplate = messageTemplate,
                RenderedMessage = args.Length > 0 ? string.Format(messageTemplate, args) : messageTemplate,
                Properties = args.Length > 0 ? args : null,
                MachineName = Environment.MachineName,
                Application = ApplicationName
            };

            _queue.Enqueue(entry);
            LogWritten?.Invoke(this, entry);

            // Fire-and-forget flush
            _ = FlushQueueAsync();
        }

        /// <summary>
        /// Logs with structured properties (key-value pairs).
        /// </summary>
        public void LogStructured(LogLevel level, string message, params (string Key, object Value)[] properties)
        {
            if (level < MinLevel) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                MessageTemplate = message,
                RenderedMessage = message,
                MachineName = Environment.MachineName,
                Application = ApplicationName,
                StructuredProperties = properties
            };

            _queue.Enqueue(entry);
            LogWritten?.Invoke(this, entry);
            _ = FlushQueueAsync();
        }

        // Convenience methods matching SimpleLogger API
        public void Trace(string message, params object[] args) => Log(LogLevel.Trace, message, args);
        public void Debug(string message, params object[] args) => Log(LogLevel.Debug, message, args);
        public void Info(string message, params object[] args) => Log(LogLevel.Info, message, args);
        public void Warn(string message, params object[] args) => Log(LogLevel.Warn, message, args);
        public void Error(string message, params object[] args) => Log(LogLevel.Error, message, args);

        /// <summary>
        /// Logs an exception with context.
        /// </summary>
        public void Error(Exception ex, string message, params object[] args)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Error,
                MessageTemplate = message,
                RenderedMessage = args.Length > 0 ? string.Format(message, args) : message,
                ExceptionType = ex.GetType().FullName,
                ExceptionMessage = ex.Message,
                ExceptionStackTrace = ex.StackTrace,
                MachineName = Environment.MachineName,
                Application = ApplicationName
            };

            _queue.Enqueue(entry);
            LogWritten?.Invoke(this, entry);
            _ = FlushQueueAsync();
        }

        private async Task FlushQueueAsync()
        {
            if (!await _writeSemaphore.WaitAsync(0)) return; // Non-blocking

            try
            {
                var sb = new StringBuilder();

                while (_queue.TryDequeue(out var entry))
                {
                    if (EnableConsoleOutput)
                        Console.Write(FormatPlainText(entry));

                    if (EnableJsonFormat && _logDirectory != null)
                        sb.AppendLine(FormatJson(entry));
                    else if (_logDirectory != null)
                        sb.Append(FormatPlainText(entry));
                }

                if (sb.Length > 0 && _currentLogFile != null)
                {
                    await File.AppendAllTextAsync(_currentLogFile, sb.ToString());
                    _currentFileSize += sb.Length;

                    // Rotate if needed
                    if (_currentFileSize > MaxFileSizeBytes)
                        RotateLogFiles();
                }
            }
            catch
            {
                // Best-effort logging
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        private string FormatPlainText(LogEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append($"[{entry.Timestamp:O}] [{entry.Level,-5}] {entry.RenderedMessage}");
            if (entry.ExceptionMessage != null)
                sb.Append($" | Exception: {entry.ExceptionType}: {entry.ExceptionMessage}");
            sb.AppendLine();
            return sb.ToString();
        }

        private string FormatJson(LogEntry entry)
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

            writer.WriteStartObject();
            writer.WriteString("@t", entry.Timestamp.ToString("O"));
            writer.WriteString("@l", entry.Level.ToString());
            writer.WriteString("@mt", entry.MessageTemplate);
            writer.WriteString("@m", entry.RenderedMessage);
            writer.WriteString("Application", entry.Application);
            writer.WriteString("MachineName", entry.MachineName);

            if (entry.ExceptionType != null)
            {
                writer.WriteString("@x", $"{entry.ExceptionType}: {entry.ExceptionMessage}\n{entry.ExceptionStackTrace}");
            }

            if (entry.StructuredProperties != null)
            {
                foreach (var prop in entry.StructuredProperties)
                {
                    writer.WriteString(prop.Key, prop.Value?.ToString() ?? "null");
                }
            }

            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private string GetCurrentLogFilePath()
        {
            return Path.Combine(_logDirectory ?? ".", $"platypustools-{DateTime.UtcNow:yyyyMMdd}.log");
        }

        private void RotateLogFiles()
        {
            if (_logDirectory == null) return;

            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "platypustools-*.log");
                Array.Sort(logFiles);

                // Delete oldest files if over limit
                while (logFiles.Length >= MaxLogFiles)
                {
                    File.Delete(logFiles[0]);
                    logFiles = logFiles[1..];
                }

                _currentLogFile = GetCurrentLogFilePath();
                _currentFileSize = File.Exists(_currentLogFile) ? new FileInfo(_currentLogFile).Length : 0;
            }
            catch { /* best-effort rotation */ }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _writeSemaphore.Dispose();
        }
    }

    /// <summary>
    /// Structured log entry with Serilog-compatible fields.
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string MessageTemplate { get; set; } = string.Empty;
        public string RenderedMessage { get; set; } = string.Empty;
        public object[]? Properties { get; set; }
        public (string Key, object Value)[]? StructuredProperties { get; set; }
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
        public string? ExceptionStackTrace { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string Application { get; set; } = string.Empty;
    }
}
