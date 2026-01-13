using System;

namespace PlatypusTools.Core.Models.Audio;

/// <summary>
/// Represents an audio track with full metadata support.
/// </summary>
public class AudioTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = string.Empty;
    public string FileName => System.IO.Path.GetFileName(FilePath);
    
    // Core metadata
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int Year { get; set; }
    public int TrackNumber { get; set; }
    public int DiscNumber { get; set; }
    
    // Technical metadata
    public TimeSpan Duration { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string Codec { get; set; } = string.Empty;
    public long FileSize { get; set; }
    
    // Album art
    public byte[]? AlbumArt { get; set; }
    public string? AlbumArtPath { get; set; }
    
    // Playback statistics
    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.Now;
    public int Rating { get; set; } // 0-5 stars
    
    // ReplayGain
    public double TrackGain { get; set; }
    public double TrackPeak { get; set; }
    public double AlbumGain { get; set; }
    public double AlbumPeak { get; set; }
    
    // Display helpers
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FileName : Title;
    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;
    public string DisplayAlbum => string.IsNullOrWhiteSpace(Album) ? "Unknown Album" : Album;
    public string DurationFormatted => Duration.TotalHours >= 1 
        ? Duration.ToString(@"h\:mm\:ss") 
        : Duration.ToString(@"m\:ss");
    
    public override string ToString() => $"{DisplayArtist} - {DisplayTitle}";
}
