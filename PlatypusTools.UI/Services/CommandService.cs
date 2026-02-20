using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.Views;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing application commands and the Command Palette (Ctrl+Shift+P)
    /// </summary>
    public sealed class CommandService
    {
        private static readonly Lazy<CommandService> _instance = new(() => new CommandService());
        public static CommandService Instance => _instance.Value;

        private readonly Dictionary<string, CommandItem> _commands = new();
        private readonly Dictionary<KeyGesture, string> _shortcuts = new();

        public IReadOnlyDictionary<string, CommandItem> Commands => _commands;

        public event EventHandler<string>? CommandExecuted;

        private CommandService()
        {
            RegisterDefaultCommands();
        }

        #region Tab Navigation Helper

        /// <summary>
        /// Navigate to a tab by following a path of header names through nested TabControls.
        /// Example: NavigateToTabByHeaders("📁 File Management", "File Diff")
        /// </summary>
        public static void NavigateToTabByHeaders(params string[] headerPath)
        {
            var mainWindow = Application.Current?.MainWindow;
            if (mainWindow == null) return;

            TabControl? current = FindLogicalDescendant<TabControl>(mainWindow);
            foreach (var header in headerPath)
            {
                if (current == null) return;

                bool found = false;
                for (int i = 0; i < current.Items.Count; i++)
                {
                    if (current.Items[i] is TabItem tab)
                    {
                        var tabHeader = tab.Header?.ToString() ?? "";
                        if (tabHeader.Equals(header, StringComparison.OrdinalIgnoreCase) ||
                            tabHeader.Contains(header, StringComparison.OrdinalIgnoreCase))
                        {
                            current.SelectedIndex = i;
                            current = FindLogicalDescendant<TabControl>(tab);
                            found = true;
                            break;
                        }
                    }
                }
                if (!found) return;
            }
        }

        private static T? FindLogicalDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is T found) return found;
                if (child is DependencyObject depChild)
                {
                    var result = FindLogicalDescendant<T>(depChild);
                    if (result != null) return result;
                }
            }
            return null;
        }

        #endregion

        private void RegisterDefaultCommands()
        {
            // ── File commands ──
            Register(new CommandItem
            {
                Icon = "📂", Name = "Open Folder",
                Description = "Browse and open a folder",
                Category = "File",
                Keywords = new List<string> { "browse", "directory" },
                Shortcut = "Ctrl+O"
            });

            Register(new CommandItem
            {
                Icon = "💾", Name = "Save Workspace",
                Description = "Save current workspace settings",
                Category = "File",
                Keywords = new List<string> { "save", "export" },
                Shortcut = "Ctrl+S"
            });

            Register(new CommandItem
            {
                Icon = "📦", Name = "Backup Settings",
                Description = "Export all settings to a backup file",
                Category = "File",
                Keywords = new List<string> { "backup", "export", "settings", "save" },
                Execute = () => { /* Wired at startup from MainWindow */ }
            });

            Register(new CommandItem
            {
                Icon = "📥", Name = "Restore Settings",
                Description = "Import settings from a backup file",
                Category = "File",
                Keywords = new List<string> { "restore", "import", "settings", "load" },
                Execute = () => { /* Wired at startup from MainWindow */ }
            });

            // ── View commands ──
            Register(new CommandItem
            {
                Icon = "🔍", Name = "Advanced Search",
                Description = "Open advanced file search",
                Category = "View",
                Keywords = new List<string> { "find", "search", "filter" },
                Shortcut = "Ctrl+Shift+F"
            });

            Register(new CommandItem
            {
                Icon = "🎨", Name = "Toggle Theme",
                Description = "Switch between light and dark theme",
                Category = "View",
                Keywords = new List<string> { "dark", "light", "appearance" }
            });

            // ── Tools commands ──
            Register(new CommandItem
            {
                Icon = "📋", Name = "Batch Operations",
                Description = "Open batch file operations",
                Category = "Tools",
                Keywords = new List<string> { "rename", "copy", "move", "bulk" },
                Shortcut = "Ctrl+B"
            });

            Register(new CommandItem
            {
                Icon = "⚙️", Name = "Settings",
                Description = "Open application settings",
                Category = "Tools",
                Keywords = new List<string> { "preferences", "options", "config" },
                Shortcut = "Ctrl+,"
            });

            // ── Navigate: Top-Level Tabs ──
            Register(new CommandItem
            {
                Icon = "🏠", Name = "Dashboard",
                Description = "Go to Dashboard",
                Category = "Navigate",
                Keywords = new List<string> { "home", "overview", "status" },
                Execute = () => NavigateToTabByHeaders("Dashboard")
            });

            Register(new CommandItem
            {
                Icon = "📁", Name = "File Management",
                Description = "Go to File Management tab",
                Category = "Navigate",
                Keywords = new List<string> { "files", "cleaner", "duplicates", "robocopy" },
                Shortcut = "Ctrl+1",
                Execute = () => NavigateToTabByHeaders("File Management")
            });

            Register(new CommandItem
            {
                Icon = "🎬", Name = "Multimedia",
                Description = "Go to Multimedia tab (Audio, Image, Video)",
                Category = "Navigate",
                Keywords = new List<string> { "audio", "player", "image", "video", "music" },
                Shortcut = "Ctrl+2",
                Execute = () => NavigateToTabByHeaders("Multimedia")
            });

            Register(new CommandItem
            {
                Icon = "🖥️", Name = "System Tools",
                Description = "Go to System Tools tab",
                Category = "Navigate",
                Keywords = new List<string> { "system", "process", "startup", "registry", "disk" },
                Shortcut = "Ctrl+3",
                Execute = () => NavigateToTabByHeaders("System")
            });

            Register(new CommandItem
            {
                Icon = "🔒", Name = "Security",
                Description = "Go to Security tab",
                Category = "Navigate",
                Keywords = new List<string> { "security", "forensics", "hash", "wipe", "encryption" },
                Shortcut = "Ctrl+4",
                Execute = () => NavigateToTabByHeaders("Security")
            });

            Register(new CommandItem
            {
                Icon = "📋", Name = "Metadata",
                Description = "Go to Metadata Editor tab",
                Category = "Navigate",
                Keywords = new List<string> { "metadata", "tags", "properties" },
                Shortcut = "Ctrl+5",
                Execute = () => NavigateToTabByHeaders("Metadata")
            });

            Register(new CommandItem
            {
                Icon = "🔧", Name = "Tools",
                Description = "Go to Tools tab",
                Category = "Navigate",
                Keywords = new List<string> { "tools", "network", "pdf", "archive", "browser" },
                Shortcut = "Ctrl+6",
                Execute = () => NavigateToTabByHeaders("Tools")
            });

            // ── Navigate: File Management Sub-Tabs ──
            Register(new CommandItem
            {
                Icon = "🧹", Name = "File Cleaner",
                Description = "Go to File Cleaner",
                Category = "Navigate",
                Keywords = new List<string> { "clean", "delete", "junk", "temp" },
                Execute = () => NavigateToTabByHeaders("File Management", "File Cleaner")
            });

            Register(new CommandItem
            {
                Icon = "🔄", Name = "Duplicates Finder",
                Description = "Go to Duplicates Finder",
                Category = "Navigate",
                Keywords = new List<string> { "duplicate", "dedup", "same", "hash" },
                Execute = () => NavigateToTabByHeaders("File Management", "Duplicates")
            });

            Register(new CommandItem
            {
                Icon = "📂", Name = "Empty Folder Scanner",
                Description = "Go to Empty Folder Scanner",
                Category = "Navigate",
                Keywords = new List<string> { "empty", "folder", "cleanup" },
                Execute = () => NavigateToTabByHeaders("File Management", "Empty Folder Scanner")
            });

            Register(new CommandItem
            {
                Icon = "📋", Name = "Robocopy",
                Description = "Go to Robocopy file sync",
                Category = "Navigate",
                Keywords = new List<string> { "robocopy", "sync", "mirror", "copy" },
                Execute = () => NavigateToTabByHeaders("File Management", "Robocopy")
            });

            Register(new CommandItem
            {
                Icon = "☁", Name = "Cloud Sync",
                Description = "Go to Cloud Sync",
                Category = "Navigate",
                Keywords = new List<string> { "cloud", "onedrive", "gdrive", "dropbox", "sync" },
                Execute = () => NavigateToTabByHeaders("File Management", "Cloud Sync")
            });

            Register(new CommandItem
            {
                Icon = "📝", Name = "File Diff",
                Description = "Compare two files side-by-side",
                Category = "Navigate",
                Keywords = new List<string> { "diff", "compare", "merge", "difference" },
                Execute = () => NavigateToTabByHeaders("File Management", "File Diff")
            });

            Register(new CommandItem
            {
                Icon = "📦", Name = "Bulk File Mover",
                Description = "Organize files with rules-based mover",
                Category = "Navigate",
                Keywords = new List<string> { "move", "organize", "sort", "bulk", "rule" },
                Execute = () => NavigateToTabByHeaders("File Management", "Bulk File Mover")
            });

            Register(new CommandItem
            {
                Icon = "🔗", Name = "Symlink Manager",
                Description = "Create and manage symbolic links",
                Category = "Navigate",
                Keywords = new List<string> { "symlink", "symbolic", "junction", "hardlink", "link" },
                Execute = () => NavigateToTabByHeaders("File Management", "Symlink Manager")
            });

            // ── Navigate: Multimedia Sub-Tabs ──
            Register(new CommandItem
            {
                Icon = "🎵", Name = "Audio Player",
                Description = "Go to Audio Player",
                Category = "Navigate",
                Keywords = new List<string> { "audio", "music", "player", "mp3", "playlist" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Audio", "Audio Player")
            });

            Register(new CommandItem
            {
                Icon = "✂️", Name = "Audio Trim",
                Description = "Go to Audio Trimmer",
                Category = "Navigate",
                Keywords = new List<string> { "audio", "trim", "cut", "split" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Audio", "Audio Trim")
            });

            Register(new CommandItem
            {
                Icon = "🎤", Name = "Audio Transcription",
                Description = "Transcribe audio using Whisper AI",
                Category = "Navigate",
                Keywords = new List<string> { "transcribe", "whisper", "speech", "text", "stt", "subtitles" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Audio", "Audio Transcription")
            });

            Register(new CommandItem
            {
                Icon = "🖼️", Name = "Image Editor",
                Description = "Go to Image Editor",
                Category = "Navigate",
                Keywords = new List<string> { "image", "edit", "photo", "picture" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Image", "Image Edit")
            });

            Register(new CommandItem
            {
                Icon = "🔄", Name = "Image Converter",
                Description = "Go to Image Converter",
                Category = "Navigate",
                Keywords = new List<string> { "image", "convert", "png", "jpg", "webp" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Image", "Image Converter")
            });

            Register(new CommandItem
            {
                Icon = "📐", Name = "Image Resizer",
                Description = "Go to Image Resizer",
                Category = "Navigate",
                Keywords = new List<string> { "image", "resize", "scale", "dimension" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Image", "Image Resizer")
            });

            Register(new CommandItem
            {
                Icon = "🎬", Name = "Video Player",
                Description = "Go to Video Player",
                Category = "Navigate",
                Keywords = new List<string> { "video", "player", "watch", "mp4" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Video", "Video Player")
            });

            Register(new CommandItem
            {
                Icon = "✂️", Name = "Video Editor",
                Description = "Go to Video Editor",
                Category = "Navigate",
                Keywords = new List<string> { "video", "edit", "cut", "trim" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Video", "Video Editor")
            });

            Register(new CommandItem
            {
                Icon = "🔄", Name = "Video Converter",
                Description = "Go to Video Converter",
                Category = "Navigate",
                Keywords = new List<string> { "video", "convert", "encode", "transcode" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Video", "Video Converter")
            });

            Register(new CommandItem
            {
                Icon = "📊", Name = "Video Metadata",
                Description = "View and edit video metadata",
                Category = "Navigate",
                Keywords = new List<string> { "video", "metadata", "tags", "info", "ffprobe" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Video", "Video Metadata")
            });

            Register(new CommandItem
            {
                Icon = "🎞️", Name = "GIF Maker",
                Description = "Create animated GIFs from video",
                Category = "Navigate",
                Keywords = new List<string> { "gif", "animate", "animation", "convert" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Video", "GIF Maker")
            });

            Register(new CommandItem
            {
                Icon = "📚", Name = "Media Library",
                Description = "Go to Media Library",
                Category = "Navigate",
                Keywords = new List<string> { "library", "collection", "media", "browse" },
                Execute = () => NavigateToTabByHeaders("Multimedia", "Media Library")
            });

            // ── Navigate: System Sub-Tabs ──
            Register(new CommandItem
            {
                Icon = "💽", Name = "Disk Cleanup",
                Description = "Go to System Disk Cleanup",
                Category = "Navigate",
                Keywords = new List<string> { "disk", "cleanup", "space", "free" },
                Execute = () => NavigateToTabByHeaders("System", "Disk Cleanup")
            });

            Register(new CommandItem
            {
                Icon = "🔐", Name = "Privacy Cleaner",
                Description = "Go to Privacy Cleaner",
                Category = "Navigate",
                Keywords = new List<string> { "privacy", "history", "traces", "clean" },
                Execute = () => NavigateToTabByHeaders("System", "Privacy Cleaner")
            });

            Register(new CommandItem
            {
                Icon = "🚀", Name = "Startup Manager",
                Description = "Manage startup programs",
                Category = "Navigate",
                Keywords = new List<string> { "startup", "boot", "autorun", "programs" },
                Execute = () => NavigateToTabByHeaders("System", "Startup Manager")
            });

            Register(new CommandItem
            {
                Icon = "📊", Name = "Process Manager",
                Description = "View and manage running processes",
                Category = "Navigate",
                Keywords = new List<string> { "process", "task", "kill", "running" },
                Execute = () => NavigateToTabByHeaders("System", "Process Manager")
            });

            Register(new CommandItem
            {
                Icon = "🗓️", Name = "Scheduled Tasks",
                Description = "Manage scheduled tasks",
                Category = "Navigate",
                Keywords = new List<string> { "schedule", "task", "cron", "timer" },
                Execute = () => NavigateToTabByHeaders("System", "Scheduled Tasks")
            });

            Register(new CommandItem
            {
                Icon = "💻", Name = "Terminal",
                Description = "Open embedded terminal",
                Category = "Navigate",
                Keywords = new List<string> { "terminal", "console", "cmd", "powershell", "shell" },
                Execute = () => NavigateToTabByHeaders("System", "Terminal")
            });

            Register(new CommandItem
            {
                Icon = "💾", Name = "Scheduled Backup",
                Description = "Configure and run scheduled backups",
                Category = "Navigate",
                Keywords = new List<string> { "backup", "schedule", "zip", "archive", "automated" },
                Execute = () => NavigateToTabByHeaders("System", "Scheduled Backup")
            });

            Register(new CommandItem
            {
                Icon = "🌍", Name = "Environment Variables",
                Description = "Manage system and user environment variables",
                Category = "Navigate",
                Keywords = new List<string> { "environment", "variable", "env", "path", "system" },
                Execute = () => NavigateToTabByHeaders("System", "Environment Variables")
            });

            Register(new CommandItem
            {
                Icon = "⚙️", Name = "Windows Services",
                Description = "Manage Windows services",
                Category = "Navigate",
                Keywords = new List<string> { "service", "windows", "start", "stop", "restart" },
                Execute = () => NavigateToTabByHeaders("System", "Windows Services")
            });

            // ── Navigate: Security Sub-Tabs ──
            Register(new CommandItem
            {
                Icon = "👁", Name = "Folder Hider",
                Description = "Hide and unhide folders",
                Category = "Navigate",
                Keywords = new List<string> { "hide", "folder", "invisible", "protect" },
                Execute = () => NavigateToTabByHeaders("Security", "Folder Hider")
            });

            Register(new CommandItem
            {
                Icon = "🔬", Name = "Forensics Analyzer",
                Description = "Go to Forensics Analyzer",
                Category = "Navigate",
                Keywords = new List<string> { "forensics", "analyze", "evidence", "timeline" },
                Execute = () => NavigateToTabByHeaders("Security", "Forensics Analyzer")
            });

            Register(new CommandItem
            {
                Icon = "🔢", Name = "Hash Scanner",
                Description = "Scan files for hash matches",
                Category = "Navigate",
                Keywords = new List<string> { "hash", "md5", "sha256", "checksum", "verify" },
                Execute = () => NavigateToTabByHeaders("Security", "Hash Scanner")
            });

            Register(new CommandItem
            {
                Icon = "🔍", Name = "CVE Search",
                Description = "Search for CVE vulnerabilities",
                Category = "Navigate",
                Keywords = new List<string> { "cve", "vulnerability", "exploit", "security" },
                Execute = () => NavigateToTabByHeaders("Security", "CVE Search")
            });

            // ── Navigate: Tools Sub-Tabs ──
            Register(new CommandItem
            {
                Icon = "🌐", Name = "Website Downloader",
                Description = "Download entire websites",
                Category = "Navigate",
                Keywords = new List<string> { "website", "download", "wget", "scrape" },
                Execute = () => NavigateToTabByHeaders("Tools", "Website Downloader")
            });

            Register(new CommandItem
            {
                Icon = "📊", Name = "File Analyzer",
                Description = "Analyze file types and sizes",
                Category = "Navigate",
                Keywords = new List<string> { "analyze", "file", "type", "size", "statistics" },
                Execute = () => NavigateToTabByHeaders("Tools", "File Analyzer")
            });

            Register(new CommandItem
            {
                Icon = "💿", Name = "Disk Space Analyzer",
                Description = "Visualize disk space usage",
                Category = "Navigate",
                Keywords = new List<string> { "disk", "space", "usage", "treemap", "size" },
                Execute = () => NavigateToTabByHeaders("Tools", "Disk Space Analyzer")
            });

            Register(new CommandItem
            {
                Icon = "🌐", Name = "Network Tools",
                Description = "Ping, traceroute, DNS lookup",
                Category = "Navigate",
                Keywords = new List<string> { "network", "ping", "traceroute", "dns", "port" },
                Execute = () => NavigateToTabByHeaders("Tools", "Network Tools")
            });

            Register(new CommandItem
            {
                Icon = "📦", Name = "Archive Manager",
                Description = "Create and extract archives",
                Category = "Navigate",
                Keywords = new List<string> { "archive", "zip", "7z", "rar", "extract", "compress" },
                Execute = () => NavigateToTabByHeaders("Tools", "Archive Manager")
            });

            Register(new CommandItem
            {
                Icon = "📄", Name = "PDF Tools",
                Description = "Merge, split, and edit PDFs",
                Category = "Navigate",
                Keywords = new List<string> { "pdf", "merge", "split", "document" },
                Execute = () => NavigateToTabByHeaders("Tools", "PDF Tools")
            });

            Register(new CommandItem
            {
                Icon = "📷", Name = "Screenshot",
                Description = "Capture screenshots",
                Category = "Navigate",
                Keywords = new List<string> { "screenshot", "capture", "screen", "snip" },
                Execute = () => NavigateToTabByHeaders("Tools", "Screenshot")
            });

            Register(new CommandItem
            {
                Icon = "💿", Name = "Bootable USB Creator",
                Description = "Create bootable USB drives",
                Category = "Navigate",
                Keywords = new List<string> { "usb", "bootable", "iso", "flash" },
                Execute = () => NavigateToTabByHeaders("Tools", "Bootable USB Creator")
            });

            Register(new CommandItem
            {
                Icon = "📋", Name = "Clipboard History",
                Description = "View and manage clipboard history",
                Category = "Navigate",
                Keywords = new List<string> { "clipboard", "history", "copy", "paste", "clip" },
                Execute = () => NavigateToTabByHeaders("Tools", "Clipboard History")
            });

            Register(new CommandItem
            {
                Icon = "🌐", Name = "Web Browser",
                Description = "Open embedded web browser",
                Category = "Navigate",
                Keywords = new List<string> { "browser", "web", "internet", "url" },
                Execute = () => NavigateToTabByHeaders("Tools", "Web Browser")
            });

            Register(new CommandItem
            {
                Icon = "📺", Name = "Screen Recorder",
                Description = "Record your screen",
                Category = "Navigate",
                Keywords = new List<string> { "record", "screen", "video", "capture" },
                Execute = () => NavigateToTabByHeaders("Tools", "Screen Recorder")
            });

            // ── Edit commands ──
            Register(new CommandItem
            {
                Icon = "↩️", Name = "Undo",
                Description = "Undo last file operation",
                Category = "Edit",
                Keywords = new List<string> { "undo", "revert", "back" },
                Shortcut = "Ctrl+Z"
            });

            Register(new CommandItem
            {
                Icon = "↪️", Name = "Redo",
                Description = "Redo last undone operation",
                Category = "Edit",
                Keywords = new List<string> { "redo", "forward" },
                Shortcut = "Ctrl+Y"
            });

            // ── Help commands ──
            Register(new CommandItem
            {
                Icon = "❓", Name = "Help",
                Description = "View help documentation",
                Category = "Help",
                Keywords = new List<string> { "docs", "documentation", "manual" },
                Shortcut = "F1"
            });

            Register(new CommandItem
            {
                Icon = "ℹ️", Name = "About",
                Description = "About PlatypusTools",
                Category = "Help",
                Keywords = new List<string> { "version", "info" }
            });
        }

        public void Register(CommandItem command, Action? execute = null)
        {
            var id = $"{command.Category}.{command.Name}".ToLowerInvariant().Replace(" ", "_");
            
            if (execute != null)
                command.Execute = execute;

            _commands[id] = command;

            // Parse and register shortcut if present
            if (!string.IsNullOrEmpty(command.Shortcut))
            {
                try
                {
                    var gesture = ParseKeyGesture(command.Shortcut);
                    if (gesture != null)
                        _shortcuts[gesture] = id;
                }
                catch { /* Ignore invalid gestures */ }
            }
        }

        public void SetCommandAction(string category, string name, Action execute)
        {
            var id = $"{category}.{name}".ToLowerInvariant().Replace(" ", "_");
            if (_commands.TryGetValue(id, out var command))
            {
                command.Execute = execute;
            }
        }

        public void Execute(string commandId)
        {
            if (_commands.TryGetValue(commandId, out var command))
            {
                try
                {
                    command.Execute?.Invoke();
                    CommandExecuted?.Invoke(this, commandId);
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error($"Command execution failed: {commandId} - {ex.Message}");
                }
            }
        }

        public bool TryExecuteShortcut(Key key, ModifierKeys modifiers)
        {
            foreach (var kvp in _shortcuts)
            {
                if (kvp.Key.Key == key && kvp.Key.Modifiers == modifiers)
                {
                    Execute(kvp.Value);
                    return true;
                }
            }
            return false;
        }

        public IEnumerable<CommandItem> GetAllCommands()
        {
            return _commands.Values.OrderBy(c => c.Category).ThenBy(c => c.Name);
        }

        public IEnumerable<CommandItem> GetCommandsByCategory(string category)
        {
            return _commands.Values
                .Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name);
        }

        public void ShowCommandPalette(Window owner)
        {
            var commands = GetAllCommands().Where(c => c.Execute != null);
            CommandPaletteWindow.Show(owner, commands);
        }

        private static KeyGesture? ParseKeyGesture(string shortcut)
        {
            var parts = shortcut.Split('+');
            var modifiers = ModifierKeys.None;
            Key key = Key.None;

            foreach (var part in parts)
            {
                var normalized = part.Trim();
                
                if (normalized.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Control;
                else if (normalized.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Shift;
                else if (normalized.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Alt;
                else if (Enum.TryParse<Key>(normalized, true, out var parsedKey))
                    key = parsedKey;
            }

            return key != Key.None ? new KeyGesture(key, modifiers) : null;
        }
    }
}

