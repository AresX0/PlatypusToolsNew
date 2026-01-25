using System;

namespace PlatypusTools.Core.Utilities
{
    /// <summary>
    /// Centralized utility for formatting values consistently across the application.
    /// Consolidates duplicate FormatBytes/FormatFileSize implementations.
    /// </summary>
    public static class FormatHelper
    {
        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

        /// <summary>
        /// Formats a byte count into a human-readable string (e.g., "1.5 GB").
        /// </summary>
        /// <param name="bytes">The number of bytes to format.</param>
        /// <param name="decimalPlaces">Number of decimal places (default: 2).</param>
        /// <returns>Formatted string like "1.5 GB".</returns>
        public static string FormatBytes(long bytes, int decimalPlaces = 2)
        {
            if (bytes < 0) return "0 B";
            
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < SizeUnits.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            string format = decimalPlaces > 0 ? $"0.{new string('#', decimalPlaces)}" : "0";
            return $"{size.ToString(format)} {SizeUnits[order]}";
        }

        /// <summary>
        /// Alias for FormatBytes for backward compatibility.
        /// </summary>
        public static string FormatFileSize(long bytes) => FormatBytes(bytes);

        /// <summary>
        /// Formats a TimeSpan into a human-readable duration string.
        /// </summary>
        /// <param name="duration">The duration to format.</param>
        /// <param name="includeMilliseconds">Whether to include milliseconds.</param>
        /// <returns>Formatted string like "1:23:45" or "23:45".</returns>
        public static string FormatDuration(TimeSpan duration, bool includeMilliseconds = false)
        {
            if (duration.TotalHours >= 1)
            {
                return includeMilliseconds
                    ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}.{duration.Milliseconds:D3}"
                    : $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            }
            
            return includeMilliseconds
                ? $"{duration.Minutes}:{duration.Seconds:D2}.{duration.Milliseconds:D3}"
                : $"{duration.Minutes}:{duration.Seconds:D2}";
        }

        /// <summary>
        /// Formats a TimeSpan into a short duration string for display.
        /// </summary>
        /// <param name="duration">The duration to format.</param>
        /// <returns>Formatted string like "2d 5h", "3h 20m", or "45m 30s".</returns>
        public static string FormatDurationShort(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}d {duration.Hours}h";
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            return $"{duration.Minutes}m {duration.Seconds}s";
        }

        /// <summary>
        /// Formats a number with thousand separators.
        /// </summary>
        /// <param name="number">The number to format.</param>
        /// <returns>Formatted string like "1,234,567".</returns>
        public static string FormatNumber(long number)
        {
            return number.ToString("N0");
        }

        /// <summary>
        /// Formats a bitrate value into a readable string.
        /// </summary>
        /// <param name="bitsPerSecond">The bitrate in bits per second.</param>
        /// <returns>Formatted string like "320 kbps" or "1.5 Mbps".</returns>
        public static string FormatBitrate(long bitsPerSecond)
        {
            if (bitsPerSecond >= 1_000_000)
                return $"{bitsPerSecond / 1_000_000.0:0.#} Mbps";
            if (bitsPerSecond >= 1000)
                return $"{bitsPerSecond / 1000} kbps";
            return $"{bitsPerSecond} bps";
        }

        /// <summary>
        /// Formats a sample rate value into a readable string.
        /// </summary>
        /// <param name="sampleRate">The sample rate in Hz.</param>
        /// <returns>Formatted string like "44.1 kHz" or "48 kHz".</returns>
        public static string FormatSampleRate(int sampleRate)
        {
            if (sampleRate >= 1000)
                return $"{sampleRate / 1000.0:0.#} kHz";
            return $"{sampleRate} Hz";
        }
    }
}
