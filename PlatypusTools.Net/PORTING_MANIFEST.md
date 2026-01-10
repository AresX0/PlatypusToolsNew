# PlatypusTools Porting Manifest (Detailed)

> Purpose: a comprehensive, itemized record of what has been ported from the original PowerShell scripts into the .NET 10 WPF application, what changes were made to archived scripts to allow automated parity testing, verification status, and exactly what remains to be implemented, verified, or hardened.

---

## Summary (short) ✅
- Goal: Port the functionality of original `*.ps1` PlatypusTools scripts into a .NET 10 WPF app while keeping the original scripts archived and non-interactive for parity tests.
- Status: **~72% Complete**. Core services and features for **FileCleaner + Batch Renamer (combined)**, **RecentCleaner**, **Hider**, **Duplicates**, **Media Conversion (Video/Image/ICO/Resize/Upscaler)**, **Disk Cleanup**, and **Privacy Cleaner** are implemented. UI reorganized into logical tabs: File Cleaner, Media Conversion (5 sub-tabs), Duplicates, Cleanup (3 sub-tabs), Security, Metadata. Enhanced functionality includes FFmpeg progress parsing, cancellation support, staging workflow, comprehensive batch renaming, ICO conversion, image resizing, system cleanup, and privacy data cleaning.

---

## How parity testing works (🔧 important)
- The parity test harness dot-sources archived PowerShell scripts in `ArchivedScripts/Originals` and invokes the original functions to capture outputs.
- C# ports are exercised with the same inputs and outputs are compared. Tests will fail fast if an archived script attempts interactive prompts.
- To make archived scripts testable: every archived script was made dot-sourceable and non-interactive by:
  - Moving `param()` blocks to the top.
  - Adding `[switch]$NonInteractive` or equivalent guards.
  - Replacing `Read-Host` / interactive UI with a `Tools/NonInteractive.ps1` helper to throw or fail in test mode rather than prompting.

---

## Ported Features (Detailed)

### 1) File Cleaner (Combined with Batch Renamer) ✅
- What was ported:
  - **Pattern Cleaner**: File scanning logic, pattern matching and deletion strategies with backup preview.
  - **Batch Renamer**: Complete file renaming suite with prefix management, season/episode numbering, token removal, name normalization.
- Key features:
  - Pattern-based file scanning (semicolon-separated patterns)
  - Dry run preview mode
  - Backup path with preview
  - Prefix operations: Add, Remove, Replace with normalization
  - Season/Episode numbering (S01E001 format with configurable padding)
  - Renumber alphabetically
  - Token cleaning (720p, 1080p, 4k, HD, custom tokens)
  - Name normalization (spaces, dashes, underscores conversions)
  - Preview changes before applying
  - Undo last changes
  - Select all/none with status tracking
- Key files:
  - `ArchivedScripts/FileCleaner.ps1` (archived, modified to be non-interactive)
  - `PlatypusTools.Core/Services/FileCleanerService.cs`
  - `PlatypusTools.Core/Services/FileRenamerService.cs` ✅ NEW
  - `PlatypusTools.Core/Models/RenameOperation.cs` ✅ NEW
  - `PlatypusTools.UI/Views/FileCleanerView.xaml` ✅ UPDATED (now contains both tabs)
  - `PlatypusTools.UI/ViewModels/FileCleanerViewModel.cs` ✅ UPDATED
  - `PlatypusTools.UI/ViewModels/FileRenamerViewModel.cs` ✅ NEW
- Tests:
  - Unit tests for FileCleanerService (pattern matching, safe-delete)
  - `FileRenamerServiceTests.cs` ✅ NEW (14 tests: prefix, episode, cleaning, normalization, complete workflow)
- Remaining: Additional file type filtering enhancements

### 2) Recent Shortcuts Cleaner ✅
- What was ported:
  - Logic to remove recent/JumpList/shortcut entries and the safe deletion modes.
  - Tab integrated into Cleanup section with UI for directory exclusions.
- Key files:
  - `ArchivedScripts/RecentCleaner.ps1` (archived + non-interactive)
  - `PlatypusTools.Core/Services/RecentCleanerService.cs`
  - `PlatypusTools.UI` UI to integrate RecentCleaner functions
- Tests: parity tests against archived script behavior present and passing.
- Remaining:
  - ⏳ Directory exclusion management (Add/Remove)
  - ⏳ Scheduling UI (Frequency, Time, Days)
  - ⏳ Show Preview, Export CSV, Undo Last buttons
  - ⏳ Run Now and Schedule Task integration

### 3) Hider (Folder hide / ACL modification)
- What was ported:
  - `Set-Hidden`, `Get-HiddenState`, `Clear-Hidden`, password storage and validation.
  - Password records use PBKDF2-HMAC-SHA256; credential migration moves encrypted blobs into Windows Credential Manager via P/Invoke.
- Key files:
  - `ArchivedScripts/Hider.ps1` (modified to be dot-source safe)
  - `PlatypusTools.Core/Services/HiderService.cs`
  - `PlatypusTools.Core/Models/PasswordRecord.cs`, `HiderRecord.cs`
  - `PlatypusTools.UI/Views/HiderView.xaml`, `HiderEditWindow.xaml`
  - `PlatypusTools.UI/ViewModels/HiderViewModel.cs`, `HiderEditViewModel.cs`
- Tests:
  - `HiderServiceTests`, `HiderParityTests`, credential migration tests.
- Remaining: thorough E2E tests that create/restore ACLs on a variety of Windows environments and EFS scenarios.

### 4) Duplicates (core + UI) — Recent work (preview & staging) ✅
- What was ported & implemented:
  - `DuplicatesScanner` (SHA256-based scanning & grouping), group selection strategies (SelectNewest, SelectOldest, SelectLargest, SelectSmallest, KeepOnePerGroup).
  - UI: `DuplicatesView.xaml` and the new `DuplicateGroupViewModel` / `DuplicateFileViewModel` to show group/file rows and per-file controls.
  - Per-file commands: **Preview**, **Open**, **Folder**, **Rename**, **Stage**.
  - Preview window: `Views/PreviewWindow.xaml` and `PreviewWindow.xaml.cs` (image display for common image types; fallback icon rendering for other file types).
  - Staging: `DuplicatesViewModel.StageFileToStaging()` (copies file to a staging directory under %TEMP%\PlatypusTools\DuplicatesStaging) and `StageFile()` shows an optional dialog to open staging folder.
- Key files:
  - `PlatypusTools.Core/Services/DuplicatesScanner.cs`
  - `PlatypusTools.UI/Views/DuplicatesView.xaml`
  - `PlatypusTools.UI/ViewModels/DuplicatesViewModel.cs`
  - `PlatypusTools.UI/Views/PreviewWindow.xaml` + `PreviewWindow.xaml.cs`
- Tests:
  - `DuplicatesScannerTests` (scanner correctness)
  - `DuplicatesScannerParityTests` (hash parity with original PS Get-FastFileHash)
  - `DuplicatesViewModelTests` (SelectNewest and a new `StageFileToStaging_CopiesFile` test)
- Remaining / planned:
  - Full move-to-staging/restore/commit workflow UI (stage list, restore, commit purge) — design required.
  - Thumbnail caching/performance improvements for large galleries.
  - Video preview support (thumbnail + playback) — low-priority for now.
  - End-to-end parity test for the complete duplicates workflow (scan → select strategy → staging → delete) — planned.

### 5) Media Conversion ✅
- What was ported:
  - **Video Combiner**: `FFmpegService` (detection + process wrapper) and `VideoCombinerService` (creates concat list and invokes ffmpeg to combine videos).
  - **Image Upscaler**: `video2x` integration with progress tracking, scale selection, and cancellation.
  - **ICO Converter**: Complete service for converting images to ICO format with multiple size options and format conversion between PNG, JPG, BMP, GIF, TIFF. ✅ NEW
  - **Image Resizer**: High-quality image resizing service with aspect ratio maintenance, quality control, and batch processing. ✅ NEW
- Key features:
  - **Video**: FFmpeg progress parsing, file list management, cancellation support
  - **Upscaler**: video2x integration, 2x/3x/4x scaling, progress logging
  - **ICO Converter**: Size selection (16-256px), format conversion, batch processing, overwrite control, progress reporting
  - **Image Resizer**: Max width/height constraints, maintain aspect ratio, JPEG quality slider, format conversion, no upscaling (only downscale), high-quality bicubic interpolation
- Key files:
  - `PlatypusTools.Core/Services/FFmpegService.cs`
  - `PlatypusTools.Core/Services/VideoCombinerService.cs`
  - `PlatypusTools.Core/Services/UpscalerService.cs`
  - `PlatypusTools.Core/Services/IconConverterService.cs` ✅ NEW (280 lines)
  - `PlatypusTools.Core/Services/ImageResizerService.cs` ✅ NEW (282 lines)
  - `PlatypusTools.UI/Views/VideoCombinerView.xaml`
  - `PlatypusTools.UI/Views/UpscalerView.xaml`
  - `PlatypusTools.UI/Views/IconConverterView.xaml` ✅ NEW
  - `PlatypusTools.UI/Views/ImageResizerView.xaml` ✅ NEW
  - `PlatypusTools.UI/ViewModels/VideoCombinerViewModel.cs`
  - `PlatypusTools.UI/ViewModels/UpscalerViewModel.cs`
  - `PlatypusTools.UI/ViewModels/IconConverterViewModel.cs` ✅ NEW (388 lines)
  - `PlatypusTools.UI/ViewModels/ImageResizerViewModel.cs` ✅ NEW (324 lines)
- Tests:
  - `tests/PlatypusTools.Core.Tests/VideoCombinerTests.cs`
  - `tests/PlatypusTools.Core.Tests/UpscalerIntegrationTests.cs` (with fake video2x shim)
- Remaining:
  - Unit tests for IconConverterService and ImageResizerService
  - Bootable USB Creator (requires admin elevation and low-level disk operations)

### 6) Disk Cleanup ✅ NEW
- What was ported:
  - Complete disk cleanup service with 9 cleanup categories: Windows Temp Files, User Temp Files, Prefetch Files, Recycle Bin, Downloads >30 Days, Windows Update Cache, Thumbnail Cache, Windows Error Reports, Old Log Files.
  - Analyze/Clean workflow with dry run support and detailed reporting.
- Key features:
  - Category-based cleanup with checkboxes for each category
  - Analyze phase: Scans and estimates space to be freed
  - Clean phase: Deletes files with optional dry run mode
  - Progress reporting with detailed status messages
  - Error collection and reporting
  - Cancellation support
  - Results grid showing Category/Files/Size/Path
- Key files:
  - `PlatypusTools.Core/Services/DiskCleanupService.cs` (397 lines)
  - `PlatypusTools.UI/Views/DiskCleanupView.xaml`
  - `PlatypusTools.UI/ViewModels/DiskCleanupViewModel.cs` (292 lines)
- Tests:
  - Unit tests pending

### 7) Privacy Cleaner ✅ NEW
- What was ported:
  - Comprehensive privacy data cleanup service covering browsers, cloud services, Windows identity data, and application caches.
  - 15 cleanup categories across 4 groups: Browsers (Chrome/Edge/Firefox/Brave), Cloud Services (OneDrive/Google/Dropbox/iCloud), Windows Identity (Recent Docs/Jump Lists/Explorer History/Clipboard), Applications (Office/Adobe/Media Players).
  - Analyze/Clean workflow with dry run support and detailed reporting.
- Key features:
  - Browser data cleanup: cookies, cache, history, form data, downloads for Chrome, Edge, Firefox, Brave
  - Cloud service cleanup: cached credentials and temporary sync data for OneDrive, Google Drive, Dropbox, iCloud
  - Windows identity cleanup: Recent documents, taskbar jump lists, Explorer recent/frequent, clipboard history
  - Application cleanup: Office recent files, Adobe recent files, VLC/Windows Media Player history
  - Grouped checkbox UI for easy category selection
  - Analyze phase: Scans and counts items/sizes
  - Clean phase: Deletes data with optional dry run mode
  - Warning message styling for sensitive operations
  - Progress reporting with detailed status
  - Error collection and reporting
  - Cancellation support
- Key files:
  - `PlatypusTools.Core/Services/PrivacyCleanerService.cs` (560+ lines)
  - `PlatypusTools.UI/Views/PrivacyCleanerView.xaml`
  - `PlatypusTools.UI/ViewModels/PrivacyCleanerViewModel.cs` (288 lines)
- Tests:
  - Unit tests pending

---
---

## Important Implementation Notes / Decisions (🔎)
- Passwords & Credential Storage:
  - Implemented a PBKDF2-HMAC-SHA256-based `PasswordRecord` to be deterministic and safe across runtimes.
  - Replaced `Rfc2898DeriveBytes` deprecated direct constructor usage with an explicit implementation to avoid cross-runtime discrepancies.
  - Migrated encrypted password blobs from config files into Windows Credential Manager using P/Invoke (`CredWrite` / `CredRead`) to avoid an external `CredentialManagement` dependency and NU1701 warnings.
- Archived scripts:
  - All archived scripts were safe-guarded for dot-sourcing and non-interactivity. If a script *would* prompt, `Tools/NonInteractive.ps1` will cause tests to fail rather than blocking (explicit design requirement).
- UI patterns:
  - Commands are `RelayCommand` and views use `BindableBase` (simple MVVM). UI uses Windows Forms `FolderBrowserDialog` for folder selection to preserve existing behavior.

---

## Files Modified or Created (selected, exhaustive for core parts)
- Archive edits:
  - `ArchivedScripts/*` — multiple scripts: moved `param()` blocks to top; added `[switch]$NonInteractive`; removed accidental Read-Host usage; added `Tools/NonInteractive.ps1`.
- Core services & models:
  - `PlatypusTools.Core/Services/HiderService.cs`
  - `PlatypusTools.Core/Services/SecurityService.cs`
  - `PlatypusTools.Core/Services/DuplicatesScanner.cs`
  - `PlatypusTools.Core/Models/PasswordRecord.cs`
  - `PlatypusTools.Core/Models/HiderRecord.cs`
- UI & ViewModels:
  - `PlatypusTools.UI/Views/HiderView.xaml`
  - `PlatypusTools.UI/Views/HiderEditWindow.xaml`
  - `PlatypusTools.UI/Views/DuplicatesView.xaml` (added per-file Preview & Stage controls)
  - `PlatypusTools.UI/Views/PreviewWindow.xaml` (NEW)
  - `PlatypusTools.UI/Views/PreviewWindow.xaml.cs` (NEW)
  - `PlatypusTools.UI/ViewModels/DuplicatesViewModel.cs` (Preview, Stage, StageFileToStaging)
  - `PlatypusTools.UI/ViewModels/HiderViewModel.cs` (existing)
- Tests:
  - `tests/PlatypusTools.Core.Tests/HiderServiceTests.cs`
  - `tests/PlatypusTools.Core.Tests/DuplicatesScannerTests.cs`
  - `tests/PlatypusTools.UI.Tests/DuplicatesViewModelTests.cs` (added `StageFileToStaging_CopiesFile`)

---

## How to Run the Tests Locally (⚙️)
1. Open a Developer PowerShell for VS (or ensure `dotnet` is on PATH). This project targets .NET 10.
2. From repo root run:

   - dotnet test --filter Category!=Integration (to run unit tests)
   - dotnet test (to run full test suite)

3. Parity tests may require PowerShell & the archived scripts to be dot-sourceable; ensure the test environment is running on Windows and that PowerShell is available.

Notes: If tests hang, check for any unexpected message boxes from UI code running in test context. Tests are designed to avoid UI prompts; if a prompt appears, it indicates an archived script is still interactive or a UI method was invoked that shows a dialog.

---

## Known Issues / Warnings (⚠️)
- `dotnet` may not be present on all development machines by default—make sure the correct SDK (NET 10) is installed and PATH is set.
- Thumbnail/preview performance: current implementation uses `BitmapImage` with `OnLoad` caching. For large images or many concurrent previews, a background thumbnailer + cache is recommended.
- Video preview: not yet implemented (placeholder fallback to icon). Implementing video thumbnails and playback requires adding a media player control and possibly transient temporary thumbnail generation.
- Some archived scripts rely on very old PowerShell behavior or COM objects; we added defensive fallbacks but cross-machine behavior should be validated by CI.

---

## Prioritized TODOs (ordered with clear next steps) 📋
1. (High) Finish full Duplicate workflow parity tests (scan -> strategy -> stage -> delete) and make them non-interactive and deterministic. (Next: design test harness for staging + restore flow). ✅ in planning
2. (High) Implement staging UI (list staged files, allow restore and commit purge). (Next: Add `StagingViewModel` and `StagingView.xaml` and wire to `DuplicatesView`).
3. (Medium) Add thumbnail caching + background loader for `PreviewWindow`. (Next: add a thumbnail cache service + async load and cancellation tokens.)
4. (Medium) Add video preview support (thumbnail + playback) in `PreviewWindow`. (Next: evaluate `MediaElement` or add a lightweight video player dependency.)
5. (Low) Extend Hider E2E tests for ACL / EFS edge cases and document expected behavior on domain-joined machines.
6. (Low) Final packaging & build script adjustments: ensure that archived scripts are included in release artifacts and that CI run performs parity tests before creating packages.

---

## Developer Notes & Conventions (💡)
- Non-interactivity is enforced during tests — do not add Read-Host or message boxes inside code paths executed by parity tests.
- When adding UI features that need user confirmation, prefer to provide ways for tests to bypass UI (e.g., extract core logic into a public/virtual method that tests can call instead of the UI method).
- Use `System.IO.Path` and `Path.GetTempPath()` consistently for temporary artifacts.
- When dealing with credentials, prefer P/Invoke `CredWrite`/`CredRead` so we avoid external nuget packages that may not be compatible across .NET versions.

---

## Contact & Next Steps (short) ✉️
- Current next steps: implement full staging UI & workflow, add thumbnail caching, add parade of E2E parity tests for duplicates (scan → stage → commit), and finalize manifest for release.
- If you want, I can now:
  - Expand the manifest with file-by-file diffs and code snippets for each major change (very verbose), or
  - Start implementing the staging UI and the E2E parity tests for duplicates immediately.

---

(Manifest generated: $(date))
