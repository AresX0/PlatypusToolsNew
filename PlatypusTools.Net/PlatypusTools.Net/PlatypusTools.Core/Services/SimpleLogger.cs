using System;
using System.IO;

namespace PlatypusTools.Core.Services
{
    public enum LogLevel { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4 }

    public static class SimpleLogger
    {
        private static readonly object _sync = new object();
        public static string? LogFile { get; set; }
        public static LogLevel MinLevel { get; set; } = LogLevel.Info;

        private static string FormatEntry(LogLevel level, string message)
        {
            return $"[{DateTime.UtcNow:O}] [{level}] {message}{Environment.NewLine}";
        }

        public static void Log(LogLevel level, string message)
        {
            try
            {
                if (level < MinLevel) return;
                var entry = FormatEntry(level, message);
                if (string.IsNullOrEmpty(LogFile)) Console.Write(entry);
                else
                {
                    lock (_sync)
                    {
                        var dir = Path.GetDirectoryName(LogFile) ?? ".";
                        Directory.CreateDirectory(dir);
                        File.AppendAllText(LogFile, entry);
                    }
                }
            }
            catch { /* best-effort logging */ }
        }

        public static void Trace(string message) => Log(LogLevel.Trace, message);
        public static void Debug(string message) => Log(LogLevel.Debug, message);
        public static void Info(string message) => Log(LogLevel.Info, message);
        public static void Warn(string message) => Log(LogLevel.Warn, message);
        public static void Error(string message) => Log(LogLevel.Error, message);
    }
}