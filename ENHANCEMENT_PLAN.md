# PlatypusTools Enhancement Plan

## Immediate Fixes Required

### 1. Startup Manager Crash Fix
**Issue**: Application crashes when navigating to Startup Manager tab
**Cause**: Likely null reference in StartupManagerService.GetStartupItems()
**Fix**: Add null safety checks and error handling

### 2. DataGrid Column Resizing
**Status**: Partially fixed in FileCleanerView
**Needed**: Apply to ALL DataGrid controls across the application

**Files requiring CanUserResizeColumns="True"**:
- HiderView.xaml
- FileRenamerView.xaml  
- VideoConverterView.xaml
- MetadataEditorView.xaml
- SystemAuditView.xaml
- DiskCleanupView.xaml
- DuplicatesView.xaml
- IconConverterView.xaml
- ImageResizerView.xaml
- RecentCleanupView.xaml
- PrivacyCleanerView.xaml

### 3. Default Column Widths
**Issue**: Text is cut off, columns too narrow
**Fix**: Set proportional widths with MinWidth for all DataGrid columns

---

## Required External Dependencies

### FFmpeg/FFprobe
**Required for**: Video conversion, video combining, media analysis
**Location**: Should be in `Tools/` subdirectory or PATH
**Solution**: 
1. Check for FFmpeg on startup
2. Prompt user to download if missing
3. Provide download link: https://ffmpeg.org/download.html
4. Auto-detect common installation paths

### ExifTool  
**Required for**: Metadata editing
**Location**: Should be in `Tools/exiftool_files/` subdirectory
**Solution**:
1. Bundle with installer
2. Check on first use of Metadata tab
3. Download link: https://exiftool.org/

### WebView2 Runtime
**Required for**: HTML help system
**Status**: Already added as NuGet package
**Note**: May require user to install WebView2 Runtime on older Windows versions

---

## Proposed New Features

### 1. Website Downloader Tab
**Source**: C:\Projects\Website Downloader (Python)
**Core Functionality**:
- Download assets/files from websites
- Support for various file types (images, videos, documents)
- Recursive crawling with depth limits
- Pattern matching for URLs
- Download queue management
- Progress tracking
- Resume capability
- Duplicate detection

**C# Implementation Requirements**:
- HttpClient for downloads
- HTML parsing (HtmlAgilityPack NuGet package)
- URL pattern matching (Regex)
- Async/await for concurrent downloads
- Download manager service
- Settings for concurrent connections, timeouts, user-agent

**UI Components**:
- URL input field
- File type filters
- Output directory selector
- Download queue grid
- Progress bars
- Speed/ETA indicators
- Downloaded files list

### 2. File Analyzer Tab
**Source**: C:\Projects\FileAnalyzer (Python)
**Core Functionality**:
- File type analysis and statistics
- Directory tree visualization
- Size distribution charts
- File age analysis
- Extension mapping
- Large file finder
- Empty file/folder detection
- Hash calculation for integrity checks

**C# Implementation Requirements**:
- Recursive file system scanning
- File size calculation with aggregation
- Date/time analysis
- Chart/graph rendering (OxyPlot or LiveCharts NuGet)
- Export to CSV/JSON
- Filtering and sorting capabilities

**UI Components**:
- Tree view for directory structure
- Statistics dashboard
- Chart/graph displays
- File list with sorting
- Export options
- Search and filter controls

### 3. Batch File Operations
**Enhancement to existing File Cleaner**:
- Copy/Move operations (not just rename)
- Batch attribute changes
- Timestamp modification
- Permission management
- Hash calculation and verification

### 4. Media Library Manager
**New comprehensive media tool**:
- Organize media files by metadata
- Auto-rename based on EXIF/metadata
- Duplicate media detection (by content, not just hash)
- Media preview panel
- Batch thumbnail generation
- Media format conversion queue

### 5. System Restore Point Manager
**New system utility**:
- Create restore points
- List existing restore points
- Restore from points
- Delete old restore points
- Schedule automatic creation

### 6. Registry Cleaner (Advanced)
**New system utility**:
- Scan for invalid registry entries
- Backup registry before changes
- Whitelist/blacklist patterns
- Safe cleanup with undo capability

### 7. Disk Space Analyzer
**Enhanced version of current cleanup**:
- Tree map visualization
- Folder size calculation
- WinDirStat-style display
- Quick actions for large folders
- Search for files by size/age
- Duplicate finder integration

### 8. Network Tools
**New utilities tab**:
- Port scanner
- Ping/traceroute
- DNS lookup
- Network adapter info
- Bandwidth monitor
- Connection viewer

### 9. Scheduled Tasks Manager
**Addition to System tools**:
- View all scheduled tasks
- Create new tasks
- Enable/disable tasks
- Edit task schedules
- Run tasks on demand
- Export/import task configurations

### 10. Process Manager
**System monitoring tool**:
- Process list with details
- CPU/Memory usage
- Kill processes
- Service management
- Startup impact analysis
- DLL viewer

---

## Installation & Deployment

### Installer Requirements
1. **Include in installer package**:
   - FFmpeg binaries (optional download component)
   - ExifTool
   - WebView2 Runtime (bootstrapper)
   - Default configuration files
   - Help HTML documentation
   - Sample themes/skins

2. **Installation checks**:
   - Verify .NET 10 runtime installed
   - Check for required Windows version (10/11)
   - Detect administrator privileges
   - Create Tools directory structure

3. **Post-install configuration**:
   - First-run wizard
   - Dependency checker with download links
   - Settings import from PowerShell version
   - Create desktop/start menu shortcuts

### Directory Structure
```
C:\Program Files\PlatypusTools\
├── PlatypusTools.UI.exe
├── Assets\
│   ├── platypus.png
│   └── PlatypusTools_Help.html
├── Tools\
│   ├── ffmpeg\
│   │   ├── ffmpeg.exe
│   │   ├── ffprobe.exe
│   │   └── ffplay.exe
│   ├── exiftool\
│   │   └── exiftool.exe
│   └── Logs\
├── Data\
│   ├── Settings\
│   ├── Workspaces\
│   └── Backups\
└── Logs\
```

---

## Priority Implementation Order

### Phase 1 (Immediate - Critical Bugs)
1. ✅ Fix Startup Manager crash
2. ✅ Make all DataGrid columns resizable
3. ✅ Set proper default column widths
4. ✅ Add dependency checker on startup

### Phase 2 (High Priority - Core Features)
5. 🔄 Website Downloader tab
6. 🔄 File Analyzer tab
7. 🔄 Dependency installer/downloader
8. 🔄 Comprehensive help system integration

### Phase 3 (Medium Priority - Enhanced Features)
9. ⏳ Media Library Manager
10. ⏳ Disk Space Analyzer with visualization
11. ⏳ Batch file operations enhancements
12. ⏳ Scheduled Tasks Manager

### Phase 4 (Low Priority - Advanced Tools)
13. ⏳ Registry Cleaner
14. ⏳ Network Tools
15. ⏳ Process Manager
16. ⏳ System Restore Point Manager

---

## Testing Requirements

### Unit Tests
- All new services
- File operations with rollback
- Download manager
- File analyzer algorithms

### Integration Tests
- UI interaction tests
- External tool integration (FFmpeg, ExifTool)
- Cross-feature workflows

### Manual Testing Checklist
- [ ] All tabs load without crashing
- [ ] All DataGrids allow column resizing
- [ ] Text is fully visible in all columns
- [ ] Help system opens and navigates correctly
- [ ] External dependencies are detected
- [ ] Download operations complete successfully
- [ ] File analysis produces correct results
- [ ] All cleanup operations are reversible

---

## Documentation Updates Needed

1. Update HTML help with new features
2. Create quick-start guide
3. Add troubleshooting section
4. Document all keyboard shortcuts
5. Create video tutorials (optional)
6. Update README with feature list
7. Create developer documentation for extending

---

## Performance Considerations

### Large File Handling
- Stream processing for files >1GB
- Async operations for responsiveness
- Progress reporting for long operations
- Cancellation support for all operations

### UI Responsiveness
- Background workers for all intensive operations
- Virtual scrolling for large DataGrids
- Lazy loading for tree views
- Debounced search/filter operations

### Memory Management
- Dispose of resources properly
- Use weak references where appropriate
- Limit in-memory file lists
- Page large result sets

---

## Security Considerations

1. **File Operations**: Always backup before destructive operations
2. **Registry Access**: Require administrator elevation
3. **Network Operations**: Validate URLs, sanitize inputs
4. **Download Safety**: Virus scan integration (optional)
5. **Data Privacy**: Never upload user data without consent
6. **Credentials**: Secure storage for any saved credentials

---

## Accessibility Features

1. Keyboard navigation for all features
2. Screen reader support
3. High contrast mode
4. Configurable font sizes
5. Tooltips on all controls
6. Status announcements

---

## Future Considerations

- Plugin architecture for extensibility
- Scripting support (PowerShell or Python)
- Cloud backup integration
- Portable version
- Multi-language support
- Dark/Light theme toggle (already implemented)
- Custom color schemes
- Scheduled automation
- Remote management capabilities
