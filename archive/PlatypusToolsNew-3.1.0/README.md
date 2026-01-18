# 🦆 PlatypusTools

A comprehensive Windows system utility combining file management, media conversion, system cleanup, security tools, and metadata editing into a single PowerShell WPF application.

## � Prerequisites

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