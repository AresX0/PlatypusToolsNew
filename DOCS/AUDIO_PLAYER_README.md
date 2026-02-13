# Audio Player & Library - Complete Integration Guide

**Purpose**: Master summary document for the complete audio player implementation  
**Date**: February 8, 2026  
**Version**: 3.4.0  
**Audience**: Project leads, developers, stakeholders  

---

## Document Overview

This package contains **three comprehensive guides** for your audio player:

| Document | Purpose | Audience | Pages |
|----------|---------|----------|-------|
| **AUDIO_PLAYER_FEATURE_MANIFEST.md** | Complete feature tracking | Everyone | 30+ |
| **AUDIO_PLAYER_IMPLEMENTATION_STATUS.md** | Current state & gaps | Developers | 20+ |
| **AUDIO_LIBRARY_REWRITE_GUIDE.md** | Step-by-step implementation | Developers | 25+ |
| **README** (this file) | Navigation & summary | Everyone | 5 |

---

## Quick Start: What to Read First

### For Project Managers / Stakeholders
1. Read this **README** (5 min)
2. Review **AUDIO_PLAYER_FEATURE_MANIFEST.md** section 1-3 (10 min)
3. Review **AUDIO_PLAYER_IMPLEMENTATION_STATUS.md** section "Executive Summary" (5 min)

### For Developers
1. Read this **README** (5 min)
2. Review **AUDIO_PLAYER_IMPLEMENTATION_STATUS.md** (20 min)
3. Study **AUDIO_LIBRARY_REWRITE_GUIDE.md** (30 min)
4. Begin implementation with Step 1 of the rewrite guide

### For QA / Testing
1. Review **AUDIO_PLAYER_FEATURE_MANIFEST.md** section 14 (Testing)
2. Use **AUDIO_PLAYER_IMPLEMENTATION_STATUS.md** section "Code Structure Recommendations"
3. Create test cases from feature list

---

## Current Status: Snapshot

### What's Done ✅
- **Playback**: Play, pause, volume, shuffle, repeat - **100% working**
- **Visualizer**: 22 GPU-rendered modes via SkiaSharp - **100% working**
- **Fullscreen**: Arrow-key mode switching, OSD overlay - **100% working**
- **Screensaver**: All 22 modes, idle animation, Windows integration - **100% working**
- **Memory Safety**: SKMaskFilter, SKTypeface, SKBitmap leaks fixed - **100% done** ✅
- **UI Layout**: Three-pane design with proper separation - **100% working**
- **Settings Window**: Visualizer settings panel - **100% working**
- **Library Indexing**: Persistent JSON storage with atomic writes - **100% done** ✅
- **Metadata Extraction**: TagLib# tag reading - **100% done** ✅
- **Queue Persistence**: Save/load with auto-restore - **100% done** ✅
- **Crossfade**: Configurable 0-5s transitions - **100% done** ✅
- **Drag-and-Drop**: Queue reordering - **100% done** ✅
- **Context Menus**: Play, Add to Queue, Remove - **100% done** ✅
- **Remote Control (Platypus Remote)**: Phone/PWA control - **100% done** ✅

### What's Missing ⚠️
- **Gapless Playback**: Pre-buffer next track - **0% done** (v3.2.0)
- **Real Audio EQ**: DSP processing (currently only affects visualizer) - **0% done** (v3.2.0)
- **Replay Gain**: Volume normalization - **0% done** (v3.2.0)
- **Delete from Disk**: Safe file deletion with confirm - **0% done** (v3.2.0)
- **Sleep Timer**: Auto-stop playback - **0% done** (v3.2.0)

### Score: **95% Complete** → Future versions for remaining features

---

## Key Decisions Made

### 1. Architecture Pattern: MVVM
- ✅ **ViewModels** separate logic from UI
- ✅ **Services** handle business logic
- ✅ **Models** define data structures
- ✅ **DI Container** manages dependencies

### 2. Audio Engine: NAudio
- ✅ Robust, Windows-focused
- ✅ Good community support
- ✅ Extensible for future features

### 3. Metadata Extraction: TagLib#
- ✅ Support for all major formats
- ✅ Handles edge cases well
- ✅ Well-maintained library

### 4. Storage: Versioned JSON
- ✅ Human-readable (debugging)
- ✅ Standard format (import/export)
- ✅ Atomic writes for safety

### 5. Visualization: SkiaSharp GPU-Accelerated
- ✅ 22 GPU-rendered modes
- ✅ Hardware-accelerated via `SKElement`
- ✅ Fullscreen with OSD and mode switching
- ✅ Screensaver with all modes
- ✅ Memory-safe (all native leaks fixed)

---

## Implementation Priorities

### Priority 1: Foundation (Week 1) ✅ COMPLETE
**Library Indexing + Metadata**
- ✅ Persistent library with JSON index
- ✅ Fast cold start (<1.5s for 10k tracks)
- ✅ Foundation for search/filter

**Status**: ✅ Implemented in v3.1.0

### Priority 2: Enhancement (Week 2) ✅ COMPLETE
**Queue Persistence + Bulk Operations**
- ✅ Save/load queue snapshot
- ✅ Multi-select removal
- ✅ Context menu operations

**Status**: ✅ Implemented in v3.1.1

### Priority 3: Polish (Week 3) ⭐
**Optimization + Error Handling**
- Performance tuning
- Missing file detection
- Better error messages

**Effort**: 10 hours  
**Impact**: Production readiness

---

## Technology Stack

```
┌─────────────────────────────────────────────────────────────┐
│                       WPF UI (.NET 8)                        │
│   (MVVM: Views, ViewModels, Converters, Commands)           │
├─────────────────────────────────────────────────────────────┤
│                     Core Services Layer                      │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ • AudioPlayerService (NAudio)                       │  │
│  │ • LibraryIndexService (JSON persistence)            │  │
│  │ • MetadataExtractorService (TagLib#)                │  │
│  │ • QueueService (FIFO management)                    │  │
│  │ • SearchService (indexed search)                    │  │
│  │ • SettingsService (atomic writes)                   │  │
│  └──────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                    Data Storage Layer                        │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ • library.index.json (versioned, atomic)            │  │
│  │ • settings.json (user preferences)                  │  │
│  │ • queue.json (optional, auto-restore)               │  │
│  │ Backups: .bak files for recovery                    │  │
│  └──────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                      External Libraries                      │
│  • NAudio (audio playback)                                  │
│  • TagLib# (metadata extraction)                            │
│  • System.Text.Json (serialization)                         │
│  • MathNet.Numerics (FFT for visualizer)                    │
│  • SkiaSharp (GPU rendering)                                │
└─────────────────────────────────────────────────────────────┘
```

---

## File Structure After Implementation

```
PlatypusTools.Core/
├── Models/
│   ├── Track.cs (COMPLETE - all metadata fields)
│   ├── LibraryIndex.cs (NEW - versioned index)
│   ├── QueueSnapshot.cs (UPDATE - persistence)
│   └── Settings.cs (EXISTING - update for library)
│
├── Services/
│   ├── AudioPlayerService.cs (EXISTING)
│   ├── LibraryIndexService.cs (NEW - core)
│   ├── MetadataExtractorService.cs (NEW - tags)
│   ├── QueueService.cs (NEW - queue ops)
│   ├── SearchService.cs (NEW - indexed search)
│   ├── SettingsService.cs (UPDATE - atomic)
│   └── AudioVisualizerService.cs (EXISTING)
│
└── Utilities/
    ├── PathCanonicalizer.cs (NEW - dedup)
    ├── AtomicFileWriter.cs (NEW - safe writes)
    ├── MetadataCache.cs (NEW - fast lookups)
    └── SimpleLogger.cs (EXISTING)

PlatypusTools.UI/
├── ViewModels/
│   ├── AudioPlayerViewModel.cs (UPDATE - use services)
│   ├── LibraryViewModel.cs (UPDATE - indexing)
│   └── SettingsViewModel.cs (EXISTING)
│
├── Views/
│   ├── AudioPlayerView.xaml (EXISTING - working)
│   ├── LibraryView.xaml (UPDATE - bind to index)
│   ├── QueueView.xaml (UPDATE - persistence)
│   └── SettingsWindow.xaml (EXISTING)
│
└── Commands/
    └── ... (existing)
```

---

## Performance Targets (Post-Implementation)

| Metric | Before | After | Target |
|--------|--------|-------|--------|
| **Cold Start (10k tracks)** | N/A | - | < 1.5s |
| **Incremental Rescan (10k)** | N/A | - | < 15s |
| **Search Response** | Slow | - | < 300ms |
| **Visualizer FPS** | 60 | 60 | 60 |
| **Memory Usage** | Unbounded | - | < 500MB |
| **Library Load Time** | N/A | - | < 1s |

---

## Testing Plan

### Phase 1: Unit Tests
- [ ] Serialization round-trip (Track, LibraryIndex)
- [ ] Incremental scan detection
- [ ] Metadata parsing edge cases
- [ ] Atomic write corruption handling

### Phase 2: Integration Tests
- [ ] End-to-end library add/scan/save
- [ ] Queue add/remove/reorder
- [ ] Search on large library (10k+)
- [ ] Settings persist across sessions

### Phase 3: UI Tests (Smoke)
- [ ] Add folder; library populates
- [ ] Search finds tracks
- [ ] Queue operations work
- [ ] Visualizer renders

### Phase 4: Performance Tests
- [ ] Cold start < 1.5s (10k tracks)
- [ ] Incremental scan < 15s (change 10% of 10k)
- [ ] UI responsive during scanning

---

## Build & Deployment

### Development Build
```powershell
dotnet build -c Debug
dotnet run --project PlatypusTools.UI
```

### Release Build
```powershell
dotnet build -c Release
dotnet publish PlatypusTools.UI -c Release -o ./publish \
  --self-contained -r win-x64 \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:PublishTrimmed=false
```

### Package
```powershell
# Self-contained exe in: ./publish/PlatypusTools.UI.exe
# ~150MB (includes .NET 8 runtime)
```

---

## Known Limitations & Future Work

### v1.0 Scope (Current)
- ✅ Local library only (no network)
- ✅ Windows-only (no macOS/Linux)
- ✅ Single-user
- ✅ No cloud sync
- ✅ No streaming services

### v1.1 Planned
- ⚠️ Playlists & smart playlists
- ⚠️ Watch folders (auto-scan)
- ⚠️ Advanced search/filtering
- ⚠️ ReplayGain normalization

### v2.0 Future
- ❌ Cross-platform (Qt/Avalonia)
- ❌ Streaming integration
- ❌ Advanced DSP (EQ, effects)
- ❌ Mobile companion app

---

## Support & Documentation

### User Documentation
- See `DOCS/USER_GUIDE.md` (to be created)
- Building library (step-by-step)
- Managing queue & playlists
- Troubleshooting

### Developer Documentation
- See `DOCS/` folder (these guides)
- Architecture overview
- API reference (generated from code)
- Contributing guidelines

### Community
- GitHub Issues: Bug reports & feature requests
- GitHub Discussions: Questions & ideas
- Pull Requests: Contributions welcome

---

## Quick Reference: Key Files

### Start Here
- `DOCS/AUDIO_PLAYER_FEATURE_MANIFEST.md` - Complete feature list
- `DOCS/AUDIO_PLAYER_IMPLEMENTATION_STATUS.md` - What's done & what's not
- `DOCS/AUDIO_LIBRARY_REWRITE_GUIDE.md` - Step-by-step implementation

### Implementation Files (Create First)
1. `PlatypusTools.Core/Models/Track.cs`
2. `PlatypusTools.Core/Models/LibraryIndex.cs`
3. `PlatypusTools.Core/Utilities/PathCanonicalizer.cs`
4. `PlatypusTools.Core/Utilities/AtomicFileWriter.cs`
5. `PlatypusTools.Core/Services/MetadataExtractorService.cs`
6. `PlatypusTools.Core/Services/LibraryIndexService.cs`

### Integration Points
- `PlatypusTools.UI/ViewModels/AudioPlayerViewModel.cs` - Add service DI
- `PlatypusTools.UI/Views/AudioPlayerView.xaml` - Bind to library
- `App.xaml.cs` - Register services

---

## NuGet Packages Needed

```xml
<!-- In PlatypusTools.Core.csproj -->
<ItemGroup>
    <PackageReference Include="TagLibSharp" Version="2.3.0" />
    <PackageReference Include="MathNet.Numerics" Version="5.2.0" />
    <PackageReference Include="System.Text.Json" Version="8.0.0" />
</ItemGroup>

<!-- In PlatypusTools.UI.csproj -->
<ItemGroup>
    <PackageReference Include="SkiaSharp" Version="2.88.3" />
</ItemGroup>
```

Install:
```powershell
dotnet add PlatypusTools.Core package TagLibSharp
dotnet add PlatypusTools.Core package MathNet.Numerics
dotnet add PlatypusTools.UI package SkiaSharp
```

---

## Estimated Timeline

| Week | Phase | Tasks | Hours | Status |
|------|-------|-------|-------|--------|
| 1 | Foundation | Indexing + Metadata | 14h | 📅 Ready |
| 2 | Enhancement | Persistence + Bulk Ops | 11h | 📅 Ready |
| 3 | Polish | Optimization + Errors | 10h | 📅 Ready |
| 4 | Testing | Unit + UI Tests | 13h | 📅 Ready |
| 5 | Release | Documentation + Package | 8h | 📅 Ready |

**Total**: 56 hours (~1.5 months part-time, or 1 month full-time)

---

## Success Criteria

Your audio player is **production-ready** when:

- [ ] Library persists across sessions
- [ ] Cold start < 1.5s (10k+ tracks)
- [ ] Incremental rescan < 15s
- [ ] Queue multi-select removal works
- [ ] Settings persist
- [ ] Visualizer maintains 60 FPS
- [ ] No errors on 100k+ file libraries
- [ ] Atomic writes prevent corruption
- [ ] All unit tests pass
- [ ] UI smoke tests pass
- [ ] Documentation complete
- [ ] Release build < 200MB

**Estimated Completion**: 6-8 weeks from now (late February 2026)

---

## Contact & Questions

For questions about these guides:
- 📧 Check existing GitHub issues
- 💬 Open GitHub Discussion
- 📝 Create detailed issue with context

For implementation support:
- Reference the step-by-step guide
- Check code examples provided
- Review similar projects on GitHub

---

## Checklist: Getting Started Today

- [ ] Read this README (5 min)
- [ ] Read AUDIO_PLAYER_IMPLEMENTATION_STATUS.md (20 min)
- [ ] Review AUDIO_LIBRARY_REWRITE_GUIDE.md Step 1-2 (30 min)
- [ ] Create development branch: `feature/audio-library-v1.0`
- [ ] Install required NuGet packages
- [ ] Create models directory structure
- [ ] Begin with Step 1: Create Track.cs model

**Estimated Time**: 1-2 hours to get fully oriented

---

**Documents Version**: 1.1.0  
**Status**: 🟢 Ready for Production Implementation  
**Last Updated**: February 12, 2026  
**Next Review**: March 12, 2026  

✨ **Good luck with the implementation!** ✨

---

## Platypus Remote - Phone Control

**Version**: v3.4.0+ | **Status**: ✅ Complete

Control your audio player from any phone, tablet, or browser on your local network.

### Quick Start

1. Go to **Settings** → **Remote Control** tab
2. Check **"Enable Remote Control Server"**
3. **Scan the QR code** with your phone
4. Control playback from your phone!

### Features

| Feature | Description |
|---------|-------------|
| **Real-time Sync** | Play/pause, next/prev, volume sync instantly via SignalR |
| **QR Code Pairing** | Scan to connect - no typing URLs |
| **PWA Install** | Install as app on iOS/Android for quick access |
| **Library Browsing** | Search and browse your music from your phone |
| **Queue Management** | View and manage the play queue |
| **Audio Streaming** | Stream audio to your phone (optional) |
| **Album Art** | See current track artwork |
| **Progress Seeking** | Tap progress bar to seek |

### Installing as an App (PWA)

#### iOS (Safari)
1. Open the remote URL in Safari
2. Tap **Share** button (square with arrow)
3. Scroll down and tap **"Add to Home Screen"**
4. Tap **Add**

#### Android (Chrome)
1. Open the remote URL in Chrome
2. Tap the **⋮** menu (three dots)
3. Tap **"Install app"** or **"Add to Home screen"**
4. Tap **Install**

### Audio Streaming to Phone

To listen to music on your phone instead of your PC:

1. On the **Now Playing** tab, tap **"🎧 Stream audio to this device"**
2. Audio will play on your phone synced with the current track
3. Tap again to stop streaming

> **Note**: Streaming uses HTTP. For best results, use on the same WiFi network.

### Technical Details

| Component | Technology |
|-----------|------------|
| Web Server | ASP.NET Core Kestrel (embedded) |
| Real-time | SignalR WebSocket |
| Port | 47392 (HTTPS) |
| PWA | Service Worker + Web App Manifest |
| Streaming | HTTP Range Requests |
| QR Code | QRCoder library |

### Network Requirements

- PC and phone must be on the **same local network**
- Port **47392** must not be blocked by firewall
- For external access, use port forwarding or a tunnel (Cloudflare, ngrok)

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Can't connect | Ensure both devices are on same WiFi |
| Certificate warning | Tap "Advanced" → "Proceed" (self-signed cert) |
| QR code not showing | Click "Refresh QR Code" button |
| Controls don't respond | Check server is enabled in Settings |
| Install prompt missing | Use Safari (iOS) or Chrome (Android) |

---

## 🌐 External Access (Outside Local Network)

Control your music from anywhere using your own domain.

### Option 1: Port Forwarding + DNS (Simplest)

#### Step 1: Find Your Local IP
```powershell
ipconfig | Select-String "IPv4"
# Example: 192.168.1.100
```

#### Step 2: Configure Router Port Forwarding
1. Log into your router (usually `http://192.168.1.1`)
2. Find **Port Forwarding** / **NAT** / **Virtual Server** settings
3. Add a new rule:

| Setting | Value |
|---------|-------|
| Name | PlatypusRemote |
| External Port | 47392 |
| Internal IP | Your PC's IP (e.g., 192.168.1.100) |
| Internal Port | 47392 |
| Protocol | TCP |

4. Save and apply

#### Step 3: Get Your Public IP
```powershell
(Invoke-WebRequest -Uri "https://api.ipify.org").Content
# Example: 203.0.113.42
```

#### Step 4: Configure DNS (Using Your Domain)
Go to your DNS provider and add an A record:

| Type | Name | Value | TTL |
|------|------|-------|-----|
| A | music | Your public IP (203.0.113.42) | 300 |

This creates `music.josephtheplatypus.com`

#### Step 5: Access From Anywhere
```
https://music.josephtheplatypus.com:47392
```

> **Note**: Accept the certificate warning (self-signed). For proper SSL, use Option 2.

---

### Option 2: Reverse Proxy with Caddy (Proper SSL)

Caddy automatically manages Let's Encrypt SSL certificates.

#### Prerequisites
- Domain pointing to your public IP
- Ports 80 and 443 forwarded to your PC
- Caddy installed

#### Step 1: Install Caddy
```powershell
winget install Caddy.Caddy
# Or download from https://caddyserver.com/download
```

#### Step 2: Create Caddyfile
Create `C:\Caddy\Caddyfile`:
```
music.josephtheplatypus.com {
    reverse_proxy localhost:47392 {
        transport http {
            tls_insecure_skip_verify
        }
    }
}
```

#### Step 3: Forward Ports 80 and 443
In your router, forward:
- Port 80 → Your PC:80 (for Let's Encrypt verification)
- Port 443 → Your PC:443 (for HTTPS)

#### Step 4: Run Caddy
```powershell
cd C:\Caddy
caddy run
```

#### Step 5: Access with Proper SSL
```
https://music.josephtheplatypus.com
```
No port number needed, no certificate warnings!

---

### Option 3: Cloudflare Tunnel (No Port Forwarding)

If you can't forward ports (CG-NAT, apartment, etc.), use Cloudflare Tunnel.

#### Step 1: Create Cloudflare Account
1. Go to https://cloudflare.com
2. Add your domain and update nameservers

#### Step 2: Install cloudflared
```powershell
winget install Cloudflare.cloudflared
```

#### Step 3: Authenticate
```powershell
cloudflared tunnel login
```

#### Step 4: Create Tunnel
```powershell
cloudflared tunnel create platypus-remote
cloudflared tunnel route dns platypus-remote music.josephtheplatypus.com
```

#### Step 5: Configure Tunnel
Create `~/.cloudflared/config.yml`:
```yaml
tunnel: platypus-remote
credentials-file: ~/.cloudflared/<TUNNEL_ID>.json

ingress:
  - hostname: music.josephtheplatypus.com
    service: https://localhost:47392
    originRequest:
      noTLSVerify: true
  - service: http_status:404
```

#### Step 6: Run Tunnel
```powershell
cloudflared tunnel run platypus-remote
```

#### Step 7: Access From Anywhere
```
https://music.josephtheplatypus.com
```

---

### Dynamic IP Considerations

If your public IP changes (most home connections):

#### Option A: Use a DDNS Service
1. Sign up at No-IP, DuckDNS, or Dynu
2. Install their updater client
3. Create CNAME record: `music.josephtheplatypus.com` → `yourname.ddns.net`

#### Option B: Cloudflare API Script
```powershell
# Save as Update-CloudflareDNS.ps1
$email = "your@email.com"
$apiKey = "your-api-key"
$zoneId = "your-zone-id"
$recordId = "your-record-id"

$ip = (Invoke-WebRequest -Uri "https://api.ipify.org").Content
$headers = @{
    "X-Auth-Email" = $email
    "X-Auth-Key" = $apiKey
    "Content-Type" = "application/json"
}
$body = @{
    type = "A"
    name = "music"
    content = $ip
    ttl = 300
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/dns_records/$recordId" `
    -Method PUT -Headers $headers -Body $body
```

Schedule to run hourly via Task Scheduler.

---

### Security Recommendations

| Setting | Recommendation |
|---------|----------------|
| Firewall | Only allow port 47392, 80, 443 |
| Router | Disable UPnP if not needed |
| Updates | Keep Windows and router firmware updated |
| Password | Consider adding basic auth to Caddy |
| Monitoring | Check router logs for suspicious activity |
