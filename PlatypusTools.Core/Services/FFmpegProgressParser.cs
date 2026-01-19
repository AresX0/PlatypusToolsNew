using System;
using System.Globalization;

namespace PlatypusTools.Core.Services
{
    public static class FFmpegProgressParser
    {
        /// <summary>
        /// Parses FFmpeg progress output and returns normalized progress (0-1).
        /// </summary>
        /// <param name="line">FFmpeg stderr output line.</param>
        /// <param name="totalDuration">Total duration of the media.</param>
        /// <returns>Progress value from 0 to 1, or null if line doesn't contain time info.</returns>
        public static double? ParseProgressLine(string line, TimeSpan totalDuration)
        {
            if (string.IsNullOrWhiteSpace(line) || totalDuration.TotalSeconds <= 0)
                return null;

            // Try to parse "time=HH:MM:SS.mm" format from stderr
            var timeIdx = line.IndexOf("time=", StringComparison.OrdinalIgnoreCase);
            if (timeIdx >= 0)
            {
                var timeStart = timeIdx + 5;
                var timeEnd = line.IndexOf(' ', timeStart);
                if (timeEnd < 0) timeEnd = line.Length;

                var timeStr = line[timeStart..timeEnd].Trim();
                if (TimeSpan.TryParse(timeStr, out var currentTime))
                {
                    return Math.Min(1.0, currentTime.TotalSeconds / totalDuration.TotalSeconds);
                }
            }

            // Try out_time_ms format
            if (TryParseOutTimeMs(line, out var ms))
            {
                return Math.Min(1.0, ms / 1000.0 / totalDuration.TotalSeconds);
            }

            return null;
        }

        // Parses lines produced by ffmpeg -progress pipe:1, e.g. "out_time_ms=12345" or "out_time=00:00:12.345"
        public static bool TryParseOutTimeMs(string line, out long outTimeMs)
        {
            outTimeMs = 0;
            if (string.IsNullOrWhiteSpace(line)) return false;
            var parts = line.Split('=', 2);
            if (parts.Length != 2) return false;
            var key = parts[0].Trim();
            var val = parts[1].Trim();
            if (key.Equals("out_time_ms", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out outTimeMs)) return true;
                return false;
            }
            if (key.Equals("out_time", StringComparison.OrdinalIgnoreCase))
            {
                // format HH:MM:SS.mmm
                if (TimeSpan.TryParseExact(val, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var ts))
                {
                    outTimeMs = (long)ts.TotalMilliseconds;
                    return true;
                }
                // try general parse
                if (TimeSpan.TryParse(val, out ts))
                {
                    outTimeMs = (long)ts.TotalMilliseconds;
                    return true;
                }
                return false;
            }
            return false;
        }
    }
}