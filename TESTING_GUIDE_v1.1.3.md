# PlatypusTools v1.1.3 - Testing Guide

## Overview
This guide explains how to test PlatypusTools v1.1.3 comprehensively, following the "headless first, then GUI" approach.

## Phase 1: Headless Testing ✅ **COMPLETED**

### Test Environment Setup
```powershell
cd C:\Projects\Platypustools\PlatypusTools.Net
& "C:\Program Files\dotnet\dotnet.exe" build PlatypusTools.Tests.Headless\PlatypusTools.Tests.Headless.csproj
```

### Running Headless Tests
```powershell
& "C:\Program Files\dotnet\dotnet.exe" run --project PlatypusTools.Tests.Headless\PlatypusTools.Tests.Headless.csproj
```

### Expected Results
```
=== PlatypusTools Headless Tests ===
Logs will be written to: C:\Users\[username]\AppData\Roaming\PlatypusTools\Logs

--- Testing Startup Manager ---
StartupManagerService created successfully
GetStartupItems() returned [N] items
✓ Startup Manager test PASSED

--- Testing Folder Hider ---
Test folder: [temp path]
Test folder created
HiderRecord created: [temp path]
Test folder deleted
✓ Folder Hider test PASSED

--- Testing ExifTool Detection ---
ExifTool path: [path or "exiftool"]
ExifTool available: [True/False]
✓ ExifTool test PASSED (or ⚠ ExifTool not found)
```

### Reviewing Logs
```powershell
# View most recent log file
Get-ChildItem C:\Users\$env:USERNAME\AppData\Roaming\PlatypusTools\Logs -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content | Out-GridView

# Or open in notepad
notepad (Get-ChildItem C:\Users\$env:USERNAME\AppData\Roaming\PlatypusTools\Logs -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
```

## Phase 2: GUI Testing ⏳ **REQUIRED**

### Build Application
```powershell
cd C:\Projects\Platypustools\PlatypusTools.Net
& "C:\Program Files\dotnet\dotnet.exe" build PlatypusTools.UI\PlatypusTools.UI.csproj --configuration Release
```

### Launch Application
```powershell
& "C:\Projects\Platypustools\PlatypusTools.Net\PlatypusTools.UI\bin\Release\net10.0\PlatypusTools.UI.exe"
```

### Test Cases

#### 1. Startup Manager Tab 🔍

**Test 1.1: Basic Functionality**
1. Navigate to "Startup Manager" tab
2. Click "Refresh" button
3. ✅ **Expected**: List populates with startup items
4. ✅ **Expected**: No crashes or errors
5. ✅ **Expected**: Status message shows item count

**Test 1.2: Column Sorting**
1. Click on each column header
2. ✅ **Expected**: Items sort ascending/descending
3. ✅ **Expected**: No visual glitches

**Test 1.3: CSV Export**
1. Click "Export to CSV" button
2. Choose save location
3. ✅ **Expected**: CSV file created successfully
4. ✅ **Expected**: All columns present
5. ✅ **Expected**: Data matches display

**Test 1.4: Error Handling**
1. Review logs after refresh: `%AppData%\PlatypusTools\Logs`
2. ✅ **Expected**: No errors in logs
3. ✅ **Expected**: All CSV lines processed successfully

#### 2. System Audit Tab - Inbound Traffic 🌐

**Test 2.1: New Button Visibility**
1. Navigate to "System Audit" tab
2. ✅ **Expected**: "Scan Inbound Traffic" button visible
3. ✅ **Expected**: Button next to "Scan Outbound Traffic"
4. ✅ **Expected**: Background color: LightGreen

**Test 2.2: Inbound Traffic Scan**
1. Click "Scan Inbound Traffic" button
2. ✅ **Expected**: Status message: "Scanning inbound traffic..."
3. ✅ **Expected**: Results appear in audit items list
4. ✅ **Expected**: Category: "Inbound Traffic"
5. ✅ **Expected**: Shows listening ports and processes
6. ✅ **Expected**: Status message updates with count

**Test 2.3: Compare with Outbound**
1. Click "Scan Outbound Traffic" button
2. Click "Scan Inbound Traffic" button
3. ✅ **Expected**: Both results visible
4. ✅ **Expected**: Different categories
5. ✅ **Expected**: Filter dropdown works

**Test 2.4: Firewall Rules**
1. Scan inbound traffic
2. Look for "Firewall Inbound Rules" item
3. ✅ **Expected**: Shows allow/block rule counts
4. ✅ **Expected**: Info severity

#### 3. Metadata Editor Tab - ExifTool 🛠️

**Test 3.1: No ExifTool Installed**
1. Remove ExifTool from all search paths
2. Navigate to "Metadata Editor" tab
3. Select a video/image file
4. ✅ **Expected**: Warning message about ExifTool not found
5. ✅ **Expected**: Option to configure path

**Test 3.2: ExifTool with Custom Path**
1. Create file: `%AppData%\PlatypusTools\exiftool_path.txt`
2. Add path to ExifTool: `C:\Tools\exiftool.exe` (or actual location)
3. Restart application
4. Navigate to "Metadata Editor" tab
5. Select a file
6. ✅ **Expected**: Metadata loads successfully
7. ✅ **Expected**: No errors about missing ExifTool

**Test 3.3: ExifTool in Program Files**
1. Install ExifTool to: `C:\Program Files\PlatypusTools\Tools\exiftool.exe`
2. Remove custom path config
3. Restart application
4. Navigate to "Metadata Editor" tab
5. ✅ **Expected**: ExifTool detected automatically
6. ✅ **Expected**: Metadata operations work

**Test 3.4: Edit Metadata**
1. Select a video file
2. Modify metadata fields
3. Click "Save" or "Apply"
4. ✅ **Expected**: Changes saved successfully
5. ✅ **Expected**: Reload file shows updated metadata

#### 4. Folder Hider Tab 🗂️

**Test 4.1: Add Folder to Hide List**
1. Navigate to "Folder Hider" tab
2. Click "Add Folder" button
3. Select a test folder
4. ✅ **Expected**: Folder appears in list
5. ✅ **Expected**: No crashes
6. ✅ **Expected**: Status updates

**Test 4.2: Check Logs**
1. After adding folder, review logs
2. ✅ **Expected**: Log shows:
   - `[HiderService.LoadConfig] Loading config from: [path]`
   - `[HiderService.AddRecord] Adding record for: [folder]`
   - `[HiderService.SaveConfig] Saving config to: [path]`
3. ✅ **Expected**: No errors

**Test 4.3: Hide Folder**
1. Select folder in list
2. Click "Hide" button
3. ✅ **Expected**: Folder becomes hidden in File Explorer
4. ✅ **Expected**: Attributes include Hidden+System

**Test 4.4: Unhide Folder**
1. Select hidden folder
2. Click "Unhide" button
3. ✅ **Expected**: Folder becomes visible
4. ✅ **Expected**: Attributes normal

**Test 4.5: Remove from List**
1. Select folder
2. Click "Remove" button
3. ✅ **Expected**: Folder removed from list
4. ✅ **Expected**: Config saved successfully

#### 5. Regression Testing 🔄

**Test 5.1: System Audit - Full Audit**
1. Click "Full System Audit" button
2. ✅ **Expected**: All audit categories run
3. ✅ **Expected**: Results populate
4. ✅ **Expected**: No crashes

**Test 5.2: System Audit - Elevated Users**
1. Click "Scan Elevated Users" button
2. ✅ **Expected**: Shows administrator accounts
3. ✅ **Expected**: CSV export works

**Test 5.3: Disk Space Analyzer**
1. Navigate to "Disk Space Analyzer" tab
2. Select a drive
3. Click "Analyze"
4. ✅ **Expected**: Results display correctly
5. ✅ **Expected**: No errors

**Test 5.4: Scheduled Tasks**
1. Navigate to "Scheduled Tasks" tab
2. Click "Refresh"
3. ✅ **Expected**: Tasks load
4. ✅ **Expected**: Columns resizable
5. ✅ **Expected**: CSV export works

**Test 5.5: Recent Items Cleaner**
1. Navigate to "Recent Items Cleaner" tab
2. Scan for recent items
3. ✅ **Expected**: Items found
4. ✅ **Expected**: Clean operation works

### Stress Testing

**Stress Test 1: Rapid Button Clicking**
1. Click "Refresh" on Startup Manager rapidly 10 times
2. ✅ **Expected**: No crashes
3. ✅ **Expected**: Proper async handling (no "Already running" errors)

**Stress Test 2: Large File Metadata**
1. Select 100+ video files
2. Load metadata
3. ✅ **Expected**: Reasonable performance
4. ✅ **Expected**: No memory leaks

**Stress Test 3: Many Folders Hidden**
1. Add 50+ folders to hide list
2. ✅ **Expected**: UI remains responsive
3. ✅ **Expected**: Config saves correctly

### Error Recovery Testing

**Error Test 1: Invalid ExifTool Path**
1. Set custom path to non-existent file
2. Try to edit metadata
3. ✅ **Expected**: Clear error message
4. ✅ **Expected**: Option to reconfigure

**Error Test 2: Permission Denied**
1. Try to hide system folder without admin rights
2. ✅ **Expected**: Permission error shown
3. ✅ **Expected**: Log contains details

**Error Test 3: Malformed CSV**
1. Edit scheduled tasks output manually (if possible)
2. Trigger CSV parsing
3. ✅ **Expected**: Graceful error handling
4. ✅ **Expected**: Log shows specific line causing issue

## Phase 3: Performance Testing

### Performance Test 1: Startup Time
```powershell
Measure-Command { & "C:\Projects\Platypustools\PlatypusTools.Net\PlatypusTools.UI\bin\Release\net10.0\PlatypusTools.UI.exe" }
```
✅ **Expected**: < 3 seconds to main window

### Performance Test 2: Memory Usage
1. Launch Task Manager
2. Open PlatypusTools
3. Run all audits
4. ✅ **Expected**: Memory < 500MB
5. ✅ **Expected**: No memory leaks over time

### Performance Test 3: Log File Size
```powershell
Get-ChildItem C:\Users\$env:USERNAME\AppData\Roaming\PlatypusTools\Logs -Recurse | Measure-Object -Property Length -Sum
```
✅ **Expected**: < 10MB for typical usage session

## Phase 4: Log Analysis

### Critical Log Patterns to Check

**Success Patterns** ✅
```
[StartupManagerService.GetStartupItems] Total startup items found: [N]
[HiderService.SaveConfig] Config file written successfully
[MetadataService.GetExifToolPath] ExifTool path: [path]
```

**Warning Patterns** ⚠️
```
[StartupManagerService] schtasks exited with code [non-zero]
[HiderService] Password protection failed, using fallback
[MetadataService] ExifTool not found at [path]
```

**Error Patterns** ❌
```
ERROR: [Exception message]
Exception: [Stack trace]
LogError: [Context]
```

### Log Analysis Script
```powershell
# Find all errors in logs
$logDir = "$env:APPDATA\PlatypusTools\Logs"
Get-ChildItem $logDir -Recurse -Filter "*.log" | ForEach-Object {
    $errors = Select-String -Path $_.FullName -Pattern "ERROR|Exception|LogError"
    if ($errors) {
        Write-Host "`n=== Errors in $($_.Name) ===" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host $_.Line }
    }
}
```

## Test Result Documentation

### Template
```markdown
## Test Session: [Date/Time]

### Headless Tests
- [x] Startup Manager: PASS
- [x] Folder Hider: PASS
- [x] ExifTool Detection: PASS/FAIL

### GUI Tests
- [ ] Startup Manager Tab: PASS/FAIL - [Notes]
- [ ] System Audit Inbound: PASS/FAIL - [Notes]
- [ ] Metadata Editor: PASS/FAIL - [Notes]
- [ ] Folder Hider: PASS/FAIL - [Notes]
- [ ] Regression Tests: PASS/FAIL - [Notes]

### Issues Found
1. [Issue description]
   - Severity: Critical/High/Medium/Low
   - Log excerpt: [relevant log lines]
   - Reproduction steps: [steps]

### Performance Metrics
- Startup time: [N] seconds
- Memory usage: [N] MB
- Log file size: [N] KB

### Recommendations
- [ ] Ready for release
- [ ] Needs fixes: [list issues]
- [ ] Additional testing needed: [specify areas]
```

## Quick Reference

### Key Directories
- **Application**: `C:\Projects\Platypustools\PlatypusTools.Net\PlatypusTools.UI\bin\Release\net10.0\`
- **Logs**: `%AppData%\PlatypusTools\Logs\`
- **Config**: `%AppData%\PlatypusTools\`
- **ExifTool Config**: `%AppData%\PlatypusTools\exiftool_path.txt`

### Key Commands
```powershell
# Build Release
& "C:\Program Files\dotnet\dotnet.exe" build -c Release

# Run Headless Tests
& "C:\Program Files\dotnet\dotnet.exe" run --project PlatypusTools.Tests.Headless\PlatypusTools.Tests.Headless.csproj

# View Latest Log
notepad (Get-ChildItem $env:APPDATA\PlatypusTools\Logs -Recurse | Sort LastWriteTime -Desc | Select -First 1).FullName

# Clear Logs
Remove-Item $env:APPDATA\PlatypusTools\Logs\* -Recurse -Force
```

## Reporting Issues

When reporting issues, include:
1. **Steps to reproduce**
2. **Expected behavior**
3. **Actual behavior**
4. **Log excerpt** (last 50 lines before crash)
5. **Screenshots** (if applicable)
6. **System info**: Windows version, .NET version

### Example Issue Report
```markdown
**Issue**: Startup Manager crashes on refresh

**Steps**:
1. Launch application
2. Navigate to Startup Manager tab
3. Click "Refresh" button

**Expected**: List populates with startup items

**Actual**: Application freezes then crashes

**Log Excerpt**:
[2026-01-10 18:02:42.878] [StartupManagerService.GetScheduledStartupTasks:199] Parsed 28 CSV fields
[2026-01-10 18:02:42.878] [StartupManagerService.GetScheduledStartupTasks:203] Task name: CRAZYHORSE
ERROR: Index out of range exception at line 245

**System**:
- Windows 11 Pro 23H2
- .NET 10.0.101
- PlatypusTools v1.1.3
```

---

**Testing Status**: Headless tests ✅ PASSED | GUI tests ⏳ PENDING
**Next Action**: Run GUI tests following this guide
**Support**: Check logs in `%AppData%\PlatypusTools\Logs` for troubleshooting
