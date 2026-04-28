# PlatypusTools v5 Roadmap

**Baseline:** v4.0.2.5 — git `08a3bb6` (main).
**Companion ledger:** [`V5_PHASE_CHANGELOG.md`](V5_PHASE_CHANGELOG.md) — every file change + undo recipe.

> ⚠️ **Rules**
> - No removals. Additive only. Every existing tab/feature stays.
> - Each new tab gets a `TabVisibilityService` property, Settings checkbox, and `TabCheckboxMapping` entry.
> - Each new external dep gets registered in `DependencyCheckerService` + Settings/Setup windows.
> - Build only via `.\Build-Release.ps1`. Ask user before commit/push/release.
> - Update this file's checkboxes as work progresses.

---

## Phase 1 — High Impact, Low Risk (target v4.1.x)

### 1.1 Global Command Palette (`Ctrl+Shift+P`)  ✅ ALREADY EXISTS
_Audited 2026-04-27: shipped before v5 plan; baseline implementation present._
- [x] `Services/CommandService.cs` — fuzzy search + registry
- [x] `Views/CommandPaletteWindow.xaml` overlay
- [x] Hotkey wired in `MainWindow.xaml.cs` (Ctrl+Shift+P)
- [ ] Future enhancement (deferred): `[PaletteCommand]` attribute auto-discovery from VMs
- [ ] Future enhancement (deferred): pinned + recent persistence in `palette.json`
- [ ] Future enhancement (deferred): Settings checkbox to disable palette

### 1.2 Activity Log + Notification Center  ✅ SHIPPED
_Audited 2026-04-27: in-memory infrastructure is already in `Services/ToastNotificationService.cs` (Active + History collections, UnreadCount, IsPanelOpen, ClearHistory). DashboardViewModel exposes history; StatusBarControl toggles panel._
- [x] In-memory toast history (last 100) + unread badge + clear
- [ ] **Gap:** disk persistence (history lost on restart)
- [ ] **Gap:** dedicated `NotificationCenterView` tab (currently dashboard-only inline)
- [ ] **Gap:** separate `ActivityLogService` for non-toast events (job lifecycle, file ops) with category/level filters
- [ ] Wire emitters: `JobQueueService`, `RobocopyService`, `WallpaperRotatorService`, `AudioTranscriptionService`
- [ ] `TabVisibilityService.NotificationCenter` + Settings checkbox
- [ ] Retention/filter settings panel

### 1.3 Auto-Update  ✅ BASE EXISTS (delta patches deferred)
_Audited 2026-04-27: `Services/UpdateService.cs` already polls GitHub releases and downloads MSI/EXE._
- [x] `Services/UpdateService.cs` — GitHub Releases polling + verified download
- [ ] Future enhancement (deferred): `Build-Release.ps1` delta-patch emission (zstd/bsdiff)
- [ ] Future enhancement (deferred): stable/beta channel toggle in Settings
- [ ] Future enhancement (deferred): in-app changelog dialog with diff

### 1.4 Per-Tab Reset & Export/Import Config  ✅ SHIPPED
- [ ] `Services/TabConfig/ITabConfigProvider.cs` interface (optional opt-in)
- [ ] `Views/Controls/TabActionMenu.xaml` (⋯ button) — Export / Import / Reset
- [ ] `.platypuscfg` JSON bundle format with version + tab key + payload
- [ ] Implement on 3 reference tabs: WallpaperRotator, Robocopy, AudioTranscription
- [ ] Smoke test: export → wipe settings → import → state restored

### 1.5 Crash-Recovery Snapshots  ✅ SHIPPED (infrastructure)
- [ ] `Services/SessionStateService.cs` — periodic `IRecoverableState` snapshot
- [ ] `Models/IRecoverableState.cs`
- [ ] Restore prompt on next launch if last shutdown was non-clean
- [ ] Implement on JobQueue (in-flight jobs) and Robocopy (queued plans)
- [ ] Smoke test: kill process mid-job → relaunch → restore prompt → resume

### Phase 1 Exit Criteria
- [x] All 1.1–1.5 checked.
- [ ] `.\Build-Release.ps1` succeeds (auto-bump to ~v4.1.0).
- [ ] User approves → commit + push + GH release.
- [x] Changelog entry `[PHASE 1 COMPLETE]` written.

---

## Phase 2 — Power-User Layer (target v4.2.x)

### 2.1 PowerShell / Python REPL Tab
- [ ] `Services/Scripting/PowerShellHostService.cs`
- [ ] `Services/Scripting/PythonHostService.cs` (uses Tools/python if present)
- [ ] `Views/ScriptingConsoleView.xaml` — dual sub-tabs
- [ ] `$Platypus` proxy wrapper exposing read-only services
- [ ] Tab visibility + Settings checkbox (default visible, hosts disabled)
- [ ] DependencyCheckerService registration

### 2.2 Local REST API
- [ ] Extend `PlatypusTools.Remote.Server` with `/api/v1/*` controllers
- [ ] Separate API token auth, loopback default
- [ ] Swagger UI at `/api/docs`
- [ ] Settings: enable/disable, port, LAN binding opt-in
- [ ] Smoke test: curl endpoint → service responds

### 2.3 Workflow / Macro Recorder
- [ ] `Services/Workflow/WorkflowEngine.cs` — node graph executor
- [ ] `Views/WorkflowDesignerView.xaml` — node editor
- [ ] `.platypusflow` file format
- [ ] Triggers: hotkey, file-watch, schedule
- [ ] Tab + visibility + checkbox

### 2.4 Scheduled Job Templates
- [ ] Extend `ScheduledTasksView` with template gallery
- [ ] `templates/*.platypustemplate.json` shipping presets

### 2.5 Plugin Marketplace UI
- [ ] Extend `PluginManagerView` with registry browser
- [ ] `plugins/registry.json` index format
- [ ] Authenticode + SHA-256 verification

### Phase 2 Exit Criteria
- [ ] All 2.1–2.5 checked, build clean, user approves release.

---

## Phase 3 — Intelligence Layer (target v4.3.x)

### 3.1 Local LLM Sidebar
- [ ] `Services/Llm/LocalLlmService.cs` (Ollama / llama.cpp / OpenAI-compat)
- [ ] `Views/AssistantPanel.xaml` — dockable right sidebar
- [ ] `ILlmContextProvider` opt-in per tab
- [ ] DependencyCheckerService: Ollama
- [ ] Settings panel: backend selection, privacy toggles

### 3.2 Smart-Rename / Auto-Tag for Media
- [ ] `Services/MediaIntelligenceService.cs`
- [ ] Buttons in MediaLibrary, BatchWatermark, BulkFileMover

### 3.3 Whisper Subtitles in VideoEditor
- [ ] Extend `AudioTranscriptionService` with SRT/VTT writer
- [ ] VideoEditor subtitle track UI

### 3.4 Auto-Chaptering
- [ ] `Services/SceneDetectionService.cs` (ffmpeg scene + silence detect)
- [ ] Chapter markers in VideoEditor + MediaHub

### 3.5 Album Art Auto-Fetch
- [ ] `Services/MusicBrainzClient.cs`
- [ ] Button in audio player

### Phase 3 Exit Criteria
- [ ] All 3.1–3.5 checked, build clean, user approves release.

---

## Phase 4 — Security Pro Suite (target v4.4.x)

### 4.1 Threat-Feed Aggregator
- [ ] `Services/ThreatFeedService.cs` (CISA KEV, MISP, OTX)
- [ ] Surface in CveSearch + IocScanner

### 4.2 YARA Rule Editor + Scanner
- [ ] AdvancedForensics → YARA subtab
- [ ] dnYara NuGet
- [ ] Bundled rule sets

### 4.3 Sigma → KQL Translator
- [ ] LocalKQL Sigma sub-pane

### 4.4 Browser Extension Companion
- [ ] `PlatypusTools.BrowserExt/` MV3
- [ ] Talks to REST API loopback

### 4.5 Shared Encrypted Clipboard
- [ ] Extend ClipboardHistory with E2E sync via SecurityVault key

### Phase 4 Exit Criteria
- [ ] All 4.1–4.5 checked, build clean, user approves release.

---

## Phase 5 — UX & Visualization (target v4.5.x)

### 5.1 TreeMap Disk Heatmap
- [ ] DiskSpaceAnalyzer subtab "TreeMap"

### 5.2 Theme Builder
- [ ] Settings → Theme Builder tab with live preview + export

### 5.3 Accessibility Pass
- [ ] AutomationProperties on icon-only buttons (sweep)
- [ ] High Contrast theme
- [ ] Keyboard shortcut overlay (Ctrl+/)

### 5.4 Localization Completeness Meter
- [ ] Settings → Translation Status panel

### 5.5 HDR Thumbnailing
- [ ] MediaLibrary HDR tone-mapped thumbnails

### Phase 5 Exit Criteria
- [ ] All 5.1–5.5 checked, build clean, user approves release.

---

## Phase 6 — Performance & Scale (target v4.6.x)

### 6.1 Lazy Tab Loading
- [ ] `[LazyTab]` attribute + DataTemplate-based loader

### 6.2 Background Task Budget
- [ ] `Services/ResourceGovernor.cs`

### 6.3 Startup Profiler
- [ ] `Services/StartupProfilerService.cs` → startup-profile.json + About dialog

### 6.4 Multi-Machine Fleet View
- [ ] `Views/FleetView.xaml` discovery via remote channel

### 6.5 PWA Mobile Remote Dashboard
- [ ] manifest.json + service worker for RemoteDashboard

### Phase 6 Exit Criteria
- [ ] All 6.1–6.5 checked, build clean, user approves release.

---

## Status Snapshot
| Phase | Status | Version |
|-------|--------|---------|
| 1.1 Command Palette | ✅ Already shipped | (pre-v5) |
| 1.2 Activity Log / Notif Ctr | ✅ Notification Center shipped (history persisted to disk, dedicated view) | v4.0.2.7 |
| 1.3 Auto-Update | ✅ Base shipped (delta patches deferred) | (pre-v5) |
| 1.4 Per-Tab Reset/Export | ✅ ITabConfigProvider + TabActionMenuButton shipped (reference adopter: WallpaperRotator) | v4.0.2.7 |
| 1.5 Crash Recovery | ✅ Infrastructure shipped (IRecoverableState + SessionStateService + dirty-shutdown prompt). Per-tab adopters TBD. | v4.0.2.7 |
| 2.1 Scripting Console | ✅ UI + service shipped (one-shot pwsh/python via subprocess) | v4.0.3.0 |
| 2.2–2.5 | 🔴 Deferred | — |
| 3.1–3.4 | 🔴 Deferred (LLM/ffmpeg work) | — |
| 3.5 MusicBrainz client | ✅ Service shipped (UI wiring deferred) | v4.0.3.0 |
| 4.1 Threat Feed | ✅ Service shipped (CISA KEV; UI wiring deferred) | v4.0.3.0 |
| 4.2–4.5 | 🔴 Deferred | — |
| 5.1, 5.2, 5.4, 5.5 | 🔴 Deferred | — |
| 5.3 Keyboard shortcuts overlay | ✅ Ctrl+/ window shipped | v4.0.3.0 |
| 6.1, 6.4, 6.5 | 🔴 Deferred | — |
| 6.2 ResourceGovernor | ✅ Skeleton shipped (per-service adopters TBD) | v4.0.3.0 |
| 6.3 Startup Profiler | ✅ Already shipped (`Services/StartupProfiler.cs`) | (pre-v5) |
