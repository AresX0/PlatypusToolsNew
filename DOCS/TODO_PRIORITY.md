# PlatypusTools - Priority Feature List

**Created**: January 19, 2026  
**Purpose**: Quick wins and high-impact features for Audio Player (MusicBee-inspired) and Video Editor (Shotcut-inspired)  
**Legend**: ⭐ = 1-2 hours | ⭐⭐ = 3-4 hours | ⭐⭐⭐ = 5-8 hours

---

## 🎵 Audio Player Enhancements (MusicBee-Inspired)

### Immediate (⭐ = 1-2 hours each)

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| AP-001 | Track Rating UI | ⭐ | Add clickable star rating widget to Now Playing panel (model already exists) | ❌ Not Started |
| AP-002 | Play Speed Control | ⭐ | Add 0.5x / 0.75x / 1x / 1.25x / 1.5x / 2x playback speed buttons | ❌ Not Started |
| AP-003 | Media Key Support | ⭐ | Respond to Play/Pause/Next/Prev hardware keyboard keys | ❌ Not Started |
| AP-004 | Now Playing Queue Reorder | ⭐ | Drag-and-drop reordering in the queue list | ❌ Not Started |
| AP-005 | Track Info Tooltip | ⭐ | Hover tooltip showing full metadata (bitrate, codec, file size) | ❌ Not Started |

### Quick Wins (⭐⭐ = 3-4 hours each)

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| AP-006 | 10-Band EQ UI | ⭐⭐ | Upgrade from 3-band to 10-band equalizer (32Hz, 64Hz, 125Hz, 250Hz, 500Hz, 1kHz, 2kHz, 4kHz, 8kHz, 16kHz) | ❌ Not Started |
| AP-007 | LRC Lyrics Parsing | ⭐⭐ | Parse .lrc files and display synced lyrics with current line highlighting | ❌ Not Started |
| AP-008 | Smart Playlists | ⭐⭐ | Auto-playlists based on rules (Most Played, Recently Added, Top Rated, Genre-based) | ❌ Not Started |
| AP-009 | Mini Player Mode | ⭐⭐ | Compact floating window with basic controls and album art | ❌ Not Started |
| AP-010 | Audio File Info Panel | ⭐⭐ | Detailed file info panel (codec, bitrate, sample rate, channels, ReplayGain) | ❌ Not Started |

### Medium Effort (⭐⭐⭐ = 5-8 hours each)

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| AP-011 | Gapless Playback | ⭐⭐⭐ | Seamless album playback without gaps between tracks | ❌ Not Started |
| AP-012 | Auto DJ / Radio Mode | ⭐⭐⭐ | Auto-queue similar tracks based on genre/artist | ❌ Not Started |
| AP-013 | Scrobbling / Last.fm | ⭐⭐⭐ | Track listening history and submit to Last.fm (requires API) | ❌ Not Started |
| AP-014 | ReplayGain Support | ⭐⭐⭐ | Apply ReplayGain normalization during playback | ❌ Not Started |
| AP-015 | Audio Converter Integration | ⭐⭐⭐ | Convert tracks to MP3/FLAC/AAC from library context menu | ❌ Not Started |

---

## 🎬 Video Editor Enhancements (Shotcut-Inspired)

### Immediate (⭐ = 1-2 hours each)

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| VE-001 | Filter Search Box | ⭐ | Add search TextBox to filter the 80+ filters by name/description | ❌ Not Started |
| VE-002 | Filter Favorites | ⭐ | Add star button to mark frequently-used filters | ❌ Not Started |
| VE-003 | Track Lock UI | ⭐ | Add lock icon to track headers (model already exists) | ❌ Not Started |
| VE-004 | Track Solo/Mute UI | ⭐ | Add solo/mute buttons to track headers | ❌ Not Started |
| VE-005 | Timeline Ruler Click-to-Seek | ⭐ | Click on ruler to move playhead instantly | ❌ Not Started |

### Quick Wins (⭐⭐ = 3-4 hours each)

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| VE-006 | Filter Presets (Save/Load) | ⭐⭐ | Save current filter settings as named presets, load later | ❌ Not Started |
| VE-007 | Timeline Snapping | ⭐⭐ | Snap clips to playhead, other clip edges, markers | ❌ Not Started |
| VE-008 | Clip Markers | ⭐⭐ | Add markers within clips for sync points and notes | ❌ Not Started |
| VE-009 | Magnetic Timeline | ⭐⭐ | Auto-close gaps when deleting clips (ripple delete) | ❌ Not Started |
| VE-010 | Project Auto-Save | ⭐⭐ | Auto-save project every N minutes with recovery | ❌ Not Started |

### Medium Effort (⭐⭐⭐ = 5-8 hours each)

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| VE-011 | Audio Waveforms on Clips | ⭐⭐⭐ | Display audio waveform visualization on timeline clips | ❌ Not Started |
| VE-012 | Keyframe Editor Panel | ⭐⭐⭐ | Visual keyframe editor for filter parameters with bezier curves | ❌ Not Started |
| VE-013 | Thumbnail Strip for Clips | ⭐⭐⭐ | Show video frame thumbnails on timeline clips | ❌ Not Started |
| VE-014 | Ripple/Rolling Edit Modes | ⭐⭐⭐ | Advanced edit modes that shift/adjust adjacent clips | ❌ Not Started |
| VE-015 | Export Presets Panel | ⭐⭐⭐ | Export presets for YouTube, Vimeo, Instagram, TikTok, etc. | ❌ Not Started |

---

## 📋 Implementation Order (Recommended)

### Phase 1: Quick UI Improvements (Week 1)
1. ✅ **VE-001**: Filter Search Box - immediate usability win for 80+ filters
2. ✅ **VE-002**: Filter Favorites - personalization
3. ✅ **AP-001**: Track Rating UI - model exists, just needs UI
4. ✅ **AP-002**: Play Speed Control - simple MediaPlayer property
5. ✅ **AP-003**: Media Key Support - system hook

### Phase 2: Enhanced Editing (Week 2)
6. ✅ **VE-003/004**: Track Lock/Solo/Mute UI
7. ✅ **VE-007**: Timeline Snapping
8. ✅ **VE-010**: Project Auto-Save
9. ✅ **AP-006**: 10-Band EQ UI

### Phase 3: Rich Features (Week 3)
10. ✅ **AP-007**: LRC Lyrics Parsing
11. ✅ **AP-008**: Smart Playlists
12. ✅ **VE-006**: Filter Presets
13. ✅ **VE-008**: Clip Markers

### Phase 4: Advanced (Week 4+)
14. ✅ **VE-011**: Audio Waveforms on Clips
15. ✅ **VE-012**: Keyframe Editor Panel
16. ✅ **AP-011**: Gapless Playback
17. ✅ **AP-009**: Mini Player Mode

---

## 🔗 Related Documentation

- [TODO.md](TODO.md) - Full project TODO list
- [IMPLEMENTATION_MANIFEST.md](IMPLEMENTATION_MANIFEST.md) - Feature specifications
- [PROJECT_DOCUMENTATION.md](PROJECT_DOCUMENTATION.md) - File structure reference

---

## Summary

| Category | Immediate (⭐) | Quick Wins (⭐⭐) | Medium (⭐⭐⭐) | Total |
|----------|----------------|------------------|----------------|-------|
| Audio Player | 5 | 5 | 5 | **15** |
| Video Editor | 5 | 5 | 5 | **15** |
| **Total** | **10** | **10** | **10** | **30** |

**Estimated Total Time**: ~80-120 hours for all 30 features

---

## 🔧 Code Optimization & Architecture (PRIORITY - Do First)

**Backup Location**: `LocalArchive\CodeBackup_20260119_165720\`

### Immediate Priority (Do Before Features)

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| OPT-001 | Tab Visibility Settings | ⭐⭐ | Add settings to control which tabs are shown at app startup | ✅ Completed |
| OPT-002 | Independent Tab Launch | ⭐⭐ | Ensure each tab can be launched/loaded independently without dependencies | ✅ Completed |
| OPT-003 | Lazy Tab Loading | ⭐⭐ | Only load tab content when first accessed (reduce startup time) | ✅ Completed |
| OPT-004 | ViewModel Base Consolidation | ⭐⭐ | Consolidate common ViewModel patterns (INotifyPropertyChanged, Commands) | ✅ Completed |
| OPT-005 | Service Singleton Optimization | ⭐ | Review singleton services for proper lifecycle management | ✅ Completed |

**Implemented Changes (OPT-001 to OPT-005)**:
- `SettingsManager.cs`: Extended `AppSettings` with `VisibleTabs` dictionary and INotifyPropertyChanged
- `TabVisibilityService.cs`: New singleton service with visibility properties for all tabs
- `SettingsWindow.xaml`: Added "Tab Visibility" settings panel with checkboxes for all tabs
- `SettingsWindow.xaml.cs`: Added tab visibility load/save methods with immediate UI refresh
- `MainWindow.xaml`: Bound all TabItem visibility to TabVisibilityService.Instance
- `MainWindowViewModel.cs`: Converted all 35 ViewModels to use `Lazy<T>` for on-demand creation
- **OPT-004**: Refactored 12 ViewModels/models to use `BindableBase` instead of duplicating INotifyPropertyChanged:
  - `DuplicateFileViewModel`, `HiderEditViewModel`, `VideoConverterViewModel`
  - `SystemAuditViewModel`, `MultimediaEditorViewModel`, `PluginManagerViewModel`, `PluginInfo`
  - `ForensicsAnalyzerViewModel`, `MetadataEditorViewModel`, `MetadataTag`, `FolderFileMetadata`
  - `VideoEditorViewModel` (with IDisposable preserved)
- **OPT-005**: Created `ServiceLocator.cs` for singleton service access:
  - Shared services: FFmpeg, FFprobe, BeatDetection, TimelineOperations, KeyframeInterpolator
  - Core services: FileRenamer, VideoConverter, VideoCombiner, Upscaler, DiskSpaceAnalyzer
  - System services: ProcessManager, ScheduledTasks, StartupManager, SystemRestore, PdfTools
  - Updated 10 ViewModels to use ServiceLocator instead of creating new instances

### Code Deduplication

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| OPT-006 | Command Pattern Standardization | ⭐⭐ | Standardize RelayCommand/AsyncRelayCommand across all ViewModels | ✅ Completed |
| OPT-007 | File Dialog Helper | ⭐ | Create unified file/folder dialog service to replace duplicated code | ✅ Completed |
| OPT-008 | Progress Reporting Consolidation | ⭐⭐ | Unified progress reporting pattern for all long-running operations | ⏸️ Deferred |
| OPT-009 | Theme/Style Consolidation | ⭐⭐ | Merge duplicated styles across resource dictionaries | ✅ Completed |
| OPT-010 | Settings Service Optimization | ⭐ | Centralize settings access patterns | ✅ Completed |

**Implemented Changes (OPT-006)**:
- Enhanced `AsyncRelayCommand.cs` with three command variants:
  - `AsyncRelayCommand`: Parameterless async command with execution tracking
  - `AsyncRelayCommand<T>`: Generic typed parameter async command (moved from AudioPlayerViewModel)
  - `AsyncParameterCommand`: Object parameter async command
- All async commands now include:
  - `_isExecuting` guard to prevent double-execution
  - Automatic CanExecute refresh on start/end
  - Try-catch with MessageBox error display
- Removed duplicate `AsyncRelayCommand<T>` from AudioPlayerViewModel

**Implemented Changes (OPT-007)**:
- `FileDialogService.cs`: New static service with unified dialog methods:
  - `BrowseForFolder()`, `BrowseForSourceFolder()`, `BrowseForOutputFolder()` - Folder selection
  - `OpenFile()`, `OpenFiles()`, `SaveFile()` - File selection with filters
  - `OpenVideoFiles()`, `OpenAudioFiles()`, `OpenImageFiles()`, `OpenMediaFiles()`, `OpenPdfFiles()` - Specialized dialogs
  - Standard filter constants: `VideoFilter`, `AudioFilter`, `ImageFilter`, `MediaFilter`, `PdfFilter`
- Updated ViewModels to use FileDialogService: VideoConverterViewModel, UpscalerViewModel, VideoCombinerViewModel, FileCleanerViewModel

**Implemented Changes (OPT-009)**:
- Theme files (Light.xaml, Dark.xaml) already have matching resource keys
- Both themes provide aliases for backward compatibility (e.g., `TextSecondary`, `SecondaryTextBrush`, `TextSecondaryBrush`)
- VideoEditorStyles.xaml defines specialized styles used by video editor views

**Implemented Changes (OPT-010)**:
- `SettingsManager.cs` now has `Current` property for cached singleton access
- Added `SaveCurrent()` method for saving cached settings
- Updated all settings consumers to use `SettingsManager.Current` instead of `Load()`:
  - MainWindowViewModel, UpdateViewModel, SettingsWindow.xaml.cs
- Reduces redundant file I/O on settings access

### Performance Improvements

| # | Task | Effort | Description | Status |
|---|------|--------|-------------|--------|
| OPT-011 | Async Initialization | ⭐⭐ | Move heavy initialization to async methods | ✅ Completed |
| OPT-012 | Memory Usage Optimization | ⭐⭐⭐ | Review image/video handling for memory leaks | ❌ Not Started |
| OPT-013 | UI Virtualization | ⭐⭐ | Ensure all large lists use virtualization | ✅ Completed |
| OPT-014 | Startup Time Optimization | ⭐⭐ | Profile and reduce app startup time | ❌ Not Started |
| OPT-015 | Dispose Pattern Review | ⭐⭐ | Ensure proper IDisposable implementation | ❌ Not Started |

**Implemented Changes (OPT-011)**:
- Created `IAsyncInitializable.cs` interface for ViewModels requiring async initialization
- Created `AsyncBindableBase.cs` base class with thread-safe async initialization support:
  - `IsInitialized` / `IsInitializing` properties for UI binding
  - `InitializeAsync()` method that runs only once (safe to call multiple times)
  - `OnInitializeAsync()` abstract method for derived classes to override
  - `OnInitializationError()` virtual method for error handling
  - `ResetInitialization()` for refresh scenarios
- Updated ViewModels to use `AsyncBindableBase`:
  - `PluginManagerViewModel` - Defers plugin discovery until tab is shown
  - `StagingViewModel` - Defers staged file loading until window is shown
  - `TimelineViewModel` - Defers transition loading and track creation
- Updated `LazyTabContent` control to trigger async initialization when tab becomes visible
- Updated views to trigger async initialization on Loaded event:
  - `PluginManagerView`, `StagingWindow`, `TimelineControl`
- Benefits: Faster startup, reduced blocking I/O during construction, tab-by-tab initialization

**Implemented Changes (OPT-013)**:
- Added `VirtualizingStackPanel.IsVirtualizing="True"` and `VirtualizingStackPanel.VirtualizationMode="Recycling"` to all high-impact list controls
- Added `EnableRowVirtualization="True"` to DataGrids for efficient row recycling
- Updated views with virtualization:
  - **AudioPlayerView.xaml** - QueueListBox (music queue can have many tracks)
  - **DuplicatesView.xaml** - DataGrid (can display thousands of duplicate files)
  - **ProcessManagerView.xaml** - DataGrid (hundreds of running processes)
  - **MediaLibraryView.xaml** - DataGrid (large media libraries)
  - **FileCleanerView.xaml** - PreviewDataGrid (batch rename operations)
  - **ScheduledTasksView.xaml** - TasksDataGrid (many scheduled tasks)
  - **StartupManagerView.xaml** - DataGrid (startup items)
  - **NetworkToolsView.xaml** - ConnectionsDataGrid and AdaptersDataGrid
- Benefits: Reduced memory usage, smoother scrolling, faster rendering for large datasets

### Requirements

1. **NO functionality removal** - All existing features must remain
2. **Tab Independence** - Each tab must work standalone
3. **Settings Persistence** - Tab visibility settings saved to user preferences
4. **Immediate UI Update** - When tab setting changes, UI updates instantly (no restart)

---

*Last updated: January 20, 2026*
