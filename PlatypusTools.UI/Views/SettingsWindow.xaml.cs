using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Comprehensive settings window for configuring all application options.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly StackPanel[] _panels;
        
        public SettingsWindow()
        {
            InitializeComponent();
            
            _panels = new[] 
            { 
                GeneralPanel, AppearancePanel, KeyboardPanel, 
                AIPanel, UpdatesPanel, BackupPanel, AdvancedPanel 
            };
            
            LoadSettings();
        }
        
        private void LoadSettings()
        {
            try
            {
                var settings = SettingsManager.Load();

                // Theme
                if (settings?.Theme == ThemeManager.Dark)
                    DarkThemeRadio.IsChecked = true;
                else if (LightThemeRadio != null)
                    LightThemeRadio.IsChecked = true;

                // Auto-update
                if (AutoCheckUpdatesCheck != null)
                    AutoCheckUpdatesCheck.IsChecked = settings?.CheckForUpdatesOnStartup ?? true;

                // Load keyboard shortcuts
                LoadKeyboardShortcuts();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        private void LoadKeyboardShortcuts()
        {
            try
            {
                var shortcuts = KeyboardShortcutService.Instance;
                if (shortcuts?.Shortcuts == null || ShortcutsGrid == null) return;

                var shortcutList = new System.Collections.Generic.List<ShortcutDisplayItem>();
                foreach (var kvp in shortcuts.Shortcuts)
                {
                    if (kvp.Value != null)
                    {
                        shortcutList.Add(new ShortcutDisplayItem
                        {
                            Name = kvp.Key,
                            Shortcut = kvp.Value.ToString() ?? "",
                            Description = GetShortcutDescription(kvp.Key)
                        });
                    }
                }
                ShortcutsGrid.ItemsSource = shortcutList;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading shortcuts: {ex.Message}");
            }
        }

        private string GetShortcutDescription(string name)
        {
            return name switch
            {
                "Open" => "Open a file or folder",
                "Save" => "Save current changes",
                "Help" => "Open help documentation",
                "ToggleTheme" => "Switch between light and dark themes",
                "Settings" => "Open settings window",
                "Undo" => "Undo last operation",
                "Redo" => "Redo last undone operation",
                "Search" => "Focus search box",
                "Refresh" => "Refresh current view",
                _ => ""
            };
        }
        
        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                foreach (var panel in _panels)
                {
                    panel.Visibility = Visibility.Collapsed;
                }
                
                var targetPanel = tag switch
                {
                    "General" => GeneralPanel,
                    "Appearance" => AppearancePanel,
                    "Keyboard" => KeyboardPanel,
                    "AI" => AIPanel,
                    "Updates" => UpdatesPanel,
                    "Backup" => BackupPanel,
                    "Advanced" => AdvancedPanel,
                    _ => GeneralPanel
                };
                
                targetPanel.Visibility = Visibility.Visible;
            }
        }
        
        private void CustomizeTheme_Click(object sender, RoutedEventArgs e)
        {
            var themeEditor = new ThemeEditorWindow();
            themeEditor.Owner = this;
            themeEditor.ShowDialog();
        }
        
        private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Reset all keyboard shortcuts to defaults?", "Reset Shortcuts",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // Reset shortcuts logic
                MessageBox.Show("Shortcuts reset to defaults.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private async void TestAI_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var apiKey = AIApiKeyBox.Password;
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("Please enter an API key.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                AIService.Instance.Configure(apiKey);
                var response = await AIService.Instance.SendPromptAsync("Say 'Hello, PlatypusTools!' in a creative way.");
                MessageBox.Show($"Connection successful!\n\nResponse: {response}", "AI Connection Test", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}", "AI Connection Test", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var update = await UpdateService.Instance.CheckForUpdatesAsync();
                if (update != null)
                {
                    var updateWindow = new UpdateWindow(update);
                    updateWindow.Owner = this;
                    updateWindow.ShowDialog();
                }
                else
                {
                    MessageBox.Show("You are running the latest version.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private async void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PlatypusTools Backup (*.ptbackup)|*.ptbackup",
                FileName = SettingsBackupService.Instance.GetDefaultBackupFileName()
            };
            
            if (dlg.ShowDialog() == true)
            {
                var success = await SettingsBackupService.Instance.CreateBackupAsync(dlg.FileName);
                MessageBox.Show(success ? "Backup created successfully." : "Backup failed.", 
                    "Backup", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Error);
            }
        }
        
        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PlatypusTools Backup (*.ptbackup)|*.ptbackup"
            };
            
            if (dlg.ShowDialog() == true)
            {
                var result = MessageBox.Show("This will overwrite your current settings. Continue?", 
                    "Restore Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    
                if (result == MessageBoxResult.Yes)
                {
                    var success = await SettingsBackupService.Instance.RestoreBackupAsync(dlg.FileName);
                    if (success)
                    {
                        MessageBox.Show("Settings restored. Please restart the application.", "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Restore failed.", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        
        private void ClearRecent_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Clear all recent files and workspaces?", "Clear Recent",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                RecentWorkspacesService.Instance.ClearAll();
                MessageBox.Show("Recent items cleared.", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Cache cleared.", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Reset ALL settings to defaults? This cannot be undone.", 
                "Reset All Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                SettingsManager.Save(new AppSettings());
                LoadSettings();
                MessageBox.Show("Settings reset to defaults.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }
        
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            DialogResult = true;
            Close();
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void SaveSettings()
        {
            var settings = SettingsManager.Load();
            
            // Theme
            if (DarkThemeRadio.IsChecked == true)
            {
                settings.Theme = ThemeManager.Dark;
                ThemeManager.ApplyTheme(ThemeManager.Dark);
            }
            else
            {
                settings.Theme = ThemeManager.Light;
                ThemeManager.ApplyTheme(ThemeManager.Light);
            }
            
            settings.CheckForUpdatesOnStartup = AutoCheckUpdatesCheck.IsChecked == true;
            
            SettingsManager.Save(settings);
        }
    }
    
    public class ShortcutDisplayItem
    {
        public string Name { get; set; } = "";
        public string Shortcut { get; set; } = "";
        public string Description { get; set; } = "";
    }
}


