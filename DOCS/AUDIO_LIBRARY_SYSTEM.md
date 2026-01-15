# Audio Library System Implementation Guide

## Overview

This document describes the complete audio library system implemented for PlatypusTools. The system provides persistent library indexing, metadata extraction, searching, and organization capabilities for audio files.

## Architecture

### Layered Architecture

```
┌─────────────────────────────────────────┐
│  UI Layer (Phase 4)                     │
│  - MainWindow.xaml                      │
│  - Library scanning UI                  │
│  - Search UI                            │
│  - Organization mode selector           │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  ViewModel Layer (MVVM)                 │
│  - AudioPlayerViewModel                 │
│  - ObservableCollections for binding    │
│  - Command handling                     │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Service Layer                          │
│  - LibraryIndexService                  │
│  - MetadataExtractorService             │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Utility Layer                          │
│  - PathCanonicalizer                    │
│  - AtomicFileWriter                     │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Data Layer                             │
│  - Track model                          │
│  - LibraryIndex model                   │
│  - JSON persistence                     │
└─────────────────────────────────────────┘
```

## Core Components

### 1. Track Model
**File**: `PlatypusTools.Core/Models/Audio/Track.cs`

Enhanced audio metadata model with 30+ fields:
- **Identifiers**: Id (UUID), FilePath
- **File Metadata**: FileSize, LastModified
- **Audio Metadata**: Title, Artist, Album, AlbumArtist, Genre, Year
- **Track Info**: TrackNumber, TotalTracks, DiscNumber
- **Audio Properties**: DurationMs, Bitrate, SampleRate, Channels, Codec
- **Extended Metadata**: Genres (list), Composer, Conductor, Lyrics, Comments
- **Media**: HasArtwork, ArtworkThumbnail (base64, max 50KB)
- **Advanced**: ReplayGainAlbum, ReplayGainTrack
- **User Data**: MetadataExtractedAt, IsMarkedForDeletion, UserRating, PlayCount, LastPlayed

**Computed Properties**:
- `DisplayTitle` - Fallback to filename if not set
- `DisplayArtist` - Default to "Unknown Artist" if not set
- `DisplayAlbum` - Default to "Unknown Album" if not set
- `DurationFormatted` - MM:SS format
- `GetDeduplicationKey()` - For duplicate detection

### 2. LibraryIndex Model
**File**: `PlatypusTools.Core/Models/Audio/LibraryIndex.cs`

Versioned container for persistent library storage:

**Structure**:
```csharp
public class LibraryIndex
{
    public string Version { get; set; }                    // Semantic versioning
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public string ContentHash { get; set; }                // SHA256 for integrity
    public List<Track> Tracks { get; set; }
    public int TrackCount { get; set; }
    public long TotalSize { get; set; }
    public LibraryIndexSourceMetadata SourceMetadata { get; set; }
    public LibraryIndexStatistics Statistics { get; set; }
}
```

**Statistics** (auto-calculated):
- Artist count
- Album count
- Genre count
- Total duration
- Codec distribution
- Bitrate statistics
- Tracks with artwork
- Tracks with complete metadata

**Methods**:
- `RebuildIndices()` - Rebuild search indices
- `GetTracksByArtist(artist)` - Fast artist lookup
- `GetTracksByAlbum(album)` - Fast album lookup
- `FindTrackByPath(path)` - Find track by file path
- `SearchByTitle(query)` - Full-text search

### 3. PathCanonicalizer Utility
**File**: `PlatypusTools.Core/Utilities/PathCanonicalizer.cs`

Path normalization for reliable deduplication:

**Features**:
- **Unicode Normalization**: NFC (Canonical Decomposition, followed by Canonical Composition)
- **Case Handling**: Lowercase on Windows (case-insensitive), preserve on other platforms
- **Platform Compatibility**: Handles both / and \ separators
- **Deduplication**: Consistent keys for identical paths regardless of formatting

**Key Methods**:
- `Canonicalize(path)` - Full normalization
- `GetDeduplicationKey(path)` - Case-insensitive key
- `PathsEqual(path1, path2)` - Cross-platform comparison
- `IsSameFile(path1, path2)` - Full equivalence check
- `NormalizeToOS(path)` - OS-specific separator normalization

### 4. AtomicFileWriter Utility
**File**: `PlatypusTools.Core/Utilities/AtomicFileWriter.cs`

Safe file operations with atomicity guarantees:

**Write Pattern**:
1. Write to `.tmp` file
2. Flush to disk
3. Create backup of original (if exists)
4. Atomic file swap
5. Automatic cleanup on failure

**Key Methods**:
- `WriteTextAtomicAsync(path, content, encoding, keepBackup)` - Safe text write
- `WriteBinaryAtomicAsync(path, content, keepBackup)` - Safe binary write
- `RestoreFromBackup(path)` - Recover from `.backup`
- `BackupExists(path)` - Check backup availability
- `GetBackupPath(path)` - Get backup file path

**Error Handling**:
- Automatic `.tmp` cleanup on failure
- Backup recovery support
- Graceful exception handling

### 5. MetadataExtractorService
**File**: `PlatypusTools.Core/Services/MetadataExtractorService.cs`

Audio metadata extraction using TagLib#:

**Supported Formats** (9 formats):
- MP3, FLAC, OGG Vorbis, M4A, AAC
- WMA, WAV, Opus, APE

**Features**:
- **Synchronous**: `ExtractMetadata(filePath)` → Returns Track or null
- **Asynchronous**: `ExtractMetadataAsync(filePath)` → Task<Track>
- **Parallel**: `ExtractMetadataAsync(IEnumerable<string>, maxDegreeOfParallelism)` → Parallel extraction with SemaphoreSlim (default max 4 concurrent)
- **Quick Info**: `GetQuickInfo(filePath)` → Duration and bitrate only (faster)
- **Format Detection**: `IsAudioFile(filePath)` → Check if file is audio

**Error Handling**:
- Returns minimal Track data on metadata extraction failure
- Never throws exceptions
- Graceful degradation with fallback values

**Metadata Extracted**:
- Title, Artist, Album, AlbumArtist, Genre, Year
- TrackNumber, TotalTracks, DiscNumber
- Duration, Bitrate, SampleRate, Channels, Codec
- Comments, Composer, Lyrics
- Genres (as list)
- Artwork (base64 thumbnail, max 50KB)

### 6. LibraryIndexService
**File**: `PlatypusTools.Core/Services/LibraryIndexService.cs`

Core library management with JSON persistence:

**Default Index Location**: `%APPDATA%/PlatypusTools/audio_library_index.json`

**Key Methods**:

#### `LoadOrCreateIndexAsync()`
- Load existing index from disk
- Create new empty index if not found
- Validate SHA256 content hash
- Return LibraryIndex object

#### `ScanAndIndexDirectoryAsync(directory, recursive, progressCallback)`
- Recursively scan directory for audio files
- **Incremental**: Skip files already in index (by path)
- Extract metadata for new files
- Detect and skip duplicates
- Report progress: (current, total, status)
- Update library index
- Recalculate statistics
- Return count of new tracks added

#### `RemoveMissingFilesAsync(progressCallback)`
- Detect files no longer on disk
- Remove from index
- Report progress
- Update statistics
- Save index

#### `Search(query, searchType)`
- Full-text search across library
- Search types: Title, Artist, Album, All
- Case-insensitive search
- Return matching Track list

#### `GetAllArtists()` / `GetAllAlbums()`
- Get unique values from library
- Sorted alphabetically

#### `SaveIndexAsync()`
- Serialize index to JSON
- Calculate SHA256 content hash
- Atomic write with backup
- Automatic recovery on failure

#### `ClearAsync()`
- Reset to empty library
- Clear all tracks
- Reset statistics

**Features**:
- **Persistence**: JSON with versioning and integrity checking
- **Statistics**: Auto-calculated on each update
- **Incremental Scanning**: Only process new files
- **Deduplication**: Path-based duplicate detection
- **Error Handling**: Backup and recovery
- **Progress Reporting**: Callbacks for long operations

## ViewModel Integration

### AudioPlayerViewModel
**File**: `PlatypusTools.UI/ViewModels/AudioPlayerViewModel.cs`

**Service Integration**:
```csharp
private LibraryIndexService _libraryIndexService;

// Constructor
public AudioPlayerViewModel()
{
    _libraryIndexService = new LibraryIndexService();
}
```

**New Methods** (Phase 2):

#### `InitializeLibraryAsync()`
- Called on app startup
- Load library index from disk
- Convert Track → AudioTrack for UI
- Populate `_allLibraryTracks` collection
- Rebuild library groups
- Error handling with status message

#### `ScanLibraryDirectoryAsync(string directory)`
- Scan directory for audio files
- Progress callbacks update `ScanStatus`
- Reload library
- Rebuild groups
- Status reporting

#### `SearchLibrary(string query)`
- Search library using LibraryIndexService
- Rebuild groups with results
- Handle empty query

#### `RebuildLibraryGroups()`
- Organize library by mode:
  - Mode 0: All tracks (single group)
  - Mode 1: Artist grouping
  - Mode 2: Album grouping
  - Mode 3: Genre grouping
  - Mode 4: Folder grouping
- Update `LibraryGroups` ObservableCollection

#### `UpdateLibraryGroups()`
- Refactored to call RebuildLibraryGroups()

#### `FilterLibraryTracks()`
- Filter by selected group and search query
- Sort by artist → album → track number
- Populate `LibraryTracks` ObservableCollection

**ObservableCollections**:
- `LibraryGroups` - Bound to UI (artist/album/genre/folder groups)
- `LibraryTracks` - Bound to UI (filtered tracks in current group)
- `_allLibraryTracks` - All tracks in index

**Data Flow**:
```
App Startup
    ↓
InitializeLibraryAsync()
    ↓
Load Index → Convert Tracks → RebuildLibraryGroups()
    ↓
LibraryGroups ObservableCollection updated
    ↓
UI displays groups
    ↓
User selects group or searches
    ↓
FilterLibraryTracks()
    ↓
LibraryTracks ObservableCollection updated
    ↓
UI displays tracks
```

## Unit Tests

### Test Suite: AudioLibraryTests
**File**: `PlatypusTools.Core.Tests/AudioLibraryTests.cs`

**Coverage** (15 tests):
1. ✅ `PathCanonicalizer_NormalizesPath` - Path normalization
2. ✅ `PathCanonicalizer_DeduplicationKey` - Dedup key generation
3. ✅ `PathCanonicalizer_PathsEqual` - Path comparison
4. ✅ `AtomicFileWriter_WritesFileAtomically` - Atomic writes
5. ✅ `AtomicFileWriter_CreatesBackup` - Backup creation
6. ✅ `AtomicFileWriter_BackupExists` - Backup verification
7. ✅ `Track_DisplayPropertiesFallback` - Display properties
8. ✅ `Track_DurationFormatting` - Duration formatting
9. ✅ `LibraryIndex_BuildsIndices` - Index building
10. ✅ `LibraryIndex_SearchByTitle` - Title search
11. ✅ `LibraryIndex_FindByPath` - Path lookup
12. ✅ `LibraryIndexService_CreatesIndex` - Index creation
13. ✅ `LibraryIndexService_PersistsIndex` - Persistence
14. ✅ `MetadataExtractorService_IsAudioFile` - Format detection
15. ✅ `MetadataExtractorService_HandlesNonexistentFile` - Error handling

**Status**: ✅ **All 15 tests passing**

## Data Persistence

### Index File Format

**Location**: `%APPDATA%/PlatypusTools/audio_library_index.json`

**Structure**:
```json
{
  "version": "1.0.0",
  "createdAt": "2025-01-14T19:30:00Z",
  "lastUpdatedAt": "2025-01-14T19:35:00Z",
  "contentHash": "SHA256_HEX_STRING",
  "trackCount": 100,
  "totalSize": 5368709120,
  "tracks": [
    {
      "id": "UUID",
      "filePath": "/path/to/song.mp3",
      "fileSize": 5242880,
      "title": "Song Title",
      "artist": "Artist Name",
      "album": "Album Name",
      // ... 25+ more fields
    }
  ],
  "sourceMetadata": {
    "sourceDirs": ["/music", "/media"],
    "fileExtensions": [".mp3", ".flac"],
    "lastFullRescanAt": "2025-01-14T19:00:00Z",
    "lastIncrementalScanAt": "2025-01-14T19:35:00Z",
    "isIncrementalScanSupported": true
  },
  "statistics": {
    "artistCount": 45,
    "albumCount": 120,
    "genreCount": 18,
    "totalDurationMs": 2419200000,
    "tracksWithArtwork": 89,
    "tracksWithCompleteMetadata": 95,
    "codecDistribution": {
      "mp3": 60,
      "flac": 25,
      "aac": 15
    },
    "bitrateStats": {
      "min": 128,
      "max": 320,
      "average": 256
    }
  }
}
```

## Configuration

### Dependencies
- **TagLib# 2.2.0** - Audio metadata extraction
- **.NET 8** - Framework
- **WPF** - UI (existing)
- **NAudio** - Audio playback (existing)

### NuGet Packages
```
dotnet add package TagLibSharp --version 2.2.0
```

## Performance Characteristics

### Scanning Performance
- **Target**: > 100 tracks/second
- **Parallel Extraction**: 4 concurrent metadata extractions
- **Incremental Mode**: Only new files processed

### Search Performance
- **Single Query**: < 100ms for 10k tracks
- **Index Rebuild**: < 500ms for 10k tracks

### Startup Performance
- **Cold Start**: Load index + convert to UI models: < 1s for 10k tracks
- **Warm Start**: Index already loaded: < 100ms

## Usage Examples

### Initialize Library on Startup
```csharp
// In MainWindow.xaml.cs or App startup
await viewModel.InitializeLibraryAsync();
```

### Scan Directory
```csharp
// Scan ~/Music with progress reporting
await viewModel.ScanLibraryDirectoryAsync(
    Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Music"),
    recursive: true
);
```

### Search Library
```csharp
// Search for "jazz"
viewModel.SearchLibrary("jazz");

// Reset to full library
viewModel.SearchLibrary("");
```

### Organize by Artist
```csharp
// Switch to artist grouping
viewModel.OrganizeModeIndex = 1; // Artist mode
viewModel.RebuildLibraryGroups();
```

## Error Handling

### Metadata Extraction Failures
- Corrupted or unsupported files: Return minimal Track data
- Missing files: Gracefully skip with status message
- Permission denied: Log and continue scanning

### Index Persistence Failures
- Write failure: Automatic restore from backup
- Corruption: Rebuild from scratch
- Missing index: Create new empty index

## Future Enhancements

### Phase 4: UI Integration
- Connect buttons to ViewModel methods
- Bind ObservableCollections to UI
- Add progress display
- Add error notifications

### Phase 5: End-to-End Testing
- Test complete scanning workflow
- Verify persistence across restarts
- Test search and filtering
- Test queue integration

### Phase 6: Performance Optimization
- Profile metadata extraction
- Optimize search algorithms
- Cache frequently accessed data
- Implement lazy loading for large libraries

### Phase 7: Release Packaging
- Release build configuration
- Documentation
- User guide
- Known limitations

## Troubleshooting

### Library Index Not Loading
- Check file permissions on %APPDATA%/PlatypusTools/
- Verify audio_library_index.json exists
- Check JSON file for corruption

### Slow Scanning
- Large directories with thousands of files
- Use recursive: false for initial scan
- Run scans in background

### Missing Metadata
- Some audio formats may have limited metadata
- Check file tags with external tools
- Update TagLib# if needed

### Duplicate Entries
- Run `RemoveMissingFilesAsync()` to clean up
- Verify files have unique paths

## References

- TagLib# Documentation: https://github.com/mono/taglib-sharp
- NAudio: https://github.com/naudio/NAudio
- MVVM Pattern: https://en.wikipedia.org/wiki/Model%E2%80%93view%E2%80%93viewmodel

## Summary

The audio library system provides a complete, production-grade solution for:
- ✅ Indexing large audio libraries (1000+ tracks)
- ✅ Extracting metadata from 9 audio formats
- ✅ Persistent storage with integrity checking
- ✅ Fast searching and filtering
- ✅ Atomic write operations for data safety
- ✅ Incremental scanning for performance
- ✅ MVVM integration for WPF UI binding
- ✅ Comprehensive unit test coverage (15 tests)

**Build Status**: ✅ Zero compilation errors  
**Test Status**: ✅ 15/15 tests passing  
**Ready For**: Phase 4 UI integration
