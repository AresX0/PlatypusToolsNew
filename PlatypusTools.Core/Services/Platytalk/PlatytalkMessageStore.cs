using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Encrypted-at-rest local message store backed by System.Data.SQLite.
    ///
    /// Each message body is sealed by <see cref="LocalSecretBox"/> (DPAPI on
    /// Windows, AES-GCM with a per-install key elsewhere) before being written
    /// to disk. Metadata (id, conversation, sender, timestamps, status) is
    /// stored in the clear so we can index, sort, expire, and tombstone
    /// without paying the unwrap cost on every query.
    /// </summary>
    internal sealed class PlatytalkMessageStore : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _dataDir;
        private readonly object _gate = new();
        private SQLiteConnection? _conn;

        public PlatytalkMessageStore(string dataDir)
        {
            _dataDir = dataDir;
            _dbPath = Path.Combine(dataDir, "messages.db");
            Initialize();
        }

        private void Initialize()
        {
            Directory.CreateDirectory(_dataDir);
            var connStr = new SQLiteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Version = 3,
                Pooling = false,
                JournalMode = SQLiteJournalModeEnum.Wal,
                ForeignKeys = false,
                BusyTimeout = 5000,
            }.ToString();
            _conn = new SQLiteConnection(connStr);
            _conn.Open();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS messages (
    message_id TEXT PRIMARY KEY,
    conversation_id TEXT NOT NULL,
    sender_id TEXT NOT NULL,
    direction INTEGER NOT NULL,
    timestamp_utc TEXT NOT NULL,
    expires_utc TEXT,
    status INTEGER NOT NULL,
    is_tombstoned INTEGER NOT NULL DEFAULT 0,
    sealed_body BLOB
);
CREATE INDEX IF NOT EXISTS idx_messages_conv_ts ON messages(conversation_id, timestamp_utc);
CREATE INDEX IF NOT EXISTS idx_messages_expires ON messages(expires_utc);
";
            cmd.ExecuteNonQuery();
        }

        public void UpsertMessage(PlatytalkMessage msg)
        {
            if (_conn is null) return;
            byte[]? sealed_ = null;
            if (!msg.IsTombstoned && !string.IsNullOrEmpty(msg.Body))
            {
                var plain = Encoding.UTF8.GetBytes(msg.Body);
                sealed_ = LocalSecretBox.Protect(plain, _dataDir);
            }
            lock (_gate)
            {
                using var tx = _conn.BeginTransaction();
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO messages (message_id, conversation_id, sender_id, direction, timestamp_utc, expires_utc, status, is_tombstoned, sealed_body)
VALUES ($id, $conv, $sender, $dir, $ts, $exp, $status, $tomb, $body)
ON CONFLICT(message_id) DO UPDATE SET
    status = excluded.status,
    expires_utc = excluded.expires_utc,
    is_tombstoned = excluded.is_tombstoned,
    sealed_body = CASE WHEN excluded.is_tombstoned = 1 THEN NULL ELSE excluded.sealed_body END;";
                cmd.Parameters.AddWithValue("$id", msg.MessageId);
                cmd.Parameters.AddWithValue("$conv", msg.ConversationId);
                cmd.Parameters.AddWithValue("$sender", msg.SenderId);
                cmd.Parameters.AddWithValue("$dir", (int)msg.Direction);
                cmd.Parameters.AddWithValue("$ts", msg.TimestampUtc.ToString("O"));
                cmd.Parameters.AddWithValue("$exp", (object?)msg.ExpiresUtc?.ToString("O") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$status", (int)msg.Status);
                cmd.Parameters.AddWithValue("$tomb", msg.IsTombstoned ? 1 : 0);
                cmd.Parameters.AddWithValue("$body", (object?)sealed_ ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
        }

        public IReadOnlyList<PlatytalkMessage> LoadMessages(string conversationId, int limit = 500)
        {
            var result = new List<PlatytalkMessage>();
            if (_conn is null) return result;
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"SELECT message_id, sender_id, direction, timestamp_utc, expires_utc, status, is_tombstoned, sealed_body
FROM messages WHERE conversation_id = $conv ORDER BY timestamp_utc ASC LIMIT $lim;";
                cmd.Parameters.AddWithValue("$conv", conversationId);
                cmd.Parameters.AddWithValue("$lim", limit);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var tomb = rdr.GetInt32(6) != 0;
                    string body = string.Empty;
                    if (!tomb && !rdr.IsDBNull(7))
                    {
                        try
                        {
                            var sealed_ = (byte[])rdr.GetValue(7);
                            var plain = LocalSecretBox.Unprotect(sealed_, _dataDir);
                            body = Encoding.UTF8.GetString(plain);
                        }
                        catch { body = string.Empty; }
                    }
                    var msg = new PlatytalkMessage
                    {
                        MessageId = rdr.GetString(0),
                        ConversationId = conversationId,
                        SenderId = rdr.GetString(1),
                        Direction = (MessageDirection)rdr.GetInt32(2),
                        Body = body,
                        Status = (MessageStatus)rdr.GetInt32(5),
                        IsTombstoned = tomb,
                    };
                    if (!rdr.IsDBNull(4))
                        msg.ExpiresUtc = DateTime.Parse(rdr.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind);
                    result.Add(msg);
                }
            }
            return result;
        }

        public IReadOnlyList<string> ListConversationIds()
        {
            var result = new List<string>();
            if (_conn is null) return result;
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT conversation_id FROM messages;";
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read()) result.Add(rdr.GetString(0));
            }
            return result;
        }

        public int PurgeExpired(DateTime nowUtc)
        {
            if (_conn is null) return 0;
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "DELETE FROM messages WHERE expires_utc IS NOT NULL AND expires_utc < $now;";
                cmd.Parameters.AddWithValue("$now", nowUtc.ToString("O"));
                return cmd.ExecuteNonQuery();
            }
        }

        public void Tombstone(string messageId)
        {
            if (_conn is null) return;
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "UPDATE messages SET is_tombstoned = 1, sealed_body = NULL, status = $status WHERE message_id = $id;";
                cmd.Parameters.AddWithValue("$status", (int)MessageStatus.RemotelyDeleted);
                cmd.Parameters.AddWithValue("$id", messageId);
                cmd.ExecuteNonQuery();
            }
        }

        public void Wipe()
        {
            if (_conn is null) return;
            lock (_gate)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "DELETE FROM messages;";
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            try { _conn?.Close(); _conn?.Dispose(); } catch { }
            _conn = null;
        }
    }
}
