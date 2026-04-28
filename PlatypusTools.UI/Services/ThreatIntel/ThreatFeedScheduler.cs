using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.ThreatIntel
{
    /// <summary>
    /// Phase 4.1 — periodically pulls all enabled threat feeds (CISA KEV, MISP, OTX),
    /// writes their normalized output to <c>%APPDATA%/PlatypusTools/threat-cache/*.json</c>.
    /// Other components (CVE Search badge, IOC Scanner auto-import) read from the cache
    /// directly so they don't block on network at startup.
    /// </summary>
    public sealed class ThreatFeedScheduler : IDisposable
    {
        private static readonly Lazy<ThreatFeedScheduler> _instance = new(() => new ThreatFeedScheduler());
        public static ThreatFeedScheduler Instance => _instance.Value;

        public static string CacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "threat-cache");

        public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
        public bool EnableCisaKev { get; set; } = true;
        public bool EnableMisp { get; set; }
        public string? MispUrl { get; set; }
        public string? MispKey { get; set; }
        public bool EnableOtx { get; set; }
        public string? OtxKey { get; set; }

        public DateTime LastRunUtc { get; private set; }
        public string LastError { get; private set; } = "";
        public event Action<string>? Status;

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private bool _disposed;

        public void Start()
        {
            if (_loop != null) return;
            Directory.CreateDirectory(CacheDir);
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            _loop = null;
        }

        public async Task RunOnceAsync(CancellationToken ct = default)
        {
            Directory.CreateDirectory(CacheDir);
            LastError = "";
            try
            {
                if (EnableCisaKev)
                {
                    Status?.Invoke("Pulling CISA KEV…");
                    var kev = await ThreatFeedService.Instance.GetCisaKevAsync(ct).ConfigureAwait(false);
                    Write("cisa-kev.json", kev);
                }
                if (EnableMisp && !string.IsNullOrWhiteSpace(MispUrl) && !string.IsNullOrWhiteSpace(MispKey))
                {
                    Status?.Invoke("Pulling MISP events…");
                    var misp = await ThreatFeedService.Instance.GetMispEventsAsync(MispUrl!, MispKey!, ct).ConfigureAwait(false);
                    Write("misp.json", misp);
                }
                if (EnableOtx && !string.IsNullOrWhiteSpace(OtxKey))
                {
                    Status?.Invoke("Pulling AlienVault OTX pulses…");
                    var otx = await ThreatFeedService.Instance.GetOtxPulsesAsync(OtxKey!, ct).ConfigureAwait(false);
                    Write("otx.json", otx);
                }
                LastRunUtc = DateTime.UtcNow;
                Status?.Invoke($"Threat feeds refreshed at {LastRunUtc:u}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Status?.Invoke("Refresh failed: " + ex.Message);
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            // Stagger initial run by 10s so app launch isn't slowed.
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false); }
            catch { return; }
            while (!ct.IsCancellationRequested)
            {
                await RunOnceAsync(ct).ConfigureAwait(false);
                try { await Task.Delay(Interval, ct).ConfigureAwait(false); }
                catch { break; }
            }
        }

        private static void Write<T>(string fileName, T data)
        {
            try
            {
                var path = Path.Combine(CacheDir, fileName);
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        /// <summary>
        /// Reads cached threat-feed indicators in their normalized form (one IOC per line, type:value).
        /// </summary>
        public static IEnumerable<(string Type, string Value, string Source)> ReadCachedIndicators()
        {
            if (!Directory.Exists(CacheDir)) yield break;

            // CISA KEV - CVEs only
            var kevPath = Path.Combine(CacheDir, "cisa-kev.json");
            if (File.Exists(kevPath))
            {
                List<KevEntry>? entries = null;
                try { entries = JsonSerializer.Deserialize<List<KevEntry>>(File.ReadAllText(kevPath)); } catch { }
                if (entries != null)
                    foreach (var e in entries)
                        if (!string.IsNullOrEmpty(e.CveId)) yield return ("CVE", e.CveId, "CISA KEV");
            }

            // MISP - heterogeneous IOCs
            var mispPath = Path.Combine(CacheDir, "misp.json");
            if (File.Exists(mispPath))
            {
                List<MispIoc>? iocs = null;
                try { iocs = JsonSerializer.Deserialize<List<MispIoc>>(File.ReadAllText(mispPath)); } catch { }
                if (iocs != null)
                    foreach (var i in iocs)
                        if (!string.IsNullOrEmpty(i.Value) && !string.IsNullOrEmpty(i.Type))
                            yield return (i.Type, i.Value, "MISP");
            }

            // OTX
            var otxPath = Path.Combine(CacheDir, "otx.json");
            if (File.Exists(otxPath))
            {
                List<OtxIoc>? iocs = null;
                try { iocs = JsonSerializer.Deserialize<List<OtxIoc>>(File.ReadAllText(otxPath)); } catch { }
                if (iocs != null)
                    foreach (var i in iocs)
                        if (!string.IsNullOrEmpty(i.Indicator) && !string.IsNullOrEmpty(i.Type))
                            yield return (i.Type, i.Indicator, "AlienVault OTX");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
        }
    }
}
