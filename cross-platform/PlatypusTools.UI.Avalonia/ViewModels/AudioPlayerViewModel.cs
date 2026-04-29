using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class AudioPlayerViewModel : ObservableObject, IDisposable
{
    private LibVLC? _libvlc;
    private MediaPlayer? _player;

    [ObservableProperty] private string _status = "No track loaded.";
    [ObservableProperty] private string _trackName = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _position; // 0..1
    [ObservableProperty] private int _volume = 80;

    public AudioPlayerViewModel()
    {
        try
        {
            // LibVLCSharp Core.Initialize() must be called before LibVLC ctor.
            // Fully qualify to avoid clash with PlatypusTools.Core namespace.
            LibVLCSharp.Shared.Core.Initialize();
            _libvlc = new LibVLC();
            _player = new MediaPlayer(_libvlc);
            _player.Volume = Volume;
            _player.PositionChanged += (_, e) => Position = e.Position;
            _player.Playing += (_, _) => IsPlaying = true;
            _player.Paused += (_, _) => IsPlaying = false;
            _player.Stopped += (_, _) => IsPlaying = false;
            _player.EndReached += (_, _) => IsPlaying = false;
        }
        catch (Exception ex)
        {
            Status = $"VLC unavailable: {ex.Message}. Install libvlc on Linux (e.g. apt install vlc).";
        }
    }

    public bool VlcAvailable => _libvlc is not null && _player is not null;

    partial void OnVolumeChanged(int value)
    {
        if (_player is not null) _player.Volume = Math.Clamp(value, 0, 100);
    }

    public Task LoadAsync(string path)
    {
        if (_libvlc is null || _player is null) return Task.CompletedTask;
        if (!File.Exists(path))
        {
            Status = $"Not found: {path}";
            return Task.CompletedTask;
        }

        using var media = new Media(_libvlc, new Uri(path));
        _player.Media = media;
        TrackName = Path.GetFileName(path);
        Status = $"Loaded: {TrackName}";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Play() => _player?.Play();

    [RelayCommand]
    private void Pause() => _player?.Pause();

    [RelayCommand]
    private void Stop() => _player?.Stop();

    public void Dispose()
    {
        _player?.Dispose();
        _libvlc?.Dispose();
        _player = null;
        _libvlc = null;
    }
}
