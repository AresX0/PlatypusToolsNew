# PlatypusTools v3.4.0 - Copilot Continuation Prompt

Use this prompt to resume work on PlatypusTools if the session is interrupted.

---

## CONTINUATION PROMPT

```
I am working on PlatypusTools v3.4.0, a .NET 10 WPF application at C:\Projects\PlatypusToolsNew.

GitHub: https://github.com/AresX0/PlatypusToolsNew

## Current State
Review the following files for context:
- DOCS/TODO.md - Detailed task list with completion status (247/332 tasks complete)
- DOCS/TODO_PRIORITY.md - Priority feature list with status
- DOCS/IMPLEMENTATION_MANIFEST.md - Complete feature specifications
- DOCS/PROJECT_DOCUMENTATION.md - File and code explanations
- .github/copilot-instructions.md - Build rules and coding conventions

## Project Overview
PlatypusTools is a comprehensive media management & DFIR application with:
- Video Editor (with timeline, multi-track, transitions, keyframes, 80+ filters)
- Image Scaler (batch processing, comparison preview, per-item overrides)
- Audio Player (NAudio-based with 22 GPU visualizers, gapless, EQ, streaming)
- Metadata Editor (templates, batch copy)
- Duplicate Finder (image/video similarity)
- DFIR Toolkit (YARA, IOC Scanner, Registry Diff, PCAP, Memory Analysis)
- Advanced Forensics (Volatility 3, KAPE, OpenSearch, Plaso, Velociraptor)
- System Tools (Cleaner, Process Manager, Network Tools, Startup Manager)
- Media Library (scanning, sync, smart collections)
- File Tools (Renamer, Converter, PDF, Archive, Screenshot, Watermark)

## Technology Stack
- .NET 10 WPF with MVVM pattern (BindableBase, RelayCommand, AsyncRelayCommand)
- NAudio for audio playback & processing
- SkiaSharp for GPU-accelerated visualizer rendering
- TagLib# (TagLibSharp 2.3.0) for metadata extraction
- FFmpeg/FFprobe for video processing
- ServiceLocator pattern (planned migration to DI)
- WiX 6 for MSI installer
- Build-Release.ps1 for all releases

## What to Do
1. Read .github/copilot-instructions.md for build rules and conventions
2. Read DOCS/TODO.md to see current progress
3. Find the first incomplete task (marked with [ ])
4. Implement that task following the specifications in IMPLEMENTATION_MANIFEST.md
5. Build: .\Build-Release.ps1 -NoVersionBump (for testing)
6. Update TODO.md to mark the task complete
7. Ask user before committing/pushing/releasing
8. Continue with next incomplete task

## Key Files
- PlatypusTools.UI/MainWindow.xaml - Main application window
- PlatypusTools.Core/Services/ - Business logic services
- PlatypusTools.UI/ViewModels/ - MVVM ViewModels
- PlatypusTools.UI/Views/ - XAML views

## Build & Test
- Quick build: dotnet build PlatypusTools.UI/PlatypusTools.UI.csproj -c Release -o publish
- Full release: .\Build-Release.ps1
- Run: Start-Process "C:\Projects\PlatypusToolsNew\publish\PlatypusTools.UI.exe"
- Test: dotnet test

## Commit Guidelines
- ASK USER before committing/pushing/releasing
- Keep commits focused on single feature
- NEVER mention MIRCAT, CLAW, or Microsoft Incident Response
- Use PLATYPUS and BILL for internal references

Please continue implementing the v3.0.0 features where I left off.
```

---

## Quick Reference

### Solution Structure
```
PlatypusToolsNew.sln
├── PlatypusTools.Core           # .NET 10 class library (services, models, utilities)
├── PlatypusTools.UI             # .NET 10 WPF application (views, viewmodels)
├── PlatypusTools.Installer      # WiX 6 MSI project
└── PlatypusTools.Core.Tests     # Unit tests
```

### Feature Priority Order
1. Phase 10 - Shotcut-Inspired Video Editor (rolling/slip/slide edits, text/titles, proxy editing)
2. Architecture - DI Container migration (ServiceLocator → IServiceCollection)
3. Audio Player - Relink missing files, Watch Folders integration
4. Testing - Unit tests for DFIR services, audio features
5. Polish - Remaining Phase 8/9 items

### Important Commands
```powershell
# Build for testing
dotnet build PlatypusTools.UI/PlatypusTools.UI.csproj -c Release -o publish
Start-Process "C:\Projects\PlatypusToolsNew\publish\PlatypusTools.UI.exe"

# Full release build (auto-increments version, builds MSI)
.\Build-Release.ps1

# Run tests
dotnet test

# GitHub release (after Build-Release.ps1)
gh release create vX.Y.Z "releases\PlatypusToolsSetup-vX.Y.Z.msi" --title "vX.Y.Z" --notes "Release notes"
```

### File Locations
| Purpose | Path |
|---------|------|
| Main Window | PlatypusTools.UI/MainWindow.xaml |
| App.xaml | PlatypusTools.UI/App.xaml |
| Themes | PlatypusTools.UI/Themes/ |
| ViewModels | PlatypusTools.UI/ViewModels/ |
| Views | PlatypusTools.UI/Views/ |
| Services | PlatypusTools.Core/Services/ |
| Models | PlatypusTools.Core/Models/ |
| Helpers | PlatypusTools.Core/Helpers/ |

### C++ Audio Core — NOT USED
The audio player uses a pure managed .NET implementation with NAudio.
There are NO C++, C++/CLI, or SQLite components in this project.
| Component | Implementation |
|-----------|---------------|
| Audio Playback | NAudio (WasapiOut) via EnhancedAudioPlayerService.cs |
| Metadata | TagLib# (TagLibSharp 2.3.0) via MetadataExtractorService.cs |
| Library Storage | JSON files via LibraryIndexService.cs + AtomicFileWriter |
| Visualizer | SkiaSharp GPU via AudioVisualizerView.xaml (~10k lines) |

---

## Recovery Checklist

If resuming after interruption:

- [ ] Read .github/copilot-instructions.md for build rules
- [ ] Read DOCS/TODO.md for progress (247/332 tasks complete)
- [ ] Check for uncommitted changes (`git status`)
- [ ] Review last commit (`git log -1`)
- [ ] Find next incomplete task in Phase 10 or remaining phases
- [ ] Implement and test (build + launch app)
- [ ] Ask user before committing
- [ ] Continue to next task

---

*Last Updated: February 11, 2026*
*This prompt should be given to Copilot when resuming interrupted work.*
