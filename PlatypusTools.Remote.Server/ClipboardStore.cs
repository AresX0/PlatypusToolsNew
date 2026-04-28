// Phase 4.5 / 4.4 — Opaque clipboard blob store for /api/v1/clipboard endpoints.
// Latest holds the encrypted-blob payload; Plain holds the cleartext push from the
// browser extension. Both expire after Ttl elapses since the last write.
using System;

namespace PlatypusTools.Remote.Server
{
    internal static class ClipboardStore
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
        private static object? _latest;
        private static string _plain = string.Empty;
        private static DateTime _latestUtc;
        private static DateTime _plainUtc;
        private static readonly object _lock = new();

        public static object? Latest
        {
            get { lock (_lock) { return DateTime.UtcNow - _latestUtc > Ttl ? null : _latest; } }
            set { lock (_lock) { _latest = value; _latestUtc = DateTime.UtcNow; } }
        }

        public static string Plain
        {
            get { lock (_lock) { return DateTime.UtcNow - _plainUtc > Ttl ? string.Empty : _plain; } }
            set { lock (_lock) { _plain = value ?? string.Empty; _plainUtc = DateTime.UtcNow; } }
        }
    }
}
