using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlatypusTools.UI.Views
{
    public partial class CommandPaletteWindow : Window
    {
        private List<CommandItem> _allCommands = new();
        private List<CommandItem> _filteredCommands = new();

        public CommandPaletteWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => SearchBox.Focus();
            Deactivated += (s, e) => Close();
        }

        public void SetCommands(IEnumerable<CommandItem> commands)
        {
            _allCommands = commands.ToList();
            _filteredCommands = _allCommands;
            CommandList.ItemsSource = _filteredCommands;
            
            if (_filteredCommands.Count > 0)
                CommandList.SelectedIndex = 0;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim().ToLowerInvariant();
            
            if (string.IsNullOrEmpty(query))
            {
                _filteredCommands = _allCommands;
            }
            else
            {
                _filteredCommands = _allCommands
                    .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                c.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                c.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(c => c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            CommandList.ItemsSource = _filteredCommands;
            
            if (_filteredCommands.Count > 0)
                CommandList.SelectedIndex = 0;
                
            StatusText.Text = $"{_filteredCommands.Count} commands  |  ↑↓ Navigate  Enter Execute  Esc Close";
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    if (CommandList.SelectedIndex < CommandList.Items.Count - 1)
                        CommandList.SelectedIndex++;
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (CommandList.SelectedIndex > 0)
                        CommandList.SelectedIndex--;
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ExecuteSelectedCommand();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private void CommandList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExecuteSelectedCommand();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelectedCommand();
        }

        private void ExecuteSelectedCommand()
        {
            if (CommandList.SelectedItem is CommandItem command)
            {
                Close();
                command.Execute?.Invoke();
            }
        }

        public static void Show(Window owner, IEnumerable<CommandItem> commands)
        {
            var palette = new CommandPaletteWindow { Owner = owner };
            palette.SetCommands(commands);
            palette.ShowDialog();
        }
    }

    public class CommandItem
    {
        public string Icon { get; set; } = "▸";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Shortcut { get; set; } = "";
        public string Category { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
        public Action? Execute { get; set; }

        public CommandItem() { }

        public CommandItem(string name, string description, Action execute)
        {
            Name = name;
            Description = description;
            Execute = execute;
        }
    }
}
