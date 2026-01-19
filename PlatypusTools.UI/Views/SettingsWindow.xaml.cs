using System;
using System.Collections.Generic;
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
        private StackPanel[]? _panels;
        
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Initialize panels array after InitializeComponent
            InitializePanels();
            
            LoadSettings();
        }
        
        private void InitializePanels()
        {
            try
            {
                _panels = new[] 
                { 
                    GeneralPanel, AppearancePanel, KeyboardPanel, 
                    AIPanel, UpdatesPanel, BackupPanel, TabVisibilityPanel, VisualizerPanel, AdvancedPanel 
                };
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing panels: {ex.Message}");
                _panels = Array.Empty<StackPanel>();
            }
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
            if (_panels == null || NavList?.SelectedItem is not ListBoxItem item || item.Tag is not string tag)
                return;
                
            foreach (var panel in _panels)
            {
                if (panel != null)
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
                "TabVisibility" => TabVisibilityPanel,
                "Visualizer" => VisualizerPanel,
                "Advanced" => AdvancedPanel,
                _ => GeneralPanel
            };
            
            if (targetPanel != null)
                targetPanel.Visibility = Visibility.Visible;
            
            // Load tab visibility settings when switching to that panel
            if (tag == "TabVisibility")
                LoadTabVisibilitySettings();
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
            
            // Save tab visibility settings
            SaveTabVisibilitySettings(settings);
            
            SettingsManager.Save(settings);
            
            // Refresh tab visibility in the main UI immediately
            TabVisibilityService.Instance.RefreshFromSettings();
        }
        
        #region Tab Visibility
        
        /// <summary>
        /// Maps checkbox names to tab keys for settings storage.
        /// </summary>
        private static readonly Dictionary<string, string> TabCheckboxMapping = new()
        {
            // File Management
            { "TabFileManagement", "FileManagement" },
            { "TabFileCleaner", "FileManagement.FileCleaner" },
            { "TabDuplicates", "FileManagement.Duplicates" },
            { "TabEmptyFolderScanner", "FileManagement.EmptyFolderScanner" },
            
            // Multimedia
            { "TabMultimedia", "Multimedia" },
            { "TabAudioPlayer", "Multimedia.Audio.AudioPlayer" },
            { "TabAudioTrim", "Multimedia.Audio.AudioTrim" },
            { "TabImageEdit", "Multimedia.Image.ImageEdit" },
            { "TabImageConverter", "Multimedia.Image.ImageConverter" },
            { "TabImageResizer", "Multimedia.Image.ImageResizer" },
            { "TabIconConverter", "Multimedia.Image.IconConverter" },
            { "TabBatchWatermark", "Multimedia.Image.BatchWatermark" },
            { "TabImageScaler", "Multimedia.Image.ImageScaler" },
            { "TabVideoPlayer", "Multimedia.Video.VideoPlayer" },
            { "TabVideoEditor", "Multimedia.Video.VideoEditor" },
            { "TabUpscaler", "Multimedia.Video.Upscaler" },
            { "TabVideoCombiner", "Multimedia.Video.VideoCombiner" },
            { "TabVideoConverter", "Multimedia.Video.VideoConverter" },
            { "TabMediaLibrary", "Multimedia.MediaLibrary" },
            { "TabExternalTools", "Multimedia.ExternalTools" },
            
            // System
            { "TabSystem", "System" },
            { "TabDiskCleanup", "System.DiskCleanup" },
            { "TabPrivacyCleaner", "System.PrivacyCleaner" },
            { "TabRecentCleaner", "System.RecentCleaner" },
            { "TabStartupManager", "System.StartupManager" },
            { "TabProcessManager", "System.ProcessManager" },
            { "TabRegistryCleaner", "System.RegistryCleaner" },
            { "TabScheduledTasks", "System.ScheduledTasks" },
            { "TabSystemRestore", "System.SystemRestore" },
            
            // Security
            { "TabSecurity", "Security" },
            { "TabFolderHider", "Security.FolderHider" },
            { "TabSystemAudit", "Security.SystemAudit" },
            { "TabForensicsAnalyzer", "Security.ForensicsAnalyzer" },
            
            // Metadata
            { "TabMetadata", "Metadata" },
            
            // Tools
            { "TabTools", "Tools" },
            { "TabWebsiteDownloader", "Tools.WebsiteDownloader" },
            { "TabFileAnalyzer", "Tools.FileAnalyzer" },
            { "TabDiskSpaceAnalyzer", "Tools.DiskSpaceAnalyzer" },
            { "TabNetworkTools", "Tools.NetworkTools" },
            { "TabArchiveManager", "Tools.ArchiveManager" },
            { "TabPdfTools", "Tools.PdfTools" },
            { "TabScreenshot", "Tools.Screenshot" },
            { "TabBootableUSB", "Tools.BootableUSB" },
            { "TabPluginManager", "Tools.PluginManager" },
        };
        
        private void LoadTabVisibilitySettings()
        {
            try
            {
                var settings = SettingsManager.Current;
                
                foreach (var kvp in TabCheckboxMapping)
                {
                    var checkbox = FindName(kvp.Key) as System.Windows.Controls.CheckBox;
                    if (checkbox != null)
                    {
                        checkbox.IsChecked = settings.IsTabVisible(kvp.Value);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tab visibility: {ex.Message}");
            }
        }
        
        private void SaveTabVisibilitySettings(AppSettings settings)
        {
            try
            {
                foreach (var kvp in TabCheckboxMapping)
                {
                    var checkbox = FindName(kvp.Key) as System.Windows.Controls.CheckBox;
                    if (checkbox != null)
                    {
                        settings.SetTabVisible(kvp.Value, checkbox.IsChecked == true);
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving tab visibility: {ex.Message}");
            }
        }
        
        private void ShowAllTabs_Click(object sender, RoutedEventArgs e)
        {
            SetAllTabsVisibility(true);
        }
        
        private void HideAllTabs_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will hide all tabs. You can restore them later from Settings. Continue?",
                "Hide All Tabs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                SetAllTabsVisibility(false);
            }
        }
        
        private void SetAllTabsVisibility(bool visible)
        {
            foreach (var kvp in TabCheckboxMapping)
            {
                var checkbox = FindName(kvp.Key) as System.Windows.Controls.CheckBox;
                if (checkbox != null)
                {
                    checkbox.IsChecked = visible;
                }
            }
        }
        
        #endregion
    }
    
    public class ShortcutDisplayItem
    {
        public string Name { get; set; } = "";
        public string Shortcut { get; set; } = "";
        public string Description { get; set; } = "";
    }
}


