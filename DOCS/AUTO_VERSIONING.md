# Auto-Versioning Setup

This project uses automatic version incrementing on each commit.

## How It Works

1. A pre-commit git hook runs before each commit
2. The hook executes `scripts/Increment-Version.ps1`
3. The script increments the 4th version number (revision/patch) by 1
4. Version files are automatically staged with the commit

## Version Format

`Major.Minor.Build.Revision` (e.g., `3.2.6.1` → `3.2.6.2`)

## Files Updated

The following files are automatically updated:

- `PlatypusTools.UI/PlatypusTools.UI.csproj`
- `PlatypusTools.Core/PlatypusTools.Core.csproj`
- `PlatypusTools.Installer/Product.wxs`
- `PlatypusTools.UI/Views/SettingsWindow.xaml`
- `PlatypusTools.UI/Views/AboutWindow.xaml`
- `PlatypusTools.UI/Assets/PlatypusTools_Help.html`

## Manual Version Increment

To manually increment the version:

```powershell
.\scripts\Increment-Version.ps1
```

To preview changes without applying:

```powershell
.\scripts\Increment-Version.ps1 -WhatIf
```

## Setting Up the Hook

The pre-commit hook should be automatically installed in `.git/hooks/pre-commit`.

If needed, manually copy:
```powershell
Copy-Item .\scripts\pre-commit-hook .\.git\hooks\pre-commit -Force
```

## Skipping Version Increment

To commit without incrementing version (e.g., for documentation-only changes):

```bash
git commit --no-verify -m "Your message"
```

## Major/Minor Version Changes

For major or minor version changes, manually update the version in:
- `PlatypusTools.UI/PlatypusTools.UI.csproj` 
- Run `.\scripts\Increment-Version.ps1 -WhatIf` to verify propagation

Then commit normally - the hook will handle the rest.
