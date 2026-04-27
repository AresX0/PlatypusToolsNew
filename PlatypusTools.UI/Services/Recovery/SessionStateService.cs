using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PlatypusTools.UI.Services.Recovery
{
    /// <summary>
    /// Phase 1.5 — periodically snapshots registered <see cref="IRecoverableState"/>
    /// providers to disk. Uses a "clean shutdown" sentinel file so that if the app
    /// crashes (sentinel still present at next launch) we can prompt the user to restore.
    ///
    /// Storage: %APPDATA%\PlatypusTools\sessions\&lt;recoveryKey&gt;.json
    /// Sentinel: %APPDATA%\PlatypusTools\sessions\.dirty
    /// </summary>
    public sealed class SessionStateService
    {
        private static readonly Lazy<SessionStateService> _instance = new(() => new SessionStateService());
        public static SessionStateService Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, IRecoverableState> _providers = new();
        private readonly string _dir;
        private readonly string _dirtyMarker;
        private Timer? _timer;
        private bool _started;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private SessionStateService()
        {
            _dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "sessions");
            _dirtyMarker = Path.Combine(_dir, ".dirty");
        }

        /// <summary>Whether the previous shutdown was non-clean (sentinel file is present).</summary>
        public bool LastShutdownWasDirty { get; private set; }

        /// <summary>List of recovery snapshots present from the previous (dirty) session.</summary>
        public IReadOnlyList<string> AvailableSnapshots { get; private set; } = Array.Empty<string>();

        public void Initialize()
        {
            try
            {
                Directory.CreateDirectory(_dir);
                LastShutdownWasDirty = File.Exists(_dirtyMarker);
                if (LastShutdownWasDirty)
                {
                    var snaps = Directory.GetFiles(_dir, "*.json")
                        .Select(p => Path.GetFileNameWithoutExtension(p) ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();
                    AvailableSnapshots = snaps;
                }
                // Plant the dirty sentinel for THIS run.
                File.WriteAllText(_dirtyMarker, DateTime.UtcNow.ToString("o"));
            }
            catch { /* best-effort */ }
        }

        public void Register(IRecoverableState provider)
        {
            if (provider == null || string.IsNullOrEmpty(provider.RecoveryKey)) return;
            _providers[provider.RecoveryKey] = provider;
        }

        public void Start(TimeSpan? interval = null)
        {
            if (_started) return;
            _started = true;
            var period = interval ?? TimeSpan.FromSeconds(30);
            _timer = new Timer(_ => SnapshotAll(), null, period, period);
        }

        public void SnapshotAll()
        {
            foreach (var kv in _providers)
            {
                try
                {
                    var state = kv.Value.CaptureState();
                    if (state == null || state.Count == 0)
                    {
                        SafeDelete(SnapshotPath(kv.Key));
                        continue;
                    }
                    var path = SnapshotPath(kv.Key);
                    File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOpts));
                }
                catch { /* best-effort per provider */ }
            }
        }

        /// <summary>
        /// Attempts to restore each registered provider whose snapshot exists.
        /// Returns the keys that were actually restored.
        /// </summary>
        public IList<string> RestoreFromAvailable()
        {
            var restored = new List<string>();
            foreach (var kv in _providers)
            {
                var path = SnapshotPath(kv.Key);
                if (!File.Exists(path)) continue;
                try
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var dict = ToDictionary(doc.RootElement);
                    kv.Value.RestoreState(dict);
                    restored.Add(kv.Key);
                }
                catch { /* skip bad snapshots */ }
            }
            return restored;
        }

        /// <summary>Mark this session as cleanly shut down (removes sentinel + snapshots).</summary>
        public void MarkCleanShutdown()
        {
            try
            {
                _timer?.Dispose();
                _timer = null;
                if (Directory.Exists(_dir))
                {
                    foreach (var f in Directory.GetFiles(_dir))
                    {
                        SafeDelete(f);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Show a prompt at startup if the last shutdown was dirty AND snapshots exist.
        /// Returns true if the user opted to restore (and snapshots were restored).
        /// </summary>
        public bool PromptAndRestoreIfNeeded()
        {
            if (!LastShutdownWasDirty || AvailableSnapshots.Count == 0) return false;
            var msg = $"PlatypusTools didn't shut down cleanly last time.\n\n" +
                      $"In-progress state was found for:\n  • " +
                      string.Join("\n  • ", AvailableSnapshots) +
                      "\n\nRestore these now?";
            var result = MessageBox.Show(msg, "Recover previous session",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                // User declined: clear snapshots so we don't ask again.
                foreach (var key in AvailableSnapshots) SafeDelete(SnapshotPath(key));
                return false;
            }
            var restored = RestoreFromAvailable();
            return restored.Count > 0;
        }

        private string SnapshotPath(string key) => Path.Combine(_dir, $"{key}.json");

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static IDictionary<string, object?> ToDictionary(JsonElement element)
        {
            var dict = new Dictionary<string, object?>();
            if (element.ValueKind != JsonValueKind.Object) return dict;
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = ToValue(prop.Value);
            }
            return dict;
        }

        private static object? ToValue(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => ToDictionary(el),
            JsonValueKind.Array => el.EnumerateArray().Select(ToValue).ToList(),
            _ => el.ToString()
        };
    }
}
