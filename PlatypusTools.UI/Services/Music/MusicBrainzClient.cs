using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services.Music
{
    /// <summary>
    /// Phase 3.5 — minimal MusicBrainz / Cover Art Archive lookup. Returns the URL of
    /// the best front-cover image for an artist/album, or null if not found.
    /// Read-only HTTP. Caller is responsible for downloading + caching.
    /// </summary>
    public sealed class MusicBrainzClient
    {
        private static readonly Lazy<MusicBrainzClient> _instance = new(() => new MusicBrainzClient());
        public static MusicBrainzClient Instance => _instance.Value;

        private readonly HttpClient _http;
        private const string MbBase = "https://musicbrainz.org/ws/2";
        private const string CaaBase = "https://coverartarchive.org/release";

        private MusicBrainzClient()
        {
            _http = HttpClientFactory.Api;
            // MusicBrainz requires a User-Agent identifying the app.
            if (!_http.DefaultRequestHeaders.UserAgent.ToString().Contains("PlatypusTools"))
            {
                try { _http.DefaultRequestHeaders.UserAgent.ParseAdd("PlatypusTools/1.0 (https://github.com/AresX0/PlatypusToolsNew)"); }
                catch { }
            }
        }

        public async Task<string?> FindFrontCoverUrlAsync(string artist, string album, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album)) return null;
            try
            {
                var query = Uri.EscapeDataString($"release:\"{album}\" AND artist:\"{artist}\"");
                var url = $"{MbBase}/release/?query={query}&fmt=json&limit=3";
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("releases", out var rels)) return null;
                foreach (var r in rels.EnumerateArray())
                {
                    if (!r.TryGetProperty("id", out var idEl)) continue;
                    var id = idEl.GetString();
                    if (string.IsNullOrEmpty(id)) continue;
                    var caaUrl = $"{CaaBase}/{id}/front";
                    using var head = new HttpRequestMessage(HttpMethod.Head, caaUrl);
                    using var headResp = await _http.SendAsync(head, ct).ConfigureAwait(false);
                    if (headResp.IsSuccessStatusCode) return caaUrl;
                }
            }
            catch { }
            return null;
        }
    }
}
