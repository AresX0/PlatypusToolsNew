/*
 * DEPRECATED: This service has been replaced by EnhancedAudioPlayerService.
 * The enhanced service uses NAudio for advanced features like:
 * - Real 10-band parametric EQ
 * - Gapless playback
 * - ReplayGain support
 * - Playback speed control (0.25x-4x)
 * - Media key support
 * - LRC lyrics parsing
 * - Last.fm scrobbling
 * 
 * This code is commented out for reference and can be removed once
 * EnhancedAudioPlayerService is fully validated.
 * 
 * To re-enable: Uncomment all code below and update dependent files:
 * - AudioLibraryViewModel.cs
 * - AudioLibraryService.cs
 * - AudioVisualizerView.xaml.cs
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using PlatypusTools.Core.Models.Audio;

namespace PlatypusTools.UI.Services;

/*
/// <summary>
/// Audio player service using Windows Media Player for playback.
/// Supports queue management, shuffle/repeat, and visualization data.
/// </summary>
public class AudioPlayerService
{
    private static AudioPlayerService? _instance;
    public static AudioPlayerService Instance => _instance ??= new AudioPlayerService();
    
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _visualizerTimer;
    private readonly Random _random = new();
    
    private List<AudioTrack> _queue = new();
    private List<int> _shuffledIndices = new();
    private int _currentIndex = -1;
    private bool _isShuffled;
    
    // Crossfade support
    private readonly MediaPlayer _crossfadePlayer;
    private bool _isCrossfading;
    private DispatcherTimer? _crossfadeTimer;
    private int _crossfadeDurationMs = 2000; // Default 2 seconds
    private bool _crossfadeEnabled = true;
    private double _crossfadeOriginalVolume = 0.7; // Store volume before crossfade starts
    
    /// <summary>
    /// Gets or sets whether crossfade is enabled between tracks.
    /// </summary>
    public bool CrossfadeEnabled
    {
        get => _crossfadeEnabled;
        set => _crossfadeEnabled = value;
    }
    
    /// <summary>
    /// Gets or sets the crossfade duration in milliseconds (0-5000).
    /// </summary>
    public int CrossfadeDurationMs
    {
        get => _crossfadeDurationMs;
        set => _crossfadeDurationMs = Math.Clamp(value, 0, 5000);
    }
    
    // EQ settings (affects visualization, actual audio EQ requires NAudio)
    private int _eqBass = 0;
    private int _eqMid = 0;
    private int _eqTreble = 0;
    
    /// <summary>
    /// Gets the current EQ bass setting (-12 to +12 dB).
    /// </summary>
    public int EqBass => _eqBass;
    
    /// <summary>
    /// Gets the current EQ mid setting (-12 to +12 dB).
    /// </summary>
    public int EqMid => _eqMid;
    
    /// <summary>
    /// Gets the current EQ treble setting (-12 to +12 dB).
    /// </summary>
    public int EqTreble => _eqTreble;
    
    /// <summary>
    /// Sets the bass EQ value (-12 to +12 dB).
    /// </summary>
    public void SetEqBass(int value)
    {
        _eqBass = Math.Clamp(value, -12, 12);
        System.Diagnostics.Debug.WriteLine($"EQ Bass set to {_eqBass} dB");
    }
    
    /// <summary>
    /// Sets the mid EQ value (-12 to +12 dB).
    /// </summary>
    public void SetEqMid(int value)
    {
        _eqMid = Math.Clamp(value, -12, 12);
        System.Diagnostics.Debug.WriteLine($"EQ Mid set to {_eqMid} dB");
    }
    
    /// <summary>
    /// Sets the treble EQ value (-12 to +12 dB).
    /// </summary>
    public void SetEqTreble(int value)
    {
        _eqTreble = Math.Clamp(value, -12, 12);
        System.Diagnostics.Debug.WriteLine($"EQ Treble set to {_eqTreble} dB");
    }
    
    /// <summary>
    /// Resets all EQ bands to flat (0 dB).
    /// </summary>
    public void ResetEq()
    {
        _eqBass = 0;
        _eqMid = 0;
        _eqTreble = 0;
        System.Diagnostics.Debug.WriteLine("EQ Reset to flat");
    }
    
    // Events
    public event EventHandler<AudioTrack?>? TrackChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<double[]>? SpectrumDataUpdated;
    public event EventHandler<TimeSpan>? MediaReady; // Fired when media is ready with duration
    
    public enum RepeatMode { None, All, One }
    
    // Properties
    public AudioTrack? CurrentTrack => _currentIndex >= 0 && _currentIndex < _queue.Count 
        ? _queue[_currentIndex] : null;
    public TimeSpan Position => _mediaPlayer.Position;
    public TimeSpan Duration => _mediaPlayer.NaturalDuration.HasTimeSpan 
        ? _mediaPlayer.NaturalDuration.TimeSpan : TimeSpan.Zero;
    public double Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 1);
    }
    public bool IsMuted
    {
        get => _mediaPlayer.IsMuted;
        set => _mediaPlayer.IsMuted = value;
    }
    public bool IsPlaying { get; private set; }
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
    
    /// <summary>
    /// Gets the current track index for debugging purposes.
    /// </summary>
    public int CurrentIndex => _currentIndex;
    
    private AudioPlayerService()
    {
        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaFailed += OnMediaFailed;
        
        // Initialize crossfade player
        _crossfadePlayer = new MediaPlayer();
        _crossfadePlayer.MediaOpened += (s, e) => { };
        _crossfadePlayer.MediaFailed += (s, e) => { };
        
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _positionTimer.Tick += OnPositionTimerTick;
        
        _visualizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _visualizerTimer.Tick += (s, e) => GenerateVisualizationData();
        
        // Start visualizer timer immediately so visualizer shows idle animation
        _visualizerTimer.Start();
        System.Diagnostics.Debug.WriteLine("AudioPlayerService: Visualizer timer started in constructor");
    }
    
    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        PositionChanged?.Invoke(this, Position);
        
        // Check if we should start crossfade
        if (_crossfadeEnabled && !_isCrossfading && IsPlaying && Duration.TotalMilliseconds > 0)
        {
            var remainingMs = (Duration - Position).TotalMilliseconds;
            if (remainingMs <= _crossfadeDurationMs && remainingMs > 0 && _queue.Count > 1)
            {
                StartCrossfade();
            }
        }
    }
    
    private void StartCrossfade()
    {
        if (_isCrossfading || Repeat == RepeatMode.One) return;
        
        // Get next track
        int nextIndex = GetNextIndex();
        if (nextIndex < 0 || nextIndex >= _queue.Count) return;
        
        var nextTrack = _queue[nextIndex];
        if (nextTrack == null || !File.Exists(nextTrack.FilePath)) return;
        
        _isCrossfading = true;
        
        try
        {
            // Store the original volume BEFORE we start fading
            _crossfadeOriginalVolume = _mediaPlayer.Volume;
            
            // Start next track on crossfade player at volume 0
            _crossfadePlayer.Open(new Uri(nextTrack.FilePath));
            _crossfadePlayer.Volume = 0;
            _crossfadePlayer.Play();
            
            // Start crossfade timer
            _crossfadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            var startTime = DateTime.Now;
            var fadeOutVolume = _crossfadeOriginalVolume;
            
            _crossfadeTimer.Tick += (s, e) =>
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var progress = Math.Min(elapsed / _crossfadeDurationMs, 1.0);
                
                // Fade out current, fade in next
                _mediaPlayer.Volume = fadeOutVolume * (1 - progress);
                _crossfadePlayer.Volume = fadeOutVolume * progress;
                
                if (progress >= 1.0)
                {
                    CompleteCrossfade();
                }
            };
            _crossfadeTimer.Start();
        }
        catch
        {
            _isCrossfading = false;
        }
    }
    
    private void CompleteCrossfade()
    {
        try
        {
            _crossfadeTimer?.Stop();
            _crossfadeTimer = null;
            
            // Stop the old track
            _mediaPlayer.Stop();
            
            // Use the stored original volume (NOT the faded-out volume)
            var targetVolume = _crossfadeOriginalVolume;
            
            // Move to next track index
            _currentIndex = GetNextIndex();
            
            if (CurrentTrack != null)
            {
                CurrentTrack.PlayCount++;
                CurrentTrack.LastPlayed = DateTime.Now;
            }
            
            // Get the current position from crossfade player before we touch anything
            var crossfadePosition = _crossfadePlayer.Position;
            
            // Open the track on main player
            if (CurrentTrack != null && File.Exists(CurrentTrack.FilePath))
            {
                _mediaPlayer.Open(new Uri(CurrentTrack.FilePath));
                _mediaPlayer.Volume = targetVolume;
                
                // Wait for media to open, then sync position and swap
                _mediaPlayer.MediaOpened += OnCrossfadeMediaOpened;
                void OnCrossfadeMediaOpened(object? s, EventArgs ev)
                {
                    _mediaPlayer.MediaOpened -= OnCrossfadeMediaOpened;
                    try
                    {
                        // Sync position and ensure volume is correct
                        _mediaPlayer.Position = crossfadePosition;
                        _mediaPlayer.Volume = targetVolume;
                        _mediaPlayer.Play();
                        
                        // Now stop crossfade player
                        _crossfadePlayer.Stop();
                        _crossfadePlayer.Volume = 0;
                    }
                    catch { }
                };
            }
            else
            {
                // No valid track, just stop crossfade player
                _crossfadePlayer.Stop();
            }
            
            _isCrossfading = false;
            TrackChanged?.Invoke(this, CurrentTrack);
        }
        catch
        {
            _isCrossfading = false;
        }
    }
    
    private int GetNextIndex()
    {
        if (_queue.Count == 0) return -1;
        
        if (_isShuffled && _shuffledIndices.Count > 0)
        {
            var currentShuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (currentShuffleIndex < _shuffledIndices.Count - 1)
                return _shuffledIndices[currentShuffleIndex + 1];
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
    
    // Playback control
    public async Task<AudioTrack?> LoadTrackAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        
        var track = new AudioTrack
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath)
        };
        
        await ReadMetadataAsync(track);
        return track;
    }
    
    public async Task PlayTrackAsync(AudioTrack track)
    {
        if (track == null) 
        {
            System.Diagnostics.Debug.WriteLine("PlayTrackAsync: track is null!");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"PlayTrackAsync: track='{track.DisplayTitle}', queue.Count={_queue.Count}");
        
        // Find by ID or FilePath
        var index = _queue.FindIndex(t => t.Id == track.Id || t.FilePath == track.FilePath);
        if (index < 0)
        {
            _queue.Add(track);
            index = _queue.Count - 1;
            System.Diagnostics.Debug.WriteLine($"PlayTrackAsync: Added track to queue at index {index}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"PlayTrackAsync: Found track at index {index}");
        }
        
        _currentIndex = index;
        System.Diagnostics.Debug.WriteLine($"PlayTrackAsync: Set _currentIndex to {_currentIndex}, CurrentTrack is {(CurrentTrack != null ? "valid" : "NULL")}");
        await PlayCurrentAsync();
    }
    
    public async Task PlayCurrentAsync()
    {
        System.Diagnostics.Debug.WriteLine($"PlayCurrentAsync: _currentIndex={_currentIndex}, _queue.Count={_queue.Count}, CurrentTrack is {(CurrentTrack != null ? CurrentTrack.DisplayTitle : "NULL")}");
        if (CurrentTrack == null) return;
        
        try
        {
            _mediaPlayer.Open(new Uri(CurrentTrack.FilePath));
            await Task.Delay(50); // Allow media to load
            _mediaPlayer.Play();
            IsPlaying = true;
            _positionTimer.Start();
            _visualizerTimer.Start();
            
            CurrentTrack.PlayCount++;
            CurrentTrack.LastPlayed = DateTime.Now;
            
            System.Diagnostics.Debug.WriteLine($"PlayCurrentAsync: Firing TrackChanged event with '{CurrentTrack.DisplayTitle}'");
            TrackChanged?.Invoke(this, CurrentTrack);
            PlaybackStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlayCurrentAsync: Exception - {ex.Message}");
        }
    }
    
    public void Play()
    {
        if (CurrentTrack != null)
        {
            _mediaPlayer.Play();
            IsPlaying = true;
            _positionTimer.Start();
            _visualizerTimer.Start();
            PlaybackStateChanged?.Invoke(this, true);
        }
    }
    
    public void Pause()
    {
        _mediaPlayer.Pause();
        IsPlaying = false;
        _positionTimer.Stop();
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    public void Stop()
    {
        _mediaPlayer.Stop();
        IsPlaying = false;
        _positionTimer.Stop();
        _visualizerTimer.Stop();
        PlaybackStateChanged?.Invoke(this, false);
    }
    
    public void Seek(TimeSpan position)
    {
        if (_mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            _mediaPlayer.Position = position;
        }
    }
    
    public async Task NextAsync()
    {
        if (_queue.Count == 0) return;
        
        if (Repeat == RepeatMode.One)
        {
            await PlayCurrentAsync();
            return;
        }
        
        if (_isShuffled && _shuffledIndices.Count > 0)
        {
            var currentShuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (currentShuffleIndex < _shuffledIndices.Count - 1)
                _currentIndex = _shuffledIndices[currentShuffleIndex + 1];
            else if (Repeat == RepeatMode.All)
                _currentIndex = _shuffledIndices[0];
            else
            {
                Stop();
                return;
            }
        }
        else
        {
            if (_currentIndex < _queue.Count - 1)
                _currentIndex++;
            else if (Repeat == RepeatMode.All)
                _currentIndex = 0;
            else
            {
                Stop();
                return;
            }
        }
        
        await PlayCurrentAsync();
    }
    
    public async Task PreviousAsync()
    {
        if (_queue.Count == 0) return;
        
        // If more than 3 seconds in, restart current track
        if (Position.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }
        
        if (_isShuffled && _shuffledIndices.Count > 0)
        {
            var currentShuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (currentShuffleIndex > 0)
                _currentIndex = _shuffledIndices[currentShuffleIndex - 1];
        }
        else
        {
            if (_currentIndex > 0)
                _currentIndex--;
            else if (Repeat == RepeatMode.All)
                _currentIndex = _queue.Count - 1;
        }
        
        await PlayCurrentAsync();
    }
    
    // Queue management
    public void SetQueue(IEnumerable<AudioTrack> tracks)
    {
        _queue = tracks.ToList();
        if (_isShuffled) ShuffleQueue();
        
        // Reset current index if it's out of bounds or if we're starting fresh
        if (_currentIndex < 0 && _queue.Count > 0)
        {
            _currentIndex = 0; // Default to first track
        }
        else if (_currentIndex >= _queue.Count)
        {
            _currentIndex = _queue.Count > 0 ? 0 : -1;
        }
        
        System.Diagnostics.Debug.WriteLine($"SetQueue: {_queue.Count} tracks, _currentIndex={_currentIndex}");
    }
    
    public void AddToQueue(AudioTrack track)
    {
        _queue.Add(track);
        if (_isShuffled) _shuffledIndices.Add(_queue.Count - 1);
    }
    
    public void AddToQueue(IEnumerable<AudioTrack> tracks)
    {
        var startIndex = _queue.Count;
        _queue.AddRange(tracks);
        if (_isShuffled)
        {
            for (int i = startIndex; i < _queue.Count; i++)
                _shuffledIndices.Add(i);
        }
    }
    
    public void RemoveFromQueue(AudioTrack track)
    {
        var index = _queue.FindIndex(t => t.Id == track.Id);
        if (index >= 0)
        {
            _queue.RemoveAt(index);
            _shuffledIndices.Remove(index);
            if (index < _currentIndex) _currentIndex--;
        }
    }
    
    /// <summary>
    /// Inserts a track to play next (after the currently playing track).
    /// </summary>
    public void PlayNext(AudioTrack track)
    {
        // Remove if already in queue
        var existingIndex = _queue.FindIndex(t => t.Id == track.Id);
        if (existingIndex >= 0)
        {
            _queue.RemoveAt(existingIndex);
            if (existingIndex < _currentIndex) _currentIndex--;
            else if (existingIndex == _currentIndex) return; // Can't play next if currently playing
        }
        
        // Insert after current track
        var insertIndex = _currentIndex + 1;
        if (insertIndex > _queue.Count) insertIndex = _queue.Count;
        if (insertIndex < 0) insertIndex = 0;
        
        _queue.Insert(insertIndex, track);
        
        // Update shuffle indices if needed
        if (_isShuffled)
        {
            _shuffledIndices = _shuffledIndices.Select(i => i >= insertIndex ? i + 1 : i).ToList();
            var currentShuffleIndex = _shuffledIndices.IndexOf(_currentIndex);
            if (currentShuffleIndex >= 0 && currentShuffleIndex < _shuffledIndices.Count - 1)
                _shuffledIndices.Insert(currentShuffleIndex + 1, insertIndex);
            else
                _shuffledIndices.Add(insertIndex);
        }
    }
    
    /// <summary>
    /// Moves a track from one position to another in the queue.
    /// </summary>
    public void MoveInQueue(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _queue.Count) return;
        if (toIndex < 0 || toIndex >= _queue.Count) return;
        if (fromIndex == toIndex) return;
        
        var track = _queue[fromIndex];
        _queue.RemoveAt(fromIndex);
        _queue.Insert(toIndex, track);
        
        // Adjust current index if needed
        if (_currentIndex == fromIndex)
            _currentIndex = toIndex;
        else if (fromIndex < _currentIndex && toIndex >= _currentIndex)
            _currentIndex--;
        else if (fromIndex > _currentIndex && toIndex <= _currentIndex)
            _currentIndex++;
        
        // Rebuild shuffle indices if shuffled
        if (_isShuffled) ShuffleQueue();
    }
    
    /// <summary>
    /// Opens the containing folder of a track in Windows Explorer.
    /// </summary>
    public void RevealInExplorer(AudioTrack track)
    {
        if (track == null || string.IsNullOrEmpty(track.FilePath)) return;
        
        try
        {
            if (File.Exists(track.FilePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
            }
            else
            {
                var folder = Path.GetDirectoryName(track.FilePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
        }
        catch { }
    }
    
    public void ClearQueue()
    {
        Stop();
        _queue.Clear();
        _shuffledIndices.Clear();
        _currentIndex = -1;
        TrackChanged?.Invoke(this, null);
    }
    
    public async Task<List<AudioTrack>> ScanFolderAsync(string folderPath, bool includeSubfolders = true)
    {
        var tracks = new List<AudioTrack>();
        var extensions = new[] { ".mp3", ".wav", ".wma", ".aac", ".m4a", ".flac", ".ogg", ".opus" };
        var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        
        try
        {
            // Run enumeration on background thread to avoid UI blocking
            var files = await Task.Run(() =>
                Directory.EnumerateFiles(folderPath, "*.*", searchOption)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList()
            );
            
            // Load tracks asynchronously with a cap to avoid memory issues
            using var semaphore = new System.Threading.SemaphoreSlim(4); // Max 4 concurrent loads
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await LoadTrackAsync(file);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            var results = await Task.WhenAll(tasks);
            tracks.AddRange(results.Where(t => t != null)!);
        }
        catch (Exception)
        {
            // Log error
        }
        
        return tracks;
    }
    
    private void ShuffleQueue()
    {
        _shuffledIndices = Enumerable.Range(0, _queue.Count).OrderBy(_ => _random.Next()).ToList();
        
        // Keep current track at start
        if (_currentIndex >= 0)
        {
            _shuffledIndices.Remove(_currentIndex);
            _shuffledIndices.Insert(0, _currentIndex);
        }
    }
    
    // Event handlers
    private async void OnMediaEnded(object? sender, EventArgs e)
    {
        await NextAsync();
    }
    
    private void OnMediaOpened(object? sender, EventArgs e)
    {
        // Media loaded successfully - update duration on the track
        if (CurrentTrack != null && _mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            CurrentTrack.Duration = _mediaPlayer.NaturalDuration.TimeSpan;
            System.Diagnostics.Debug.WriteLine($"OnMediaOpened: Duration = {CurrentTrack.Duration}");
            
            // Fire MediaReady event with duration
            MediaReady?.Invoke(this, _mediaPlayer.NaturalDuration.TimeSpan);
            
            // Re-fire track changed to update UI with duration
            TrackChanged?.Invoke(this, CurrentTrack);
        }
    }
    
    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        // Handle media load failure
        System.Diagnostics.Debug.WriteLine($"Media failed: {e.ErrorException?.Message}");
    }
    
    // Visualization
    private readonly double[] _spectrumData = new double[128]; // Support up to 128 bars
    private double[] _previousSpectrum = new double[128];
    
    private void GenerateVisualizationData()
    {
        // Generate simulated spectrum data based on position
        // Real implementation would use NAudio or similar for actual FFT
        if (!IsPlaying)
        {
            // Still fire event with low idle data so visualizer can decide what to do
            for (int i = 0; i < _spectrumData.Length; i++)
            {
                _spectrumData[i] = 0;
            }
            SpectrumDataUpdated?.Invoke(this, _spectrumData);
            return;
        }
        
        var t = Position.TotalSeconds;
        
        // Calculate EQ multipliers (convert dB to linear scale)
        double bassMultiplier = Math.Pow(10, _eqBass / 20.0);
        double midMultiplier = Math.Pow(10, _eqMid / 20.0);
        double trebleMultiplier = Math.Pow(10, _eqTreble / 20.0);
        
        for (int i = 0; i < _spectrumData.Length; i++)
        {
            // Create more dynamic spectrum based on position
            double freq = (i + 1) * 0.4;
            double baseValue = Math.Sin(t * freq) * 0.4 + 0.4;
            
            // Add harmonics
            double harmonic1 = Math.Sin(t * freq * 2.1 + 0.5) * 0.15;
            double harmonic2 = Math.Sin(t * freq * 0.5 + 1.2) * 0.1;
            
            // Bass emphasis for lower frequencies
            double bassBoost = i < 20 ? (1.0 - i / 20.0) * 0.25 : 0;
            
            // Random variation
            double noise = _random.NextDouble() * 0.15;
            
            // Calculate base raw value
            double rawValue = baseValue + harmonic1 + harmonic2 + bassBoost + noise;
            
            // Apply EQ based on frequency band
            // Bass: indices 0-30 (roughly 0-250 Hz)
            // Mid: indices 31-80 (roughly 250-2000 Hz)
            // Treble: indices 81-127 (roughly 2000+ Hz)
            double eqMultiplier;
            if (i <= 30)
            {
                // Bass region - blend with mid at boundary
                double blend = i / 30.0;
                eqMultiplier = bassMultiplier * (1 - blend * 0.3) + midMultiplier * (blend * 0.3);
            }
            else if (i <= 80)
            {
                // Mid region
                double pos = (i - 30) / 50.0;
                double bassBlend = 1 - Math.Min(pos * 3, 1);
                double trebleBlend = Math.Max((pos - 0.66) * 3, 0);
                double midBlend = 1 - bassBlend - trebleBlend;
                eqMultiplier = bassMultiplier * bassBlend * 0.3 + midMultiplier * midBlend + trebleMultiplier * trebleBlend * 0.3;
            }
            else
            {
                // Treble region - blend with mid at boundary
                double blend = (i - 80) / 47.0;
                eqMultiplier = midMultiplier * (1 - blend) * 0.3 + trebleMultiplier * (0.7 + blend * 0.3);
            }
            
            rawValue *= eqMultiplier;
            
            // Smooth with previous value
            _spectrumData[i] = Math.Clamp(_previousSpectrum[i] * 0.3 + rawValue * 0.7, 0, 1);
            _previousSpectrum[i] = _spectrumData[i];
        }
        
        SpectrumDataUpdated?.Invoke(this, _spectrumData);
    }
    
    // Metadata reading
    private async Task ReadMetadataAsync(AudioTrack track)
    {
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
            }
            catch
            {
                track.Title = Path.GetFileNameWithoutExtension(track.FilePath);
            }
        });
    }
    
    // Queue Persistence
    private static readonly string QueueFilePath = Path.Combine(
        SettingsManager.DataDirectory, "audio_queue.json");
    
    /// <summary>
    /// Saves the current queue to a JSON file for persistence across sessions.
    /// </summary>
    public async Task SaveQueueAsync()
    {
        try
        {
            var queueData = new QueuePersistenceData
            {
                CurrentIndex = _currentIndex,
                Volume = Volume,
                IsMuted = IsMuted,
                IsShuffle = _isShuffled,
                RepeatMode = (int)Repeat,
                Tracks = _queue.Select(t => new TrackPersistenceData
                {
                    FilePath = t.FilePath,
                    Title = t.Title,
                    Artist = t.Artist,
                    Album = t.Album,
                    Duration = t.Duration.Ticks
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
            var queueData = JsonSerializer.Deserialize<QueuePersistenceData>(json);
            
            if (queueData == null || queueData.Tracks == null || queueData.Tracks.Count == 0)
                return false;
            
            // Filter to only existing files
            var validTracks = new List<AudioTrack>();
            foreach (var trackData in queueData.Tracks)
            {
                if (File.Exists(trackData.FilePath))
                {
                    validTracks.Add(new AudioTrack
                    {
                        Id = Guid.NewGuid().ToString(),
                        FilePath = trackData.FilePath,
                        Title = trackData.Title ?? Path.GetFileNameWithoutExtension(trackData.FilePath),
                        Artist = trackData.Artist ?? "Unknown Artist",
                        Album = trackData.Album ?? "Unknown Album",
                        Duration = TimeSpan.FromTicks(trackData.Duration)
                    });
                }
            }
            
            if (validTracks.Count == 0)
                return false;
            
            _queue = validTracks;
            _currentIndex = Math.Min(queueData.CurrentIndex, _queue.Count - 1);
            if (_currentIndex < 0) _currentIndex = 0;
            
            Volume = queueData.Volume;
            IsMuted = queueData.IsMuted;
            _isShuffled = queueData.IsShuffle;
            Repeat = (RepeatMode)queueData.RepeatMode;
            
            if (_isShuffled)
                ShuffleQueue();
            
            TrackChanged?.Invoke(this, CurrentTrack);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading queue: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Data structure for queue persistence.
    /// </summary>
    private class QueuePersistenceData
    {
        public int CurrentIndex { get; set; }
        public double Volume { get; set; } = 0.7;
        public bool IsMuted { get; set; }
        public bool IsShuffle { get; set; }
        public int RepeatMode { get; set; }
        public List<TrackPersistenceData> Tracks { get; set; } = new();
    }
    
    private class TrackPersistenceData
    {
        public string FilePath { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public long Duration { get; set; }
    }
}
*/
