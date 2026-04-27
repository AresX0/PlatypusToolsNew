# V5 Roadmap — Phase Changelog (append-only)

Every entry records files touched, git SHAs, and an undo recipe.
Use this together with `V5_ROADMAP.md`.

Format per entry:
```
## [Phase X.Y] <Title>
Started:  <ISO timestamp>  pre-SHA: <sha>
Completed: <ISO timestamp> post-SHA: <sha>

NEW FILES:
  - path/to/file.cs

MODIFIED FILES:
  - path/to/file.cs  (description of change)

DELETED FILES:
  - (none — additive policy)

UNDO:
  git revert <post-sha>
  # or file-level:
  rm new-files...
  git checkout <pre-sha> -- modified-files...
```

---

## Baseline
v4.0.2.5 published.  baseline SHA: `08a3bb6` (main, 2026-04-27).

Working-tree version after auto-bump: 4.0.2.6 (uncommitted).

---

<!-- Append phase entries below -->

---

## [Audit] Phase 1 baseline check — 2026-04-27 @ 08a3bb6
NEW FILES: (none)
MODIFIED FILES:
  - DOCS/V5_ROADMAP.md  (marked 1.1 and 1.3 as already-existing)

Findings:
  - 1.1 Command Palette: ALREADY IMPLEMENTED. `Services/CommandService.cs` + `Views/CommandPaletteWindow.xaml` + Ctrl+Shift+P hotkey wired in `MainWindow.xaml.cs`. UndoRedoService and Ctrl+1..9 also exist.
  - 1.2 Activity Log + Notification Center: MISSING. Will implement next.
  - 1.3 Auto-Update: BASE EXISTS in `Services/UpdateService.cs` (polls GH, downloads MSI/EXE with magic-byte verification). Delta patches deferred to a later phase.
  - 1.4 Per-Tab Reset/Export: MISSING.
  - 1.5 Crash Recovery: MISSING.

UNDO: revert DOCS/V5_ROADMAP.md edits (cosmetic only).

---

## [Phase 1.2] Activity Log + Notification Center — IN PROGRESS
Started: 2026-04-27  pre-SHA: 08a3bb6
Plan:
  - Add `PlatypusTools.Core/Services/ActivityLog/IActivityLogService.cs` + `ActivityLogService.cs` (in-memory ring + JSONL append at `%APPDATA%\PlatypusTools\activity.jsonl`).
  - Add `PlatypusTools.Core/Models/ActivityEntry.cs`.
  - Hook UI's existing toast helpers (`Helpers/ToastHelper.cs` if present, or inline `_notificationService.Show*`) so every notification also lands in the log. Locate the canonical toast entry point first; do NOT replace it — just add a side-channel call.
  - Add `Views/NotificationCenterView.xaml` + `ViewModels/NotificationCenterViewModel.cs` with filter (level/category) + clear + open-folder.
  - Add tab to MainWindow.xaml under Tools section, with TabVisibilityService property + Settings checkbox + TabCheckboxMapping entry.
  - Settings panel: retention (days), max in-memory entries.

UNDO (when implementation lands): list new files for deletion; modified files = ToastHelper, MainWindow.xaml, SettingsWindow.xaml/.cs, TabVisibilityService.cs, SettingsManager.cs.

---

## [Phase 1.2 COMPLETE] Notification Center — 2026-04-27  pre-SHA: 08a3bb6
Scope kept minimal: reuse existing `ToastNotificationService.NotificationHistory` rather than introducing a new ActivityLog service. Persistence + dedicated viewer added.

NEW FILES:
  - `PlatypusTools.UI/Views/NotificationCenterView.xaml`
  - `PlatypusTools.UI/Views/NotificationCenterView.xaml.cs`

MODIFIED FILES:
  - `PlatypusTools.UI/Services/ToastNotificationService.cs`  (added JSON persistence: `notification-history.json` at `%APPDATA%\PlatypusTools\`, max 500 entries, top 100 reloaded into `NotificationHistory` on startup)
  - `PlatypusTools.UI/Services/TabVisibilityService.cs`      (added `NotificationCenter` property keyed `"Tools.NotificationCenter"`)
  - `PlatypusTools.UI/MainWindow.xaml`                       (added Notification Center tab under Tools)
  - `PlatypusTools.UI/Views/SettingsWindow.xaml`             (added `TabNotificationCenter` checkbox)
  - `PlatypusTools.UI/Views/SettingsWindow.xaml.cs`          (added mapping `"TabNotificationCenter" → "Tools.NotificationCenter"`)

Build: `dotnet build PlatypusTools.UI -c Release` → 0 errors.

UNDO:
  rm PlatypusTools.UI/Views/NotificationCenterView.xaml*
  git checkout 08a3bb6 -- PlatypusTools.UI/Services/ToastNotificationService.cs \
                          PlatypusTools.UI/Services/TabVisibilityService.cs \
                          PlatypusTools.UI/MainWindow.xaml \
                          PlatypusTools.UI/Views/SettingsWindow.xaml \
                          PlatypusTools.UI/Views/SettingsWindow.xaml.cs

---

## [Phase 1.4 COMPLETE] Per-Tab Config Export / Import / Reset — 2026-04-27  pre-SHA: 08a3bb6
Opt-in pattern: tab VMs implement `ITabConfigProvider`, drop `<controls:TabActionMenuButton Provider="{Binding}"/>` in their header. Reference adopter = WallpaperRotator.

NEW FILES:
  - `PlatypusTools.UI/Services/TabConfig/ITabConfigProvider.cs`
  - `PlatypusTools.UI/Services/TabConfig/TabConfigService.cs`        (Export/Import/Reset + JSON helpers + `GetValue<T>` typed getter)
  - `PlatypusTools.UI/Controls/TabActionMenuButton.xaml`
  - `PlatypusTools.UI/Controls/TabActionMenuButton.xaml.cs`          (DependencyProperty `Provider`; falls back to `DataContext as ITabConfigProvider`)

MODIFIED FILES:
  - `PlatypusTools.UI/ViewModels/WallpaperRotatorViewModel.cs`       (implements `ITabConfigProvider` — exports 15 wallpaper config fields)
  - `PlatypusTools.UI/Views/WallpaperRotatorView.xaml`               (added xmlns:controls + `TabActionMenuButton` in header DockPanel)

Bundle format: `{ bundleVersion: "1", tab: "<TabKey>", exportedAt, payload: {…} }` — file extension `.platypuscfg`.
On import-tab-mismatch a confirmation dialog is shown; user can override.

Build: `dotnet build PlatypusTools.UI -c Release` → 0 errors.

UNDO:
  rm PlatypusTools.UI/Services/TabConfig/ITabConfigProvider.cs
  rm PlatypusTools.UI/Services/TabConfig/TabConfigService.cs
  rmdir PlatypusTools.UI/Services/TabConfig
  rm PlatypusTools.UI/Controls/TabActionMenuButton.xaml*
  git checkout 08a3bb6 -- PlatypusTools.UI/ViewModels/WallpaperRotatorViewModel.cs \
                          PlatypusTools.UI/Views/WallpaperRotatorView.xaml

---

## [Phase 1.5 COMPLETE] Crash Recovery Snapshots — 2026-04-27  pre-SHA: 08a3bb6
Infrastructure-only landing. Tabs adopt by implementing `IRecoverableState` and calling `SessionStateService.Instance.Register(this)`; future PRs add per-tab adoption.

NEW FILES:
  - `PlatypusTools.UI/Services/Recovery/IRecoverableState.cs`
  - `PlatypusTools.UI/Services/Recovery/SessionStateService.cs`     (singleton; sentinel `.dirty` file at `%APPDATA%\PlatypusTools\sessions\`; periodic 30s snapshots; clean-shutdown deletes everything)

MODIFIED FILES:
  - `PlatypusTools.UI/App.xaml.cs`                                  (`OnStartup`: Initialize + Start; `OnExit`: MarkCleanShutdown; after main window shows: `PromptAndRestoreIfNeeded`)

Behavior: on dirty restart, if any `<key>.json` snapshots exist, user is prompted "Recover previous session?" with the list of keys. Yes → calls each provider's `RestoreState`. No → snapshots cleared so prompt won't repeat.

Build: `dotnet build PlatypusTools.UI -c Release` → 0 errors.

UNDO:
  rm PlatypusTools.UI/Services/Recovery/IRecoverableState.cs
  rm PlatypusTools.UI/Services/Recovery/SessionStateService.cs
  rmdir PlatypusTools.UI/Services/Recovery
  git checkout 08a3bb6 -- PlatypusTools.UI/App.xaml.cs

---

## [PHASE 1 COMPLETE] — 2026-04-27
Roadmap items 1.1 (pre-existing), 1.2, 1.3 (pre-existing base), 1.4, 1.5 all landed.
Next session: begin Phase 2 (advanced search / fuzzy finder / global hotkeys expansion).
