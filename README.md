# 🦆 PlatypusTools

A comprehensive Windows system utility combining file management, media conversion, system cleanup, security tools, and metadata editing into a single PowerShell WPF application.

![PowerShell](https://img.shields.io/badge/PowerShell-5.1+-blue.svg)
![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6.svg)
![License](https://img.shields.io/badge/License-MIT-green.svg)

## ✨ Features

### 📁 File Cleaner
- Scan and clean folders based on file types and patterns
- Filter by extension, age, and size
- Recursive directory scanning
- Safe deletion with confirmation

### 🎬 Media Conversion
- **Video Combiner** - Merge multiple videos into one (requires FFmpeg)
- **Graphics Conversion** - Convert images between formats (JPG, PNG, BMP, GIF, TIFF, WebP)
- **Image Resize** - Resize images by dimensions or percentage

### 🔍 Duplicate Finder
- Find duplicate files using MD5/SHA256 hash comparison
- Compare by filename or size
- Review and safely delete duplicates

### 🧹 System Cleanup
- **Disk Cleanup** - Remove Windows temp files, caches, and junk
- **Privacy Cleaner** - Clear browser data (Chrome, Firefox, Edge) and Windows traces
- **Startup Manager** - Control Windows startup programs
- **Hash Calculator** - Generate MD5, SHA1, SHA256, SHA512 checksums

### 🔒 Security
- **Folder Hider** - Hide folders using Windows attributes
- **Security Scan** - Check firewall, antivirus, UAC, and network shares
- **User Management** - Manage local Windows user accounts

### 📊 Metadata Editor
- View and edit file metadata (EXIF, IPTC, XMP)
- Support for images, video, audio, and documents
- Export metadata to CSV (requires ExifTool)

## 📋 Requirements

- **Windows 10/11** (64-bit recommended)
- **PowerShell 5.1** or later
- **Administrator privileges** (for some features)

### Optional Dependencies

| Tool | Required For | Download |
|------|--------------|----------|
| FFmpeg | Video combining/conversion | [ffmpeg.org](https://ffmpeg.org/download.html) |
| ExifTool | Metadata viewing/editing | [exiftool.org](https://exiftool.org) |

## 🚀 Installation

1. **Clone or download** this repository:
   ```powershell
   git clone https://github.com/yourusername/PlatypusTools.git
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

## 📁 Directory Structure

```
PlatypusTools/
├── PlatypusTools.ps1           # Main application script
├── PlatypusTools_Help.html     # Help documentation
├── README.md                   # This file
├── LICENSE                     # MIT License
├── Assets/                     # Icons and images (optional)
├── Data/                       # Configuration files (auto-created)
├── Logs/                       # Log files (auto-created)
└── Tools/                      # FFmpeg, ExifTool (user-provided)
```

The application uses `C:\ProgramFiles\PlatypusUtils\` as its base path by default, with subfolders:
- `Assets/` - Application images
- `Data/` - Configuration and scan data
- `Logs/` - Operation logs
- `Tools/` - External tools (FFmpeg, ExifTool)

## ⚙️ Configuration

Settings are saved to `Data/PlatypusTools_Config.json` and include:
- Default scan/output/tools folders
- Theme (Light/Dark)
- Font size preference
- UI visibility options

Access configuration via **File** menu → **Save/Load Configuration**

## 🎨 Themes

Switch between Light and Dark mode via **View** menu → **Theme**

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| F5 | Refresh |
| Ctrl+S | Save configuration |
| Ctrl+Q | Exit |
| F1 | Open Help |
| Ctrl+Tab | Next tab |

## 📖 Documentation

- Open **Help** → **Help Contents** for full documentation
- Quick Start guide available via **Help** → **Quick Start**

## 🔧 Troubleshooting

### FFmpeg/ExifTool not found
1. Download the required tool from the official website
2. Extract to the `Tools/` folder
3. Or set the path via **File** → **Set Default Tools Folder**

### Access Denied errors
- Run PlatypusTools as Administrator
- Some system folders require elevated privileges

### Script won't run
```powershell
# Set execution policy (run as Admin)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📧 Support

If you encounter issues:
1. Check the `Logs/` folder for error details
2. Open an issue on GitHub with:
   - Windows version
   - PowerShell version (`$PSVersionTable.PSVersion`)
   - Error message from logs

---

**Made with ❤️ using PowerShell and WPF**
