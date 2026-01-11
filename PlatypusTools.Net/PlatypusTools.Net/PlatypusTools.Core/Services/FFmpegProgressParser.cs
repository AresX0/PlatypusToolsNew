using System;
using System.Globalization;

namespace PlatypusTools.Core.Services
{
    public static class FFmpegProgressParser
    {
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