# PlatypusTools Website Setup Guide

## For JosephThePlatypus.com

### Step 1: Capture Screenshots

You need to capture screenshots of each feature. Open PlatypusTools and take screenshots of each tab:

**Recommended Settings:**
- Resolution: 1920x1080 or higher
- Use the Dark theme for consistency (or LCARS for uniqueness)
- Show the tool with sample data loaded when possible

**Screenshots Needed:**

| Filename | Tab/Feature to Capture |
|----------|------------------------|
| `platypus-logo.png` | Export your logo from Assets folder |
| `screenshot-dark-theme.png` | Main window in Dark mode |
| `screenshot-light-theme.png` | Main window in Light mode |
| `screenshot-lcars-theme.png` | Main window in LCARS mode |
| `screenshot-file-renamer.png` | File Management → File Renamer |
| `screenshot-duplicate-finder.png` | File Management → Duplicate Finder |
| `screenshot-disk-analyzer.png` | File Management → Disk Space Analyzer |
| `screenshot-empty-folders.png` | File Management → Empty Folder Scanner |
| `screenshot-file-analyzer.png` | File Management → File Analyzer |
| `screenshot-archive-manager.png` | Tools → Archive Manager |
| `screenshot-video-converter.png` | Multimedia → Video Converter |
| `screenshot-audio-editor.png` | Multimedia → Audio Tools |
| `screenshot-image-resizer.png` | Multimedia → Image Resizer |
| `screenshot-image-editor.png` | Multimedia → Image Edit |
| `screenshot-batch-watermark.png` | Multimedia → Batch Watermark |
| `screenshot-ai-upscaler.png` | Multimedia → AI Upscaler |
| `screenshot-icon-converter.png` | Multimedia → Icon Converter |
| `screenshot-screenshot-tool.png` | Tools → Screenshot |
| `screenshot-disk-cleanup.png` | System → Disk Cleanup |
| `screenshot-startup-manager.png` | System → Startup Manager |
| `screenshot-bootable-usb.png` | Tools → Bootable USB Creator |
| `screenshot-recent-cleaner.png` | System → Recent Cleaner |
| `screenshot-folder-hider.png` | Security → Folder Hider |
| `screenshot-file-shredder.png` | Security → File Shredder |
| `screenshot-privacy-cleaner.png` | Security → Privacy Cleaner |
| `screenshot-forensics.png` | Security → Forensics Analyzer |
| `screenshot-metadata-editor.png` | Metadata → Metadata Editor |
| `screenshot-metadata-stripper.png` | Metadata → Metadata Stripper |
| `screenshot-ftp-client.png` | Tools → Network Tools → FTP/SFTP |
| `screenshot-ssh-terminal.png` | Tools → Network Tools → SSH/Telnet |
| `screenshot-web-browser.png` | Tools → Web Browser |
| `screenshot-website-downloader.png` | Tools → Website Downloader |
| `screenshot-pdf-tools.png` | Tools → PDF Tools |
| `screenshot-plugin-manager.png` | Tools → Plugin Manager |

### Step 2: Optimize Screenshots

1. Resize all screenshots to a consistent width (recommended: 800px wide)
2. Compress using TinyPNG or similar to reduce file size
3. Save as PNG or WebP for best quality

### Step 3: Upload to WordPress

#### Option A: Use WordPress Block Editor (Gutenberg)

1. Log in to JosephThePlatypus.com WordPress admin
2. Go to **Pages → Add New**
3. Title the page "PlatypusTools" or "Download"
4. Click the **+** button and choose **Custom HTML** block
5. Paste the entire content from `platypustools-wordpress-page.html`
6. Upload all screenshots to the Media Library
7. Replace each `[UPLOAD: filename.png]` with the actual image URL

#### Option B: Use Classic Editor

1. Go to **Pages → Add New**
2. Switch to **Text/HTML** mode (not Visual)
3. Paste the HTML content
4. Upload images and replace placeholders

### Step 4: Replace Image Placeholders

Find and replace each placeholder with the actual WordPress image URL:

```
[UPLOAD: screenshot-dark-theme.png]
```

Replace with:

```
https://josephtheplatypus.com/wp-content/uploads/2026/01/screenshot-dark-theme.png
```

### Step 5: Set Up Page Settings

1. **Permalink:** Set to `/platypustools` or `/download`
2. **Template:** Use full-width template if available
3. **Featured Image:** Use the logo or a hero screenshot
4. **SEO Title:** "PlatypusTools - Free Windows File Management & Media Tools"
5. **Meta Description:** "Download PlatypusTools, a free all-in-one Windows application with 50+ tools for file management, multimedia editing, system utilities, and more."

### Step 6: Add to Navigation

1. Go to **Appearance → Menus**
2. Add the PlatypusTools page to your main navigation
3. Consider adding a prominent download button in the header

### Optional: Add Google Analytics Tracking

Add this to track downloads:

```html
<a href="..." class="pt-download-btn" onclick="gtag('event', 'download', {'event_category': 'PlatypusTools', 'event_label': 'MSI Installer'});">
```

### File Locations

- WordPress HTML: `website/platypustools-wordpress-page.html`
- This guide: `website/WEBSITE_SETUP.md`

### Live Links

After setup, your page will be available at:
- https://josephtheplatypus.com/platypustools

Download links point to:
- MSI: https://github.com/AresX0/PlatypusToolsNew/releases/download/v3.2.7.4/PlatypusToolsSetup.msi
- ZIP: https://github.com/AresX0/PlatypusToolsNew/releases/download/v3.2.7.4/PlatypusTools-3.2.7.4-Standalone.zip
