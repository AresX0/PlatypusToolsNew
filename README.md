# 🦆 PlatypusTools

A comprehensive Windows system utility combining file management, media conversion, system cleanup, security tools, metadata editing, and 3D model creation into a single WPF application.

## ✨ Features

### 📁 File Management
- Duplicate file scanner with hash comparison
- Empty folder cleanup
- File renaming with pattern matching
- Folder hider (hide/unhide folders)

### 🎬 Multimedia
- **Image Converter** - Convert between formats including SVG output
- **Image Resizer** - Batch resize images
- **ICO Converter** - Create Windows icons
- **Batch Watermark** - Add watermarks to images
- **3D Model Editor** - Create and edit 3D models:
  - Convert SVG files to 3D printable STL/OBJ formats
  - Create basic shapes (Cube, Cylinder, Sphere, Pyramid, Cone)
  - Create 3D extruded text
  - Interactive viewport with mouse controls
  - Automatic image tracing for embedded raster SVGs
- **Video Converter** - Convert video formats with FFmpeg
- **Screen Recorder** - Record your screen to video:
  - Multiple codecs: H.264, H.265, VP9
  - Configurable frame rate (15-60 FPS)
  - System audio capture (record what you hear)
  - Global hotkeys (Ctrl+Shift+R/S)
  - 3-second countdown delay option
- **Audio Library** - Manage and tag audio files

### 🔧 System Tools
- Disk cleanup and space analyzer
- Startup manager
- Privacy cleaner
- System audit
- **Windows Update Repair** - Diagnose and fix Windows Update issues:
  - DISM health checks and repairs
  - SFC system file scan
  - Windows Update service restart
  - Component store cleanup
  - Modern troubleshooter integration (Windows 10/11)

### 🔒 Security
- Forensics analyzer with HTML export
- File shredder

### 📋 Metadata
- Read and edit file metadata with ExifTool

## 📋 Prerequisites

PlatypusTools requires the following external tools to be installed on your system:

### Required Tools
- **FFmpeg** - Video/audio conversion and processing
  - Download: https://ffmpeg.org/download.html
  - Windows: https://www.gyan.dev/ffmpeg/builds/
  - Add to PATH or place in `Tools/` folder

- **ExifTool** - Metadata reading and writing
  - Download: https://exiftool.org/
  - Windows: https://exiftool.org/index.html#download
  - Add to PATH or place in `Tools/` folder

### Optional Tools
- **fpcalc** (AcoustID) - Acoustic fingerprinting for music recognition
  - Download: https://acoustid.org/fingerprinter
  - Windows: https://acoustid.org/fingerprinter
  - Used for audio library features

- **WebView2 Runtime** - For help and documentation display
  - Download: https://developer.microsoft.com/en-us/microsoft-edge/webview2/

**Note:** PlatypusTools will detect missing prerequisites on startup and provide download links if needed.

## �🚀 Installation

1. **Clone or download** this repository:
```powershell
git clone https://github.com/AresX0/PlatypusTools.git
```

2. **Install dependencies** (optional):
   - Download FFmpeg and extract to `Tools/` folder
   - Download ExifTool and extract to `Tools/` folder

3. **Run the application**:
```powershell
# Right-click and "Run with PowerShell"
# Or from terminal:
powershell -ExecutionPolicy Bypass -File PlatypusTools.ps1
```

**Releases:** Download packaged releases (zip or installer) from the GitHub Releases page. Release assets should include `PlatypusTools_Help.html` (or a release ZIP that contains it). When installing manually, place `PlatypusTools_Help.html` into `C:\ProgramFiles\PlatypusUtils\` so the app can open it via File → Help.

---

## 🔨 Building Releases

> **⚠️ IMPORTANT: Use ONLY the build script for releases**

```powershell
cd C:\Projects\PlatypusToolsNew
.\Build-Release.ps1
```

This script handles everything:
- Auto-increments version number
- Updates all version files
- Cleans and rebuilds everything
- Outputs MSI to `releases\PlatypusToolsSetup-vX.Y.Z.msi`

See [.github/copilot-instructions.md](.github/copilot-instructions.md) for detailed build rules.

---

See `PlatypusTools_Help.html` for full user documentation.

---

## 🎨 Open Source Attribution

PlatypusTools incorporates the following open source projects:

### Audio Visualization
- **projectM** - Open source music visualizer
  - Repository: https://github.com/projectM-visualizer/projectm
  - License: LGPL v2
  - Used for real-time audio spectrum and waveform visualization

### External Tools
- **FFmpeg** - Multimedia framework
  - Website: https://ffmpeg.org/
  - License: LGPL v2+
  - Usage: Video conversion, audio processing, format detection

- **ExifTool** - Metadata extraction and writing tool
  - Website: https://exiftool.org/
  - License: Perl Artistic License
  - Usage: Read/write metadata tags in media files

- **fpcalc** - Acoustic fingerprinter (by AcoustID)
  - Website: https://acoustid.org/
  - License: LGPL v2
  - Usage: Music recognition and duplicate detection

### .NET Libraries
- **MVVM Toolkit** - MVVM pattern implementation
  - License: MIT
  - Usage: ViewModel and data binding

- **SharpCompress** - Archive manipulation
  - License: MIT
  - Usage: ZIP, 7z, RAR extraction and creation

- **TagLibSharp** - Audio metadata library
  - License: LGPL v2
  - Usage: Audio tag reading and writing

For complete license information, see individual tool documentation and the LICENSE files in this repository.