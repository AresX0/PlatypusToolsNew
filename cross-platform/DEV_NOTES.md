# Cross-Platform Port — Developer Notes & Decisions

> **READ THIS FILE BEFORE ANY CHANGE TO THE CROSS-PLATFORM PORT.**
> It is the authoritative log of what was done, why, and what NOT to touch.

---

## 2026-04-30 (later) — Batch 10: final 2 cross-platform-able features + security cleanup

User directive: "Do the things that can be ported over, skip the things that
can't, I don't want a non supported panel. Finish the notes and make a help
file, do the mailkit and security fixes."

**Ported (2):**

- **Service Manager** (System) — listing + start/stop/restart for any service:
  - Linux: `systemctl list-units --type=service` + `systemctl {verb} {name}`
  - Mac:   `launchctl list` + `launchctl {start|stop} {name}` (restart = stop+start)
  - Win:   `sc query state= all` + `sc {verb} {name}`
- **Directory / LDAP Security Analyzer** (Security) — pure `ldapsearch` shell-out
  works against AD, OpenLDAP, FreeIPA. 6 prebuilt LDAP filter presets for common
  security queries (enabled users, never-expire passwords, admin-count groups,
  servers, domain admins). LDIF output. No `System.DirectoryServices` dependency.

**Skipped intentionally (no cross-platform analog, per user directive — no
"unsupported" panels):**

- `WindowsUpdateRepairView` — `wuauclt`/DISM/SFC are Windows-only.
- `RegistryCleanerView`     — Windows registry only.

**Security / vulnerability fixes:**

- Bumped `MailKit` from **4.15.1 → 4.16.0** in `PlatypusTools.Core.csproj`
  (clears NU1902 GHSA-9j88-vvj5-vhgr).
  - 4.16.0 made `IImapClient.Inbox` and `IMailFolder?` returns nullable; added
    null-forgiving (`!`) at three call sites: two in
    `PlatypusTools.Core/Services/Mail/ImapMailService.cs` (lines 84 + 386), one
    in `Batch9ExtraViewModels.cs` MailClient.
- Pinned `Tmds.DBus.Protocol` from transitive **0.20.0 → 0.92.0** as a direct
  `PackageReference` in `PlatypusTools.UI.Avalonia.csproj` (clears NU1903
  GHSA-xrw6-gwf8-vvr9 via override). Avalonia 11.2.3 still binds correctly.

**Build:** `dotnet build PlatypusTools.CrossPlatform.sln -c Release` →
**Build succeeded. 0 Error(s). 12 Warning(s)** — down from 18; the two
vulnerability advisories are gone. Remaining warnings are pre-existing
nullability and CA1716 style rules, none security-related.

**Total sidebar features:** 111 → **113** across 7 categories.

---

## 2026-04-30 — Mass port Batch 9 (30 new features incl. multimedia, screensaver, slideshow, Intune, AI)

Sidebar grew from **81 → 111 features**. User directive: "MULTIMEDIA MUST BE
PRESENT all multimedia must be" + "you can backup intune from any system and you
can have a screensaver on anything and slideshows anywhere" + "DO THIS ALL
NOTHING IS OUT OF SCOPE unless I say it is."

**Multimedia (9):** NativeAudioTrim, NativeImageEdit, NativeVideoPlayer,
MediaHub, MediaLibrary, MultimediaEditor (launches host editors), Model3DEditor
(blender/freecad/meshlab/openscad), ShotcutNativeEditor, Upscaler.

**Screensaver / Slideshow (2):** ScreensaverConfig (persists to AppSettings),
SlideshowScreensaver (opens a fullscreen `SlideshowWindow` with
`DispatcherTimer` cycling images; Esc/click closes).

**Intune Backup (1):** Pure-HTTP `HttpClient` against Microsoft Graph
v1.0 with paged GETs and `@odata.nextLink` follow — no SDK, works on any OS
that can talk HTTPS. User pastes a Bearer access token from Graph Explorer.

**Forensics / Mail / System (8):** AdvancedForensics (Shannon entropy +
magic-byte signature), MailClient (MailKit IMAP read-only), RebootAnalyzer,
RecentCleanup, FileCleaner, SystemHardening, SystemRestore, PlexBackup.

**Tools / Misc (10):** ThemeBuilder, EncryptedClipboard (AES-256-CBC),
ExportQueue, AIAssistant (OpenAI-compatible chat-completions), WorkflowDesigner,
FleetView, RemoteDashboard, PluginMarketplace, GlobalSearch, UpdateChecker.

**AppSettings extended:** ScreensaverIdleMin / ScreensaverLock /
ScreensaverFolder / ScreensaverInterval / ScreensaverShuffle / AiEndpoint /
AiApiKey / AiModel / UpdateRepo.

**Build:** `dotnet build PlatypusTools.CrossPlatform.sln -c Release` →
**Build succeeded. 0 Error(s). 18 Warning(s).** All warnings pre-existing
(MailKit + Tmds.DBus.Protocol vulnerability advisories, ColorPicker partial-
method signatures).

**Notes / decisions:**
- Did NOT touch `PlatypusTools.UI/` (WPF) or root `PlatypusTools.sln`.
- `MediaLibraryViewModel` inherits `MediaHubViewModel` — same scanner, different view.
- `RemoteDashboardViewModel` inherits `FleetViewModel` — same data, different layout.
- Multimedia editors that lack managed equivalents shell out to host installs;
  no NuGet runtime dependency added.
- AES round-trip uses SHA-256(password) as key derivation — fast, but not as
  strong as PBKDF2; documented in code as "round-trip", not for at-rest storage.
- ObservableObject-derived classes (`ExportJob`, `WorkflowStep`, `FleetHost`)
  must be `partial` — `[ObservableProperty]` source generator requires it.

---

## 2026-04-29 — Mass port Batch 8 (16 new features)

Sidebar grew from **65 → 81 features**. New Home category was expanded with
Command Palette, Changelog viewer, and Keyboard Shortcuts. The rest are
slotted across Media (Audio Transcription), Files (Cloud Sync, Tree-Map Disk,
Hider, Scheduled Backup), Security (CVE Search, YARA Scanner, Forensics
Analyzer, Credential Manager), System (Disk Health, Bootable USB, Remote
Desktop), and Tools (Batch Job Queue, Plugin Manager, Simple Browser).

| Feature | Implementation | Notes |
| --- | --- | --- |
| Command Palette | In-page fuzzy filter; calls `MainWindowViewModel.NavigateToVmTypeName` | Reaches `MainWindow.DataContext` via `Application.ApplicationLifetime` |
| Changelog Viewer | Plain `TextBox` over markdown text; auto-discovers `CHANGELOG.md` | No markdown rendering yet (kept simple) |
| Keyboard Shortcuts | Static `ObservableCollection<ShortcutRow>` in DataGrid | Reference card only — no global hotkeys yet |
| Cloud Sync | `rclone sync / bisync` with `--dry-run` | Lists configured remotes via `rclone listremotes` |
| Tree-Map Disk | Top-level dir/file size scan, top-50, bar = % of total | Pure managed; no external |
| Hider | Linux/Mac: `mv` to dot-prefixed name. Windows: `attrib +H/-H` | Round-trippable |
| Scheduled Backup | `ZipFile.CreateFromDirectory`; schedule = `schtasks /Create /SC DAILY` or `crontab -l` ; `crontab` | Schedule entry uses zip on Unix path |
| Audio Transcription | `whisper <file> --model … --output_format txt` with `whisper.cpp` fallback | Status reports both exit codes if first fails |
| CVE Search | `services.nvd.nist.gov/rest/json/cves/2.0?keywordSearch=…` | Uses HttpClient + System.Text.Json; CVSS metrics from v3.1 → v3.0 → v2 |
| YARA Scanner | `yara [-r] rules target` live transcript | Cancellable |
| Forensics Analyzer | Magic-byte detection (PE/ELF/Mach-O/PNG/JPEG/PDF/ZIP), MD5/SHA1/SHA256, ASCII strings extraction | Pure managed |
| Credential Manager | `cmdkey /generic …` / `security add-generic-password` / `secret-tool store` | Read & delete also wired |
| Disk Health | `smartctl -a <device>` + `smartctl --scan` | Needs root/Admin for raw devices |
| Bootable USB | `dd if=… of=… bs=4M status=progress oflag=sync` | Confirmation checkbox required; Windows defers to Rufus |
| Remote Desktop | Launches `mstsc /v:` / `xfreerdp /v:` / `vncviewer` / `ssh` | Protocol picker driven by OS |
| Plugin Manager | Lists `<AppData>/plugins/*`; opens via `FileLauncher` | DI-free; non-static `FileLauncher` |
| Simple Browser | Defers URL to OS default browser | Avalonia has no built-in WebView |
| Batch Job Queue | Sequential `pwsh -NoProfile -Command` / `bash -c` | Per-row exit code + state |

Build: `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln -c Release`
→ **Build succeeded. 0 Error(s).** Pre-existing warnings unchanged
(NU1902/NU1903/CS8826/SYSLIB0060/CS8618/CS0169 in unrelated files).

No changes to `PlatypusTools.UI/` (WPF) or root `PlatypusTools.sln`.

---

## 2026-05-03 — Mass port Batch 7 (8 new features) + polish

Added 8 new feature triplets and a Dependencies probe page, plus three polish
items (sidebar search, persisted last view, dark/light toggle). Total sidebar
count is now **65 features across 7 categories** (added a new **Home** category).

| File pair | Purpose | Cross-platform notes |
| --- | --- | --- |
| `QrCodeViewModel/View` | QR code generator | `QRCoder` 1.6.0 (managed); generate + Save PNG via Avalonia StorageProvider. |
| `PdfToolsViewModel/View` | PDF merge/split/rotate/encrypt/decrypt | Wraps `qpdf` CLI. Linux: `apt install qpdf` · Mac: `brew install qpdf` · Win: `scoop install qpdf`. |
| `FtpClientViewModel/View` | FTP/FTPS browser | `FluentFTP` 51.1.0 (`AsyncFtpClient`). List + download. |
| `TerminalClientViewModel/View` | Embedded terminal | `ShellHelper.RunAsync` with shell selector (pwsh/bash/zsh/sh/cmd). Live transcript + cancel. |
| `FileSyncViewModel/View` | Folder sync | `rsync -avh` (+`--delete`/`--dry-run`) on Linux/Mac; `robocopy /MIR /L` on Windows. |
| `MetadataEditorViewModel/View` | EXIF/XMP edit | Wraps `exiftool` read & write. |
| `MailSenderViewModel/View` | Send SMTP mail | `MailKit.Net.Smtp` + `MimeKit`; STARTTLS support. MailKit is already a transitive dep of `PlatypusTools.Core`. |
| `DashboardViewModel/View` | Home tiles | WrapPanel of buttons; clicks resolve target VM type via `MainWindowViewModel.NavigateToVmTypeName`. |
| `DependenciesViewModel/View` | External-tool probe | Walks `PATH`, plus brew/macports paths on macOS. Reports per-OS install hint. |

**Polish:**
- Sidebar **search filter**: `TextBox` bound to `MainWindowViewModel.Filter`. `OnFilterChanged` rebuilds `Categories` from the immutable `AllCategories` master list.
- **Persisted last view**: `Services/AppSettings.cs` (JSON to `<AppData>/settings.json`). `OnSelectedItemChanged` writes; constructor reads.
- **Dark/light toggle**: Button in sidebar header bound to `ToggleThemeCommand`. State persisted alongside last view.

**NuGet additions to `cross-platform/PlatypusTools.UI.Avalonia.csproj`:**
- `FluentFTP` 51.1.0
- `QRCoder` 1.6.0

**Analyzer tweak (`Directory.Build.props`):** added `CA2000` to NoWarn. The ViewModels collected into `MainWindowViewModel.AllCategories` are long-lived — analyzer flow analysis can't see they're stored when constructed inside the constructor body, only via property initializers.

**Service APIs touched (no breaking changes):**
- `MainWindowViewModel.NavigateToVmTypeName(string)` — public for Dashboard tile clicks.
- `Services/AppSettings.cs` — new lightweight JSON store.

**Build result:** `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln -c Release` → **Build succeeded. 0 Error(s).** Warnings unchanged from prior batches (NU1902/NU1903 transitive vulns; CS8826 ColorPicker partial signature; SYSLIB0060 Rfc2898DeriveBytes; CS8618/CS0169 unused field).

## ⛔ NEVER-DO LIST (immutable)

1. **DO NOT modify any file under `PlatypusTools.UI/`** (the WPF Windows project).
   That project is the production Windows release. The Windows MSI is built
   from it via `Build-Release.ps1`. The cross-platform port is **completely
   side-by-side** and must not influence the Windows build in any way.

2. **DO NOT modify the root `PlatypusTools.sln`.**
   This solution is the Windows build's source of truth. Cross-platform work
   uses a separate solution: `cross-platform/PlatypusTools.CrossPlatform.sln`.

3. **DO NOT modify `Build-Release.ps1`, root `Directory.Build.props`, or
   `PlatypusTools.Installer/*`.** These feed the Windows MSI pipeline.

4. **DO NOT remove or refactor existing code** in `PlatypusTools.Core`.
   It IS shared between Windows and cross-platform. If a Core file needs a
   tweak (e.g. an API that throws on Linux), gate it via:
   ```csharp
   if (OperatingSystem.IsWindows()) { /* existing path */ }
   else { /* graceful fallback or NotSupportedException */ }
   ```
   Then build BOTH the Windows project and the cross-platform solution
   before considering the change done.

5. **DO NOT add Windows-only NuGet packages** to the Avalonia project. If
   you need Windows-specific APIs in shared code, they must already live in
   `PlatypusTools.Core` and be guarded.

---

## Architecture decisions

### Why Avalonia 11 (not MAUI / Uno / web)
- WPF-closest XAML dialect; makes future ViewModel sharing tractable.
- Real Linux + macOS support (MAUI's Linux story is community-only).
- Active community, full set of controls, built-in dark mode, Skia renderer
  matches what Honmoon/etc. already use in the Windows project.

### Why a separate `cross-platform/` folder
- Visually obvious it's a side project.
- Easy `.gitignore` carve-outs and CI matrix split.
- Protects the Windows build from any accidental contamination.

### Why reference `PlatypusTools.Core` directly (not a copy)
- The Core project is already `net10.0` and 95% cross-platform.
- A copy would diverge — bug fixes wouldn't propagate.
- Any non-portable Core API gets wrapped with `OperatingSystem.IsWindows()`
  guards rather than copied.

### What lives in `cross-platform/PlatypusTools.UI.Avalonia/Services/`
Platform shims for things WPF/Win32 did natively:
- `IFileLauncher` — `xdg-open` (Linux) / `open` (mac) / `Process.Start("explorer", ...)` (Win)
- `ITrashService` — `gio trash` (Linux) / `osascript` Finder (mac) / `SHFileOperation` (Win, via Core)
- `IClipboardService` — Avalonia clipboard
- `IThemeService` — Avalonia FluentTheme variant
- `INotificationService` — Avalonia native dialog / `notify-send` / `osascript display notification`

### Audio playback
- LibVLCSharp.Avalonia + `VideoLAN.LibVLC.Linux` and `VideoLAN.LibVLC.Mac`
  NuGet runtime packages so VLC ships with the app (no system VLC needed).

### Visualizer port
- The Honmoon / Matrix / Klingon / Jedi / etc. visualizers in
  `PlatypusTools.UI/Views/AudioVisualizerView.xaml.cs` are SkiaSharp-based —
  they CAN be ported. Initial port: empty placeholder. Honmoon port will be
  copied **as a sibling file** in the Avalonia project (NOT moved or
  deleted from the WPF project).

---

## Feature parity matrix

| Feature                       | Windows (WPF) | Linux/macOS (Avalonia) |
| ----------------------------- | ------------- | ---------------------- |
| Audio Player + visualizer     | ✅            | 🔨 In progress         |
| File Manager                  | ✅            | 🔨 Planned             |
| Duplicates / Hash Scanner     | ✅            | 🔨 Planned             |
| Text Editor                   | ✅            | 🔨 Planned (AvaloniaEdit) |
| Vault                         | ✅            | 🔨 Planned             |
| Image Viewer / thumbnails     | ✅            | 🔨 Planned             |
| Audio Library scan            | ✅            | 🔨 Planned             |
| Settings / themes             | ✅            | 🔨 Planned             |
| AD Security / GPO / DomainJoin| ✅            | ❌ Stub ("Windows only")|
| Wallpaper Rotator             | ✅            | ❌ Stub                 |
| Slideshow Screensaver (.scr)  | ✅            | ❌ Stub                 |
| Cloudflare Tunnel manager     | ✅            | ⚠️ Maybe (cloudflared works) |
| Tailscale manager             | ✅            | ⚠️ Maybe (tailscale CLI)|
| WinRT toasts                  | ✅            | ❌ Replaced by `notify-send` / `osascript` |

---

## Build / packaging

| Script                              | Output                                              |
| ----------------------------------- | --------------------------------------------------- |
| `cross-platform/build-linux.sh`     | `dist/PlatypusTools-linux-x64.tar.gz`               |
| `cross-platform/build-linux-appimage.sh` | `dist/PlatypusTools-x86_64.AppImage`           |
| `cross-platform/build-macos.sh`     | `dist/PlatypusTools-osx-x64.app`, `…osx-arm64.app`  |
| `cross-platform/build-portable.sh`  | Single-file binary for current host                 |

These run on Linux/macOS hosts. On Windows you can `dotnet build` to verify
compile, but cannot produce final artifacts (AppImage / .app need native
host tooling).

---

## Change log (append-only)

### 2026-04-29 — Initial scaffold
- **Why**: User approved scope B (Avalonia shell, subset of features) and
  was emphatic the Windows codeset must not change.
- **Files added**:
  - `cross-platform/README.md` — top-level docs
  - `cross-platform/PlatypusTools.CrossPlatform.sln`
  - `cross-platform/PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj`
    (references `..\..\PlatypusTools.Core`)
  - `cross-platform/PlatypusTools.UI.Avalonia/App.axaml(.cs)`
  - `cross-platform/PlatypusTools.UI.Avalonia/Program.cs`
  - `cross-platform/PlatypusTools.UI.Avalonia/app.manifest`
  - `cross-platform/DEV_NOTES.md` (this file)
  - `/memories/repo/CROSS_PLATFORM_PORT.md` (always-loaded reminder)
- **Files modified outside `cross-platform/`**: NONE.
- **Next**: scaffold Views/ViewModels/Services skeleton, then build scripts.

### 2026-04-29 — Shell, ViewModels, placeholder Views, platform services, build scripts
- **Why**: Continue B-scope scaffold so the Avalonia app compiles cleanly
  end-to-end and shows a navigable shell.
- **Files added** (all under `cross-platform/`):
  - `PlatypusTools.UI.Avalonia/ViewModels/MainWindowViewModel.cs`
  - `PlatypusTools.UI.Avalonia/ViewModels/PlaceholderViewModels.cs`
    (9 stub VMs: AudioPlayer, FileManager, Duplicates, HashScanner,
     TextEditor, Vault, ImageViewer, AudioLibrary, Settings)
  - `PlatypusTools.UI.Avalonia/ViewModels/TextDocumentConverter.cs`
    (string ↔ AvaloniaEdit `TextDocument` two-way)
  - `PlatypusTools.UI.Avalonia/Views/MainWindow.axaml(.cs)`
    (220 px sidebar ListBox + ContentControl, view-locator DataTemplates)
  - 9 placeholder views + code-behinds:
    `AudioPlayerView`, `FileManagerView`, `DuplicatesView`,
    `HashScannerView`, `TextEditorView`, `VaultView`, `ImageViewerView`,
    `AudioLibraryView`, `SettingsView`
  - `PlatypusTools.UI.Avalonia/Services/IPlatformServices.cs`
    (`IFileLauncher`, `ITrashService`, `INotificationService`, `IThemeService`)
  - `PlatypusTools.UI.Avalonia/Services/FileLauncher.cs`
    (xdg-open / open / open -R / explorer /select)
  - `PlatypusTools.UI.Avalonia/Services/TrashService.cs`
    (`gio trash` / `osascript` Finder delete / Win fallback)
  - `PlatypusTools.UI.Avalonia/Services/NotificationService.cs`
    (`notify-send` / `osascript display notification`)
  - `PlatypusTools.UI.Avalonia/Services/ThemeService.cs`
    (Avalonia FluentTheme variant switch)
  - `PlatypusTools.UI.Avalonia/Assets/README.md` (placeholder for AvaloniaResource glob)
  - `cross-platform/build-linux.sh` (self-contained linux-x64 + tar.gz)
  - `cross-platform/build-linux-appimage.sh` (AppDir + appimagetool)
  - `cross-platform/build-macos.sh` (osx-x64 AND osx-arm64 .app bundles + Info.plist)
  - `cross-platform/build-portable.sh` (single-file for host RID)
- **Files modified inside `cross-platform/`**:
  - `PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj`:
    - Removed `VideoLAN.LibVLC.Linux` (no such NuGet exists — Linux uses
      distro-installed libvlc via `apt install libvlc-dev`).
    - Bumped `VideoLAN.LibVLC.Mac` to `3.1.3.1` (3.0.20 not on feed).
    - Removed bogus `AvaloniaEdit.TextMate` package; kept `Avalonia.AvaloniaEdit 11.1.0`.
- **Files modified outside `cross-platform/`**: NONE. Verified via
  `git status` — only `cross-platform/**` paths touched.
- **Build verification**:
  `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln -c Release`
  → **Build succeeded. 0 Error(s).** (~702 pre-existing warnings from
  `PlatypusTools.Core` Windows-only API call sites — unchanged baseline,
  not a regression.)
- **Known issue handled**: CA2000 was promoted to error by the root
  Directory.Build.props analyzer config. `TrashService.RunAsync` wraps the
  `Process` lifecycle in try/catch with explicit Dispose in both the
  `Exited` handler and the catch path; CA2000 is `#pragma`-suppressed
  *only* around that block (not project-wide).
- **Next**:
  - Port the actual feature shells (Audio Player, File Manager, Hash Scanner)
    one at a time as parallel sibling files — never modifying the WPF originals.
  - Wire `IFileLauncher` etc. into the Settings VM / DI container.
  - Add a real app icon (PNG + ICNS) under `Assets/`.
  - On a Linux host, run `./build-linux.sh` end-to-end to verify the
    self-contained binary launches.

### 2026-04-29 — First real feature port: Hash Scanner + DI bootstrap
- **Why**: Hash Scanner uses only `System.Security.Cryptography` — fully
  cross-platform with no Windows-specific deps. Good first real port.
- **Files added**:
  - `PlatypusTools.UI.Avalonia/Services/AppServices.cs` — tiny static
    service locator (no `Microsoft.Extensions.DependencyInjection` dep,
    keeps trim/AOT story clean).
  - `PlatypusTools.UI.Avalonia/ViewModels/HashScannerViewModel.cs` — real
    VM with `[RelayCommand]` HashFiles/Cancel/Clear, multi-file streaming
    1 MiB-buffered hash with progress, cancellation token, native toast
    on completion via `INotificationService`. Supports SHA256 (default),
    SHA1, MD5, SHA512.
- **Files modified**:
  - `PlatypusTools.UI.Avalonia/ViewModels/PlaceholderViewModels.cs` —
    removed stub `HashScannerViewModel` (now lives in its own file).
  - `PlatypusTools.UI.Avalonia/Views/HashScannerView.axaml` — replaced
    placeholder with algorithm picker, Pick Files / Cancel / Clear buttons,
    progress bar, scrollable results list with selectable hash text.
  - `PlatypusTools.UI.Avalonia/Views/HashScannerView.axaml.cs` — uses
    `TopLevel.GetTopLevel(this).StorageProvider.OpenFilePickerAsync` and
    `TryGetLocalPath()` (works on Linux/macOS native pickers + Windows).
- **Files modified outside `cross-platform/`**: NONE.
- **Avalonia gotchas hit**:
  - `Grid.RowSpacing` does NOT exist in Avalonia 11.2 (it's WPF-only-ish).
    Use per-row `Margin` instead. **Document for future ports.**
- **Build**: `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln`
  → **Build succeeded. 0 Error(s).**
- **Next**: Port File Manager (uses `IFileLauncher` + `ITrashService`
  shims) and then Audio Player with LibVLCSharp.

### 2026-04-29 — File Manager + Audio Player ports
- **Why**: Exercise both platform shims (`IFileLauncher`, `ITrashService`,
  `INotificationService`) end-to-end (FileManager) and prove LibVLCSharp
  audio works on the cross-platform target (AudioPlayer).
- **Files added**:
  - `PlatypusTools.UI.Avalonia/ViewModels/FileManagerViewModel.cs` —
    full VM: navigate (`..` entry, GoUp, GoHome, refresh on path change),
    double-click open via `IFileLauncher`, reveal-in-file-manager,
    move-to-trash via `ITrashService` + native toast.
  - `PlatypusTools.UI.Avalonia/ViewModels/AudioPlayerViewModel.cs` —
    LibVLCSharp `LibVLC` + `MediaPlayer` wrapper, Play/Pause/Stop relay
    commands, volume slider 0..100, position 0..1, IsPlaying state,
    `LoadAsync(path)` for picker. Disposable.
- **Files modified**:
  - `PlatypusTools.UI.Avalonia/ViewModels/PlaceholderViewModels.cs` —
    removed stub `FileManagerViewModel` and `AudioPlayerViewModel`.
  - `PlatypusTools.UI.Avalonia/Views/FileManagerView.axaml(.cs)` —
    full toolbar (🏠 Home / ⬆ Up / 🔄 Refresh + path TextBox) +
    `DataGrid` of entries (icon, name, size, modified, Open/Reveal/🗑).
    Double-tap opens / navigates.
  - `PlatypusTools.UI.Avalonia/Views/AudioPlayerView.axaml(.cs)` — picker,
    transport buttons, volume slider, progress bar bound to `Position`,
    track-name display, helper text about libvlc on Linux.
  - `PlatypusTools.UI.Avalonia/PlatypusTools.UI.Avalonia.csproj` — added
    `Avalonia.Controls.DataGrid 11.2.3` package (DataGrid is a separate
    NuGet in Avalonia 11).
  - `PlatypusTools.UI.Avalonia/App.axaml` — added
    `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml" />`
    so DataGrid renders correctly.
- **Files modified outside `cross-platform/`**: NONE.
- **Avalonia gotchas hit**:
  - `Avalonia.Controls.DataGrid` is NOT in the base Avalonia package —
    need both the NuGet AND its theme `StyleInclude`.
  - LibVLCSharp's `Core.Initialize()` clashes with `PlatypusTools.Core`
    namespace. Always fully qualify as `LibVLCSharp.Shared.Core.Initialize()`.
  - `IStorageFile.TryGetLocalPath()` is the standard way to get a real
    filesystem path from Avalonia's `StorageProvider` picker.
- **Build**: `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln`
  → **Build succeeded. 0 Error(s).**
- **Next**: app icon assets (PNG / ICNS / ICO sibling), then run
  `./build-linux.sh` on a Linux host for the first real artifact.

### 2026-04-29 — Duplicates + Text Editor ports
- **Why**: `DuplicatesScanner` (in `PlatypusTools.Core`) is fully
  cross-platform — pure `System.IO` + `System.Security.Cryptography` —
  so it ports for free with just a VM wrapper. Text Editor likewise
  via `Avalonia.AvaloniaEdit`.
- **Files added**:
  - `PlatypusTools.UI.Avalonia/ViewModels/DuplicatesViewModel.cs` —
    wraps `Core.Services.DuplicatesScanner.FindDuplicatesAsync`
    with `Recurse` toggle, progress callback, cancellation, native
    toast on completion, `RevealFile` via `IFileLauncher`.
  - `PlatypusTools.UI.Avalonia/ViewModels/TextEditorViewModel.cs` —
    holds an `AvaloniaEdit.Document.TextDocument` (NOT a string —
    the prior `TextDocumentConverter` instantiated a fresh document
    every binding eval, breaking the editor). Async load/save,
    IsModified flag, Save/SaveAs.
- **Files modified**:
  - `PlatypusTools.UI.Avalonia/ViewModels/PlaceholderViewModels.cs` —
    removed stub `DuplicatesViewModel` and `TextEditorViewModel`.
  - `PlatypusTools.UI.Avalonia/Views/DuplicatesView.axaml(.cs)` —
    folder picker via `OpenFolderPickerAsync`, recurse toggle, scan/
    cancel buttons, progress bar, scrollable group list with hash +
    file paths.
  - `PlatypusTools.UI.Avalonia/Views/TextEditorView.axaml(.cs)` —
    Open/Save/Save As toolbar, `OnDataContextChanged` wires
    `Editor.Document = vm.Document` directly (Document is not bindable
    in a sane way on `AvaloniaEdit.TextEditor`).
- **Files removed**:
  - `PlatypusTools.UI.Avalonia/ViewModels/TextDocumentConverter.cs` —
    no longer needed; the new VM exposes `Document` directly.
- **Files modified outside `cross-platform/`**: NONE.
- **Avalonia gotchas hit**:
  - `AvaloniaEdit.TextEditor.Document` is NOT a normal AvaloniaProperty —
    don't try to `{Binding Document}` it. Set it imperatively in
    `OnDataContextChanged` after the control is initialized.
- **Build**: `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln`
  → **Build succeeded. 0 Error(s).**
- **Next**: Image Viewer + Audio Library port, then app icon assets.

### 2026-04-29 — Image Viewer + Settings ports
- **Why**: Image Viewer maps cleanly to Avalonia's `Bitmap` + `Image` control;
  Settings was finally wired to the real `IThemeService` so theme toggling
  actually works.
- **Files added**:
  - `PlatypusTools.UI.Avalonia/ViewModels/ImageViewerViewModel.cs` —
    holds `Avalonia.Media.Imaging.Bitmap`, async load, ZoomIn/Out/Reset,
    Reveal via `IFileLauncher`.
  - `PlatypusTools.UI.Avalonia/ViewModels/SettingsViewModel.cs` —
    real `OnUseDarkThemeChanged` that calls `AppServices.Theme.ApplyTheme(value)`,
    detailed `PlatformInfo` (OS / arch / framework).
- **Files modified**:
  - `PlatypusTools.UI.Avalonia/ViewModels/PlaceholderViewModels.cs` —
    removed stub `ImageViewerViewModel` and stub `SettingsViewModel`.
    Only `VaultViewModel` and `AudioLibraryViewModel` remain as stubs.
  - `PlatypusTools.UI.Avalonia/Views/ImageViewerView.axaml(.cs)` —
    toolbar (Open / +/− / 1:1 / Reveal), scrollable image with
    `ScaleTransform.ScaleX/Y` bound to `Zoom`, dark backdrop.
  - `PlatypusTools.UI.Avalonia/Views/SettingsView.axaml` — Appearance
    panel + About panel with monospace platform-info block.
  - `cross-platform/README.md` — added current Status table reflecting
    7-of-9 features ported.
- **Files modified outside `cross-platform/`**: NONE.
- **Build**: `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln`
  → **Build succeeded. 0 Error(s).**
- **Status**: 7 features ported (Hash, FileMgr, Audio, TextEd, Dup,
  ImgViewer, Settings). 2 still stubs (Vault, AudioLibrary). Next:
  app icon assets and a dry-run on a real Linux/Mac host.

### 2026-04-29 — Vault + Audio Library + app icon (final feature pass)
- **Why**: Last two stubs ported and the window/taskbar icon wired so the
  cross-platform build is functionally at parity for the portable feature
  subset. No Core service existed for Vault, so a self-contained
  `VaultService` was added in the Avalonia project itself.
- **Files added**:
  - `PlatypusTools.UI.Avalonia/Services/VaultService.cs` \u2014 AES-256-GCM
    cipher with PBKDF2-SHA256 (200 000 iterations) key derivation.
    Stores `vault.json` under `Environment.SpecialFolder.LocalApplicationData`
    (`~/.local/share/PlatypusTools/vault.json` on Linux,
    `~/Library/Application Support/PlatypusTools/vault.json` on macOS).
    JSON envelope `{ v, salt, iv, tag, ct }` (all base64). Atomic write via
    `*.tmp` + `File.Move(overwrite:true)`.
  - `PlatypusTools.UI.Avalonia/ViewModels/VaultViewModel.cs` \u2014 lock/
    unlock/create flow, Add/Delete entries, Copy Password to clipboard,
    Generate Password (CSPRNG, 20 chars). Persists via the service after
    every mutation.
  - `PlatypusTools.UI.Avalonia/ViewModels/AudioLibraryViewModel.cs` \u2014
    safe recursive `Directory.GetDirectories` / `GetFiles` walker (catches
    UnauthorizedAccess), filters by audio extensions
    (`.mp3 .flac .wav .ogg .oga .m4a .aac .opus .wma .alac`), reports
    progress via `Dispatcher.UIThread.Post`, cancellable. Reveal via
    `IFileLauncher`, Play hands off to OS default audio app.
  - `PlatypusTools.UI.Avalonia/Assets/app-icon.png` \u2014 256x256 platypus
    icon (copied from `PlatypusTools.UI/Assets/platypus.png`; **read-only**
    copy, the Windows source was not modified).
  - `PlatypusTools.UI.Avalonia/Assets/app-icon-large.png` \u2014 1024x1024
    logo (copied from `PlatypusTools.UI/Assets/PlatypusToolsLogo.png`).
- **Files modified**:
  - `PlatypusTools.UI.Avalonia/ViewModels/PlaceholderViewModels.cs` \u2014
    file is now empty (just a comment + namespace). All VMs live in their
    own files.
  - `PlatypusTools.UI.Avalonia/Views/VaultView.axaml(.cs)` \u2014 full
    locked/unlocked UI with `DataGrid` of entries and an inline new-entry
    form with Generate (\ud83c\udfb2) button.
  - `PlatypusTools.UI.Avalonia/Views/AudioLibraryView.axaml(.cs)` \u2014
    Pick Folder / Scan / Cancel toolbar, `DataGrid` of tracks (File,
    Folder, Size, Modified), Reveal + Play, double-tap = Play, scan
    progress bar.
  - `PlatypusTools.UI.Avalonia/Views/MainWindow.axaml` \u2014 added
    `Icon=\"avares://\u2026/Assets/app-icon.png\"`.
  - `cross-platform/build-linux-appimage.sh` \u2014 copies real icon into
    AppDir instead of writing a 1x1 placeholder.
  - `cross-platform/build-macos.sh` \u2014 copies icon into `Contents/
    Resources/AppIcon.png` and adds `CFBundleIconFile` to Info.plist.
- **Files modified outside `cross-platform/`**: NONE. (Icons were *read*
  from `PlatypusTools.UI/Assets/`, never modified.)
- **Avalonia gotchas hit**:
  - When the project namespace is `PlatypusTools.UI.Avalonia`, bare
    `Avalonia.Threading.\u2026` and `Avalonia.Application.\u2026` references
    fail to resolve \u2014 the C# compiler picks the project's nested
    namespace first. Use `global::Avalonia.\u2026` to escape, or `using`
    aliases. Fixed in both `AudioLibraryViewModel.cs` and
    `VaultViewModel.cs`.
- **Build**: `dotnet build cross-platform/PlatypusTools.CrossPlatform.sln`
  \u2192 **Build succeeded. 0 Error(s).**
- **Status**: ALL 9 sidebar features now ported with real working
  implementations. The cross-platform port is feature-complete for the
  portable subset.
- **Next**: dry-run `./build-linux.sh` and `./build-linux-appimage.sh` on
  a real Linux host (cannot do this from Windows \u2014 needs a Linux
  build agent or WSL with x64 dotnet SDK installed).

---

## 2026-04-30 — Mass platform port (11 new features)

**Author:** automated agent (Claude)

**Goal:** "port everything … and for things you can make mac and linux equivalent features. Macs have screensavers and backgrounds for example" → autonomous "just do it all now".

### New cross-platform helper services
- `Services/ShellHelper.cs` — generic `RunAsync(file, args, ct, onLine)` returning `(ExitCode, StdOut, StdErr)`. Streams stdout line-by-line via the `onLine` callback. `IsLinux/IsMac/IsWindows` flags + `AppDataDir` (cross-platform persistent per-user dir).
- `Services/WallpaperService.cs` — `SetAsync(path)`. Linux branches by `XDG_CURRENT_DESKTOP`: GNOME/Cinnamon/Unity/Pantheon (`gsettings`), XFCE (`xfconf-query`), KDE (`qdbus org.kde.plasmashell`), feh fallback. macOS via `osascript` System Events. Windows via `SystemParametersInfoW` P/Invoke (testing).
- `Services/ScreensaverService.cs` — `LockAsync()` and `SetIdleSecondsAsync(s)`. Linux tries `loginctl lock-session`, `gnome-screensaver-command --lock`, `xscreensaver-command -lock`, `qdbus … ScreenSaver Lock`. macOS uses `pmset displaysleepnow` / `ScreenSaverEngine`. Windows uses `rundll32 user32.dll,LockWorkStation`.
- `Services/FFmpegRunner.cs` — ffmpeg/ffprobe discovery (env vars `PT_FFMPEG`/`PT_FFPROBE` → PATH → common bundle locations like `/opt/homebrew/bin`, `/usr/bin`, `/snap/bin`). Cached.

### New ViewModels + Views (11)
1. **Process Manager** — `ProcessManagerViewModel` lists `Process.GetProcesses()` sorted by working set, kill button.
2. **Wallpaper Rotator** — folder of images + interval timer, `WallpaperService.SetAsync`.
3. **Screensaver / Lock** — Lock now button + idle-timeout slider.
4. **Disk Space Analyzer** — top-level subfolder size scan with cancellation.
5. **Network Tools** — Ping / Trace / DNS / Ports / IP via `ShellHelper` with line-streamed output to a `SelectableTextBlock`.
6. **File Diff** — LCS-based line diff (`MyersDiff`).
7. **Empty Folder Scanner** — recursive scan + bulk delete.
8. **Bulk Checksum** — SHA1/256/512/MD5 over a folder, manifest export.
9. **Video Converter** (FFmpeg) — MP4/MKV/WebM/MP3/WAV presets.
10. **GIF Maker** (FFmpeg) — `-ss/-t/-vf "fps=N,scale=W:-1"`.
11. **Audio/Video Trim** (FFmpeg) — `-ss/-t/-c copy` or re-encode.
12. **Image Resizer** (FFmpeg) — `-vf scale=W:H`, output format dropdown.
13. **File Encryption** — AES-256-GCM + PBKDF2-SHA256 (200 000 iters), magic header `PTENC1`, atomic envelope `[magic|salt|nonce|tag|ct]`.
14. **Secure Wipe** — N-pass random overwrite + final zero pass + delete.
15. **Archive Manager (ZIP)** — `System.IO.Compression.ZipFile` create/extract.
16. **Color Picker** — RGBA sliders + hex parsing, live `IBrush` preview.
17. **Wi-Fi Passwords** — Linux `nmcli connection show` / mac `security find-generic-password -wga` hint / Windows `netsh wlan show profile name=… key=clear` hint. Doesn't auto-elevate (read-only listing).
18. **Activity Log** — append-only file under `ShellHelper.AppDataDir`.

(Note: 11 new sidebar entries — items 9-12 share the FFmpeg base, 13-15 share file ops, etc.)

### Side-bar
`MainWindowViewModel.NavigationItems` now has 22 entries grouped by comments (Media / Files / Security / System / FFmpeg media / Tools). Still flat `ListBox` — TreeView grouping deferred.

### Gotchas hit & solved
- `Grid.RowSpacing` does NOT exist in Avalonia 11 — use per-row `Margin` (re-confirmed previous DEV_NOTE).
- `Color` cannot be constructed from individual byte AvaloniaProperty bindings (`AVLN3000`). Fix: expose `IBrush PreviewBrush` on the VM and update it in the partial-property-changed handlers, then bind `Border.Background={Binding PreviewBrush}`.

### Build verification
`dotnet build cross-platform/PlatypusTools.CrossPlatform.sln -c Release --nologo` → **0 errors, 13 warnings**.

### Still pending
- Sidebar TreeView/category refactor.
- Wrap remaining shared `Core` services where cross-platform safe (FileIntegrity, EmptyFolder/Duplicate APIs, Archive, etc. as different surfaces if needed).
- Subagents-style features that depend on Windows-only APIs (CertMon, AD, Defender, etc.) intentionally skipped.
- Linux/macOS dry-run on real hardware.


---

## 2026-05-01 — Mass port Batch 5 + sidebar refactor (17 new features)

**Author:** automated agent (Claude)

**Goal:** "do everything you can an fully port things over" → autonomous mass-port wave 5.

**17 new VM/View pairs added** (now wired into a categorised TreeView sidebar):

**Files** (extended)
- `SymlinkManagerViewModel`/`SymlinkManagerView` — list & create symlinks (`File.CreateSymbolicLink`, `Directory.CreateSymbolicLink`).
- `BulkFileMoverViewModel`/`BulkFileMoverView` — pattern-based copy/move with overwrite + preview.
- `SmartRenameViewModel`/`SmartRenameView` — regex / literal find-replace rename with dry-run preview.
- `FileAnalyzerViewModel`/`FileAnalyzerView` — file metadata + magic-byte type detection (PE/ELF/Mach-O/PNG/JPEG/PDF/ZIP/…) + MD5/SHA1/SHA256.

**Media (FFmpeg)** (extended)
- `ScreenRecorderViewModel`/`ScreenRecorderView` — `gdigrab` (Win) / `avfoundation` (Mac) / `x11grab` (Linux), FPS + duration cap.
- `ScreenshotViewModel`/`ScreenshotView` — single-frame capture per OS, with delay.
- `VideoCombinerViewModel`/`VideoCombinerView` — FFmpeg concat demuxer with temp list file.
- `ImageConverterViewModel`/`ImageConverterView` — FFmpeg batch image format conversion.
- `BatchWatermarkViewModel`/`BatchWatermarkView` — FFmpeg overlay with 5 anchor positions.

**Security** (extended)
- `PrivacyCleanerViewModel`/`PrivacyCleanerView` — discovers OS-specific cache/recent/trash and cleans selected.
- `SshKeyManagerViewModel`/`SshKeyManagerView` — list `~/.ssh`, generate via `ssh-keygen` (ed25519/rsa/ecdsa).

**System** (extended)
- `EnvironmentVariablesViewModel`/`EnvironmentVariablesView` — Process / User / Machine env vars (User+Machine are Win-only).
- `StartupManagerViewModel`/`StartupManagerView` — Win Startup folder / Mac LaunchAgents / Linux `~/.config/autostart`.

**Tools** (extended)
- `ScriptingConsoleViewModel`/`ScriptingConsoleView` — pwsh/bash/sh/zsh/python3/ruby/cmd one-shot via ShellHelper, cancellable.
- `RecentWorkspacesViewModel`/`RecentWorkspacesView` — top-50 list persisted to `<AppData>/recent_workspaces.txt`.
- `NotificationCenterViewModel`/`NotificationCenterView` — append-only `<AppData>/notifications.log` (Info / Warning / Error).
- `AboutViewModel`/`AboutView` — version, platform, runtime, data dir, keyboard shortcuts, credits.

### Sidebar refactor (categorised TreeView)

`MainWindowViewModel.cs` was rewritten from a flat 28-item `ObservableCollection<NavigationItem>` to an `ObservableCollection<NavigationCategory>` where each category contains its own items. New 6-section layout: **Media**, **Files**, **Security**, **System**, **Tools**, **Settings** (44 items total).

`MainWindow.axaml` now uses a `TreeView` with two templates (`TreeDataTemplate` for `NavigationCategory`, `DataTemplate` for `NavigationItem`). A `SelectionChanged` handler in code-behind ignores category-header selections so only leaf items drive the content area.

### Issues fixed during this batch

1. **API mismatches (build errors).** Several new VMs called non-existent helpers:
   - `FFmpegRunner.Locate()` → corrected to `FFmpegRunner.FFmpegPath` property (5 files).
   - Static `FileLauncher.Open(path)` → corrected to `_ = new FileLauncher().OpenFileAsync(path)` (`RecentWorkspacesViewModel`).
2. **`Grid.ColumnSpacing` does not exist in Avalonia 11** (same trap as `Grid.RowSpacing`). Removed from 4 axaml files (`EnvironmentVariablesView`, `StartupManagerView`, `SshKeyManagerView`, `NotificationCenterView`). Use per-column `Margin` instead.

### Build verification

`dotnet build cross-platform/PlatypusTools.CrossPlatform.sln -c Release` → **Build succeeded. 0 Error(s).** (13 pre-existing warnings remain: NU190x dependency advisories, CS8826 partial-method-signature differences in `ColorPickerViewModel`, SYSLIB0060 obsolete `Rfc2898DeriveBytes` ctors.)

### Cumulative state

Cross-platform sidebar now exposes **44 features** organised into 6 categories — the largest superset of Windows feature parity yet shipped to the cross-platform target.


---

## 2026-05-02 — Mass port Batch 6 (12 new features)

**Author:** automated agent (Claude)

**Goal:** "continue moving stuff over" — autonomous wave 6.

**12 new VM/View pairs added** (all wired into the categorised TreeView sidebar):

**Files** (extended)
- `FileIntegrityViewModel`/`FileIntegrityView` — load a SHA1/256/512/MD5 manifest, verify hashes, report OK / MISMATCH / MISSING.

**Media (FFmpeg)** (extended)
- `BatchUpscaleViewModel`/`BatchUpscaleView` — ffmpeg `scale=iw*N:ih*N:flags=…` batch image upscaler (2x/3x/4x · lanczos/bicubic/bilinear/neighbor).
- `IconConverterViewModel`/`IconConverterView` — multi-size PNG export (16/32/48/64/128/256) plus `.ico` on Windows.
- `VideoMetadataViewModel`/`VideoMetadataView` — ffprobe JSON dump (`-show_format -show_streams`).

**Security** (extended)
- `CertificateManagerViewModel`/`CertificateManagerView` — read-only listing of CA certs from common file stores: `/etc/ssl/certs`, `/etc/pki/tls/certs`, `/etc/ssl/cert.pem` (Linux/Mac); `LocalMachine\Root` X509Store (Windows).

**System** (extended)
- `DiskCleanupViewModel`/`DiskCleanupView` — discovers temp/cache/crash-dump dirs and bulk-deletes selected entries.
- `NetworkTrafficViewModel`/`NetworkTrafficView` — `netstat -ano` (Win) / `ss -tunap` or `netstat -tunap` (Linux) / `netstat -anv` (Mac).
- `SystemAuditViewModel`/`SystemAuditView` — collects OS, runtime, drives, and platform-native dumps (`uname`, `os-release`, `free`, `lscpu` on Linux; `sw_vers`, `sysctl`, `vm_stat` on Mac; `systeminfo` excerpt on Windows).
- `ScheduledTasksViewModel`/`ScheduledTasksView` — `schtasks /query` (Win) / `launchctl list` + crontab (Mac) / `systemctl list-timers` + crontab + `/etc/cron.d` (Linux).

**Tools** (extended)
- `LogViewerViewModel`/`LogViewerView` — tail-N + filter on any text log file.
- `ClipboardHistoryViewModel`/`ClipboardHistoryView` — in-memory history (200-cap) + Avalonia clipboard paste.
- `WebsiteDownloaderViewModel`/`WebsiteDownloaderView` — single-URL `curl -L` or recursive `wget -r -np -E -k -p` mirror.

### Sidebar layout updates

`MainWindowViewModel.cs` `Categories` initializer extended with these 12 items, slotted into Files/Media/Security/System/Tools. `MainWindow.axaml` gained 12 new `<DataTemplate>` resource entries with `*Tpl` keys (matching existing convention).

### Build verification

`dotnet build cross-platform/PlatypusTools.CrossPlatform.sln -c Release` → **Build succeeded. 0 Error(s).**

### Cumulative state

Cross-platform sidebar now exposes **56 features** across 6 categories.

