using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Wallpaper
{
    /// <summary>
    /// Fetches and caches NASA Artemis missions and launch schedule, with offline fallback.
    /// </summary>
    public class NasaInfoService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "nasa_cache.json");

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", "PlatypusTools/4.0 (NASA overlay)" } }
        };

        public sealed record Mission(string Name, string Status, string MissionType,
            string LaunchDate, string Duration, string Splashdown, string Description, string Url);

        public sealed record LaunchEvent(string Name, string Date, string Url);

        public sealed record NasaSnapshot(IReadOnlyList<Mission> Missions, IReadOnlyList<LaunchEvent> Launches, DateTimeOffset FetchedAt);

        private sealed record CacheFile(NasaSnapshot Data);

        public static IReadOnlyList<Mission> FallbackMissions { get; } = new List<Mission>
        {
            new("Artemis I", "Completed", "Uncrewed Lunar Flight Test", "Nov. 16, 2022",
                "25 days, 10 hours, 53 minutes", "Dec. 11, 2022",
                "First integrated flight test of SLS and Orion. Travelled 1.4 million miles. Re-entry speed: 24,581 mph (Mach 32).",
                "https://www.nasa.gov/mission/artemis-i/"),
            new("Artemis II", "Completed", "Crewed Lunar Flyby", "April 1, 2026",
                "9 days, 1 hour, 32 minutes", "April 10, 2026",
                "First crewed Artemis flight. Four astronauts ventured around the Moon, demonstrating capabilities for deep space missions.",
                "https://www.nasa.gov/mission/artemis-ii/"),
            new("Artemis III", "Upcoming", "Rendezvous & Docking in LEO", "2027", "", "",
                "LEO demonstration testing Orion rendezvous and docking with commercial landers from SpaceX and/or Blue Origin.",
                "https://www.nasa.gov/mission/artemis-iii/"),
            new("Artemis IV", "Upcoming", "First Lunar Landing", "Early 2028", "", "",
                "First Artemis crewed lunar landing. Crew transfers from Orion to a commercial lander for transport to the lunar surface.",
                "https://www.nasa.gov/event/artemis-iv/"),
            new("Artemis V", "Upcoming", "Lunar Surface Mission", "Late 2028", "", "",
                "Lunar surface mission using standardized SLS configuration. Expected to begin construction of a Moon base.",
                "https://www.nasa.gov/event/artemis-v/"),
        };

        public static IReadOnlyList<LaunchEvent> FallbackLaunches { get; } = new List<LaunchEvent>
        {
            new("Northrop Grumman CRS-24", "NET April 11, 2026", "https://www.nasa.gov/event/nasas-northrop-grumman-crs-24/"),
            new("Boeing Starliner-1", "NET April 2026", "https://www.nasa.gov/event/nasas-boeing-starliner-1/"),
            new("SpaceX CRS-34", "NET May 2026", "https://www.nasa.gov/event/nasas-spacex-crs-34/"),
            new("Roscosmos Progress 95", "NET Spring 2026", "https://www.nasa.gov/event/roscosmos-progress-95/"),
            new("Soyuz MS-29", "NET July 2026", "https://www.nasa.gov/event/soyuz-ms-29/"),
            new("CLPS: Astrobotic Griffin-1", "NET July 2026", "https://www.nasa.gov/event/clps-flight-astrobotics-griffin-mission-one/"),
            new("SpaceX CRS-35", "NET August 2026", "https://www.nasa.gov/event/nasas-spacex-crs-35/"),
            new("JAXA HTV-X2", "NET Summer 2026", "https://www.nasa.gov/event/jaxa-htv-x2/"),
            new("Northrop Grumman CRS-25", "NET Fall 2026", "https://www.nasa.gov/event/nasas-northrop-grumman-crs-25/"),
            new("CLPS: Blue Ghost Mission 2", "2026", "https://www.nasa.gov/event/clps-flight-firefly-aerospaces-blue-ghost-mission-2/"),
            new("CLPS: Blue Moon Mark 1", "2026", "https://www.nasa.gov/event/clps-flight-blue-origins-blue-moon-mark-1/"),
            new("Nancy Grace Roman Space Telescope", "NLT May 2027", "https://www.nasa.gov/event/nancy-grace-roman-space-telescope/"),
            new("Artemis III", "2027", "https://www.nasa.gov/event/artemis-iii-launch/"),
            new("Artemis IV", "Early 2028", "https://www.nasa.gov/event/artemis-iv/"),
            new("Artemis V", "Late 2028", "https://www.nasa.gov/event/artemis-v/"),
        };

        /// <summary>
        /// Returns cached snapshot if fresh, else attempts a live refresh, falling back to bundled data.
        /// </summary>
        public async Task<NasaSnapshot> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!forceRefresh)
            {
                var cached = TryLoadCache();
                if (cached != null && DateTimeOffset.UtcNow - cached.FetchedAt < CacheTtl)
                    return cached;
            }

            try
            {
                var live = await FetchLiveAsync(ct).ConfigureAwait(false);
                TrySaveCache(live);
                return live;
            }
            catch
            {
                // Fall through to cached-or-fallback
            }

            return TryLoadCache()
                ?? new NasaSnapshot(FallbackMissions, FallbackLaunches, DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Renders the current snapshot as a list of overlay lines for image burn-in.
        /// </summary>
        public static IReadOnlyList<string> BuildOverlayLines(NasaSnapshot snapshot, int maxLines = 18)
        {
            var lines = new List<string>();
            lines.Add("═══ ARTEMIS PROGRAM ═══");

            foreach (var m in snapshot.Missions)
            {
                if (lines.Count >= maxLines) break;
                string prefix = m.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? "✓"
                              : m.Status.Equals("Active",    StringComparison.OrdinalIgnoreCase) ? "●"
                              : "◇";
                lines.Add($"{prefix} {m.Name} — {m.LaunchDate}");
            }

            if (lines.Count < maxLines)
            {
                lines.Add("");
                lines.Add("═══ UPCOMING LAUNCHES ═══");
                foreach (var l in snapshot.Launches)
                {
                    if (lines.Count >= maxLines) break;
                    lines.Add($"▸ {l.Name} ({l.Date})");
                }
            }

            return lines;
        }

        // ── Live fetch (best-effort; falls back on any failure) ─────────────────

        private async Task<NasaSnapshot> FetchLiveAsync(CancellationToken ct)
        {
            // Start from the curated list and try to enrich/replace with live launch data.
            var missions = FallbackMissions.ToList();
            var launches = new List<LaunchEvent>(FallbackLaunches);

            try
            {
                string html = await Http.GetStringAsync("https://www.nasa.gov/launch-schedule/", ct).ConfigureAwait(false);
                var live = ParseLaunchSchedule(html);
                if (live.Count > 0) launches = live;
            }
            catch { /* keep fallback */ }

            return new NasaSnapshot(missions, launches, DateTimeOffset.UtcNow);
        }

        private static List<LaunchEvent> ParseLaunchSchedule(string html)
        {
            var found = new List<LaunchEvent>();
            // Best-effort extraction: NASA wraps each event in an <a href="…/event/…"> with a date snippet nearby.
            var anchorRx = new Regex(
                @"<a[^>]+href=""(https://www\.nasa\.gov/event/[^""]+)""[^>]*>\s*(?:<[^>]+>\s*)*(?<name>[^<]{4,160})",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (Match m in anchorRx.Matches(html))
            {
                var name = WebDecode(m.Groups["name"].Value).Trim();
                var url = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3) continue;
                if (found.Any(x => x.Url == url)) continue;
                found.Add(new LaunchEvent(name, "", url));
                if (found.Count >= 25) break;
            }

            return found;
        }

        private static string WebDecode(string s)
        {
            return s.Replace("&amp;", "&")
                    .Replace("&#039;", "'")
                    .Replace("&quot;", "\"")
                    .Replace("&nbsp;", " ")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">");
        }

        // ── Cache I/O ──────────────────────────────────────────────────────────

        private static NasaSnapshot? TryLoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var json = File.ReadAllText(CachePath, Encoding.UTF8);
                return JsonSerializer.Deserialize<NasaSnapshot>(json);
            }
            catch { return null; }
        }

        private static void TrySaveCache(NasaSnapshot snapshot)
        {
            try
            {
                var dir = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(CachePath,
                    JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }
            catch { /* non-fatal */ }
        }
    }
}
