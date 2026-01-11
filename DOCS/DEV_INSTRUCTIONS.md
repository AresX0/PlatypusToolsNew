# Developer Instructions (starter)

Quick steps for working on `PlatypusTools.ps1` locally.

Prerequisites:
- PowerShell 7+ (pwsh) recommended
- PSScriptAnalyzer module (Install-Module PSScriptAnalyzer -Scope CurrentUser)

Local workflow (always local-first):
1. Run parse check: `pwsh -NoProfile -File .\.tools\parse_check.ps1`
2. Run static analysis: `Import-Module PSScriptAnalyzer; Invoke-ScriptAnalyzer -Path .\PlatypusTools.ps1 -Recurse -Severity Error,Warning`
3. Create a backup and a working copy via helper: `pwsh -NoProfile .\.tools\new_version.ps1 -Version 1.0.1 -Note "short summary"`
   - This creates: `backups/PlatypusTools_<timestamp>.ps1` and `PlatypusTools.v1.0.1.ps1`
   - It also updates `CHANGELOG.md` with an initial entry.
4. Make edits in the working copy file `PlatypusTools.v1.0.1.ps1`.
5. Re-run analyzer/parse checks until satisfied.
6. Run manual smoke tests (UI preview dry-runs for cleanup, recent cleaners, duplicates scan in a small test folder).
7. Add a CHANGELOG.md entry describing the change and any manual test steps.

Guidance for Copilot or novice re-writes:
- Target small modules first: extract pure functions and provide unit tests.
- Keep heavy system calls (formatting disks, ACL changes, file deletes) behind explicit flags and a dry-run mode.
- Document assumptions (Admin required, Windows only, PowerShell version).

Safety notes:
- Backup before running destructive operations.
- For areas that modify system state (ACLs, bootable USB), add clear confirmation prompts and logs.

Contact: Leave notes in `CHANGELOG.md` and create TODO entries in `DOCS/MANIFEST.md` if more deep documentation is required.
