using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Audit logging service for tracking remote client actions.
/// Logs all commands from remote clients (play, pause, skip, seek, volume, etc.)
/// with timestamps, client info, and action details.
/// </summary>
public class RemoteAuditLogService : IDisposable
{
    private static readonly Lazy<RemoteAuditLogService> _instance = new(() => new RemoteAuditLogService());
    public static RemoteAuditLogService Instance => _instance.Value;

    private readonly ConcurrentQueue<AuditLogEntry> _pendingWrites = new();
    private readonly List<AuditLogEntry> _recentEntries = new();
    private readonly object _lock = new();
    private readonly string _logDirectory;
    private readonly Timer _flushTimer;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isEnabled = true;
    private const int MAX_RECENT_ENTRIES = 500;
    private const int FLUSH_INTERVAL_MS = 5000;

    /// <summary>
    /// Fired when a new audit entry is logged.
    /// </summary>
    public event EventHandler<AuditLogEntry>? EntryLogged;

    /// <summary>
    /// Gets or sets whether audit logging is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// Gets recent log entries (up to 500).
    /// </summary>
    public IReadOnlyList<AuditLogEntry> RecentEntries
    {
        get
        {
            lock (_lock)
            {
                return _recentEntries.ToList().AsReadOnly();
            }
        }
    }

    public RemoteAuditLogService()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "RemoteAuditLogs");
        Directory.CreateDirectory(_logDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Flush pending writes every 5 seconds
        _flushTimer = new Timer(_ => FlushAsync().ConfigureAwait(false), null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
    }

    /// <summary>
    /// Log a remote action.
    /// </summary>
    public void LogAction(string connectionId, string clientIp, string action, string? details = null, string? trackInfo = null)
    {
        if (!_isEnabled) return;

        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ConnectionId = connectionId,
            ClientIp = clientIp,
            Action = action,
            Details = details,
            TrackInfo = trackInfo
        };

        _pendingWrites.Enqueue(entry);

        lock (_lock)
        {
            _recentEntries.Add(entry);
            while (_recentEntries.Count > MAX_RECENT_ENTRIES)
                _recentEntries.RemoveAt(0);
        }

        EntryLogged?.Invoke(this, entry);
    }

    /// <summary>
    /// Log a connection event.
    /// </summary>
    public void LogConnection(string connectionId, string clientIp, string userAgent)
    {
        LogAction(connectionId, clientIp, "Connected", $"UserAgent: {userAgent}");
    }

    /// <summary>
    /// Log a disconnection event.
    /// </summary>
    public void LogDisconnection(string connectionId, string clientIp)
    {
        LogAction(connectionId, clientIp, "Disconnected");
    }

    /// <summary>
    /// Flush pending entries to disk.
    /// </summary>
    public async Task FlushAsync()
    {
        var entries = new List<AuditLogEntry>();
        while (_pendingWrites.TryDequeue(out var entry))
        {
            entries.Add(entry);
        }

        if (entries.Count == 0) return;

        try
        {
            var logFile = Path.Combine(_logDirectory, $"audit_{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.AppendLine(JsonSerializer.Serialize(entry, _jsonOptions));
            }

            await File.AppendAllTextAsync(logFile, sb.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteAuditLog] Error flushing: {ex.Message}");
        }
    }

    /// <summary>
    /// Get log entries for a specific date.
    /// </summary>
    public async Task<List<AuditLogEntry>> GetEntriesForDateAsync(DateTime date)
    {
        var logFile = Path.Combine(_logDirectory, $"audit_{date:yyyy-MM-dd}.jsonl");
        if (!File.Exists(logFile)) return new List<AuditLogEntry>();

        var entries = new List<AuditLogEntry>();
        var lines = await File.ReadAllLinesAsync(logFile);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<AuditLogEntry>(line, _jsonOptions);
                if (entry != null) entries.Add(entry);
            }
            catch { }
        }

        return entries;
    }

    /// <summary>
    /// Get summary statistics for a date range.
    /// </summary>
    public async Task<AuditSummary> GetSummaryAsync(DateTime from, DateTime to)
    {
        var summary = new AuditSummary { From = from, To = to };
        
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            var entries = await GetEntriesForDateAsync(date);
            summary.TotalActions += entries.Count;
            
            foreach (var entry in entries)
            {
                summary.ActionCounts.TryGetValue(entry.Action, out var count);
                summary.ActionCounts[entry.Action] = count + 1;

                if (!string.IsNullOrEmpty(entry.ClientIp) && !summary.UniqueClients.Contains(entry.ClientIp))
                    summary.UniqueClients.Add(entry.ClientIp);
            }
        }

        return summary;
    }

    /// <summary>
    /// Clean up logs older than the specified number of days.
    /// </summary>
    public int CleanupOldLogs(int daysToKeep = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
        var deleted = 0;

        try
        {
            foreach (var file in Directory.GetFiles(_logDirectory, "audit_*.jsonl"))
            {
                var fileDate = File.GetCreationTimeUtc(file);
                if (fileDate < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteAuditLog] Cleanup error: {ex.Message}");
        }

        return deleted;
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        FlushAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// A single audit log entry.
/// </summary>
public class AuditLogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; set; } = string.Empty;

    [JsonPropertyName("clientIp")]
    public string ClientIp { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("trackInfo")]
    public string? TrackInfo { get; set; }

    public override string ToString() => 
        $"[{Timestamp:HH:mm:ss}] {ClientIp} | {Action}{(Details != null ? $" | {Details}" : "")}";
}

/// <summary>
/// Summary statistics for audit logs.
/// </summary>
public class AuditSummary
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalActions { get; set; }
    public Dictionary<string, int> ActionCounts { get; set; } = new();
    public List<string> UniqueClients { get; set; } = new();
}
