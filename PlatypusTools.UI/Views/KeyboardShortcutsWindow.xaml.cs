using System.Collections.Generic;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class KeyboardShortcutsWindow : Window
    {
        public KeyboardShortcutsWindow()
        {
            InitializeComponent();
            ShortcutsList.ItemsSource = new List<Shortcut>
            {
                new("Ctrl+Shift+P", "Open the global Command Palette"),
                new("Ctrl+/",        "Show this keyboard shortcut overlay"),
                new("Ctrl+Z",        "Undo last action (where supported)"),
                new("Ctrl+Y",        "Redo (where supported)"),
                new("Ctrl+Shift+A",  "AD Security unlock dialog"),
                new("Ctrl+1 .. 9",   "Jump to top-level tab 1–9"),
                new("Ctrl+Enter",    "Run script in Scripting Console"),
                new("F1",            "Show in-app help"),
                new("Shift+F1",      "About PlatypusTools"),
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public sealed record Shortcut(string Key, string Description);
    }
}
