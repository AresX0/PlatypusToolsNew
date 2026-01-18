# PlatypusTools - Complete Project Documentation

**Version**: 2.0.0.17  
**Branch**: `v3.0.0-major-features`  
**Target Framework**: .NET 10.0 (Windows)  
**Last Updated**: January 11, 2026

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Solution Structure](#solution-structure)
3. [PlatypusTools.Core Project](#platypustoolscore-project)
4. [PlatypusTools.UI Project](#platypustoolsui-project)
5. [PlatypusTools.Installer Project](#platypustoolsinstaller-project)
6. [External Dependencies](#external-dependencies)
7. [Build Instructions](#build-instructions)

---

## Project Overview

PlatypusTools is a comprehensive Windows system utility and media management application built with WPF using the MVVM (Model-View-ViewModel) pattern. The application provides:

- **System Cleaning**: Disk cleanup, privacy cleaning, registry cleaning
- **File Management**: Duplicate finder, empty folder scanner, file renamer, file analyzer
- **Media Tools**: Video converter, video combiner, image converter, image resizer, metadata editor
- **System Tools**: Startup manager, process manager, scheduled tasks, system restore
- **Hider**: Hide files and folders with password protection

### Architecture Diagram

```
┌────────────────────────────────────────────────────────────────┐
│                    PlatypusTools.UI                            │
│                  (.NET 10 WPF Application)                     │
├────────────────────────────────────────────────────────────────┤
│  Views (XAML)  │  ViewModels (C#)  │  Services  │  Converters  │
├────────────────────────────────────────────────────────────────┤
│                    PlatypusTools.Core                          │
│                    (.NET 10 Class Library)                     │
├────────────────────────────────────────────────────────────────┤
│     Services     │     Models     │     Config     │  Utilities │
├────────────────────────────────────────────────────────────────┤
│                    External Tools                              │
│         ffmpeg.exe │ exiftool.exe │ fpcalc.exe                 │
└────────────────────────────────────────────────────────────────┘
```

---

## Solution Structure

```
PlatypusToolsNew.sln
│
├── PlatypusTools.Core/           # Core business logic library
│   ├── Config/                   # Application configuration
│   ├── Models/                   # Data models
│   ├── Services/                 # Business logic services
│   └── Utilities/                # Helper utilities
│
├── PlatypusTools.UI/             # WPF application
│   ├── Assets/                   # External tools and resources
│   ├── Converters/               # XAML value converters
│   ├── Models/                   # UI-specific models
│   ├── Services/                 # UI services
│   ├── Themes/                   # Dark/Light theme XAML
│   ├── ViewModels/               # MVVM ViewModels
│   └── Views/                    # XAML views and windows
│
├── PlatypusTools.Installer/      # WiX MSI installer
│   ├── Package.wxs               # Main installer definition
│   └── PublishFiles.wxs          # File harvesting
│
├── PlatypusTools.Core.Tests/     # Unit tests
│
└── DOCS/                         # Documentation
    ├── IMPLEMENTATION_MANIFEST.md
    ├── TODO.md
    ├── COPILOT_CONTINUATION_PROMPT.md
    └── PROJECT_DOCUMENTATION.md
```

---

## PlatypusTools.Core Project

The Core project contains all business logic, separated from UI concerns.

### Config/AppConfig.cs

**Purpose**: Centralized application configuration management.

```csharp
namespace PlatypusTools.Core.Config
{
    /// <summary>
    /// Provides application-wide configuration settings.
    /// Manages paths, default values, and runtime configuration.
    /// </summary>
    public static class AppConfig
    {
        // Configuration properties and methods
    }
}
```

**Key Responsibilities**:
- Define default file paths
- Store application constants
- Manage configuration file locations

---

### Models/FilenameComponents.cs

**Purpose**: Parse and represent components of a filename for renaming operations.

```csharp
/// <summary>
/// Represents the parsed components of a filename.
/// Used by FileRenamerService for pattern-based renaming.
/// </summary>
public class FilenameComponents
{
    public string Directory { get; set; }      // Parent directory path
    public string Name { get; set; }           // Filename without extension
    public string Extension { get; set; }      // File extension including dot
    public int? Number { get; set; }           // Extracted numeric portion
    public string Prefix { get; set; }         // Text before number
    public string Suffix { get; set; }         // Text after number
}
```

---

### Models/HiderConfig.cs

**Purpose**: Configuration model for the Hider feature.

```csharp
/// <summary>
/// Stores configuration for hidden items including paths,
/// encryption settings, and access credentials.
/// </summary>
public class HiderConfig
{
    public List<HiddenItem> Items { get; set; }    // List of hidden paths
    public string PasswordHash { get; set; }        // Hashed access password
    public string Salt { get; set; }                // Salt for password hashing
    public DateTime LastAccessed { get; set; }      // Last access timestamp
}

public class HiddenItem
{
    public string OriginalPath { get; set; }       // Original file/folder path
    public string HiddenPath { get; set; }         // Path after hiding
    public bool IsFolder { get; set; }             // True if directory
    public DateTime HiddenDate { get; set; }       // When it was hidden
}
```

---

### Models/ProcessResult.cs

**Purpose**: Standardized result object for service operations.

```csharp
/// <summary>
/// Represents the result of a service operation.
/// Provides success status, messages, and any output data.
/// </summary>
public class ProcessResult
{
    public bool Success { get; set; }              // Operation succeeded
    public string Message { get; set; }            // Status message
    public string ErrorMessage { get; set; }       // Error details if failed
    public int ExitCode { get; set; }              // Process exit code
    public string StandardOutput { get; set; }     // Stdout content
    public string StandardError { get; set; }      // Stderr content
}
```

---

### Models/RenameOperation.cs

**Purpose**: Represents a file rename operation for preview and execution.

```csharp
/// <summary>
/// Defines a single file rename operation.
/// Used to preview changes before applying them.
/// </summary>
public class RenameOperation
{
    public string SourcePath { get; set; }         // Current file path
    public string DestinationPath { get; set; }    // New file path
    public string OriginalName { get; set; }       // Current filename
    public string NewName { get; set; }            // New filename
    public bool HasConflict { get; set; }          // Name collision detected
    public string ConflictMessage { get; set; }    // Conflict description
}
```

---

### Models/VideoConversionTask.cs

**Purpose**: Represents a video conversion job with all settings.

```csharp
/// <summary>
/// Defines parameters for a video conversion operation.
/// Includes source, destination, codec, and quality settings.
/// </summary>
public class VideoConversionTask
{
    public string InputPath { get; set; }          // Source video path
    public string OutputPath { get; set; }         // Destination path
    public string VideoCodec { get; set; }         // e.g., "h264", "hevc"
    public string AudioCodec { get; set; }         // e.g., "aac", "mp3"
    public int? Bitrate { get; set; }              // Video bitrate in kbps
    public int? AudioBitrate { get; set; }         // Audio bitrate in kbps
    public string Resolution { get; set; }         // e.g., "1920x1080"
    public double? Fps { get; set; }               // Frames per second
    public TimeSpan? StartTime { get; set; }       // Trim start
    public TimeSpan? Duration { get; set; }        // Trim duration
}
```

---

### Services/BootableUSBService.cs

**Purpose**: Create bootable USB drives from ISO files.

**Key Methods**:
- `CreateBootableUSBAsync(string isoPath, string driveLetter)` - Creates bootable USB
- `GetAvailableUSBDrives()` - Lists connected USB drives
- `ValidateISOFile(string path)` - Validates ISO file format

**Dependencies**: Windows diskpart, bcdboot

---

### Services/DependencyCheckerService.cs

**Purpose**: Verify external tool dependencies are available.

**Key Methods**:
- `CheckFFmpeg()` - Verifies ffmpeg.exe exists and is functional
- `CheckExifTool()` - Verifies exiftool.exe exists
- `CheckAllDependencies()` - Comprehensive check of all tools

**Usage**: Called at startup to ensure required tools are present.

---

### Services/DiskCleanupService.cs

**Purpose**: Clean temporary files and free disk space.

**Key Methods**:
- `ScanForCleanableItemsAsync()` - Find temp files, caches, logs
- `CleanItemsAsync(IEnumerable<CleanupItem> items)` - Delete selected items
- `GetTotalCleanableSize()` - Calculate potential space savings

**Cleaned Locations**:
- Windows Temp folder
- User Temp folder
- Browser caches
- Recycle Bin
- Windows Update cache
- Thumbnail cache

---

### Services/DiskSpaceAnalyzerService.cs

**Purpose**: Analyze disk space usage with visual breakdown.

**Key Methods**:
- `AnalyzeDriveAsync(string drive)` - Scan drive for space usage
- `GetFolderSizesAsync(string path)` - Calculate folder sizes recursively
- `GetLargestFilesAsync(string path, int count)` - Find largest files

**Output**: Hierarchical view of disk usage by folder.

---

### Services/DuplicatesScanner.cs

**Purpose**: Find duplicate files using hash comparison.

**Key Methods**:
- `ScanForDuplicatesAsync(string path, IProgress<int> progress)` - Scan directory
- `ComputeFileHashAsync(string path)` - Calculate MD5/SHA256 hash
- `GetDuplicateGroups()` - Group files by matching hash

**Algorithm**:
1. First pass: Group by file size
2. Second pass: Hash first 1KB of same-size files
3. Third pass: Full hash of candidates

---

### Services/EmptyFolderScannerService.cs

**Purpose**: Find and optionally delete empty folders.

**Key Methods**:
- `ScanAsync(string rootPath, bool includeHidden)` - Find empty folders
- `DeleteEmptyFoldersAsync(IEnumerable<string> paths)` - Remove folders
- `IsDirectoryEmpty(string path)` - Check if folder is empty

**Definition of "Empty"**: No files, only empty subdirectories.

---

### Services/FFmpegProgressParser.cs

**Purpose**: Parse FFmpeg console output for progress information.

**Key Methods**:
- `ParseProgress(string line)` - Extract progress from output line
- `GetDuration(string ffprobeOutput)` - Parse total duration
- `CalculatePercentage(TimeSpan current, TimeSpan total)` - Compute progress %

**Parsed Information**:
- Current time position
- Frame number
- Bitrate
- Speed multiplier

---

### Services/FFmpegService.cs

**Purpose**: Wrapper for FFmpeg command-line operations.

**Key Methods**:
- `ConvertAsync(VideoConversionTask task, IProgress<double> progress, CancellationToken ct)`
- `CombineVideosAsync(string[] inputs, string output, CancellationToken ct)`
- `ExtractAudioAsync(string input, string output)`
- `TrimVideoAsync(string input, string output, TimeSpan start, TimeSpan duration)`
- `AddWatermarkAsync(string input, string watermark, string output, string position)`

**FFmpeg Path**: `Assets/ffmpeg.exe`

---

### Services/FFprobeService.cs

**Purpose**: Extract media file metadata using FFprobe.

**Key Methods**:
- `GetMediaInfoAsync(string path)` - Get full media info as JSON
- `GetDurationAsync(string path)` - Get video/audio duration
- `GetResolutionAsync(string path)` - Get video dimensions
- `GetCodecsAsync(string path)` - Get video/audio codecs

**FFprobe Path**: `Assets/ffprobe.exe`

---

### Services/FileAnalyzerService.cs

**Purpose**: Analyze files for detailed information.

**Key Methods**:
- `AnalyzeFileAsync(string path)` - Get comprehensive file info
- `GetFileHashAsync(string path, HashAlgorithm algorithm)` - Compute hash
- `DetectFileType(string path)` - Identify file type by magic bytes

**Analyzed Properties**:
- Size, dates, attributes
- Hash values (MD5, SHA256)
- MIME type
- Digital signature (if applicable)

---

### Services/FileCleaner.cs

**Purpose**: Clean up file metadata and unwanted attributes.

**Key Methods**:
- `CleanFileMetadataAsync(string path)` - Remove metadata
- `StripExifDataAsync(string imagePath)` - Remove EXIF from images
- `RemoveAlternateDataStreams(string path)` - Clear ADS

---

### Services/FileRenamerService.cs

**Purpose**: Batch rename files with pattern support.

**Key Methods**:
- `GeneratePreview(string folder, string pattern)` - Preview renames
- `ExecuteRenameAsync(IEnumerable<RenameOperation> operations)` - Apply renames
- `ParsePattern(string pattern)` - Parse rename pattern syntax

**Pattern Syntax**:
- `{name}` - Original filename
- `{ext}` - Extension
- `{num}` - Sequential number
- `{date}` - Current date
- `{parent}` - Parent folder name

---

### Services/HiderService.cs

**Purpose**: Hide files and folders with password protection.

**Key Methods**:
- `HideItemAsync(string path, string password)` - Hide file/folder
- `UnhideItemAsync(string hiddenPath, string password)` - Reveal item
- `ValidatePassword(string password)` - Verify access password
- `SetPassword(string newPassword)` - Change password

**Hiding Mechanism**:
1. Move to hidden location (AppData)
2. Apply Hidden + System attributes
3. Encrypt file list with password

---

### Services/IconConverterService.cs

**Purpose**: Convert images to .ico format.

**Key Methods**:
- `ConvertToIconAsync(string imagePath, string outputPath)` - Create .ico
- `GenerateMultiSizeIconAsync(string imagePath, string outputPath)` - Multi-resolution

**Supported Sizes**: 16x16, 32x32, 48x48, 64x64, 128x128, 256x256

---

### Services/ImageConversionService.cs

**Purpose**: Convert between image formats.

**Key Methods**:
- `ConvertAsync(string input, string output, string format)` - Convert format
- `BatchConvertAsync(string[] inputs, string outputFolder, string format)` - Batch

**Supported Formats**: PNG, JPEG, BMP, GIF, TIFF, WEBP, ICO

---

### Services/ImageResizerService.cs

**Purpose**: Resize images with various options.

**Key Methods**:
- `ResizeAsync(string input, string output, int width, int height)` - Resize
- `ResizeByPercentAsync(string input, string output, int percent)` - Scale by %
- `MaintainAspectRatio(int width, int height, Size original)` - Calculate new size

**Options**:
- Maintain aspect ratio
- Crop to fit
- Pad with color
- Quality setting (for JPEG)

---

### Services/MediaLibraryService.cs

**Purpose**: Organize and index media files.

**Key Methods**:
- `ScanLibraryAsync(string[] folders)` - Index media files
- `SearchAsync(string query)` - Search indexed files
- `GetByTypeAsync(MediaType type)` - Filter by type

**Indexed Properties**:
- Path, size, dates
- Duration (for video/audio)
- Resolution (for images/video)
- Metadata tags

---

### Services/MediaService.cs

**Purpose**: Common media operations and utilities.

**Key Methods**:
- `GetThumbnailAsync(string videoPath, TimeSpan position)` - Extract frame
- `IsMediaFile(string path)` - Check if file is media
- `GetMediaType(string path)` - Determine media type

---

### Services/MetadataService.cs

**Purpose**: Read and write file metadata using ExifTool.

**Key Methods**:
- `ReadMetadataAsync(string path)` - Get all metadata
- `WriteMetadataAsync(string path, Dictionary<string, string> tags)` - Set tags
- `ClearMetadataAsync(string path)` - Remove all metadata
- `CopyMetadataAsync(string source, string destination)` - Copy tags

**ExifTool Path**: `Assets/exiftool.exe`

---

### Services/NativeMetadataReader.cs

**Purpose**: Read basic metadata without external tools.

**Key Methods**:
- `ReadBasicMetadata(string path)` - Get file properties
- `ReadImageDimensions(string imagePath)` - Get image size
- `ReadAudioDuration(string audioPath)` - Get audio length

**Uses**: Windows Shell API, System.Drawing

---

### Services/NetworkToolsService.cs

**Purpose**: Network diagnostic utilities.

**Key Methods**:
- `PingAsync(string host)` - Ping host
- `TraceRouteAsync(string host)` - Trace network path
- `GetLocalIPAddresses()` - List local IPs
- `ScanPortsAsync(string host, int[] ports)` - Port scanner

---

### Services/PrivacyCleanerService.cs

**Purpose**: Clear privacy-sensitive data.

**Key Methods**:
- `ScanForPrivacyItemsAsync()` - Find cleanable items
- `CleanPrivacyItemsAsync(IEnumerable<PrivacyItem> items)` - Clean items

**Cleaned Data**:
- Browser history, cookies, cache
- Recent documents list
- Clipboard content
- Windows search history
- Jump lists

---

### Services/ProcessManagerService.cs

**Purpose**: View and manage running processes.

**Key Methods**:
- `GetRunningProcessesAsync()` - List all processes
- `KillProcessAsync(int pid)` - Terminate process
- `GetProcessDetailsAsync(int pid)` - Detailed info

**Properties**:
- Name, PID, CPU%, Memory
- Path, command line
- Start time, parent PID

---

### Services/RecentCleaner.cs

**Purpose**: Clean recent file/folder history.

**Key Methods**:
- `GetRecentItemsAsync()` - List recent items
- `ClearRecentItemsAsync()` - Clear recent history
- `ClearSpecificItemAsync(string path)` - Remove one item

---

### Services/RegistryCleanerService.cs

**Purpose**: Find and clean invalid registry entries.

**Key Methods**:
- `ScanRegistryAsync()` - Find issues
- `CleanRegistryAsync(IEnumerable<RegistryIssue> issues)` - Fix issues
- `BackupRegistryAsync(string path)` - Create backup

**Scanned Areas**:
- Uninstall entries
- File type associations
- COM/ActiveX entries
- Startup entries

---

### Services/ScheduledTasksService.cs

**Purpose**: View and manage Windows scheduled tasks.

**Key Methods**:
- `GetScheduledTasksAsync()` - List all tasks
- `EnableTaskAsync(string taskPath)` - Enable task
- `DisableTaskAsync(string taskPath)` - Disable task
- `DeleteTaskAsync(string taskPath)` - Remove task

---

### Services/SecurityService.cs

**Purpose**: Security utilities for password hashing and encryption.

**Key Methods**:
- `HashPassword(string password, byte[] salt)` - Create password hash
- `VerifyPassword(string password, string hash, byte[] salt)` - Verify password
- `GenerateSalt()` - Create random salt
- `EncryptData(byte[] data, string password)` - AES encryption
- `DecryptData(byte[] data, string password)` - AES decryption

**Algorithm**: PBKDF2 with SHA256, AES-256-CBC

---

### Services/SimpleLogger.cs

**Purpose**: Application logging service.

**Key Methods**:
- `LogInfo(string message)` - Info level
- `LogWarning(string message)` - Warning level
- `LogError(string message, Exception ex)` - Error level
- `LogDebug(string message)` - Debug level

**Log Location**: `%AppData%\PlatypusTools\Logs\`

---

### Services/StartupManagerService.cs

**Purpose**: Manage Windows startup programs.

**Key Methods**:
- `GetStartupItemsAsync()` - List startup items
- `EnableStartupItemAsync(string name)` - Enable item
- `DisableStartupItemAsync(string name)` - Disable item
- `DeleteStartupItemAsync(string name)` - Remove item

**Sources**:
- Registry Run keys (HKCU, HKLM)
- Startup folder
- Task Scheduler

---

### Services/SystemAuditService.cs

**Purpose**: Comprehensive system information report.

**Key Methods**:
- `GenerateAuditReportAsync()` - Create full report
- `GetSystemInfoAsync()` - Basic system info
- `GetInstalledSoftwareAsync()` - List installed programs

**Report Includes**:
- Hardware specs
- OS information
- Installed software
- Network configuration
- Security status

---

### Services/SystemRestoreService.cs

**Purpose**: Manage Windows System Restore.

**Key Methods**:
- `GetRestorePointsAsync()` - List restore points
- `CreateRestorePointAsync(string description)` - Create point
- `RestoreToPointAsync(int sequenceNumber)` - Restore system

---

### Services/UpscalerService.cs

**Purpose**: AI-powered image upscaling.

**Key Methods**:
- `UpscaleAsync(string input, string output, int scale)` - Upscale image
- `GetAvailableModels()` - List AI models
- `SetModel(string modelName)` - Select model

**Models**: Real-ESRGAN, waifu2x, ESPCN

---

### Services/VideoCombinerService.cs

**Purpose**: Merge multiple videos into one.

**Key Methods**:
- `CombineVideosAsync(string[] inputs, string output, IProgress<double> progress)`
- `ValidateInputFiles(string[] paths)` - Check compatibility
- `GenerateContactList(string[] paths)` - Create FFmpeg concat file

---

### Services/VideoConverterService.cs

**Purpose**: Convert videos between formats.

**Key Methods**:
- `ConvertAsync(VideoConversionTask task, IProgress<double> progress, CancellationToken ct)`
- `GetPresetSettings(string presetName)` - Load preset
- `EstimateOutputSize(VideoConversionTask task)` - Calculate output size

---

### Services/WebsiteDownloaderService.cs

**Purpose**: Download websites for offline viewing.

**Key Methods**:
- `DownloadWebsiteAsync(string url, string outputPath, DownloadOptions options)`
- `GetPageAssets(string url)` - List page resources
- `MirrorSiteAsync(string url, string outputPath)` - Full site mirror

---

### Utilities/ElevationHelper.cs

**Purpose**: Handle UAC elevation for admin operations.

**Key Methods**:
- `IsRunningAsAdmin()` - Check admin status
- `RestartAsAdmin()` - Relaunch with elevation
- `RunProcessAsAdmin(string path, string args)` - Run elevated process

---

## PlatypusTools.UI Project

The UI project contains all WPF views, ViewModels, and UI-specific services.

### App.xaml / App.xaml.cs

**Purpose**: Application entry point and global resources.

```xaml
<Application x:Class="PlatypusTools.UI.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <!-- Global styles and resources -->
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Light.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**Code-Behind Responsibilities**:
- Application startup logic
- Splash screen display
- Global exception handling
- Theme initialization

---

### MainWindow.xaml / MainWindow.xaml.cs

**Purpose**: Main application window with navigation.

**Layout Structure**:
```
┌─────────────────────────────────────────────────────┐
│ Title Bar (menu, theme toggle, help)                │
├──────────────┬──────────────────────────────────────┤
│              │                                      │
│  Navigation  │          Content Area                │
│    (Left)    │          (Right)                     │
│              │                                      │
│  - Home      │     (CurrentView binding)            │
│  - Files     │                                      │
│  - Media     │                                      │
│  - System    │                                      │
│  - Privacy   │                                      │
│  - Tools     │                                      │
│              │                                      │
└──────────────┴──────────────────────────────────────┘
```

**Key Elements**:
- TreeView for navigation
- ContentControl for dynamic view display
- Menu bar with File, Edit, View, Tools, Help
- Theme toggle button

---

### Converters/BytesToMBConverter.cs

**Purpose**: Convert bytes to human-readable sizes.

```csharp
public class BytesToMBConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            if (bytes >= 1073741824) // 1 GB
                return $"{bytes / 1073741824.0:F2} GB";
            if (bytes >= 1048576) // 1 MB
                return $"{bytes / 1048576.0:F2} MB";
            if (bytes >= 1024) // 1 KB
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
        return "0 B";
    }
}
```

---

### Converters/Converters.cs

**Purpose**: Collection of common XAML value converters.

**Included Converters**:
- `BoolToVisibilityConverter` - bool → Visible/Collapsed
- `InverseBoolConverter` - Invert boolean
- `NullToVisibilityConverter` - null → Collapsed
- `StringToVisibilityConverter` - empty → Collapsed
- `DateTimeConverter` - Format DateTime display
- `FileSizeConverter` - Format file sizes

---

### Models/Workspace.cs

**Purpose**: Represent a workspace (folder) for recent workspaces.

```csharp
public class Workspace
{
    public string Path { get; set; }           // Full folder path
    public string Name { get; set; }           // Folder name
    public DateTime LastAccessed { get; set; } // Last use time
    public bool IsPinned { get; set; }         // Pinned to top
}
```

---

### Services/PowerShellRunner.cs

**Purpose**: Execute PowerShell scripts and commands.

**Key Methods**:
- `RunScriptAsync(string script)` - Run PowerShell script
- `RunCommandAsync(string command)` - Run single command
- `RunWithOutputAsync(string script, Action<string> outputHandler)` - Stream output

---

### Services/SettingsManager.cs

**Purpose**: Manage user settings persistence.

**Key Methods**:
- `GetSetting<T>(string key, T defaultValue)` - Read setting
- `SetSetting<T>(string key, T value)` - Write setting
- `SaveSettings()` - Persist to disk
- `LoadSettings()` - Load from disk

**Storage Location**: `%AppData%\PlatypusTools\settings.json`

---

### Services/ThemeManager.cs

**Purpose**: Switch between Dark and Light themes.

**Key Methods**:
- `SetTheme(string themeName)` - Apply theme ("Dark" or "Light")
- `GetCurrentTheme()` - Get active theme name
- `ToggleTheme()` - Switch to opposite theme

**Implementation**:
```csharp
public void SetTheme(string themeName)
{
    var themeUri = new Uri($"Themes/{themeName}.xaml", UriKind.Relative);
    var themeDict = new ResourceDictionary { Source = themeUri };
    
    // Replace theme dictionary
    Application.Current.Resources.MergedDictionaries.Clear();
    Application.Current.Resources.MergedDictionaries.Add(themeDict);
}
```

---

### Services/WorkspaceManager.cs

**Purpose**: Manage recent workspaces history.

**Key Methods**:
- `AddWorkspace(string path)` - Add to recent
- `GetRecentWorkspaces()` - Get list
- `RemoveWorkspace(string path)` - Remove from list
- `PinWorkspace(string path)` - Pin to top
- `ClearAll()` - Clear history

---

### Themes/Dark.xaml

**Purpose**: Dark theme color scheme and control styles.

**Key Colors**:
```xaml
<Color x:Key="BackgroundColor">#1E1E1E</Color>
<Color x:Key="ForegroundColor">#F0F0F0</Color>
<Color x:Key="ControlBackgroundColor">#2D2D2D</Color>
<Color x:Key="BorderColor">#3D3D3D</Color>
<Color x:Key="AccentColor">#8AB4F8</Color>
```

**Styled Controls**: Window, Button, TextBox, ComboBox, ListBox, DataGrid, TreeView, Menu, ScrollBar, TabControl, CheckBox, RadioButton, ProgressBar, Slider, GroupBox, Expander

---

### Themes/Light.xaml

**Purpose**: Light theme color scheme and control styles.

**Key Colors**:
```xaml
<Color x:Key="BackgroundColor">#FFFFFF</Color>
<Color x:Key="ForegroundColor">#1E1E1E</Color>
<Color x:Key="ControlBackgroundColor">#F5F5F5</Color>
<Color x:Key="BorderColor">#D0D0D0</Color>
<Color x:Key="AccentColor">#0078D4</Color>
```

---

### ViewModels/BindableBase.cs

**Purpose**: Base class for all ViewModels implementing INotifyPropertyChanged.

```csharp
public abstract class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;
        
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
```

---

### ViewModels/RelayCommand.cs

**Purpose**: ICommand implementation for MVVM bindings.

```csharp
public class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Predicate<object> _canExecute;
    
    public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
    
    public void Execute(object parameter) => _execute(parameter);
    
    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
```

---

### ViewModels/AsyncRelayCommand.cs

**Purpose**: Async version of RelayCommand for async operations.

```csharp
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object, Task> _execute;
    private readonly Predicate<object> _canExecute;
    private bool _isExecuting;
    
    public async void Execute(object parameter)
    {
        if (_isExecuting) return;
        
        _isExecuting = true;
        try
        {
            await _execute(parameter);
        }
        finally
        {
            _isExecuting = false;
        }
    }
}
```

---

### ViewModels/MainWindowViewModel.cs

**Purpose**: ViewModel for main application window.

**Key Properties**:
- `CurrentView` - Active view content
- `NavigationItems` - Navigation tree items
- `IsDarkTheme` - Current theme state
- `StatusMessage` - Status bar text

**Key Commands**:
- `NavigateCommand` - Switch views
- `ToggleThemeCommand` - Switch theme
- `OpenHelpCommand` - Show help
- `ExitCommand` - Close application

---

### ViewModels/[Feature]ViewModel.cs

Each feature has a corresponding ViewModel:

| ViewModel | Purpose |
|-----------|---------|
| DuplicatesViewModel | Duplicate file finder |
| EmptyFolderScannerViewModel | Empty folder scanner |
| FileRenamerViewModel | Batch file renaming |
| FileAnalyzerViewModel | File analysis |
| VideoConverterViewModel | Video conversion |
| VideoCombinerViewModel | Video merging |
| ImageConverterViewModel | Image format conversion |
| ImageResizerViewModel | Image resizing |
| MetadataEditorViewModel | File metadata editing |
| DiskCleanupViewModel | Disk cleanup |
| PrivacyCleanerViewModel | Privacy cleaning |
| RegistryCleanerViewModel | Registry cleaning |
| StartupManagerViewModel | Startup management |
| ProcessManagerViewModel | Process management |
| ScheduledTasksViewModel | Scheduled tasks |
| SystemRestoreViewModel | System restore |
| HiderViewModel | File/folder hider |
| UpscalerViewModel | Image upscaling |
| NetworkToolsViewModel | Network diagnostics |
| BootableUSBViewModel | Bootable USB creation |

---

### Views/[Feature]View.xaml

Each feature has a corresponding View (XAML):

**Common Pattern**:
```xaml
<UserControl x:Class="PlatypusTools.UI.Views.FeatureView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:PlatypusTools.UI.ViewModels">
    
    <UserControl.DataContext>
        <vm:FeatureViewModel/>
    </UserControl.DataContext>
    
    <Grid>
        <!-- Feature-specific UI -->
    </Grid>
</UserControl>
```

---

### Views/SplashScreenWindow.xaml

**Purpose**: Animated splash screen shown during startup.

**Features**:
- Video background (platypus swimming)
- Application name and version
- Loading indicator

---

### Views/HelpWindow.xaml

**Purpose**: WebView2-based help documentation viewer.

**Content**: Loads `Assets/PlatypusTools_Help.html`

---

## PlatypusTools.Installer Project

WiX 6 MSI installer project.

### Package.wxs

**Purpose**: Main installer definition.

**Sections**:
- Package metadata (name, version, manufacturer)
- Installation directory structure
- Features (selectable components)
- Shortcuts (Start Menu, Desktop)
- Registry entries

### PublishFiles.wxs

**Purpose**: File harvesting from publish output.

**Contains**:
- Component definitions for each file
- Directory structure
- File references

---

## External Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| HtmlAgilityPack | 1.11.x | HTML parsing |
| MetadataExtractor | 2.8.x | Image metadata reading |
| Microsoft.Web.WebView2 | 1.0.x | Embedded browser |
| Microsoft.Data.Sqlite | 8.0.x | Database (future) |

### External Tools (in Assets/)

| Tool | Purpose |
|------|---------|
| ffmpeg.exe | Video/audio processing |
| ffprobe.exe | Media info extraction |
| ffplay.exe | Media playback |
| exiftool.exe | Metadata manipulation |
| fpcalc.exe | Audio fingerprinting |

---

## Build Instructions

### Prerequisites

- Visual Studio 2022 17.8+
- .NET 10 SDK
- WiX Toolset v6
- Windows 10/11 SDK

### Build Commands

```powershell
# Restore packages
dotnet restore PlatypusTools.sln

# Build Debug
dotnet build PlatypusTools.sln

# Build Release
dotnet build PlatypusTools.sln -c Release

# Publish self-contained
dotnet publish PlatypusTools.UI -c Release -r win-x64 --self-contained

# Build installer
dotnet build PlatypusTools.Installer -c Release
```

### Output Locations

- Debug: `PlatypusTools.UI/bin/Debug/net10.0-windows/`
- Release: `PlatypusTools.UI/bin/Release/net10.0-windows/`
- Published: `PlatypusTools.UI/bin/Release/net10.0-windows/win-x64/publish/`
- Installer: `PlatypusTools.Installer/bin/Release/`

---

*Document Version: 1.0*  
*Last Updated: January 11, 2026*
