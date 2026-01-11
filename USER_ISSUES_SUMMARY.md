# PlatypusTools Critical Issues Summary - January 10, 2026

## User-Reported Issues

### Issue 1: ✅ Startup Manager Works Now 
**Status:** FIXED in v1.1.3.2
- No crashes when opening Startup Manager tab
- IsEnabled binding fixed (OneWay mode)

### Issue 2: ❌ Column Resizing STILL NOT WORKING
**Status:** PARTIAL FIX - NEEDS INVESTIGATION
**Problem:** Despite removing `IsReadOnly="True"` from DataGrid level:
- User reports: "resizing of columns is not working"
- User can see resize cursor (↔)
- But dragging does nothing

**Analysis Needed:**
- All DataGrids have `CanUserResizeColumns="True"` ✅
- All columns have `CanUserResize="True"` ✅
- IsReadOnly moved to column level ✅
- But columns still won't resize in runtime

**Possible Causes:**
1. **WPF version/rendering issue** - .NET 10 DataGrid behavior
2. **Style inheritance** - Some parent style preventing resize
3. **Column Width="*" conflict** - Star-sized columns with MinWidth
4. **DataGrid virtualization** - Preventing header manipulation
5. **Event handler issue** - Something capturing mouse events

**Next Steps:**
- Need to test with simple standalone DataGrid
- Check if issue affects ALL tabs or just some
- Try removing MinWidth constraints
- Check for global event handlers

---

### Issue 3: ❌ **Folder Hider - Checkboxes Not Editable**
**Status:** CRITICAL BUG - KNOWN CAUSE

**Problem:**
- Cannot check/uncheck "Hidden" checkbox
- Cannot check/uncheck "Restrict to Administrators" checkbox  
- Cannot check/uncheck "Use EFS" checkbox

**Root Cause:** CheckBox columns use `Mode=OneWay` which prevents user input!

**Location:** [HiderView.xaml](PlatypusTools.Net/PlatypusTools.UI/Views/HiderView.xaml#L34-L36)

**Current Code:**
```xaml
<DataGridCheckBoxColumn Header="Hidden" Binding="{Binding IsHidden, Mode=OneWay}" ... />
<DataGridCheckBoxColumn Header="Restrict to Administrators" Binding="{Binding AclRestricted, Mode=OneWay}" ... />
<DataGridCheckBoxColumn Header="Use EFS" Binding="{Binding EfsEnabled, Mode=OneWay}" ... />
```

**Problem Analysis:**
- `IsHidden` is READ-ONLY (calculated property): `public bool IsHidden => HiderService.GetHiddenState(FolderPath);`
- `AclRestricted` and `EfsEnabled` are WRITABLE: they have setters in HiderRecordViewModel
- We fixed IsHidden to OneWay to prevent crash
- But we also changed AclRestricted and EfsEnabled to OneWay (MISTAKE!)

**Solution:**
```xaml
<!-- IsHidden: Keep OneWay (read-only display) -->
<DataGridCheckBoxColumn Header="Hidden" Binding="{Binding IsHidden, Mode=OneWay}" IsReadOnly="True" ... />

<!-- AclRestricted: Change to TwoWay (user can edit) -->
<DataGridCheckBoxColumn Header="Restrict to Administrators" Binding="{Binding AclRestricted}" ... />

<!-- EfsEnabled: Change to TwoWay (user can edit) -->
<DataGridCheckBoxColumn Header="Use EFS" Binding="{Binding EfsEnabled}" ... />
```

---

### Issue 4: ❌ **Folder Hider - Actions Menu Missing Options**
**Status:** NEEDS INVESTIGATION

**User Report:** "under actions the menu doesn't give me the options I want"

**Current Actions:** [HiderView.xaml](PlatypusTools.Net/PlatypusTools.UI/Views/HiderView.xaml#L39-L42)
- Edit button
- Remove button

**Questions:**
- What options does user expect?
- Context menu? Button menu? Dialog?
- Should there be: Hide/Unhide, Apply ACL, Apply EFS directly per row?

**Possible Solution:** Add context menu to Actions column with:
- Hide Folder
- Unhide Folder
- Apply ACL
- Apply EFS
- Edit
- Remove

---

### Issue 5: ❌ **Recent Cleaner - Browse Button Missing**
**Status:** CRITICAL BUG

**Problem:**
- User reports: "doesn't allow me to browse to directory to include"
- [RecentCleanupView.xaml](PlatypusTools.Net/PlatypusTools.UI/Views/RecentCleanupView.xaml) shows NO Browse button!
- Only has text entry for "Target directories (semicolon separated)"

**Current UI:**
```xaml
<TextBox Width="300" Text="{Binding TargetDirs, UpdateSourceTrigger=PropertyChanged}" />
<!-- NO BROWSE BUTTON! -->
```

**Solution:** Add Browse button with FolderBrowserDialog

---

### Issue 6: ❌ **Recent Cleaner - Scan Recent Button Does Nothing**
**Status:** NEEDS INVESTIGATION

**Problem:** 
- Button exists: `<Button Content="Scan Recent" Command="{Binding ScanCommand}" />`
- Command exists: `ScanCommand = new RelayCommand(async _ => await Scan());`
- But user says "does nothing"

**Possible Causes:**
1. Command not executing
2. TargetDirs empty (no input)
3. Async method failing silently
4. No error handling showing failures
5. Results.Clear() but no items added (scan finds nothing)

**Solution:** Add error handling, status messages, validation

---

### Issue 7: ❌ **Duplicates - Delete Selected Not Appearing After Selection**
**Status:** DESIGN/UX ISSUE

**Problem:**
- User scans for duplicates
- User clicks "Select Newest" or "Select Oldest"
- User expects "Delete Selected" button to become enabled
- But it might not be visible/enabled

**Current Code:**
```csharp
DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), 
    _ => Groups.Any(g => g.Files.Any(f => f.IsSelected)));
```

**The command IS checking for selected files correctly!**

**Possible Issues:**
1. **CanExecute not refreshing** - Need to call `RaiseCanExecuteChanged()` after Select methods
2. **Button visibility** - Button might be always visible but disabled (grayed out)
3. **User confusion** - Button exists but user doesn't see it's enabled

**Solution:** Add `RaiseCanExecuteChanged()` after selection methods:
```csharp
private void SelectNewest()
{
    foreach (var g in Groups)
    {
        var chosen = g.Files.OrderByDescending(f => File.GetLastWriteTimeUtc(f.Path)).FirstOrDefault();
        foreach (var f in g.Files) f.IsSelected = f == chosen;
    }
    ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged(); // ADD THIS
}
```

---

### Issue 8: ❌ **Duplicates - Selection Override Not Working**
**Status:** NEEDS CLARIFICATION

**User Report:** "If I click another button it should override what is selected too"

**Current Behavior:** Each Select button (Newest, Oldest, Largest, Smallest) completely replaces previous selection

**Expected Behavior (According to user):** ???

**Questions:**
- Does user want additive selection?
- Does user want selection to persist across button clicks?
- Or is the current behavior correct but not clearly communicated?

---

### Issue 9: ❌ **Video Converter - Missing File Selection Feature**
**Status:** FEATURE REQUEST

**User Request:** "I should be able to pick a source folder and then select files single or multiple to convert"

**Current Behavior:**
- User browses for source folder
- User browses for output folder
- Scan button scans ALL files in source folder
- User can select/deselect files in grid
- Convert button converts selected files

**Problem:** User might want different workflow:
1. Select source folder
2. See file list immediately (without separate Scan button)
3. Multi-select files
4. Convert selected

**Solution:** Auto-scan when source folder selected OR add file picker dialog

---

### Issue 10: ❌ **File Cleaner - Cannot Apply Changes**
**Status:** CRITICAL BUG

**Problem:** "I cannot apply changes"

**Current Command:**
```csharp
ApplyChangesCommand = new RelayCommand(_ => ApplyChanges(), 
    _ => PreviewItems.Any(p => p.IsSelected));
```

**Possible Issues:**
1. **No preview items** - User hasn't scanned/previewed first
2. **No items selected** - Command disabled because nothing selected
3. **ApplyChanges() failing silently** - Need to see actual implementation
4. **Button not visible** - UI layout issue

**Need to check:** ApplyChanges() method implementation

---

### Issue 11: ❓ **File Cleaner - Prefix Management Feature Request**
**Status:** FEATURE REQUEST/UNCLEAR

**User Request:**
"if I add multiple options under File cleaner, I want to always keep the prefix I want to apply, include the episode or episode and season numbers after the prefix, and sort alphabetically based on the file name that doesn't include the prefix or episode or season numbers"

**Translation:**
1. **Keep prefix persistent** - Once set, don't reset prefix when adding options
2. **Episode/Season numbering** - Add after prefix automatically
3. **Alphabetical sorting** - Sort files by name (excluding prefix/episode numbers) before numbering

**Example:**
```
Input files: zebra.mp4, apple.mp4, banana.mp4
Prefix: "MyShow"
Season: 1
Start Episode: 1

Expected output:
MyShow S01E01 apple.mp4
MyShow S01E02 banana.mp4
MyShow S01E03 zebra.mp4
```

**Current Behavior:** Need to check if this is already supported

---

## Priority Matrix

### CRITICAL (P0) - App Breaking
1. ✅ Folder Hider checkboxes not editable
2. ✅ Recent Cleaner missing Browse button
3. ✅ Recent Cleaner Scan button not working
4. ✅ File Cleaner Apply Changes not working
5. ✅ Duplicates Delete Selected not enabling

### HIGH (P1) - Major UX Issues
6. ❌ Column resizing still broken (despite fix)
7. ❓ Folder Hider Actions menu missing options

### MEDIUM (P2) - Feature Requests/Enhancements
8. ❓ Video Converter file selection workflow
9. ❓ File Cleaner prefix management persistence
10. ❓ Duplicates selection override behavior

---

## Next Actions

### Immediate Fixes (Can do now)
1. **Fix Folder Hider checkboxes** - Change AclRestricted and EfsEnabled back to default (TwoWay) binding
2. **Add Browse button to Recent Cleaner** - Simple UI addition
3. **Fix Duplicates DeleteSelected** - Add RaiseCanExecuteChanged() calls
4. **Add Recent Cleaner error handling** - Status messages and validation

### Needs Investigation
1. **Column resizing** - Runtime testing required
2. **File Cleaner Apply Changes** - Check method implementation
3. **Recent Cleaner Scan** - Debug why it's not working

### Needs User Clarification
1. **Folder Hider Actions menu** - What options are needed?
2. **Video Converter workflow** - What's the ideal workflow?
3. **File Cleaner prefix persistence** - Clarify exact behavior wanted
4. **Duplicates selection override** - What does "override" mean here?

---

## Technical Debt Identified

1. **Lack of validation messages** - Many features fail silently
2. **Inconsistent command CanExecute refresh** - Some places update, some don't
3. **Missing Browse buttons** - Should have browse buttons for all path inputs
4. **Poor error handling** - Try-catch blocks but no user-facing error messages
5. **No status indicators** - Hard to tell when operations complete/fail
