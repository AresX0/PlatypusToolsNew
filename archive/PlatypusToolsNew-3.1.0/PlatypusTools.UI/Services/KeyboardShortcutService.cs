using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Manages configurable keyboard shortcuts for application commands.
    /// Supports saving/loading shortcuts and detecting conflicts.
    /// </summary>
    public class KeyboardShortcutService
    {
        private static KeyboardShortcutService? _instance;
        public static KeyboardShortcutService Instance => _instance ??= new KeyboardShortcutService();

        private readonly Dictionary<string, KeyGesture> _shortcuts = new();
        private readonly Dictionary<string, Action> _commandActions = new();
        private readonly string _shortcutsPath;

        public KeyboardShortcutService()
        {
            _shortcutsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "shortcuts.json");
            
            LoadDefaultShortcuts();
            LoadUserShortcuts();
        }

        /// <summary>
        /// Gets all registered shortcuts.
        /// </summary>
        public IReadOnlyDictionary<string, KeyGesture> Shortcuts => _shortcuts;

        /// <summary>
        /// Registers a command with its action and optional default shortcut.
        /// </summary>
        public void RegisterCommand(string commandId, Action action, KeyGesture? defaultGesture = null)
        {
            _commandActions[commandId] = action;
            if (defaultGesture != null && !_shortcuts.ContainsKey(commandId))
            {
                _shortcuts[commandId] = defaultGesture;
            }
        }

        /// <summary>
        /// Sets a shortcut for a command.
        /// </summary>
        public void SetShortcut(string commandId, KeyGesture gesture)
        {
            _shortcuts[commandId] = gesture;
            SaveUserShortcuts();
        }

        /// <summary>
        /// Removes a shortcut from a command.
        /// </summary>
        public void RemoveShortcut(string commandId)
        {
            _shortcuts.Remove(commandId);
            SaveUserShortcuts();
        }

        /// <summary>
        /// Gets the shortcut for a command, or null if not set.
        /// </summary>
        public KeyGesture? GetShortcut(string commandId)
        {
            return _shortcuts.TryGetValue(commandId, out var gesture) ? gesture : null;
        }

        /// <summary>
        /// Gets the display string for a shortcut (e.g., "Ctrl+O").
        /// </summary>
        public string? GetShortcutDisplayString(string commandId)
        {
            var gesture = GetShortcut(commandId);
            if (gesture == null) return null;

            var parts = new List<string>();
            if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(gesture.Key.ToString());

            return string.Join("+", parts);
        }

        /// <summary>
        /// Handles a key press and executes the corresponding command if matched.
        /// </summary>
        public bool HandleKeyPress(Key key, ModifierKeys modifiers)
        {
            foreach (var (commandId, gesture) in _shortcuts)
            {
                if (gesture.Key == key && gesture.Modifiers == modifiers)
                {
                    if (_commandActions.TryGetValue(commandId, out var action))
                    {
                        action();
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a shortcut is already in use by another command.
        /// </summary>
        public string? GetConflictingCommand(string excludeCommandId, KeyGesture gesture)
        {
            foreach (var (commandId, existing) in _shortcuts)
            {
                if (commandId != excludeCommandId &&
                    existing.Key == gesture.Key &&
                    existing.Modifiers == gesture.Modifiers)
                {
                    return commandId;
                }
            }
            return null;
        }

        /// <summary>
        /// Resets all shortcuts to defaults.
        /// </summary>
        public void ResetToDefaults()
        {
            _shortcuts.Clear();
            LoadDefaultShortcuts();
            SaveUserShortcuts();
        }

        private void LoadDefaultShortcuts()
        {
            // File menu
            _shortcuts["File.Open"] = new KeyGesture(Key.O, ModifierKeys.Control);
            _shortcuts["File.Save"] = new KeyGesture(Key.S, ModifierKeys.Control);
            _shortcuts["File.SaveAs"] = new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift);
            _shortcuts["File.Exit"] = new KeyGesture(Key.F4, ModifierKeys.Alt);

            // Edit menu
            _shortcuts["Edit.Undo"] = new KeyGesture(Key.Z, ModifierKeys.Control);
            _shortcuts["Edit.Redo"] = new KeyGesture(Key.Y, ModifierKeys.Control);
            _shortcuts["Edit.SelectAll"] = new KeyGesture(Key.A, ModifierKeys.Control);
            _shortcuts["Edit.Find"] = new KeyGesture(Key.F, ModifierKeys.Control);

            // View menu
            _shortcuts["View.ToggleTheme"] = new KeyGesture(Key.D, ModifierKeys.Control | ModifierKeys.Shift);
            _shortcuts["View.FullScreen"] = new KeyGesture(Key.F11, ModifierKeys.None);
            _shortcuts["View.ZoomIn"] = new KeyGesture(Key.OemPlus, ModifierKeys.Control);
            _shortcuts["View.ZoomOut"] = new KeyGesture(Key.OemMinus, ModifierKeys.Control);

            // Tools
            _shortcuts["Tools.FileCleaner"] = new KeyGesture(Key.D1, ModifierKeys.Control);
            _shortcuts["Tools.Duplicates"] = new KeyGesture(Key.D2, ModifierKeys.Control);
            _shortcuts["Tools.VideoConverter"] = new KeyGesture(Key.D3, ModifierKeys.Control);
            _shortcuts["Tools.Metadata"] = new KeyGesture(Key.D4, ModifierKeys.Control);
            _shortcuts["Tools.EmptyFolders"] = new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Alt);

            // Help
            _shortcuts["Help.Documentation"] = new KeyGesture(Key.F1, ModifierKeys.None);
            _shortcuts["Help.About"] = new KeyGesture(Key.F1, ModifierKeys.Shift);
        }

        private void LoadUserShortcuts()
        {
            try
            {
                if (!File.Exists(_shortcutsPath)) return;

                var json = File.ReadAllText(_shortcutsPath);
                var userShortcuts = JsonSerializer.Deserialize<Dictionary<string, ShortcutData>>(json);
                
                if (userShortcuts != null)
                {
                    foreach (var (commandId, data) in userShortcuts)
                    {
                        if (Enum.TryParse<Key>(data.Key, out var key) &&
                            Enum.TryParse<ModifierKeys>(data.Modifiers, out var modifiers))
                        {
                            _shortcuts[commandId] = new KeyGesture(key, modifiers);
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors loading user shortcuts
            }
        }

        private void SaveUserShortcuts()
        {
            try
            {
                var dir = Path.GetDirectoryName(_shortcutsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var data = new Dictionary<string, ShortcutData>();
                foreach (var (commandId, gesture) in _shortcuts)
                {
                    data[commandId] = new ShortcutData
                    {
                        Key = gesture.Key.ToString(),
                        Modifiers = gesture.Modifiers.ToString()
                    };
                }

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_shortcutsPath, json);
            }
            catch
            {
                // Ignore errors saving shortcuts
            }
        }

        private class ShortcutData
        {
            public string Key { get; set; } = string.Empty;
            public string Modifiers { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Represents a keyboard shortcut configuration item for UI binding.
    /// </summary>
    public class ShortcutItem
    {
        public string CommandId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ShortcutDisplay { get; set; } = string.Empty;
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; }
    }
}
