using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Audio;

/// <summary>
/// Represents lyrics for a track, with optional time-sync support.
/// </summary>
public class Lyrics
{
    public string TrackId { get; set; } = string.Empty;
    public LyricsType Type { get; set; } = LyricsType.Unsynchronized;
    public List<LyricLine> Lines { get; set; } = new();
    public string Source { get; set; } = string.Empty; // Embedded, LRC file, etc.
    
    public string PlainText => string.Join(Environment.NewLine, Lines.ConvertAll(l => l.Text));
}

public class LyricLine
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
    public TimeSpan? Duration { get; set; }
    
    public LyricLine() { }
    public LyricLine(TimeSpan timestamp, string text)
    {
        Timestamp = timestamp;
        Text = text;
    }
}

public enum LyricsType
{
    Unsynchronized,
    LineSynced,     // Standard LRC format
    WordSynced      // Karaoke-style
}
