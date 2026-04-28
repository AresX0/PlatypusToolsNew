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

        // ─── Phase 4.1 — MISP feed ────────────────────────────────────────────────────
        // Pull recent events from a MISP instance. Endpoint: GET {baseUrl}/events/restSearch
        // with header Authorization: <key>. Returns flattened attribute IOCs.
        public async Task<IReadOnlyList<MispIoc>> GetMispEventsAsync(string baseUrl, string apiKey, CancellationToken ct = default)
        {
            var list = new List<MispIoc>();
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey)) return list;
            try
            {
                var url = baseUrl.TrimEnd('/') + "/events/restSearch";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("Authorization", apiKey);
                req.Headers.Add("Accept", "application/json");
                req.Content = new StringContent("{\"limit\":200,\"includeEventTags\":1}", System.Text.Encoding.UTF8, "application/json");
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return list;
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("response", out var responses)) return list;
                foreach (var ev in responses.EnumerateArray())
                {
                    if (!ev.TryGetProperty("Event", out var e)) continue;
                    var info = Get(e, "info");
                    if (!e.TryGetProperty("Attribute", out var attrs)) continue;
                    foreach (var a in attrs.EnumerateArray())
                    {
                        var t = Get(a, "type");
                        var v = Get(a, "value");
                        if (string.IsNullOrEmpty(v)) continue;
                        list.Add(new MispIoc(NormalizeMispType(t), v, info));
                    }
                }
            }
            catch { }
            return list;
        }

        private static string NormalizeMispType(string mispType) => mispType.ToLowerInvariant() switch
        {
            "ip-src" or "ip-dst" or "ip-src|port" or "ip-dst|port" => "IPv4",
            "domain" or "hostname" => "Domain",
            "url" or "uri" => "URL",
            "md5" => "MD5",
            "sha1" => "SHA1",
            "sha256" => "SHA256",
            "email" or "email-src" or "email-dst" => "Email",
            "filename" => "FileName",
            "vulnerability" => "CVE",
            _ => "Custom"
        };

        // ─── Phase 4.1 — AlienVault OTX feed ──────────────────────────────────────────
        // Pulls subscribed pulses' indicators. Endpoint: GET https://otx.alienvault.com/api/v1/pulses/subscribed
        public async Task<IReadOnlyList<OtxIoc>> GetOtxPulsesAsync(string apiKey, CancellationToken ct = default)
        {
            var list = new List<OtxIoc>();
            if (string.IsNullOrWhiteSpace(apiKey)) return list;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    "https://otx.alienvault.com/api/v1/pulses/subscribed?limit=50");
                req.Headers.Add("X-OTX-API-KEY", apiKey);
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return list;
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("results", out var pulses)) return list;
                foreach (var p in pulses.EnumerateArray())
                {
                    var pulseName = Get(p, "name");
                    if (!p.TryGetProperty("indicators", out var inds)) continue;
                    foreach (var i in inds.EnumerateArray())
                    {
                        var t = Get(i, "type");
                        var v = Get(i, "indicator");
                        if (string.IsNullOrEmpty(v)) continue;
                        list.Add(new OtxIoc(NormalizeOtxType(t), v, pulseName));
                    }
                }
            }
            catch { }
            return list;
        }

        private static string NormalizeOtxType(string otxType) => otxType.ToUpperInvariant() switch
        {
            "IPV4" => "IPv4",
            "IPV6" => "IPv6",
            "DOMAIN" or "HOSTNAME" => "Domain",
            "URL" or "URI" => "URL",
            "FILEHASH-MD5" => "MD5",
            "FILEHASH-SHA1" => "SHA1",
            "FILEHASH-SHA256" => "SHA256",
            "EMAIL" => "Email",
            "CVE" => "CVE",
            _ => "Custom"
        };
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

    public sealed record MispIoc(string Type, string Value, string EventInfo);
    public sealed record OtxIoc(string Type, string Indicator, string PulseName);
}
