# PlatypusTools .NET Port - TODO List

## Project Overview
Porting PlatypusTools.ps1 (PowerShell) to PlatypusTools.NET (WPF/.NET 10)

## Status Legend
- ✅ Complete
- 🔄 In Progress  
- ⏳ Planned
- ❌ Blocked

---

## File Cleaner
- ✅ Pattern-based file scanner and cleaner
- ✅ Batch renamer with prefix management
- ✅ Season/Episode numbering
- ✅ Filename cleaning (remove tokens)
- ✅ Name normalization (spaces, dashes, underscores)
- ✅ Preview and undo functionality

## Media Conversion

### Video
- ✅ Video combiner with file list management
- ✅ FFmpeg progress parsing with determinate progress bar
- ✅ FFprobe duration extraction
- ✅ Cancellation support
- ⏳ Tool folder path configuration
- ⏳ Preview encodings
- ⏳ Normalize (H264/AAC) option
- ⏳ Safe combine (re-encode) vs direct concat
- ⏳ Move up/down file ordering

### Graphics (ICO Converter)
- ✅ Browse images folder
- ✅ Add files / Add folder / Clear list
- ✅ Icon size selection (16, 32, 64, 128, 256)
- ✅ Convert to ICO functionality
- ✅ Convert between formats (PNG, JPG, BMP, GIF, TIFF)
- ✅ Output folder and name customization
- ✅ Overwrite existing option
- ✅ Progress reporting and cancellation
- ✅ Select All / Select None commands

### Image Resize
- ✅ Browse and add multiple images
- ✅ Show dimensions and size in grid
- ✅ Max width/height constraints
- ✅ Quality slider (for JPEG)
- ✅ Format conversion (Same, JPG, PNG, BMP, GIF, TIFF)
- ✅ Maintain aspect ratio option
- ✅ Overwrite existing option
- ✅ Batch resize with progress
- ✅ High-quality bicubic interpolation
- ✅ No upscaling (only downscale)
- ✅ Select All / Select None commands

### Bootable USB
- ⏳ ISO file browser
- ⏳ USB drive detection and dropdown
- ⏳ Refresh USB drives button
- ⏳ File system selection (NTFS, FAT32, exFAT)
- ⏳ Volume label customization
- ⏳ Boot mode selection (UEFI GPT, UEFI+Legacy, Legacy MBR)
- ⏳ Quick format option
- ⏳ Verify after write option
- ⏳ Progress logging
- ⏳ Cancel operation

### Upscaler
- ✅ video2x integration
- ✅ Scale selection (2x, 3x, 4x)
- ✅ Progress logging
- ✅ Cancellation support

## Duplicates

### Scanner
- ✅ Basic hash-based duplicate detection (SHA256)
- ✅ File type filtering
- ⏳ Multiple hash algorithms (SHA256, SHA1, MD5)
- ⏳ Fast vs Deep scan modes
- ⏳ Perceptual scanning for images/video
- ⏳ Custom file extensions input
- ⏳ Include subfolders option
- ⏳ Enhanced UI matching screenshot (checkboxes for file types)
- ⏳ Scan progress indicator
- ⏳ Results grid with Hash/Count/Size/Name/Directory/Full Path

### Selection Strategies
- ✅ Select oldest
- ✅ Select newest
- ✅ Select largest
- ✅ Select smallest
- ⏳ Custom selection logic

### Actions
- ✅ Stage selected files
- ✅ Preview staged files
- ✅ Restore from staging
- ✅ Commit (delete originals + staged)
- ⏳ Rename selected duplicates
- ⏳ Delete selected directly

### Hash Calculator
- ⏳ Select file to hash
- ⏳ Algorithm dropdown (SHA256, SHA1, MD5)
- ⏳ Display hash in text field
- ⏳ Copy hash to clipboard

## Cleanup

### Recent Shortcuts
- ✅ Basic recent files cleanup
- ⏳ Directories to exclude list with Add/Remove
- ⏳ Include subdirectories option
- ⏳ Dry run preview mode
- ⏳ Output folder configuration
- ⏳ Scheduled task creation
  - ⏳ Frequency (Daily, Weekly, Monthly)
  - ⏳ Time selection (HH:mm)
  - ⏳ Day selection (checkboxes for each day)
- ⏳ Show preview grid
- ⏳ Export to CSV
- ⏳ Undo last operation
- ⏳ Run now button
- ⏳ Schedule task button

### Disk Cleanup
- ✅ Cleanup categories (checkboxes):
  - ✅ Windows Temp Files
  - ✅ User Temp Files
  - ✅ Prefetch Files
  - ✅ Recycle Bin
  - ✅ Downloads Folder (older than 30 days)
  - ✅ Windows Update Cache
  - ✅ Thumbnail Cache
  - ✅ Windows Error Reports
  - ✅ Old Log Files
- ✅ Analyze button (scan and estimate)
- ✅ Files to be cleaned grid (Category/Files/Size/Path)
- ✅ Clean Now button
- ✅ Dry Run mode
- ✅ Progress and status reporting
- ✅ Cancellation support
- ✅ Error collection and reporting

### Privacy Cleaner
- ✅ Browser Data section:
  - ✅ Chrome (Cookies, Cache, History)
  - ✅ Edge (Cookies, Cache, History)
  - ✅ Firefox (Cookies, Cache, History)
  - ✅ Brave (Cookies, Cache, History)
- ✅ Cloud Service Tokens:
  - ✅ OneDrive Cached Credentials
  - ✅ Google Drive Cached Credentials
  - ✅ Dropbox Cached Credentials
  - ✅ iCloud Cached Data
- ✅ Windows Identity:
  - ✅ Recent Documents List
  - ✅ Taskbar Jump Lists
  - ✅ Explorer History
  - ✅ Clipboard History
- ✅ Application Data:
  - ✅ Office Recent Files
  - ✅ Adobe Recent Files
  - ✅ Media Player History (VLC, Windows Media Player)
- ✅ Analyze button with grouped results
- ✅ Clean Now button
- ✅ Dry Run mode
- ✅ Progress and status reporting
- ✅ Warning message styling
- ✅ Cancellation support

## Security

### Folder Hider
- ✅ Add folders to hide list
- ✅ Hide/unhide operations
- ✅ Password record management
- ⏳ Integration with Windows Credential Manager
- ⏳ ACL manipulation logging

### System Audit
- ⏳ Analyze elevated users
- ⏳ Scan critical ACLs
- ⏳ List outbound traffic and open ports
- ⏳ Users and Groups management UI
- ⏳ Disable/Delete user accounts
- ⏳ Reset passwords
- ⏳ Export audit reports

### Startup Manager
- ⏳ Scan startup items button
- ⏳ Sources: Registry (HKCU/HKLM Run keys)
- ⏳ Sources: Startup folders
- ⏳ Sources: Scheduled Tasks
- ⏳ Display grid with Select/Name/Status/Source/Command/Location
- ⏳ Disable Selected button
- ⏳ Enable Selected button
- ⏳ Delete Selected button
- ⏳ Open Location button
- ⏳ Task Scheduler button (open Task Scheduler)
- ⏳ Status reporting

## Metadata
- ⏳ Select file browser
- ⏳ Quick Info display (Type, Size, Created, Modified, Dimensions, Duration)
- ⏳ All Metadata grid (Tag/Value from exiftool/ffprobe)
- ⏳ Edit Metadata section (Audio/Video):
  - ⏳ Title
  - ⏳ Artist
  - ⏳ Album
  - ⏳ Year
  - ⏳ Comment
- ⏳ Save Changes button (write via exiftool)
- ⏳ Export All button (export metadata to file)
- ⏳ Refresh button
- ⏳ ExifTool integration
- ⏳ FFprobe fallback for media files
- ⏳ .NET metadata reading for images

## Testing & Quality

### Unit Tests
- ✅ FileRenamerService tests (14 tests passing)
- ✅ DuplicatesScanner tests
- ✅ FFmpegProgressParser tests
- ✅ UpscalerService tests
- ⏳ Disk cleanup service tests
- ⏳ Privacy cleaner service tests
- ⏳ Startup manager service tests
- ⏳ Metadata service tests

### Integration Tests
- ✅ VideoCombiner with fake ffmpeg/ffprobe
- ✅ Cancellation tests for media conversion
- ⏳ USB bootable creation tests (with mock)
- ⏳ ICO conversion tests

### Parity Tests
- ✅ Duplicates E2E parity (SelectOldest)
- ⏳ File Renamer parity with PowerShell
- ⏳ All selection strategies parity
- ⏳ Metadata editing parity
- ⏳ Complete parity matrix for all features

## UX & Polish

### UI Enhancements
- ⏳ Keyboard shortcuts (Ctrl+O, Ctrl+S, etc.)
- ⏳ Toolbar with icon buttons
- ⏳ Application icon
- ⏳ Feature-specific icons
- ⏳ Consistent button styling
- ⏳ Status bar improvements
- ⏳ Progress indicators for long operations

### Help & Documentation
- ⏳ Help menu with documentation
- ⏳ About dialog with version info
- ⏳ Tooltips for all controls
- ⏳ Inline help text
- ⏳ User guide (markdown or HTML)

### Settings & Configuration
- ⏳ Persistent settings (tool paths, default options)
- ⏳ Theme support (Light/Dark already implemented)
- ⏳ Export/Import configuration
- ⏳ Recent workspaces list

## Packaging & Deployment
- ⏳ MSI installer creation
- ⏳ ClickOnce deployment option
- ⏳ Self-contained deployment (include .NET runtime)
- ⏳ Automated build pipeline (GitHub Actions or Azure DevOps)
- ⏳ Release notes generation
- ⏳ Version management

## Known Issues & Improvements
- ⏳ Fix nullable reference warnings in HiderViewModel
- ⏳ Update to Microsoft.NET.Sdk (remove WindowsDesktop SDK)
- ⏳ Upgrade Microsoft.CodeAnalysis.NetAnalyzers to 10.0.0
- ⏳ Implement proper logging framework
- ⏳ Add crash reporting/telemetry (optional)
- ⏳ Performance profiling for large file operations

## Priority Order (Next Sprint)
1. Media Conversion - ICO Converter (high user demand)
2. Media Conversion - Image Resizer (high user demand)
3. Duplicates - Enhanced scanner UI
4. Cleanup - Disk Cleanup (quick win)
5. Cleanup - Privacy Cleaner (high value)
6. Security - Startup Manager (moderate complexity)
7. Metadata - ExifTool integration (complex, high value)
8. Media Conversion - Bootable USB (complex, lower priority)

## Technical Debt
- Consolidate duplicate code in ViewModels
- Create base classes for common patterns
- Implement service locator or DI container
- Refactor file operation helpers into shared library
- Add XML documentation to all public APIs
- Create reusable WPF controls for common patterns

---

**Last Updated:** January 9, 2026
**Target Release:** Q1 2026
**Completion Estimate:** 65% complete
