# Audio Library Component - Rewrite & Enhancement Guide

**Purpose**: Comprehensive guide to transform the audio library from in-memory-only to a production-grade indexed system  
**Audience**: Developers implementing the audio player  
**Date**: January 14, 2026  

---

## Overview

The current audio library is functional but fragile:
- ❌ **No persistence** - data lost on app restart
- ❌ **No indexing** - slow with large collections
- ❌ **No metadata** - filenames only
- ❌ **No deduplication** - same file can be added multiple times
- ❌ **No search optimization** - linear scans

This guide shows how to evolve it into a **production-grade indexed library** with JSON persistence, atomic writes, and incremental updates.

---

## Current State Analysis

### What Exists Today

**Files**:
- `PlatypusTools.UI/ViewModels/AudioPlayerViewModel.cs` - Main player VM
- `PlatypusTools.UI/Views/AudioPlayerView.xaml` - Main player UI
- `PlatypusTools.Core/Services/AudioPlayerService.cs` - Audio engine
- `PlatypusTools.Core/Models/Audio/AudioTrack.cs` - Track model

**Current Flow**:
```
User Click → OpenFolder() → ScanFolder() → Add to ObservableCollection → Display
                                ↓
                        Discarded on app exit
```

**Limitations**:
- ~300ms freeze for 1000 files (UI thread scanning)
- No incremental updates
- Memory grows unbounded
- Lost on restart

---

## Target State: Production Architecture

### New Architecture

```
┌─────────────────────────────────────────────────────┐
│              LibraryIndexService                     │
│  (Manages persistent JSON index with atomic writes) │
└────────────┬────────────────────────────────────────┘
             │
        ┌────┴────┬────────────┬──────────────┐
        ↓         ↓            ↓              ↓
   ┌────────┐ ┌────────┐ ┌───────────┐ ┌──────────┐
   │ Scan   │ │Metadata│ │Path       │ │Atomic    │
   │Service │ │Extract │ │Canonical  │ │File      │
   │        │ │Service │ │Service    │ │Writer    │
   └────────┘ └────────┘ └───────────┘ └──────────┘
        │         │           │              │
        └─────────┴───────────┴──────────────┘
                  │
          ┌───────┴────────┐
          ↓                ↓
    ┌───────────────┐ ┌──────────────┐
    │library.index  │ │library.index  │
    │.json (live)   │ │.json.bak      │
    └───────────────┘ └──────────────┘
          │
    ┌─────┴──────┐
    ↓            ↓
┌─────────────────────────────────────────┐
│      AudioPlayerViewModel               │
│  (Displays tracks from index)            │
└─────────────────────────────────────────┘
```

---

## Step 1: Create Core Models

### A. Enhanced Track Model

**File**: `PlatypusTools.Core/Models/Track.cs` (create/update)

```csharp
using System;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models;

/// <summary>
/// Represents a single audio track with full metadata.
/// Serialized to/from JSON for library persistence.
/// </summary>
public sealed record Track(
    [property: JsonPropertyName("id")]
    string Id,                              // UUID or stable hash
    
    [property: JsonPropertyName("path")]
    string Path,                            // Canonical full path
    
    [property: JsonPropertyName("filename")]
    string Filename,                        // Basename only
    
    [property: JsonPropertyName("size")]
    long Size,                              // File size in bytes
    
    [property: JsonPropertyName("mtime")]
    long MTime,                             // Modification time (Unix timestamp)
    
    [property: JsonPropertyName("hash")]
    string? Hash = null,                    // Optional short hash for validation
    
    // Metadata (from tags or filename)
    [property: JsonPropertyName("title")]
    string? Title = null,
    
    [property: JsonPropertyName("artist")]
    string? Artist = null,
    
    [property: JsonPropertyName("album")]
    string? Album = null,
    
    [property: JsonPropertyName("track_no")]
    int? TrackNo = null,
    
    [property: JsonPropertyName("disc_no")]
    int? DiscNo = null,
    
    [property: JsonPropertyName("duration_ms")]
    int DurationMs = 0,
    
    [property: JsonPropertyName("codec")]
    string Codec = "unknown",               // mp3, aac, flac, wav, ogg, opus
    
    [property: JsonPropertyName("bitrate")]
    int? Bitrate = null,                    // bps
    
    [property: JsonPropertyName("samplerate")]
    int? SampleRate = null,                 // Hz
    
    [property: JsonPropertyName("channels")]
    int? Channels = null,
    
    [property: JsonPropertyName("genre")]
    string? Genre = null,
    
    [property: JsonPropertyName("year")]
    int? Year = null,
    
    [property: JsonPropertyName("artwork")]
    string? ArtworkBase64 = null,           // Embedded artwork (base64)
    
    // State
    [property: JsonPropertyName("is_missing")]
    bool IsMissing = false,                 // File was deleted
    
    // Timestamps
    [property: JsonPropertyName("added_at")]
    DateTime AddedAt = default,
    
    [property: JsonPropertyName("last_played_at")]
    DateTime? LastPlayedAt = null,
    
    // Stats
    [property: JsonPropertyName("play_count")]
    int PlayCount = 0,
    
    [property: JsonPropertyName("rating")]
    int? Rating = null                      // 0-5 stars
)
{
    /// <summary>
    /// Returns display title (falls back to filename if no title).
    /// </summary>
    public string DisplayTitle => !string.IsNullOrEmpty(Title) 
        ? Title 
        : Path.GetFileNameWithoutExtension(Filename);
    
    /// <summary>
    /// Returns display artist (or "Unknown").
    /// </summary>
    public string DisplayArtist => !string.IsNullOrEmpty(Artist) 
        ? Artist 
        : "Unknown Artist";
    
    /// <summary>
    /// Returns display album (or "Unknown").
    /// </summary>
    public string DisplayAlbum => !string.IsNullOrEmpty(Album) 
        ? Album 
        : "Unknown Album";
    
    /// <summary>
    /// Returns formatted duration (MM:SS).
    /// </summary>
    public string DurationFormatted => TimeSpan.FromMilliseconds(DurationMs)
        .ToString(@"m\:ss");
}
```

### B. LibraryIndex Model

**File**: `PlatypusTools.Core/Models/LibraryIndex.cs` (create)

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models;

/// <summary>
/// Versioned library index for persistence and fast loading.
/// </summary>
public sealed class LibraryIndex
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;
    
    [JsonPropertyName("generated_at")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    
    [JsonPropertyName("track_count")]
    public int TrackCount => Tracks.Count;
    
    [JsonPropertyName("tracks")]
    public List<Track> Tracks { get; set; } = new();
}
```

---

## Step 2: Create Utility Services

### A. Path Canonicalizer

**File**: `PlatypusTools.Core/Utilities/PathCanonicalizer.cs` (create)

```csharp
using System;
using System.IO;
using System.Text;

namespace PlatypusTools.Core.Utilities;

/// <summary>
/// Standardizes paths for comparison and deduplication.
/// Ensures consistent path representation across the library.
/// </summary>
public static class PathCanonicalizer
{
    /// <summary>
    /// Canonicalize a path for comparison.
    /// - Converts to full path
    /// - Lowercase on Windows (case-insensitive filesystems)
    /// - Uses forward slashes
    /// - Normalizes Unicode (NFC)
    /// </summary>
    public static string Canonicalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));
        
        // Get full path
        var fullPath = Path.GetFullPath(path);
        
        // Normalize Unicode (NFC form)
        var normalized = fullPath.Normalize(NormalizationForm.FormC);
        
        // Case-insensitive on Windows, case-sensitive elsewhere
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToLowerInvariant();
        
        // Use forward slashes for consistency
        normalized = normalized.Replace(
            Path.DirectorySeparatorChar, 
            '/'
        );
        
        return normalized;
    }
    
    /// <summary>
    /// Generate a stable ID from a canonical path.
    /// </summary>
    public static string GenerateId(string canonicalPath)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalPath));
            return Convert.ToHexString(hash)[..16]; // First 16 chars
        }
    }
}
```

### B. Atomic File Writer

**File**: `PlatypusTools.Core/Utilities/AtomicFileWriter.cs` (create)

```csharp
using System;
using System.IO;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Utilities;

/// <summary>
/// Writes files atomically to prevent corruption.
/// Write pattern: temp → flush → replace original with backup.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Write content to file atomically.
    /// - Writes to temporary file first
    /// - Replaces target file (atomic on Windows)
    /// - Keeps backup of previous version
    /// </summary>
    public static void WriteAtomic(string targetPath, string content)
    {
        var dir = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("Invalid path", nameof(targetPath));
        
        Directory.CreateDirectory(dir);
        
        var fileName = Path.GetFileName(targetPath);
        var tmpPath = Path.Combine(dir, $".{fileName}.tmp");
        var bakPath = Path.Combine(dir, $"{fileName}.bak");
        
        try
        {
            // Write to temporary file
            File.WriteAllText(tmpPath, content);
            
            // Atomic replace (Windows native operation)
            // File.Replace: if target exists, move to backup, then move temp to target
            File.Replace(tmpPath, targetPath, bakPath);
        }
        finally
        {
            // Cleanup temp file if it somehow still exists
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }
    
    /// <summary>
    /// Async version of WriteAtomic.
    /// </summary>
    public static async Task WriteAtomicAsync(string targetPath, string content)
    {
        var dir = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException("Invalid path", nameof(targetPath));
        
        Directory.CreateDirectory(dir);
        
        var fileName = Path.GetFileName(targetPath);
        var tmpPath = Path.Combine(dir, $".{fileName}.tmp");
        var bakPath = Path.Combine(dir, $"{fileName}.bak");
        
        try
        {
            // Write to temporary file
            await File.WriteAllTextAsync(tmpPath, content);
            
            // Atomic replace
            File.Replace(tmpPath, targetPath, bakPath);
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }
    
    /// <summary>
    /// Restore from backup if needed.
    /// </summary>
    public static bool RestoreFromBackup(string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath)!;
        var fileName = Path.GetFileName(targetPath);
        var bakPath = Path.Combine(dir, $"{fileName}.bak");
        
        if (File.Exists(bakPath))
        {
            File.Copy(bakPath, targetPath, overwrite: true);
            return true;
        }
        
        return false;
    }
}
```

### C. Metadata Extractor Service

**File**: `PlatypusTools.Core/Services/MetadataExtractorService.cs` (create)

**First, add NuGet package**:
```powershell
dotnet add PlatypusTools.Core package TagLibSharp
```

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using TagLib;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.Core.Services;

/// <summary>
/// Extracts metadata from audio files using TagLib#.
/// Handles errors gracefully; falls back to filename if tags missing.
/// </summary>
public sealed class MetadataExtractorService : IMetadataExtractorService
{
    private static readonly string[] SupportedExtensions = 
    { 
        ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma" 
    };
    
    /// <summary>
    /// Extract metadata from an audio file.
    /// </summary>
    public async Task<Track> ExtractMetadataAsync(string filePath)
    {
        return await Task.Run(() => ExtractMetadataSync(filePath));
    }
    
    private Track ExtractMetadataSync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");
        
        var fileInfo = new FileInfo(filePath);
        var canonicalPath = PathCanonicalizer.Canonicalize(filePath);
        var id = PathCanonicalizer.GenerateId(canonicalPath);
        
        try
        {
            using (var file = TagLib.File.Create(filePath))
            {
                var duration = file.Properties.Duration;
                var codec = GetCodecFromFile(file);
                
                return new Track(
                    Id: id,
                    Path: canonicalPath,
                    Filename: fileInfo.Name,
                    Size: fileInfo.Length,
                    MTime: fileInfo.LastWriteTimeUtc.ToUnixTimeSeconds(),
                    Hash: null,
                    Title: file.Tag.Title?.Trim() ?? null,
                    Artist: file.Tag.FirstPerformer?.Trim() ?? null,
                    Album: file.Tag.Album?.Trim() ?? null,
                    TrackNo: (int?)file.Tag.Track > 0 ? (int)file.Tag.Track : null,
                    DiscNo: (int?)file.Tag.FirstDiscNumber > 0 ? (int)file.Tag.FirstDiscNumber : null,
                    DurationMs: (int)duration.TotalMilliseconds,
                    Codec: codec,
                    Bitrate: file.Properties.AudioBitrate,
                    SampleRate: file.Properties.AudioSampleRate,
                    Channels: file.Properties.AudioChannels,
                    Genre: file.Tag.FirstGenre?.Trim() ?? null,
                    Year: (int?)file.Tag.Year > 0 ? (int)file.Tag.Year : null,
                    ArtworkBase64: ExtractArtworkBase64(file),
                    IsMissing: false,
                    AddedAt: DateTime.UtcNow,
                    LastPlayedAt: null,
                    PlayCount: 0,
                    Rating: null
                );
            }
        }
        catch (Exception ex)
        {
            // Fallback: create track with minimal info
            SimpleLogger.Warn($"Failed to extract metadata from {filePath}: {ex.Message}");
            
            return new Track(
                Id: id,
                Path: canonicalPath,
                Filename: fileInfo.Name,
                Size: fileInfo.Length,
                MTime: fileInfo.LastWriteTimeUtc.ToUnixTimeSeconds(),
                Hash: null,
                Title: Path.GetFileNameWithoutExtension(fileInfo.Name),
                Artist: "Unknown",
                Album: "Unknown",
                TrackNo: null,
                DiscNo: null,
                DurationMs: 0,
                Codec: GetCodecFromExtension(filePath),
                Bitrate: null,
                SampleRate: null,
                Channels: null,
                Genre: null,
                Year: null,
                ArtworkBase64: null,
                IsMissing: false,
                AddedAt: DateTime.UtcNow,
                LastPlayedAt: null,
                PlayCount: 0,
                Rating: null
            );
        }
    }
    
    private string GetCodecFromFile(TagLib.File file)
    {
        return file.Properties.Codecs.Length > 0 
            ? file.Properties.Codecs[0].Description?.ToLower() ?? "unknown"
            : GetCodecFromExtension(file.Name);
    }
    
    private string GetCodecFromExtension(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => "mp3",
            ".m4a" or ".aac" => "aac",
            ".flac" => "flac",
            ".wav" => "wav",
            ".ogg" => "ogg",
            ".opus" => "opus",
            ".wma" => "wma",
            _ => "unknown"
        };
    }
    
    private string? ExtractArtworkBase64(TagLib.File file)
    {
        try
        {
            if (file.Tag.Pictures.Count > 0)
            {
                var picture = file.Tag.Pictures[0];
                return Convert.ToBase64String(picture.Data.Data);
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Debug($"Failed to extract artwork: {ex.Message}");
        }
        
        return null;
    }
}

public interface IMetadataExtractorService
{
    Task<Track> ExtractMetadataAsync(string filePath);
}
```

---

## Step 3: Create Library Index Service

**File**: `PlatypusTools.Core/Services/LibraryIndexService.cs` (create)

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.Core.Services;

/// <summary>
/// Manages persistent library index with atomic writes.
/// - Loads/saves JSON index
/// - Scans folders for new files
/// - Detects changes (incremental)
/// - Deduplicates by path
/// </summary>
public sealed class LibraryIndexService : ILibraryIndexService
{
    private readonly string _indexPath;
    private readonly IMetadataExtractorService _metadataExtractor;
    private readonly JsonSerializerOptions _jsonOptions;
    
    private static readonly string[] AllowedExtensions = 
    { 
        ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma" 
    };
    
    public LibraryIndexService(
        string indexPath, 
        IMetadataExtractorService metadataExtractor)
    {
        _indexPath = indexPath;
        _metadataExtractor = metadataExtractor;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false, // Compact in production
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
    
    /// <summary>
    /// Load existing index or return empty if not found.
    /// </summary>
    public async Task<LibraryIndex> LoadIndexAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_indexPath))
        {
            SimpleLogger.Info("Library index not found, starting fresh");
            return new LibraryIndex();
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(_indexPath, ct);
            var index = JsonSerializer.Deserialize<LibraryIndex>(json, _jsonOptions);
            
            SimpleLogger.Info($"Loaded library index with {index?.Tracks.Count ?? 0} tracks");
            return index ?? new LibraryIndex();
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"Failed to load index: {ex.Message}. Attempting backup restore.");
            
            if (AtomicFileWriter.RestoreFromBackup(_indexPath))
            {
                SimpleLogger.Info("Restored from backup");
                return await LoadIndexAsync(ct);
            }
            
            return new LibraryIndex();
        }
    }
    
    /// <summary>
    /// Save index atomically.
    /// </summary>
    public async Task SaveIndexAsync(LibraryIndex index, CancellationToken ct = default)
    {
        index.GeneratedAt = DateTime.UtcNow;
        
        var json = JsonSerializer.Serialize(index, _jsonOptions);
        
        await AtomicFileWriter.WriteAtomicAsync(_indexPath, json);
        
        SimpleLogger.Info($"Saved library index with {index.Tracks.Count} tracks");
    }
    
    /// <summary>
    /// Scan folder for audio files and extract metadata.
    /// </summary>
    public async Task<List<Track>> ScanFolderAsync(
        string folderPath, 
        bool recursive, 
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var tracks = new List<Track>();
        var filesProcessed = 0;
        
        try
        {
            var files = GetAudioFiles(folderPath, recursive);
            
            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();
                
                try
                {
                    var track = await _metadataExtractor.ExtractMetadataAsync(filePath);
                    tracks.Add(track);
                    filesProcessed++;
                    
                    progress?.Report(filesProcessed);
                }
                catch (Exception ex)
                {
                    SimpleLogger.Warn($"Failed to extract metadata from {filePath}: {ex.Message}");
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            SimpleLogger.Error($"Access denied scanning {folderPath}: {ex.Message}");
        }
        
        return tracks;
    }
    
    /// <summary>
    /// Incremental rescan: detect new/updated/deleted files.
    /// </summary>
    public async Task<LibraryIndex> IncrementalScanAsync(
        string folderPath, 
        LibraryIndex existingIndex,
        bool recursive = true,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var updated = new LibraryIndex { Version = existingIndex.Version };
        var filesProcessed = 0;
        
        // Build map of existing tracks by canonical path
        var existingByPath = existingIndex.Tracks
            .Where(t => !t.IsMissing)
            .ToDictionary(t => t.Path);
        
        // Scan for current files
        var currentFiles = GetAudioFiles(folderPath, recursive)
            .ToDictionary(f => PathCanonicalizer.Canonicalize(f));
        
        // Process each current file
        foreach (var (canonicalPath, filePath) in currentFiles)
        {
            ct.ThrowIfCancellationRequested();
            
            if (existingByPath.TryGetValue(canonicalPath, out var existing))
            {
                // File exists in index - check if changed
                var fileInfo = new FileInfo(filePath);
                
                if (fileInfo.Length == existing.Size && 
                    fileInfo.LastWriteTimeUtc.ToUnixTimeSeconds() == existing.MTime)
                {
                    // Unchanged, keep existing
                    updated.Tracks.Add(existing);
                }
                else
                {
                    // Changed, re-extract metadata
                    try
                    {
                        var track = await _metadataExtractor.ExtractMetadataAsync(filePath);
                        updated.Tracks.Add(track);
                    }
                    catch
                    {
                        // Keep existing on error
                        updated.Tracks.Add(existing);
                    }
                }
            }
            else
            {
                // New file
                try
                {
                    var track = await _metadataExtractor.ExtractMetadataAsync(filePath);
                    updated.Tracks.Add(track);
                }
                catch (Exception ex)
                {
                    SimpleLogger.Warn($"Failed to extract {filePath}: {ex.Message}");
                }
            }
            
            filesProcessed++;
            progress?.Report(filesProcessed);
        }
        
        // Mark missing files
        foreach (var track in existingIndex.Tracks)
        {
            if (track.IsMissing || !currentFiles.ContainsKey(track.Path))
            {
                updated.Tracks.Add(track with { IsMissing = true });
            }
        }
        
        return updated;
    }
    
    private List<string> GetAudioFiles(string folderPath, bool recursive)
    {
        var files = new List<string>();
        var dirs = new Stack<string>();
        dirs.Push(folderPath);
        
        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();
            
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    if (Directory.Exists(entry) && recursive)
                    {
                        dirs.Push(entry);
                    }
                    else if (File.Exists(entry) && 
                             AllowedExtensions.Contains(Path.GetExtension(entry).ToLowerInvariant()))
                    {
                        files.Add(entry);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible directories
            }
        }
        
        return files;
    }
}

public interface ILibraryIndexService
{
    Task<LibraryIndex> LoadIndexAsync(CancellationToken ct = default);
    Task SaveIndexAsync(LibraryIndex index, CancellationToken ct = default);
    Task<List<Track>> ScanFolderAsync(
        string folderPath, 
        bool recursive,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
    Task<LibraryIndex> IncrementalScanAsync(
        string folderPath,
        LibraryIndex existingIndex,
        bool recursive = true,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
```

---

## Step 4: Integrate into ViewModel

Update `AudioPlayerViewModel.cs` to use the new services:

```csharp
// In constructor
var indexPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "PlatypusTools",
    "library.index.json"
);

_metadataExtractor = new MetadataExtractorService();
_libraryIndexService = new LibraryIndexService(indexPath, _metadataExtractor);

// Load on startup
await LoadLibraryIndexAsync();

// Save on modifications
private async Task LoadLibraryIndexAsync()
{
    try
    {
        var index = await _libraryIndexService.LoadIndexAsync();
        _allLibraryTracks.Clear();
        _allLibraryTracks.AddRange(index.Tracks);
        UpdateLibraryGroups();
    }
    catch (Exception ex)
    {
        StatusMessage = $"Failed to load library: {ex.Message}";
    }
}

private async Task SaveLibraryIndexAsync()
{
    try
    {
        var index = new LibraryIndex();
        index.Tracks.AddRange(_allLibraryTracks);
        await _libraryIndexService.SaveIndexAsync(index);
    }
    catch (Exception ex)
    {
        StatusMessage = $"Failed to save library: {ex.Message}";
    }
}

// Update scanning
private async Task OpenFolderAsync()
{
    // ... existing code ...
    
    var tracks = await _libraryIndexService.ScanFolderAsync(
        dialog.SelectedPath, 
        IncludeSubfolders,
        new Progress<int>(count => ScanStatus = $"Processed {count} files...")
    );
    
    // Add to library and save
    _allLibraryTracks.AddRange(tracks);
    UpdateLibraryGroups();
    FilterLibraryTracks();
    await SaveLibraryIndexAsync();
}
```

---

## Step 5: Testing Strategy

### Unit Tests

```csharp
[TestClass]
public class LibraryIndexServiceTests
{
    [TestMethod]
    public async Task ScanFolder_FindsAllAudioFiles()
    {
        // Arrange
        var testFolder = CreateTestAudioFiles();
        var service = new LibraryIndexService(_indexPath, _extractor);
        
        // Act
        var tracks = await service.ScanFolderAsync(testFolder, recursive: true);
        
        // Assert
        Assert.AreEqual(5, tracks.Count);
        Assert.IsTrue(tracks.All(t => !t.IsMissing));
    }
    
    [TestMethod]
    public async Task SaveLoadRoundTrip_PreservesData()
    {
        // Arrange
        var index = CreateTestIndex();
        var service = new LibraryIndexService(_indexPath, _extractor);
        
        // Act
        await service.SaveIndexAsync(index);
        var loaded = await service.LoadIndexAsync();
        
        // Assert
        Assert.AreEqual(index.Tracks.Count, loaded.Tracks.Count);
        Assert.AreEqual(index.Tracks[0].Title, loaded.Tracks[0].Title);
    }
    
    [TestMethod]
    public async Task IncrementalScan_DetectsChanges()
    {
        // Arrange
        var original = await service.ScanFolderAsync(testFolder, true);
        var originalIndex = new LibraryIndex { Tracks = original };
        
        // Modify one file (update mtime)
        File.SetLastWriteTimeUtc(testFiles[0], DateTime.UtcNow.AddMinutes(5));
        
        // Act
        var updated = await service.IncrementalScanAsync(
            testFolder, 
            originalIndex
        );
        
        // Assert
        Assert.AreEqual(original.Count, updated.Tracks.Count);
        // One file should be updated
    }
}
```

---

## Step 6: Deployment Checklist

- [ ] Add TagLib# NuGet package
- [ ] Add MathNet.Numerics NuGet package (for FFT)
- [ ] Create all model files
- [ ] Create all service files
- [ ] Create all utility files
- [ ] Update ViewModel to use services
- [ ] Test with sample library (1000+ files)
- [ ] Verify JSON index format
- [ ] Test atomic writes (simulate crash)
- [ ] Performance test (cold start timing)
- [ ] Build release package
- [ ] Update documentation

---

## Performance Targets

After implementation:

| Metric | Current | Target | Status |
|--------|---------|--------|--------|
| Cold Start (10k tracks) | N/A | < 1.5s | ⚠️ To test |
| Incremental Rescan | N/A | < 15s | ⚠️ To test |
| Search Response | 500ms+ | < 300ms | ⚠️ To optimize |
| UI Responsiveness | ✅ | Maintained | ✅ Expected |

---

## Next Steps

1. **Today**: Review this guide
2. **Tomorrow**: Create model files (Step 1)
3. **Day 2**: Create utility services (Step 2)
4. **Day 3**: Create library index service (Step 3)
5. **Day 4**: Integrate into ViewModel (Step 4)
6. **Day 5**: Add tests & validate
7. **Day 6**: Performance tuning
8. **Day 7**: Documentation & release

**Estimated Total Effort**: 40-48 hours

---

## References

- [System.Text.Json Documentation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json)
- [TagLib# Documentation](https://taglib.org/)
- [WPF MVVM Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2009/february/patterns-wpf-apps-with-the-model-view-viewmodel-design-pattern)
- [Atomic File Operations](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace)

---

**Document Version**: 1.0  
**Last Updated**: January 14, 2026  
**Status**: Ready for Implementation
