# Script-scoped variables (automatically detected)

List of commonly used script/global variables. Add descriptions of their purpose and usage.

- $script:Version       - Script semantic version
- $script:HasFFmpeg     - Flag indicating presence of ffmpeg
- $script:HasExifTool   - Flag indicating presence of exiftool
- $script:ImageFormatMap - Mapping of file extensions to .NET image formats
- $PlatypusBase         - Base installation path (C:\ProgramFiles\PlatypusUtils)
- $PlatypusAssets       - Assets directory under base
- $PlatypusData         - Data directory under base
- $PlatypusLogs         - Logs directory under base
- $script:DataRoot      - Working data root (video editor)
- $script:GlobalLogDir  - Global log directory
- $script:AppConfigPath - Path to saved app config
- $script:AppConfig     - In-memory application configuration object
- $script:ToolDir       - Selected tools folder (ffmpeg/exiftool)
- $script:ffmpegPath    - Resolved path to ffmpeg executable
- $script:ffprobePath   - Resolved path to ffprobe executable
- $script:exiftoolPath  - Resolved path to exiftool executable
- $script:FileComponents - Cache used for renumbering/metadata extraction
- $script:DupJsonDir    - Directory for duplicate scan JSON snapshots
- $script:secStatus     - UI control reference for security status
- $global:LastOperations - Last rename operations (used for undo)
- $script:UsbCancelRequested - Flag used during USB creation flow

> Tip: Expand each entry with the variable's data type, default value, and where in the code it is initialized/used.
