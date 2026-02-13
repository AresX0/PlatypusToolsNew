using System.Collections.Concurrent;
using PlatypusTools.Remote.Server.Models;

namespace PlatypusTools.Remote.Server.Services;

/// <summary>
/// In-memory session manager for tracking connected clients.
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SessionInfo>> _sessions = new();
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    public Task RegisterSessionAsync(string userId, SessionInfo session)
    {
        var userSessions = _sessions.GetOrAdd(userId, _ => new ConcurrentDictionary<string, SessionInfo>());
        userSessions[session.SessionId] = session;
        
        _logger.LogInformation("Session registered: {SessionId} for user {UserId}", session.SessionId, userId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionInfo>> GetUserSessionsAsync(string userId)
    {
        if (_sessions.TryGetValue(userId, out var userSessions))
        {
            return Task.FromResult<IReadOnlyList<SessionInfo>>(userSessions.Values.ToList());
        }
        return Task.FromResult<IReadOnlyList<SessionInfo>>(Array.Empty<SessionInfo>());
    }

    public Task EndSessionAsync(string userId, string sessionId)
    {
        if (_sessions.TryGetValue(userId, out var userSessions))
        {
            userSessions.TryRemove(sessionId, out _);
            _logger.LogInformation("Session ended: {SessionId} for user {UserId}", sessionId, userId);
        }
        return Task.CompletedTask;
    }

    public Task EndAllSessionsAsync(string userId)
    {
        _sessions.TryRemove(userId, out _);
        _logger.LogInformation("All sessions ended for user {UserId}", userId);
        return Task.CompletedTask;
    }
}
