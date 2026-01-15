# Phase 5: End-to-End Testing Plan

**Status**: In Progress  
**Date Started**: 2026-01-14  
**Target Duration**: 2-3 hours  

## Overview
Comprehensive manual testing of the Audio Library system with real workflows to validate all components work together seamlessly.

---

## Test Environment Setup

### Prerequisites
- ✅ Build: 0 errors, 34 warnings (non-critical)
- ✅ All 15 unit tests passing
- ✅ UI components fully implemented
- ✅ ViewModel commands wired
- ✅ Data bindings configured

### Test Data
- Audio files location: To be determined during test execution
- Minimum files for testing: 5-10 audio files with varied metadata
- Formats to test: MP3, FLAC, M4A (if available)

---

## Test Scenarios

### 1. Application Startup & Initialization
**Objective**: Verify app starts correctly and initializes library system

- [ ] **1.1** Launch PlatypusTools application
- [ ] **1.2** Verify Audio Player view loads without errors
- [ ] **1.3** Verify Library Management section is visible
- [ ] **1.4** Check that library statistics display (Track/Artist/Album count)
- [ ] **1.5** Verify no console errors in debugger
- [ ] **1.6** Check that UI is responsive

**Expected Result**: App starts cleanly, Library tab displays with all controls visible

---

### 2. Library Scanning
**Objective**: Test directory scanning and metadata extraction

- [ ] **2.1** Click "🔍 Scan Library" button
- [ ] **2.2** Verify folder browser dialog opens
- [ ] **2.3** Select a music folder with audio files
- [ ] **2.4** Verify scanning begins (IsScanning = true)
- [ ] **2.5** Observe progress bar display (should be indeterminate initially)
- [ ] **2.6** Verify ScanProgress value updates (0-100)
- [ ] **2.7** Verify ScanStatus text updates with current activity
- [ ] **2.8** Wait for scan to complete
- [ ] **2.9** Verify IsScanning becomes false after completion
- [ ] **2.10** Verify statistics are updated (TrackCount, ArtistCount, AlbumCount)
- [ ] **2.11** Verify library tracks appear in DataGrid
- [ ] **2.12** Check console for any metadata extraction errors

**Expected Result**: Scan completes successfully with accurate metadata and statistics

**Performance Baseline**: Measure scan time for baseline comparison

---

### 3. Library Display & Organization
**Objective**: Verify library data displays correctly with different organization modes

#### 3.1 All Tracks Display
- [ ] **3.1.1** Verify "Organize Mode" selector is visible
- [ ] **3.1.2** Select "All Tracks" mode
- [ ] **3.1.3** Verify library groups list shows "All Tracks"
- [ ] **3.1.4** Verify DataGrid shows all scanned tracks
- [ ] **3.1.5** Check track information is correct (Title, Artist, Album, Duration)

#### 3.2 Artist Organization
- [ ] **3.2.1** Select "Artist" mode from organize selector
- [ ] **3.2.2** Verify groups list updates to show all artists
- [ ] **3.2.3** Click on an artist in groups list
- [ ] **3.2.4** Verify DataGrid filters to show only that artist's tracks
- [ ] **3.2.5** Verify track count matches artist's tracks

#### 3.3 Album Organization
- [ ] **3.3.1** Select "Album" mode
- [ ] **3.3.2** Verify groups list updates to show all albums
- [ ] **3.3.3** Click on an album
- [ ] **3.3.4** Verify DataGrid filters to show only that album's tracks

#### 3.4 Genre Organization
- [ ] **3.4.1** Select "Genre" mode
- [ ] **3.4.2** Verify groups list shows genres
- [ ] **3.4.3** Click on a genre
- [ ] **3.4.4** Verify DataGrid shows only tracks in that genre

#### 3.5 Folder Organization
- [ ] **3.5.1** Select "Folder" mode
- [ ] **3.5.2** Verify groups list shows folder structure
- [ ] **3.5.3** Click on a folder
- [ ] **3.5.4** Verify DataGrid shows only tracks from that folder

**Expected Result**: All organization modes work correctly and filter appropriately

---

### 4. Search Functionality
**Objective**: Verify search and filtering works with various queries

#### 4.1 Title Search
- [ ] **4.1.1** In search box, type a partial song title
- [ ] **4.1.2** Verify DataGrid updates in real-time (no manual submit needed)
- [ ] **4.1.3** Verify results match the search query
- [ ] **4.1.4** Clear search box and verify all tracks reappear

#### 4.2 Artist Search
- [ ] **4.2.1** In search box, type an artist name
- [ ] **4.2.2** Verify results show only tracks by that artist
- [ ] **4.2.3** Verify organization mode + search work together

#### 4.3 Album Search
- [ ] **4.3.1** In search box, type an album name
- [ ] **4.3.2** Verify results show only tracks from that album

#### 4.4 Genre Search
- [ ] **4.4.1** In search box, type a genre name
- [ ] **4.4.2** Verify results show only tracks in that genre

#### 4.5 Edge Cases
- [ ] **4.5.1** Search with empty query (should show all tracks)
- [ ] **4.5.2** Search with no matching results
- [ ] **4.5.3** Search with special characters
- [ ] **4.5.4** Case-insensitive search verification

**Expected Result**: Search filters results correctly in real-time with live updates

---

### 5. Cancel Operation
**Objective**: Test cancelling a long-running scan operation

- [ ] **5.1** Click "Scan Library" and select a folder
- [ ] **5.2** Once scan starts, verify Cancel button appears
- [ ] **5.3** Immediately click Cancel button
- [ ] **5.4** Verify scan stops (ScanStatus shows "Cancelled")
- [ ] **5.5** Verify UI remains responsive
- [ ] **5.6** Verify partial library data is preserved
- [ ] **5.7** Verify can scan again after cancellation

**Expected Result**: Cancel operation works smoothly without UI freezing

---

### 6. Persistence & Restart
**Objective**: Verify library data persists across application restarts

- [ ] **6.1** After scanning a library, note the statistics (track count, artist count)
- [ ] **6.2** Close the application
- [ ] **6.3** Wait 5 seconds
- [ ] **6.4** Relaunch the application
- [ ] **6.5** Verify library loads automatically on startup
- [ ] **6.6** Verify statistics match previous session
- [ ] **6.7** Verify all tracks and metadata are preserved
- [ ] **6.8** Verify search and organization still work

**Expected Result**: Library persists in JSON file and loads correctly on restart

---

### 7. Statistics Accuracy
**Objective**: Verify computed statistics are correct

- [ ] **7.1** After scanning, note the displayed statistics
- [ ] **7.2** Manually count unique artists in scanned tracks
- [ ] **7.3** Verify ArtistCount matches manual count
- [ ] **7.4** Manually count unique albums
- [ ] **7.5** Verify AlbumCount matches manual count
- [ ] **7.6** Manually count all tracks
- [ ] **7.7** Verify TrackCount matches manual count
- [ ] **7.8** Test statistics after organization changes

**Expected Result**: All statistics are accurate and update correctly

---

### 8. Error Handling
**Objective**: Verify graceful error handling

- [ ] **8.1** Try to scan a folder with no audio files
- [ ] **8.2** Verify appropriate message displays (not an error crash)
- [ ] **8.3** Try to scan a restricted/permission-denied folder
- [ ] **8.4** Verify error is handled gracefully
- [ ] **8.5** Check console for handled exceptions (should be logged, not thrown)
- [ ] **8.6** Verify app remains stable after errors

**Expected Result**: All errors handled gracefully with user-friendly messages

---

### 9. UI Responsiveness
**Objective**: Verify UI remains responsive during operations

- [ ] **9.1** During scan, try to interact with other controls
- [ ] **9.2** Try to change organization mode during scan
- [ ] **9.3** Try to search during scan
- [ ] **9.4** Verify no UI freezing or hangs
- [ ] **9.5** Verify all interactions are queued and processed correctly

**Expected Result**: UI remains responsive at all times

---

### 10. Performance Baseline
**Objective**: Establish performance metrics

- [ ] **10.1** Measure time to scan 100+ tracks
- [ ] **10.2** Measure time for search to return results
- [ ] **10.3** Measure time for organization mode changes
- [ ] **10.4** Check memory usage before/after scan
- [ ] **10.5** Document any performance concerns

**Expected Targets**:
- Scan: > 100 tracks/second
- Search: < 500ms
- Organization change: < 300ms
- Memory: Stable, no leaks

---

## Test Execution Log

### Session 1: Initial E2E Testing
- **Date**: 2026-01-14
- **Time Started**: [To be filled]
- **Tests Executed**: 
  - [ ] All 10 test scenarios
- **Issues Found**: 
  - [To be documented]
- **Time Completed**: [To be filled]
- **Duration**: [To be calculated]

---

## Summary & Results

### Overall Status
- [ ] All 10 test scenarios passed
- [ ] No critical issues found
- [ ] Performance targets met
- [ ] Ready for Phase 6 optimization

### Issues Found & Resolution Status
- [ ] [To be documented]

### Performance Metrics Collected
- Scan time: ____ seconds for ____ tracks
- Average track extraction: ____ ms
- Search response time: ____ ms
- Memory usage: ____ MB

### Recommendations for Phase 6
- [ ] [To be documented based on findings]

---

## Sign-Off
- Test Plan Status: Ready
- Build Status: ✅ 0 errors, 34 warnings
- Unit Tests: ✅ 15/15 passing
- Date Completed: [To be filled]

