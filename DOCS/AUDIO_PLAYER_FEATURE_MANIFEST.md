# Audio Player Feature Manifest
**Status**: Production Specification v2.0  
**Target Platform**: Windows 10+ | WPF (.NET 10)  
**Last Updated**: February 8, 2026  

---

## Executive Summary
This manifest tracks all required features for a production-grade desktop Audio Player with real-time visualizer, robust queue/library management, recursive folder scanning, and persistent JSON library indexing.

**Legend**: ✅ Complete | 🔄 In Progress | ⚠️ Planned | ❌ Not Started

---

## 1. Core Playback Engine

### 1.1 Audio Format Support
- ✅ **MP3** - MPEG Audio
- ✅ **M4A/AAC** - Advanced Audio Codec
- ✅ **FLAC** - Free Lossless Audio Codec
- ✅ **WAV** - Waveform Audio Format
- ✅ **OGG** - Ogg Vorbis
- ⚠️ **OPUS** - High-quality compressed format
- ⚠️ **WMA** - Windows Media Audio (future)

### 1.2 Playback Controls
- ✅ Play/Pause
- ✅ Previous Track
- ✅ Next Track
- ✅ Seek (non-blocking)
- ✅ Volume Control (0-100%)
- ✅ Mute/Unmute
- ✅ Shuffle Mode (On/Off)
- ✅ Repeat Modes (Off / Repeat All / Repeat One)
- ⚠️ Gapless Playback (optimized for compatible codecs)
- ⚠️ Crossfade (0-5s configurable)

### 1.3 Playback State Machine
- ✅ Idle → Loading → Playing
- ✅ Playing ↔ Paused
- ⚠️ Stalled/Error States
- ⚠️ Error Recovery & Retry Logic

### 1.4 Threading Model
- ✅ UI Thread: WPF rendering + input
- ✅ Background Pool: Folder scanning, metadata extraction
- ⚠️ Audio Thread: NAudio callbacks (real-time)
- ⚠️ Visualizer Thread: CompositionTarget.Rendering loop
- ⚠️ Lock-free Ring Buffer: For visualizer data

---

## 2. Visualizer System

### 2.1 Visualizer Modes — 22 GPU-Rendered Modes ✅
- ✅ **Bars** (0) — Classic FFT spectrum bars with gradient fill
- ✅ **Mirror** (1) — Symmetric spectrum reflected across center
- ✅ **Waveform** (2) — Time-domain waveform line
- ✅ **Circular** (3) — Radial bar ring around center
- ✅ **Radial** (4) — Outward-expanding radial lines
- ✅ **Particles** (5) — HSL-colored particle system driven by FFT
- ✅ **Aurora** (6) — Multi-layer aurora borealis waves
- ✅ **Wave Grid** (7) — 3D perspective wave grid
- ✅ **Starfield** (8) — Forward-flying star field
- ✅ **Toasters** (9) — Flying toasters animation
- ✅ **Matrix** (10) — Matrix rain with glow (fullscreen-aware)
- ✅ **Star Wars Crawl** (11) — Perspective text crawl
- ✅ **Stargate** (12) — Wormhole/vortex tunnel
- ✅ **Klingon** (13) — Klingon-themed spectrum with logo overlay
- ✅ **Federation** (14) — Federation particle nebula with logo
- ✅ **Jedi** (15) — Lightsaber array driven by FFT bands
- ✅ **TimeLord** (16) — TARDIS vortex with feedback buffer
- ✅ **VU Meter** (17) — Analog VU meter pair
- ✅ **Oscilloscope** (18) — Traditional oscilloscope trace
- ✅ **Milkdrop** (19) — Milkdrop-style feedback buffer with warping
- ✅ **3D Bars** (20) — Perspective 3D bar graph
- ✅ **Waterfall** (21) — Scrolling spectrogram heatmap

### 2.2 Visualizer Configuration
- ✅ FFT Size Selection (1024-point)
- ✅ Bar Count Adjustment (8-128 bars)
- ✅ Color Palette Customization (8 color schemes)
- ✅ Smoothing with rise/fall multipliers
- ✅ Fullscreen responsiveness multipliers (1.5× rise / 1.3× fall)
- ⚠️ Dynamic Quality Adaptation (<40 FPS → reduce complexity)

### 2.3 Performance & Stability ✅
- ✅ GPU-accelerated SkiaSharp rendering via `SKElement`
- ✅ ~22 FPS DispatcherTimer render loop
- ✅ All SKMaskFilter native leaks fixed (20 sites, `using var`)
- ✅ All SKTypeface handles cached (8 static typefaces)
- ✅ GPU bitmap disposal on unload (`DisposeGpuResources()`)
- ✅ Feedback buffers capped at 640px width
- ✅ Render crash recovery (try/catch in `OnPaintSurface`)
- ✅ Thread-safe frame-skip guard (`Interlocked.CompareExchange`)

### 2.4 Fullscreen Mode ✅
- ✅ Enter via double-click or F11
- ✅ Arrow-key mode switching (←/→/↑/↓)
- ✅ OSD overlay with mode name, ◀/▶ buttons, track info
- ✅ Music-responsive in fullscreen (DispatcherPriority.Input)
- ✅ Exit via Escape or double-click

### 2.5 Screensaver Integration ✅
- ✅ All 22 modes available as Windows screensaver
- ✅ Full app copy to `%ProgramData%\PlatypusTools\Screensaver\`
- ✅ 45ms idle animation timer (evolving data without audio)
- ✅ Configuration window for mode selection

### 2.6 Visualizer Integration ✅
- ✅ Real-time spectrum data feed from audio engine
- ✅ Separate AudioVisualizerView component (~10 000 lines)
- ✅ Thread-safe data synchronization (Interlocked guard)
- ✅ Non-blocking audio pipeline

---

## 3. Queue Management

### 3.1 Queue Operations
- ✅ Add Files to Queue
- ✅ Add Folders to Queue (with recursive option)
- ✅ Remove Single Track from Queue
- 🔄 **Multi-Select Removal** - Bulk remove multiple tracks
  - Status: Implemented in XAML
  - TODO: Command binding & logic
- ✅ Clear Entire Queue
- ✅ Reorder Tracks (drag-and-drop ready)
- ⚠️ Drag-and-Drop Reordering (XAML support needed)
- ⚠️ Play Next (insert before current)

### 3.2 Queue Deduplication
- ⚠️ **Canonical Path Deduplication** - Prevent same file added twice
- ⚠️ **User Setting**: Allow/Disallow Duplicate Entries
- ⚠️ **Merge Duplicates by Tags** - Maintenance action (v2)

### 3.3 Queue Persistence
- ⚠️ **Auto-Save Queue** - Save on track change/exit
- ⚠️ **Queue Snapshot** - Restore previous queue on startup (optional)
- ⚠️ **Queue JSON Schema** (see appendix)
  ```json
  {
    "nowPlayingIndex": 0,
    "items": ["uuid-1", "uuid-2", "..."]
  }
  ```

### 3.4 Queue UI Components
- ✅ Queue Pane (right sidebar)
- ✅ Track List Display (Title, Artist, Duration)
- ✅ Now Playing Indicator
- ⚠️ Context Menu (Play Next, Remove, Reveal in Explorer)
- ⚠️ Keyboard Shortcuts (Del = Remove, Ctrl+A = Select All)
- ✅ Status Display (Track count)

---

## 4. Library Management

### 4.1 Library Indexing
- ⚠️ **Persistent JSON Index** - library.index.json
  - Schema: Versioned, atomic writes, backup
  - TODO: Full implementation
- ⚠️ **Cold Start Performance** - Index load < 1.5s for 10k tracks
- ⚠️ **Incremental Rescan** - Detect adds/updates/deletes by path/size/mtime
- ⚠️ **Optional Hash Validation** - For corrupted file detection

### 4.2 Library Scanning
- ✅ Add Folder(s) to Library
- ✅ Recursive Subfolder Scanning (toggle)
- 🔄 **Scan Progress & ETA**
  - Status: IsScanning flag & ScanStatus display
  - TODO: Real ETA calculation
- ⚠️ **Background Scan Job** - Non-blocking UI
- ⚠️ **Stop Scan** - User cancellation support

### 4.3 Metadata Parsing
- ⚠️ **Tag Reading** (TagLib#)
  - Title, Artist, Album, Track #, Disc #, Duration
  - Bitrate, Sample Rate, Channels, Codec
  - Genre, Year, Artwork (embedded)
- ⚠️ **Tag Fallback** - Use filename if tags missing
- ⚠️ **Corrupt Tag Handling** - Skip with error log
- ⚠️ **Artwork Extraction** - Embedded cover art (base64 storage)

### 4.4 Library Sections
- ⚠️ **All Music** - Complete track list
- ⚠️ **Artists** - Grouped by artist
- ⚠️ **Albums** - Grouped by artist/album
- ⚠️ **Genres** - Grouped by genre
- ⚠️ **Folders** - Folder-based organization
- ⚠️ **Playlists** (future v2)
- ⚠️ **Smart Playlists** (future v2)

### 4.5 Library Search & Filter
- ✅ Search by Artist, Album, Title, Genre
- ✅ Filter with debounce
- ⚠️ **Advanced Search** - Multi-field combinations
- ⚠️ **Search History** (future)
- ⚠️ **Fuzzy Matching** (future)

### 4.6 Missing File Handling
- ⚠️ **Mark Missing** - is_missing=true in index
- ⚠️ **Relink Missing** - User selects new root; remap by filename
- ⚠️ **Bulk Relink** - Handle moved libraries
- ⚠️ **Cleanup** - Remove permanently deleted entries

### 4.7 Library Maintenance
- ⚠️ **Rescan Library** - Full or incremental rescan
- ⚠️ **Remove from Library** - Non-destructive (removes from index only)
- ⚠️ **Delete from Disk** - Destructive; double confirm if >20 items
- ⚠️ **Duplicate Detection** - Find duplicate tracks
- ⚠️ **Update Metadata** - Refresh tags from disk

---

## 5. UI/UX Layout & Components

### 5.1 Main Window Structure
- ✅ **Three-Pane Layout** with resizable splitters
- ✅ **Persist Pane Sizes** across sessions
- ⚠️ **Light/Dark Theme Support**
- ⚠️ **High Contrast Theme** (accessibility)
- ⚠️ **Window State Persistence** (maximize, position, size)

### 5.2 Left Sidebar – Library
- ⚠️ **Section Tabs**: All Music, Artists, Albums, Genres, Folders
- ✅ **Search Box** with filter
- 🔄 **Action Buttons**:
  - ✅ Add Folder to Library
  - ✅ Include Subfolders (checkbox)
  - ⚠️ Scan Library (with progress)
  - ⚠️ Stop Scan
- 🔄 **Track List** - Virtualized DataGrid
  - Columns: Title, Artist, Album, Duration, Genre, Year
  - Status: Columns defined, need virtualization
- ⚠️ **Context Menu**: Rescan, Remove, Properties

### 5.3 Center – Now Playing & Visualizer
- ✅ **Header Section**:
  - ✅ Current track title, artist, album
  - ⚠️ Cover art (placeholder if missing)
- ✅ **Visualizer Canvas** (250px height)
  - ✅ Multiple render modes
  - ✅ Mode selector dropdown
- ⚠️ **Visualizer Controls**:
  - ✅ Mode selector (Spectrum/Waveform/Circular/Mirror)
  - ✅ Bar count slider
  - ✅ EQ preset selector
  - ⚠️ Color customization
  - ⚠️ FPS cap adjustment
- ✅ **Footer – Transport Controls**:
  - ✅ Previous, Play/Pause, Stop, Next buttons
  - ✅ Shuffle & Repeat toggles
  - ✅ Volume control (slider + percentage)
  - ✅ Time slider (elapsed/remaining)
- ⚠️ **Keyboard Shortcuts Display**

### 5.4 Right Sidebar – Queue
- ✅ **Queue Pane** with header showing track count
- ✅ **Track List** (Title, Artist, Duration)
- 🔄 **Action Buttons**:
  - ✅ Add Files
  - ✅ Add Folder (recursive toggle)
  - ✅ Clear Queue
  - ⚠️ Save as Playlist (future)
  - ⚠️ Load Playlist (future)
- ⚠️ **Drag Handles** - Reorder tracks
- ⚠️ **Multi-Select** - Ctrl+Click, Shift+Click range
- ⚠️ **Context Menu**: Play Now, Play Next, Remove, Properties
- ✅ **Empty State Message** - "Queue is empty"

### 5.5 Bottom Status Bar
- ✅ **Playback Status** - Current state message
- ⚠️ **Output Device** - Selected audio device
- ⚠️ **Library Stats** - "10,234 tracks indexed"
- ⚠️ **CPU Usage** (optional)
- ⚠️ **Error Messages** - Non-intrusive error display

### 5.6 Accessibility Features
- ⚠️ **Keyboard Navigation** - Full keyboard support
- ⚠️ **Visible Focus Indicators** - Clear focus rect
- ⚠️ **AutomationProperties** - Screen reader support
- ⚠️ **High Contrast Themes** - Tested with Windows accessibility settings
- ⚠️ **Color-Blind Safe Palettes** - Deuteranopia, Protanopia support
- ⚠️ **Font Scaling** - Respect system DPI settings

---

## 6. Data Models & JSON Schema

### 6.1 Track Model
```csharp
public sealed record Track(
    string Id,                      // UUID or stable hash
    string Path,                    // Canonical path
    string Filename,                // Basename
    long Size,                      // File size in bytes
    long MTime,                     // Modification time (epoch)
    string? Hash,                   // Short hash (optional)
    string? Title,
    string? Artist,
    string? Album,
    int? TrackNo,
    int? DiscNo,
    int DurationMs,
    string Codec,                   // mp3, aac, flac, wav, ogg, opus
    int? Bitrate,
    int? SampleRate,
    int? Channels,
    string? Genre,
    int? Year,
    string? ArtworkBase64,          // Embedded artwork (base64)
    bool IsMissing,
    DateTime AddedAt,
    DateTime? LastPlayedAt,
    int PlayCount,
    int? Rating                     // 0-5 stars (optional)
);
```

### 6.2 LibraryIndex Model
```csharp
public sealed class LibraryIndex
{
    public int Version { get; set; } = 1;
    public DateTime GeneratedAt { get; set; }
    public List<Track> Tracks { get; set; } = new();
}
```

### 6.3 JSON Schema (library.index.json)
- ⚠️ **Version**: 1 (versioning for migrations)
- ⚠️ **Generated At**: ISO 8601 timestamp
- ⚠️ **Tracks Array**: All indexed tracks
- ⚠️ **Serialization**: System.Text.Json with source generators
- ⚠️ **Pretty-print**: Off in production, on for debugging

### 6.4 Settings Model
```csharp
public sealed class PlayerSettings
{
    public string? Theme { get; set; } = "dark";
    public bool ResumeOnStartup { get; set; } = true;
    public int CrossfadeMs { get; set; } = 1000;
    public bool AllowDuplicateQueueEntries { get; set; } = false;
    public bool AutoSaveQueue { get; set; } = true;
    
    public VisualizerSettings Visualizer { get; set; } = new();
    public LibrarySettings Library { get; set; } = new();
}

public sealed class VisualizerSettings
{
    public string Mode { get; set; } = "spectrum";  // spectrum, waveform, circular
    public int FftSize { get; set; } = 2048;        // 1024, 2048, 4096
    public int FpsCap { get; set; } = 60;
    public double Smoothing { get; set; } = 0.8;
}

public sealed class LibrarySettings
{
    public bool RecursiveDefault { get; set; } = true;
    public string[] Extensions { get; set; } = new[] 
    { 
        ".mp3", ".m4a", ".flac", ".wav", ".ogg", ".opus" 
    };
}
```

### 6.5 Queue Model
```csharp
public sealed class QueueSnapshot
{
    public int NowPlayingIndex { get; set; }
    public List<string> Items { get; set; } = new();  // Track IDs
}
```

---

## 7. Scanning, Indexing & Deduplication

### 7.1 Folder Add Flow
1. ✅ User selects folder(s)
2. ✅ Recursive option toggle
3. 🔄 Begin scan job with progress display
4. ⚠️ Enumerate files by extension filter
5. ⚠️ For each file: capture path/size/mtime
6. ⚠️ Check existing index by path
7. ⚠️ If unchanged (size + mtime), skip
8. ⚠️ Else: parse metadata (TagLib#) and update/insert
9. ⚠️ Optionally compute short hash (background task)
10. ⚠️ After traversal: detect missing entries
11. ⚠️ Mark is_missing=true for removed files
12. ⚠️ Save index atomically
13. ✅ Show summary dialog

### 7.2 Recursive Traversal
- ✅ Stack-based directory traversal
- ✅ Extension filtering (.mp3, .flac, etc.)
- ⚠️ Long path support (260+ char paths)
- ⚠️ Unicode filename handling
- ⚠️ Locked file detection (skip with log)

### 7.3 Incremental Rescan
- ⚠️ **Change Detection**: path, size, mtime
- ⚠️ **Skip Unchanged**: If all three match, use cached metadata
- ⚠️ **Optional Hash**: For additional validation
- ⚠️ **Performance**: <15s for 10k files on typical SSD

### 7.4 Deduplication Strategy
- ⚠️ **Library**: Dedup by canonical path (ignore case/sep on Windows)
- ⚠️ **Queue**: Configurable - prevent or allow duplicates
- ⚠️ **Merge Action**: Optional tag-based merge (future)

---

## 8. File Operations & Safety

### 8.1 Remove Operations
- ✅ **Remove from Queue**: Immediate, supports multi-select
- ⚠️ **Remove from Library**: Non-destructive (removes from index only)
- ⚠️ **Delete from Disk**: Destructive; requires double confirmation
  - Show item count
  - Require confirmation if >20 items
  - Log deletion
  - Move to Recycle Bin (if possible)

### 8.2 Atomic Index Writes
- ⚠️ **Write Pattern**:
  1. Write to temporary file (.tmp)
  2. Flush to disk
  3. Replace target with temp (atomic)
  4. Keep backup (.bak) of previous version
- ⚠️ **Corruption Protection**: Validate JSON before replacing
- ⚠️ **Crash Safety**: Backup allows recovery

### 8.3 Relink Missing Files
- ⚠️ User selects new root directory
- ⚠️ Attempt remap by filename + tags
- ⚠️ Show success/failure report
- ⚠️ Option to delete unmatched entries

### 8.4 Error Handling
- ⚠️ **Locked Files**: Retry with exponential backoff
- ⚠️ **Unsupported Files**: Skip with reason in log
- ⚠️ **Corrupt Metadata**: Use filename as fallback
- ⚠️ **Permission Denied**: Show actionable error
- ⚠️ **Very Long Paths**: Enable Windows long path support

---

## 9. Playback Engine Details

### 9.1 Audio Output
- ⚠️ **NAudio Integration**:
  - WasapiOut (preferred for modern Windows)
  - WaveOutEvent (fallback)
  - Device enumeration & selection
- ⚠️ **ISampleProvider Chain**:
  - File reader → Normalizer (optional) → Crossfade mixer → Output
- ⚠️ **Event Pipeline**:
  - OnTrackStart, OnPosition, OnBuffer, OnError, OnEnd

### 9.2 Advanced Features
- ⚠️ **ReplayGain** - If tags contain gain, apply normalization
- ⚠️ **Peak Analysis** - Background task for normalization
- ⚠️ **Crossfade** - 0-5s configurable between tracks
- ⚠️ **Gapless** - Where codec allows (FLAC, MP4)

---

## 10. Settings & Preferences

### 10.1 Settings UI (SettingsWindow)
- ✅ **Audio Visualizer Panel** - Mode, bars, colors, EQ
- ⚠️ **Playback Panel** - Resume on startup, crossfade, duplicate queue setting
- ⚠️ **Library Panel** - Default recursive, allowed extensions
- ⚠️ **Theme Panel** - Dark/Light/System
- ⚠️ **Shortcuts Panel** - Editable keymap

### 10.2 Settings Persistence
- ⚠️ **settings.json** - Atomic writes
- ⚠️ **Application Data** - User's local AppData folder
- ⚠️ **Default Values** - Sensible defaults on first run
- ⚠️ **Migration** - Handle version updates

---

## 11. Error Handling & Edge Cases

### 11.1 Audio Issues
- ⚠️ **No Audio Device**: Show user-friendly error
- ⚠️ **Device Disconnected**: Fallback or pause playback
- ⚠️ **Format Not Supported**: Skip track with notification
- ⚠️ **Corrupt File**: Attempt recovery or skip
- ⚠️ **Stream Error**: Retry or move to next track

### 11.2 Large Libraries
- ⚠️ **Virtualization**: WPF ItemsControl with VirtualizingPanel
- ⚠️ **100k+ Tracks**: Should load index in <2s
- ⚠️ **Search Performance**: Indexed search or live filter with debounce

### 11.3 Path & Unicode
- ⚠️ **Long Paths**: Support >260 characters (Windows long path)
- ⚠️ **Unicode**: Full UTF-16 support; NFC normalization
- ⚠️ **Case Sensitivity**: Canonical path comparison (case-insensitive on Windows)
- ⚠️ **Special Chars**: Handle in filenames and tags

---

## 12. Testing & Acceptance Criteria

### 12.1 Functional Tests
- ⚠️ **Library Scanning**:
  - [ ] Add single folder; index populates
  - [ ] Add folder with subfolders; recursive works
  - [ ] Rescan detects new/updated/deleted files
  - [ ] Cold start < 1.5s with 10k tracks
  - [ ] Missing files marked and can relink
  
- ⚠️ **Queue Operations**:
  - [ ] Add files/folders to queue
  - [ ] Multi-select removal works
  - [ ] Dedupe works per setting
  - [ ] Drag-and-drop reorder works
  - [ ] Queue persists (if enabled)
  
- ⚠️ **Playback**:
  - [ ] Play/Pause/Stop work
  - [ ] Seek to position works
  - [ ] Volume control responsive
  - [ ] Shuffle/Repeat modes work
  - [ ] Previous/Next navigate correctly
  
- ⚠️ **Visualizer**:
  - [ ] ~60 FPS on mid-range hardware
  - [ ] All visualization modes render
  - [ ] FFT size adjustment works
  - [ ] Color customization applies
  - [ ] Resizing doesn't stutter audio
  
- ⚠️ **File Ops**:
  - [ ] Delete from disk requires confirmation
  - [ ] Remove from library doesn't delete files
  - [ ] Atomic index writes ensure no corruption
  - [ ] Backup restore works (crash simulation)

### 12.2 Unit Tests
- ⚠️ **Serialization**: Index write/read round-trip
- ⚠️ **Scanning**: Incremental rescan logic
- ⚠️ **Metadata**: TagLib# parsing for major formats
- ⚠️ **Dedup**: Canonical path comparison
- ⚠️ **Queue**: Add, remove, reorder operations
- ⚠️ **Atomic Writes**: Temp → replace → backup pattern

### 12.3 UI Tests (optional)
- ⚠️ **Smoke Tests**: WinAppDriver or Playwright
- ⚠️ **Keyboard Navigation**: Tab order, focus
- ⚠️ **Screen Reader**: NVDA/JAWS compatibility

---

## 13. Performance Budgets

| Metric | Target | Notes |
|--------|--------|-------|
| Cold Start (10k tracks) | < 1.5s | Index load + UI ready |
| Incremental Rescan (10k) | < 15s | Change detection + metadata |
| Visualizer FPS | ~60 | Smooth, degrade at <40 |
| Search/Filter Debounce | < 300ms | Type → results |
| Add Folder (1k files) | < 5s | Metadata parse + index |
| Seek Position | < 200ms | Non-blocking |

---

## 14. Security & Privacy

### 14.1 Data Privacy
- ✅ No external telemetry by default
- ✅ Local-only logging (no network calls)
- ✅ Respect file permissions
- ✅ Store only local paths & media tags (no PII)

### 14.2 File Operations
- ✅ Double confirm destructive operations
- ✅ Log all deletions
- ✅ Atomic writes prevent partial corruption
- ✅ Validate JSON before write

---

## 15. Architecture & Modules

### 15.1 Project Structure
```
PlatypusTools.Core/
├── Models/
│   ├── Track.cs
│   ├── LibraryIndex.cs
│   ├── QueueSnapshot.cs
│   └── Settings.cs
├── Services/
│   ├── AudioService.cs
│   ├── VisualizerService.cs
│   ├── LibraryService.cs
│   ├── QueueService.cs
│   ├── SearchService.cs
│   ├── SettingsService.cs
│   └── JsonIndexService.cs
└── Utilities/
    ├── PathCanonicalizer.cs
    ├── AtomicFileWriter.cs
    └── MetadataExtractor.cs

PlatypusTools.UI/
├── ViewModels/
│   ├── AudioPlayerViewModel.cs
│   ├── LibraryViewModel.cs
│   └── SettingsViewModel.cs
├── Views/
│   ├── AudioPlayerView.xaml
│   ├── LibraryView.xaml
│   └── SettingsWindow.xaml
├── Converters/
│   ├── DurationConverter.cs
│   └── StatusColorConverter.cs
└── Commands/
    ├── PlayCommand.cs
    └── ...
```

### 15.2 Dependency Injection
- ✅ Microsoft.Extensions.DependencyInjection
- ✅ Service registration in App.xaml.cs
- ✅ Constructor injection in ViewModels

### 15.3 MVVM Pattern
- ✅ BindableBase for INotifyPropertyChanged
- ✅ ObservableCollection for lists
- ✅ ICommand implementations
- ✅ CollectionViewSource for filtering

---

## 16. Implementation Roadmap

### Phase 1: Core Playback (Current)
- ✅ Basic play/pause/next/prev
- ✅ Volume control
- ✅ UI layout (three panes)
- ✅ Visualizer integration (native modes)
- Status: ~60% complete

### Phase 2: Library & Queue (Next)
- 🔄 Library indexing (JSON schema)
- 🔄 Incremental scanning
- 🔄 Queue persistence
- 🔄 Multi-select removal
- Estimated: 2-3 weeks

### Phase 3: Advanced Features (v1.1)
- Metadata extraction (TagLib#)
- Search/filter optimization
- Relink missing files
- Gapless playback
- Estimated: 3-4 weeks

### Phase 4: Polish & Testing (v1.0 Release)
- Error handling & edge cases
- Performance optimization
- Unit & UI tests
- Documentation & user guide
- Estimated: 2-3 weeks

### Phase 5: Future Enhancements (v2.0+)
- Playlists & smart playlists
- Watch folders (FileSystemWatcher)
- Advanced DSP (EQ, effects)
- Streaming service integration
- Cross-platform (Linux, macOS)

---

## 17. Key Dependencies

| Package | Purpose | Status |
|---------|---------|--------|
| NAudio | Audio playback & processing | ✅ Integrated |
| TagLib# | Metadata extraction | ⚠️ Planned |
| MathNet.Numerics | FFT & signal processing | ⚠️ Planned |
| SkiaSharp | GPU-accelerated rendering | ✅ Available |
| System.Text.Json | JSON serialization | ✅ Built-in |
| Microsoft.Extensions.DependencyInjection | DI container | ✅ Integrated |
| xUnit + FluentAssertions | Unit testing | ⚠️ Planned |
| Serilog | Structured logging | ⚠️ Optional |

---

## 18. Known Issues & Limitations

- ⚠️ **Crossfade**: Not yet implemented
- ⚠️ **Gapless**: Codec-dependent; may have small gaps
- ⚠️ **Artwork**: Not yet extracted from metadata
- ⚠️ **Long Path Support**: Needs Windows registry configuration
- ⚠️ **Watch Folders**: Not implemented (planned v2)
- ⚠️ **Playlists**: Not implemented (planned v2)

---

## 19. Appendix: Key Code Patterns

### A. Atomic Write Pattern
```csharp
static void WriteIndexAtomic(string targetPath, string json)
{
    var dir = Path.GetDirectoryName(targetPath)!;
    Directory.CreateDirectory(dir);
    var tmp = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.tmp");
    var bak = Path.Combine(dir, $"{Path.GetFileName(targetPath)}.bak");
    
    // Write to temporary file
    File.WriteAllText(tmp, json);
    
    // Atomic replace (Windows native)
    File.Replace(tmp, targetPath, bak);
}
```

### B. Recursive Folder Scan
```csharp
void ScanFolder(string root, bool recursive, Action<string> processFile)
{
    var dirs = new Stack<string>();
    dirs.Push(root);
    
    while (dirs.Count > 0)
    {
        var dir = dirs.Pop();
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                if (Directory.Exists(entry) && recursive)
                    dirs.Push(entry);
                else if (File.Exists(entry) && HasAllowedExtension(entry))
                    processFile(entry);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // Log and continue
        }
    }
}
```

### C. Settings Persistence
```csharp
public sealed class SettingsService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlatypusTools",
        "settings.json"
    );
    
    public async Task SaveAsync(PlayerSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        // Use atomic writer
        WriteIndexAtomic(_settingsPath, json);
    }
    
    public async Task<PlayerSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
            return new PlayerSettings(); // Defaults
        
        var json = await File.ReadAllTextAsync(_settingsPath);
        return JsonSerializer.Deserialize<PlayerSettings>(json, _jsonOptions)
            ?? new PlayerSettings();
    }
}
```

---

## 20. Review Checklist

Before release, verify:
- [ ] All playback controls functional
- [ ] Visualizer renders at ~60 FPS
- [ ] Library indexing works with incremental updates
- [ ] Queue multi-select removal works
- [ ] Settings persist across sessions
- [ ] Error messages are user-friendly
- [ ] Performance meets budgets (cold start, rescan)
- [ ] Tests pass (unit + smoke tests)
- [ ] Documentation complete
- [ ] Code reviewed for accessibility
- [ ] Build & package scripts validated

---

## 21. Contact & Support

**Project Lead**: [You]  
**Repository**: [GitHub URL]  
**Issue Tracker**: [GitHub Issues]  
**Documentation**: See `/DOCS` folder  

**Build & Run**:
```bash
cd c:\Projects\PlatypusToolsNew
dotnet build -c Debug
dotnet run --project PlatypusTools.UI
```

**Publish Release**:
```bash
dotnet publish PlatypusTools.UI -c Release -o ./publish --self-contained -r win-x64
```

---

**Last Updated**: January 14, 2026  
**Version**: 1.0.0-alpha  
**Next Review**: February 14, 2026
