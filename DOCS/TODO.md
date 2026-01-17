# PlatypusTools v3.1.1 - Detailed TODO List

**Branch**: `main`  
**Last Updated**: January 17, 2026  
**Current Version**: v3.1.1  
**Legend**: ✅ Complete | 🔄 In Progress | ❌ Not Started

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
- [ ] **TASK-134**: Implement scrolling capture (optional)
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
- [ ] **TASK-163**: Create `PluginManagerViewModel.cs`
- [ ] **TASK-164**: Create `PluginManagerView.xaml`
- [x] **TASK-165**: Implement plugin discovery ✅ *ScanPlugins*
- [x] **TASK-166**: Implement plugin loading/unloading ✅ *LoadPlugin/UnloadPlugin*
- [ ] **TASK-167**: Implement plugin sandboxing
- [ ] **TASK-168**: Create sample plugin
- [ ] **TASK-169**: Add Plugin Manager to settings

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

## Priority Next Steps (v3.2.0 Candidates)

Based on remaining work and user value, here are the **recommended next features**:

### High Priority (Quick Wins)

1. **TASK-029: Drag-and-drop clips on timeline** - Improves video editor usability
2. **TASK-041: TransitionPickerView.xaml UI** - Users need UI to select transitions
3. **TASK-046/047: Transition duration control and preview** - Complete the transition feature
4. **TASK-053: Per-item settings override for batch upscale** - More flexibility
5. **TASK-062: Synchronized zoom/pan for comparison viewer** - Better comparison UX

### Medium Priority (New Functionality)

6. **TASK-066/067: Template save/load UI for metadata** - Users can save common metadata sets
7. **TASK-072/074: Tag selection checkboxes and preview for batch metadata** - Better control
8. **TASK-084-089: Video Similarity Detection** - Highly requested for finding duplicate videos
9. **TASK-111/112: PDF encryption and watermark** - Complete PDF toolset
10. **TASK-123/125: Archive password protection and split** - Complete archive toolset

### Lower Priority (Enhancement)

11. **TASK-013-017: Recent Workspaces UI** - Nice to have file menu integration
12. **TASK-134: Scrolling capture** - Advanced screenshot feature
13. **TASK-163-169: Plugin Manager UI** - Plugin system already works, UI is polish

---

## Known Bugs/Issues

1. ~~Website Downloader - Scan button may not enable~~ - Needs verification
2. ~~Recent Cleaner - Scan button issue~~ - Fixed in v3.0.8

---

## Summary

| Phase | Total Tasks | Completed | Remaining |
|-------|-------------|-----------|-----------|
| Phase 1: UI Foundation | 21 | 18 | 3 |
| Phase 2: Enhanced Tools | 68 | 48 | 20 |
| Phase 3: New Tools | 56 | 50 | 6 |
| Phase 4: System Features | 33 | 26 | 7 |
| Phase 5: C++ Core | 65 | 0 | 65 (Deferred) |
| Phase 6: C++/CLI Bridge | 16 | 0 | 16 (Deferred) |
| Phase 7: .NET UI | 31 | 0 | 31 (Deferred) |
| Phase 8: Testing & Docs | 19 | 4 | 15 |
| **TOTAL** | **309** | **146** | **163** |

**Note**: Phases 5-7 (C++ Audio Core) are deferred - the audio player uses managed .NET implementation with NAudio.

---

*Last verified: January 17, 2026***Note**: Phases 5-7 (C++ Audio Core) are deferred - the audio player currently uses managed .NET implementation with NAudio.

---

*Last verified: January 28, 2026*


