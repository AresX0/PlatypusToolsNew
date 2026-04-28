using System;
using System.Collections.Generic;

namespace PlatypusTools.UI.Services.Diagnostics
{
    /// <summary>
    /// No-op telemetry shell. Always defaults to OFF; even when "enabled" by
    /// the user it currently writes nothing — events are buffered locally and
    /// dropped on shutdown. The point is to define the surface so any future
    /// real backend can be wired in one place without hunting through call
    /// sites, and so the privacy toggle is visible to the user today.
    ///
    /// Privacy contract:
    ///   • Default OFF (AppSettings.TelemetryOptIn = false).
    ///   • No HTTP calls, no file persistence beyond ActivityLog.
    ///   • Disabled at the call site short-circuits before any payload work.
    /// </summary>
    public sealed class TelemetryService
    {
        private static readonly Lazy<TelemetryService> _instance = new(() => new TelemetryService());
        public static TelemetryService Instance => _instance.Value;

        public bool Enabled { get; set; } // mirrored from AppSettings.TelemetryOptIn at startup

        public void TrackEvent(string name, IReadOnlyDictionary<string, string>? properties = null)
        {
            if (!Enabled) return;
            // Future: forward to a self-host collector. For now, just mirror
            // into the activity log so power users can see what *would* be
            // sent if a backend were wired up.
            try
            {
                var summary = properties == null || properties.Count == 0
                    ? name
                    : name + " " + string.Join(", ", FormatProps(properties));
                ActivityLogService.Instance.Debug("Telemetry", summary);
            }
            catch { }
        }

        public void TrackException(Exception ex, string? context = null)
        {
            if (!Enabled) return;
            try
            {
                ActivityLogService.Instance.Debug(
                    "Telemetry",
                    $"exception/{ex.GetType().Name}: {ex.Message}" + (context != null ? $" ({context})" : ""));
            }
            catch { }
        }

        private static IEnumerable<string> FormatProps(IReadOnlyDictionary<string, string> p)
        {
            foreach (var kv in p) yield return kv.Key + "=" + kv.Value;
        }
    }
}
