## v4.0.1.10

### Added
- **Intune Backup** new System sub-tab integrating the `IntuneBackup.ps1` workflow directly into PlatypusTools
  - One-click actions: Full Backup, Compare Exports, Generate Reimport, Baseline Compare/Import, Cross-Tenant Compare/Import, Settings CSV Export
  - Custom Input mode for ad-hoc workflows
  - Tenant GUID validation, configurable Project Root, live status log, and Cancel support
  - Runs PowerShell in-process (hidden) -- no external console window
- Tab visibility toggle `System.IntuneBackupSuite` in Settings

### Assets
- `PlatypusToolsSetup-v4.0.1.10.msi` -- installer
- `PlatypusTools-v4.0.1.10-Portable.exe` -- standalone single-file executable
