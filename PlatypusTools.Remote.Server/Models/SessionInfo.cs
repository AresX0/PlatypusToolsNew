namespace PlatypusTools.Remote.Server.Models;

/// <summary>
/// Information about an authenticated session.
/// </summary>
public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public string UserAgent { get; set; } = string.Empty;
}
