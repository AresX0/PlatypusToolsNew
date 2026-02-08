using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using PlatypusTools.Core.Models.Audio;

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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _stationsPath = Path.Combine(appData, "PlatypusTools", "RadioStations.json");
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
        
        // Check if yt-dlp is available
        string ytdlp = FindYtDlp();
        if (string.IsNullOrEmpty(ytdlp))
        {
            StreamError?.Invoke(this, "yt-dlp not found. Install it from https://github.com/yt-dlp/yt-dlp");
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
        new RadioStation { Name = "SomaFM - Groove Salad", Url = "https://ice1.somafm.com/groovesalad-128-mp3", Genre = "Ambient" },
        new RadioStation { Name = "SomaFM - Drone Zone", Url = "https://ice1.somafm.com/dronezone-128-mp3", Genre = "Ambient" },
        new RadioStation { Name = "SomaFM - DEF CON Radio", Url = "https://ice1.somafm.com/defcon-128-mp3", Genre = "Electronic" },
        new RadioStation { Name = "SomaFM - Space Station", Url = "https://ice1.somafm.com/spacestation-128-mp3", Genre = "Electronic" },
        new RadioStation { Name = "KEXP 90.3 FM", Url = "https://kexp-mp3-128.streamguys1.com/kexp128.mp3", Genre = "Alternative" },
        new RadioStation { Name = "Jazz24", Url = "https://live.wostreaming.net/direct/ppm-jazz24mp3-ibc1", Genre = "Jazz" },
    };
    
    // --- Helpers ---
    
    private static bool IsYouTubeUrl(string url) =>
        url.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase);
    
    private static bool IsSoundCloudUrl(string url) =>
        url.Contains("soundcloud.com/", StringComparison.OrdinalIgnoreCase);
    
    private static string FindYtDlp()
    {
        // Check common locations
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
}
