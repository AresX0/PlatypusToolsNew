using PlatypusTools.Core.Models.Remote;

namespace PlatypusTools.Remote.Server.Services;

/// <summary>
/// Interface for remote audio control operations.
/// Bridges the remote API to the desktop PlatypusTools audio player.
/// </summary>
public interface IRemoteAudioService
{
    // Playback control
    Task PlayAsync();
    Task PauseAsync();
    Task StopAsync();
    Task NextTrackAsync();
    Task PreviousTrackAsync();
    Task SeekAsync(double position);
    Task SetVolumeAsync(double volume);

    // Now playing state
    Task<NowPlayingDto> GetNowPlayingAsync();

    // Queue management
    Task<IReadOnlyList<QueueItemDto>> GetQueueAsync();
    Task ClearQueueAsync();
    Task ShuffleQueueAsync();
    Task RemoveFromQueueAsync(int index);
    Task PlayQueueItemAsync(int index);
    Task AddToQueueAsync(string path);

    // Library browsing
    Task<IReadOnlyList<LibraryFolderDto>> GetLibraryFoldersAsync();
    Task<IReadOnlyList<LibraryFileDto>> GetLibraryFilesAsync(string path);
    
    // Direct file play
    Task PlayFileAsync(string path);
}
