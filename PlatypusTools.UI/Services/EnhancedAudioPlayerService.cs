using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Dsp;
using PlatypusTools.Core.Models.Audio;

namespace PlatypusTools.UI.Services;

/// <summary>
/// Enhanced Audio Player Service with NAudio backend for advanced features:
/// - 10-band EQ with real audio processing
/// - Gapless playback
/// - ReplayGain support
/// - Media key support
/// - Playback speed control
/// </summary>
public class EnhancedAudioPlayerService : IDisposable
{
    private static EnhancedAudioPlayerService? _instance;
    public static EnhancedAudioPlayerService Instance => _instance ??= new EnhancedAudioPlayerService();
    
    // NAudio components
    private IWavePlayer? _wavePlayer;
    private AudioFileReader? _audioFileReader;
    private EqualizerSampleProvider? _equalizer;
    private SpeedControlSampleProvider? _speedController;
    private VolumeSampleProvider? _volumeController;
    
    // Gapless playback - preload next track
    private AudioFileReader? _preloadedReader;
    private string? _preloadedFilePath;
    
    // Queue management
    private List<AudioTrack> _queue = new();
    private List<int> _shuffledIndices = new();
    private int _currentIndex = -1;
    private bool _isShuffled;
    private readonly Random _random = new();
    
    // Queue History - previously played tracks
    private readonly List<AudioTrack> _playHistory = new();
    private const int MAX_HISTORY_SIZE = 100;
    
    // A-B Loop
    private bool _isABLoopEnabled;
    private TimeSpan? _loopPointA;
    private TimeSpan? _loopPointB;
    
    // Fade on Pause
    private bool _fadeOnPause = true;
    private int _fadeOnPauseDurationMs = 500;
    private DispatcherTimer? _fadeTimer;
    
    // Audio Bookmarks - track position persistence  
    private readonly Dictionary<string, TimeSpan> _bookmarks = new();
    private static readonly string BookmarksFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlatypusTools", "audio_bookmarks.json");
    
    // Audio Output Device
    private int _selectedDeviceNumber = -1; // -1 = default device
    
    // Playback state
    private bool _isPlaying;
    private bool _isPaused;
    private double _volume = 0.7;
    private bool _isMuted;
    private double _playbackSpeed = 1.0;
    private bool _replayGainEnabled = true;
    private ReplayGainMode _replayGainMode = ReplayGainMode.Track;
    
    // 10-band EQ values (dB)
    private readonly float[] _eqBands = new float[10]; // 32Hz, 64Hz, 125Hz, 250Hz, 500Hz, 1kHz, 2kHz, 4kHz, 8kHz, 16kHz
    
    // Timers
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _spectrumTimer;
    
    // Spectrum analyzer - balanced FFT size for performance
    private const int FFT_SIZE = 1024; // Reduced for performance (was 4096)
    private const int SPECTRUM_BANDS = 64; // Reduced for performance (was 128)
    private readonly float[] _spectrumData = new float[SPECTRUM_BANDS];
    private readonly float[] _fftMagnitudes = new float[FFT_SIZE / 2];
    private readonly float[] _smoothedSpectrum = new float[SPECTRUM_BANDS]; // Smoothed output
    private volatile bool _fftInProgress = false; // Prevent overlapping FFT computations
    
    // Spectrum analyzer sample provider for capturing audio
    private SpectrumAnalyzerSampleProvider? _spectrumAnalyzer;
    
    // Shared HttpClient for lyrics downloads (reuse to avoid socket exhaustion)
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    
    static EnhancedAudioPlayerService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatypusTools/1.0");
    }
    
    // Events
    public event EventHandler<AudioTrack?>? TrackChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<float[]>? SpectrumDataUpdated;
    public event EventHandler<(float PeakLeft, float PeakRight, float RmsLeft, float RmsRight)>? VULevelsUpdated;
    public event EventHandler<float[]>? OscilloscopeDataUpdated;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler? TrackEnded;
    public event EventHandler<string>? PlaybackError;
    
    public enum RepeatMode { None, All, One }
    public enum ReplayGainMode { Off, Track, Album }
    
    // Properties
    public AudioTrack? CurrentTrack => _currentIndex >= 0 && _currentIndex < _queue.Count ? _queue[_currentIndex] : null;
    public TimeSpan Position => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _audioFileReader?.TotalTime ?? TimeSpan.Zero;
    
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            ApplyVolume();
        }
    }
    
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            ApplyVolume();
        }
    }
    
    public bool IsPlaying => _isPlaying && !_isPaused;
    public bool IsShuffle
    {
        get => _isShuffled;
        set
        {
            _isShuffled = value;
            if (value) ShuffleQueue();
        }
    }
    
    public RepeatMode Repeat { get; set; } = RepeatMode.None;
    public IReadOnlyList<AudioTrack> Queue => _queue.AsReadOnly();
    public int CurrentIndex => _currentIndex;
    
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = Math.Clamp(value, 0.25, 4.0);
            _speedController?.SetSpeed((float)_playbackSpeed);
        }
    }
    
    public bool ReplayGainEnabled
    {
        get => _replayGainEnabled;
        set => _replayGainEnabled = value;
    }
    
    public ReplayGainMode ReplayGainSetting
    {
        get => _replayGainMode;
        set => _replayGainMode = value;
    }
    
    #region A-B Loop Properties
    
    /// <summary>
    /// Gets or sets whether A-B loop is enabled.
    /// </summary>
    public bool IsABLoopEnabled
    {
        get => _isABLoopEnabled;
        set => _isABLoopEnabled = value;
    }
    
    /// <summary>
    /// Gets or sets the loop start point (A).
    /// </summary>
    public TimeSpan? LoopPointA
    {
        get => _loopPointA;
        set => _loopPointA = value;
    }
    
    /// <summary>
    /// Gets or sets the loop end point (B).
    /// </summary>
    public TimeSpan? LoopPointB
    {
        get => _loopPointB;
        set => _loopPointB = value;
    }
    
    /// <summary>
    /// Sets point A to current position.
    /// </summary>
    public void SetLoopPointA()
    {
        _loopPointA = Position;
        _isABLoopEnabled = _loopPointA.HasValue && _loopPointB.HasValue && _loopPointB > _loopPointA;
    }
    
    /// <summary>
    /// Sets point B to current position and enables loop.
    /// </summary>
    public void SetLoopPointB()
    {
        _loopPointB = Position;
        _isABLoopEnabled = _loopPointA.HasValue && _loopPointB.HasValue && _loopPointB > _loopPointA;
    }
    
    /// <summary>
    /// Clears A-B loop points.
    /// </summary>
    public void ClearABLoop()
    {
        _loopPointA = null;
        _loopPointB = null;
        _isABLoopEnabled = false;
    }
    
    #endregion
    
    #region Fade on Pause Properties
    
    /// <summary>
    /// Gets or sets whether to fade volume when pausing.
    /// </summary>
    public bool FadeOnPause
    {
        get => _fadeOnPause;
        set => _fadeOnPause = value;
    }
    
    /// <summary>
    /// Gets or sets the fade duration in milliseconds.
    /// </summary>
    public int FadeOnPauseDurationMs
    {
        get => _fadeOnPauseDurationMs;
        set => _fadeOnPauseDurationMs = Math.Clamp(value, 100, 2000);
    }
    
    #endregion
    
    #region Queue History Properties
    
    /// <summary>
    /// Gets the list of previously played tracks.
    /// </summary>
    public IReadOnlyList<AudioTrack> PlayHistory => _playHistory.AsReadOnly();
    
    /// <summary>
    /// Clears the play history.
    /// </summary>
    public void ClearHistory()
    {
        _playHistory.Clear();
    }
    
    #endregion
    
    #region Audio Bookmarks Properties
    
    /// <summary>
    /// Gets all saved bookmarks.
    /// </summary>
    public IReadOnlyDictionary<string, TimeSpan> Bookmarks => _bookmarks;
    
    /// <summary>
    /// Saves a bookmark for the current track at current position.
    /// </summary>
    public void SaveBookmark()
    {
        if (CurrentTrack == null) return;
        _bookmarks[CurrentTrack.FilePath] = Position;
        SaveBookmarksAsync();
    }
    
    /// <summary>
    /// Saves a bookmark for a specific track.
    /// </summary>
    public void SaveBookmark(string filePath, TimeSpan position)
    {
        _bookmarks[filePath] = position;
        SaveBookmarksAsync();
    }
    
    /// <summary>
    /// Gets a saved bookmark for the specified file, or null if none.
    /// </summary>
    public TimeSpan? GetBookmark(string filePath)
    {
        return _bookmarks.TryGetValue(filePath, out var pos) ? pos : null;
    }
    
    /// <summary>
    /// Removes a bookmark for the specified file.
    /// </summary>
    public void RemoveBookmark(string filePath)
    {
        _bookmarks.Remove(filePath);
        SaveBookmarksAsync();
    }
    
    /// <summary>
    /// Loads bookmarks from disk.
    /// </summary>
    private void LoadBookmarks()
    {
        try
        {
            if (File.Exists(BookmarksFilePath))
            {
                var json = File.ReadAllText(BookmarksFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        _bookmarks[kvp.Key] = TimeSpan.FromTicks(kvp.Value);
                    }
                }
            }
        }
        catch { }
    }
    
    /// <summary>
    /// Saves bookmarks to disk asynchronously.
    /// </summary>
    private async void SaveBookmarksAsync()
    {
        try
        {
            var data = _bookmarks.ToDictionary(k => k.Key, v => v.Value.Ticks);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var dir = Path.GetDirectoryName(BookmarksFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(BookmarksFilePath, json);
        }
        catch { }
    }
    
    #endregion
    
    #region Audio Output Device
    
    /// <summary>
    /// Gets available audio output devices.
    /// </summary>
    public static IEnumerable<(int DeviceNumber, string Name)> GetAudioOutputDevices()
    {
        for (int i = -1; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            yield return (i, i == -1 ? "Default Device" : caps.ProductName);
        }
    }
    
    /// <summary>
    /// Gets or sets the selected audio output device number (-1 = default).
    /// </summary>
    public int SelectedDeviceNumber
    {
        get => _selectedDeviceNumber;
        set
        {
            _selectedDeviceNumber = value;
            // Note: Changing device requires restarting playback
        }
    }
    
    #endregion
    
    private EnhancedAudioPlayerService()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _positionTimer.Tick += (s, e) => 
        {
            PositionChanged?.Invoke(this, Position);
            
            // A-B Loop: check if we've passed point B
            if (_isABLoopEnabled && _loopPointA.HasValue && _loopPointB.HasValue)
            {
                if (Position >= _loopPointB.Value)
                {
                    Seek(_loopPointA.Value);
                }
            }
        };
        
        _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) }; // ~22 FPS
        _spectrumTimer.Tick += (s, e) => GenerateSpectrum();
        _spectrumTimer.Start();
        
        // Load saved bookmarks
        LoadBookmarks();
        
        // Register for media keys
        RegisterMediaKeys();
    }
    
    #region 10-Band Equalizer
    
    public void SetEqBand(int bandIndex, float valueDb)
    {
        if (bandIndex >= 0 && bandIndex < 10)
        {
            _eqBands[bandIndex] = Math.Clamp(valueDb, -12f, 12f);
            _equalizer?.UpdateBand(bandIndex, _eqBands[bandIndex]);
        }
    }
    
    public float GetEqBand(int bandIndex)
    {
        return bandIndex >= 0 && bandIndex < 10 ? _eqBands[bandIndex] : 0f;
    }
    
    public void SetEqPreset(EQPreset preset)
    {
        var bands = preset.GetBands();
        for (int i = 0; i < Math.Min(bands.Length, 10); i++)
        {
            SetEqBand(i, (float)bands[i]);
        }
    }
    
    public void ResetEq()
    {
        for (int i = 0; i < 10; i++)
        {
            SetEqBand(i, 0f);
        }
    }
    
    #endregion
    
    #region Playback Control
    
    // Stream playback state
    private bool _isStreaming;
    private string _streamUrl = string.Empty;
    public bool IsStreaming => _isStreaming;
    public string StreamUrl => _streamUrl;
    
    /// <summary>
    /// Play an audio stream from a URL (internet radio, YouTube, SoundCloud, direct HTTP audio).
    /// Uses MediaFoundationReader for HTTP streams and yt-dlp for YouTube/SoundCloud.
    /// </summary>
    public async Task PlayStreamAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        
        try
        {
            Stop();
            
            var streamResult = await AudioStreamingService.Instance.OpenStreamAsync(url);
            if (streamResult == null) return;
            
            _isStreaming = true;
            _streamUrl = url;
            
            // Build pipeline from stream: WaveProvider -> SampleProvider -> Speed -> EQ -> Spectrum -> Volume
            var sampleProvider = streamResult.WaveProvider.ToSampleProvider();
            
            _speedController = new SpeedControlSampleProvider(sampleProvider);
            _speedController.SetSpeed((float)_playbackSpeed);
            
            _equalizer = new EqualizerSampleProvider(_speedController);
            for (int i = 0; i < 10; i++)
            {
                _equalizer.UpdateBand(i, _eqBands[i]);
            }
            
            _spectrumAnalyzer = new SpectrumAnalyzerSampleProvider(_equalizer, FFT_SIZE);
            
            _volumeController = new VolumeSampleProvider(_spectrumAnalyzer);
            ApplyVolume();
            
            var waveOut = new WaveOutEvent();
            if (_selectedDeviceNumber >= 0 && _selectedDeviceNumber < WaveOut.DeviceCount)
            {
                waveOut.DeviceNumber = _selectedDeviceNumber;
            }
            _wavePlayer = waveOut;
            _wavePlayer.PlaybackStopped += OnPlaybackStopped;
            _wavePlayer.Init(_volumeController);
            _wavePlayer.Play();
            
            _isPlaying = true;
            _isPaused = false;
            _positionTimer.Start();
            
            // Create a virtual track for display
            var streamTrack = new AudioTrack
            {
                Title = streamResult.Title,
                Artist = streamResult.IsLiveStream ? "Live Stream" : "Stream",
                Album = streamResult.Url,
                FilePath = url
            };
            
            TrackChanged?.Invoke(this, streamTrack);
            PlaybackStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            PlaybackError?.Invoke(this, $"Stream error: {ex.Message}");
            _isStreaming = false;
        }
    }
    
    public async Task PlayTrackAsync(AudioTrack track)
    {
        if (track == null) return;
        
        var index = _queue.FindIndex(t => t.Id == track.Id || t.FilePath == track.FilePath);
        if (index < 0)
        {
            _queue.Add(track);
            index = _queue.Count - 1;
        }
        
        _currentIndex = index;
        await PlayCurrentAsync();
    }
    
    public async Task PlayCurrentAsync()
    {
        if (CurrentTrack == null || !File.Exists(CurrentTrack.FilePath))
        {
            PlaybackError?.Invoke(this, "Track file not found");
            return;
        }
        
        try
        {
            // Add current track to history before switching
            if (_currentIndex >= 0 && _currentIndex < _queue.Count)
            {
                var previousTrack = _queue[_currentIndex];
                if (!_playHistory.Contains(previousTrack))
                {
                    _playHistory.Insert(0, previousTrack);
                    if (_playHistory.Count > MAX_HISTORY_SIZE)
                        _playHistory.RemoveAt(_playHistory.Count - 1);
                }
            }
            
            // Check if we have a preloaded track ready (gapless transition)
            bool usingPreload = _preloadedReader != null && _preloadedFilePath == CurrentTrack.FilePath;
            
            // Stop current playback, but preserve preload if we're using it
            Stop(preservePreload: usingPreload);
            
            if (usingPreload)
            {
                _audioFileReader = _preloadedReader;
                _preloadedReader = null;
                _preloadedFilePath = null;
            }
            else
            {
                _audioFileReader = new AudioFileReader(CurrentTrack.FilePath);
            }
            
            // Check for saved bookmark and resume from it if available
            var bookmark = GetBookmark(CurrentTrack.FilePath);
            if (bookmark.HasValue && bookmark.Value.TotalSeconds > 5 && _audioFileReader != null)
            {
                // Resume from bookmark (only if more than 5 seconds in)
                _audioFileReader.CurrentTime = bookmark.Value;
            }
            
            // Apply ReplayGain if enabled
            ApplyReplayGain();
            
            // Build audio pipeline: Reader -> Speed -> EQ -> Spectrum Analyzer -> Volume -> Output
            _speedController = new SpeedControlSampleProvider(_audioFileReader.ToSampleProvider());
            _speedController.SetSpeed((float)_playbackSpeed);
            
            _equalizer = new EqualizerSampleProvider(_speedController);
            for (int i = 0; i < 10; i++)
            {
                _equalizer.UpdateBand(i, _eqBands[i]);
            }
            
            // Add spectrum analyzer to capture audio samples for FFT
            _spectrumAnalyzer = new SpectrumAnalyzerSampleProvider(_equalizer, FFT_SIZE);
            
            _volumeController = new VolumeSampleProvider(_spectrumAnalyzer);
            ApplyVolume();
            
            // Create WaveOut with selected device
            var waveOut = new WaveOutEvent();
            if (_selectedDeviceNumber >= 0 && _selectedDeviceNumber < WaveOut.DeviceCount)
            {
                waveOut.DeviceNumber = _selectedDeviceNumber;
            }
            _wavePlayer = waveOut;
            _wavePlayer.PlaybackStopped += OnPlaybackStopped;
            _wavePlayer.Init(_volumeController);
            _wavePlayer.Play();
            
            _isPlaying = true;
            _isPaused = false;
            _positionTimer.Start();
            
            CurrentTrack.PlayCount++;
            CurrentTrack.LastPlayed = DateTime.Now;
            
            TrackChanged?.Invoke(this, CurrentTrack);
            PlaybackStateChanged?.Invoke(this, true);
            DurationChanged?.Invoke(this, Duration);
            
            // Preload next track for gapless playback
            await PreloadNextTrackAsync();
        }
        catch (Exception ex)
        {
            PlaybackError?.Invoke(this, ex.Message);
        }
    }
    
    public void Play()
    {
        if (_wavePlayer != null && _isPaused)
        {
            _wavePlayer.Play();
            _isPaused = false;
            _positionTimer.Start();
            PlaybackStateChanged?.Invoke(this, true);
        }
    }
    
    public void Pause()
    {
        if (_wavePlayer != null && _isPlaying)
        {
            if (_fadeOnPause && _volumeController != null)
            {
                // Fade out before pausing
                PauseWithFade();
            }
            else
            {
                // Immediate pause
                _wavePlayer.Pause();
                _isPaused = true;
                _positionTimer.Stop();
                PlaybackStateChanged?.Invoke(this, false);
            }
        }
    }
    
    /// <summary>
    /// Pauses playback with a smooth volume fade.
    /// </summary>
    private void PauseWithFade()
    {
        if (_volumeController == null || _wavePlayer == null) return;
        
        var originalVolume = _volumeController.Volume;
        var fadeSteps = 20;
        var stepDuration = _fadeOnPauseDurationMs / fadeSteps;
        var volumeStep = originalVolume / fadeSteps;
        var currentStep = 0;
        
        _fadeTimer?.Stop();
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepDuration) };
        _fadeTimer.Tick += (s, e) =>
        {
            currentStep++;
            if (currentStep >= fadeSteps)
            {
                _fadeTimer.Stop();
                _wavePlayer?.Pause();
                _isPaused = true;
                _positionTimer.Stop();
                // Restore volume for next play
                if (_volumeController != null)
                    _volumeController.Volume = originalVolume;
                PlaybackStateChanged?.Invoke(this, false);
            }
            else
            {
                if (_volumeController != null)
                    _volumeController.Volume = originalVolume - (volumeStep * currentStep);
            }
        };
        _fadeTimer.Start();
    }
    
    public void PlayPause()
    {
        if (_isPaused) Play();
        else if (_isPlaying) Pause();
        else if (CurrentTrack != null) _ = PlayCurrentAsync();
    }
    
    public void Stop(bool preservePreload = false)
    {
        _positionTimer.Stop();
        _isPlaying = false;
        _isPaused = false;
        
        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }
        
        _audioFileReader?.Dispose();
        _audioFileReader = null;
        
        _equalizer = null;
        _speedController = null;
        _spectrumAnalyzer = null;
        _volumeController = null;
        
        // Clear preload unless we're doing gapless transition
        if (!preservePreload)
        {
            _preloadedReader?.Dispose();
            _preloadedReader = null;
            _preloadedFilePath = null;
        }
        
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    public void Seek(TimeSpan position)
    {
        if (_audioFileReader != null)
        {
            _audioFileReader.CurrentTime = position;
        }
    }
    
    /// <summary>
    /// Generates low-resolution waveform data for seek preview display.
    /// Returns an array of peak values (0-1) for the given number of samples.
    /// </summary>
    public float[]? GetWaveformData(int sampleCount = 140)
    {
        var track = CurrentTrack;
        if (track?.FilePath == null || !System.IO.File.Exists(track.FilePath))
            return null;
            
        try
        {
            using var reader = new AudioFileReader(track.FilePath);
            long totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
            long samplesPerBucket = totalSamples / sampleCount;
            if (samplesPerBucket <= 0) return null;
            
            var waveform = new float[sampleCount];
            var buffer = new float[Math.Min(4096, samplesPerBucket)];
            
            for (int i = 0; i < sampleCount; i++)
            {
                long targetPos = i * samplesPerBucket * (reader.WaveFormat.BitsPerSample / 8);
                if (targetPos >= reader.Length) break;
                reader.Position = targetPos;
                
                int read = reader.Read(buffer, 0, buffer.Length);
                float peak = 0;
                for (int j = 0; j < read; j++)
                    peak = Math.Max(peak, Math.Abs(buffer[j]));
                waveform[i] = peak;
            }
            
            return waveform;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task NextAsync()
    {
        var nextIndex = GetNextIndex();
        if (nextIndex >= 0)
        {
            _currentIndex = nextIndex;
            await PlayCurrentAsync();
        }
        else
        {
            Stop();
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public async Task PreviousAsync()
    {
        // If more than 3 seconds in, restart track
        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }
        
        var prevIndex = GetPreviousIndex();
        if (prevIndex >= 0)
        {
            _currentIndex = prevIndex;
            await PlayCurrentAsync();
        }
    }
    
    #endregion
    
    #region Queue Management
    
    public void ClearQueue()
    {
        Stop();
        _queue.Clear();
        _shuffledIndices.Clear();
        _currentIndex = -1;
        TrackChanged?.Invoke(this, null);
    }
    
    public void AddToQueue(AudioTrack track)
    {
        _queue.Add(track);
        if (_isShuffled)
        {
            _shuffledIndices.Add(_queue.Count - 1);
        }
    }
    
    public void AddToQueue(IEnumerable<AudioTrack> tracks)
    {
        foreach (var track in tracks)
        {
            AddToQueue(track);
        }
    }
    
    /// <summary>
    /// Sets the queue to the specified tracks, replacing any existing queue.
    /// </summary>
    public void SetQueue(List<AudioTrack> tracks)
    {
        _queue.Clear();
        _shuffledIndices.Clear();
        _currentIndex = -1;
        
        if (tracks != null && tracks.Count > 0)
        {
            _queue.AddRange(tracks);
            if (_isShuffled)
            {
                ShuffleQueue();
            }
        }
    }
    
    /// <summary>
    /// Loads an audio track from a file path, reading metadata.
    /// </summary>
    public async Task<AudioTrack?> LoadTrackAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        
        var track = new AudioTrack
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath)
        };
        
        await Task.Run(() =>
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(track.FilePath);
                
                // Try to parse "Artist - Title" format
                var separatorIndex = fileName.IndexOf(" - ");
                if (separatorIndex > 0)
                {
                    track.Artist = fileName.Substring(0, separatorIndex).Trim();
                    track.Title = fileName.Substring(separatorIndex + 3).Trim();
                }
                else
                {
                    track.Title = fileName;
                }
                
                // Get folder as album name
                var parentFolder = Path.GetDirectoryName(track.FilePath);
                if (!string.IsNullOrEmpty(parentFolder))
                {
                    track.Album = Path.GetFileName(parentFolder);
                }
                
                track.FileSize = new FileInfo(track.FilePath).Length;
                
                // Try to read duration using NAudio
                try
                {
                    using var reader = new AudioFileReader(track.FilePath);
                    track.Duration = reader.TotalTime;
                }
                catch { }
            }
            catch
            {
                track.Title = Path.GetFileNameWithoutExtension(track.FilePath);
            }
        });
        
        return track;
    }
    
    public void RemoveFromQueue(AudioTrack track)
    {
        var index = _queue.IndexOf(track);
        if (index >= 0)
        {
            _queue.RemoveAt(index);
            if (index < _currentIndex) _currentIndex--;
            else if (index == _currentIndex) _currentIndex = Math.Min(_currentIndex, _queue.Count - 1);
            
            if (_isShuffled)
            {
                _shuffledIndices.Remove(index);
                for (int i = 0; i < _shuffledIndices.Count; i++)
                {
                    if (_shuffledIndices[i] > index)
                        _shuffledIndices[i]--;
                }
            }
        }
    }
    
    public void MoveInQueue(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _queue.Count || toIndex < 0 || toIndex >= _queue.Count)
            return;
        
        var track = _queue[fromIndex];
        _queue.RemoveAt(fromIndex);
        _queue.Insert(toIndex, track);
        
        // Adjust current index
        if (_currentIndex == fromIndex)
            _currentIndex = toIndex;
        else if (fromIndex < _currentIndex && toIndex >= _currentIndex)
            _currentIndex--;
        else if (fromIndex > _currentIndex && toIndex <= _currentIndex)
            _currentIndex++;
    }
    
    private void ShuffleQueue()
    {
        _shuffledIndices = Enumerable.Range(0, _queue.Count).OrderBy(_ => _random.Next()).ToList();
        
        // Move current track to front of shuffle
        if (_currentIndex >= 0)
        {
            _shuffledIndices.Remove(_currentIndex);
            _shuffledIndices.Insert(0, _currentIndex);
        }
    }
    
    private int GetNextIndex()
    {
        if (_queue.Count == 0) return -1;
        
        if (Repeat == RepeatMode.One)
            return _currentIndex;
        
        if (_isShuffled && _shuffledIndices.Count > 0)
        {
            var shuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (shuffleIndex < _shuffledIndices.Count - 1)
                return _shuffledIndices[shuffleIndex + 1];
            else if (Repeat == RepeatMode.All)
                return _shuffledIndices[0];
        }
        else
        {
            if (_currentIndex < _queue.Count - 1)
                return _currentIndex + 1;
            else if (Repeat == RepeatMode.All)
                return 0;
        }
        
        return -1;
    }
    
    private int GetPreviousIndex()
    {
        if (_queue.Count == 0) return -1;
        
        if (_isShuffled && _shuffledIndices.Count > 0)
        {
            var shuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (shuffleIndex > 0)
                return _shuffledIndices[shuffleIndex - 1];
            else if (Repeat == RepeatMode.All)
                return _shuffledIndices[_shuffledIndices.Count - 1];
        }
        else
        {
            if (_currentIndex > 0)
                return _currentIndex - 1;
            else if (Repeat == RepeatMode.All)
                return _queue.Count - 1;
        }
        
        return -1;
    }
    
    #endregion
    
    #region Gapless Playback
    
    private async Task PreloadNextTrackAsync()
    {
        var nextIndex = GetNextIndex();
        if (nextIndex < 0 || nextIndex >= _queue.Count) return;
        
        var nextTrack = _queue[nextIndex];
        if (nextTrack == null || !File.Exists(nextTrack.FilePath)) return;
        
        try
        {
            await Task.Run(() =>
            {
                _preloadedReader?.Dispose();
                _preloadedReader = new AudioFileReader(nextTrack.FilePath);
                _preloadedFilePath = nextTrack.FilePath;
            });
        }
        catch
        {
            _preloadedReader = null;
            _preloadedFilePath = null;
        }
    }
    
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            PlaybackError?.Invoke(this, e.Exception.Message);
            return;
        }
        
        // Check if track ended naturally
        if (_audioFileReader != null && _audioFileReader.CurrentTime >= _audioFileReader.TotalTime - TimeSpan.FromMilliseconds(100))
        {
            // Gapless transition to next track
            Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                await NextAsync();
            });
        }
    }
    
    #endregion
    
    #region ReplayGain
    
    private void ApplyReplayGain()
    {
        if (!_replayGainEnabled || CurrentTrack == null || _audioFileReader == null)
            return;
        
        double gain = 0;
        
        switch (_replayGainMode)
        {
            case ReplayGainMode.Track:
                gain = CurrentTrack.TrackGain;
                break;
            case ReplayGainMode.Album:
                gain = CurrentTrack.AlbumGain != 0 ? CurrentTrack.AlbumGain : CurrentTrack.TrackGain;
                break;
        }
        
        if (gain != 0)
        {
            // Convert dB to linear scale and apply to volume
            double linearGain = Math.Pow(10, gain / 20.0);
            _audioFileReader.Volume = (float)Math.Min(linearGain, 1.5); // Cap at 150% to prevent clipping
        }
    }
    
    #endregion
    
    #region Volume Control
    
    private void ApplyVolume()
    {
        if (_volumeController != null)
        {
            _volumeController.Volume = _isMuted ? 0 : (float)_volume;
        }
    }
    
    #endregion
    
    #region Spectrum Analysis
    
    private void GenerateSpectrum()
    {
        // Skip if FFT is already in progress (prevents UI thread blocking)
        if (_fftInProgress)
            return;
            
        // When not playing, output silent spectrum (visualizer will stop)
        if (!_isPlaying || _isPaused)
        {
            // Clear spectrum data when not playing
            for (int i = 0; i < _spectrumData.Length; i++)
            {
                _spectrumData[i] = 0f;
            }
            SpectrumDataUpdated?.Invoke(this, _spectrumData);
            return;
        }
        
        // Get real FFT data from the spectrum analyzer if available
        if (_spectrumAnalyzer != null)
        {
            try
            {
                _fftInProgress = true;
                var fftData = _spectrumAnalyzer.GetFFTData();
                if (fftData != null && fftData.Length > 0)
                {
                    ComputeSpectrumFromFFT(fftData);
                    SpectrumDataUpdated?.Invoke(this, _spectrumData);
                }
                
                // Fire VU levels event
                var vuLevels = _spectrumAnalyzer.GetVULevels();
                VULevelsUpdated?.Invoke(this, vuLevels);
                
                // Fire oscilloscope event
                var oscilloscopeData = _spectrumAnalyzer.GetOscilloscopeData();
                OscilloscopeDataUpdated?.Invoke(this, oscilloscopeData);
                
                return;
            }
            finally
            {
                _fftInProgress = false;
            }
        }
        
        // Fallback: no data
        for (int i = 0; i < _spectrumData.Length; i++)
        {
            _spectrumData[i] = 0f;
        }
        SpectrumDataUpdated?.Invoke(this, _spectrumData);
    }
    
    /// <summary>
    /// Compute spectrum bands from FFT magnitude data using logarithmic frequency mapping.
    /// Based on pro-grade spectrum analyzer implementation.
    /// </summary>
    private void ComputeSpectrumFromFFT(float[] fftData)
    {
        int fftLength = fftData.Length;
        int numBands = _spectrumData.Length;
        
        for (int i = 0; i < numBands; i++)
        {
            // Logarithmic frequency mapping: more resolution for bass frequencies
            // Maps lower bands to lower frequencies with higher resolution
            double logIndex = Math.Pow((double)i / numBands, 2) * (fftLength - 1);
            int index = Math.Min((int)logIndex, fftLength - 1);
            
            // Average nearby bins for smoother result
            float sum = 0;
            int count = 0;
            int range = Math.Max(1, fftLength / (numBands * 2));
            for (int j = Math.Max(0, index - range); j <= Math.Min(fftLength - 1, index + range); j++)
            {
                sum += fftData[j];
                count++;
            }
            float magnitude = count > 0 ? sum / count : fftData[index];
            
            // Convert to dB scale for better visualization
            float db = 20f * MathF.Log10(magnitude + 1e-6f);
            
            // Normalize: db typically ranges from -60 to 0
            // Map to 0-1 range
            float targetHeight = MathF.Max(0, (db + 60f) / 60f);
            
            // Smooth rise/fall for fluid animation
            if (targetHeight > _smoothedSpectrum[i])
            {
                // Fast attack (instant rise like reference code)
                _smoothedSpectrum[i] = targetHeight;
            }
            else
            {
                // Smooth decay
                _smoothedSpectrum[i] = _smoothedSpectrum[i] * 0.85f + targetHeight * 0.15f;
            }
            
            _spectrumData[i] = _smoothedSpectrum[i];
        }
    }
    
    #endregion
    
    #region Media Keys
    
    private IntPtr _hwndSource;
    
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
    private const int HOTKEY_PLAY_PAUSE = 1;
    private const int HOTKEY_NEXT = 2;
    private const int HOTKEY_PREV = 3;
    private const int HOTKEY_STOP = 4;
    
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_STOP = 0xB2;
    
    private void RegisterMediaKeys()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null) return;
            
            var helper = new WindowInteropHelper(mainWindow);
            _hwndSource = helper.Handle;
            
            if (_hwndSource != IntPtr.Zero)
            {
                RegisterHotKey(_hwndSource, HOTKEY_PLAY_PAUSE, 0, VK_MEDIA_PLAY_PAUSE);
                RegisterHotKey(_hwndSource, HOTKEY_NEXT, 0, VK_MEDIA_NEXT_TRACK);
                RegisterHotKey(_hwndSource, HOTKEY_PREV, 0, VK_MEDIA_PREV_TRACK);
                RegisterHotKey(_hwndSource, HOTKEY_STOP, 0, VK_MEDIA_STOP);
                
                HwndSource.FromHwnd(_hwndSource)?.AddHook(WndProc);
            }
        });
    }
    
    private void UnregisterMediaKeys()
    {
        if (_hwndSource != IntPtr.Zero)
        {
            UnregisterHotKey(_hwndSource, HOTKEY_PLAY_PAUSE);
            UnregisterHotKey(_hwndSource, HOTKEY_NEXT);
            UnregisterHotKey(_hwndSource, HOTKEY_PREV);
            UnregisterHotKey(_hwndSource, HOTKEY_STOP);
        }
    }
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_PLAY_PAUSE:
                    PlayPause();
                    handled = true;
                    break;
                case HOTKEY_NEXT:
                    _ = NextAsync();
                    handled = true;
                    break;
                case HOTKEY_PREV:
                    _ = PreviousAsync();
                    handled = true;
                    break;
                case HOTKEY_STOP:
                    Stop();
                    handled = true;
                    break;
            }
        }
        
        return IntPtr.Zero;
    }
    
    #endregion
    
    #region LRC Lyrics Parsing
    
    /// <summary>
    /// Synchronously checks for and loads a local .lrc file. Use this for instant lyrics display.
    /// Returns null if no local .lrc file exists (does NOT download).
    /// </summary>
    public Lyrics? LoadLocalLyricsSync(AudioTrack track)
    {
        if (track == null) return null;
        
        try
        {
            var directory = Path.GetDirectoryName(track.FilePath);
            var baseName = Path.GetFileNameWithoutExtension(track.FilePath);
            
            if (string.IsNullOrEmpty(directory)) return null;
            
            // Check primary location
            var lrcPath = Path.Combine(directory, baseName + ".lrc");
            if (!File.Exists(lrcPath))
            {
                // Try alternate naming
                lrcPath = Path.Combine(directory, $"{track.Artist} - {track.Title}.lrc");
            }
            
            if (File.Exists(lrcPath))
            {
                return ParseLrcFileSync(lrcPath, track.Id);
            }
        }
        catch { }
        
        return null;
    }
    
    /// <summary>
    /// Synchronously parses an LRC file.
    /// </summary>
    private Lyrics? ParseLrcFileSync(string path, string trackId)
    {
        try
        {
            var content = File.ReadAllText(path);
            var lyrics = ParseLrcContent(content, trackId);
            lyrics.Source = Path.GetFileName(path);
            return lyrics;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<Lyrics?> LoadLyricsAsync(AudioTrack track)
    {
        if (track == null) return null;
        
        // Try to find .lrc file
        var directory = Path.GetDirectoryName(track.FilePath);
        var baseName = Path.GetFileNameWithoutExtension(track.FilePath);
        
        if (string.IsNullOrEmpty(directory)) return null;
        
        var lrcPath = Path.Combine(directory, baseName + ".lrc");
        if (!File.Exists(lrcPath))
        {
            // Try alternate naming
            lrcPath = Path.Combine(directory, $"{track.Artist} - {track.Title}.lrc");
        }
        
        if (File.Exists(lrcPath))
        {
            return await ParseLrcFileAsync(lrcPath, track.Id);
        }
        
        // Try to auto-download lyrics if none found locally
        var downloadedLyrics = await TryDownloadLyricsAsync(track);
        if (downloadedLyrics != null)
        {
            // Save the downloaded lyrics for future use
            try
            {
                var savePath = Path.Combine(directory, baseName + ".lrc");
                await SaveLyricsToFileAsync(downloadedLyrics, savePath);
            }
            catch { /* Ignore save errors */ }
            
            return downloadedLyrics;
        }
        
        // Check for embedded lyrics (would need TagLib# integration)
        return null;
    }
    
    /// <summary>
    /// Attempts to download lyrics from online sources (LRCLIB API)
    /// </summary>
    private async Task<Lyrics?> TryDownloadLyricsAsync(AudioTrack track)
    {
        if (string.IsNullOrEmpty(track.Artist) || string.IsNullOrEmpty(track.Title))
            return null;
        
        try
        {
            // Use LRCLIB.net API - a free lyrics API
            var artist = Uri.EscapeDataString(track.Artist.Trim());
            var title = Uri.EscapeDataString(track.Title.Trim());
            var url = $"https://lrclib.net/api/get?artist_name={artist}&track_name={title}";
            
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;
            
            var json = await response.Content.ReadAsStringAsync();
            
            // Parse the JSON response
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Prefer synced lyrics
            if (root.TryGetProperty("syncedLyrics", out var syncedLyricsElement) && 
                syncedLyricsElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var lrcContent = syncedLyricsElement.GetString();
                if (!string.IsNullOrEmpty(lrcContent))
                {
                    var lyrics = ParseLrcContent(lrcContent, track.Id);
                    lyrics.Source = "LRCLIB.net (auto-downloaded)";
                    System.Diagnostics.Debug.WriteLine($"Downloaded synced lyrics for: {track.Artist} - {track.Title}");
                    return lyrics;
                }
            }
            
            // Fall back to plain lyrics
            if (root.TryGetProperty("plainLyrics", out var plainLyricsElement) && 
                plainLyricsElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var plainLyrics = plainLyricsElement.GetString();
                if (!string.IsNullOrEmpty(plainLyrics))
                {
                    var lyrics = new Lyrics
                    {
                        TrackId = track.Id,
                        Type = LyricsType.Unsynchronized,
                        Source = "LRCLIB.net (auto-downloaded, unsynced)"
                    };
                    
                    // Split into lines
                    var lines = plainLyrics.Split('\n');
                    var timestamp = TimeSpan.Zero;
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            lyrics.Lines.Add(new LyricLine(timestamp, line.Trim()));
                            timestamp = timestamp.Add(TimeSpan.FromSeconds(3)); // Approximate timing
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Downloaded plain lyrics for: {track.Artist} - {track.Title}");
                    return lyrics;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to download lyrics: {ex.Message}");
        }
        
        return null;
    }
    
    private Lyrics ParseLrcContent(string lrcContent, string trackId)
    {
        var lyrics = new Lyrics
        {
            TrackId = trackId,
            Type = LyricsType.LineSynced,
            Source = "Downloaded"
        };
        
        var lines = lrcContent.Split('\n');
        var timeRegex = new Regex(@"\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\]");
        
        foreach (var line in lines)
        {
            var matches = timeRegex.Matches(line);
            if (matches.Count > 0)
            {
                var text = timeRegex.Replace(line, "").Trim();
                if (string.IsNullOrEmpty(text)) continue;
                
                foreach (Match match in matches)
                {
                    int minutes = int.Parse(match.Groups[1].Value);
                    int seconds = int.Parse(match.Groups[2].Value);
                    int milliseconds = match.Groups[3].Success 
                        ? int.Parse(match.Groups[3].Value.PadRight(3, '0')) 
                        : 0;
                    
                    var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                    lyrics.Lines.Add(new LyricLine(timestamp, text));
                }
            }
        }
        
        lyrics.Lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return lyrics;
    }
    
    private async Task SaveLyricsToFileAsync(Lyrics lyrics, string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[re:PlatypusTools]");
        sb.AppendLine("[by:Auto-downloaded from LRCLIB.net]");
        sb.AppendLine();
        
        foreach (var line in lyrics.Lines)
        {
            var ts = line.Timestamp;
            sb.AppendLine($"[{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}]{line.Text}");
        }
        
        await File.WriteAllTextAsync(path, sb.ToString());
    }
    
    private async Task<Lyrics> ParseLrcFileAsync(string path, string trackId)
    {
        var lyrics = new Lyrics
        {
            TrackId = trackId,
            Type = LyricsType.LineSynced,
            Source = "LRC File"
        };
        
        var lines = await File.ReadAllLinesAsync(path);
        var timeRegex = new Regex(@"\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\]");
        
        foreach (var line in lines)
        {
            var matches = timeRegex.Matches(line);
            if (matches.Count > 0)
            {
                var text = timeRegex.Replace(line, "").Trim();
                if (string.IsNullOrEmpty(text)) continue;
                
                foreach (Match match in matches)
                {
                    int minutes = int.Parse(match.Groups[1].Value);
                    int seconds = int.Parse(match.Groups[2].Value);
                    int milliseconds = match.Groups[3].Success 
                        ? int.Parse(match.Groups[3].Value.PadRight(3, '0')) 
                        : 0;
                    
                    var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                    lyrics.Lines.Add(new LyricLine(timestamp, text));
                }
            }
        }
        
        lyrics.Lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return lyrics;
    }
    
    public LyricLine? GetCurrentLyricLine(Lyrics lyrics, TimeSpan position)
    {
        if (lyrics == null || lyrics.Lines.Count == 0) return null;
        
        // Find the last line that has passed
        for (int i = lyrics.Lines.Count - 1; i >= 0; i--)
        {
            if (lyrics.Lines[i].Timestamp <= position)
                return lyrics.Lines[i];
        }
        
        return null;
    }
    
    #endregion
    
    #region Last.fm Scrobbling
    
    private string? _lastFmApiKey;
    private string? _lastFmApiSecret;
    private string? _lastFmSessionKey;
    private DateTime? _trackStartTime;
    private bool _scrobbled;
    
    private const string LastFmApiUrl = "https://ws.audioscrobbler.com/2.0/";
    
    public void SetLastFmCredentials(string apiKey, string apiSecret, string sessionKey)
    {
        _lastFmApiKey = apiKey;
        _lastFmApiSecret = apiSecret;
        _lastFmSessionKey = sessionKey;
    }
    
    public bool IsLastFmConfigured => !string.IsNullOrEmpty(_lastFmApiKey) && !string.IsNullOrEmpty(_lastFmSessionKey);
    
    /// <summary>
    /// Generate the Last.fm API method signature (md5 hash of sorted params + secret).
    /// </summary>
    private string GenerateLastFmSignature(SortedDictionary<string, string> parameters)
    {
        var sigString = string.Concat(parameters.Select(p => p.Key + p.Value)) + _lastFmApiSecret;
        var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(sigString));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
    
    /// <summary>
    /// Make an authenticated POST to the Last.fm API.
    /// </summary>
    private async Task<bool> LastFmApiCallAsync(string method, Dictionary<string, string> extraParams)
    {
        if (string.IsNullOrEmpty(_lastFmApiKey) || string.IsNullOrEmpty(_lastFmSessionKey) || string.IsNullOrEmpty(_lastFmApiSecret))
            return false;
        
        try
        {
            var parameters = new SortedDictionary<string, string>(extraParams)
            {
                ["method"] = method,
                ["api_key"] = _lastFmApiKey,
                ["sk"] = _lastFmSessionKey
            };
            
            parameters["api_sig"] = GenerateLastFmSignature(parameters);
            parameters["format"] = "json";
            
            using var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync(LastFmApiUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Last.fm] {method} succeeded");
                return true;
            }
            
            var body = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[Last.fm] {method} failed ({response.StatusCode}): {body}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Last.fm] {method} error: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Authenticate with Last.fm using an auth token to get a session key.
    /// Call this after the user authorizes the app at https://www.last.fm/api/auth/?api_key=KEY&token=TOKEN
    /// </summary>
    public async Task<string?> GetLastFmSessionAsync(string token)
    {
        if (string.IsNullOrEmpty(_lastFmApiKey) || string.IsNullOrEmpty(_lastFmApiSecret))
            return null;
        
        try
        {
            var parameters = new SortedDictionary<string, string>
            {
                ["method"] = "auth.getSession",
                ["api_key"] = _lastFmApiKey,
                ["token"] = token
            };
            
            parameters["api_sig"] = GenerateLastFmSignature(parameters);
            parameters["format"] = "json";
            
            var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
            var response = await _httpClient.GetAsync($"{LastFmApiUrl}?{queryString}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("session", out var session) &&
                    session.TryGetProperty("key", out var key))
                {
                    _lastFmSessionKey = key.GetString();
                    return _lastFmSessionKey;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Last.fm] auth.getSession error: {ex.Message}");
        }
        
        return null;
    }
    
    public async Task ScrobbleCurrentTrackAsync()
    {
        if (!IsLastFmConfigured || CurrentTrack == null || _scrobbled) return;
        
        // Scrobble rules: played for at least 30 seconds or half the track
        var minTime = Math.Min(30, Duration.TotalSeconds / 2);
        if (Position.TotalSeconds < minTime) return;
        
        _scrobbled = true;
        
        var track = CurrentTrack;
        var timestamp = (_trackStartTime ?? DateTime.UtcNow).Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
        
        var parameters = new Dictionary<string, string>
        {
            ["artist"] = track.Artist,
            ["track"] = string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title,
            ["timestamp"] = ((long)timestamp).ToString()
        };
        
        if (!string.IsNullOrWhiteSpace(track.Album))
            parameters["album"] = track.Album;
        if (track.Duration.TotalSeconds > 0)
            parameters["duration"] = ((int)track.Duration.TotalSeconds).ToString();
        if (track.TrackNumber > 0)
            parameters["trackNumber"] = track.TrackNumber.ToString();
        
        var success = await LastFmApiCallAsync("track.scrobble", parameters);
        System.Diagnostics.Debug.WriteLine($"Scrobbled ({(success ? "OK" : "FAIL")}): {track.Artist} - {track.Title}");
    }
    
    public async Task UpdateNowPlayingAsync()
    {
        if (!IsLastFmConfigured || CurrentTrack == null) return;
        
        _trackStartTime = DateTime.UtcNow;
        _scrobbled = false;
        
        var track = CurrentTrack;
        var parameters = new Dictionary<string, string>
        {
            ["artist"] = track.Artist,
            ["track"] = string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title
        };
        
        if (!string.IsNullOrWhiteSpace(track.Album))
            parameters["album"] = track.Album;
        if (track.Duration.TotalSeconds > 0)
            parameters["duration"] = ((int)track.Duration.TotalSeconds).ToString();
        
        var success = await LastFmApiCallAsync("track.updateNowPlaying", parameters);
        System.Diagnostics.Debug.WriteLine($"Now Playing ({(success ? "OK" : "FAIL")}): {track.Artist} - {track.Title}");
    }
    
    #endregion
    
    #region Queue Persistence
    
    private static readonly string QueueFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlatypusTools", "enhanced_audio_queue.json");
    
    /// <summary>
    /// Saves the current queue to a JSON file for persistence across sessions.
    /// </summary>
    public async Task SaveQueueAsync()
    {
        try
        {
            var queueData = new
            {
                CurrentIndex = _currentIndex,
                Volume = Volume,
                IsMuted = IsMuted,
                IsShuffle = _isShuffled,
                RepeatMode = (int)Repeat,
                PlaybackSpeed = PlaybackSpeed,
                Tracks = _queue.Select(t => new
                {
                    FilePath = t.FilePath,
                    Title = t.Title,
                    Artist = t.Artist,
                    Album = t.Album,
                    Duration = t.Duration.Ticks,
                    Rating = t.Rating
                }).ToList()
            };
            
            var directory = Path.GetDirectoryName(QueueFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            
            var json = JsonSerializer.Serialize(queueData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(QueueFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving queue: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Loads the queue from the saved JSON file.
    /// </summary>
    public async Task<bool> LoadQueueAsync()
    {
        try
        {
            if (!File.Exists(QueueFilePath))
                return false;
            
            var json = await File.ReadAllTextAsync(QueueFilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            _queue.Clear();
            _shuffledIndices.Clear();
            
            if (root.TryGetProperty("Volume", out var vol))
                Volume = vol.GetDouble();
            if (root.TryGetProperty("IsMuted", out var muted))
                IsMuted = muted.GetBoolean();
            if (root.TryGetProperty("IsShuffle", out var shuffle))
                _isShuffled = shuffle.GetBoolean();
            if (root.TryGetProperty("RepeatMode", out var repeat))
                Repeat = (RepeatMode)repeat.GetInt32();
            if (root.TryGetProperty("PlaybackSpeed", out var speed))
                PlaybackSpeed = speed.GetDouble();
            
            if (root.TryGetProperty("Tracks", out var tracks))
            {
                foreach (var t in tracks.EnumerateArray())
                {
                    var track = new AudioTrack
                    {
                        FilePath = t.GetProperty("FilePath").GetString() ?? "",
                        Title = t.TryGetProperty("Title", out var title) ? title.GetString() ?? "" : "",
                        Artist = t.TryGetProperty("Artist", out var artist) ? artist.GetString() ?? "" : "",
                        Album = t.TryGetProperty("Album", out var album) ? album.GetString() ?? "" : "",
                        Duration = TimeSpan.FromTicks(t.TryGetProperty("Duration", out var dur) ? dur.GetInt64() : 0),
                        Rating = t.TryGetProperty("Rating", out var rating) ? rating.GetInt32() : 0
                    };
                    
                    if (File.Exists(track.FilePath))
                    {
                        _queue.Add(track);
                    }
                }
            }
            
            if (root.TryGetProperty("CurrentIndex", out var idx))
            {
                _currentIndex = Math.Min(idx.GetInt32(), _queue.Count - 1);
            }
            
            if (_isShuffled)
            {
                ShuffleQueue();
            }
            
            TrackChanged?.Invoke(this, CurrentTrack);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading queue: {ex.Message}");
            return false;
        }
    }
    
    #endregion
    
    public void Dispose()
    {
        UnregisterMediaKeys();
        Stop();
        _preloadedReader?.Dispose();
        _positionTimer.Stop();
        _spectrumTimer.Stop();
    }
}

#region NAudio Sample Providers

/// <summary>
/// 10-band parametric equalizer using NAudio.
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[,] _filters; // [channel, band]
    private readonly int _channels;
    
    // Center frequencies for 10-band EQ
    private static readonly float[] CenterFrequencies = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    
    public WaveFormat WaveFormat => _source.WaveFormat;
    
    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _filters = new BiQuadFilter[_channels, 10];
        
        for (int ch = 0; ch < _channels; ch++)
        {
            for (int band = 0; band < 10; band++)
            {
                _filters[ch, band] = BiQuadFilter.PeakingEQ(
                    source.WaveFormat.SampleRate,
                    CenterFrequencies[band],
                    1.0f, // Q factor
                    0); // Initial gain in dB
            }
        }
    }
    
    public void UpdateBand(int bandIndex, float gainDb)
    {
        if (bandIndex < 0 || bandIndex >= 10) return;
        
        for (int ch = 0; ch < _channels; ch++)
        {
            _filters[ch, bandIndex] = BiQuadFilter.PeakingEQ(
                _source.WaveFormat.SampleRate,
                CenterFrequencies[bandIndex],
                1.0f,
                gainDb);
        }
    }
    
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        
        for (int i = 0; i < samplesRead; i++)
        {
            int channel = i % _channels;
            float sample = buffer[offset + i];
            
            for (int band = 0; band < 10; band++)
            {
                sample = _filters[channel, band].Transform(sample);
            }
            
            buffer[offset + i] = sample;
        }
        
        return samplesRead;
    }
}

/// <summary>
/// Playback speed controller using sample rate manipulation.
/// </summary>
public class SpeedControlSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _speed = 1.0f;
    private readonly float[] _sourceBuffer = new float[4096];
    
    public WaveFormat WaveFormat => _source.WaveFormat;
    
    public SpeedControlSampleProvider(ISampleProvider source)
    {
        _source = source;
    }
    
    public void SetSpeed(float speed)
    {
        _speed = Math.Clamp(speed, 0.25f, 4.0f);
    }
    
    public int Read(float[] buffer, int offset, int count)
    {
        if (Math.Abs(_speed - 1.0f) < 0.01f)
        {
            // No speed change needed
            return _source.Read(buffer, offset, count);
        }
        
        // Simple speed change by resampling with cubic interpolation for smoother sound
        int sourceSamplesNeeded = (int)(count * _speed) + 4; // Extra samples for cubic interpolation
        int samplesRead = _source.Read(_sourceBuffer, 0, Math.Min(sourceSamplesNeeded, _sourceBuffer.Length));
        
        if (samplesRead == 0) return 0;
        
        int outputSamples = (int)((samplesRead - 3) / _speed); // Leave room for cubic lookahead
        outputSamples = Math.Min(outputSamples, count);
        
        for (int i = 0; i < outputSamples; i++)
        {
            float sourcePosition = i * _speed;
            int sourceIndex = (int)sourcePosition;
            float fraction = sourcePosition - sourceIndex;
            
            if (sourceIndex >= 0 && sourceIndex + 3 < samplesRead)
            {
                // Cubic Hermite interpolation for smoother results
                float p0 = sourceIndex > 0 ? _sourceBuffer[sourceIndex - 1] : _sourceBuffer[sourceIndex];
                float p1 = _sourceBuffer[sourceIndex];
                float p2 = _sourceBuffer[sourceIndex + 1];
                float p3 = _sourceBuffer[sourceIndex + 2];
                
                float a = -0.5f * p0 + 1.5f * p1 - 1.5f * p2 + 0.5f * p3;
                float b = p0 - 2.5f * p1 + 2.0f * p2 - 0.5f * p3;
                float c = -0.5f * p0 + 0.5f * p2;
                float d = p1;
                
                buffer[offset + i] = a * fraction * fraction * fraction + 
                                      b * fraction * fraction + 
                                      c * fraction + d;
            }
            else if (sourceIndex + 1 < samplesRead)
            {
                // Fall back to linear interpolation at edges
                buffer[offset + i] = _sourceBuffer[sourceIndex] * (1 - fraction) + 
                                      _sourceBuffer[sourceIndex + 1] * fraction;
            }
            else if (sourceIndex < samplesRead)
            {
                buffer[offset + i] = _sourceBuffer[sourceIndex];
            }
        }
        
        return outputSamples;
    }
}

/// <summary>
/// Volume control sample provider.
/// </summary>
public class VolumeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    public float Volume { get; set; } = 1.0f;
    
    public WaveFormat WaveFormat => _source.WaveFormat;
    
    public VolumeSampleProvider(ISampleProvider source)
    {
        _source = source;
    }
    
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        
        for (int i = 0; i < samplesRead; i++)
        {
            buffer[offset + i] *= Volume;
        }
        
        return samplesRead;
    }
}

/// <summary>
/// Spectrum analyzer sample provider that captures audio samples and performs real FFT analysis.
/// Based on pro-grade spectrum analyzer implementation with proper FFT using NAudio.Dsp.
/// </summary>
public class SpectrumAnalyzerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _fftSize;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _fftMagnitudes;
    private readonly float[] _sampleBuffer;
    private int _sampleIndex;
    private readonly object _lock = new();
    
    // Hanning window for FFT to reduce spectral leakage
    private readonly float[] _hanningWindow;
    
    // VU Meter data
    private float _peakLeft;
    private float _peakRight;
    private float _rmsLeft;
    private float _rmsRight;
    private float _leftSum;
    private float _rightSum;
    private int _sampleCount;
    private const int VU_SAMPLE_COUNT = 2048;
    
    // Oscilloscope data
    private readonly float[] _oscilloscopeBuffer;
    private int _oscilloscopeIndex;
    private const int OSCILLOSCOPE_SIZE = 512;
    
    public WaveFormat WaveFormat => _source.WaveFormat;
    
    public SpectrumAnalyzerSampleProvider(ISampleProvider source, int fftSize = 4096)
    {
        _source = source;
        _fftSize = fftSize;
        _fftBuffer = new Complex[fftSize];
        _fftMagnitudes = new float[fftSize / 2];
        _sampleBuffer = new float[fftSize];
        _sampleIndex = 0;
        
        // VU meter initialization
        _peakLeft = 0;
        _peakRight = 0;
        _oscilloscopeBuffer = new float[OSCILLOSCOPE_SIZE];
        _oscilloscopeIndex = 0;
        
        // Pre-compute Hanning window
        _hanningWindow = new float[fftSize];
        for (int i = 0; i < fftSize; i++)
        {
            _hanningWindow[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (fftSize - 1)));
        }
    }
    
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        
        // Capture mono samples for FFT (mix stereo to mono if needed)
        int channels = _source.WaveFormat.Channels;
        
        lock (_lock)
        {
            for (int i = 0; i < samplesRead; i += channels)
            {
                // Get left/right samples for VU meter
                float left = (offset + i < buffer.Length) ? buffer[offset + i] : 0;
                float right = (channels > 1 && offset + i + 1 < buffer.Length) ? buffer[offset + i + 1] : left;
                
                // Update peak levels
                _peakLeft = Math.Max(_peakLeft * 0.9995f, Math.Abs(left));
                _peakRight = Math.Max(_peakRight * 0.9995f, Math.Abs(right));
                
                // Accumulate for RMS calculation
                _leftSum += left * left;
                _rightSum += right * right;
                _sampleCount++;
                
                if (_sampleCount >= VU_SAMPLE_COUNT)
                {
                    _rmsLeft = MathF.Sqrt(_leftSum / _sampleCount);
                    _rmsRight = MathF.Sqrt(_rightSum / _sampleCount);
                    _leftSum = 0;
                    _rightSum = 0;
                    _sampleCount = 0;
                }
                
                // Mix to mono for spectrum
                float sample = (left + right) / 2f;
                
                // Add to FFT circular buffer
                _sampleBuffer[_sampleIndex] = sample;
                _sampleIndex = (_sampleIndex + 1) % _fftSize;
                
                // Add to oscilloscope buffer
                _oscilloscopeBuffer[_oscilloscopeIndex] = sample;
                _oscilloscopeIndex = (_oscilloscopeIndex + 1) % OSCILLOSCOPE_SIZE;
            }
        }
        
        return samplesRead;
    }
    
    /// <summary>
    /// Gets the current FFT magnitude data for visualization.
    /// Returns magnitude values in the range 0-1.
    /// </summary>
    public float[] GetFFTData()
    {
        lock (_lock)
        {
            // Copy samples to FFT buffer with Hanning window applied
            int readIndex = _sampleIndex;
            for (int i = 0; i < _fftSize; i++)
            {
                float sample = _sampleBuffer[readIndex] * _hanningWindow[i];
                _fftBuffer[i] = new Complex { X = sample, Y = 0 };
                readIndex = (readIndex + 1) % _fftSize;
            }
        }
        
        // Perform FFT using NAudio.Dsp
        FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _fftBuffer);
        
        // Calculate magnitudes (only first half - Nyquist)
        for (int i = 0; i < _fftSize / 2; i++)
        {
            float magnitude = MathF.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X + _fftBuffer[i].Y * _fftBuffer[i].Y);
            _fftMagnitudes[i] = magnitude;
        }
        
        return _fftMagnitudes;
    }
    
    /// <summary>
    /// Gets VU meter levels (peak and RMS) for left and right channels.
    /// </summary>
    public (float PeakLeft, float PeakRight, float RmsLeft, float RmsRight) GetVULevels()
    {
        lock (_lock)
        {
            return (_peakLeft, _peakRight, _rmsLeft, _rmsRight);
        }
    }
    
    /// <summary>
    /// Gets oscilloscope waveform data for visualization.
    /// </summary>
    public float[] GetOscilloscopeData()
    {
        lock (_lock)
        {
            var result = new float[OSCILLOSCOPE_SIZE];
            int readIndex = _oscilloscopeIndex;
            for (int i = 0; i < OSCILLOSCOPE_SIZE; i++)
            {
                result[i] = _oscilloscopeBuffer[readIndex];
                readIndex = (readIndex + 1) % OSCILLOSCOPE_SIZE;
            }
            return result;
        }
    }
}

#endregion
