# PlatypusTools v3.0.0 - Detailed TODO List

**Branch**: `v3.0.0-major-features`  
**Last Updated**: January 11, 2026  
**Legend**: ✅ Complete | 🔄 In Progress | ❌ Not Started

---

## Phase 1: UI Foundation

### 1.1 Status Bar
- [x] **TASK-001**: Create `StatusBarControl.xaml` custom control
- [x] **TASK-002**: Create `StatusBarViewModel.cs` with progress tracking
- [x] **TASK-003**: Add status bar to `MainWindow.xaml`
- [ ] **TASK-004**: Wire up status bar to all long-running operations
- [ ] **TASK-005**: Add cancel button functionality

### 1.2 Keyboard Shortcuts
- [x] **TASK-006**: Create `KeyboardShortcutService.cs` for hotkey management
- [ ] **TASK-007**: Create `KeyboardShortcutsViewModel.cs` for settings UI
- [x] **TASK-008**: Create `KeyboardShortcutsView.xaml` (in SettingsWindow) settings page
- [ ] **TASK-009**: Define default shortcuts in `shortcuts.json`
- [ ] **TASK-010**: Integrate shortcuts with all commands
- [x] **TASK-011**: Add shortcut display to menu items

### 1.3 Recent Workspaces
- [x] **TASK-012**: Create `RecentWorkspacesService.cs`
- [ ] **TASK-013**: Add recent workspaces to File menu
- [ ] **TASK-014**: Create `RecentWorkspacesView.xaml` in Home tab
- [ ] **TASK-015**: Implement pin/unpin functionality
- [ ] **TASK-016**: Add "Clear Recent" option
- [ ] **TASK-017**: Persist recent workspaces to settings

### 1.4 Settings Backup/Restore
- [x] **TASK-018**: Create `SettingsBackupService.cs`
- [ ] **TASK-019**: Export settings to JSON/ZIP
- [ ] **TASK-020**: Import settings from backup
- [ ] **TASK-021**: Add Settings Backup/Restore UI in settings

---

## Phase 2: Enhanced Existing Tools

### 2.1 Video Editor Timeline
- [x] **TASK-022**: Design timeline data models (`TimelineTrack.cs`, `TimelineClip.cs`)
- [x] **TASK-023**: Create `TimelineControl.xaml` custom control
- [ ] **TASK-024**: Implement timeline ruler with zoom
- [ ] **TASK-025**: Create `TimelineTrackControl.xaml` for track headers
- [ ] **TASK-026**: Create `TimelineClipControl.xaml` for clip display
- [ ] **TASK-027**: Implement playhead with scrubbing
- [x] **TASK-028**: Create `TimelineViewModel.cs`
- [ ] **TASK-029**: Implement drag-and-drop clips
- [ ] **TASK-030**: Implement clip trimming handles
- [ ] **TASK-031**: Implement clip splitting
- [x] **TASK-032**: Implement undo/redo for timeline actions

### 2.2 Video Editor Multi-Track
- [x] **TASK-033**: Add track type enumeration (Video, Audio, Title, Effects)
- [x] **TASK-034**: Implement track add/remove
- [x] **TASK-035**: Implement track reordering
- [x] **TASK-036**: Implement track visibility toggle
- [x] **TASK-037**: Implement track mute/solo
- [x] **TASK-038**: Implement track lock

### 2.3 Video Editor Transitions
- [x] **TASK-039**: Create `Transition.cs` model
- [x] **TASK-040**: Create `TransitionService.cs` with FFmpeg filters
- [ ] **TASK-041**: Create `TransitionPickerView.xaml` UI
- [x] **TASK-042**: Implement fade transitions (in, out, cross)
- [x] **TASK-043**: Implement wipe transitions
- [x] **TASK-044**: Implement slide transitions
- [x] **TASK-045**: Implement zoom transitions
- [ ] **TASK-046**: Add transition duration control
- [ ] **TASK-047**: Add transition preview

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
- [x] **TASK-061**: Implement overlay toggle mode
- [ ] **TASK-062**: Implement synchronized zoom/pan
- [x] **TASK-063**: Add comparison view to Image Scaler

### 2.6 Metadata Template Presets
- [x] **TASK-064**: Create `MetadataTemplate.cs` model
- [x] **TASK-065**: Create `MetadataTemplateService.cs`
- [ ] **TASK-066**: Create template save/load UI
- [ ] **TASK-067**: Implement "Save as Template" feature
- [x] **TASK-068**: Implement template management (list, delete, rename)
- [x] **TASK-069**: Implement "Apply Template" with merge/replace modes

### 2.7 Metadata Batch Copy
- [x] **TASK-070**: Create batch metadata copy UI (MetadataTemplateViewModel)
- [x] **TASK-071**: Implement source file selection
- [ ] **TASK-072**: Implement tag selection (checkboxes)
- [x] **TASK-073**: Implement target file selection
- [ ] **TASK-074**: Implement preview before apply
- [x] **TASK-075**: Implement batch apply with progress

### 2.8 Duplicate Finder Image Similarity
- [ ] **TASK-076**: Create `ImageSimilarityService.cs`
- [ ] **TASK-077**: Implement pHash (perceptual hash)
- [ ] **TASK-078**: Implement dHash (difference hash)
- [ ] **TASK-079**: Implement aHash (average hash)
- [ ] **TASK-080**: Create similarity threshold UI
- [ ] **TASK-081**: Create grouped results view
- [ ] **TASK-082**: Add similarity percentage display
- [ ] **TASK-083**: Add visual comparison grid

### 2.9 Duplicate Finder Video Similarity
- [ ] **TASK-084**: Create `VideoSimilarityService.cs`
- [ ] **TASK-085**: Implement key frame extraction
- [ ] **TASK-086**: Implement frame hash comparison
- [ ] **TASK-087**: Implement duration tolerance
- [ ] **TASK-088**: Add video thumbnail preview
- [ ] **TASK-089**: Add configurable frame sample rate

---

## Phase 3: New Tools

### 3.1 Batch Watermarking
- [ ] **TASK-090**: Create `WatermarkService.cs`
- [ ] **TASK-091**: Create `BatchWatermarkViewModel.cs`
- [ ] **TASK-092**: Create `BatchWatermarkView.xaml`
- [ ] **TASK-093**: Implement text watermark (font, color, opacity)
- [ ] **TASK-094**: Implement image watermark (PNG with alpha)
- [ ] **TASK-095**: Implement position options (corners, center, tiled, custom)
- [ ] **TASK-096**: Implement preview
- [ ] **TASK-097**: Implement batch processing for images
- [ ] **TASK-098**: Implement video watermarking via FFmpeg
- [ ] **TASK-099**: Add Watermark tool to navigation

### 3.2 PDF Tools
- [ ] **TASK-100**: Add PDFsharp NuGet package
- [ ] **TASK-101**: Create `PdfService.cs`
- [ ] **TASK-102**: Create `PdfToolsViewModel.cs`
- [ ] **TASK-103**: Create `PdfToolsView.xaml`
- [ ] **TASK-104**: Implement PDF merge
- [ ] **TASK-105**: Implement PDF split
- [ ] **TASK-106**: Implement page extraction
- [ ] **TASK-107**: Implement PDF compression
- [ ] **TASK-108**: Implement PDF to images
- [ ] **TASK-109**: Implement images to PDF
- [ ] **TASK-110**: Implement PDF rotation
- [ ] **TASK-111**: Implement PDF encryption/decryption
- [ ] **TASK-112**: Implement PDF watermark
- [ ] **TASK-113**: Add PDF Tools to navigation

### 3.3 Archive Manager
- [x] **TASK-114**: Add SharpCompress NuGet package
- [x] **TASK-115**: Create `ArchiveService.cs`
- [x] **TASK-116**: Create `ArchiveManagerViewModel.cs`
- [x] **TASK-117**: Create `ArchiveManagerView.xaml`
- [x] **TASK-118**: Implement archive browsing
- [x] **TASK-119**: Implement ZIP creation
- [ ] **TASK-120**: Implement 7z creation (via 7z.dll)
- [x] **TASK-121**: Implement archive extraction (ZIP, 7z, RAR, TAR)
- [x] **TASK-122**: Implement selective extraction
- [ ] **TASK-123**: Implement password protection
- [x] **TASK-124**: Implement compression levels
- [ ] **TASK-125**: Implement split archives
- [x] **TASK-126**: Add Archive Manager to navigation

### 3.4 Screenshot Tool
- [ ] **TASK-127**: Create `ScreenCaptureService.cs`
- [ ] **TASK-128**: Create `ScreenshotViewModel.cs`
- [ ] **TASK-129**: Create `ScreenshotView.xaml`
- [ ] **TASK-130**: Create `RegionSelectWindow.xaml` overlay
- [ ] **TASK-131**: Implement full screen capture
- [ ] **TASK-132**: Implement active window capture
- [ ] **TASK-133**: Implement region selection
- [ ] **TASK-134**: Implement scrolling capture (optional)
- [ ] **TASK-135**: Create annotation toolbar
- [ ] **TASK-136**: Implement arrow annotation
- [ ] **TASK-137**: Implement rectangle annotation
- [ ] **TASK-138**: Implement ellipse annotation
- [ ] **TASK-139**: Implement freehand annotation
- [ ] **TASK-140**: Implement text annotation
- [ ] **TASK-141**: Implement blur/pixelate tool
- [ ] **TASK-142**: Implement highlight tool
- [ ] **TASK-143**: Implement copy to clipboard
- [ ] **TASK-144**: Implement save to file
- [ ] **TASK-145**: Add Screenshot Tool to navigation

---

## Phase 4: System Features

### 4.1 Auto-Update
- [ ] **TASK-146**: Add Octokit NuGet package
- [ ] **TASK-147**: Create `UpdateService.cs`
- [ ] **TASK-148**: Create `UpdateViewModel.cs`
- [ ] **TASK-149**: Create `UpdateView.xaml` notification dialog
- [ ] **TASK-150**: Implement GitHub releases API check
- [ ] **TASK-151**: Implement version comparison
- [ ] **TASK-152**: Implement download progress
- [ ] **TASK-153**: Implement installer launch
- [ ] **TASK-154**: Add "Check for Updates" menu item
- [ ] **TASK-155**: Add update check on startup setting

### 4.2 Plugin/Extension System
- [ ] **TASK-156**: Create `PlatypusTools.Plugins.SDK` project
- [ ] **TASK-157**: Define `IPlugin` interface
- [ ] **TASK-158**: Define `IToolPlugin` interface
- [ ] **TASK-159**: Define `IVisualizerPlugin` interface
- [ ] **TASK-160**: Define `IFileProcessorPlugin` interface
- [ ] **TASK-161**: Create `PluginManifest.cs` model
- [ ] **TASK-162**: Create `PluginLoader.cs` service
- [ ] **TASK-163**: Create `PluginManagerViewModel.cs`
- [ ] **TASK-164**: Create `PluginManagerView.xaml`
- [ ] **TASK-165**: Implement plugin discovery
- [ ] **TASK-166**: Implement plugin loading/unloading
- [ ] **TASK-167**: Implement plugin sandboxing
- [ ] **TASK-168**: Create sample plugin
- [ ] **TASK-169**: Add Plugin Manager to settings

### 4.3 Logging UI
- [ ] **TASK-170**: Create `LogViewerViewModel.cs`
- [ ] **TASK-171**: Create `LogViewerWindow.xaml`
- [ ] **TASK-172**: Implement real-time log display
- [ ] **TASK-173**: Implement log level filtering
- [ ] **TASK-174**: Implement log search
- [ ] **TASK-175**: Implement log export
- [ ] **TASK-176**: Implement log clear
- [ ] **TASK-177**: Implement auto-scroll toggle
- [ ] **TASK-178**: Add "View Logs" menu item

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
- [ ] **TASK-209**: Implement crossfade
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
- [ ] **TASK-305**: Create PROJECT_DOCUMENTATION.md (file explanations)
- [ ] **TASK-306**: Add XML doc comments to all public APIs
- [ ] **TASK-307**: Create user guide
- [ ] **TASK-308**: Create plugin developer guide
- [ ] **TASK-309**: Update README with new features

---

## Summary

| Phase | Total Tasks | Completed | Remaining |
|-------|-------------|-----------|-----------|
| Phase 1: UI Foundation | 21 | 7 | 14 |
| Phase 2: Enhanced Tools | 68 | 0 | 68 |
| Phase 3: New Tools | 56 | 0 | 56 |
| Phase 4: System Features | 33 | 0 | 33 |
| Phase 5: C++ Core | 65 | 0 | 65 |
| Phase 6: C++/CLI Bridge | 16 | 0 | 16 |
| Phase 7: .NET UI | 31 | 0 | 31 |
| Phase 8: Testing & Docs | 19 | 0 | 19 |
| **TOTAL** | **309** | **7** | **302** |

---

*Update this file as tasks are completed.*


