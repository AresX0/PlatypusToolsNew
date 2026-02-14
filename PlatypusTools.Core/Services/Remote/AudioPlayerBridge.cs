using PlatypusTools.Core.Models.Remote;

namespace PlatypusTools.Core.Services.Remote;

/// <summary>
/// Static bridge that allows the PlatypusTools.UI to provide implementations
/// for audio player operations. This enables the Remote Server to control
/// the actual audio player when running embedded in the UI.
/// </summary>
public static class AudioPlayerBridge
{
    /// <summary>
    /// Interface that the UI implements to provide audio player functionality.
    /// </summary>
    public interface IAudioPlayerProvider
    {
        // Playback control
        void Play();
        void Pause();
        void Stop();
        void NextTrack();
        void PreviousTrack();
        void Seek(double position); // 0.0 - 1.0
        void SetVolume(double volume); // 0.0 - 1.0
        
        // State
        bool IsPlaying { get; }
        double Volume { get; }
        TimeSpan Position { get; }
        TimeSpan Duration { get; }
        
        // Current track info
        string? CurrentTrackTitle { get; }
        string? CurrentTrackArtist { get; }
        string? CurrentTrackAlbum { get; }
        string? CurrentTrackPath { get; }
        int CurrentTrackIndex { get; }
        
        // Queue
        IReadOnlyList<QueueItemDto> GetQueue();
        void ClearQueue();
        void ShuffleQueue();
        void RemoveFromQueue(int index);
        void PlayQueueItem(int index);
        void AddToQueue(string path);
        
        // Library
        IReadOnlyList<LibraryFolderDto> GetLibraryFolders();
        IReadOnlyList<LibraryFileDto> GetLibraryFiles(string path);
        
        // Direct file play
        void PlayFile(string path);
    }
    
    /// <summary>
    /// The registered audio player provider from PlatypusTools.UI.
    /// If null, the RemoteAudioService will use mock data.
    /// </summary>
    public static IAudioPlayerProvider? Provider { get; private set; }
    
    /// <summary>
    /// Registers the audio player provider. Call this from PlatypusTools.UI
    /// when initializing the remote server.
    /// </summary>
    public static void RegisterProvider(IAudioPlayerProvider provider)
    {
        Provider = provider;
    }
    
    /// <summary>
    /// Unregisters the provider (e.g., when shutting down).
    /// </summary>
    public static void UnregisterProvider()
    {
        Provider = null;
    }
    
    /// <summary>
    /// Returns true if a provider is registered and ready.
    /// </summary>
    public static bool IsConnected => Provider != null;
}
