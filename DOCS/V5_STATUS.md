# V5 Roadmap — Living Status File

**ALWAYS read this file before starting Phase 2-6 work. Update it whenever you ship.**

Legend: ✅ done & shipped · 🟢 done in working tree (not yet released) · 🟡 partial · 🔲 not started · 🔵 deferred to dedicated session (complex) · ❌ blocked

Last updated: simple cross-phase pass (working tree, build clean — 0 errors, ready to release)

---

## Phase 1 — Quality Foundation (v4.0.2.9) ✅ COMPLETE
| # | Item | Status | Notes |
|---|------|--------|-------|
| 1.1 | Notification Center | ✅ | Tab + service shipped v4.0.2.9 |
| 1.2 | Per-Tab Config (`ITabConfigProvider`) | ✅ | Infrastructure shipped; per-tab adopters deferred |
| 1.3 | Crash Recovery (`IRecoverableState`) | ✅ | Infrastructure + WallpaperRotator adopter |
| 1.4 | Error-resilient tabs | ✅ | Pre-existing `ErrorResilientTabHost` |
| 1.5 | Pre-commit hooks | ✅ | Auto patch-bump on every commit |

## Phase 2 — Power-User Layer
| # | Item | Status | Notes |
|---|------|--------|-------|
| 2.1 | Scripting Console | 🟡 | Subprocess PowerShell+Python tab shipped v4.0.3.0. **Missing:** `Microsoft.PowerShell.SDK` host, `$Platypus.*` proxies, sub-tabs, Settings opt-in, DependencyChecker registration. |
| 2.2 | REST/gRPC `/api/v1` | � | `Remote.Server/Program.cs` exposes `/api/v1` group with file-based Bearer (auto-generated 32-byte token at `%APPDATA%/PlatypusTools/api-token.txt`). Endpoints: `/health`, `/info`, `/audio/{nowplaying,queue,play,pause,next,previous}`, `/vault/items`. |
| 2.3 | Workflow / Macro Recorder | 🟢 | `WorkflowEngine` (shell/powershell/writefile/readfile/delay/log nodes, `{{key}}` substitution, JSON `.platypusflow` schema) + `WorkflowDesignerWindow` (Tools menu → 🔁 Workflow Designer). |
| 2.4 | Scheduled Job Templates | � | `ScheduledJobTemplates.cs` ships 4 starter Task Scheduler XML templates (backup-home, cleanup-temp, media-convert-mp4, forensics-collect). 'Templates ▾' button on `ScheduledTasksView` saves chosen template to disk + shows the `schtasks /create /xml` registration command. |
| 2.5 | Plugin Marketplace | � | `PluginRegistryService` reads `plugins/registry.json` (URL or local fallback), verifies SHA-256 against pin before installing into `%APPDATA%/PlatypusTools/Plugins/`. UI: Tools → 🧩 Plugin Marketplace. |

## Phase 3 — Intelligence Layer
| # | Item | Status | Notes |
|---|------|--------|-------|
| 3.1 | Local LLM Sidebar | � | `LocalLlmService` singleton (Ollama `/api/chat` + OpenAI-compat `/v1/chat/completions` for LM Studio) + `AIAssistantWindow` (Tools menu → 🤖 AI Assistant) with backend/model picker, system prompt, and chat log. |
| 3.2 | Smart-Rename / Auto-Tag | � | `SmartRenameService` calls `LocalLlmService` to suggest snake_case filenames; UI grid lets user toggle each suggestion before applying. Tools → ✨ Smart Rename (LLM). |
| 3.3 | Whisper Subtitles | ✅ | Pre-existing — `AudioTranscriptionView` already supports SRT + VTT export via `LocalWhisperService.TranscribeAsync` → `FormatCaptions`. Verified this pass. |
| 3.4 | Auto-Chaptering | � | `SceneDetectionService` shells to ffmpeg `select='gt(scene,N)',showinfo`, parses `pts_time:` to scenes, writes `;FFMETADATA1` chapter file. '🎬 Detect Scenes' button in `MultimediaEditorView` video player. |
| 3.5 | Album Art Auto-Fetch | 🟡→🟢 (pending build) | Service shipped v4.0.3.0; UI button in audio player added this pass. |

## Phase 4 — Security Pro Suite
| # | Item | Status | Notes |
|---|------|--------|-------|
| 4.1 | Threat-Feed Aggregator | 🟡→🟢 (pending build) | `ThreatFeedService.GetCisaKevAsync` shipped v4.0.3.0. CveSearch KEV badge added this pass. **Missing:** MISP, OTX, scheduled refresh, IocScanner auto-import. |
| 4.2 | YARA editor + scanner | � | `YaraScannerService` shells out to yara.exe (registered in `DependencyCheckerService.CheckYaraAsync`). UI: Tools → 🧬 YARA Scanner with rules picker, target picker, recursive toggle, sample-rule generator. |
| 4.3 | Sigma → KQL | � | Pure C# `SigmaToKqlTranslator` (tiny YAML reader → MDE/Sentinel KQL). Modifiers: contains/startswith/endswith/re. Surfaced as '🛡️ Sigma → KQL' button on AdvancedForensics LocalKQL tab — opens YAML, loads translated KQL into editor. |
| 4.4 | Browser Extension | � | MV3 extension at `BrowserExtension/` (manifest.json, background.js, popup.html/js) with context menus to push selection/link to `/api/v1/clipboard/plain`. |
| 4.5 | Encrypted Clipboard sync | � | `EncryptedClipboardSyncService` uses AES-GCM with a pre-shared 32-byte key (in `%APPDATA%/PlatypusTools/clipboard-key.bin`); pushes/pulls opaque blobs through Remote.Server `/api/v1/clipboard`. UI: Tools → 🔐 Encrypted Clipboard. |

## Phase 5 — UX & Visualization
| # | Item | Status | Notes |
|---|------|--------|-------|
| 5.1 | TreeMap Disk Heatmap | � | `TreeMapLayout` (squarified algorithm, pure WPF) drives `TreeMapDiskWindow` with click-to-reveal in Explorer. Tools → 🗺️ Disk TreeMap. |
| 5.2 | Theme Builder | � | `ThemeBuilderWindow` with hex editors, live preview pane, and Save/Load `ResourceDictionary` xaml export. Tools → 🎨 Theme Builder. |
| 5.3 | Accessibility Pass | 🟡 | Shortcut overlay (Ctrl+/) shipped v4.0.3.0. Focus visual style + High Contrast theme stub added this pass. **Missing:** AutomationProperties sweep across all icon-only buttons. |
| 5.4 | Localization Completeness Meter | 🟢 (pending build) | New panel in Settings → Language added this pass. |
| 5.5 | HDR Thumbnailing | � | `ThumbnailCacheService.GenerateVideoThumbnail` probes for `smpte2084`/`arib-std-b67`/`bt2020`; if HDR runs `zscale→tonemap=hable→bt709` before grabbing JPEG. |

## Phase 6 — Performance & Scale
| # | Item | Status | Notes |
|---|------|--------|-------|
| 6.1 | Lazy Tab Loading | � | `LazyTabContent` attached property defers UserControl construction until parent TabItem first becomes visible/focused. Opt-in per heavy tab; documented in service file. |
| 6.2 | Background Task Budget | � | `ResourceGovernor` adopted by `SceneDetectionService` (CPU slot acquired before ffmpeg invocation). Pattern is documented for future adopters. **Future:** JOB_OBJECT_LIMIT_CPU_RATE_CONTROL, more adopters across hot ffmpeg paths. |
| 6.3 | Startup Profiler | ✅ | `StartupProfiler.cs` exists; About-dialog surface verified this pass. |
| 6.4 | Fleet View | � | `FleetViewWindow` (Tools menu → 🛰️ Fleet View) parallel-scans `/24` subnet against the Remote.Server `/health` endpoint and renders an Address/Status/Latency/Banner grid. |
| 6.5 | PWA Mobile Dashboard | � | `PlatypusTools.Remote.Client/wwwroot/manifest.webmanifest` + `service-worker.js` already shipped with PWA icons — verified standalone display, theme color, maskable icon. |

---

## This Pass — "Simple features across all phases" (working tree, pending build)

| File | Change |
|------|--------|
| `PlatypusTools.UI/Views/EnhancedAudioPlayerView.xaml` (+ .cs) | Album-art fetch button → `MusicBrainzClient` |
| `PlatypusTools.UI/Views/CveSearchView.xaml.cs` | KEV badge enrichment via `ThreatFeedService` |
| `PlatypusTools.UI/Views/SettingsWindow.xaml` (+ .cs) | "Scripting" opt-in toggle + Localization Completeness panel |
| `PlatypusTools.UI/Services/DependencyCheckerService.cs` | PowerShell + Python entries |
| `PlatypusTools.UI/Views/AboutWindow.xaml` (+ .cs) | "Show startup profile" link |
| `PlatypusTools.UI/App.xaml` | Focus-visual style |
| `PlatypusTools.UI/Themes/HighContrast.xaml` (NEW) | High Contrast theme stub |

## Wave C/D — Marketplace, YARA, Smart Rename, Browser ext, Clipboard, TreeMap, Theme Builder, Lazy Tabs (built clean)

| File | Change |
|------|--------|
| `PlatypusTools.UI/Services/Plugins/PluginRegistryService.cs` (NEW) + `plugins/registry.json` (NEW) + `Views/PluginMarketplaceWindow.xaml` (+ .cs) | Phase 2.5 — fetch & SHA-256-pinned install. |
| `PlatypusTools.UI/Services/Files/SmartRenameService.cs` (NEW) + `Views/SmartRenameWindow.xaml` (+ .cs) | Phase 3.2 — LLM-driven snake_case rename suggestions. |
| `PlatypusTools.UI/Services/Security/YaraScannerService.cs` (NEW) + `Views/YaraScannerWindow.xaml` (+ .cs) + `DependencyCheckerService.CheckYaraAsync` | Phase 4.2 — yara.exe shell-out scanner. |
| `BrowserExtension/` (NEW) | Phase 4.4 — MV3 manifest + service worker + popup, talks to `/api/v1`. |
| `PlatypusTools.UI/Services/Clipboard/EncryptedClipboardSyncService.cs` (NEW) + `Views/EncryptedClipboardWindow.xaml` (+ .cs) + `Remote.Server/ClipboardStore.cs` + `/api/v1/clipboard` endpoints | Phase 4.5 — AES-GCM E2E sync. |
| `PlatypusTools.UI/Services/Visualization/TreeMapLayout.cs` (NEW) + `Views/TreeMapDiskWindow.xaml` (+ .cs) | Phase 5.1 — squarified treemap, pure WPF Canvas. |
| `PlatypusTools.UI/Views/ThemeBuilderWindow.xaml` (+ .cs) | Phase 5.2 — palette editor + ResourceDictionary export/import. |
| `PlatypusTools.UI/Services/UI/LazyTabContent.cs` (NEW) | Phase 6.1 — attached behavior to defer heavy tab construction. |

---

## Wave B — REST + Workflow + LLM + Fleet (built clean)

| File | Change |
|------|--------|
| `PlatypusTools.Remote.Server/Program.cs` | `/api/v1` group with file-based Bearer auth (auto-generated token), endpoints for health/info/audio/vault (Phase 2.2). |
| `PlatypusTools.UI/Services/Workflow/WorkflowEngine.cs` (NEW) | Workflow engine with shell/powershell/writefile/readfile/delay/log nodes + JSON serializer (Phase 2.3). |
| `PlatypusTools.UI/Views/WorkflowDesignerWindow.xaml` (+ .cs) (NEW) | Steps-list designer: Open/Save As/Run with streaming output. |
| `PlatypusTools.UI/Services/AI/LocalLlmService.cs` (NEW) | Ollama + OpenAI-compat client (LM Studio) with model listing & non-streaming chat (Phase 3.1). |
| `PlatypusTools.UI/Views/AIAssistantWindow.xaml` (+ .cs) (NEW) | Backend/model picker, system prompt, chat log, Ctrl+Enter send. |
| `PlatypusTools.UI/Views/FleetViewWindow.xaml` (+ .cs) (NEW) | Parallel `/24` health-endpoint scanner with cancel + result grid (Phase 6.4). |
| `PlatypusTools.UI/MainWindow.xaml` (+ .cs) | Tools menu: 🔁 Workflow Designer, 🤖 AI Assistant, 🛰️ Fleet View. |

---

## Wave A — Phase 2-6 deferred items (built clean)

| File | Change |
|------|--------|
| `PlatypusTools.UI/Services/Video/SceneDetectionService.cs` (NEW) | ffmpeg scene detection + FFMETADATA chapter writer (Phase 3.4). Uses `ResourceGovernor` CPU slot (Phase 6.2). |
| `PlatypusTools.UI/Services/Security/SigmaToKqlTranslator.cs` (NEW) | Pure C# Sigma YAML → KQL translator (Phase 4.3). |
| `PlatypusTools.UI/Services/Scheduling/ScheduledJobTemplates.cs` (NEW) | 4 starter Task Scheduler XML templates (Phase 2.4). |
| `PlatypusTools.UI/Views/MultimediaEditorView.xaml` (+ .cs) | "🎬 Detect Scenes" button on video player. |
| `PlatypusTools.UI/Views/AdvancedForensicsView.xaml` (+ .cs) | "🛡️ Sigma → KQL" button on LocalKQL tab. |
| `PlatypusTools.UI/Views/ScheduledTasksView.xaml` (+ .cs) | "📋 Templates ▾" context menu of starter jobs. |
| `PlatypusTools.UI/Services/ThumbnailCacheService.cs` | HDR-aware video thumbnails via ffmpeg `zscale`/`tonemap=hable` when smpte2084/arib-std-b67/bt2020 detected (Phase 5.5). |

---

## Plan for Complex Items (next dedicated sessions)

### Phase 2.2 — REST API `/api/v1` (1 session)
- Add controllers under `PlatypusTools.Remote.Server` separate from existing remote dashboard endpoints.
- New auth middleware: Bearer token from `%APPDATA%/PlatypusTools/api-token.txt`, generated on first run.
- Endpoints: `/api/v1/vault`, `/api/v1/audio`, `/api/v1/forensics`, `/api/v1/files` — proxy to existing services.
- Swagger UI at `/api/docs` (already have `AddOpenApi()`; add Swashbuckle).
- Settings toggle: "Enable local API" (off), "Allow LAN" (off, loopback default).

### Phase 2.3 — Workflow Engine
- `IWorkflowNode { string Type; Dictionary<string,object> Inputs; Task<object> ExecuteAsync(); }`
- `WorkflowEngine` runs DAG of nodes; persists `.platypusflow` JSON.
- Designer tab: simple ListBox-based node list (NOT visual graph this round) — ship visual canvas in dedicated v4.5+ session.
- Triggers: manual + hotkey only first; on-file-watch + on-schedule reuse `FileWatcherService` + `ScheduledTasks`.

### Phase 2.5 — Plugin Marketplace
- `plugins/registry.json` schema: `[{ id, name, version, sha256, downloadUrl, author, signature }]`.
- New tab in `PluginManagerView`: "Marketplace" with install/update buttons.
- Signature: Authenticode check + SHA-256 pin against registry value.

### Phase 3.1 — Local LLM Sidebar
- `ILlmBackend` (Ollama HTTP, OpenAI-compat). llama.cpp via OpenAI-compat against local server.
- `AssistantPanel` UserControl, dockable via existing `DetachableTabService`.
- `ILlmContextProvider` interface; opt-in providers in 2-3 tabs initially (Forensics, LocalKQL, MediaLibrary).
- Privacy gate: cloud key entry requires explicit dialog acknowledgment.

### Phase 3.3, 3.4
- 3.3: extend `AudioTranscriptionService` with SRT/VTT formatter; add "Generate subtitles" button on VideoEditor; new optional subtitle track via existing track infrastructure.
- 3.4: `SceneDetectionService` shells out to ffmpeg, parses scene timestamps, writes `;FFMETADATA1` chapter file.

### Phase 4.2, 4.3
- 4.2: add `dnYara` package, register in DependencyChecker, new YARA tab; bundle ~50 community rules from public github mirrors.
- 4.3: pure C# Sigma parser → KQL emitter (covers ~70% of common rules); subtab on LocalKQL.

### Phase 5.1, 5.2, 5.5
- 5.1: pick `LiveChartsCore.SkiaSharpView.WPF` (treemap supported); inside-tab toggle.
- 5.2: extend existing `ThemeEditorWindow` with color-pick + xaml export — verify it can already do this; if not, add.
- 5.5: precheck `ffprobe` for HDR metadata; if HDR, run `ffmpeg -vf zscale=t=linear,tonemap=hable` before sampling.

### Phase 6.1, 6.4, 6.5
- 6.1: introduce `[LazyTab]` attribute; `MainWindow.xaml` refactor to `DataTemplate` per tab; first-load lazy via `TabControl.SelectionChanged`.
- 6.4: leverage existing `RemoteDesktop` discovery + signalR `PlatypusHub`.
- 6.5: add `wwwroot/manifest.json` + service worker to existing `RemoteDashboard`.

---

## UNDO Recipes
Each entry below was a single commit; revert with `git revert <sha>`. Baseline pre-Phase-2: `483b04c`. Phase 2-6 skeleton commit: `64fe020`.
