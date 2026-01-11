# Help Section → Code Map (short)

This table links user-facing help sections to the primary functions and code locations in `PlatypusTools.ps1`.

| Help Section | Primary functions / handlers | Notes (where to look) |
|--------------|-----------------------------|-----------------------|
| File Cleaner | Get-FilteredFiles, Get-ProposedName, Get-CommonPrefix, Normalize-NameSpaces, Rename-ItemCaseAware, BtnScan/BtnApply handlers | See `Get-FilteredFiles`, `Get-ProposedName`, `BtnScan.Add_Click`, `BtnApply.Add_Click` in `PlatypusTools.ps1` (File Cleaner region) |
| Media Conversion | Ensure-Tools, Run-FFmpegLogged, Get-OutputDir, BtnCombConvert/BtnCombCombine/BtnCombSafe handlers | Video combiner & convert logic, `Run-FFmpegLogged` and combiner handlers (ffmpeg integration) |
| Graphics / Image Resize | Convert-ImageToIco, Add-ImgFiles, BtnImgConvert, Resize logic (BtnResizeRun) | Image conversion and resize functions; ICO creation in `Convert-ImageToIco` |
| Duplicates | Get-FileFingerprint, Get-FastFileHash, Get-DupExtensionsFromUI, BtnDupScan handler, Get-SelectedDupes | See duplicate scan workflow: listing, hashing, grouping (BtnDupScan.Add_Click) |
| Cleanup (Disk) | Get-DiskCleanupPaths, BtnDiskAnalyze.Add_Click, BtnDiskClean.Add_Click | Disk cleanup paths and cleaning handlers |
| Privacy Cleaner | Get-BrowserPaths, Get-IdentityPaths, BtnPrivAnalyze.Add_Click, BtnPrivClean.Add_Click | Browser & Windows identity cleanup logic |
| Folder Hider & Security | Get-HiderConfig, Add-HiderRecord, Hide-HiderFolder, Unhide-HiderFolder, Set-AclRestriction, Run-SecurityScan, Show-CriticalAclWindow, Show-OutboundWindow | Folder Hider core functions and security scan utilities |
| Metadata Editor | Load-FileMetadata, BtnMetaSave.Add_Click, ExifTool and ffprobe usage | Uses `exiftool` and `ffprobe` when available; metadata grid handlers |
| Startup Manager & Users | Get-StartupItems and Get-LocalUsersSafe, startup handlers | Startup scan and user management handlers |
| Configuration & UI | Load-AppConfig, Save-AppConfig, Apply-Theme, Apply-FontSize | App config JSON (`PlatypusTools_Config.json`) and menu handlers |

Notes:
- Search for function names (e.g., `Get-ProposedName`) in `PlatypusTools.ps1` to jump directly to implementation.
- The app embeds XAML UI and wires event handlers to the functions above; UI control names are defined near the XAML and the associated Add_Click handlers implement behavior.

If you'd like, I can annotate the file with exact line ranges or create IDE-friendly anchors for quicker navigation.