# PlatypusTools.Net (WPF, .NET 10)

This folder contains a new .NET 10 WPF port of PlatypusTools.

Projects:
- PlatypusTools.Core (class library) - core logic, non-UI
- PlatypusTools.UI (WPF app) - user interface, references Core
- PlatypusTools.Core.Tests (MSTest) - unit tests for Core

Build (once .NET SDK installed):
- dotnet build
- dotnet run --project PlatypusTools.UI\PlatypusTools.UI.csproj

Publish single-file EXE (win-x64, self-contained):
- dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish --self-contained true PlatypusTools.UI\PlatypusTools.UI.csproj

Installer options (recommended):
- WiX Toolset (MSI)
- MSIX Packaging Tool

Notes:
- I scaffolded a basic multi-project skeleton and sample classes. Next steps: port functions from `PlatypusTools.ps1` into `PlatypusTools.Core\Services` and wire UI views in the WPF project.
