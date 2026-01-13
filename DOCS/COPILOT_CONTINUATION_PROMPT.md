# PlatypusTools v3.0.0 - Copilot Continuation Prompt

Use this prompt to resume work on the v3.0.0 major features implementation if the session is interrupted.

---

## CONTINUATION PROMPT

```
I am working on PlatypusTools v3.0.0, a .NET 10 WPF application at C:\Projects\PlatypusToolsNew.

GitHub: https://github.com/AresX0/PlatypusToolsNew
Branch: v3.0.0-major-features

## Current State
Review the following files for context:
- DOCS/IMPLEMENTATION_MANIFEST.md - Complete feature specifications
- DOCS/TODO.md - Detailed task list with completion status
- DOCS/PROJECT_DOCUMENTATION.md - File and code explanations

## Project Overview
PlatypusTools is a media management application with:
- Video Editor (with timeline, multi-track, transitions)
- Image Scaler (batch processing, comparison preview)
- Metadata Editor (templates, batch copy)
- Duplicate Finder (image/video similarity)
- System Cleaner
- Empty Folder Scanner
- File Converter
- Audio Player (C++ core + .NET UI)

## Technology Stack
- .NET 10 WPF with MVVM pattern
- C++17 for audio core (FFmpeg, TagLib, PortAudio, KissFFT, libebur128)
- C++/CLI for interop bridging
- SQLite for library database
- WiX 6 for MSI installer

## What to Do
1. Read DOCS/TODO.md to see current progress
2. Find the first incomplete task (marked with [ ])
3. Implement that task following the specifications in IMPLEMENTATION_MANIFEST.md
4. Update TODO.md to mark the task complete
5. Commit with message: "v3.0.0: [TaskID] Description"
6. Continue with next incomplete task

## Key Files
- PlatypusTools.UI/MainWindow.xaml - Main application window
- PlatypusTools.Core/Services/ - Business logic services
- PlatypusTools.UI/ViewModels/ - MVVM ViewModels
- PlatypusTools.UI/Views/ - XAML views

## Build & Test
- Build: dotnet build PlatypusToolsNew.sln
- Run: dotnet run --project PlatypusTools.UI
- Test: dotnet test

## Commit Guidelines
- Prefix commits with "v3.0.0:"
- Reference task ID from TODO.md
- Keep commits focused on single feature

Please continue implementing the v3.0.0 features where I left off.
```

---

## Quick Reference

### Solution Structure
```
PlatypusToolsNew.sln
├── PlatypusTools.Core           # .NET 10 class library
├── PlatypusTools.UI             # .NET 10 WPF application
├── PlatypusTools.Installer      # WiX 6 MSI project
├── PlatypusTools.Core.Tests     # Unit tests
├── CppAudioCore                 # C++17 static library (future)
├── CppAudioBridge               # C++/CLI wrapper (future)
└── PlatypusTools.Plugins.SDK    # Plugin SDK (future)
```

### Feature Priority Order
1. P1 - UI/UX Enhancements (Status bar, Shortcuts, Recent workspaces)
2. P1 - Video Editor Timeline
3. P1 - Image Scaler Batch/Comparison
4. P1 - Metadata Templates
5. P1 - Duplicate Finder Similarity
6. P2 - New Tools (Watermark, PDF, Archive, Screenshot)
7. P2 - System Features (Auto-update, Plugins, Logging)
8. P3 - Audio Player Suite (C++ Core + .NET UI)

### Important Commands
```powershell
# Switch to feature branch
git checkout v3.0.0-major-features

# Build solution
dotnet build PlatypusToolsNew.sln

# Run application
dotnet run --project PlatypusTools.UI

# Run tests
dotnet test

# Commit progress
git add .
git commit -m "v3.0.0: [TaskID] Description"

# Push to GitHub
git push origin v3.0.0-major-features
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

### C++ Audio Core (When Implementing)
| Component | Files |
|-----------|-------|
| ConverterService | CppAudioCore/src/converter/ConverterService.cpp |
| PlayerService | CppAudioCore/src/player/PlayerService.cpp |
| Wrappers | CppAudioBridge/src/*Wrapper.cpp |

---

## Recovery Checklist

If resuming after interruption:

- [ ] Confirm on branch `v3.0.0-major-features`
- [ ] Read DOCS/TODO.md for progress
- [ ] Check for uncommitted changes (`git status`)
- [ ] Review last commit (`git log -1`)
- [ ] Find next incomplete task
- [ ] Implement and test
- [ ] Commit with proper message
- [ ] Continue to next task

---

*This prompt should be given to Copilot when resuming interrupted work.*
