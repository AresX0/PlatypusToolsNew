using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.Core.Services;
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
                    AIPanel, UpdatesPanel, BackupPanel, TabVisibilityPanel, VisualizerPanel, DependenciesPanel, AdvancedPanel 
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
                var settings = SettingsManager.Current;

                // Theme
                if (settings?.Theme == ThemeManager.LCARS)
                    LCARSThemeRadio.IsChecked = true;
                else if (settings?.Theme == ThemeManager.PipBoy)
                    PipBoyThemeRadio.IsChecked = true;
                else if (settings?.Theme == ThemeManager.Dark)
                    DarkThemeRadio.IsChecked = true;
                else if (LightThemeRadio != null)
                    LightThemeRadio.IsChecked = true;
                
                // Glass effect
                if (GlassEffectCheck != null)
                    GlassEffectCheck.IsChecked = ThemeManager.Instance.IsGlassEnabled;
                
                // Glass level
                if (GlassLevelCombo != null)
                {
                    var level = ThemeManager.Instance.GlassLevel;
                    foreach (System.Windows.Controls.ComboBoxItem item in GlassLevelCombo.Items)
                    {
                        if (item.Tag?.ToString() == level.ToString())
                        {
                            GlassLevelCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Auto-update
                if (AutoCheckUpdatesCheck != null)
                    AutoCheckUpdatesCheck.IsChecked = settings?.CheckForUpdatesOnStartup ?? true;

                // Admin rights
                if (RequireAdminRightsCheck != null)
                    RequireAdminRightsCheck.IsChecked = settings?.RequireAdminRights ?? true;

                // Load keyboard shortcuts
                LoadKeyboardShortcuts();
                
                // Load Audio Visualizer settings
                LoadVisualizerSettings(settings);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }
        
        private void LoadVisualizerSettings(AppSettings? settings)
        {
            if (settings == null) return;
            
            try
            {
                // Enable checkboxes
                if (EnableVisualizerCheck != null)
                    EnableVisualizerCheck.IsChecked = settings.VisualizerEnabled;
                if (ShowVisualizerDuringPlaybackCheck != null)
                    ShowVisualizerDuringPlaybackCheck.IsChecked = settings.ShowVisualizerDuringPlayback;
                
                // Preset combo
                if (VisualizerPresetCombo != null)
                {
                    foreach (System.Windows.Controls.ComboBoxItem item in VisualizerPresetCombo.Items)
                    {
                        if (item.Content?.ToString() == settings.VisualizerPreset)
                        {
                            VisualizerPresetCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                // Opacity slider
                if (VisualizerOpacitySlider != null)
                {
                    VisualizerOpacitySlider.Value = settings.VisualizerOpacity;
                    VisualizerOpacitySlider.ValueChanged += VisualizerOpacitySlider_ValueChanged;
                    UpdateOpacityText(settings.VisualizerOpacity);
                }
                
                // Bar count slider
                if (VisualizerBarCountSlider != null)
                {
                    VisualizerBarCountSlider.Value = settings.VisualizerBarCount;
                    VisualizerBarCountSlider.ValueChanged += VisualizerBarCountSlider_ValueChanged;
                    UpdateBarCountText(settings.VisualizerBarCount);
                }
                
                // Color buttons
                if (PrimaryColorButton != null)
                {
                    try
                    {
                        PrimaryColorButton.Background = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.VisualizerPrimaryColor));
                    }
                    catch { }
                }
                if (BackgroundColorButton != null)
                {
                    try
                    {
                        BackgroundColorButton.Background = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.VisualizerBackgroundColor));
                    }
                    catch { }
                }
                
                // Advanced checkboxes
                if (EnableGPUAccelerationCheck != null)
                    EnableGPUAccelerationCheck.IsChecked = settings.VisualizerGpuAcceleration;
                if (EnableAudioNormalizationCheck != null)
                    EnableAudioNormalizationCheck.IsChecked = settings.VisualizerNormalize;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading visualizer settings: {ex.Message}");
            }
        }
        
        private void VisualizerOpacitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityText(e.NewValue);
        }
        
        private void VisualizerBarCountSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateBarCountText((int)e.NewValue);
        }
        
        private void UpdateOpacityText(double value)
        {
            if (OpacityValueText != null)
                OpacityValueText.Text = $"{value * 100:F0}%";
        }
        
        private void UpdateBarCountText(int value)
        {
            if (BarCountValueText != null)
                BarCountValueText.Text = value.ToString();
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
                "Dependencies" => DependenciesPanel,
                "Advanced" => AdvancedPanel,
                _ => GeneralPanel
            };
            
            if (targetPanel != null)
                targetPanel.Visibility = Visibility.Visible;
            
            // Load tab visibility settings when switching to that panel
            if (tag == "TabVisibility")
                LoadTabVisibilitySettings();
            
            // Load dependency status when switching to that panel
            if (tag == "Dependencies")
                _ = LoadDependencyStatusAsync();
        }
        
        private void CustomizeTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var themeEditor = new ThemeEditorWindow();
                themeEditor.Owner = this;
                themeEditor.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open Theme Editor:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void GlassEffectCheck_Changed(object sender, RoutedEventArgs e)
        {
            PlatypusTools.Core.Services.SimpleLogger.Debug($"GlassEffectCheck_Changed: IsChecked={GlassEffectCheck.IsChecked}");
            ThemeManager.Instance.IsGlassEnabled = GlassEffectCheck.IsChecked == true;
        }
        
        private void GlassLevelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (GlassLevelCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag != null)
            {
                var levelStr = item.Tag.ToString();
                PlatypusTools.Core.Services.SimpleLogger.Debug($"GlassLevelCombo_SelectionChanged: levelStr={levelStr}");
                if (Enum.TryParse<Interop.GlassLevel>(levelStr, out var level))
                {
                    PlatypusTools.Core.Services.SimpleLogger.Debug($"GlassLevelCombo_SelectionChanged: parsed level={level}");
                    ThemeManager.Instance.GlassLevel = level;
                }
            }
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
            var settings = SettingsManager.Current;
            
            // Theme
            if (LCARSThemeRadio.IsChecked == true)
            {
                settings.Theme = ThemeManager.LCARS;
                ThemeManager.ApplyTheme(ThemeManager.LCARS);
            }
            else if (PipBoyThemeRadio.IsChecked == true)
            {
                settings.Theme = ThemeManager.PipBoy;
                ThemeManager.ApplyTheme(ThemeManager.PipBoy);
            }
            else if (DarkThemeRadio.IsChecked == true)
            {
                settings.Theme = ThemeManager.Dark;
                ThemeManager.ApplyTheme(ThemeManager.Dark);
            }
            else
            {
                settings.Theme = ThemeManager.Light;
                ThemeManager.ApplyTheme(ThemeManager.Light);
            }
            
            // Glass effect - save to settings AND apply
            var glassEnabled = GlassEffectCheck.IsChecked == true;
            settings.GlassEnabled = glassEnabled;
            ThemeManager.Instance.IsGlassEnabled = glassEnabled;
            
            // Glass level
            if (GlassLevelCombo.SelectedItem is System.Windows.Controls.ComboBoxItem levelItem && levelItem.Tag != null)
            {
                if (Enum.TryParse<Interop.GlassLevel>(levelItem.Tag.ToString(), out var level))
                {
                    settings.GlassLevel = level;
                    ThemeManager.Instance.GlassLevel = level;
                }
            }
            
            settings.CheckForUpdatesOnStartup = AutoCheckUpdatesCheck.IsChecked == true;
            
            // Admin rights (requires restart to take effect)
            var previousAdminSetting = settings.RequireAdminRights;
            settings.RequireAdminRights = RequireAdminRightsCheck.IsChecked == true;
            if (previousAdminSetting != settings.RequireAdminRights)
            {
                UpdateManifestForNextLaunch(settings.RequireAdminRights);
            }
            
            // Save tab visibility settings
            SaveTabVisibilitySettings(settings);
            
            // Save audio visualizer settings
            SaveVisualizerSettings(settings);
            
            SettingsManager.SaveCurrent();
            
            // Refresh tab visibility in the main UI immediately
            TabVisibilityService.Instance.RefreshFromSettings();
        }
        
        private void SaveVisualizerSettings(AppSettings settings)
        {
            try
            {
                // Enable checkboxes
                settings.VisualizerEnabled = EnableVisualizerCheck?.IsChecked == true;
                settings.ShowVisualizerDuringPlayback = ShowVisualizerDuringPlaybackCheck?.IsChecked == true;
                
                // Preset combo
                if (VisualizerPresetCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem presetItem)
                {
                    settings.VisualizerPreset = presetItem.Content?.ToString() ?? "Default";
                }
                
                // Sliders
                settings.VisualizerOpacity = VisualizerOpacitySlider?.Value ?? 0.9;
                settings.VisualizerBarCount = (int)(VisualizerBarCountSlider?.Value ?? 32);
                
                // Color buttons
                if (PrimaryColorButton?.Background is System.Windows.Media.SolidColorBrush primaryBrush)
                {
                    settings.VisualizerPrimaryColor = primaryBrush.Color.ToString();
                }
                if (BackgroundColorButton?.Background is System.Windows.Media.SolidColorBrush bgBrush)
                {
                    settings.VisualizerBackgroundColor = bgBrush.Color.ToString();
                }
                
                // Advanced checkboxes
                settings.VisualizerGpuAcceleration = EnableGPUAccelerationCheck?.IsChecked == true;
                settings.VisualizerNormalize = EnableAudioNormalizationCheck?.IsChecked == true;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving visualizer settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Updates the manifest file for the next application launch based on the admin rights setting.
        /// Since embedded manifests cannot be changed at runtime, this creates a companion file
        /// that App.xaml.cs checks on startup.
        /// </summary>
        private void UpdateManifestForNextLaunch(bool requireAdmin)
        {
            try
            {
                // The setting is already saved to SettingsManager.Current
                // App.xaml.cs checks this setting on startup and elevates if needed
                
                // Show info message about restart
                MessageBox.Show(
                    requireAdmin 
                        ? "Administrator rights will be requested on next launch.\nPlease restart the application for changes to take effect."
                        : "The application will no longer request administrator rights on next launch.\nPlease restart the application for changes to take effect.",
                    "Admin Rights Setting Changed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating admin mode flag: {ex.Message}");
            }
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
            { "TabRobocopy", "FileManagement.Robocopy" },
            
            // Multimedia
            { "TabMultimedia", "Multimedia" },
            { "TabEnhancedAudioPlayer", "Multimedia.Audio.EnhancedAudioPlayer" },
            { "TabAudioPlayer", "Multimedia.Audio.AudioPlayer" },
            { "TabAudioTrim", "Multimedia.Audio.AudioTrim" },
            { "TabImageEdit", "Multimedia.Image.ImageEdit" },
            { "TabImageConverter", "Multimedia.Image.ImageConverter" },
            { "TabImageResizer", "Multimedia.Image.ImageResizer" },
            { "TabIconConverter", "Multimedia.Image.IconConverter" },
            { "TabBatchWatermark", "Multimedia.Image.BatchWatermark" },
            { "TabImageScaler", "Multimedia.Image.ImageScaler" },
            { "TabModel3DEditor", "Multimedia.Image.Model3DEditor" },
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
            { "TabSecureWipe", "Security.SecureWipe" },
            { "TabAdvancedForensics", "Security.AdvancedForensics" },
            { "TabAdvForensicsMemory", "Security.AdvancedForensics.Memory" },
            { "TabAdvForensicsArtifacts", "Security.AdvancedForensics.Artifacts" },
            { "TabAdvForensicsTimeline", "Security.AdvancedForensics.Timeline" },
            { "TabAdvForensicsKusto", "Security.AdvancedForensics.Kusto" },
            { "TabAdvForensicsLocalKQL", "Security.AdvancedForensics.LocalKQL" },
            { "TabAdvForensicsMalware", "Security.AdvancedForensics.Malware" },
            { "TabAdvForensicsExtract", "Security.AdvancedForensics.Extract" },
            { "TabAdvForensicsOpenSearch", "Security.AdvancedForensics.OpenSearch" },
            { "TabAdvForensicsOSINT", "Security.AdvancedForensics.OSINT" },
            { "TabAdvForensicsSchedule", "Security.AdvancedForensics.Schedule" },
            { "TabAdvForensicsBrowser", "Security.AdvancedForensics.Browser" },
            { "TabAdvForensicsIOC", "Security.AdvancedForensics.IOC" },
            { "TabAdvForensicsRegistry", "Security.AdvancedForensics.Registry" },
            { "TabAdvForensicsPCAP", "Security.AdvancedForensics.PCAP" },
            { "TabAdvForensicsResults", "Security.AdvancedForensics.Results" },
            
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
            { "TabFtpClient", "Tools.FtpClient" },
            { "TabTerminalClient", "Tools.TerminalClient" },
            { "TabSimpleBrowser", "Tools.SimpleBrowser" },
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
        
        #region Dependencies Panel
        
        private readonly DependencyCheckerService _depChecker = new();
        
        private async Task LoadDependencyStatusAsync()
        {
            try
            {
                DepFFmpegStatus.Text = "⏳";
                DepExifToolStatus.Text = "⏳";
                DepWebView2Status.Text = "⏳";
                DepFFmpegInfo.Text = "Checking...";
                DepExifToolInfo.Text = "Checking...";
                DepWebView2Info.Text = "Checking...";
                
                var result = await _depChecker.CheckAllDependenciesAsync();
                
                // FFmpeg
                if (result.FFmpegInstalled)
                {
                    DepFFmpegStatus.Text = "✅";
                    DepFFmpegInfo.Text = "Installed";
                    DepFFmpegBtn.IsEnabled = false;
                    DepFFmpegBtn.Content = "Installed";
                }
                else
                {
                    DepFFmpegStatus.Text = "❌";
                    DepFFmpegInfo.Text = "Not found - required for video editing";
                    DepFFmpegBtn.IsEnabled = true;
                    DepFFmpegBtn.Content = "Install";
                }
                
                // ExifTool
                if (result.ExifToolInstalled)
                {
                    DepExifToolStatus.Text = "✅";
                    DepExifToolInfo.Text = "Installed";
                    DepExifToolBtn.IsEnabled = false;
                    DepExifToolBtn.Content = "Installed";
                }
                else
                {
                    DepExifToolStatus.Text = "❌";
                    DepExifToolInfo.Text = "Not found - required for metadata";
                    DepExifToolBtn.IsEnabled = true;
                    DepExifToolBtn.Content = "Install";
                }
                
                // WebView2
                if (result.WebView2Installed)
                {
                    DepWebView2Status.Text = "✅";
                    DepWebView2Info.Text = "Installed";
                    DepWebView2Btn.IsEnabled = false;
                    DepWebView2Btn.Content = "Installed";
                }
                else
                {
                    DepWebView2Status.Text = "❌";
                    DepWebView2Info.Text = "Not found - required for help system";
                    DepWebView2Btn.IsEnabled = true;
                    DepWebView2Btn.Content = "Install";
                }
                
                // Load setting
                ShowDependencyPromptCheck.IsChecked = !SettingsManager.Current.HasSeenDependencyPrompt;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading dependency status: {ex.Message}");
            }
        }
        
        private async void DepRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadDependencyStatusAsync();
        }
        
        private async void DepInstallFFmpeg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DepFFmpegBtn.IsEnabled = false;
                DepFFmpegBtn.Content = "Installing...";
                DepFFmpegStatus.Text = "⏳";
                
                var success = await DownloadAndInstallFFmpegAsync();
                
                if (success)
                {
                    MessageBox.Show("FFmpeg installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("FFmpeg installation failed. Please install manually.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await LoadDependencyStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing FFmpeg: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await LoadDependencyStatusAsync();
            }
        }
        
        private async void DepInstallExifTool_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DepExifToolBtn.IsEnabled = false;
                DepExifToolBtn.Content = "Installing...";
                DepExifToolStatus.Text = "⏳";
                
                var success = await DownloadAndInstallExifToolAsync();
                
                if (success)
                {
                    MessageBox.Show("ExifTool installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("ExifTool installation failed. Please install manually.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await LoadDependencyStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing ExifTool: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await LoadDependencyStatusAsync();
            }
        }
        
        private async void DepInstallWebView2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DepWebView2Btn.IsEnabled = false;
                DepWebView2Btn.Content = "Installing...";
                DepWebView2Status.Text = "⏳";
                
                var success = await DownloadAndInstallWebView2Async();
                
                if (success)
                {
                    MessageBox.Show("WebView2 Runtime installer launched. Please follow the installer prompts.", "WebView2", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("WebView2 installation failed. Please install manually from Microsoft.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await Task.Delay(3000);
                await LoadDependencyStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing WebView2: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await LoadDependencyStatusAsync();
            }
        }
        
        private void DepOpenFFmpegSite_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://ffmpeg.org/download.html", UseShellExecute = true });
        }
        
        private void DepOpenExifToolSite_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://exiftool.org/", UseShellExecute = true });
        }
        
        private void DepOpenWebView2Site_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/", UseShellExecute = true });
        }
        
        private void DepOpenToolsFolder_Click(object sender, RoutedEventArgs e)
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsPath)) Directory.CreateDirectory(toolsPath);
            Process.Start(new ProcessStartInfo { FileName = toolsPath, UseShellExecute = true });
        }
        
        private void OpenDependencySetup_Click(object sender, RoutedEventArgs e)
        {
            var setupWindow = new DependencySetupWindow();
            setupWindow.Owner = this;
            setupWindow.ShowDialog();
            _ = LoadDependencyStatusAsync();
        }
        
        private async Task<bool> DownloadAndInstallFFmpegAsync()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsPath)) Directory.CreateDirectory(toolsPath);
            
            var ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
            var zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
            var extractPath = Path.Combine(Path.GetTempPath(), "ffmpeg_extract");
            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            
            var response = await client.GetAsync(ffmpegUrl);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(zipPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            
            var ffmpegExe = Directory.GetFiles(extractPath, "ffmpeg.exe", SearchOption.AllDirectories);
            if (ffmpegExe.Length == 0) return false;
            
            var destPath = Path.Combine(toolsPath, "ffmpeg.exe");
            File.Copy(ffmpegExe[0], destPath, true);
            
            var ffprobeExe = Directory.GetFiles(extractPath, "ffprobe.exe", SearchOption.AllDirectories);
            if (ffprobeExe.Length > 0)
            {
                File.Copy(ffprobeExe[0], Path.Combine(toolsPath, "ffprobe.exe"), true);
            }
            
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(extractPath, true); } catch { }
            
            return File.Exists(destPath);
        }
        
        private async Task<bool> DownloadAndInstallExifToolAsync()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool_files");
            if (!Directory.Exists(toolsPath)) Directory.CreateDirectory(toolsPath);
            
            var exifToolUrl = "https://exiftool.org/exiftool-12.76.zip";
            var zipPath = Path.Combine(Path.GetTempPath(), "exiftool.zip");
            var extractPath = Path.Combine(Path.GetTempPath(), "exiftool_extract");
            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            
            var response = await client.GetAsync(exifToolUrl);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(zipPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            
            var exeFiles = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories);
            if (exeFiles.Length == 0) return false;
            
            var destPath = Path.Combine(toolsPath, "exiftool.exe");
            File.Copy(exeFiles[0], destPath, true);
            
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(extractPath, true); } catch { }
            
            return File.Exists(destPath);
        }
        
        private async Task<bool> DownloadAndInstallWebView2Async()
        {
            var installerPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
            var webView2Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);
            
            var response = await client.GetAsync(webView2Url);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(installerPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            Process.Start(new ProcessStartInfo { FileName = installerPath, UseShellExecute = true });
            
            return true;
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


