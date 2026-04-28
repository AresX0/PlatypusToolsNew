using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using PlatypusTools.UI.Services.Files;

namespace PlatypusTools.UI.Views
{
    public partial class SmartRenameWindow : Window
    {
        public ObservableCollection<SmartRenameSuggestion> Items { get; } = new();
        private readonly SmartRenameService _svc = new();

        public SmartRenameWindow()
        {
            InitializeComponent();
            Grid.ItemsSource = Items;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
        }

        private async void Suggest_Click(object sender, RoutedEventArgs e)
        {
            var folder = FolderBox.Text?.Trim();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                StatusText.Text = "Pick a valid folder.";
                return;
            }
            Items.Clear();
            StatusText.Text = "Asking the LLM... (requires Ollama or LM Studio running)";
            try
            {
                var files = Directory.EnumerateFiles(folder).Take(20).ToList();
                var suggestions = await _svc.SuggestAsync(files, HintBox.Text);
                foreach (var s in suggestions) Items.Add(s);
                StatusText.Text = $"Got {Items.Count} suggestion(s) for {files.Count} file(s).";
            }
            catch (Exception ex) { StatusText.Text = "Error: " + ex.Message; }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (Items.Count == 0) { StatusText.Text = "Nothing to apply."; return; }
            var n = _svc.Apply(Items);
            StatusText.Text = $"Renamed {n} file(s).";
            Items.Clear();
        }
    }
}
