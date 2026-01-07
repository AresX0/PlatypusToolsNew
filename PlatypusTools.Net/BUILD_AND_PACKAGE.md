# Build & Packaging

Requirements:
- Install .NET 10 SDK (https://dotnet.microsoft.com/en-us/download/dotnet/10.0) — ensure `dotnet --list-sdks` shows a 10.x entry
- For installer: WiX Toolset (https://wixtoolset.org) or MSIX packaging tools

Build:
- Ensure .NET 10 SDK is installed and the repo root contains `global.json` to pin SDK (optional)
- dotnet build .\PlatypusTools.sln

Run UI locally:
- dotnet run --project PlatypusTools.UI\PlatypusTools.UI.csproj

Publish single-file exe (win-x64):
- dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish --self-contained true PlatypusTools.UI\PlatypusTools.UI.csproj

Create installer (recommended approach):
- Use WiX: create a WiX project that packages the published folder into an MSI.
- Or create MSIX using MSIX Packaging Tool (manual or CI).

Notes:
- We will port each PowerShell feature into the Core library under `Services` and then call from UI.
- Start with dry-run behaviors & unit tests to ensure safety for destructive operations.

Logging:
- The app reads `appsettings.json` from the application directory at startup.
- Supported keys:
  - `LogFile`: path to the log file (e.g., `C:\Logs\platypustools.log`). If omitted, the app writes to `%LOCALAPPDATA%\\PlatypusTools\\logs\\platypustools.log` by default.
  - `LogLevel`: minimum log level (Trace, Debug, Info, Warn, Error). Case-insensitive; default is `Info`.

Sample `appsettings.json`:

```json
{
  "LogFile": "C:\\Users\\<you>\\AppData\\Local\\PlatypusTools\\logs\\platypustools.log",
  "LogLevel": "Debug"
}
```

Tip: Set a custom `LogFile` when running in CI or a managed environment to collect logs centrally.

Troubleshooting: dotnet not found in terminal
- If `dotnet` is installed but not visible in a terminal, add its install folder to your **user** PATH. The .NET SDK is typically installed at `C:\Program Files\dotnet`.

- To add the folder to your current session (temporary):

```powershell
$env:Path = "C:\Program Files\dotnet;$env:Path"
```

- To persist the change for the current user (recommended):

```powershell
# Append the dotnet folder to the user PATH
$u = [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not $u) { $u = 'C:\Program Files\dotnet' } elseif ($u -notmatch 'C:\\Program Files\\dotnet') { $u = "$u;C:\Program Files\dotnet" }
[Environment]::SetEnvironmentVariable('Path', $u, 'User')
```

After persisting the change, **restart your terminal or VS Code** to pick up the new PATH. Verify with `dotnet --list-sdks`.

- If you prefer an elevated approach, `setx` can be used but be wary of path length truncation on older Windows versions.

- If `dotnet` is not installed, install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and re-open your terminal.
