using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Audio;

/// <summary>
/// Represents a playlist containing audio tracks.
/// </summary>
public class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> TrackIds { get; set; } = new();
    public DateTime DateCreated { get; set; } = DateTime.Now;
    public DateTime DateModified { get; set; } = DateTime.Now;
    public string? CoverArtPath { get; set; }
    public bool IsSmartPlaylist { get; set; }
    public string? SmartPlaylistQuery { get; set; }
    public int TrackCount => TrackIds.Count;
    
    public PlaylistType Type { get; set; } = PlaylistType.User;
}

public enum PlaylistType
{
    User,
    Smart,
    Custom,
    NowPlaying,
    RecentlyPlayed,
    MostPlayed,
    RecentlyAdded,
    TopRated
}
