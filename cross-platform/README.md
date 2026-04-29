# PlatypusTools — Cross-Platform Port (Linux / macOS)

This folder is an **independent Avalonia port** of PlatypusTools. **The Windows
codebase under `PlatypusTools.UI/` is intentionally untouched.** This port
references the existing `PlatypusTools.Core` library (which is already
`net10.0` and largely cross-platform) and provides a separate Avalonia 11
shell for Linux and macOS.

## Layout

```
cross-platform/
├── PlatypusTools.CrossPlatform.sln       # Independent solution
├── PlatypusTools.UI.Avalonia/            # Avalonia 11 app
│   ├── App.axaml(.cs)
│   ├── Program.cs
│   ├── ViewModels/
│   ├── Views/
│   ├── Services/                         # Cross-platform shims (Trash, Open, etc.)
│   └── Assets/
├── build-linux.sh                        # Builds linux-x64 self-contained + .tar.gz
├── build-linux-appimage.sh               # Builds .AppImage
├── build-macos.sh                        # Builds osx-x64 + osx-arm64 .app bundles
└── build-portable.sh                     # Single-file portable for current host
```

## Feature subset

Ported (or stubbed and ready to wire):
- Audio Player + visualizer
- File Manager / Duplicates / Hash Scanner
- Text Editor
- Vault (password manager)
- Image Viewer / thumbnails
- Settings / themes
- Audio library scan

**Not ported** (Windows-only and replaced with friendly "unavailable on this
platform" stubs):
- AD Security Analyzer / GPO / Domain Join
- Wallpaper Rotator (Win32 desktop API)
- Slideshow Screensaver (.scr)
- MSI installer machinery
- Toast notifications via WinRT
- Cloudflare Tunnel manager
- Tailscale manager (uses native Tailscale CLI)

## Mac/Linux-specific extras
- Native dark-mode follow (Avalonia FluentTheme)
- Trash via `gio trash` (Linux) / `osascript` Finder (macOS)
- Open-with via `xdg-open` / `open`
- Drag-and-drop (Avalonia native)

## Build

### Linux (self-contained x64)
```bash
cd cross-platform
./build-linux.sh
# output: cross-platform/dist/PlatypusTools-linux-x64.tar.gz
```

### Linux (AppImage)
```bash
./build-linux-appimage.sh
# output: cross-platform/dist/PlatypusTools-x86_64.AppImage
```

### macOS
```bash
cd cross-platform
./build-macos.sh
# output: cross-platform/dist/PlatypusTools-osx-x64.app, PlatypusTools-osx-arm64.app
```

### Run from source
```bash
cd cross-platform/PlatypusTools.UI.Avalonia
dotnet run
```

## Versioning
Version stays in lock-step with the Windows release (read from
`Directory.Build.props` at the repo root).

## Status (2026-05-03)

### Original 9 sidebar features
| Feature       | Status      | Notes                                                       |
| ------------- | ----------- | ----------------------------------------------------------- |
| Hash Scanner  | ✅ Ported    | SHA256/SHA1/MD5/SHA512, multi-file, progress, cancellation. |
| File Manager  | ✅ Ported    | Navigate, Open, Reveal, Trash (xdg / Finder).               |
| Audio Player  | ✅ Ported    | LibVLCSharp; Linux needs system libvlc.                     |
| Text Editor   | ✅ Ported    | AvaloniaEdit; line numbers, monospace.                      |
| Duplicates    | ✅ Ported    | Wraps `Core.DuplicatesScanner`; recursive, cancellable.     |
| Image Viewer  | ✅ Ported    | Avalonia native bitmap; pan + zoom.                         |
| Settings      | ✅ Ported    | Light/dark theme switch, platform info.                     |
| Vault         | ✅ Ported    | AES-256-GCM + PBKDF2-SHA256 (200k iters); CSPRNG generator. |
| Audio Library | ✅ Ported    | Recursive scan, filter by audio ext, reveal, play.          |

### Mass-port batch (April 30 — 11 new features)
| Feature             | Status   | Mac/Linux equivalent                                                                                    |
| ------------------- | -------- | ------------------------------------------------------------------------------------------------------- |
| Process Manager     | ✅ Ported | `Process.GetProcesses()` + `Process.Kill()` (cross-platform .NET).                                      |
| Wallpaper Rotator   | ✅ Ported | Linux: `gsettings`/`xfconf-query`/`qdbus`/`feh`. macOS: `osascript` System Events. Windows: P/Invoke.   |
| Screensaver / Lock  | ✅ Ported | Linux: `loginctl`/`gnome-screensaver-command`/`xscreensaver`/`qdbus`. macOS: `pmset`. Windows: rundll32.|
| Disk Space Analyzer | ✅ Ported | Top-level subfolder size scan, cancellable.                                                             |
| Network Tools       | ✅ Ported | Ping/Trace/DNS/Ports/IP cmds vary by OS but VM picks the right one.                                     |
| File Diff           | ✅ Ported | Pure-managed LCS line diff.                                                                             |
| Empty Folder Scanner| ✅ Ported | Recursive find + bulk delete.                                                                           |
| Bulk Checksum       | ✅ Ported | SHA1/256/512/MD5 with manifest export.                                                                  |
| Video Converter     | ✅ Ported | FFmpeg presets (MP4/MKV/WebM/MP3/WAV).                                                                  |
| GIF Maker           | ✅ Ported | FFmpeg `fps=N,scale=W:-1`.                                                                              |
| Audio / Video Trim  | ✅ Ported | FFmpeg `-ss/-t`, copy-or-reencode.                                                                      |
| Image Resizer       | ✅ Ported | FFmpeg `-vf scale=W:H`.                                                                                 |
| File Encryption     | ✅ Ported | AES-256-GCM + PBKDF2-SHA256 200k, magic-header `PTENC1`.                                                |
| Secure Wipe         | ✅ Ported | N-pass random + zero-pass, then delete.                                                                 |
| Archive (ZIP)       | ✅ Ported | `System.IO.Compression.ZipFile`.                                                                        |
| Color Picker        | ✅ Ported | RGBA sliders, hex parse, live brush preview.                                                            |
| Wi-Fi Passwords     | ✅ Ported | Linux: `nmcli`. macOS: `security find-generic-password`. Windows: `netsh wlan show profile`.            |
| Activity Log        | ✅ Ported | Append-only file under `~/.local/share/PlatypusTools/`.                                                 |

### Mass-port batch 5 (May 1 — 17 new features)
| Feature              | Status   | Mac/Linux equivalent                                                                                  |
| -------------------- | -------- | ----------------------------------------------------------------------------------------------------- |
| Symlink Manager      | ✅ Ported | `File.CreateSymbolicLink` / `Directory.CreateSymbolicLink`; lists reparse points across all 3 OSes.   |
| Bulk File Mover      | ✅ Ported | Pattern-based copy/move with overwrite + preview (managed `System.IO`).                               |
| Smart Rename         | ✅ Ported | Regex / literal find-replace with dry-run preview.                                                    |
| File Analyzer        | ✅ Ported | Magic-byte type detection (PE/ELF/Mach-O/PNG/JPEG/PDF/ZIP/…) + MD5/SHA1/SHA256.                       |
| Screen Recorder      | ✅ Ported | FFmpeg `gdigrab` (Win) / `avfoundation` (Mac) / `x11grab` (Linux), FPS + duration cap.                |
| Screenshot           | ✅ Ported | FFmpeg single-frame capture per OS, with delay.                                                       |
| Video Combiner       | ✅ Ported | FFmpeg concat demuxer with temp list file.                                                            |
| Image Converter      | ✅ Ported | FFmpeg batch image format conversion (png/jpg/webp/bmp/tiff/gif).                                     |
| Batch Watermark      | ✅ Ported | FFmpeg overlay with 5 anchor positions (TL/TR/BL/BR/center).                                          |
| Privacy Cleaner      | ✅ Ported | Discovers OS-specific cache/recent/trash; cleans selected.                                            |
| SSH Key Manager      | ✅ Ported | List `~/.ssh`, generate via `ssh-keygen` (ed25519/rsa/ecdsa); cross-platform.                         |
| Environment Vars     | ✅ Ported | Process / User / Machine env vars (User+Machine = Win-only; Process scope works on all OSes).         |
| Startup Manager      | ✅ Ported | Win Startup folder (.bat) / Mac LaunchAgents (.plist) / Linux `~/.config/autostart` (.desktop).       |
| Scripting Console    | ✅ Ported | pwsh/bash/sh/zsh/python3/ruby/cmd one-shot via `ShellHelper`, cancellable.                            |
| Recent Workspaces    | ✅ Ported | Top-50 list persisted to `<AppData>/recent_workspaces.txt`.                                           |
| Notification Center  | ✅ Ported | Append-only `<AppData>/notifications.log` (Info / Warning / Error).                                   |
| About / Help         | ✅ Ported | Version, platform, runtime, data dir, keyboard shortcuts, credits.                                    |

**Sidebar refactor:** the flat list was replaced with a categorised `TreeView` grouped into **Media · Files · Security · System · Tools · Settings**.

### Mass-port batch 6 (May 2 — 12 new features)
| Feature              | Status   | Mac/Linux equivalent                                                                                  |
| -------------------- | -------- | ----------------------------------------------------------------------------------------------------- |
| File Integrity       | ✅ Ported | Verifies a SHA1/256/512/MD5 manifest; flags OK / MISMATCH / MISSING.                                  |
| Batch Upscale        | ✅ Ported | FFmpeg `scale=iw*N:ih*N:flags=…` (2x/3x/4x · lanczos/bicubic/bilinear/neighbor).                       |
| Icon Converter       | ✅ Ported | Multi-size PNG export (16/32/48/64/128/256) + `.ico` on Windows via FFmpeg.                           |
| Video Metadata       | ✅ Ported | `ffprobe -show_format -show_streams -print_format json`.                                              |
| Certificate Manager  | ✅ Ported | Reads `/etc/ssl/certs` etc. on Linux/Mac; `LocalMachine\Root` X509Store on Windows.                    |
| Disk Cleanup         | ✅ Ported | Discovers temp/cache/crash-dump dirs per OS and bulk-deletes selected entries.                        |
| Network Traffic      | ✅ Ported | `netstat -ano` / `ss -tunap` / `netstat -anv` per OS.                                                 |
| System Audit         | ✅ Ported | OS + runtime + drives + native dumps (`uname`, `sw_vers`, `systeminfo`).                              |
| Scheduled Tasks      | ✅ Ported | `schtasks` / `launchctl + crontab` / `systemctl list-timers + crontab + /etc/cron.d`.                 |
| Log Viewer           | ✅ Ported | Tail-N + substring filter on any text log file.                                                       |
| Clipboard History    | ✅ Ported | In-memory 200-cap history + Avalonia clipboard paste.                                                 |
| Website Downloader   | ✅ Ported | `curl -L` single URL or `wget -r -np -E -k -p` full-site mirror.                                      |

**Total: 56 sidebar features ported across 6 categories.** App icon wired into the main window.

### Mass-port batch 7 + polish (May 3 — 9 new features)
| Feature              | Status   | Mac/Linux equivalent                                                                                  |
| -------------------- | -------- | ----------------------------------------------------------------------------------------------------- |
| Dashboard (Home)     | ✅ Ported | Tile grid jumping to common features by VM type lookup.                                               |
| QR Code Generator    | ✅ Ported | `QRCoder` (managed) → PNG; preview + Save-As.                                                         |
| PDF Tools            | ✅ Ported | Wraps `qpdf` (merge / split / rotate / encrypt / decrypt). Cross-platform CLI.                        |
| FTP Client           | ✅ Ported | `FluentFTP.AsyncFtpClient`; list / download arbitrary remote paths.                                   |
| Terminal             | ✅ Ported | pwsh / bash / zsh / sh / cmd one-shot via `ShellHelper`; live transcript + cancel.                    |
| File Sync            | ✅ Ported | `rsync -avh [--delete] [--dry-run]` on Linux/Mac; `robocopy /MIR /L` on Windows.                      |
| Metadata Editor      | ✅ Ported | `exiftool` read (`-a -G1`) and write (`-Tag=Value -overwrite_original`).                              |
| Mail Sender (SMTP)   | ✅ Ported | `MailKit.Net.Smtp` + `MimeKit`; STARTTLS support, plain-text body.                                    |
| Dependencies         | ✅ Ported | Probes ffmpeg / ffprobe / qpdf / exiftool / rsync / ssh-keygen / curl / wget; per-OS install hints.   |

**Polish:**
- Sidebar **search filter** — TextBox above the TreeView filters categories/items live (case-insensitive).
- **Persisted last-selected page** — restored from `<AppData>/settings.json` on launch.
- **Dark / light toggle button** — top-right of sidebar header; persisted to `settings.json`.

**Total: 65 sidebar features ported across 7 categories** (Home / Media / Files / Security / System / Tools / Settings).

### Mass-port batch 8 (April 29 — 16 new features)
| Feature              | Status   | Mac/Linux equivalent                                                                                  |
| -------------------- | -------- | ----------------------------------------------------------------------------------------------------- |
| Command Palette      | ✅ Ported | In-page fuzzy filter across every sidebar entry; jumps via VM type lookup.                            |
| Changelog Viewer     | ✅ Ported | Loads/edits any markdown file (auto-discovers `CHANGELOG.md`).                                        |
| Keyboard Shortcuts   | ✅ Ported | Static reference list rendered as a DataGrid.                                                         |
| Cloud Sync           | ✅ Ported | `rclone sync / bisync` with --dry-run, push / pull / bisync directions.                               |
| Tree-Map Disk        | ✅ Ported | Top-50 entries by size; bar widths driven by % of total (managed `System.IO`).                        |
| Hider                | ✅ Ported | Linux/Mac: rename with leading `.`; Windows: `attrib +H/-H`.                                          |
| Scheduled Backup     | ✅ Ported | Now-archive via `ZipFile.CreateFromDirectory`; daily schedule via `schtasks` or `crontab -l`.         |
| Audio Transcription  | ✅ Ported | `whisper` CLI (OpenAI) with `whisper.cpp` fallback (`main -m ggml-*.bin`).                            |
| CVE Search           | ✅ Ported | NVD REST API 2.0 (`services.nvd.nist.gov/rest/json/cves/2.0`); JSON parsed to grid.                   |
| YARA Scanner         | ✅ Ported | `yara [-r] rules.yar target/`; live transcript + cancel.                                              |
| Forensics Analyzer   | ✅ Ported | Magic-byte detection, MD5/SHA1/SHA256, ASCII strings extraction (no external deps).                   |
| Credential Manager   | ✅ Ported | `cmdkey` / `security` / `secret-tool` per OS for generic creds.                                       |
| Disk Health          | ✅ Ported | `smartctl -a` plus `--scan` listing (smartmontools).                                                  |
| Bootable USB Writer  | ✅ Ported | `dd if=… of=… bs=4M status=progress oflag=sync` on Linux/Mac (Win: defers to Rufus / DiskImager).     |
| Remote Desktop       | ✅ Ported | Launches `mstsc` / `xfreerdp` / `vncviewer` / `ssh` based on protocol pick.                           |
| Plugin Manager       | ✅ Ported | Lists files in `<AppData>/plugins`; opens that folder via the OS.                                     |
| Simple Browser       | ✅ Ported | Hands the URL to the OS default browser (`xdg-open` / `open` / `cmd /c start`).                       |
| Batch Job Queue      | ✅ Ported | Queue arbitrary shell commands; runs sequentially via `pwsh` (Win) or `bash` (Unix).                  |

#### Batch 9 — Multimedia, screensaver/slideshow, Intune, fleet, AI, more

| Feature                | Status   | Implementation Notes                                                                                  |
|------------------------|----------|------------------------------------------------------------------------------------------------------|
| Native Audio Trim      | ✅ Ported | FFmpeg `-ss/-to`; toggle re-encode (libmp3lame VBR) for sample accuracy.                             |
| Native Image Edit      | ✅ Ported | FFmpeg filter chain (`transpose`, `hflip/vflip`, `eq=brightness:contrast`).                          |
| Native Video Player    | ✅ Ported | OS-default + VLC + mpv launchers. Avalonia has no embedded player.                                   |
| Media Hub              | ✅ Ported | Recursive scanner that classifies audio/video/image/document by extension; click ▶ to open.         |
| Media Library          | ✅ Ported | Same scanner with full path column; bulk inventory view.                                            |
| Multimedia Editor      | ✅ Ported | Launches host-installed Shotcut/Kdenlive/OpenShot/DaVinci/iMovie/Audacity/Blender/GIMP/Inkscape.   |
| 3D Editor              | ✅ Ported | Blender / FreeCAD / MeshLab / OpenSCAD launcher.                                                    |
| Shotcut Editor         | ✅ Ported | Direct Shotcut launcher with optional file arg.                                                     |
| Upscaler               | ✅ Ported | FFmpeg `scale=iw*N:ih*N:flags=lanczos` 2×/3×/4×.                                                   |
| Screensaver Config     | ✅ Ported | Persists idle, lock, image folder, interval, shuffle to `settings.json`.                            |
| Slideshow Screensaver  | ✅ Ported | Opens a fullscreen `Window` with `DispatcherTimer` cycling images; Esc/click closes.                |
| Intune Backup          | ✅ Ported | Pure-HTTP Microsoft Graph paginated dump (apps, configs, compliance, groups…) — works on any OS.    |
| Advanced Forensics     | ✅ Ported | Shannon entropy + magic-byte signature (PE/ELF/Mach-O/PDF/ZIP).                                     |
| Mail Client (IMAP)     | ✅ Ported | MailKit IMAP read-only inbox preview (last 25).                                                     |
| Reboot Analyzer        | ✅ Ported | `wevtutil` (Win) / `last reboot` (Mac) / `who -b` + `last -x` (Linux).                              |
| Recent Cleanup         | ✅ Ported | Per-OS recents jump-list cleanup (dry-run by default).                                              |
| File Cleaner           | ✅ Ported | Pattern list (`*.tmp;*.bak;Thumbs.db;.DS_Store`) recursive.                                        |
| System Hardening Audit | ✅ Ported | Per-OS firewall / Defender / Gatekeeper / SIP / UFW / sshd / sudoers checks.                        |
| System Restore         | ✅ Ported | `Get-ComputerRestorePoint` / `tmutil listbackups` / snapper / timeshift listing.                    |
| Plex Backup            | ✅ Ported | `ZipFile.CreateFromDirectory` of platform-specific Plex config dir.                                 |
| Theme Builder          | ✅ Ported | Generates an Avalonia `ResourceDictionary` from accent/bg/fg colors.                                |
| Encrypted Clipboard    | ✅ Ported | AES-256-CBC + SHA-256(password) round-trip with random IV; base64.                                  |
| Export Queue           | ✅ Ported | Generic shell-command queue runner (per-job state).                                                 |
| AI Assistant           | ✅ Ported | OpenAI-compatible `POST /v1/chat/completions`; key/endpoint/model persisted.                       |
| Workflow Designer      | ✅ Ported | Linear workflow with shell/delay/echo step kinds.                                                  |
| Fleet View             | ✅ Ported | Host list with ICMP probe + `ssh -o BatchMode=yes uptime`.                                         |
| Remote Dashboard       | ✅ Ported | Same data as Fleet, rendered as a status grid of cards.                                            |
| Plugin Marketplace     | ✅ Ported | Fetches a JSON registry `[{name,version,url,description}]` over HTTP.                              |
| Global Search          | ✅ Ported | Recursive `File.ReadLines` substring grep with 1000-match cap.                                     |
| Update Checker         | ✅ Ported | GitHub `repos/{owner}/{name}/releases/latest` JSON fetch.                                          |
| Service Manager        | ✅ Ported | `systemctl` / `launchctl` / `sc` list + start/stop/restart of any service.                         |
| Directory Analyzer     | ✅ Ported | LDAP via `ldapsearch` against AD/OpenLDAP/FreeIPA with prebuilt security filter presets.           |

**Total: 113 sidebar features ported across 7 categories.**

Skipped (no cross-platform analog): Windows Update Repair (`DISM`/`SFC`),
Registry Cleaner (Windows registry only).

`dotnet build cross-platform/PlatypusTools.CrossPlatform.sln -c Release` →
**Build succeeded. 0 Error(s).** (Verified on Windows host; full Linux/Mac
artifact builds require running the `build-*.sh` scripts on those hosts.)

External dependencies (Linux/macOS):
- **ffmpeg** — auto-discovered from `PT_FFMPEG`/`PT_FFPROBE` env vars, then `PATH`, then common locations (`/opt/homebrew/bin`, `/usr/bin`, `/snap/bin`). Install with `apt install ffmpeg` / `brew install ffmpeg`.
- **libvlc** — required by Audio Player (Linux: `apt install libvlc-dev` or `vlc`).
- **DE-specific tools** for Wallpaper/Screensaver/Wi-Fi (gsettings, qdbus, xfconf-query, nmcli, loginctl, etc.). Pre-installed on virtually all desktop distros.

See `DEV_NOTES.md` for the chronological developer journal and
`/memories/repo/CROSS_PLATFORM_PORT.md` for the always-loaded reminder rules.
