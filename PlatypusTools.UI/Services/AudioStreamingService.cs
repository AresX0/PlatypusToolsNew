using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services;

/// <summary>
/// Service for streaming audio from URLs (Internet Radio, direct links, yt-dlp).
/// Supports Shoutcast/Icecast streams, direct HTTP audio URLs, and YouTube via yt-dlp.
/// </summary>
public class AudioStreamingService
{
    private static AudioStreamingService? _instance;
    public static AudioStreamingService Instance => _instance ??= new AudioStreamingService();
    
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    
    // Saved radio stations
    private readonly List<RadioStation> _stations = new();
    private string _stationsPath;
    
    // Stream state
    private MediaFoundationReader? _streamReader;
    private CancellationTokenSource? _streamCts;
    
    // Events
    public event EventHandler<string>? StreamStatusChanged;
    public event EventHandler<StreamMetadata>? MetadataUpdated;
    public event EventHandler<string>? StreamError;
    public event EventHandler<double>? BufferProgressChanged;
    
    public IReadOnlyList<RadioStation> Stations => _stations.AsReadOnly();
    
    public AudioStreamingService()
    {
        _stationsPath = Path.Combine(SettingsManager.DataDirectory, "RadioStations.json");
        _ = LoadStationsAsync();
    }
    
    /// <summary>
    /// Creates an IWaveProvider from a stream URL using MediaFoundationReader.
    /// Supports HTTP/HTTPS audio streams (Shoutcast, Icecast, direct MP3/AAC/OGG links).
    /// </summary>
    public async Task<StreamPlaybackResult?> OpenStreamAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            _streamCts?.Cancel();
            _streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            StreamStatusChanged?.Invoke(this, "Connecting...");
            BufferProgressChanged?.Invoke(this, 0);
            
            // Check if it's a YouTube URL - extract audio via yt-dlp
            if (IsYouTubeUrl(url))
            {
                return await OpenYouTubeStreamAsync(url, _streamCts.Token);
            }
            
            // Check if it's a SoundCloud URL
            if (IsSoundCloudUrl(url))
            {
                return await OpenSoundCloudStreamAsync(url, _streamCts.Token);
            }
            
            // Check if it's a paid streaming service URL (Spotify, Apple, Amazon, Tidal, Deezer, Bandcamp)
            if (IsPaidStreamingServiceUrl(url))
            {
                return await OpenPaidStreamAsync(url, _streamCts.Token);
            }
            
            // Check if URL is a playlist file (.m3u, .pls)
            string resolvedUrl = await ResolvePlaylistUrl(url, _streamCts.Token);
            
            StreamStatusChanged?.Invoke(this, "Buffering...");
            BufferProgressChanged?.Invoke(this, 25);
            
            // Use MediaFoundationReader for HTTP streams
            _streamReader?.Dispose();
            _streamReader = await Task.Run(() => new MediaFoundationReader(resolvedUrl), _streamCts.Token);
            
            BufferProgressChanged?.Invoke(this, 100);
            StreamStatusChanged?.Invoke(this, "Playing");
            
            // Try to get stream metadata (Shoutcast/Icecast title)
            _ = PollStreamMetadataAsync(resolvedUrl, _streamCts.Token);
            
            return new StreamPlaybackResult
            {
                WaveProvider = _streamReader,
                Title = ExtractStationName(url),
                Url = url,
                IsLiveStream = true
            };
        }
        catch (OperationCanceledException)
        {
            StreamStatusChanged?.Invoke(this, "Cancelled");
            return null;
        }
        catch (Exception ex)
        {
            StreamError?.Invoke(this, $"Stream error: {ex.Message}");
            StreamStatusChanged?.Invoke(this, "Error");
            return null;
        }
    }
    
    /// <summary>
    /// Extract audio from YouTube URL using yt-dlp subprocess.
    /// </summary>
    private async Task<StreamPlaybackResult?> OpenYouTubeStreamAsync(string url, CancellationToken ct)
    {
        StreamStatusChanged?.Invoke(this, "Extracting YouTube audio...");
        BufferProgressChanged?.Invoke(this, 10);
        
        // Ensure yt-dlp is available (auto-downloads if missing)
        string ytdlp = await EnsureYtDlpAsync(ct);
        if (string.IsNullOrEmpty(ytdlp))
        {
            return null;
        }
        
        try
        {
            // Get direct audio URL from yt-dlp
            var psi = new ProcessStartInfo
            {
                FileName = ytdlp,
                Arguments = $"-f bestaudio --get-url --no-warnings \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) throw new Exception("Failed to start yt-dlp");
            
            string audioUrl = (await process.StandardOutput.ReadToEndAsync(ct)).Trim();
            string error = (await process.StandardError.ReadToEndAsync(ct)).Trim();
            await process.WaitForExitAsync(ct);
            
            if (process.ExitCode != 0 || string.IsNullOrEmpty(audioUrl))
            {
                throw new Exception($"yt-dlp failed: {error}");
            }
            
            BufferProgressChanged?.Invoke(this, 50);
            
            // Get title
            string title = "YouTube Audio";
            try
            {
                var titlePsi = new ProcessStartInfo
                {
                    FileName = ytdlp,
                    Arguments = $"--get-title --no-warnings \"{url}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var titleProcess = Process.Start(titlePsi);
                if (titleProcess != null)
                {
                    title = (await titleProcess.StandardOutput.ReadToEndAsync(ct)).Trim();
                    await titleProcess.WaitForExitAsync(ct);
                    if (string.IsNullOrEmpty(title)) title = "YouTube Audio";
                }
            }
            catch { }
            
            StreamStatusChanged?.Invoke(this, "Buffering YouTube audio...");
            BufferProgressChanged?.Invoke(this, 75);
            
            _streamReader?.Dispose();
            _streamReader = await Task.Run(() => new MediaFoundationReader(audioUrl), ct);
            
            BufferProgressChanged?.Invoke(this, 100);
            StreamStatusChanged?.Invoke(this, "Playing");
            
            return new StreamPlaybackResult
            {
                WaveProvider = _streamReader,
                Title = title,
                Url = url,
                IsLiveStream = false
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            StreamError?.Invoke(this, $"YouTube extraction failed: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Open a SoundCloud URL using yt-dlp (which supports SoundCloud natively).
    /// </summary>
    private async Task<StreamPlaybackResult?> OpenSoundCloudStreamAsync(string url, CancellationToken ct)
    {
        StreamStatusChanged?.Invoke(this, "Extracting SoundCloud audio...");
        // yt-dlp handles SoundCloud URLs natively
        return await OpenYouTubeStreamAsync(url, ct); // Same flow works for SoundCloud
    }
    
    /// <summary>
    /// Resolve .m3u or .pls playlist files to actual stream URLs.
    /// </summary>
    private async Task<string> ResolvePlaylistUrl(string url, CancellationToken ct)
    {
        string lower = url.ToLowerInvariant();
        
        if (lower.EndsWith(".m3u") || lower.EndsWith(".m3u8"))
        {
            try
            {
                string content = await _httpClient.GetStringAsync(url, ct);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith('#') && (trimmed.StartsWith("http://") || trimmed.StartsWith("https://")))
                        return trimmed;
                }
            }
            catch { }
        }
        else if (lower.EndsWith(".pls"))
        {
            try
            {
                string content = await _httpClient.GetStringAsync(url, ct);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("File1=", StringComparison.OrdinalIgnoreCase))
                        return trimmed[6..];
                }
            }
            catch { }
        }
        
        return url;
    }
    
    /// <summary>
    /// Poll for Shoutcast/Icecast metadata (current song title).
    /// </summary>
    private async Task PollStreamMetadataAsync(string url, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(15000, ct); // Check every 15 seconds
                
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Icy-MetaData", "1");
                
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                
                if (response.Headers.TryGetValues("icy-name", out var names))
                {
                    string stationName = names.FirstOrDefault() ?? "";
                    if (!string.IsNullOrEmpty(stationName))
                    {
                        MetadataUpdated?.Invoke(this, new StreamMetadata { StationName = stationName });
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* Metadata polling is non-critical */ }
        }
    }
    
    /// <summary>
    /// Stop the current stream and release resources.
    /// </summary>
    public void StopStream()
    {
        _streamCts?.Cancel();
        _streamReader?.Dispose();
        _streamReader = null;
        StreamStatusChanged?.Invoke(this, "Stopped");
    }
    
    // --- Radio Station Management ---
    
    public void AddStation(RadioStation station)
    {
        if (!_stations.Any(s => string.Equals(s.Url, station.Url, StringComparison.OrdinalIgnoreCase)))
        {
            _stations.Add(station);
            _ = SaveStationsAsync();
        }
    }
    
    public void RemoveStation(RadioStation station)
    {
        _stations.Remove(station);
        _ = SaveStationsAsync();
    }
    
    private async Task SaveStationsAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_stationsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_stations, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_stationsPath, json);
        }
        catch { }
    }
    
    private async Task LoadStationsAsync()
    {
        try
        {
            if (!File.Exists(_stationsPath)) 
            {
                // Add some default stations
                _stations.AddRange(GetDefaultStations());
                return;
            }
            var json = await File.ReadAllTextAsync(_stationsPath);
            var stations = JsonSerializer.Deserialize<List<RadioStation>>(json);
            if (stations != null) _stations.AddRange(stations);
        }
        catch { _stations.AddRange(GetDefaultStations()); }
    }
    
    private static List<RadioStation> GetDefaultStations() => new()
    {
        // Internet Radio Stations
        new RadioStation { Name = "SomaFM - Groove Salad", Url = "https://ice1.somafm.com/groovesalad-128-mp3", Genre = "Ambient" },
        new RadioStation { Name = "SomaFM - Drone Zone", Url = "https://ice1.somafm.com/dronezone-128-mp3", Genre = "Ambient" },
        new RadioStation { Name = "SomaFM - DEF CON Radio", Url = "https://ice1.somafm.com/defcon-128-mp3", Genre = "Electronic" },
        new RadioStation { Name = "SomaFM - Space Station", Url = "https://ice1.somafm.com/spacestation-128-mp3", Genre = "Electronic" },
        new RadioStation { Name = "SomaFM - Lush", Url = "https://ice1.somafm.com/lush-128-mp3", Genre = "Electronic" },
        new RadioStation { Name = "SomaFM - Beat Blender", Url = "https://ice1.somafm.com/beatblender-128-mp3", Genre = "Electronic" },
        new RadioStation { Name = "KEXP 90.3 FM", Url = "https://kexp-mp3-128.streamguys1.com/kexp128.mp3", Genre = "Alternative" },
        new RadioStation { Name = "Jazz24", Url = "https://live.wostreaming.net/direct/ppm-jazz24mp3-ibc1", Genre = "Jazz" },
        new RadioStation { Name = "WFMU - Freeform Radio", Url = "https://stream0.wfmu.org/freeform-128k", Genre = "Eclectic" },
        new RadioStation { Name = "NTS Radio - Channel 1", Url = "https://stream-relay-geo.ntslive.net/stream", Genre = "Eclectic" },
        new RadioStation { Name = "NTS Radio - Channel 2", Url = "https://stream-relay-geo.ntslive.net/stream2", Genre = "Eclectic" },
        new RadioStation { Name = "Radio Paradise - Main Mix", Url = "https://stream.radioparadise.com/mp3-192", Genre = "Eclectic" },
        new RadioStation { Name = "Radio Paradise - Mellow Mix", Url = "https://stream.radioparadise.com/mellow-192", Genre = "Chill" },
        new RadioStation { Name = "Radio Paradise - Rock Mix", Url = "https://stream.radioparadise.com/rock-192", Genre = "Rock" },
        new RadioStation { Name = "Digitally Imported - Chillout", Url = "https://prem1.di.fm/chillout?type=mp3", Genre = "Chill" },
        new RadioStation { Name = "Digitally Imported - Trance", Url = "https://prem1.di.fm/trance?type=mp3", Genre = "Trance" },
        new RadioStation { Name = "BBC Radio 1", Url = "http://stream.live.vc.bbcmedia.co.uk/bbc_radio_one", Genre = "Pop" },
        new RadioStation { Name = "BBC Radio 6 Music", Url = "http://stream.live.vc.bbcmedia.co.uk/bbc_6music", Genre = "Alternative" },
        new RadioStation { Name = "Classic FM", Url = "https://media-ice.musicradio.com/ClassicFMMP3", Genre = "Classical" },
        new RadioStation { Name = "FIP (France Inter Paris)", Url = "https://icecast.radiofrance.fr/fip-hifi.aac", Genre = "Eclectic" },
        new RadioStation { Name = "FluxFM - Berlin", Url = "https://streams.fluxfm.de/live/mp3-320/audio/", Genre = "Alternative" },
        new RadioStation { Name = "Triple J (Australia)", Url = "https://live-radio01.mediahubaustralia.com/2TJW/mp3/", Genre = "Alternative" },
        
        // Streaming Service Placeholders (paste share URLs)
        new RadioStation { Name = "✨ Spotify — Paste share link", Url = "https://open.spotify.com/", Genre = "Streaming", RequiresAuth = true, CredentialKey = "streaming.spotify" },
        new RadioStation { Name = "✨ Apple Music — Paste share link", Url = "https://music.apple.com/", Genre = "Streaming", RequiresAuth = true, CredentialKey = "streaming.apple" },
        new RadioStation { Name = "✨ Amazon Music — Paste share link", Url = "https://music.amazon.com/", Genre = "Streaming", RequiresAuth = true, CredentialKey = "streaming.amazon" },
        new RadioStation { Name = "✨ Tidal — Paste share link", Url = "https://tidal.com/", Genre = "Streaming", RequiresAuth = true, CredentialKey = "streaming.tidal" },
        new RadioStation { Name = "✨ Deezer — Paste share link", Url = "https://www.deezer.com/", Genre = "Streaming", RequiresAuth = true, CredentialKey = "streaming.deezer" },
        new RadioStation { Name = "✨ Bandcamp — Paste share link", Url = "https://bandcamp.com/", Genre = "Streaming", RequiresAuth = false, CredentialKey = "streaming.bandcamp" },
    };
    
    // --- Helpers ---
    
    private static bool IsYouTubeUrl(string url) =>
        url.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase);
    
    private static bool IsSoundCloudUrl(string url) =>
        url.Contains("soundcloud.com/", StringComparison.OrdinalIgnoreCase);

    private static bool IsPaidStreamingServiceUrl(string url) =>
        IsSpotifyUrl(url) || IsAppleMusicUrl(url) || IsAmazonMusicUrl(url) ||
        IsTidalUrl(url) || IsDeezerUrl(url) || IsBandcampUrl(url);

    private static bool IsSpotifyUrl(string url) =>
        url.Contains("open.spotify.com/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("spotify.link/", StringComparison.OrdinalIgnoreCase);

    private static bool IsAppleMusicUrl(string url) =>
        url.Contains("music.apple.com/", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmazonMusicUrl(string url) =>
        url.Contains("music.amazon.com/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("amazon.com/music/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTidalUrl(string url) =>
        url.Contains("tidal.com/", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeezerUrl(string url) =>
        url.Contains("deezer.com/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("deezer.page.link/", StringComparison.OrdinalIgnoreCase);

    private static bool IsBandcampUrl(string url) =>
        url.Contains("bandcamp.com/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines the streaming service type from a URL for display and routing.
    /// </summary>
    public static string GetStreamingServiceName(string url)
    {
        if (IsYouTubeUrl(url)) return "YouTube";
        if (IsSoundCloudUrl(url)) return "SoundCloud";
        if (IsSpotifyUrl(url)) return "Spotify";
        if (IsAppleMusicUrl(url)) return "Apple Music";
        if (IsAmazonMusicUrl(url)) return "Amazon Music";
        if (IsTidalUrl(url)) return "Tidal";
        if (IsDeezerUrl(url)) return "Deezer";
        if (IsBandcampUrl(url)) return "Bandcamp";
        return "Radio";
    }
    
    /// <summary>
    /// Open a paid streaming service URL.
    /// Bandcamp: yt-dlp direct (native support).
    /// Spotify/Apple Music/Tidal/Amazon/Deezer: Resolve track title via oEmbed or page metadata,
    /// then find matching audio on YouTube via yt-dlp (DRM prevents direct extraction).
    /// </summary>
    private async Task<StreamPlaybackResult?> OpenPaidStreamAsync(string url, CancellationToken ct)
    {
        string serviceName = GetStreamingServiceName(url);
        StreamStatusChanged?.Invoke(this, $"Connecting to {serviceName}...");
        BufferProgressChanged?.Invoke(this, 10);
        
        // Bandcamp works natively with yt-dlp — no DRM
        if (IsBandcampUrl(url))
        {
            return await OpenYouTubeStreamAsync(url, ct);
        }
        
        // For DRM services (Spotify, Apple, Amazon, Tidal, Deezer):
        // Resolve the track title, then find it on YouTube
        return await OpenDrmServiceViaYouTubeAsync(url, serviceName, ct);
    }
    
    /// <summary>
    /// Resolves a DRM-protected streaming service link to a track title using oEmbed/metadata,
    /// then searches YouTube for a matching stream and plays it via yt-dlp.
    /// </summary>
    private async Task<StreamPlaybackResult?> OpenDrmServiceViaYouTubeAsync(string url, string serviceName, CancellationToken ct)
    {
        // Step 1: Resolve track title from the service
        StreamStatusChanged?.Invoke(this, $"Looking up track on {serviceName}...");
        BufferProgressChanged?.Invoke(this, 15);
        
        string? trackTitle = await ResolveTrackTitleAsync(url, serviceName, ct);
        if (string.IsNullOrEmpty(trackTitle))
        {
            StreamError?.Invoke(this, $"Could not identify the track from this {serviceName} URL. Make sure you paste a link to a specific track, not a playlist or homepage.");
            return null;
        }
        
        // Step 2: Ensure yt-dlp is available
        string ytdlp = await EnsureYtDlpAsync(ct);
        if (string.IsNullOrEmpty(ytdlp)) return null;
        
        // Step 3: Search YouTube for the track
        StreamStatusChanged?.Invoke(this, $"Finding \"{Truncate(trackTitle, 40)}\" on YouTube...");
        BufferProgressChanged?.Invoke(this, 30);
        
        try
        {
            // Get direct audio URL from YouTube search
            var psi = new ProcessStartInfo
            {
                FileName = ytdlp,
                Arguments = $"-f bestaudio --get-url --no-warnings \"ytsearch1:{EscapeShellArg(trackTitle)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) throw new Exception("Failed to start yt-dlp");
            
            string audioUrl = (await process.StandardOutput.ReadToEndAsync(ct)).Trim();
            string error = (await process.StandardError.ReadToEndAsync(ct)).Trim();
            await process.WaitForExitAsync(ct);
            
            if (process.ExitCode != 0 || string.IsNullOrEmpty(audioUrl))
            {
                StreamError?.Invoke(this, $"Could not find \"{Truncate(trackTitle, 50)}\" on YouTube. Try a different track.");
                return null;
            }
            
            BufferProgressChanged?.Invoke(this, 60);
            StreamStatusChanged?.Invoke(this, "Buffering audio...");
            
            _streamReader?.Dispose();
            _streamReader = await Task.Run(() => new MediaFoundationReader(audioUrl), ct);
            
            BufferProgressChanged?.Invoke(this, 100);
            StreamStatusChanged?.Invoke(this, $"Playing — {Truncate(trackTitle, 60)}");
            
            return new StreamPlaybackResult
            {
                WaveProvider = _streamReader,
                Title = trackTitle,
                Url = url,
                IsLiveStream = false
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            StreamError?.Invoke(this, $"Playback failed: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Resolve a track title from a streaming service URL using oEmbed APIs or page metadata.
    /// </summary>
    private async Task<string?> ResolveTrackTitleAsync(string url, string serviceName, CancellationToken ct)
    {
        try
        {
            // Try oEmbed first (Spotify and Apple Music support this)
            string? oEmbedEndpoint = null;
            if (IsSpotifyUrl(url))
                oEmbedEndpoint = $"https://open.spotify.com/oembed?url={Uri.EscapeDataString(url)}";
            else if (IsAppleMusicUrl(url))
                oEmbedEndpoint = $"https://music.apple.com/oembed?url={Uri.EscapeDataString(url)}";
            
            if (oEmbedEndpoint != null)
            {
                try
                {
                    string json = await _httpClient.GetStringAsync(oEmbedEndpoint, ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("title", out var titleProp))
                    {
                        return titleProp.GetString();
                    }
                }
                catch { /* Fall through to HTML title extraction */ }
            }
            
            // Fallback: Fetch the page and extract <title> tag
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode)
                {
                    // Read just the first chunk for the <title> tag
                    var content = await response.Content.ReadAsStringAsync(ct);
                    var titleMatch = System.Text.RegularExpressions.Regex.Match(
                        content, @"<title[^>]*>([^<]+)</title>", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    if (titleMatch.Success)
                    {
                        string title = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                        // Clean up common suffixes like " - Spotify", " | Tidal", " - Amazon Music"
                        title = CleanServiceSuffix(title, serviceName);
                        if (!string.IsNullOrEmpty(title) && title.Length > 3)
                            return title;
                    }
                }
            }
            catch { }
        }
        catch { }
        
        return null;
    }
    
    /// <summary>
    /// Remove service name suffixes from page titles (e.g., " - Spotify", " | Tidal").
    /// </summary>
    private static string CleanServiceSuffix(string title, string serviceName)
    {
        string[] separators = { " - ", " | ", " — ", " · " };
        foreach (var sep in separators)
        {
            int idx = title.LastIndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (idx > 5) // Keep at least some title
            {
                string suffix = title[(idx + sep.Length)..].Trim();
                if (suffix.Contains(serviceName, StringComparison.OrdinalIgnoreCase) ||
                    suffix.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ||
                    suffix.Contains("Apple Music", StringComparison.OrdinalIgnoreCase) ||
                    suffix.Contains("Amazon", StringComparison.OrdinalIgnoreCase) ||
                    suffix.Contains("Tidal", StringComparison.OrdinalIgnoreCase) ||
                    suffix.Contains("Deezer", StringComparison.OrdinalIgnoreCase))
                {
                    return title[..idx].Trim();
                }
            }
        }
        return title;
    }
    
    private static string EscapeShellArg(string arg) =>
        arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    
    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 1)] + "…";
    
    /// <summary>
    /// Get the credential key for a streaming service URL.
    /// </summary>
    private static string? GetCredentialKeyForUrl(string url)
    {
        if (IsSpotifyUrl(url)) return "streaming.spotify";
        if (IsAppleMusicUrl(url)) return "streaming.apple";
        if (IsAmazonMusicUrl(url)) return "streaming.amazon";
        if (IsTidalUrl(url)) return "streaming.tidal";
        if (IsDeezerUrl(url)) return "streaming.deezer";
        if (IsBandcampUrl(url)) return "streaming.bandcamp";
        return null;
    }
    
    private static string FindYtDlp()
    {
        // Check the app's Tools folder first (auto-installed location)
        var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "yt-dlp.exe");
        if (File.Exists(toolsPath)) return toolsPath;
        
        // Check common locations on PATH
        string[] candidates = { "yt-dlp", "yt-dlp.exe" };
        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(3000);
                    if (process.ExitCode == 0) return candidate;
                }
            }
            catch { }
        }
        return string.Empty;
    }
    
    /// <summary>
    /// Ensures yt-dlp is available, auto-downloading it if not found.
    /// Returns the path to yt-dlp, or empty string if download fails.
    /// </summary>
    private async Task<string> EnsureYtDlpAsync(CancellationToken ct = default)
    {
        string existing = FindYtDlp();
        if (!string.IsNullOrEmpty(existing)) return existing;
        
        // Auto-download yt-dlp
        StreamStatusChanged?.Invoke(this, "Installing yt-dlp (one-time download)...");
        
        try
        {
            var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsDir)) Directory.CreateDirectory(toolsDir);
            
            var destPath = Path.Combine(toolsDir, "yt-dlp.exe");
            
            // Download latest yt-dlp release
            const string ytdlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PlatypusTools/1.0");
            
            using var response = await client.GetAsync(ytdlpUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                StreamError?.Invoke(this, $"Failed to download yt-dlp (HTTP {(int)response.StatusCode}). Install manually from https://github.com/yt-dlp/yt-dlp");
                return string.Empty;
            }
            
            await using var fs = new FileStream(destPath, FileMode.Create);
            await response.Content.CopyToAsync(fs, ct);
            fs.Close();
            
            if (File.Exists(destPath))
            {
                StreamStatusChanged?.Invoke(this, "yt-dlp installed successfully.");
                return destPath;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            StreamError?.Invoke(this, $"Auto-install of yt-dlp failed: {ex.Message}");
        }
        
        return string.Empty;
    }
    
    private static string ExtractStationName(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch { return "Internet Radio"; }
    }
}

/// <summary>
/// Result of opening a stream for playback.
/// </summary>
public class StreamPlaybackResult
{
    public IWaveProvider WaveProvider { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsLiveStream { get; set; }
}

/// <summary>
/// Metadata received from a stream (e.g., current song on radio).
/// </summary>
public class StreamMetadata
{
    public string StationName { get; set; } = "";
    public string CurrentSong { get; set; } = "";
}

/// <summary>
/// Saved radio station entry.
/// </summary>
public class RadioStation
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Genre { get; set; } = "";
    public bool IsFavorite { get; set; }
    /// <summary>
    /// Whether this station requires authentication (e.g., paid streaming services).
    /// </summary>
    public bool RequiresAuth { get; set; }
    /// <summary>
    /// Credential key for CredentialManagerService lookup. E.g., "streaming.spotify".
    /// </summary>
    public string? CredentialKey { get; set; }
}
