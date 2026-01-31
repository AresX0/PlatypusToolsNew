# Changelog

All notable changes to this project will be documented in this file.

## v3.2.17.0 - 2026-01-31

### Added
- **Admin Rights Control** - New setting to control administrator elevation behavior
  - Added "Launch with administrator rights" checkbox in Settings → General → Startup
  - App now uses on-demand elevation via UAC prompt instead of forcing admin rights in manifest
  - Users can disable admin elevation for normal use, re-enable when system tools are needed
  - Changed app.manifest from `requireAdministrator` to `asInvoker` for flexibility

### Changed
- **Tab Reorganization** - Moved tabs to more logical locations:
  - **Windows Update Repair** moved from Tools → System tab (after System Restore)
  - **Screen Recorder** moved from Multimedia/Video → Tools tab (after Plex Backup)
- **MSDT Deprecation Handling** - Updated Windows Update troubleshooter for Windows 11 23H2+
  - Primary method: PowerShell `Get-TroubleshootingPack` command
  - Fallback: Opens Windows Settings troubleshooter via `ms-settings:troubleshoot`
  - Legacy: MSDT.exe as final fallback with deprecation warning

### Fixed
- **Video Player Freeze** - Fixed UI freezing when loading videos in the native video player
  - LibVLC initialization now runs asynchronously in background thread
  - Video player no longer blocks UI thread during heavy native library loading
- **Screen Recorder Microphone Issue** - Removed microphone audio recording option
  - Microphone recording caused recordings to fail on many systems
  - System audio recording still available and working

---

## v3.2.16.0 - 2026-01-30

### Added
- **Bundled Tools** - FFmpeg and ExifTool are now included in the installer
  - FFmpeg 8.0.1 essentials build (ffmpeg.exe, ffprobe.exe, ffplay.exe)
  - ExifTool 13.47 64-bit for metadata operations
  - Tools are installed to `C:\Program Files\PlatypusTools\Tools\` subfolder
  - No external downloads required for Screen Recorder or Video Editor features

### Fixed
- **Screen Recorder** - Now works out of the box with bundled FFmpeg
- **Video Editor** - Uses bundled FFmpeg for all video processing
- **Metadata Features** - ExifTool bundled for reliable metadata extraction

---

## v3.2.15.0 - 2026-01-27

### Added
- **Screen Recorder** - New tab for recording your screen to video files
  - FFmpeg-based screen capture with gdigrab backend
  - Multiple video codec support: H.264, H.265 (HEVC), VP9
  - Configurable frame rate (15, 24, 30, 60 FPS)
  - Microphone audio recording with device selection
  - System audio loopback recording (capture what you hear)
  - 3-second countdown delay option before recording starts
  - Global hotkeys: Ctrl+Shift+R (Start), Ctrl+Shift+S (Stop)
  - Real-time duration display during recording
  - Recording log with detailed status messages
  - Output folder customization with quick-open buttons
  - Automatic output file naming with timestamps

---

## v3.2.14.6 - 2026-01-25

### Added
- **Matrix Digital Rain Visualizer** - Added new audio visualizer mode inspired by The Matrix
  - Falling Japanese katakana/letter/number columns with variable speeds
  - Characters randomly change creating the iconic "glitch" effect
  - Bright white-green head character with fading green trail
  - Audio-reactive: bass affects fall speed, overall intensity affects brightness
  - Subtle glow overlay effect at higher audio levels

### Fixed
- **Splash Screen Video** - Added retry timer and improved error handling for video playback
  - Videos now attempt up to 5 retries at 500ms intervals
  - Better cleanup on window close
- **Terminal Vertical Text** - Fixed text display issue where characters appeared vertically
  - Properly splits output by newline characters into separate paragraphs
  - Correct paragraph-based buffer limit

---

## v3.2.13.11 - 2026-01-25

### Fixed
- **UI Freezing** - Converted 35+ blocking `Dispatcher.Invoke()` calls to non-blocking `InvokeAsync()` pattern across ViewModels:
  - RecentCleanupViewModel, MediaLibraryViewModel, RobocopyViewModel, NetworkToolsViewModel
  - FileRenamerViewModel, LazyTabContent, SystemRestoreViewModel, TerminalClientViewModel
  - ScheduledTasksViewModel, RegistryCleanerViewModel, BatchUpscaleViewModel, DiskSpaceAnalyzerViewModel
  - BootableUSBViewModel, ProcessManagerViewModel, DuplicatesViewModel, FtpClientViewModel
  - MetadataTemplateViewModel, SecureWipeViewModel, LogViewerViewModel, FileAnalyzerViewModel
  - EmptyFolderScannerViewModel, ForensicsAnalyzerViewModel, AdvancedForensicsViewModel, VideoEditorViewModel
  - PdfToolsViewModel
- **UI Freezing (Views)** - Converted blocking Invoke in Views:
  - EnhancedAudioPlayerView, SearchWindow, SplashScreenWindow, BatchOperationsWindow
- This resolves app freezing/hanging when background operations update the UI

---

## v3.2.11.0 - 2026-01-24

### Added
- **Filter Favorites (VE-002)** - Mark frequently used filters with star icon for quick access
- **Filter Presets Save/Load (VE-006)** - Save and load filter parameter configurations
  - FilterPresetService for persistent preset storage
  - Apply presets to any compatible filter
  - Import/export preset configurations
- **Timeline Snapping (VE-007)** - Clips snap to playhead, markers, and other clip edges
  - Configurable snap threshold
  - Snap indicators during drag operations
- **Clip Markers (VE-008)** - Add markers within clips for sync points and notes
  - ClipMarker class with position, name, notes, color, type
  - Support for Generic, SyncPoint, Beat, CuePoint, Chapter, Note, Todo markers
- **Project Auto-Save (VE-010)** - Automatic 5-minute saves with crash recovery
  - Timer-based auto-save to AppData
  - Recovery file detection on startup
  - Automatic cleanup of old auto-saves
- **Duplicate Detection (ML-006)** - Find duplicate media files by MD5 hash
  - Size-based pre-filtering for performance
  - MediaDuplicateGroup class for duplicate management
- **DFIR Tools Integration (SEC-006-010)** - Comprehensive digital forensics support
  - WinPmem memory acquisition (SEC-006)
  - Volatility 3 analysis with 15+ plugins (SEC-007)
  - KAPE collection with targets/modules (SEC-008)
  - OpenSearch/Elasticsearch export (SEC-009)
  - Plaso super-timeline creation (SEC-010)

---

## v3.2.10.0 - 2026-01-23

### Fixed
- **Robocopy Scroll** - Added scrolling to top pane of Robocopy tab for better usability on smaller screens

---

## v3.2.9.0 - 2026-01-23

### Added
- **EFS Encryption Support** - Folder Hider now supports Windows EFS encryption:
  - Encrypt/decrypt folders using Windows Encrypting File System
  - Check encryption status of files and folders
  - Automatic detection of NTFS filesystem requirement
  - Proper error handling for unsupported filesystems

- **Run Scheduled Tasks** - Scheduled Tasks module now allows running tasks immediately:
  - Execute any scheduled task on-demand via schtasks /Run
  - Success/failure feedback with error messages

- **Video Editor Undo/Redo Stack** - Full undo/redo support in Video Editor:
  - 50-action undo stack
  - UndoableAction pattern for extensible undo operations
  - CanUndo/CanRedo properties for UI feedback

- **Video Editor Trim In/Out** - Interactive clip trimming in Video Editor:
  - Drag left edge to trim in-point
  - Drag right edge to trim out-point
  - Real-time duration feedback during trim
  - Source duration limits enforced

- **Video Project Save/Load** - Shotcut Native Editor project persistence:
  - Save timeline, tracks, and clips to JSON project file
  - Load projects with full state restoration
  - Playlist items preservation
  - Track settings (mute, hide, lock) saved

### Fixed
- **Build Errors** - Fixed property name mismatches between UI and Core models
- **Trim Implementation** - Corrected TrimIn property usage for Core TimelineClip model

---

## v3.2.8.0 - 2026-01-23

### Added
- **Local KQL Database** - New "Local KQL" tab in Advanced Forensics with SQLite-backed KQL query engine:
  - 13 forensic data tables: Processes, NetworkConnections, SecurityEvents, FileEvents, RegistryEvents, PrefetchEvents, AmcacheEvents, SrumEvents, TimelineEvents, MalwareEvents, ExtractedFeatures, DocumentMetadata, ForensicArtifacts
  - KQL to SQL translation engine supporting: where, project, extend, summarize, sort, top, take, distinct
  - KQL functions: ago(), now(), contains, startswith, in, has_any, isnotnull, bin(), countif, dcount, make_set
  - 40+ pre-built KQL query templates from KQL Cheat Sheet (kustonaut.github.io)
  - Category-filtered template browser (Basic Syntax, Aggregations, Security, Threat Hunting, Malware Analysis, etc.)
  - Table browser with column schema display
  - Real-time translated SQL preview
  - Export results to CSV or JSON
  - Auto-storage of Volatility analysis results and artifact collection to local database

### Dependencies
- Added System.Data.SQLite.Core v1.0.119 for local forensics database

---

## v3.2.7.0 - 2026-01-21

### Added
- **Robocopy Help Documentation** - Added comprehensive Robocopy documentation to Help menu
- **Help Menu Enhancement** - Added "Robocopy - How to Use" menu item

### Fixed
- **Dark Theme XAML Error** - Fixed corrupted newline character in Dark.xaml that prevented dark mode from loading
- **Window Styling** - Reverted to standard window chrome for stability (removed experimental custom title bar)

### Changed
- **Robocopy Location** - Moved Robocopy tab from Tools to File Management for better organization

---

## v3.2.6.1 - 2026-01-21

### Added
- **Robocopy GUI** - Full GUI implementation for Robocopy under Tools tab:
  - All 70+ Robocopy switches organized by category
  - Presets for common operations (Mirror, Copy, Move, Sync, Backup)
  - Real-time command preview
  - Live output with progress parsing
  - Export results to JSON
  - Failed files tracking with error details

- **Empty Folder Scanner Enhancement** - Added "Ignore Junk Files" option to detect folders containing only system files (Thumbs.db, desktop.ini, .DS_Store)

- **Development Guidelines** - Added comprehensive DOCS/DEVELOPMENT_GUIDELINES.md for maintaining optimization patterns

- **Auto-Versioning** - Added pre-commit hook for automatic version increment (.0.0.1 per commit)

### Fixed
- **Dark Mode TabControl** - Fixed light background appearing in nested TabControl areas
- **DockPanel Dark Mode** - Added DockPanel styling for proper dark mode backgrounds
- **Theme Consistency** - All hardcoded colors in views replaced with DynamicResource references

---

## v3.2.5.0 - 2026-01-20

### Added
- **Resizable Panes** - All views now have resizable panes using GridSplitters:
  - Drag the gray splitter bars to resize top and bottom panes
  - All panes maintain minimum heights to prevent collapse
  - Smaller default top panes for views with just buttons
  - Larger proportional panes for views with more content

- **Vertical Scrolling** - Added vertical scroll support to all view panes:
  - DiskCleanupView, PrivacyCleanerView - Scrollable checkbox lists
  - NativeImageEditView - Scrollable toolbar with Transform, Effects, Annotate, and Advanced tools
  - All views with options panels now properly scroll when content overflows

### Fixed
- **Archive Manager Password Field** - Added horizontal scroll to Create Options to prevent password field cutoff
- **View Layout Improvements** - Optimized row definitions for consistent GridSplitter behavior across all views

---

## v3.2.4.0 - 2026-01-19

### Added
- **3D Model Editor Tab** - New tab for creating and editing 3D models:
  - Load SVG files and convert to 3D printable formats (STL, OBJ)
  - Create basic 3D shapes (Cube, Cylinder, Sphere, Pyramid, Cone)
  - Create 3D extruded text with customizable height and depth
  - Interactive 3D viewport with mouse controls:
    - Left-click drag to rotate model
    - Right-click drag to pan camera
    - Mouse wheel to zoom in/out
  - Model Scale slider for resizing objects
  - Position controls (Move X/Y/Z) for precise placement
  - Export to STL (3D printing) or OBJ format

- **SVG to Image Conversion** - Added SVG output format to Image Converter:
  - Embed mode: Wraps raster images in SVG container
  - Trace mode: Converts to vector paths (for simple images)

- **Advanced SVG Parsing for 3D** - Enhanced SVG to 3D conversion:
  - Full support for SVG paths, rectangles, circles, ellipses, polygons, lines
  - Automatic image tracing for SVGs with embedded raster images
  - Edge detection using Sobel operators
  - Contour tracing and simplification
  - Detailed diagnostic logging for troubleshooting

### Fixed
- **3D Model Editor Mouse Controls** - Added interactive viewport controls
- **3D Text Creation** - Fixed Create Text button not working

---

## v3.2.3.1 - 2026-01-19

### Added
- **Forensics Analyzer HTML Export** - Comprehensive HTML report with styled tables, summary cards, and all findings
- **Command Pattern Standardization (OPT-006)** - Enhanced AsyncRelayCommand with execution guards and generic variants
- **Settings Service Optimization (OPT-010)** - Centralized settings access using cached singleton pattern

### Fixed
- **Forensics Analyzer Export Buttons** - Fixed buttons staying disabled after analysis by using direct IsEnabled bindings

---

## v3.2.3.0 - 2026-01-19

### Added
- **ViewModel Base Consolidation (OPT-004)** - Refactored 12 ViewModels to use unified BindableBase pattern:
  - DuplicateFileViewModel, HiderEditViewModel, VideoConverterViewModel
  - SystemAuditViewModel, MultimediaEditorViewModel, PluginManagerViewModel
  - ForensicsAnalyzerViewModel, MetadataEditorViewModel, VideoEditorViewModel
  - Eliminated ~150 lines of duplicated INotifyPropertyChanged boilerplate

- **Lazy Tab Loading (OPT-003)** - All 35 ViewModels now load on-demand for faster startup

- **Tab Visibility Settings (OPT-001)** - Control which tabs appear via Settings window

- **Independent Tab Launch (OPT-002)** - Each tab can now be loaded independently

### Fixed
- **Startup Manager UI** - Fixed "Open Location" button overlapping, increased row height to 40px
- **Folder Hider UI** - Fixed "Remove" button text cutoff, widened button to 70px

---

## v3.2.1 - 2026-01-26

### Added
- **Shotcut-Inspired Video Editor Roadmap** - Added 64 new tasks for professional NLE features:
  - Multi-track timeline with unlimited video/audio tracks
  - Advanced clip operations (ripple, rolling, slip, slide edits)
  - Keyframe animation system with bezier curves
  - 300+ filters and effects (chroma key, stabilization, LUTs)
  - Text & titles with animations and 3D text
  - Audio features (waveforms, ducking, voice-over recording)
  - Proxy editing and preview scaling
  - Hardware encoding support (NVENC, QSV, AMF)
  - Project templates and auto-save

### Fixed
- **Video Editor Insert Clip** - Fixed compile error in insert at playhead feature
  - Corrected method reference for media asset import
  - Fixed null reference when accessing selected asset properties

## v3.2.0.1 - 2026-01-18

### Fixed
- **Audio Player Queue Display** - Fixed queue not updating when adding files from Library tab
  - Resolved binding issue where ItemsSource was being set to static lists
  - Queue now updates immediately when using "Add All" from Library

- **Crossfade Volume** - Fixed volume going silent after crossfade transition
  - Volume now correctly restores to original level after track change
  - Stored original volume before fade starts to preserve correct playback level

### Changed
- **Renamed Batch Upscaler to Image Scaler** - Better describes functionality
- **Moved Image Scaler to Image tab** - More logical placement with other image tools

### Added
- **Video Combiner Transitions** - Added transition effects between combined clips:
  - Enable Transitions checkbox to toggle feature
  - 10 transition types: Cross Dissolve, Fade to Black/White, Wipe (4 directions), Slide (2 directions)
  - Adjustable transition duration (0.25s - 5.0s)
  - File reordering with Move Up/Down buttons
  - Remove Selected button for file management
  - Uses FFmpeg xfade filter for smooth video transitions
  - Audio crossfade between clips

## v3.3.0 - 2026-01-17

### Added
- **Scrolling Screenshot Capture** - Capture entire scrollable content:
  - New 📜 Scroll button in Screenshot tool toolbar
  - Automatically scrolls window and stitches images together
  - Works with browsers, documents, and any scrollable window
  - Uses overlap-based stitching for seamless results

- **Audio Crossfade UI** - Visual controls for track transitions:
  - Enable/disable crossfade checkbox in Audio Player toolbar
  - Adjustable duration slider (0-5 seconds)
  - Duration display shows current setting
  - Smooth fade-out/fade-in between tracks

- **Transition Preview Animation** - Live preview in transition picker:
  - Play Preview button to see transition in action
  - Animated before/after panels ("A" → "B")
  - Supports all transition types: fade, slide, wipe, zoom
  - Respects duration and easing settings

- **Synchronized Comparison Zoom** - Linked zoom/pan for image comparison:
  - Zoom in/out buttons (+/−) with percentage display
  - Ctrl+Mouse wheel for smooth zooming
  - Reset (100%) and Fit to View buttons
  - Sync Pan toggle to link left/right image scrolling
  - Works in Slider, Side-by-Side, and Overlay modes

## v3.2.0 - 2026-01-17

### Added
- **Forensics Analyzer** - New security tool for system forensic analysis:
  - **Lightweight Mode** - Quick scan using single core, ~100MB output, 24h log history
  - **Deep Mode** - Comprehensive analysis using all cores, GB-scale output, 7-day log history
  - **Memory Analysis** - Process memory inspection, suspicious process detection, memory hog identification
  - **File System Analysis** - Executable scanning, hidden file detection, SHA256 hashing (deep mode)
  - **Registry Analysis** - Startup entry enumeration, suspicious run key detection
  - **Log Aggregation** - Windows Event Log parsing, security event analysis, error/warning categorization
  - **Export Report** - Generate detailed forensics reports in JSON or text format

- **Sleep Timer** - Audio Player enhancement:
  - Set automatic playback stop timer (15, 30, 45, 60, 90, 120 minutes)
  - Countdown display in status bar
  - Gradual fade-out before stopping
  - Cancel timer option

- **Plugin Manager UI** - New tool for managing plugins:
  - Browse installed plugins with status
  - Install new plugins from file
  - Enable/disable plugins without uninstalling
  - Uninstall plugins with confirmation
  - Reload all plugins to apply changes
  - View plugin details (version, author, description)

### Fixed
- **Audio Player Library Persistence** - Library folders and tracks now properly load on startup
- **Audio Player Play Selected** - Play Selected button now correctly plays the highlighted track
- **Audio Player Organize By** - Organize dropdown now properly filters tracks by Artist, Album, Genre, etc.
- **Audio Player DataGrid Init** - Fixed exception during initialization that prevented library loading

## v3.1.1.1 - 2026-01-17

### Fixed
- **Window Ownership Errors** - Fixed "Cannot set Owner property" errors:
  - Help menu "View Help Documentation" now opens without access denied errors
  - Tools menu "Credential Manager" no longer throws ownership exceptions
  - Tools menu "Run Parity Tests" properly handles window ownership
  - Added `GetSafeOwner()` helper to validate window ownership before setting
  - Checks that MainWindow is loaded before using as owner
  - Prevents circular reference when child window would be same as owner

### Added
- **Video Similarity Detection** - Find similar videos using perceptual hashing
- **Transition Picker UI** - Visual selector for video transitions with categories and easing
- **PDF Encryption/Decryption** - Password protect PDFs with permission controls
- **Timeline Drag-Drop** - Drag media files directly onto timeline from File Explorer
- **Metadata Templates** - Save, load, and apply metadata templates to files
- **Archive Split/Password** - Create password-protected and multi-part split archives
- **Recent Workspaces UI** - Redesigned with tabs, pinning, and filtering

## v3.1.0 - 2026-01-17

### Fixed
- **Library Persistence** - Library now properly loads on app startup:
  - Library index is now loaded in ViewModel constructor instead of View's OnLoaded
  - Fixes issue where library appeared empty until manually rescanning
  - Works with lazy-loaded tabs (library loads even before visiting Audio Player tab)

### Improved
- **Audio Player Layout** - Playback controls repositioned:
  - Play/Pause, Skip, Stop, Volume controls moved from bottom to below visualizer
  - Controls now appear above the Queue/Library tabs for better accessibility
  - Cleaner visual hierarchy with controls near the visualizer

## v3.0.9 - 2026-01-16

### Added
- **Audio Player Crossfade** - Smooth transitions between tracks:
  - Configurable crossfade duration (0-5 seconds)
  - Automatic fade-out of current track while fading-in next track
  - Toggle crossfade on/off via settings
  - Works with shuffle, repeat all, and normal playback modes

- **Queue Context Menu** - Right-click options for queue management:
  - Play Now - Immediately play the selected track
  - Play Next - Insert track after currently playing
  - Move Up/Down - Reorder tracks in queue
  - Remove from Queue - Delete selected track
  - Reveal in Explorer - Open containing folder

- **Drag-and-Drop Queue Reordering** - Reorder tracks by dragging:
  - Click and drag tracks to new positions
  - Visual feedback during drag operation
  - Automatic index adjustment for currently playing track

### Improved
- **Audio Player Service** - Enhanced queue management:
  - PlayNext method for inserting tracks after current
  - MoveInQueue method for programmatic reordering
  - RevealInExplorer for quick file access
  - Better shuffle index management during queue modifications

## v3.0.8 - 2026-01-16

### Added
- **Recent Cleaner Enhancement** - Comprehensive recent items scanning:
  - Scans Windows Recent folder, Jump Lists (Automatic & Custom Destinations)
  - Scans Favorites folder, Start Menu recent items, Office MRU locations
  - Properly resolves .lnk shortcut targets for accurate display
  - Shows item type (Recent, JumpList-Auto, JumpList-Custom, Favorite, etc.)

- **Image Converter WebP Support** - Added WebP as output format option:
  - Convert images to WebP format
  - Full list now includes: JPG, PNG, BMP, GIF, TIFF, WebP

### Improved
- **Disk Space Analyzer** - Complete folder hierarchy with files:
  - Shows full directory tree up to 10 levels deep
  - Displays individual files with sizes
  - Hidden and System file visibility toggles
  - Export to CSV or text report

- **Privacy Cleaner** - Better selection controls:
  - Select All / Select None buttons for cleanup items
  - Hierarchical tree view with category and file-level selection
  - Dry run mode for safe preview

- **Audio Player** - Multi-select queue management:
  - Ctrl+Click or Shift+Click to select multiple tracks
  - Remove Selected button removes all selected tracks at once
  - Dynamic folder scanning adds tracks to queue

## v3.0.7 - 2025-01-16

### Added
- **Image Similarity Detection** - Find visually similar images in duplicates view:
  - Perceptual hash (pHash) using DCT for robust similarity detection
  - Difference hash (dHash) for edge-based comparison  
  - Configurable similarity threshold (0-100%)
  - Groups similar images for easy review
  - Works alongside existing duplicate file detection

## v3.0.6 - 2025-02-14

### Added
- **PDF Tools** - Complete PDF manipulation suite:
  - Merge multiple PDFs into single document
  - Split PDF into individual pages or ranges
  - Extract specific pages from PDFs
  - Rotate pages (90°, 180°, 270°)
  - Add text watermarks to PDFs
  - Convert images to PDF
  - Delete and reorder pages
- **Batch Watermark Tool** - Add watermarks to multiple images:
  - Text watermarks with customizable font, size, color, opacity
  - Image watermarks with resize and position options
  - Tiled watermark pattern support
  - Position presets (corners, center, custom)
  - Live preview before applying
  - Batch processing with progress tracking
- **Screenshot Tool** - Full-featured screen capture:
  - Capture full screen, primary monitor, active window, or custom region
  - Region selection overlay with dimension display
  - Annotation tools: arrows, rectangles, ellipses, text, highlights
  - Blur/pixelate sensitive areas
  - Freehand drawing tool
  - Save to file or copy to clipboard
  - Multiple format support (PNG, JPEG, BMP)

### Changed
- Removed WinForms dependency from Core library for better architecture separation
- Clipboard operations moved to UI layer for proper WPF integration
- Improved resource disposal for FolderBrowserDialog instances

## v3.0.5 - 2025-02-13

### Added
- Audio Player improvements

## v1.0.1 - 2026-01-06
- Created local backup: `backups/PlatypusTools_20260106_0000.ps1`
- Created working copy: `PlatypusTools.v1.0.1.ps1` and set `$script:Version = '1.0.1'`
- Added initial DOCS: `functions_list.md`, `script_variables.md`, `MANIFEST.md`, `DEV_INSTRUCTIONS.md`
- Plan: Fix PSScriptAnalyzer findings (empty catch blocks, etc.), add automated backup helper, add thorough manifest entries and developer guidance.


> NOTE: This changelog is maintained locally until you request to publish changes to remote.
