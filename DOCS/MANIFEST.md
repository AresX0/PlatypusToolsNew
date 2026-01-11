# Manifest (starter)

This document is a single-source manifest for `PlatypusTools.ps1` — what it does, major features, and where code lives.

## Overview
PlatypusTools is a combined Windows utility offering: File Cleaner, Media Conversion, Duplicates, Cleanup (privacy), Security (folder hider + scans), Metadata, and additional tools. It is implemented as one large PowerShell script (`PlatypusTools.ps1`) with embedded WPF XAML UI.

## Major Features
- File Cleaner (renaming, prefix handling, episode numbering)
- Media Conversion and Video Combiner (FFmpeg integration)
- Image tools (resize, ICO creation)
- Duplicates scanner
- Recent/Privacy cleaning (Recent Shortcuts, Jump Lists, Explorer MRU)
- Folder Hider (ACL/EFS protection)
- Security scans (elevated users, ACL checks, outbound connection analysis)
- Bootable USB maker
- Metadata viewer and editor (ExifTool/FFprobe fallback)

## Where to find things
- Main script: `PlatypusTools.ps1`
- Help: `PlatypusTools_Help.html`
- Docs: `DOCS/` (this folder)
- Tests & checks: `.tools/parse_check.ps1`, `pssa_report.json` (static analysis run)

## Functions
See `DOCS/functions_list.md` for a machine-generated list of all top-level functions. Use that file as the template for expanding each function's entry with:
- Short description
- Parameters and expected types
- Side effects (modifies files, registry, ACLs)
- Permissions required (Admin?)
- Suggested tests

## Variables
See `DOCS/script_variables.md`. For each variable, add:
- Purpose
- Location where it is set
- Typical values

## Change and Versioning Policy (local-first)
For every major change:
1. Create a timestamped backup: `backups/PlatypusTools_<timestamp>.ps1`.
2. Copy current script to a versioned working copy: `PlatypusTools.vX.Y.Z.ps1`.
3. Update `$script:Version` in the working copy.
4. Add an entry to `CHANGELOG.md` including backup name and a short summary.
5. Add tests (manual steps / PSScriptAnalyzer fixes) and run parse check.
6. When ready to publish, push branch and open a PR (only when you tell me).

## Rewriting guidance for Copilot/novice
- Break the script into modules by feature (Helpers, UI loader, FileCleaner, Media, Duplicates, Cleanup, Security, Metadata).
- For each module: extract functions into a module file (e.g., `PlatypusTools.FileCleaner.psm1`) with unit-testable pure functions where possible.
- Keep UI wiring in a thin file that calls into modules.
- Establish a test harness that runs PSScriptAnalyzer and basic smoke flows (dry-run of File Cleaner, parse check, UI instantiation in headless mode if possible).

---

This MANIFEST is intentionally lightweight to make it easy to expand. Add to it as we document functions and variables in detail.
