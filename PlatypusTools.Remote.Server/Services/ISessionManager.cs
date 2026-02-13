using PlatypusTools.Remote.Server.Models;

namespace PlatypusTools.Remote.Server.Services;

/// <summary>
/// Interface for managing authenticated user sessions.
/// </summary>
public interface ISessionManager
{
    Task RegisterSessionAsync(string userId, SessionInfo session);
    Task<IReadOnlyList<SessionInfo>> GetUserSessionsAsync(string userId);
    Task EndSessionAsync(string userId, string sessionId);
    Task EndAllSessionsAsync(string userId);
}
