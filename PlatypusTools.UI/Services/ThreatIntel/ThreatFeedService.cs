using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services.ThreatIntel
{
    /// <summary>
    /// Phase 4.1 — minimal CISA KEV (Known Exploited Vulnerabilities) feed. Pure pull.
    /// Future enhancements (deferred): MISP / OTX with API keys, local caching,
    /// scheduled refresh + UI integration in CveSearch / IocScanner.
    /// </summary>
    public sealed class ThreatFeedService
    {
        private static readonly Lazy<ThreatFeedService> _instance = new(() => new ThreatFeedService());
        public static ThreatFeedService Instance => _instance.Value;

        private readonly HttpClient _http;
        private const string CisaKevUrl = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";

        private ThreatFeedService() { _http = HttpClientFactory.Api; }

        public async Task<IReadOnlyList<KevEntry>> GetCisaKevAsync(CancellationToken ct = default)
        {
            var list = new List<KevEntry>();
            try
            {
                using var resp = await _http.GetAsync(CisaKevUrl, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return list;
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("vulnerabilities", out var vulns)) return list;
                foreach (var v in vulns.EnumerateArray())
                {
                    list.Add(new KevEntry(
                        Get(v, "cveID"),
                        Get(v, "vendorProject"),
                        Get(v, "product"),
                        Get(v, "vulnerabilityName"),
                        Get(v, "shortDescription"),
                        Get(v, "dateAdded"),
                        Get(v, "dueDate"),
                        Get(v, "knownRansomwareCampaignUse")));
                }
            }
            catch { }
            return list;
        }

        private static string Get(JsonElement el, string name)
            => el.TryGetProperty(name, out var v) ? (v.GetString() ?? "") : "";
    }

    public sealed record KevEntry(
        string CveId,
        string Vendor,
        string Product,
        string Name,
        string Description,
        string DateAdded,
        string DueDate,
        string Ransomware);
}
