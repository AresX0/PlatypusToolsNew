using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Models.Remote;
using PlatypusTools.Core.Services.Remote;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Services;

/// <summary>
/// Implementation of IAudioPlayerProvider that bridges the Remote Server
/// to the actual PlatypusTools.UI audio player.
/// </summary>
public class AudioPlayerProviderImpl : AudioPlayerBridge.IAudioPlayerProvider
{
    private readonly EnhancedAudioPlayerService _audioService;
    private readonly EnhancedAudioPlayerViewModel? _viewModel;
    private static readonly string[] _audioExtensions = { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".wma", ".aac", ".opus" };

    public AudioPlayerProviderImpl(EnhancedAudioPlayerService audioService, EnhancedAudioPlayerViewModel? viewModel = null)
    {
        _audioService = audioService;
        _viewModel = viewModel;
    }

    public bool IsPlaying => _audioService.IsPlaying;
    public string? CurrentTrackTitle => _audioService.CurrentTrack?.Title;
    public string? CurrentTrackArtist => _audioService.CurrentTrack?.Artist;
    public string? CurrentTrackAlbum => _audioService.CurrentTrack?.Album;
    public string? CurrentTrackPath => _audioService.CurrentTrack?.FilePath;
    public TimeSpan Duration => _audioService.Duration;
    public TimeSpan Position => _audioService.Position;
    public double Volume => _audioService.Volume;
    public int CurrentTrackIndex => _audioService.CurrentIndex;

    public void Play() => _audioService.Play();
    public void Pause() => _audioService.Pause();
    public void Stop() => _audioService.Stop();
    
    public void NextTrack()
    {
        _ = _audioService.NextAsync();
    }

    public void PreviousTrack()
    {
        _ = _audioService.PreviousAsync();
    }

    public void Seek(double positionPercent)
    {
        var targetPosition = TimeSpan.FromSeconds(_audioService.Duration.TotalSeconds * positionPercent);
        _audioService.Seek(targetPosition);
    }

    public void SetVolume(double volume)
    {
        _audioService.Volume = volume;
    }

    public IReadOnlyList<QueueItemDto> GetQueue()
    {
        var queue = _audioService.Queue;
        var currentIndex = _audioService.CurrentIndex;

        return queue.Select((track, index) => new QueueItemDto
        {
            Index = index,
            Title = track.Title ?? Path.GetFileNameWithoutExtension(track.FilePath),
            Artist = track.Artist ?? "Unknown Artist",
            Duration = track.Duration,
            FilePath = track.FilePath,
            IsCurrentTrack = index == currentIndex
        }).ToList();
    }

    public void ClearQueue()
    {
        _audioService.ClearQueue();
    }

    public void ShuffleQueue()
    {
        _audioService.IsShuffle = !_audioService.IsShuffle;
    }

    public void RemoveFromQueue(int index)
    {
        var queue = _audioService.Queue;
        if (index >= 0 && index < queue.Count)
        {
            _audioService.RemoveFromQueue(queue[index]);
        }
    }

    public void PlayQueueItem(int index)
    {
        var queue = _audioService.Queue;
        if (index >= 0 && index < queue.Count)
        {
            _ = _audioService.PlayTrackAsync(queue[index]);
        }
    }

    public void AddToQueue(string path)
    {
        var track = new AudioTrack
        {
            FilePath = path,
            Title = Path.GetFileNameWithoutExtension(path),
            Artist = "Unknown Artist",
            Album = "Unknown Album",
            Duration = TimeSpan.Zero
        };
        _audioService.AddToQueue(track);
    }

    public IReadOnlyList<LibraryFolderDto> GetLibraryFolders()
    {
        var folders = new List<LibraryFolderDto>();

        if (_viewModel != null)
        {
            foreach (var folderPath in _viewModel.LibraryFolders)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(folderPath);
                    int fileCount = 0;
                    if (dirInfo.Exists)
                    {
                        fileCount = _audioExtensions
                            .SelectMany(ext => dirInfo.EnumerateFiles($"*{ext}", SearchOption.AllDirectories))
                            .Count();
                    }

                    folders.Add(new LibraryFolderDto
                    {
                        Path = folderPath,
                        Name = dirInfo.Name,
                        FileCount = fileCount
                    });
                }
                catch
                {
                    // Skip inaccessible folders
                    folders.Add(new LibraryFolderDto
                    {
                        Path = folderPath,
                        Name = Path.GetFileName(folderPath),
                        FileCount = 0
                    });
                }
            }
        }

        return folders;
    }

    public IReadOnlyList<LibraryFileDto> GetLibraryFiles(string path)
    {
        var files = new List<LibraryFileDto>();

        try
        {
            var dirInfo = new DirectoryInfo(path);
            if (!dirInfo.Exists)
                return files;

            // Add subdirectories first
            foreach (var subDir in dirInfo.EnumerateDirectories().OrderBy(d => d.Name))
            {
                try
                {
                    files.Add(new LibraryFileDto
                    {
                        Path = subDir.FullName,
                        Name = subDir.Name,
                        Size = 0,
                        IsDirectory = true
                    });
                }
                catch { /* Skip inaccessible dirs */ }
            }

            // Add audio files
            foreach (var file in dirInfo.EnumerateFiles()
                .Where(f => _audioExtensions.Contains(f.Extension.ToLowerInvariant()))
                .OrderBy(f => f.Name))
            {
                try
                {
                    files.Add(new LibraryFileDto
                    {
                        Path = file.FullName,
                        Name = file.Name,
                        Size = file.Length,
                        IsDirectory = false
                    });
                }
                catch { /* Skip inaccessible files */ }
            }
        }
        catch
        {
            // Return empty if folder is not accessible
        }

        return files;
    }

    /// <summary>
    /// Plays a file from the library (not adding to queue, just play immediately).
    /// </summary>
    public void PlayFile(string path)
    {
        if (File.Exists(path))
        {
            var track = new AudioTrack
            {
                FilePath = path,
                Title = Path.GetFileNameWithoutExtension(path),
                Artist = "Unknown Artist",
                Album = "Unknown Album",
                Duration = TimeSpan.Zero
            };
            _ = _audioService.PlayTrackAsync(track);
        }
    }
}
