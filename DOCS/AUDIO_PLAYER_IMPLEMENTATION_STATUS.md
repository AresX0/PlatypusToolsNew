# Audio Player Implementation Status & Gap Analysis

**Status**: Current Implementation Assessment  
**Date**: January 14, 2026  
**Version**: 1.0.0  

---

## Executive Summary

Your audio player currently has:
- ✅ **60% Core Playback** - Play, pause, volume, shuffle, repeat all working
- ✅ **80% Visualizer** - Four rendering modes (spectrum, waveform, circular, mirror) working at native level
- ⚠️ **20% Library Management** - Basic structure in place, needs JSON indexing
- ⚠️ **40% Queue** - UI exists, needs persistence & bulk operations
- ❌ **0% Metadata Extraction** - No TagLib# integration yet
- ❌ **0% Atomic Index System** - JSON library index not implemented

**Recommended Priority**: Library Indexing (highest ROI for feature completeness)

---

## Detailed Implementation Status

### ✅ Completed Features (Tier 1: Core)

#### Playback Engine
- ✅ Play/Pause/Stop commands
- ✅ Previous/Next track navigation
- ✅ Volume slider (0-100%)
- ✅ Mute/Unmute toggle
- ✅ Shuffle mode (On/Off)
- ✅ Repeat modes (Off/All/One)
- ✅ Seek bar with elapsed/remaining time
- ✅ Transport controls UI
- ✅ Queue display (upcoming tracks)
- ✅ Now Playing display

**Note**: NAudio integration exists in `AudioPlayerService.cs`

#### Visualizer System
- ✅ Four rendering modes:
  - Spectrum (FFT bars)
  - Waveform (time domain)
  - Circular (radial bars)
  - Mirror (symmetric spectrum)
- ✅ Mode selector dropdown
- ✅ Bar count adjustment (8-128)
- ✅ EQ preset selector
- ✅ Real-time spectrum data feed
- ✅ Separate AudioVisualizerView component
- ✅ Integrated into main player view (top pane)
- ✅ Color customization (background, bars)

**File**: [PlatypusTools.UI/Views/AudioVisualizerView.xaml](PlatypusTools.UI/Views/AudioVisualizerView.xaml)

#### UI Layout
- ✅ Three-pane layout (Library / Player+Visualizer / Queue)
- ✅ Resizable splitters
- ✅ Toolbar with action buttons
- ✅ Status bar

**Files**: 
- [AudioPlayerView.xaml](PlatypusTools.UI/Views/AudioPlayerView.xaml)
- [AudioPlayerViewModel.cs](PlatypusTools.UI/ViewModels/AudioPlayerViewModel.cs)

#### Settings UI
- ✅ SettingsWindow with multiple tabs
- ✅ Visualizer settings panel
  - Mode selector
  - Bar count slider
  - Color pickers
  - EQ preset selector
- ✅ Basic audio settings placeholders

**File**: [SettingsWindow.xaml](PlatypusTools.UI/Views/SettingsWindow.xaml)

---

### 🔄 In-Progress Features (Tier 2: Enhancement)

#### Queue Management
- ✅ **Add Files to Queue** - Dialog for file selection
- ✅ **Add Folder to Queue** - Folder browser with recursive option
- ✅ **Clear Queue** - Bulk clear operation
- ✅ **Queue Display** - Shows upcoming tracks
- 🔄 **Multi-Select Removal** - UI exists, command not wired
  - DataGrid supports multi-select
  - TODO: Connect to RemoveSelectedFromQueueCommand
- 🔄 **Drag-and-Drop Reorder** - Needs XAML triggers
- ⚠️ **Queue Persistence** - No save/load logic yet
- ⚠️ **Context Menu** - Not implemented

**Status**: ~70% UI complete, ~40% logic implemented

#### Library Display
- ✅ **Library View Tab** - Shows queued tracks
- ✅ **Organization Modes** - Buttons for All/Artist/Album/Genre/Folder
- ✅ **Search Box** - Search functionality with debounce
- ✅ **Groups List** - Shows grouped items when selected
- 🔄 **DataGrid** - Columns defined, virtualization needs work
- ⚠️ **Filtering** - Search works but needs optimization
- ⚠️ **Track Info** - Currently shows queue, not full library

**Status**: ~60% UI complete, ~30% functionality

---

### ⚠️ Planned Features (Tier 3: Production)

#### Library Indexing (CRITICAL)
- ❌ JSON schema not fully implemented
- ❌ Atomic write pattern not used
- ❌ Incremental rescan not implemented
- ❌ Metadata extraction (TagLib#) not integrated
- ❌ Index versioning/migration logic missing
- ❌ Path canonicalization incomplete
- ❌ Missing file detection not implemented

**Impact**: High - Foundation for fast startup & incremental updates  
**Effort**: 8-12 hours  
**Files Needed**: 
- `LibraryIndexService.cs` (new)
- `MetadataExtractor.cs` (new)
- `PathCanonicalizer.cs` (new)
- `Track.cs` model (update)
- `LibraryIndex.cs` model (new)

#### Metadata Extraction
- ❌ TagLib# NuGet not installed
- ❌ Tag reading not implemented
- ❌ Artwork extraction not implemented
- ❌ Fallback to filename if no tags
- ❌ Corrupt tag handling

**Impact**: High - Needed for library display & search  
**Effort**: 6-8 hours  
**Dependencies**: TagLib# NuGet package

#### File Operations & Safety
- ❌ Delete from Disk not implemented
- ⚠️ Double-confirm for >20 items (UI exists)
- ❌ Atomic writes for index
- ❌ Backup/restore logic
- ❌ Relink missing files

**Impact**: Medium - File safety critical  
**Effort**: 6-8 hours

#### Advanced Playback
- ❌ Gapless playback
- ❌ Crossfade between tracks
- ❌ Replay Gain normalization
- ⚠️ Error state handling (partial)

**Impact**: Low-Medium for v1.0  
**Effort**: 8-10 hours

---

## Critical Gap Analysis

### Gap 1: Library Index System (HIGHEST PRIORITY)

**Current State**:
- No persistent JSON index exists
- All data lives in memory (ObservableCollections)
- App loses library on restart
- No incremental rescanning

**Impact**: 
- ❌ Cannot provide fast cold starts
- ❌ No incremental updates
- ❌ No library persistence

**Solution**:
1. Implement `LibraryIndexService.cs` with atomic writes
2. Create `Track` and `LibraryIndex` models with System.Text.Json source generators
3. Add `PathCanonicalizer.cs` for deduplication
4. Integrate with existing `LibraryViewModel`

**Estimated Effort**: 8-12 hours  
**Files to Create**:
```
PlatypusTools.Core/
├── Models/
│   ├── Track.cs (new)
│   ├── LibraryIndex.cs (new)
│   └── QueueSnapshot.cs (update)
├── Services/
│   ├── LibraryIndexService.cs (new)
│   └── JsonIndexService.cs (new)
└── Utilities/
    ├── PathCanonicalizer.cs (new)
    └── AtomicFileWriter.cs (new)
```

---

### Gap 2: Metadata Extraction (HIGH PRIORITY)

**Current State**:
- AudioTrack has metadata properties but not populated
- No tag reading from files
- No artwork extraction
- Fallback to filename only

**Impact**:
- ❌ Library display shows generic info
- ❌ Search doesn't work on proper metadata
- ❌ No album art display

**Solution**:
1. Install TagLib# NuGet package
2. Create `MetadataExtractor.cs` with tag parsing
3. Handle errors gracefully (corrupt tags, missing files)
4. Cache extracted metadata in JSON index

**Estimated Effort**: 6-8 hours  
**Implementation**:
```csharp
public class MetadataExtractor
{
    public static AudioTrack ExtractMetadata(string filePath)
    {
        using (var file = TagLib.File.Create(filePath))
        {
            return new AudioTrack
            {
                Title = file.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
                Artist = file.Tag.FirstPerformer ?? "Unknown",
                Album = file.Tag.Album ?? "Unknown",
                Duration = file.Properties.Duration,
                // ... etc
            };
        }
    }
}
```

---

### Gap 3: Queue Persistence (MEDIUM PRIORITY)

**Current State**:
- Queue UI exists and displays tracks
- No save/load logic
- Queue lost on app restart
- No snapshot mechanism

**Impact**:
- ⚠️ User loses queue when app closes
- ⚠️ Cannot resume from same position

**Solution**:
1. Create `QueueSnapshot` model
2. Add `SaveQueue()` / `LoadQueue()` methods
3. Auto-save on track change (configurable)
4. Restore on startup (configurable)

**Estimated Effort**: 3-4 hours

---

### Gap 4: Atomic Index Writes (MEDIUM PRIORITY)

**Current State**:
- Settings saved directly to files
- No corruption protection
- No backup/restore logic

**Impact**:
- ⚠️ Index corruption possible on crash
- ⚠️ No recovery mechanism

**Solution**:
```csharp
public class AtomicFileWriter
{
    public static void WriteAtomic(string targetPath, string content)
    {
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);
        
        var tmp = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.tmp");
        var bak = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.bak");
        
        File.WriteAllText(tmp, content);
        File.Replace(tmp, targetPath, bak);
    }
}
```

**Estimated Effort**: 2-3 hours

---

## Recommended Implementation Order

### Phase 1: Foundation (Week 1 - CRITICAL)
1. **Library Indexing** (8h) - Most impactful
   - Models + serialization
   - Atomic writes
   - Basic scanning
2. **Metadata Extraction** (6h) - Enables library display
   - TagLib# integration
   - Tag parsing
   - Error handling

**Cumulative Effort**: 14 hours  
**Impact**: Library becomes functional, persistent

### Phase 2: Enhancement (Week 2 - HIGH)
3. **Queue Persistence** (4h) - User convenience
4. **Multi-Select Operations** (3h) - Complete queue UI
5. **File Operations** (4h) - Safety

**Cumulative Effort**: 11 hours  
**Impact**: Queue & library now fully functional

### Phase 3: Polish (Week 3 - MEDIUM)
6. **Search Optimization** (3h)
7. **Missing File Detection** (3h)
8. **Error Handling** (4h)

**Cumulative Effort**: 10 hours  
**Impact**: Production readiness

### Phase 4: Testing (Week 4 - ONGOING)
9. **Unit Tests** (6h)
10. **UI Smoke Tests** (4h)
11. **Performance Testing** (3h)

**Total Estimated Effort**: 48-52 hours (~1.5 months part-time)

---

## Quick Wins (Can Complete Today)

If you want to make quick progress, these are easy wins:

### 1. Wire Multi-Select Queue Removal (30 min)
- Current: UI exists, command not connected
- Fix: Bind `RemoveSelectedFromQueueCommand` to DataGrid
- Impact: Enable bulk removal from queue

### 2. Enable Queue Persistence (1 hour)
- Add simple JSON save/load
- Auto-save on track change
- Impact: Queue survives app restart

### 3. Add Settings Save/Load (1 hour)
- Persist visualizer mode
- Persist user preferences
- Impact: Better UX

### 4. Improve Error Messages (1 hour)
- Add try/catch around file operations
- Show user-friendly errors in UI
- Impact: Better stability

**Total Quick Wins**: 3.5 hours → 5 critical issues resolved

---

## Code Structure Recommendations

### Create New Services

**`LibraryIndexService.cs`**:
```csharp
public interface ILibraryIndexService
{
    Task<LibraryIndex> LoadIndexAsync(CancellationToken ct);
    Task SaveIndexAsync(LibraryIndex index, CancellationToken ct);
    Task<List<Track>> ScanFolderAsync(string path, bool recursive, IProgress<int> progress);
    Task<List<Track>> IncrementalScanAsync(string path, LibraryIndex existing);
}
```

**`MetadataExtractorService.cs`**:
```csharp
public interface IMetadataExtractorService
{
    Task<Track> ExtractMetadataAsync(string filePath);
    Task<byte[]?> ExtractArtworkAsync(string filePath);
}
```

**`PathCanonicalizerService.cs`**:
```csharp
public static class PathCanonicalizer
{
    public static string Canonicalize(string path)
    {
        return Path.GetFullPath(path)
            .ToLowerInvariant()
            .Replace(Path.DirectorySeparatorChar, '/');
    }
}
```

### Update Existing Models

**`Track.cs`** (add required fields):
```csharp
public sealed record Track(
    string Id,                  // UUID
    string Path,                // Canonical path
    string? Title,
    string? Artist,
    string? Album,
    TimeSpan Duration,
    // ... additional fields
);
```

---

## Build & Test Commands

After implementing changes:

```powershell
# Build
cd c:\Projects\PlatypusToolsNew
dotnet build -c Debug

# Run with logging
dotnet run --project PlatypusTools.UI --configuration Debug

# Run tests
dotnet test

# Publish release
dotnet publish PlatypusTools.UI -c Release -o ./publish --self-contained -r win-x64
```

---

## Next Steps

1. **Today**: Review this manifest and gap analysis
2. **Tomorrow**: Start with Library Indexing (highest ROI)
3. **This Week**: Complete Phase 1 (indexing + metadata)
4. **Next Week**: Phase 2 (persistence + operations)
5. **Release**: v1.0 with full feature set ready

---

## Questions & Decisions

### Q1: Use TagLib# or NAudio tags?
**A**: Use **TagLib#** - more robust, better format support

### Q2: Keep in-memory queue or load from disk?
**A**: **Both** - keep in memory, persist to disk, load on startup

### Q3: How often to save index?
**A**: After each scan + optional auto-save on track add

### Q4: What about very large libraries (100k+ tracks)?
**A**: Use WPF virtualization, JSON Lines for incremental loading (v2)

---

## Appendix: NuGet Packages to Add

```xml
<!-- In PlatypusTools.Core.csproj -->
<ItemGroup>
    <PackageReference Include="TagLibSharp" Version="2.3.0" />
    <PackageReference Include="MathNet.Numerics" Version="5.2.0" />
</ItemGroup>
```

Install via CLI:
```powershell
dotnet add PlatypusTools.Core package TagLibSharp
dotnet add PlatypusTools.Core package MathNet.Numerics
```

---

**Document Version**: 1.0  
**Last Updated**: January 14, 2026  
**Next Review**: January 21, 2026
