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
    
    // Events
    public event EventHandler<AudioTrack?>? TrackChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<double[]>? SpectrumDataUpdated;
    
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
    
    private AudioPlayerService()
    {
        _mediaPlayer = new MediaPlayer();
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaFailed += OnMediaFailed;
        
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _positionTimer.Tick += (s, e) => PositionChanged?.Invoke(this, Position);
        
        _visualizerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _visualizerTimer.Tick += (s, e) => GenerateVisualizationData();
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
        var index = _queue.FindIndex(t => t.Id == track.Id);
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
            
            TrackChanged?.Invoke(this, CurrentTrack);
            PlaybackStateChanged?.Invoke(this, true);
        }
        catch (Exception)
        {
            // Log error
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
        // Media loaded successfully
    }
    
    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        // Handle media load failure
    }
    
    // Visualization
    private readonly double[] _spectrumData = new double[32];
    private void GenerateVisualizationData()
    {
        // Generate simulated spectrum data based on position
        // Real implementation would use NAudio or similar for actual FFT
        if (!IsPlaying) return;
        
        var t = Position.TotalSeconds;
        for (int i = 0; i < _spectrumData.Length; i++)
        {
            var baseValue = Math.Sin(t * (i + 1) * 0.5) * 0.5 + 0.5;
            var noise = _random.NextDouble() * 0.3;
            _spectrumData[i] = Math.Clamp(baseValue * 0.7 + noise, 0, 1);
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
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlatypusTools", "audio_queue.json");
    
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
