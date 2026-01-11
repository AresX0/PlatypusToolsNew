# Complete Feature Implementation Plan

## Overview
This document outlines the complete implementation of ALL Platypustools.ps1 features in the .NET version with FULL feature parity.

## Current Status (Before Implementation)
- ❌ File Cleaner: Missing 90% of features (prefix detect, normalize case, complex renaming logic)
- ✅ ICO Converter: Complete
- ✅ Image Resizer: Complete  
- ✅ Disk Cleanup: Complete
- ✅ Privacy Cleaner: Complete
- ✅ Folder Hider: Complete
- ❌ Video Converter: Not implemented
- ❌ Bootable USB Creator: Not implemented
- ❌ Duplicate Finder: Placeholder only
- ❌ System Audit: Placeholder only
- ❌ Startup Manager: Placeholder only
- ❌ Metadata Editor: Placeholder only

## Implementation Priority

### PHASE 1: File Cleaner (CRITICAL - User's Main Complaint)
**Status**: IN PROGRESS

#### 1.1 FileRenamerService Enhancement
- [x] Basic structure exists
- [ ] Add prefix detection algorithm
- [ ] Add case-aware rename (two-step via GUID)
- [ ] Add filename component parser
- [ ] Add core name extraction for sorting
- [ ] Add metadata-based renaming (FFprobe/ExifTool integration)
- [ ] Add comprehensive season/episode parsing
- [ ] Add all normalization options
- [ ] Add custom token removal
- [ ] Add space/separator conversion presets

#### 1.2 FileCleanerViewModel Overhaul
- [ ] Remove dual-tab structure, consolidate to single view
- [ ] Add all prefix controls
- [ ] Add all season/episode controls
- [ ] Add all cleaning controls
- [ ] Add normalization controls
- [ ] Add preview grid with all columns
- [ ] Add Select All/None commands
- [ ] Add Apply/Undo commands
- [ ] Add Dry-run mode
- [ ] Add Export CSV command

#### 1.3 FileCleanerView.xaml Complete Redesign
- [ ] Create single consolidated view
- [ ] Add folder selection + file type filters
- [ ] Add prefix management section
- [ ] Add season/episode section
- [ ] Add filename cleaning section
- [ ] Add normalization section
- [ ] Add preview DataGrid with proper columns
- [ ] Add action buttons (Scan, Apply, Undo, Reset, Export)

### PHASE 2: Missing Media Conversion Features
**Status**: NOT STARTED

#### 2.1 Video Converter
- [ ] Create VideoConverterService
- [ ] Create VideoConverterViewModel
- [ ] Create VideoConverterView
- [ ] Add to Media Conversion tab
- [ ] Support container/codec presets
- [ ] Support quality/bitrate controls

#### 2.2 Bootable USB Creator  
- [ ] Create BootableUSBService (requires admin)
- [ ] Create BootableUSBViewModel
- [ ] Create BootableUSBView
- [ ] Add to Media Conversion tab
- [ ] ISO picker + USB drive picker
- [ ] Boot modes (UEFI, Legacy)
- [ ] File systems (NTFS, FAT32, exFAT)
- [ ] Verify after write option
- [ ] Multi-step confirmation + UAC

### PHASE 3: Duplicates Tab Enhancement
**Status**: PARTIAL (Basic UI exists)

#### 3.1 DuplicateFinderService
- [ ] Hash-based detection (SHA-256, SHA-1, MD5)
- [ ] Perceptual scan for media similarity
- [ ] Progress reporting
- [ ] Cancellation support

#### 3.2 DuplicatesViewModel Enhancement
- [ ] Add hash algorithm selector
- [ ] Add duplicate detection strategy
- [ ] Add results pane actions
- [ ] Add reveal file command
- [ ] Add move/delete commands
- [ ] Add preview before apply

### PHASE 4: Security Tab Features
**Status**: PARTIAL (Hider exists, others missing)

#### 4.1 System Audit (NEW)
- [ ] Create SystemAuditService
- [ ] Create SystemAuditViewModel
- [ ] Create SystemAuditView
- [ ] Enumerate elevated users
- [ ] Scan critical ACLs
- [ ] Scan outbound traffic/ports
- [ ] Show Users & Groups
- [ ] Actions: Disable, Delete, Reset passwords
- [ ] Multi-confirm + audit log

#### 4.2 Startup Manager (NEW)
- [ ] Create StartupManagerService
- [ ] Create StartupManagerViewModel
- [ ] Create StartupManagerView
- [ ] Enumerate startup items
- [ ] Enumerate Task Scheduler tasks
- [ ] Actions: Enable, Disable, Delete
- [ ] Open file location command

### PHASE 5: Metadata Tab
**Status**: NOT STARTED

#### 5.1 Metadata Service
- [ ] Create MetadataService
- [ ] Scan folder files for metadata
- [ ] Support image, video, audio metadata
- [ ] Use ExifTool/FFprobe

#### 5.2 Metadata ViewModel/View
- [ ] Create MetadataViewModel
- [ ] Create MetadataView
- [ ] Display metadata in grid
- [ ] Support inline editing
- [ ] Batch write with preview

### PHASE 6: Cross-Cutting Concerns
**Status**: NOT STARTED

#### 6.1 UAC Elevation
- [ ] Create ElevationService
- [ ] Detect if elevated
- [ ] Request elevation when needed
- [ ] Multi-step confirmations

#### 6.2 Audit Logging
- [ ] Create AuditLogService
- [ ] Log all destructive operations
- [ ] Timestamp + user + action + files affected
- [ ] Export log command

#### 6.3 Dry-Run Mode
- [ ] Ensure ALL operations support dry-run
- [ ] Clear visual indicators
- [ ] Preview-before-apply pattern

## File Structure

### New Files to Create
```
PlatypusTools.Core/
  Services/
    - FileRenamerService.cs (ENHANCE EXISTING)
    - VideoConverterService.cs (NEW)
    - BootableUSBService.cs (NEW)
    - DuplicateFinderService.cs (NEW)
    - SystemAuditService.cs (NEW)
    - StartupManagerService.cs (NEW)
    - MetadataService.cs (NEW)
    - ElevationService.cs (NEW)
    - AuditLogService.cs (NEW)
  Models/
    - FilenameComponents.cs (NEW)
    - MetadataEntry.cs (NEW)
    - StartupItem.cs (NEW)
    - UserAccount.cs (NEW)

PlatypusTools.UI/
  ViewModels/
    - FileCleanerViewModel.cs (MAJOR REWRITE)
    - VideoConverterViewModel.cs (NEW)
    - BootableUSBViewModel.cs (NEW)
    - DuplicatesViewModel.cs (ENHANCE EXISTING)
    - SystemAuditViewModel.cs (NEW)
    - StartupManagerViewModel.cs (NEW)
    - MetadataViewModel.cs (NEW)
  Views/
    - FileCleanerView.xaml (COMPLETE REDESIGN)
    - VideoConverterView.xaml (NEW)
    - BootableUSBView.xaml (NEW)
    - DuplicatesView.xaml (ENHANCE EXISTING)
    - SystemAuditView.xaml (NEW)
    - StartupManagerView.xaml (NEW)
    - MetadataView.xaml (NEW)
```

### Files to Modify
```
- MainWindow.xaml (Update tab structure)
- MainWindowViewModel.cs (Wire new ViewModels)
- App.xaml.cs (Register new services)
```

## Testing Strategy
1. Create unit tests for each new service
2. Integration tests for file operations
3. Manual testing against Platypustools.ps1
4. Feature parity checklist verification

## Estimated Effort
- Phase 1 (File Cleaner): 8-12 hours (CRITICAL)
- Phase 2 (Media Conversion): 4-6 hours
- Phase 3 (Duplicates): 3-4 hours
- Phase 4 (Security): 6-8 hours
- Phase 5 (Metadata): 4-5 hours
- Phase 6 (Cross-cutting): 3-4 hours
**Total**: 28-39 hours

## Next Steps
1. ✅ Create this implementation plan
2. [ ] Implement enhanced FileRenamerService
3. [ ] Implement FileCleanerViewModel overhaul
4. [ ] Implement FileCleanerView complete redesign
5. [ ] Test File Cleaner against PS1 version
6. [ ] Continue with remaining phases

---

**Note**: User explicitly stated "No, the name changes aren't applying, you did not implement all features as I previously asked." This indicates the File Cleaner (Phase 1) is the HIGHEST PRIORITY and must be completed first with FULL feature parity before moving to other phases.
