using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.UI.Views;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing application commands
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

        private void RegisterDefaultCommands()
        {
            // File commands
            Register(new CommandItem
            {
                Icon = "📂",
                Name = "Open Folder",
                Description = "Browse and open a folder",
                Category = "File",
                Keywords = new List<string> { "browse", "directory" },
                Shortcut = "Ctrl+O"
            });

            Register(new CommandItem
            {
                Icon = "💾",
                Name = "Save Workspace",
                Description = "Save current workspace settings",
                Category = "File",
                Keywords = new List<string> { "save", "export" },
                Shortcut = "Ctrl+S"
            });

            // View commands
            Register(new CommandItem
            {
                Icon = "🔍",
                Name = "Advanced Search",
                Description = "Open advanced file search",
                Category = "View",
                Keywords = new List<string> { "find", "search", "filter" },
                Shortcut = "Ctrl+Shift+F"
            });

            Register(new CommandItem
            {
                Icon = "🎨",
                Name = "Toggle Theme",
                Description = "Switch between light and dark theme",
                Category = "View",
                Keywords = new List<string> { "dark", "light", "appearance" }
            });

            // Tools commands
            Register(new CommandItem
            {
                Icon = "📋",
                Name = "Batch Operations",
                Description = "Open batch file operations",
                Category = "Tools",
                Keywords = new List<string> { "rename", "copy", "move", "bulk" },
                Shortcut = "Ctrl+B"
            });

            Register(new CommandItem
            {
                Icon = "⚙️",
                Name = "Settings",
                Description = "Open application settings",
                Category = "Tools",
                Keywords = new List<string> { "preferences", "options", "config" },
                Shortcut = "Ctrl+,"
            });

            // IDEA-002: Navigation commands for quick tab switching
            Register(new CommandItem
            {
                Icon = "📁",
                Name = "File Management",
                Description = "Go to File Management tab",
                Category = "Navigate",
                Keywords = new List<string> { "files", "cleaner", "duplicates", "robocopy" },
                Shortcut = "Ctrl+1"
            });

            Register(new CommandItem
            {
                Icon = "🎬",
                Name = "Multimedia",
                Description = "Go to Multimedia tab (Audio, Image, Video)",
                Category = "Navigate",
                Keywords = new List<string> { "audio", "player", "image", "video", "music" },
                Shortcut = "Ctrl+2"
            });

            Register(new CommandItem
            {
                Icon = "🔧",
                Name = "System Tools",
                Description = "Go to System Tools tab",
                Category = "Navigate",
                Keywords = new List<string> { "system", "process", "startup", "registry", "disk" },
                Shortcut = "Ctrl+3"
            });

            Register(new CommandItem
            {
                Icon = "🔒",
                Name = "Security",
                Description = "Go to Security tab",
                Category = "Navigate",
                Keywords = new List<string> { "security", "forensics", "hash", "wipe", "encryption" },
                Shortcut = "Ctrl+4"
            });

            Register(new CommandItem
            {
                Icon = "🌐",
                Name = "Network",
                Description = "Go to Network tab",
                Category = "Navigate",
                Keywords = new List<string> { "network", "ftp", "download", "remote", "browser" },
                Shortcut = "Ctrl+5"
            });

            Register(new CommandItem
            {
                Icon = "📦",
                Name = "Deployment",
                Description = "Go to Deployment tab",
                Category = "Navigate",
                Keywords = new List<string> { "intune", "package", "deploy" },
                Shortcut = "Ctrl+6"
            });

            // IDEA-002: Utility commands
            Register(new CommandItem
            {
                Icon = "↩️",
                Name = "Undo",
                Description = "Undo last file operation",
                Category = "Edit",
                Keywords = new List<string> { "undo", "revert", "back" },
                Shortcut = "Ctrl+Z"
            });

            Register(new CommandItem
            {
                Icon = "↪️",
                Name = "Redo",
                Description = "Redo last undone operation",
                Category = "Edit",
                Keywords = new List<string> { "redo", "forward" },
                Shortcut = "Ctrl+Y"
            });

            // Help commands
            Register(new CommandItem
            {
                Icon = "❓",
                Name = "Help",
                Description = "View help documentation",
                Category = "Help",
                Keywords = new List<string> { "docs", "documentation", "manual" },
                Shortcut = "F1"
            });

            Register(new CommandItem
            {
                Icon = "ℹ️",
                Name = "About",
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

