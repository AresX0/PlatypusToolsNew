using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.Services.Plugins;

namespace PlatypusTools.UI.Views
{
    public partial class PluginMarketplaceWindow : Window
    {
        public ObservableCollection<PluginRegistryEntry> Entries { get; } = new();

        public PluginMarketplaceWindow()
        {
            InitializeComponent();
            UrlBox.Text = PluginRegistryService.Instance.DefaultRegistryUrl;
            Grid.ItemsSource = Entries;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            StatusText.Text = "Loading registry...";
            Entries.Clear();
            try
            {
                var list = await PluginRegistryService.Instance.LoadAsync(UrlBox.Text?.Trim());
                foreach (var e in list) Entries.Add(e);
                StatusText.Text = $"Loaded {Entries.Count} plugin(s).";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed to load registry: " + ex.Message;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is PluginRegistryEntry entry)
            {
                StatusText.Text = $"Installing {entry.Id}...";
                try
                {
                    var dest = await PluginRegistryService.Instance.InstallAsync(entry);
                    StatusText.Text = $"Installed to {dest}";
                    MessageBox.Show($"Installed {entry.Name} {entry.Version}\n\nFile: {dest}",
                        "Plugin Marketplace", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Install failed: " + ex.Message;
                    MessageBox.Show("Install failed: " + ex.Message, "Plugin Marketplace",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void Homepage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is PluginRegistryEntry entry && !string.IsNullOrWhiteSpace(entry.Homepage))
            {
                try { Process.Start(new ProcessStartInfo(entry.Homepage) { UseShellExecute = true }); } catch { }
            }
        }
    }
}
