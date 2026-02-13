namespace PlatypusTools.Remote.Client.Models;

/// <summary>
/// Current playback state for remote display.
/// </summary>
public class NowPlayingDto
{
    public bool IsPlaying { get; set; }
    public string Title { get; set; } = "Nothing Playing";
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public TimeSpan Position { get; set; }
    public double PositionPercent { get; set; }
    public double Volume { get; set; } = 0.75;
    public string? AlbumArtUrl { get; set; }
    public int QueueIndex { get; set; }
    public int QueueLength { get; set; }
}
