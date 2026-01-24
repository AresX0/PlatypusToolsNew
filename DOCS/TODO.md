# PlatypusTools v3.4.0 - Detailed TODO List

**Branch**: `main`  
**Last Updated**: January 27, 2026  
**Current Version**: v3.2.12.1 (released)  
**Legend**: ✅ Complete | 🔄 In Progress | ❌ Not Started

---

## Recently Completed (v3.2.1)

### New Features (v3.2.1)
- [x] **Shotcut-Inspired Video Editor Roadmap** - Added 64 tasks for professional NLE features ✅
- [x] **Video Editor Insert Clip Fix** - Fixed compile errors in insert at playhead ✅

### Bug Fixes (v3.2.0.1)
- [x] **Queue Display Bug** - Fixed ItemsSource binding issue in Audio Player queue ✅
- [x] **Crossfade Volume Bug** - Volume now properly restores after crossfade ✅
- [x] **Renamed Batch Upscaler → Image Scaler** - Moved to Image tab section ✅
- [x] **Video Combiner Transitions** - Added 10 transition types with duration control ✅

---

## Recently Completed (v3.3.0)

### New Features (v3.3.0)
- [x] **Scrolling Screenshot Capture** - Capture entire scrolling content (TASK-134) ✅
  - Automatically scrolls and stitches images together
  - Works with browsers, documents, and any scrollable window
  - Added 📜 Scroll button to Screenshot tool toolbar
- [x] **Audio Crossfade UI** - Visual controls for track transitions ✅
  - Enable/disable crossfade with checkbox
  - Adjustable duration slider (0-5 seconds)
  - Smooth fade between tracks already implemented in service
- [x] **Transition Preview Animation** - Live preview in transition picker (TASK-047) ✅
  - Play button to preview selected transition
  - Animated before/after panels showing transition effect
  - Supports fade, slide, wipe, and zoom transitions
- [x] **Synchronized Comparison Zoom** - Linked zoom/pan for image comparison (TASK-062) ✅
  - Zoom in/out buttons with Ctrl+scroll support
  - Reset and Fit to View buttons
  - Sync Pan toggle to link both images
  - Works in Slider, Side-by-Side, and Overlay modes

---

## Recently Completed (v3.2.0)

### New Features (v3.2.0)
- [x] **Forensics Analyzer** - New security tool for system forensic analysis ✅
  - Lightweight Mode - Quick scan using single core, ~100MB output
  - Deep Mode - Comprehensive analysis using all cores, GB-scale output
  - Memory Analysis - Process inspection, suspicious process detection
  - File System Analysis - Executable scanning, hidden file detection, SHA256 hashing
  - Registry Analysis - Startup entry enumeration, suspicious run key detection
  - Log Aggregation - Windows Event Log parsing, security event analysis
  - Export Report - Generate detailed forensics reports in JSON or text format
- [x] **Sleep Timer** - Audio Player enhancement with automatic playback stop ✅
  - Set timer (15, 30, 45, 60, 90, 120 minutes)
  - Countdown display in status bar
  - Gradual fade-out before stopping
  - Cancel timer option
- [x] **Plugin Manager UI** - New tool for managing plugins ✅
  - Browse installed plugins with status
  - Install new plugins from file
  - Enable/disable plugins without uninstalling
  - Uninstall plugins with confirmation
  - Reload all plugins to apply changes
  - View plugin details (version, author, description)

### Bug Fixes (v3.2.0)
- [x] **Audio Player Library Persistence** - Library folders and tracks now properly load on startup ✅
- [x] **Audio Player Play Selected** - Play Selected button now correctly plays the highlighted track ✅
- [x] **Audio Player Organize By** - Organize dropdown now properly filters tracks by Artist, Album, Genre, etc. ✅
- [x] **Audio Player DataGrid Init** - Fixed exception during initialization that prevented library loading ✅

---

## Recently Completed (v3.1.0 - v3.1.2)

### New Features (v3.1.2)
- [x] **Video Similarity Detection** - Find duplicate videos using perceptual hashing ✅
- [x] **Transition Picker UI** - Visual transition selector for video editor ✅
- [x] **PDF Encryption/Decryption** - Encrypt and decrypt PDF files with password ✅
- [x] **Timeline Drag-Drop** - Drag media files directly onto timeline ✅
- [x] **Metadata Template UI** - Full template management with import/export ✅
- [x] **Archive Password & Split** - Password protect and split large archives ✅
- [x] **Recent Workspaces UI** - Enhanced recent workspaces with tabs and search ✅
- [x] **Help Menu Fix** - Fixed WebView2 access denied error ✅
- [x] **Tools Menu Fix** - Fixed error handling for Tools menu operations ✅

### Native Multimedia Controls (v3.1.0 - v3.1.1)
- [x] **Native Video Player** with fullscreen popup controls ✅
- [x] **Native Audio Trimmer** using FFmpeg ✅
- [x] **Native Image Editor** using ImageSharp ✅
- [x] **Hierarchical Multimedia Tab** reorganization ✅
- [x] **System Audit Auto-Fix** for Windows Defender, Firewall, and UAC ✅
- [x] **Updated Help Documentation** comprehensive HTML help file ✅
- [x] **About Dialog** updated with v3.1.1 features ✅

---

## Phase 1: UI Foundation

### 1.1 Status Bar
- [x] **TASK-001**: Create `StatusBarControl.xaml` custom control ✅
- [x] **TASK-002**: Create `StatusBarViewModel.cs` with progress tracking ✅
- [x] **TASK-003**: Add status bar to `MainWindow.xaml` ✅
- [x] **TASK-004**: Wire up status bar to all long-running operations ✅
- [x] **TASK-005**: Add cancel button functionality ✅

### 1.2 Keyboard Shortcuts
- [x] **TASK-006**: Create `KeyboardShortcutService.cs` for hotkey management ✅
- [x] **TASK-007**: Create `KeyboardShortcutsViewModel.cs` for settings UI ✅
- [x] **TASK-008**: Create `KeyboardShortcutsView.xaml` (in SettingsWindow) settings page ✅
- [x] **TASK-009**: Define default shortcuts in `shortcuts.json` ✅
- [x] **TASK-010**: Integrate shortcuts with all commands ✅
- [x] **TASK-011**: Add shortcut display to menu items ✅

### 1.3 Recent Workspaces
- [x] **TASK-012**: Create `RecentWorkspacesService.cs` ✅
- [x] **TASK-013**: Add recent workspaces to File menu ✅
- [x] **TASK-014**: Create `RecentWorkspacesView.xaml` in Home tab ✅
- [x] **TASK-015**: Implement pin/unpin functionality ✅
- [x] **TASK-016**: Add "Clear Recent" option ✅
- [x] **TASK-017**: Persist recent workspaces to settings ✅

### 1.4 Settings Backup/Restore
- [x] **TASK-018**: Create `SettingsBackupService.cs` ✅ *Full implementation*
- [x] **TASK-019**: Export settings to JSON/ZIP ✅ *CreateBackupAsync in SettingsBackupService*
- [x] **TASK-020**: Import settings from backup ✅ *RestoreBackupAsync in SettingsBackupService*
- [x] **TASK-021**: Add Settings Backup/Restore UI in settings ✅ *Export/Import buttons in SettingsWindow*

---

## Phase 2: Enhanced Existing Tools

### 2.1 Video Editor Timeline
- [x] **TASK-022**: Design timeline data models (`TimelineTrack.cs`, `TimelineClip.cs`) ✅ *Full implementation*
- [x] **TASK-023**: Create `TimelineControl.xaml` custom control ✅ *With playhead, clips*
- [x] **TASK-024**: Implement timeline ruler with zoom ✅ *ZoomLevel in TimelineViewModel*
- [x] **TASK-025**: Create `TimelineTrackControl.xaml` for track headers ✅ *In TimelineControl*
- [x] **TASK-026**: Create `TimelineClipControl.xaml` for clip display ✅ *TimelineClip in ViewModel*
- [x] **TASK-027**: Implement playhead with scrubbing ✅ *PlayheadPosition property*
- [x] **TASK-028**: Create `TimelineViewModel.cs` ✅ *Full implementation*
- [x] **TASK-029**: Implement drag-and-drop clips ✅ *External file drag-drop support*
- [x] **TASK-030**: Implement clip trimming handles ✅ *TrimClip method*
- [x] **TASK-031**: Implement clip splitting ✅ *SplitClip method*
- [x] **TASK-032**: Implement undo/redo for timeline actions

### 2.2 Video Editor Multi-Track
- [x] **TASK-033**: Add track type enumeration (Video, Audio, Title, Effects)
- [x] **TASK-034**: Implement track add/remove
- [x] **TASK-035**: Implement track reordering
- [x] **TASK-036**: Implement track visibility toggle
- [x] **TASK-037**: Implement track mute/solo
- [x] **TASK-038**: Implement track lock

### 2.3 Video Editor Transitions
- [x] **TASK-039**: Create `Transition.cs` model ✅
- [x] **TASK-040**: Create `TransitionService.cs` with FFmpeg filters ✅
- [x] **TASK-041**: Create `TransitionPickerView.xaml` UI ✅
- [x] **TASK-042**: Implement fade transitions (in, out, cross) ✅
- [x] **TASK-043**: Implement wipe transitions ✅
- [x] **TASK-044**: Implement slide transitions ✅
- [x] **TASK-045**: Implement zoom transitions ✅
- [x] **TASK-046**: Add transition duration control ✅
- [x] **TASK-047**: Add transition preview ✅

### 2.4 Image Scaler Batch Processing
- [x] **TASK-048**: Create `BatchUpscaleJob.cs` model
- [x] **TASK-049**: Create `BatchUpscaleService.cs`
- [x] **TASK-050**: Create `BatchUpscaleViewModel.cs`
- [x] **TASK-051**: Add batch queue UI to Image Scaler
- [x] **TASK-052**: Implement add files/folders to queue
- [ ] **TASK-053**: Implement per-item settings override
- [x] **TASK-054**: Implement progress tracking
- [x] **TASK-055**: Implement pause/resume/cancel
- [x] **TASK-056**: Implement parallel processing option
- [x] **TASK-057**: Implement output naming patterns

### 2.5 Image Scaler Comparison Preview
- [x] **TASK-058**: Create `ComparisonViewer.xaml` control
- [x] **TASK-059**: Implement slider comparison mode
- [x] **TASK-060**: Implement side-by-side mode
- [x] **TASK-061**: Implement overlay toggle mode ✅
- [x] **TASK-062**: Implement synchronized zoom/pan ✅
- [x] **TASK-063**: Add comparison view to Image Scaler ✅

### 2.6 Metadata Template Presets
- [x] **TASK-064**: Create `MetadataTemplate.cs` model ✅
- [x] **TASK-065**: Create `MetadataTemplateService.cs` ✅
- [x] **TASK-066**: Create template save/load UI ✅
- [x] **TASK-067**: Implement "Save as Template" feature ✅
- [x] **TASK-068**: Implement template management (list, delete, rename) ✅
- [x] **TASK-069**: Implement "Apply Template" with merge/replace modes ✅

### 2.7 Metadata Batch Copy
- [x] **TASK-070**: Create batch metadata copy UI (MetadataTemplateViewModel)
- [x] **TASK-071**: Implement source file selection
- [ ] **TASK-072**: Implement tag selection (checkboxes)
- [x] **TASK-073**: Implement target file selection
- [ ] **TASK-074**: Implement preview before apply
- [x] **TASK-075**: Implement batch apply with progress

### 2.8 Duplicate Finder Image Similarity
- [x] **TASK-076**: Create `ImageSimilarityService.cs` ✅ *Full implementation*
- [x] **TASK-077**: Implement pHash (perceptual hash) ✅ *CalculatePerceptualHash method*
- [x] **TASK-078**: Implement dHash (difference hash) ✅ *CalculateDifferenceHash method*
- [x] **TASK-079**: Implement aHash (average hash) ✅ *CalculateAverageHash method*
- [x] **TASK-080**: Create similarity threshold UI ✅ *In DuplicatesView*
- [x] **TASK-081**: Create grouped results view ✅ *GroupedDuplicates in ViewModel*
- [x] **TASK-082**: Add similarity percentage display ✅ *CalculateSimilarity returns percentage*
- [x] **TASK-083**: Add visual comparison grid ✅ *DataGrid with thumbnails*

### 2.9 Duplicate Finder Video Similarity
- [x] **TASK-084**: Create `VideoSimilarityService.cs` ✅
- [x] **TASK-085**: Implement key frame extraction ✅
- [x] **TASK-086**: Implement frame hash comparison ✅
- [x] **TASK-087**: Implement duration tolerance ✅
- [x] **TASK-088**: Add video thumbnail preview ✅
- [x] **TASK-089**: Add configurable frame sample rate ✅

---

## Phase 3: New Tools

### 3.1 Batch Watermarking
- [x] **TASK-090**: Create `WatermarkService.cs` ✅ *Full implementation*
- [x] **TASK-091**: Create `BatchWatermarkViewModel.cs` ✅ *Full implementation*
- [x] **TASK-092**: Create `BatchWatermarkView.xaml` ✅ *In navigation*
- [x] **TASK-093**: Implement text watermark (font, color, opacity) ✅
- [x] **TASK-094**: Implement image watermark (PNG with alpha) ✅
- [x] **TASK-095**: Implement position options (corners, center, tiled, custom) ✅
- [x] **TASK-096**: Implement preview ✅
- [x] **TASK-097**: Implement batch processing for images ✅
- [x] **TASK-098**: Implement video watermarking via FFmpeg ✅
- [x] **TASK-099**: Add Watermark tool to navigation ✅

### 3.2 PDF Tools
- [x] **TASK-100**: Add PDFsharp NuGet package ✅ *PdfSharpCore*
- [x] **TASK-101**: Create `PdfService.cs` ✅ *Full implementation*
- [x] **TASK-102**: Create `PdfToolsViewModel.cs` ✅
- [x] **TASK-103**: Create `PdfToolsView.xaml` ✅
- [x] **TASK-104**: Implement PDF merge ✅
- [x] **TASK-105**: Implement PDF split ✅
- [x] **TASK-106**: Implement page extraction ✅
- [x] **TASK-107**: Implement PDF compression ✅
- [x] **TASK-108**: Implement PDF to images ✅
- [x] **TASK-109**: Implement images to PDF ✅
- [x] **TASK-110**: Implement PDF rotation ✅
- [x] **TASK-111**: Implement PDF encryption/decryption ✅
- [x] **TASK-112**: Implement PDF watermark ✅
- [x] **TASK-113**: Add PDF Tools to navigation ✅

### 3.3 Archive Manager
- [x] **TASK-114**: Add SharpCompress NuGet package ✅ *SharpCompress 0.38.0*
- [x] **TASK-115**: Create `ArchiveService.cs` ✅ *Full implementation*
- [x] **TASK-116**: Create `ArchiveManagerViewModel.cs` ✅
- [x] **TASK-117**: Create `ArchiveManagerView.xaml` ✅
- [x] **TASK-118**: Implement archive browsing ✅
- [x] **TASK-119**: Implement ZIP creation ✅
- [x] **TASK-120**: Implement 7z creation (via 7z.dll) ✅ *SharpCompress supports 7z*
- [x] **TASK-121**: Implement archive extraction (ZIP, 7z, RAR, TAR) ✅
- [x] **TASK-122**: Implement selective extraction ✅
- [x] **TASK-123**: Implement password protection ✅
- [x] **TASK-124**: Implement compression levels ✅
- [x] **TASK-125**: Implement split archives ✅
- [x] **TASK-126**: Add Archive Manager to navigation ✅

### 3.4 Screenshot Tool
- [x] **TASK-127**: Create `ScreenCaptureService.cs` ✅ *Full implementation*
- [x] **TASK-128**: Create `ScreenshotViewModel.cs` ✅
- [x] **TASK-129**: Create `ScreenshotView.xaml` ✅
- [x] **TASK-130**: Create `RegionSelectWindow.xaml` overlay ✅
- [x] **TASK-131**: Implement full screen capture ✅
- [x] **TASK-132**: Implement active window capture ✅
- [x] **TASK-133**: Implement region selection ✅
- [x] **TASK-134**: Implement scrolling capture ✅
- [x] **TASK-135**: Create annotation toolbar ✅
- [x] **TASK-136**: Implement arrow annotation ✅
- [x] **TASK-137**: Implement rectangle annotation ✅
- [x] **TASK-138**: Implement ellipse annotation ✅
- [x] **TASK-139**: Implement freehand annotation ✅
- [x] **TASK-140**: Implement text annotation ✅
- [x] **TASK-141**: Implement blur/pixelate tool ✅
- [x] **TASK-142**: Implement highlight tool ✅
- [x] **TASK-143**: Implement copy to clipboard ✅
- [x] **TASK-144**: Implement save to file ✅
- [x] **TASK-145**: Add Screenshot Tool to navigation ✅

---

## Phase 4: System Features

### 4.1 Auto-Update
- [x] **TASK-146**: Add Octokit NuGet package ✅ *Octokit 14.0.0*
- [x] **TASK-147**: Create `UpdateService.cs` ✅ *Full implementation*
- [x] **TASK-148**: Create `UpdateViewModel.cs` ✅ *In UpdateService*
- [x] **TASK-149**: Create `UpdateView.xaml` notification dialog ✅
- [x] **TASK-150**: Implement GitHub releases API check ✅ *CheckForUpdatesAsync*
- [x] **TASK-151**: Implement version comparison ✅ *CompareVersions method*
- [x] **TASK-152**: Implement download progress ✅ *IProgress support*
- [x] **TASK-153**: Implement installer launch ✅ *LaunchInstallerAsync*
- [x] **TASK-154**: Add "Check for Updates" menu item ✅ *In MainWindow menu*
- [x] **TASK-155**: Add update check on startup setting ✅

### 4.2 Plugin/Extension System
- [x] **TASK-156**: Create `PlatypusTools.Plugins.SDK` project ✅ *PluginService.cs*
- [x] **TASK-157**: Define `IPlugin` interface ✅
- [x] **TASK-158**: Define `IToolPlugin` interface ✅
- [x] **TASK-159**: Define `IVisualizerPlugin` interface ✅
- [x] **TASK-160**: Define `IFileProcessorPlugin` interface ✅ *IFileProcessorPlugin*
- [x] **TASK-161**: Create `PluginManifest.cs` model ✅
- [x] **TASK-162**: Create `PluginLoader.cs` service ✅ *In PluginService*
- [x] **TASK-163**: Create `PluginManagerViewModel.cs` ✅ *v3.2.0*
- [x] **TASK-164**: Create `PluginManagerView.xaml` ✅ *v3.2.0*
- [x] **TASK-165**: Implement plugin discovery ✅ *ScanPlugins*
- [x] **TASK-166**: Implement plugin loading/unloading ✅ *LoadPlugin/UnloadPlugin*
- [ ] **TASK-167**: Implement plugin sandboxing
- [ ] **TASK-168**: Create sample plugin
- [x] **TASK-169**: Add Plugin Manager to settings ✅ *v3.2.0*

### 4.3 Logging UI
- [x] **TASK-170**: Create `LogViewerViewModel.cs` ✅ *Full implementation*
- [x] **TASK-171**: Create `LogViewerWindow.xaml` ✅
- [x] **TASK-172**: Implement real-time log display ✅
- [x] **TASK-173**: Implement log level filtering ✅
- [x] **TASK-174**: Implement log search ✅
- [x] **TASK-175**: Implement log export ✅
- [x] **TASK-176**: Implement log clear ✅
- [x] **TASK-177**: Implement auto-scroll toggle ✅
- [x] **TASK-178**: Add "View Logs" menu item ✅ *In MainWindow menu*

---

## Phase 5: Audio Player C++ Core

### 5.1 Project Setup
- [ ] **TASK-179**: Create `CppAudioCore` C++ static library project
- [ ] **TASK-180**: Configure vcpkg for dependencies
- [ ] **TASK-181**: Add FFmpeg libraries
- [ ] **TASK-182**: Add TagLib library
- [ ] **TASK-183**: Add PortAudio library
- [ ] **TASK-184**: Add KissFFT library
- [ ] **TASK-185**: Add libebur128 library
- [ ] **TASK-186**: Create common types header (`Types.h`)
- [ ] **TASK-187**: Create callbacks header (`Callbacks.h`)

### 5.2 ConverterService
- [ ] **TASK-188**: Create `ConverterService.h` interface
- [ ] **TASK-189**: Implement `ConverterService.cpp`
- [ ] **TASK-190**: Implement MP3 encoding (LAME)
- [ ] **TASK-191**: Implement AAC encoding
- [ ] **TASK-192**: Implement FLAC encoding
- [ ] **TASK-193**: Implement OGG Vorbis encoding
- [ ] **TASK-194**: Implement WAV/AIFF encoding
- [ ] **TASK-195**: Implement Opus encoding
- [ ] **TASK-196**: Implement bitrate/sample rate control
- [ ] **TASK-197**: Implement loudness normalization (EBU R128)
- [ ] **TASK-198**: Implement fade in/out
- [ ] **TASK-199**: Implement trim/crop
- [ ] **TASK-200**: Implement progress callbacks

### 5.3 PlayerService
- [ ] **TASK-201**: Create `PlayerService.h` interface
- [ ] **TASK-202**: Implement `PlayerService.cpp`
- [ ] **TASK-203**: Implement audio decoding
- [ ] **TASK-204**: Implement PortAudio output
- [ ] **TASK-205**: Implement play/pause/stop
- [ ] **TASK-206**: Implement seek
- [ ] **TASK-207**: Implement volume control
- [ ] **TASK-208**: Implement gapless playback
- [x] **TASK-209**: Implement crossfade ✅ *(UI + managed implementation)*
- [ ] **TASK-210**: Implement 10-band EQ
- [ ] **TASK-211**: Implement preamp gain
- [ ] **TASK-212**: Implement playback speed
- [ ] **TASK-213**: Implement position callbacks

### 5.4 MetadataService
- [ ] **TASK-214**: Create `MetadataService.h` interface
- [ ] **TASK-215**: Implement `MetadataService.cpp`
- [ ] **TASK-216**: Implement tag reading (TagLib)
- [ ] **TASK-217**: Implement tag writing
- [ ] **TASK-218**: Implement album art extraction
- [ ] **TASK-219**: Implement album art embedding
- [ ] **TASK-220**: Support ID3v1/v2, Vorbis, APE, MP4

### 5.5 LyricsService
- [ ] **TASK-221**: Create `LyricsService.h` interface
- [ ] **TASK-222**: Implement `LyricsService.cpp`
- [ ] **TASK-223**: Implement LRC file parsing
- [ ] **TASK-224**: Implement ID3 USLT extraction
- [ ] **TASK-225**: Implement ID3 SYLT extraction
- [ ] **TASK-226**: Implement Vorbis lyrics extraction
- [ ] **TASK-227**: Implement timestamp parsing

### 5.6 LibraryService
- [ ] **TASK-228**: Create `LibraryService.h` interface
- [ ] **TASK-229**: Implement `LibraryService.cpp`
- [ ] **TASK-230**: Design SQLite schema
- [ ] **TASK-231**: Implement directory scanning
- \[ \] \*\*TASK-232\*\*: Implement file hashing \(for duplicate detection\)
- [ ] **TASK-232a**: Add faster folder scanning option (background incremental scan with file watcher)
  - **NOTE**: Audio library folder scanning should support a "quick scan" mode that:
    - Uses parallel directory enumeration
    - Caches folder modification timestamps
    - Only rescans changed folders
    - Provides real-time progress updates
    - Can be cancelled without losing partial results
- [ ] **TASK-233**: Implement CRUD operations
- [ ] **TASK-234**: Implement search/filter
- [ ] **TASK-235**: Implement playlist management

### 5.7 VisualizerPluginHost
- [ ] **TASK-236**: Create `VisualizerPluginHost.h` interface
- [ ] **TASK-237**: Implement `VisualizerPluginHost.cpp`
- [ ] **TASK-238**: Implement FFT (KissFFT)
- [ ] **TASK-239**: Implement spectrum analyzer
- [ ] **TASK-240**: Implement waveform display
- [ ] **TASK-241**: Implement VU meter
- [ ] **TASK-242**: Implement plugin loading interface
- [ ] **TASK-243**: Implement audio sample callbacks

---

## Phase 6: Audio Player C++/CLI Bridge

### 6.1 Wrapper Setup
- [ ] **TASK-244**: Create `CppAudioBridge` C++/CLI project
- [ ] **TASK-245**: Reference `CppAudioCore`
- [ ] **TASK-246**: Create marshaling utilities

### 6.2 Service Wrappers
- [ ] **TASK-247**: Create `ConverterServiceWrapper.h/cpp`
- [ ] **TASK-248**: Create `PlayerServiceWrapper.h/cpp`
- [ ] **TASK-249**: Create `MetadataServiceWrapper.h/cpp`
- [ ] **TASK-250**: Create `LyricsServiceWrapper.h/cpp`
- [ ] **TASK-251**: Create `LibraryServiceWrapper.h/cpp`
- [ ] **TASK-252**: Create `VisualizerServiceWrapper.h/cpp`

### 6.3 Event Marshaling
- [ ] **TASK-253**: Implement progress event marshaling
- [ ] **TASK-254**: Implement position event marshaling
- [ ] **TASK-255**: Implement audio sample event marshaling
- [ ] **TASK-256**: Implement error event marshaling

### 6.4 Async Patterns
- [ ] **TASK-257**: Implement Task-based async wrappers
- [ ] **TASK-258**: Implement CancellationToken support
- [ ] **TASK-259**: Implement IProgress support

---

## Phase 7: Audio Player .NET UI

### 7.1 ViewModels
- [ ] **TASK-260**: Create `PlayerViewModel.cs`
- [ ] **TASK-261**: Create `PlaylistViewModel.cs`
- [ ] **TASK-262**: Create `LibraryViewModel.cs`
- [ ] **TASK-263**: Create `LyricsViewModel.cs`
- [ ] **TASK-264**: Create `EQViewModel.cs`
- [ ] **TASK-265**: Create `VisualizerViewModel.cs`
- [ ] **TASK-266**: Create `ConverterViewModel.cs`

### 7.2 Views
- [ ] **TASK-267**: Create `PlayerView.xaml`
- [ ] **TASK-268**: Create `PlaylistView.xaml`
- [ ] **TASK-269**: Create `LibraryView.xaml`
- [ ] **TASK-270**: Create `LyricsView.xaml`
- [ ] **TASK-271**: Create `EQView.xaml`
- [ ] **TASK-272**: Create `VisualizerView.xaml`
- [ ] **TASK-273**: Create `ConverterView.xaml`

### 7.3 Player Controls
- [ ] **TASK-274**: Create playback controls (play, pause, stop, next, prev)
- [ ] **TASK-275**: Create seek slider
- [ ] **TASK-276**: Create volume slider
- [ ] **TASK-277**: Create time display
- [ ] **TASK-278**: Create album art display
- [ ] **TASK-279**: Create now playing info
- [ ] **TASK-280**: Create loop/shuffle toggles

### 7.4 Library UI
- [ ] **TASK-281**: Create artist list view
- [ ] **TASK-282**: Create album grid view
- [ ] **TASK-283**: Create track list view
- [ ] **TASK-284**: Create folder tree view
- [ ] **TASK-285**: Create search bar
- [ ] **TASK-286**: Create filter panel

### 7.5 Integration
- [ ] **TASK-287**: Add Audio Player to main navigation
- [ ] **TASK-288**: Create Audio Player landing page
- [ ] **TASK-289**: Implement keyboard shortcuts for player
- [ ] **TASK-290**: Implement system media transport controls

---

## Phase 8: Testing & Documentation

### 8.1 Unit Tests
- [ ] **TASK-291**: Write tests for StatusBarViewModel
- [ ] **TASK-292**: Write tests for KeyboardShortcutService
- [ ] **TASK-293**: Write tests for RecentWorkspacesService
- [ ] **TASK-294**: Write tests for BatchUpscaleService
- [ ] **TASK-295**: Write tests for ImageSimilarityService
- [ ] **TASK-296**: Write tests for MetadataTemplateService
- [ ] **TASK-297**: Write tests for PdfService
- [ ] **TASK-298**: Write tests for ArchiveService
- [ ] **TASK-299**: Write tests for UpdateService
- [ ] **TASK-300**: Write tests for PluginLoader

### 8.2 Integration Tests
- [ ] **TASK-301**: Write integration tests for Video Editor timeline
- [ ] **TASK-302**: Write integration tests for batch processing
- [ ] **TASK-303**: Write integration tests for audio conversion
- [ ] **TASK-304**: Write integration tests for audio playback

### 8.3 Documentation
- [x] **TASK-305**: Create PROJECT_DOCUMENTATION.md (file explanations) ✅
- [ ] **TASK-306**: Add XML doc comments to all public APIs
- [x] **TASK-307**: Create user guide ✅ *PlatypusTools_Help.html*
- [ ] **TASK-308**: Create plugin developer guide
- [x] **TASK-309**: Update README with new features ✅

---

## Phase 9: Future Refinements

### 9.1 Advanced Image Editing
- [ ] **TASK-310**: Add SixLabors.ImageSharp.Drawing package for advanced graphics
- [ ] **TASK-311**: Add SixLabors.Fonts package for text rendering on images
- [ ] **TASK-312**: Implement text overlay tool with custom fonts
- [ ] **TASK-313**: Implement shape drawing tools (rectangle, ellipse, line, polygon)
- [ ] **TASK-314**: Implement brush/pen stroke drawing
- [ ] **TASK-315**: Add watermark text tool with font selection
- [ ] **TASK-316**: Add image annotation features (arrows, callouts)
- [ ] **TASK-317**: Implement layer support for compositing

### 9.2 Image Processing Filters
- [ ] **TASK-318**: Add blur filters (Gaussian, Box, Motion)
- [ ] **TASK-319**: Add sharpen filter
- [ ] **TASK-320**: Add contrast/brightness adjustments
- [ ] **TASK-321**: Add saturation/hue adjustments
- [ ] **TASK-322**: Add sepia/vintage filters
- [ ] **TASK-323**: Add vignette effect
- [ ] **TASK-324**: Add crop tool with aspect ratio presets

---

## Truly Remaining Tasks (Actionable)

### Phase 2 (3 tasks remaining)
- [ ] **TASK-053**: Per-item settings override for batch upscale
- [ ] **TASK-072**: Tag selection checkboxes for batch metadata
- [ ] **TASK-074**: Preview before apply for batch metadata

### Phase 4 (2 tasks remaining)
- [ ] **TASK-167**: Plugin sandboxing (security isolation)
- [ ] **TASK-168**: Sample plugin with documentation

### Phase 8: Testing & Documentation (15 tasks)
- [ ] **TASK-291-300**: Unit tests for services
- [ ] **TASK-301-304**: Integration tests
- [ ] **TASK-306**: XML doc comments
- [ ] **TASK-308**: Plugin developer guide

### Phase 9: Future Enhancements (15 tasks)
- [ ] **TASK-310-317**: Advanced image editing (text overlay, shapes, layers)
- [ ] **TASK-318-324**: Image processing filters (blur, sharpen, contrast)

### Audio Player (Not in C++ phases)
- [ ] Gapless playback
- [ ] LRC lyrics parsing and display
- [ ] 10-band parametric EQ (currently 3-band)

---

## Priority Next Steps (v3.3.0 Candidates)

Based on remaining work and user value, here are the **recommended next features**:

### ✅ Completed in v3.3.0

1. ~~**TASK-047: Transition preview**~~ - ✅ Let users preview transitions before applying
2. ~~**TASK-062: Synchronized zoom/pan for comparison viewer**~~ - ✅ Better before/after comparison UX
3. ~~**TASK-134: Scrolling capture**~~ - ✅ Capture long web pages and documents
4. ~~**Audio Crossfade UI**~~ - ✅ Smooth transitions between songs with UI controls

### High Priority (Quick Wins - Remaining)

1. **TASK-053: Per-item settings override for batch upscale** - More flexibility per file
2. **TASK-072: Tag selection checkboxes for batch metadata** - Select which tags to copy
3. **TASK-074: Preview before apply for batch metadata** - See changes before committing

### Medium Priority (New Functionality)

4. **TASK-167: Plugin sandboxing** - Isolate plugins for security
5. **TASK-168: Sample plugin** - Help developers create plugins
6. **Advanced Image Editing (TASK-310-317)** - Text overlay, shape drawing, layers
7. **Image Processing Filters (TASK-318-324)** - Blur, sharpen, contrast, crop tools

### Audio Player Enhancements (Native .NET)

**Already Implemented:**
- ✅ **Playlist Management** - Create, edit, save playlists (`AudioLibraryViewModel.cs`, `Playlist` model)
- ✅ **Crossfade Between Tracks** - Smooth transitions (`AudioPlayerService.cs`)
- ✅ **EQ Controls** - 10-Band Equalizer (`EnhancedAudioPlayerService.cs`, `EqualizerSampleProvider`)
- ✅ **Visualizer** - Spectrum analyzer (`AudioVisualizerViewModel.cs`, `AudioVisualizerService.cs`)
- ✅ **Gapless Playback** - Seamless album playback (`EnhancedAudioPlayerService.cs` - PreloadNextTrack)
- ✅ **Lyrics Display** - LRC file parsing and synchronized lyrics (`LyricsService.cs`, `LyricsPanel`)
- ✅ **Track Rating** - Star rating UI widget
- ✅ **Play Speed Control** - 0.5x to 2x playback speed
- ✅ **Media Key Support** - Play/Pause/Next/Prev hardware keys
- ✅ **Queue Reorder** - Drag-and-drop queue management
- ✅ **Smart Playlists** - Auto-playlists based on rules
- ✅ **Mini Player Mode** - Compact floating window
- ✅ **Auto DJ / Radio Mode** - Auto-queue similar tracks
- ✅ **Scrobbling / Last.fm** - Track listening history
- ✅ **ReplayGain Support** - Volume normalization
- ✅ **Audio Converter Integration** - Convert tracks from library context menu

### System & Quality

16. **TASK-306: XML doc comments** - Document all public APIs
17. **TASK-308: Plugin developer guide** - Help third-party developers
18. **Unit Tests (TASK-291-300)** - Increase test coverage
19. **Integration Tests (TASK-301-304)** - End-to-end testing

---

## v3.2.0.1 Bug Fixes (Released)

### 🐛 Bug Fixes
- ✅ **Queue Display Bug** - ItemsSource binding was broken when using ToList()
- ✅ **Crossfade Volume Bug** - Volume now properly stored and restored after crossfade
- ✅ **Renamed Batch Upscaler → Image Scaler** - Moved to Image tab for better organization

### ✨ Enhancements
- ✅ **Video Combiner Transitions** - Added 10 transition types (fade, dissolve, wipe, slide, etc.)

---

## v3.3.0 Feature Set (Implemented)

### 🎯 Core Features (Complete)
1. ✅ **Scrolling Screenshot Capture** - Capture entire scrolling content
2. ✅ **Audio Crossfade UI** - Visual controls for smooth track transitions
3. ✅ **Transition Preview** - Preview video transitions before applying
4. ✅ **Synchronized Comparison Zoom** - Side-by-side zoom/pan for image comparison

### 🔧 Future Enhancements (v3.4.0 Candidates)
5. **Batch Metadata Tag Selection** - Choose which tags to copy/apply (TASK-072)
6. **Per-file Batch Upscale Settings** - Override settings per file (TASK-053)
7. **Sample Plugin + Developer Guide** - Bootstrap plugin ecosystem (TASK-168)
8. **Advanced Image Editing** - Text overlay, shape drawing, layers (TASK-310-317)

---

## Phase 10: Shotcut-Inspired Video Editor Enhancements

### 10.1 Multi-Track Timeline (Shotcut-Style)
- [x] **TASK-325**: Implement unlimited video/audio tracks (Shotcut supports unlimited) ✅
- [x] **TASK-326**: Add track headers with lock/hide/mute controls ✅
- [x] **TASK-327**: Implement track height resize (draggable dividers) ✅
- [ ] **TASK-328**: Add track compositing modes (over, add, saturate, multiply, screen)
- [ ] **TASK-329**: Implement keyframeable track blend/opacity
- [ ] **TASK-330**: Add track output routing (for multi-output export)

### 10.2 Advanced Clip Operations
- [x] **TASK-331**: Implement ripple edit (shift all clips when inserting/deleting) ✅
- [ ] **TASK-332**: Implement rolling edit (trim adjacent clips together)
- [ ] **TASK-333**: Implement slip edit (move clip content within boundaries)
- [ ] **TASK-334**: Implement slide edit (move clip while adjusting neighbors)
- [x] **TASK-335**: Add clip markers (for audio sync points, cue marks) ✅
- [ ] **TASK-336**: Implement clip speed ramping (keyframeable speed)
- [ ] **TASK-337**: Add reverse clip playback
- [ ] **TASK-338**: Implement freeze frame insertion

### 10.3 Keyframe Animation System
- [x] **TASK-339**: Create keyframe editor panel (similar to Shotcut's keyframes dock) ✅
- [x] **TASK-340**: Implement keyframe interpolation (linear, smooth, ease in/out) ✅
- [x] **TASK-341**: Add bezier curve editor for keyframes ✅
- [ ] **TASK-342**: Implement keyframe copy/paste across clips
- [ ] **TASK-343**: Add keyframe snapping to playhead/markers

### 10.4 Filters & Effects (Shotcut Has 300+)
- [x] **TASK-344**: Create filter dock/panel for browsing filters ✅
- [x] **TASK-345**: Implement filter search and categorization ✅
- [x] **TASK-346**: Add filter presets with save/load ✅
- [x] **TASK-347**: Implement chroma key (green screen) filter ✅
- [x] **TASK-348**: Implement stabilization filter (vidstab) ✅
- [x] **TASK-349**: Implement lens correction filter ✅
- [ ] **TASK-350**: Implement noise reduction filter
- [ ] **TASK-351**: Implement time remap filter (speed curves)
- [x] **TASK-352**: Implement 3-way color correction (shadows/mids/highlights) ✅
- [x] **TASK-353**: Implement LUT support (.cube, .3dl files) ✅
- [ ] **TASK-354**: Implement audio filters (compressor, limiter, EQ)

### 10.5 Text & Titles (Shotcut Text Features)
- [ ] **TASK-355**: Create title generator with templates
- [ ] **TASK-356**: Implement HTML-based rich text overlay (like Shotcut)
- [ ] **TASK-357**: Add scrolling text (credits, ticker)
- [ ] **TASK-358**: Implement 3D text with perspective
- [ ] **TASK-359**: Add text animation presets (fade, slide, typewriter)
- [ ] **TASK-360**: Implement text drop shadow and outline

### 10.6 Audio Features (Shotcut Audio)
- [x] **TASK-361**: Implement audio waveform display on timeline clips ✅
- [ ] **TASK-362**: Add audio peak meters panel
- [ ] **TASK-363**: Implement audio ducking (auto-lower music under voice)
- [x] **TASK-364**: Add audio fade handles on clips ✅
- [ ] **TASK-365**: Implement audio normalize filter
- [ ] **TASK-366**: Add voice-over recording directly to timeline

### 10.7 Preview & Playback
- [ ] **TASK-367**: Implement proxy editing (lower res for editing, full res for export)
- [ ] **TASK-368**: Add preview scaling options (1/4, 1/2, full resolution)
- [ ] **TASK-369**: Implement frame-accurate preview with shuttle/jog controls
- [ ] **TASK-370**: Add external monitor support
- [ ] **TASK-371**: Implement loop playback region (in/out points)

### 10.8 Export & Encoding (Shotcut Export Panel)
- [x] **TASK-372**: Create export panel with codec presets (YouTube, Vimeo, etc.) ✅
- [ ] **TASK-373**: Implement hardware encoding support (NVENC, QSV, AMF)
- [ ] **TASK-374**: Add multi-pass encoding for quality
- [ ] **TASK-375**: Implement chapter markers for MP4/MKV
- [ ] **TASK-376**: Add export queue for batch rendering
- [ ] **TASK-377**: Implement render preview (before full export)

### 10.9 Project & Workflow
- [x] **TASK-378**: Implement project auto-save and recovery ✅
- [ ] **TASK-379**: Add project templates (common aspect ratios, frame rates)
- [ ] **TASK-380**: Implement EDL/XML export for external editors
- [ ] **TASK-381**: Add project notes/comments panel
- [ ] **TASK-382**: Implement project archiving (collect media files)

### 10.10 UI/UX Enhancements
- [ ] **TASK-383**: Create customizable workspace layouts
- [ ] **TASK-384**: Implement dockable panels (like Shotcut's dock system)
- [ ] **TASK-385**: Add thumbnail strip for timeline clips
- [x] **TASK-386**: Implement timeline snapping (to clips, markers, playhead) ✅
- [x] **TASK-387**: Add magnetic timeline mode (auto-close gaps) ✅
- [x] **TASK-388**: Implement timeline zoom gestures (pinch, scroll wheel) ✅

### 🐛 Bug Fixes & Polish
9. Fix any reported issues from v3.2.0
10. Performance improvements for large libraries
11. UI polish based on user feedback

---

## Known Bugs/Issues

*No known issues at this time. Report any bugs via GitHub Issues.*

---

## Summary

| Phase | Total Tasks | Completed | Remaining |
|-------|-------------|-----------|-----------|
| Phase 1: UI Foundation | 21 | 21 | 0 |
| Phase 2: Enhanced Tools | 68 | 65 | 3 |
| Phase 3: New Tools | 56 | 56 | 0 |
| Phase 4: System Features | 33 | 31 | 2 |
| Phase 5: C++ Core | 65 | 1 | 64 (Deferred) |
| Phase 6: C++/CLI Bridge | 16 | 0 | 16 (Deferred) |
| Phase 7: .NET UI | 31 | 0 | 31 (Deferred) |
| Phase 8: Testing & Docs | 19 | 4 | 15 |
| Phase 9: Future | 15 | 0 | 15 |
| Phase 10: Shotcut-Inspired | 64 | 23 | 41 |
| **TOTAL** | **388** | **201** | **187** |

**Notes:**
- Phases 5-7 (C++ Audio Core) are **DEFERRED** - the audio player uses managed .NET implementation with NAudio
- All Audio Player priority features (AP-001 to AP-015) are **COMPLETE**
- All Video Editor priority features (VE-001 to VE-015) are **COMPLETE** except VE-013 (Thumbnail Strip)
- All DFIR Playbook features (SEC-006 to SEC-010) are **COMPLETE**
- Phase 10 has significant progress with keyframes, filters, timeline features, and export presets

---

*Last verified: January 27, 2026*


