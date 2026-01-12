# PlatypusTools v3.0.0 - Major Features Implementation Manifest

**Created**: January 11, 2026  
**Branch**: `v3.0.0-major-features`  
**Target Release**: v3.0.0  
**Estimated Scope**: 150+ new files, 50,000+ lines of code

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Feature Categories](#feature-categories)
3. [Detailed Feature Specifications](#detailed-feature-specifications)
4. [Architecture Overview](#architecture-overview)
5. [File Structure](#file-structure)
6. [Implementation Phases](#implementation-phases)
7. [Dependencies](#dependencies)
8. [Testing Strategy](#testing-strategy)
9. [Documentation Requirements](#documentation-requirements)

---

## Executive Summary

This manifest defines the complete implementation plan for PlatypusTools v3.0.0, which includes:

- **15 Major Feature Areas**
- **Audio Player Suite** (C++/CLI + .NET interop)
- **Plugin/Extension System**
- **Comprehensive UI Enhancements**

### Feature Count Summary

| Category | Features | Priority |
|----------|----------|----------|
| Video Editor Enhancements | 3 | P1 |
| Image Scaler Enhancements | 2 | P1 |
| Metadata Editor Enhancements | 2 | P1 |
| Duplicate Finder Enhancements | 2 | P1 |
| UI/UX Enhancements | 4 | P1 |
| New Tools | 4 | P2 |
| System Features | 4 | P2 |
| Audio Player Suite | 6 modules | P3 |

---

## Feature Categories

### Category 1: Video Editor Enhancements
- [VE-001] Timeline UI with multi-track support
- [VE-002] Multi-track editing (video, audio, overlay)
- [VE-003] Transitions library (fade, wipe, dissolve, etc.)

### Category 2: Image Scaler Enhancements
- [IS-001] Batch processing with queue management
- [IS-002] Comparison preview (before/after slider)

### Category 3: Metadata Editor Enhancements
- [ME-001] Template presets (save/load tag templates)
- [ME-002] Batch metadata copy (apply to multiple files)

### Category 4: Duplicate Finder Enhancements
- [DF-001] Image similarity detection (perceptual hashing)
- [DF-002] Video similarity detection (frame sampling + hash)

### Category 5: UI/UX Enhancements
- [UX-001] Keyboard shortcuts (configurable hotkeys)
- [UX-002] Status bar with operation progress
- [UX-003] Recent workspaces with pinning
- [UX-004] Auto-update checker and installer

### Category 6: New Tools
- [NT-001] Batch watermarking (image + video)
- [NT-002] PDF tools (merge, split, compress, convert)
- [NT-003] Archive manager (ZIP, 7z, RAR, TAR)
- [NT-004] Screenshot tool (region, window, full, annotation)

### Category 7: System Features
- [SF-001] Settings backup and restore
- [SF-002] Third-party plugin/extension system
- [SF-003] Logging UI for troubleshooting
- [SF-004] Comprehensive documentation generator

### Category 8: Audio Player Suite (C++ Core + .NET UI)
- [AP-001] Audio File Converter (FFmpeg wrapper)
- [AP-002] Audio Player (gapless, crossfade, EQ)
- [AP-003] Visualizer plugin host (spectrum, waveform, projectM)
- [AP-004] Lyrics display (LRC, ID3 USLT/SYLT)
- [AP-005] Library organization (artist/album/genre)
- [AP-006] C++/CLI interop layer

---

## Detailed Feature Specifications

### [VE-001] Video Editor Timeline

**Purpose**: Provide visual timeline for precise video editing

**Components**:
- `VideoTimelineControl.xaml` - Custom WPF timeline control
- `TimelineTrack.cs` - Model for individual tracks
- `TimelineClip.cs` - Model for clips on timeline
- `TimelineViewModel.cs` - ViewModel with drag/drop, zoom, scrub

**Properties**:
- Zoom level (frames, seconds, minutes)
- Playhead position with scrubbing
- Track headers (mute, solo, lock, visibility)
- Clip trimming handles
- Snap to grid/markers

**API**:
```csharp
public class VideoTimelineViewModel
{
    public ObservableCollection<TimelineTrack> Tracks { get; }
    public TimeSpan Duration { get; set; }
    public TimeSpan PlayheadPosition { get; set; }
    public double ZoomLevel { get; set; }
    
    public void AddTrack(TrackType type);
    public void AddClip(TimelineTrack track, string filePath, TimeSpan position);
    public void TrimClip(TimelineClip clip, TimeSpan newStart, TimeSpan newEnd);
    public void SplitClip(TimelineClip clip, TimeSpan position);
    public void Export(ExportSettings settings);
}
```

---

### [VE-002] Multi-Track Editing

**Purpose**: Support multiple video, audio, and overlay tracks

**Track Types**:
- Video tracks (V1, V2, V3...)
- Audio tracks (A1, A2, A3...)
- Title/Text tracks
- Effects/Adjustment tracks

**Features**:
- Track ordering (drag to reorder)
- Track visibility toggle
- Track mute/solo
- Track lock (prevent edits)
- Keyframe animation support

---

### [VE-003] Transitions Library

**Purpose**: Apply transitions between clips

**Transition Types**:
| Category | Transitions |
|----------|-------------|
| Fade | Fade In, Fade Out, Cross Dissolve |
| Wipe | Left, Right, Up, Down, Diagonal |
| Slide | Push, Slide, Cover, Reveal |
| Zoom | Zoom In, Zoom Out, Cross Zoom |
| 3D | Cube, Flip, Page Curl |
| Blur | Gaussian, Motion, Radial |
| Custom | User-defined via FFmpeg filter |

**API**:
```csharp
public class Transition
{
    public string Name { get; set; }
    public TransitionType Type { get; set; }
    public TimeSpan Duration { get; set; }
    public EasingFunction Easing { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
}
```

---

### [IS-001] Image Scaler Batch Processing

**Purpose**: Process multiple images with queue management

**Features**:
- Add files/folders to queue
- Apply same settings to all
- Per-item override capability
- Progress tracking
- Pause/resume/cancel
- Output naming patterns
- Parallel processing (configurable threads)

**API**:
```csharp
public class BatchUpscaleJob
{
    public string InputPath { get; set; }
    public string OutputPath { get; set; }
    public int ScaleFactor { get; set; }
    public UpscaleModel Model { get; set; }
    public JobStatus Status { get; set; }
    public double Progress { get; set; }
}

public class BatchUpscaleService
{
    public Task ProcessQueueAsync(IEnumerable<BatchUpscaleJob> jobs, 
        IProgress<BatchProgress> progress, CancellationToken ct);
}
```

---

### [IS-002] Comparison Preview

**Purpose**: Visual before/after comparison

**Modes**:
- Slider (drag to reveal)
- Side-by-side
- Overlay toggle
- Zoom sync (both images zoom together)

**UI Component**:
```xaml
<controls:ComparisonViewer
    LeftImage="{Binding OriginalImage}"
    RightImage="{Binding ProcessedImage}"
    Mode="Slider"
    SliderPosition="0.5" />
```

---

### [ME-001] Metadata Template Presets

**Purpose**: Save and reuse tag configurations

**Template Structure**:
```json
{
    "name": "Album Release Template",
    "description": "Standard tags for album releases",
    "tags": {
        "Artist": "",
        "Album": "",
        "Year": "",
        "Genre": "Electronic",
        "Copyright": "© 2026 Label Name",
        "Comment": "Mastered by..."
    },
    "autoFill": {
        "TrackNumber": "fromFilename:^(\\d+)"
    }
}
```

**Features**:
- Save current tags as template
- Load template to edit
- Apply template to file(s)
- Merge mode (only fill empty fields)
- Replace mode (overwrite all)

---

### [ME-002] Batch Metadata Copy

**Purpose**: Copy tags from one file to many

**Workflow**:
1. Select source file
2. Choose which tags to copy
3. Select target files
4. Preview changes
5. Apply with undo support

---

### [DF-001] Image Similarity Detection

**Purpose**: Find visually similar images

**Algorithms**:
- pHash (perceptual hash) - DCT-based
- dHash (difference hash) - Gradient-based
- aHash (average hash) - Luminance-based
- SSIM (structural similarity) - Quality metric

**Similarity Threshold**: Configurable (0-100%)

**Output**:
- Group similar images
- Show similarity percentage
- Visual comparison grid

---

### [DF-002] Video Similarity Detection

**Purpose**: Find duplicate/similar videos

**Method**:
1. Extract key frames (N frames per video)
2. Generate perceptual hash per frame
3. Compare hash sequences
4. Consider duration tolerance

**Features**:
- Configurable frame sample rate
- Duration tolerance (±N seconds)
- Thumbnail preview of matches

---

### [UX-001] Keyboard Shortcuts

**Purpose**: Configurable hotkeys for all actions

**Default Shortcuts**:
| Action | Shortcut |
|--------|----------|
| New | Ctrl+N |
| Open | Ctrl+O |
| Save | Ctrl+S |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y |
| Find | Ctrl+F |
| Settings | Ctrl+, |
| Dark Mode | Ctrl+Shift+D |
| Full Screen | F11 |

**Configuration**:
```json
{
    "shortcuts": {
        "File.Open": "Ctrl+O",
        "Edit.Undo": "Ctrl+Z",
        "View.ToggleTheme": "Ctrl+Shift+D",
        "Tools.EmptyFolderScanner": "Ctrl+Alt+E"
    }
}
```

---

### [UX-002] Status Bar

**Purpose**: Persistent operation status display

**Components**:
- Current operation name
- Progress bar
- Elapsed time
- Items processed (X of Y)
- Cancel button
- Quick access icons

**Location**: Bottom of main window

---

### [UX-003] Recent Workspaces

**Purpose**: Quick access to recent folders/projects

**Features**:
- Last 20 workspaces
- Pin favorites
- Remove from list
- Clear all
- Open on startup option

---

### [UX-004] Auto-Update

**Purpose**: Check for and install updates

**Components**:
- `UpdateService.cs` - Check GitHub releases API
- `UpdateViewModel.cs` - UI for update notification
- `UpdateInstaller.cs` - Download and apply update

**Workflow**:
1. Check on startup (configurable)
2. Compare version with latest release
3. Show notification if update available
4. Download in background
5. Prompt to install (closes app, runs installer)

---

### [NT-001] Batch Watermarking

**Purpose**: Add watermark to images/videos

**Watermark Types**:
- Text (with font, color, opacity, position)
- Image (PNG with alpha)

**Position Options**:
- Corners (TL, TR, BL, BR)
- Center
- Tiled
- Custom X,Y coordinates

**Features**:
- Preview before apply
- Batch processing
- Video support via FFmpeg

---

### [NT-002] PDF Tools

**Purpose**: Comprehensive PDF manipulation

**Operations**:
| Operation | Description |
|-----------|-------------|
| Merge | Combine multiple PDFs |
| Split | Extract pages to separate files |
| Extract Pages | Select specific pages |
| Compress | Reduce file size |
| Convert | To/from images, Word, HTML |
| Encrypt | Add password protection |
| Decrypt | Remove password |
| Rotate | Rotate pages |
| Watermark | Add text/image watermark |

**Library**: iTextSharp or PDFsharp

---

### [NT-003] Archive Manager

**Purpose**: Create and extract archives

**Formats**:
- ZIP (read/write)
- 7z (read/write via 7z.dll)
- RAR (read only)
- TAR, TAR.GZ, TAR.BZ2
- GZIP, BZIP2

**Features**:
- Browse archive contents
- Selective extraction
- Password protection
- Compression level
- Split archives

---

### [NT-004] Screenshot Tool

**Purpose**: Screen capture with annotation

**Capture Modes**:
- Full screen
- Active window
- Region selection
- Scrolling capture

**Annotation Tools**:
- Arrow
- Rectangle
- Ellipse
- Freehand
- Text
- Blur/Pixelate
- Highlight

**Output**:
- Copy to clipboard
- Save to file
- Upload to service (optional)

---

### [SF-001] Settings Backup/Restore

**Purpose**: Export and import all settings

**Includes**:
- User preferences
- Keyboard shortcuts
- Theme settings
- Recent workspaces
- Plugin configurations
- Window layouts

**Format**: JSON or encrypted ZIP

---

### [SF-002] Plugin/Extension System

**Purpose**: Allow third-party extensions

**Architecture**:
```
Plugins/
  MyPlugin/
    manifest.json
    MyPlugin.dll
    Assets/
```

**Manifest**:
```json
{
    "id": "com.example.myplugin",
    "name": "My Plugin",
    "version": "1.0.0",
    "author": "Author Name",
    "description": "Plugin description",
    "entryPoint": "MyPlugin.dll",
    "permissions": ["filesystem", "network"],
    "extensionPoints": ["tools", "contextMenu"]
}
```

**Extension Points**:
- Tools menu
- Context menus
- File processors
- Export formats
- Visualizers (for audio player)

---

### [SF-003] Logging UI

**Purpose**: View application logs for troubleshooting

**Features**:
- Real-time log viewer
- Filter by level (Debug, Info, Warning, Error)
- Search logs
- Export logs
- Clear logs
- Auto-scroll toggle

---

### [AP-001] Audio File Converter

**Purpose**: Convert between audio formats

**Formats**:
- MP3 (LAME encoder)
- AAC/M4A (including ALAC)
- WAV
- AIFF
- FLAC
- OGG Vorbis
- Opus
- WMA
- AMR
- APE

**Controls**:
- Bitrate (CBR/VBR)
- Sample rate
- Bit depth
- Channels
- Normalization (EBU R128)
- Trim/crop
- Fade in/out
- Batch conversion

**Implementation**: FFmpeg wrapper in C++

---

### [AP-002] Audio Player

**Purpose**: Full-featured audio playback

**Features**:
- Play/pause/stop
- Seek
- Loop (track, playlist)
- Next/previous
- Playlist and queue
- Gapless playback
- Optional crossfade
- Output device selection
- 10-band EQ
- Preamp gain
- ReplayGain support
- Speed control (0.5-2.0x)
- Pitch adjustment

---

### [AP-003] Visualizer Plugin Host

**Purpose**: Audio visualization

**Visualizer Types**:
- Spectrum analyzer
- Waveform
- VU meter
- projectM presets

**Implementation**:
- FFT via KissFFT
- OpenGL rendering
- Preset hot-swapping

---

### [AP-004] Lyrics Display

**Purpose**: Show synchronized lyrics

**Sources**:
- Embedded ID3 USLT/SYLT
- Vorbis/FLAC comments
- External .lrc files

**Features**:
- Time-synced highlighting
- Karaoke mode
- Font/size/color customization
- Show/hide toggle

---

### [AP-005] Library Organization

**Purpose**: Organize audio collection

**Index Fields**:
- Artist, Album, Title
- Genre, Year
- Track #, Disc #
- Duration, Bitrate
- Sample rate, Channels
- Format, Path, Hash
- Album art

**Views**:
- By Artist
- By Album
- By Genre
- By Year
- By Folder
- Combined (Artist → Album → Tracks)

**Features**:
- Smart playlists (rule builder)
- Album art extraction
- Tag editing with undo

**Storage**: SQLite

---

### [AP-006] C++/CLI Interop Layer

**Purpose**: Bridge C++ audio core to .NET

**Components**:
- `ConverterServiceWrapper.h/cpp`
- `PlayerServiceWrapper.h/cpp`
- `MetadataServiceWrapper.h/cpp`
- `LyricsServiceWrapper.h/cpp`
- `LibraryServiceWrapper.h/cpp`
- `VisualizerServiceWrapper.h/cpp`

**Pattern**: Expose Task-based async APIs with progress callbacks

---

## Architecture Overview

### Solution Structure

```
PlatypusToolsNew.sln
│
├── PlatypusTools.Core           # .NET services and models
├── PlatypusTools.UI             # WPF application (MVVM)
├── PlatypusTools.Installer      # WiX MSI installer
├── PlatypusTools.Core.Tests     # Unit tests
│
├── CppAudioCore                 # C++ static library
│   ├── ConverterService
│   ├── PlayerService
│   ├── MetadataService
│   ├── LyricsService
│   ├── LibraryService
│   └── VisualizerPluginHost
│
├── CppAudioBridge               # C++/CLI wrapper
│
└── PlatypusTools.Plugins.SDK    # Plugin development SDK
```

### Data Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   WPF Views     │────▶│   ViewModels    │────▶│   .NET Services │
└─────────────────┘     └─────────────────┘     └─────────────────┘
                                                         │
                                                         ▼
                                                ┌─────────────────┐
                                                │  C++/CLI Bridge │
                                                └─────────────────┘
                                                         │
                                                         ▼
                                                ┌─────────────────┐
                                                │  C++ Core Libs  │
                                                │  (FFmpeg, etc.) │
                                                └─────────────────┘
```

---

## File Structure

### New Files for v3.0.0

```
PlatypusTools.UI/
├── Controls/
│   ├── VideoTimeline/
│   │   ├── TimelineControl.xaml
│   │   ├── TimelineControl.xaml.cs
│   │   ├── TimelineTrackControl.xaml
│   │   ├── TimelineClipControl.xaml
│   │   └── TimelineRuler.xaml
│   ├── ComparisonViewer.xaml
│   ├── ComparisonViewer.xaml.cs
│   ├── StatusBarControl.xaml
│   └── StatusBarControl.xaml.cs
│
├── ViewModels/
│   ├── VideoEditor/
│   │   ├── TimelineViewModel.cs
│   │   ├── TrackViewModel.cs
│   │   ├── ClipViewModel.cs
│   │   └── TransitionViewModel.cs
│   ├── AudioPlayer/
│   │   ├── PlayerViewModel.cs
│   │   ├── PlaylistViewModel.cs
│   │   ├── LibraryViewModel.cs
│   │   ├── LyricsViewModel.cs
│   │   ├── EQViewModel.cs
│   │   └── VisualizerViewModel.cs
│   ├── Tools/
│   │   ├── BatchWatermarkViewModel.cs
│   │   ├── PdfToolsViewModel.cs
│   │   ├── ArchiveManagerViewModel.cs
│   │   └── ScreenshotViewModel.cs
│   ├── Settings/
│   │   ├── KeyboardShortcutsViewModel.cs
│   │   ├── PluginManagerViewModel.cs
│   │   └── UpdateViewModel.cs
│   └── Common/
│       ├── StatusBarViewModel.cs
│       └── LogViewerViewModel.cs
│
├── Views/
│   ├── VideoEditor/
│   │   ├── VideoTimelineView.xaml
│   │   └── TransitionPickerView.xaml
│   ├── AudioPlayer/
│   │   ├── PlayerView.xaml
│   │   ├── LibraryView.xaml
│   │   ├── PlaylistView.xaml
│   │   ├── LyricsView.xaml
│   │   ├── EQView.xaml
│   │   └── VisualizerView.xaml
│   ├── Tools/
│   │   ├── BatchWatermarkView.xaml
│   │   ├── PdfToolsView.xaml
│   │   ├── ArchiveManagerView.xaml
│   │   └── ScreenshotView.xaml
│   ├── Settings/
│   │   ├── KeyboardShortcutsView.xaml
│   │   ├── PluginManagerView.xaml
│   │   └── UpdateView.xaml
│   └── Common/
│       └── LogViewerWindow.xaml
│
├── Services/
│   ├── KeyboardShortcutService.cs
│   ├── UpdateService.cs
│   ├── PluginService.cs
│   ├── SettingsBackupService.cs
│   └── ScreenCaptureService.cs

PlatypusTools.Core/
├── Services/
│   ├── Video/
│   │   ├── TimelineService.cs
│   │   └── TransitionService.cs
│   ├── Image/
│   │   ├── BatchUpscaleService.cs
│   │   ├── ImageSimilarityService.cs
│   │   └── WatermarkService.cs
│   ├── Audio/
│   │   ├── IAudioConverterService.cs
│   │   ├── IAudioPlayerService.cs
│   │   ├── ILyricsService.cs
│   │   ├── ILibraryService.cs
│   │   └── IVisualizerService.cs
│   ├── Documents/
│   │   └── PdfService.cs
│   ├── Archives/
│   │   └── ArchiveService.cs
│   └── System/
│       ├── PluginLoader.cs
│       └── LogService.cs
│
├── Models/
│   ├── Video/
│   │   ├── TimelineTrack.cs
│   │   ├── TimelineClip.cs
│   │   └── Transition.cs
│   ├── Audio/
│   │   ├── Track.cs
│   │   ├── Playlist.cs
│   │   ├── Artist.cs
│   │   ├── Album.cs
│   │   ├── Lyrics.cs
│   │   └── EQPreset.cs
│   └── Plugin/
│       ├── PluginManifest.cs
│       └── IPlugin.cs

CppAudioCore/
├── include/
│   ├── converter/
│   │   └── ConverterService.h
│   ├── player/
│   │   └── PlayerService.h
│   ├── metadata/
│   │   └── MetadataService.h
│   ├── lyrics/
│   │   └── LyricsService.h
│   ├── library/
│   │   └── LibraryService.h
│   ├── visualizer/
│   │   └── VisualizerPluginHost.h
│   └── common/
│       ├── Types.h
│       └── Callbacks.h
├── src/
│   ├── converter/
│   │   └── ConverterService.cpp
│   ├── player/
│   │   └── PlayerService.cpp
│   ├── metadata/
│   │   └── MetadataService.cpp
│   ├── lyrics/
│   │   └── LyricsService.cpp
│   ├── library/
│   │   └── LibraryService.cpp
│   └── visualizer/
│       └── VisualizerPluginHost.cpp
└── third_party/
    ├── ffmpeg/
    ├── taglib/
    ├── portaudio/
    ├── kissfft/
    └── libebur128/

CppAudioBridge/
├── src/
│   ├── ConverterServiceWrapper.h
│   ├── ConverterServiceWrapper.cpp
│   ├── PlayerServiceWrapper.h
│   ├── PlayerServiceWrapper.cpp
│   ├── MetadataServiceWrapper.h
│   ├── MetadataServiceWrapper.cpp
│   ├── LyricsServiceWrapper.h
│   ├── LyricsServiceWrapper.cpp
│   ├── LibraryServiceWrapper.h
│   ├── LibraryServiceWrapper.cpp
│   ├── VisualizerServiceWrapper.h
│   └── VisualizerServiceWrapper.cpp

PlatypusTools.Plugins.SDK/
├── IPlugin.cs
├── IToolPlugin.cs
├── IVisualizerPlugin.cs
├── IFileProcessorPlugin.cs
├── PluginContext.cs
└── PluginAttribute.cs
```

---

## Implementation Phases

### Phase 1: UI Foundation (Week 1-2)
- [x] Branch created
- [ ] Status bar implementation
- [ ] Keyboard shortcuts system
- [ ] Recent workspaces
- [ ] Settings backup/restore

### Phase 2: Enhanced Existing Tools (Week 2-4)
- [ ] Video Editor timeline
- [ ] Video Editor multi-track
- [ ] Video Editor transitions
- [ ] Image scaler batch processing
- [ ] Image scaler comparison preview
- [ ] Metadata template presets
- [ ] Metadata batch copy
- [ ] Duplicate finder image similarity
- [ ] Duplicate finder video similarity

### Phase 3: New Tools (Week 4-6)
- [ ] Batch watermarking
- [ ] PDF tools
- [ ] Archive manager
- [ ] Screenshot tool

### Phase 4: System Features (Week 6-8)
- [ ] Auto-update system
- [ ] Plugin/extension framework
- [ ] Logging UI
- [ ] Plugin SDK

### Phase 5: Audio Player C++ Core (Week 8-12)
- [ ] C++ project setup
- [ ] FFmpeg integration
- [ ] ConverterService
- [ ] PlayerService
- [ ] MetadataService
- [ ] LyricsService
- [ ] LibraryService
- [ ] VisualizerPluginHost

### Phase 6: Audio Player C++/CLI Bridge (Week 12-14)
- [ ] Wrapper classes
- [ ] Event marshaling
- [ ] Async Task wrappers
- [ ] Error handling

### Phase 7: Audio Player .NET UI (Week 14-18)
- [ ] Player view
- [ ] Library view
- [ ] Playlist management
- [ ] Lyrics display
- [ ] EQ controls
- [ ] Visualizer view

### Phase 8: Testing & Documentation (Week 18-20)
- [ ] Unit tests
- [ ] Integration tests
- [ ] Code documentation
- [ ] User documentation
- [ ] API documentation

---

## Dependencies

### .NET NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| PDFsharp | 6.x | PDF manipulation |
| SharpCompress | 0.34+ | Archive handling |
| TagLibSharp | 2.3+ | Audio metadata |
| MathNet.Numerics | 5.0+ | FFT for visualizer |
| Microsoft.Data.Sqlite | 8.0+ | Library database |
| Hardcodet.NotifyIcon.Wpf | 2.0+ | System tray |
| Octokit | 11.0+ | GitHub API for updates |

### C++ Libraries

| Library | Version | Purpose | License |
|---------|---------|---------|---------|
| FFmpeg | 6.x | Audio/video processing | LGPL/GPL |
| TagLib | 2.0 | Metadata | LGPL |
| PortAudio | 19.7+ | Audio output | MIT |
| KissFFT | 131 | FFT | BSD |
| libebur128 | 1.2+ | Loudness | MIT |
| SQLite3 | 3.45+ | Database | Public Domain |

---

## Testing Strategy

### Unit Tests
- All service methods
- Model validation
- ViewModel commands
- Plugin loading

### Integration Tests
- Conversion pipeline
- Player playback
- Library scanning
- Update process

### UI Tests
- Timeline interactions
- Keyboard shortcuts
- Theme switching
- Window states

---

## Documentation Requirements

### Per-File Documentation
Every source file must include:
1. File header comment with purpose
2. All public members documented
3. Complex algorithms explained
4. Usage examples where applicable

### API Documentation
- XML doc comments on all public APIs
- Generated HTML documentation
- Code samples

### User Documentation
- Feature guides
- Tutorial videos (optional)
- FAQ
- Troubleshooting guide

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| FFmpeg licensing | High | Dynamic linking, external process |
| C++ build complexity | Medium | vcpkg, detailed build docs |
| Plugin security | High | Sandboxing, permission system |
| Scope creep | High | Phased approach, MVP first |

---

## Success Criteria

1. All features implemented and functional
2. 90%+ test coverage
3. No critical bugs
4. Documentation complete
5. Build in under 5 minutes
6. Installer works on clean Windows 10/11
7. Plugin SDK usable by third parties

---

*Document Version: 1.0*  
*Last Updated: January 11, 2026*
