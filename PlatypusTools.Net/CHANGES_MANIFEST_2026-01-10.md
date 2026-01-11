# PlatypusTools Changes Manifest
## Changes Since 9:00 PM on January 10, 2026

### Summary
This manifest documents all source file changes made after 9:00 PM on 1/10/2026.
Last MSI Build: Version 2.0.0.3 (built 01/11/2026 02:37 AM)

### IMPORTANT: Installation Steps
**If the installed app is still showing old features after installing the new MSI:**
1. Open "Add or Remove Programs" in Windows Settings
2. Find "PlatypusTools" and UNINSTALL it completely
3. Reboot the computer (clears Windows Installer cache)
4. Install the new MSI: `PlatypusToolsSetup.msi` (Version 2.0.0.3)

---

## Critical Features Verified in Source Code

### 1. Metadata Tab
- **File:** `PlatypusTools.UI/MainWindow.xaml` (Line ~178)
- **Status:** ✅ Present
- **Feature:** Metadata tab with MetadataEditorView

### 2. Multimedia Tab (VLC/Audacity/GIMP Embedding)
- **File:** `PlatypusTools.UI/MainWindow.xaml` (Line ~228)
- **Status:** ✅ Present (restored this session)
- **Feature:** Multimedia tab under Tools with VLC, Audacity, GIMP embedding

### 3. MultimediaEditorViewModel
- **File:** `PlatypusTools.UI/ViewModels/MainWindowViewModel.cs` (Line 37)
- **Status:** ✅ Present (restored this session)
- **Feature:** `MultimediaEditor = new MultimediaEditorViewModel();`

### 4. CanUserResizeColumns in DataGrids
- **Files:** 
  - `PlatypusTools.UI/Views/MetadataEditorView.xaml`
  - `PlatypusTools.UI/Views/DuplicatesView.xaml`
- **Status:** ✅ Present (verified)

### 5. Pack URI Icon
- **File:** `PlatypusTools.UI/MainWindow.xaml` (Line 7)
- **Status:** ✅ Present
- **Feature:** `Icon="pack://application:,,,/Assets/platypus.png"`

---

## Source Files Modified Since 9:00 PM (1/10/2026)

### Core Services (Modified 11:46 PM)
| File | Last Modified |
|------|--------------|
| PlatypusTools.Core/Config/AppConfig.cs | 01/10 23:46 |
| PlatypusTools.Core/Models/FilenameComponents.cs | 01/10 23:46 |
| PlatypusTools.Core/Models/HiderConfig.cs | 01/10 23:46 |
| PlatypusTools.Core/Models/ProcessResult.cs | 01/10 23:46 |
| PlatypusTools.Core/Models/RenameOperation.cs | 01/10 23:46 |
| PlatypusTools.Core/Models/VideoConversionTask.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/BootableUSBService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/DependencyCheckerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/DiskCleanupService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/DiskSpaceAnalyzerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/DuplicatesScanner.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/FFmpegProgressParser.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/FFmpegService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/FFprobeService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/FileAnalyzerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/FileCleaner.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/FileRenamerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/HiderService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/IconConverterService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/ImageConversionService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/ImageResizerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/MediaLibraryService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/MediaService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/MetadataService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/NetworkToolsService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/PrivacyCleanerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/ProcessManagerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/RecentCleaner.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/RegistryCleanerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/ScheduledTasksService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/SecurityService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/SimpleLogger.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/StartupManagerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/SystemAuditService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/SystemRestoreService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/UpscalerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/VideoCombinerService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/VideoConverterService.cs | 01/10 23:46 |
| PlatypusTools.Core/Services/WebsiteDownloaderService.cs | 01/10 23:46 |
| PlatypusTools.Core/Utilities/ElevationHelper.cs | 01/10 23:46 |

### UI Views (Modified 11:46 PM)
| File | Last Modified |
|------|--------------|
| PlatypusTools.UI/Views/MetadataEditorView.xaml | 01/10 23:46 |
| PlatypusTools.UI/Views/MetadataEditorView.xaml.cs | 01/10 23:46 |
| PlatypusTools.UI/Views/MultimediaEditorView.xaml | 01/10 23:46 |
| PlatypusTools.UI/Views/MultimediaEditorView.xaml.cs | 01/10 23:46 |
| PlatypusTools.UI/Views/DuplicatesView.xaml | 01/10 23:46 |
| PlatypusTools.UI/Views/DuplicatesView.xaml.cs | 01/10 23:46 |
| PlatypusTools.UI/Views/FileAnalyzerView.xaml | 01/10 23:46 |
| PlatypusTools.UI/Views/FileAnalyzerView.xaml.cs | 01/10 23:46 |
| (... and 40+ additional view files) | 01/10 23:46 |

### UI ViewModels (Modified 11:46 PM)
| File | Last Modified |
|------|--------------|
| PlatypusTools.UI/ViewModels/MetadataEditorViewModel.cs | 01/10 23:46 |
| PlatypusTools.UI/ViewModels/MultimediaEditorViewModel.cs | 01/10 23:46 |
| PlatypusTools.UI/ViewModels/FileAnalyzerViewModel.cs | 01/10 23:46 |
| PlatypusTools.UI/ViewModels/DuplicatesViewModel.cs | 01/10 23:46 |
| (... and 25+ additional ViewModel files) | 01/10 23:46 |

### Installer Files (Modified After 11:46 PM)
| File | Last Modified | Notes |
|------|--------------|-------|
| PlatypusTools.Installer/FilesGenerated.wxs | 01/11 00:06 | Auto-generated |
| PlatypusTools.Installer/Shortcuts.wxs | 01/11 01:01 | Shortcut definitions |
| PlatypusTools.Installer/Files.wxs | 01/11 01:39 | File components |
| PlatypusTools.Installer/PlatypusTools.Installer.wixproj | 01/11 01:48 | Build config |
| PlatypusTools.Installer/PublishFiles.wxs | 01/11 02:05 | Publish output harvest |
| PlatypusTools.UI/Views/HelpWindow.xaml | 01/11 02:05 | Help docs |
| PlatypusTools.UI/MainWindow.xaml | 01/11 02:20 | **Multimedia tab restored** |
| PlatypusTools.UI/ViewModels/MainWindowViewModel.cs | 01/11 02:20 | **MultimediaEditor added** |
| PlatypusTools.Installer/Product.wxs | 01/11 02:26 | Version 2.0.0.2 |

---

## Build Verification

### Source Code Timestamps
- MainWindow.xaml: 01/11/2026 02:20 AM ✅
- MainWindowViewModel.cs: 01/11/2026 02:20 AM ✅
- Product.wxs (MSI Version): 01/11/2026 02:26 AM ✅

### Publish Output Timestamp
- PlatypusTools.UI.exe: 01/11/2026 02:21 AM ✅

### MSI Installer Timestamp
- PlatypusToolsSetup.msi: 01/11/2026 02:26 AM ✅

---

## Recommended Action

If the installed application is still showing old features, the issue is likely:
1. **Old MSI cached in Windows Installer** - Uninstall completely, reboot, then reinstall
2. **MSI not being rebuilt from clean state** - Run clean rebuild:
   ```
   dotnet clean
   dotnet publish ... --no-incremental
   dotnet build PlatypusTools.Installer.wixproj
   ```

---

Generated: 01/11/2026
