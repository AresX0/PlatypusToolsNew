# Quick Start - Upload to GitHub

## TL;DR - Fast Upload Commands

Copy and paste these commands (replace `yourusername` with your GitHub username):

```powershell
# Step 1: Navigate to project
cd C:\Projects\Platypustools\PlatypusTools.Net

# Step 2: Initialize git (if not done)
git init

# Step 3: Configure git (first time only)
git config --global user.name "Your Name"
git config --global user.email "your.email@example.com"

# Step 4: Create repository on GitHub first!
# Go to: https://github.com/new
# Name it: PlatypusTools
# Don't initialize with README (we have one)
# Click "Create repository"

# Step 5: Add remote (replace 'yourusername' with your GitHub username)
git remote add origin https://github.com/yourusername/PlatypusTools.git

# Step 6: Add all files
git add .

# Step 7: Commit
git commit -m "Initial commit - PlatypusTools.NET v1.0 - 100% complete with 26 features"

# Step 8: Push to GitHub
git branch -M main
git push -u origin main
```

## What Files Will Be Uploaded?

✅ **Included** (will upload):
- All source code (`.cs`, `.xaml`)
- Project files (`.csproj`, `.sln`, `.wixproj`)
- Documentation (`.md` files)
- Configuration files
- `.gitignore`

❌ **Excluded** (won't upload - see `.gitignore`):
- `bin/` and `obj/` folders
- `.vs/` folder
- `*.user` files
- Test results
- Built MSI (upload separately to Releases)

## Repository Size

Expected upload size: **~50-100 MB** (source code only, no binaries)

## Upload MSI Separately

The MSI installer should be uploaded as a Release, not with source code:

```powershell
# 1. Build MSI first
cd C:\Projects\Platypustools\PlatypusTools.Net
dotnet build PlatypusTools.Installer\PlatypusTools.Installer.wixproj -c Release

# 2. MSI Location:
# C:\Projects\Platypustools\PlatypusTools.Net\PlatypusTools.Installer\bin\x64\Release\PlatypusToolsSetup.msi

# 3. Upload to GitHub Releases:
# - Go to: https://github.com/yourusername/PlatypusTools/releases/new
# - Tag: v1.0.0
# - Title: "PlatypusTools.NET v1.0.0 - Complete Release"
# - Attach: PlatypusToolsSetup.msi (drag and drop)
# - Publish release
```

## Verification Checklist

After upload, verify:
- ☑ README.md displays correctly on GitHub homepage
- ☑ BUILD.md has complete build instructions
- ☑ All source files are present
- ☑ `.gitignore` is working (no bin/obj folders visible)
- ☑ MSI is in Releases section (not in source tree)

## Common Issues

**"Permission denied"**
→ Use HTTPS URL: `https://github.com/yourusername/PlatypusTools.git`

**"Authentication failed"**
→ Use Personal Access Token: https://github.com/settings/tokens

**"Repository not found"**
→ Create repository on GitHub first: https://github.com/new

## Full Documentation

For detailed instructions, see:
- [GITHUB_UPLOAD_GUIDE.md](GITHUB_UPLOAD_GUIDE.md) - Complete guide
- [BUILD.md](BUILD.md) - Build instructions for MSI
- [README.md](README.md) - Project overview

---

**Estimated Time**: 5-10 minutes for first upload
