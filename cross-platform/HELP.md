# PlatypusTools (Cross-Platform) — User Help

This is the end-user help for the Avalonia build of PlatypusTools, which runs
on Windows, Linux, and macOS. 113 features are organised into 7 sidebar
categories. Use the search box at the top of the sidebar to filter.

> **Tip:** Press `Ctrl+K` to focus the sidebar filter from anywhere, or open
> the Command Palette from the Home category to jump to any feature by name.

---

## Categories at a glance

| Category | What's in it |
|----------|--------------|
| **Home**     | Dashboard, Command Palette, Changelog, Keyboard Shortcuts |
| **Media**    | Audio/video/image conversion, recording, players, native multimedia editors, 3D editors, upscaling |
| **Files**    | File manager, duplicates, diff, checksums, sync, archives, PDF, metadata |
| **Security** | Hashing, vault, encryption, secure wipe, certificates, CVE/YARA, forensics, LDAP analyzer |
| **System**   | Process/disk/network tools, audit, scheduled tasks, screensaver, Plex/Intune backup, service manager |
| **Tools**    | Editors, terminal, FTP, mail, AI assistant, workflow designer, fleet view, plugin marketplace |
| **Settings** | Settings panel, About / Help |

---

## External dependencies

PlatypusTools is intentionally lightweight — instead of bundling huge
binaries, several features shell out to standard tools. Install only the ones
you need.

| Tool | Used by | Linux | macOS | Windows |
|------|---------|-------|-------|---------|
| `ffmpeg` / `ffprobe` | All Media features (convert, trim, GIF, screen rec, upscale, native edits) | `apt install ffmpeg` | `brew install ffmpeg` | `winget install Gyan.FFmpeg` |
| `libvlc` | Audio Player, Native Video Player | `apt install libvlc-dev vlc` | bundled (NuGet `VideoLAN.LibVLC.Mac`) | bundled with VLC install |
| `yara` | YARA Scanner | `apt install yara` | `brew install yara` | https://virustotal.github.io/yara/ |
| `smartctl` | Disk Health | `apt install smartmontools` | `brew install smartmontools` | smartmontools-win |
| `dd` | Bootable USB | built-in | built-in | use Rufus / Win32DiskImager |
| `ldapsearch` | Directory Analyzer | `apt install ldap-utils` | `brew install openldap` | OpenLDAP for Windows |
| `systemctl` / `launchctl` / `sc` | Service Manager | built-in | built-in | built-in |
| `mstsc` / `xfreerdp` / `vncviewer` / `ssh` | Remote Desktop | `apt install freerdp2-x11 tigervnc-viewer openssh-client` | `brew install freerdp tigervnc` | built-in |
| `gsettings` / `qdbus` / `xfconf-query` / `nmcli` | Wallpaper, Wi-Fi, Screensaver (Linux DE) | preinstalled on most desktop distros | n/a | n/a |
| Shotcut / Kdenlive / OpenShot / DaVinci / iMovie / Audacity / Blender / GIMP / Inkscape / FreeCAD / MeshLab / OpenSCAD | Multimedia Editor & 3D Editor | optional, distro pkg manager | `brew install --cask …` | winget / vendor installer |

The app discovers FFmpeg via these locations in order:
1. `PT_FFMPEG` / `PT_FFPROBE` environment variables (explicit override)
2. System `PATH`
3. Common locations: `/opt/homebrew/bin`, `/usr/local/bin`, `/usr/bin`, `/snap/bin`, `C:\Program Files\…`

---

## Feature-by-feature notes

### Home
- **Dashboard** — overview tiles + recent activity.
- **Command Palette** — type to fuzzy-jump to any sidebar feature.
- **Changelog** — release notes shipped with the build.
- **Keyboard Shortcuts** — full key map.

### Media
- **Audio Player / Audio Library** — LibVLC-based playback, playlist, library scan.
- **Image Viewer / Resizer / Converter / Icon Converter / Batch Watermark / Batch Upscale** — pure-managed image pipeline (no external deps for resize/convert; FFmpeg used for batch upscale).
- **Video Converter / Combiner / GIF Maker / Trim / Metadata** — FFmpeg-powered.
- **Screen Recorder / Screenshot** — FFmpeg `gdigrab` (Win), `x11grab` (Linux), `avfoundation` (Mac).
- **QR Code Generator** — QRCoder (no deps).
- **Audio Transcription** — Whisper.cpp (binary auto-detected on PATH).
- **Native Audio Trim / Native Image Edit / Native Video Player / Upscaler / Media Hub / Media Library** — purpose-built FFmpeg-only equivalents that don't need third-party editors.
- **Multimedia Editor / 3D Editor / Shotcut Editor** — launches whatever you have installed (Shotcut/Kdenlive/OpenShot/DaVinci/iMovie/Audacity/Blender/GIMP/Inkscape/FreeCAD/MeshLab/OpenSCAD). The buttons are disabled if the binary isn't found, otherwise they open the host program with your selected file.

### Files
- **File Manager / Duplicates / Empty Folder Scan / File Diff / Bulk Checksum / Bulk File Mover / Smart Rename / Symlink Manager / File Analyzer / File Integrity / Archive (ZIP) / PDF Tools / File Sync / Metadata Editor / Cloud Sync** — operate on the local filesystem; Cloud Sync uses rclone if installed.

### Security
- **Hash Scanner / File Encryption / Secure Wipe / Privacy Cleaner / Certificate Manager / SSH Key Manager / Vault / Hash Scanner** — managed crypto + per-OS shell-outs.
- **Wi-Fi Passwords** — `nmcli` (Linux), `security` (Mac), `netsh wlan` (Win).
- **CVE Search** — NVD REST API 2.0 (no API key required).
- **YARA Scanner** — invokes `yara` with rule file + target.
- **Forensics Analyzer / Advanced Forensics** — magic-byte detection, multi-hash, ASCII strings extraction, Shannon entropy.
- **Credential Manager** — `cmdkey` / `security` / `secret-tool` per OS for generic creds.
- **Encrypted Clipboard** — AES-256-CBC round-trip with SHA-256(password) key derivation.
- **Directory Analyzer** *(NEW)* — generic LDAP query tool. Works against AD, OpenLDAP, FreeIPA via `ldapsearch`. Includes 6 prebuilt security filter presets:
  1. All users
  2. Enabled users only (`userAccountControl` bit `:!2`)
  3. Password-never-expires accounts (UAC bit `65536`)
  4. Admin-tagged groups (`adminCount=1`)
  5. Server computers
  6. Domain Admins membership

### System
- **Process Manager / Disk Space / Disk Cleanup / Network Tools / Network Traffic / System Audit / Scheduled Tasks / Environment Vars / Startup Manager / Dependencies / Disk Health / Bootable USB / Remote Desktop** — per-OS shell-outs to the platform-native tooling.
- **Wallpaper Rotator / Screensaver / Lock** — uses `gsettings`/`qdbus`/`xfconf-query` (Linux), `osascript` (Mac), Win32 (Win).
- **Screensaver Config** — persists idle timeout, lock-on-resume, image folder, slideshow interval, shuffle to `~/.config/PlatypusTools/settings.json`.
- **Slideshow Saver** — opens a fullscreen black-background window cycling images from your configured folder; press `Esc` or click to dismiss.
- **Reboot Analyzer** — `wevtutil` (Win) / `last reboot` (Mac) / `who -b` + `last -x` (Linux).
- **Recent Cleanup / File Cleaner** — clears OS jump-lists / pattern-matched temp files.
- **System Hardening** — read-only audit of firewall / Defender / Gatekeeper / SIP / UFW / sshd / sudoers.
- **System Restore** — lists existing restore points / Time Machine snapshots / snapper / timeshift backups.
- **Plex Backup** — zips the OS-specific Plex config dir.
- **Intune Backup** *(any OS)* — paste a Microsoft Graph access token and the app makes paginated GETs against 10 endpoints (apps, configs, compliance, groups, …) and writes one JSON file per endpoint. No SDK; pure HTTPS.
- **Update Checker** — checks the configured GitHub releases endpoint for a newer build.
- **Service Manager** *(NEW)* — list, filter, start, stop, restart any service. Backed by `systemctl` / `launchctl` / `sc` per OS. The selected row's first whitespace-token is used as the service/unit name.

### Tools
- **Color Picker / Text Editor / Log Viewer / Clipboard History / Recent Workspaces / Notification Center / Activity Log** — local utilities.
- **Website Downloader** — uses `wget`/`curl` if available.
- **Scripting Console** — runs `pwsh` / `bash` / `python3`.
- **Terminal** — embedded terminal via `pwsh` / `bash`.
- **FTP Client** — FluentFTP.
- **Mail Sender / Mail Client (IMAP)** — MailKit 4.16. Mail Client previews the last 25 messages in your INBOX, read-only.
- **Batch Job Queue / Export Queue / Workflow Designer** — generic shell-command queues.
- **Plugin Manager / Plugin Marketplace** — local plugin folder + remote registry JSON fetch.
- **Simple Browser** — hands a URL to `xdg-open` / `open` / `cmd /c start`.
- **AI Assistant** — OpenAI-compatible HTTP client (`POST /v1/chat/completions`). Configure endpoint, API key, and model in Settings.
- **Fleet View / Remote Dashboard** — host list with ICMP probe + `ssh -o BatchMode=yes uptime` per host.
- **Global Search** — recursive substring grep across a folder tree.
- **Theme Builder** — generates an Avalonia `ResourceDictionary` from accent/bg/fg colors.

### Settings
- **Settings** — theme (light/dark), AI endpoint/key/model, screensaver options, update repo, last-open page, etc.
- **About / Help** — version + this document.

---

## Skipped Windows-only features (intentional)

These exist in the original Windows WPF build but have no cross-platform
analog and are deliberately **not** present in the Avalonia build:

| Feature | Reason |
|---------|--------|
| `WindowsUpdateRepairView` | `wuauclt` / `DISM` / `SFC` are Windows-only. |
| `RegistryCleanerView`     | The Windows registry doesn't exist on Linux/Mac. |

If you need either of these, run the original WPF build on Windows.

---

## Settings file location

| OS      | Path |
|---------|------|
| Windows | `%APPDATA%\PlatypusTools\settings.json` |
| Linux   | `~/.config/PlatypusTools/settings.json` |
| macOS   | `~/Library/Application Support/PlatypusTools/settings.json` |

Delete this file to reset to defaults. AI keys, mail credentials, and
encrypted-clipboard passwords are **never** stored here — only non-secret
preferences.

---

## Reporting issues

File issues at the project repository. Useful information to include:

- OS + version (`uname -a` on Unix, `winver` on Windows)
- App version (Settings → About)
- Output of any failing feature's status bar
- Whether the relevant external tool from the dependency table is on `$PATH`
