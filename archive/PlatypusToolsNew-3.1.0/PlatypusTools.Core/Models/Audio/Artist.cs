using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Audio;

/// <summary>
/// Represents an artist in the music library.
/// </summary>
public class Artist
{
    public string Name { get; set; } = string.Empty;
    public List<string> AlbumIds { get; set; } = new();
    public int TrackCount { get; set; }
    public byte[]? ArtistImage { get; set; }
    public string? Biography { get; set; }
    
    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : "Unknown Artist";
}

/// <summary>
/// Represents an album in the music library.
/// </summary>
public class Album
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Genre { get; set; } = string.Empty;
    public List<string> TrackIds { get; set; } = new();
    public byte[]? CoverArt { get; set; }
    public string? CoverArtPath { get; set; }
    public TimeSpan TotalDuration { get; set; }
    
    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : "Unknown Album";
    public string DisplayArtist => !string.IsNullOrEmpty(AlbumArtist) 
        ? AlbumArtist 
        : (!string.IsNullOrEmpty(Artist) ? Artist : "Unknown Artist");
    public int TrackCount => TrackIds.Count;
}
