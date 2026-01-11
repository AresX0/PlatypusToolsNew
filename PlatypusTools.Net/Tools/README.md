# Tools Folder

This folder contains external tools required by PlatypusTools for media operations.

## Required Tools

### FFmpeg (for Video/Image operations)

**Download:** https://ffmpeg.org/download.html

1. Download the Windows build (choose "ffmpeg-release-essentials" or "ffmpeg-release-full")
2. Extract the archive
3. Copy these files to this `Tools/` folder:
   - `ffmpeg.exe`
   - `ffprobe.exe`

**Alternative:** Use a package manager:
```powershell
# Using Chocolatey
choco install ffmpeg

# Using Winget
winget install FFmpeg
```

### ExifTool (for Metadata operations)

**Download:** https://exiftool.org

1. Download the Windows Executable
2. Extract `exiftool(-k).exe`
3. Rename to `exiftool.exe`
4. Place in this `Tools/` folder

**Alternative:** Use a package manager:
```powershell
# Using Chocolatey
choco install exiftool
```

## Folder Structure

After setup, this folder should contain:
```
Tools/
├── README.md       # This file
├── ffmpeg.exe      # FFmpeg encoder
├── ffprobe.exe     # FFmpeg media analyzer
└── exiftool.exe    # ExifTool metadata editor
```

## Verification

To verify the tools are working, open PowerShell and run:

```powershell
# Check FFmpeg
.\ffmpeg.exe -version

# Check FFprobe
.\ffprobe.exe -version

# Check ExifTool
.\exiftool.exe -ver
```

## Notes

- These files are **not included** in the repository due to licensing and file size
- Users must download and install them separately
- The main application will prompt you if tools are missing
- You can also set a custom tools path via **File** → **Set Default Tools Folder**
