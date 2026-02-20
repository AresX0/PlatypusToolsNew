using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Cached audio fingerprint data.
    /// </summary>
    public class CachedAudioFingerprint
    {
        public double DurationSeconds { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitRate { get; set; }
        public string Codec { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string CombinedHash { get; set; } = string.Empty;
        public List<ulong> SpectralHashes { get; set; } = new();
        public List<double> RmsLevels { get; set; } = new();
    }

    /// <summary>
    /// Cached video fingerprint data.
    /// </summary>
    public class CachedVideoFingerprint
    {
        public double DurationSeconds { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public long FileSize { get; set; }
        public List<ulong> FrameHashes { get; set; } = new();
        public string CombinedHash { get; set; } = string.Empty;
    }

    /// <summary>
    /// Service for caching audio and video fingerprints to speed up subsequent scans.
    /// </summary>
    public class MediaFingerprintCacheService : IDisposable
    {
        private readonly string _databasePath;
        private SQLiteConnection? _connection;
        private bool _disposed;

        /// <summary>
        /// Creates a new MediaFingerprintCacheService with database in the specified folder.
        /// </summary>
        /// <param name="cacheFolder">Folder to store the cache database. If null, uses AppData.</param>
        public MediaFingerprintCacheService(string? cacheFolder = null)
        {
            var folder = cacheFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlatypusTools",
                "Cache");
            
            Directory.CreateDirectory(folder);
            _databasePath = Path.Combine(folder, "media_fingerprints.db");
        }

        /// <summary>
        /// Initializes the database and ensures tables exist.
        /// </summary>
        public async Task InitializeAsync()
        {
            var connectionString = $"Data Source={_databasePath};Version=3;";
            _connection = new SQLiteConnection(connectionString);
            await Task.Run(() => _connection.Open());

            // Create tables if they don't exist
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS AudioFingerprints (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    LastModified INTEGER NOT NULL,
                    ScanMode TEXT NOT NULL,
                    FingerprintData TEXT NOT NULL,
                    CreatedAt INTEGER NOT NULL,
                    UNIQUE(FilePath, FileSize, LastModified, ScanMode)
                );

                CREATE TABLE IF NOT EXISTS VideoFingerprints (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    LastModified INTEGER NOT NULL,
                    ScanMode TEXT NOT NULL,
                    FingerprintData TEXT NOT NULL,
                    CreatedAt INTEGER NOT NULL,
                    UNIQUE(FilePath, FileSize, LastModified, ScanMode)
                );

                CREATE INDEX IF NOT EXISTS idx_audio_lookup ON AudioFingerprints(FilePath, FileSize, LastModified, ScanMode);
                CREATE INDEX IF NOT EXISTS idx_video_lookup ON VideoFingerprints(FilePath, FileSize, LastModified, ScanMode);
            ";
            await Task.Run(() => cmd.ExecuteNonQuery());
        }

        /// <summary>
        /// Gets cached audio fingerprint if available.
        /// </summary>
        public async Task<CachedAudioFingerprint?> GetAudioFingerprintAsync(string filePath, AudioScanMode scanMode)
        {
            EnsureInitialized();

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return null;

                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    SELECT FingerprintData FROM AudioFingerprints 
                    WHERE FilePath = @path AND FileSize = @size AND LastModified = @modified AND ScanMode = @mode
                ";
                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.Parameters.AddWithValue("@size", fileInfo.Length);
                cmd.Parameters.AddWithValue("@modified", fileInfo.LastWriteTimeUtc.Ticks);
                cmd.Parameters.AddWithValue("@mode", scanMode.ToString());

                var result = await Task.Run(() => cmd.ExecuteScalar() as string);
                if (result == null) return null;

                return JsonSerializer.Deserialize<CachedAudioFingerprint>(result);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Stores an audio fingerprint in the cache.
        /// </summary>
        public async Task StoreAudioFingerprintAsync(string filePath, AudioScanMode scanMode, CachedAudioFingerprint fingerprint)
        {
            EnsureInitialized();

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return;

                var json = JsonSerializer.Serialize(fingerprint);

                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO AudioFingerprints (FilePath, FileSize, LastModified, ScanMode, FingerprintData, CreatedAt)
                    VALUES (@path, @size, @modified, @mode, @data, @created)
                ";
                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.Parameters.AddWithValue("@size", fileInfo.Length);
                cmd.Parameters.AddWithValue("@modified", fileInfo.LastWriteTimeUtc.Ticks);
                cmd.Parameters.AddWithValue("@mode", scanMode.ToString());
                cmd.Parameters.AddWithValue("@data", json);
                cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.Ticks);

                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch
            {
                // Cache failures shouldn't break scanning
            }
        }

        /// <summary>
        /// Gets cached video fingerprint if available.
        /// </summary>
        public async Task<CachedVideoFingerprint?> GetVideoFingerprintAsync(string filePath, VideoScanMode scanMode)
        {
            EnsureInitialized();

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return null;

                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    SELECT FingerprintData FROM VideoFingerprints 
                    WHERE FilePath = @path AND FileSize = @size AND LastModified = @modified AND ScanMode = @mode
                ";
                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.Parameters.AddWithValue("@size", fileInfo.Length);
                cmd.Parameters.AddWithValue("@modified", fileInfo.LastWriteTimeUtc.Ticks);
                cmd.Parameters.AddWithValue("@mode", scanMode.ToString());

                var result = await Task.Run(() => cmd.ExecuteScalar() as string);
                if (result == null) return null;

                return JsonSerializer.Deserialize<CachedVideoFingerprint>(result);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Stores a video fingerprint in the cache.
        /// </summary>
        public async Task StoreVideoFingerprintAsync(string filePath, VideoScanMode scanMode, CachedVideoFingerprint fingerprint)
        {
            EnsureInitialized();

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return;

                var json = JsonSerializer.Serialize(fingerprint);

                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO VideoFingerprints (FilePath, FileSize, LastModified, ScanMode, FingerprintData, CreatedAt)
                    VALUES (@path, @size, @modified, @mode, @data, @created)
                ";
                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.Parameters.AddWithValue("@size", fileInfo.Length);
                cmd.Parameters.AddWithValue("@modified", fileInfo.LastWriteTimeUtc.Ticks);
                cmd.Parameters.AddWithValue("@mode", scanMode.ToString());
                cmd.Parameters.AddWithValue("@data", json);
                cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.Ticks);

                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch
            {
                // Cache failures shouldn't break scanning
            }
        }

        /// <summary>
        /// Clears all cached fingerprints.
        /// </summary>
        public async Task ClearCacheAsync()
        {
            EnsureInitialized();

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM AudioFingerprints; DELETE FROM VideoFingerprints;";
            await Task.Run(() => cmd.ExecuteNonQuery());
        }

        /// <summary>
        /// Removes stale cache entries for files that no longer exist.
        /// </summary>
        public async Task CleanupStaleEntriesAsync()
        {
            EnsureInitialized();

            var stalePaths = new List<string>();

            // Find stale audio entries
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT FilePath FROM AudioFingerprints";
                using var reader = await Task.Run(() => cmd.ExecuteReader());
                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    if (!File.Exists(path))
                        stalePaths.Add(path);
                }
            }

            // Find stale video entries
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "SELECT DISTINCT FilePath FROM VideoFingerprints";
                using var reader = await Task.Run(() => cmd.ExecuteReader());
                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    if (!File.Exists(path))
                        stalePaths.Add(path);
                }
            }

            // Delete stale entries
            foreach (var path in stalePaths)
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM AudioFingerprints WHERE FilePath = @path; DELETE FROM VideoFingerprints WHERE FilePath = @path;";
                cmd.Parameters.AddWithValue("@path", path);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
        }

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        public async Task<(int AudioCount, int VideoCount, long DatabaseSizeBytes)> GetCacheStatisticsAsync()
        {
            EnsureInitialized();

            int audioCount = 0, videoCount = 0;

            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM AudioFingerprints";
                audioCount = Convert.ToInt32(await Task.Run(() => cmd.ExecuteScalar()));
            }

            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM VideoFingerprints";
                videoCount = Convert.ToInt32(await Task.Run(() => cmd.ExecuteScalar()));
            }

            var fileSize = File.Exists(_databasePath) ? new FileInfo(_databasePath).Length : 0;

            return (audioCount, videoCount, fileSize);
        }

        private void EnsureInitialized()
        {
            if (_connection == null)
            {
                // Synchronous initialization fallback
                var connectionString = $"Data Source={_databasePath};Version=3;";
                _connection = new SQLiteConnection(connectionString);
                _connection.Open();

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AudioFingerprints (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FilePath TEXT NOT NULL,
                        FileSize INTEGER NOT NULL,
                        LastModified INTEGER NOT NULL,
                        ScanMode TEXT NOT NULL,
                        FingerprintData TEXT NOT NULL,
                        CreatedAt INTEGER NOT NULL,
                        UNIQUE(FilePath, FileSize, LastModified, ScanMode)
                    );

                    CREATE TABLE IF NOT EXISTS VideoFingerprints (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FilePath TEXT NOT NULL,
                        FileSize INTEGER NOT NULL,
                        LastModified INTEGER NOT NULL,
                        ScanMode TEXT NOT NULL,
                        FingerprintData TEXT NOT NULL,
                        CreatedAt INTEGER NOT NULL,
                        UNIQUE(FilePath, FileSize, LastModified, ScanMode)
                    );

                    CREATE INDEX IF NOT EXISTS idx_audio_lookup ON AudioFingerprints(FilePath, FileSize, LastModified, ScanMode);
                    CREATE INDEX IF NOT EXISTS idx_video_lookup ON VideoFingerprints(FilePath, FileSize, LastModified, ScanMode);
                ";
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connection?.Close();
                _connection?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
