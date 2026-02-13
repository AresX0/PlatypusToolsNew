namespace PlatypusTools.Remote.Client.Models;

/// <summary>
/// Queue item for remote display.
/// </summary>
public class QueueItemDto
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public bool IsCurrentTrack { get; set; }
}
