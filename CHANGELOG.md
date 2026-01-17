# Changelog

All notable changes to this project will be documented in this file.

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
