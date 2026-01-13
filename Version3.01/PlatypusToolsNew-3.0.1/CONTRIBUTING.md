# Contributing to PlatypusTools

Thanks for your interest in contributing! Please follow these simple guidelines.

- Fork the repo and create a descriptive branch for your change.
- Keep changes small and focused; one feature/fix per PR.
- Follow PowerShell best practices. The project uses PSScriptAnalyzer for linting.
- Before opening a PR:
  - Run `Invoke-ScriptAnalyzer -Path PlatypusTools.ps1` and fix critical warnings.
  - Test features locally on a Windows machine (PowerShell 5.1 or later).
- Include a short description of what your change does and why in the PR.
- For new features, add/update relevant documentation (`PlatypusTools_Help.html` or `README.md`).

We welcome contributions of all kinds: bug fixes, docs improvements, tests, or CI templates.