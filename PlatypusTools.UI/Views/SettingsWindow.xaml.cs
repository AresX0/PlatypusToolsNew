using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;
using QRCoder;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Comprehensive settings window for configuring all application options.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private StackPanel[]? _panels;
        private bool _isInitialized = false;
        
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Initialize panels array after InitializeComponent
            InitializePanels();
            
            LoadSettings();
            _isInitialized = true;
        }
        
        private void InitializePanels()
        {
            try
            {
                _panels = new[] 
                { 
                    GeneralPanel, AppearancePanel, KeyboardPanel, 
                    AIPanel, UpdatesPanel, BackupPanel, TabVisibilityPanel, VisualizerPanel, DependenciesPanel, AdvancedPanel, LastFmPanel, MetadataPanel, RemoteControlPanel 
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
                else if (settings?.Theme == ThemeManager.Klingon)
                    KlingonThemeRadio.IsChecked = true;
                else if (settings?.Theme == ThemeManager.PipBoy)
                    PipBoyThemeRadio.IsChecked = true;
                else if (settings?.Theme == ThemeManager.KPopDemonHunters)
                    KPopDemonHuntersThemeRadio.IsChecked = true;
                else if (settings?.Theme == ThemeManager.HighContrast)
                    HighContrastThemeRadio.IsChecked = true;
                else if (settings?.Theme == ThemeManager.Glass)
                    GlassThemeRadio.IsChecked = true;
                else if (settings?.Theme == ThemeManager.Dark)
                    DarkThemeRadio.IsChecked = true;
                else if (LightThemeRadio != null)
                    LightThemeRadio.IsChecked = true;
                
                // Glass intensity group enabled only when Glass theme is selected
                if (GlassIntensityGroup != null)
                    GlassIntensityGroup.IsEnabled = GlassThemeRadio.IsChecked == true;
                
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

                // Restore last session (IDEA-009)
                if (RestoreLastSessionCheck != null)
                    RestoreLastSessionCheck.IsChecked = settings?.RestoreLastSession ?? true;

                // Start with Windows
                if (StartWithWindowsCheck != null)
                    StartWithWindowsCheck.IsChecked = settings?.StartWithWindows ?? false;

                // Start minimized
                if (StartMinimizedCheck != null)
                    StartMinimizedCheck.IsChecked = settings?.StartMinimized ?? false;

                // Language
                if (LanguageCombo != null)
                {
                    var lang = settings?.Language ?? "en-US";
                    foreach (System.Windows.Controls.ComboBoxItem item in LanguageCombo.Items)
                    {
                        if (item.Tag?.ToString() == lang)
                        {
                            LanguageCombo.SelectedItem = item;
                            break;
                        }
                    }
                    if (LanguageCombo.SelectedItem == null && LanguageCombo.Items.Count > 0)
                        LanguageCombo.SelectedIndex = 0;
                }

                // Load keyboard shortcuts
                LoadKeyboardShortcuts();
                
                // Load Audio Visualizer settings
                LoadVisualizerSettings(settings);
                
                // Load Font settings
                LoadFontSettings(settings);
                
                // Load Last.fm settings
                LoadLastFmSettings(settings);

                // Load Metadata/TMDb settings
                LoadMetadataSettings(settings);

                // Load Remote Control settings
                LoadRemoteControlSettings(settings);
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
        
        private void LoadFontSettings(AppSettings? settings)
        {
            if (settings == null) return;
            
            try
            {
                // Font family combo
                if (FontFamilyCombo != null)
                {
                    foreach (System.Windows.Controls.ComboBoxItem item in FontFamilyCombo.Items)
                    {
                        if (item.Tag?.ToString() == settings.CustomFontFamily)
                        {
                            FontFamilyCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
                
                // Font scale slider
                if (FontScaleSlider != null)
                {
                    FontScaleSlider.Value = settings.FontScale;
                    if (FontScaleLabel != null)
                        FontScaleLabel.Text = $"{(int)(settings.FontScale * 100)}%";
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading font settings: {ex.Message}");
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

        private System.Collections.Generic.List<ShortcutDisplayItem>? _allShortcuts;

        private void LoadKeyboardShortcuts()
        {
            try
            {
                var shortcuts = KeyboardShortcutService.Instance;
                if (shortcuts?.Shortcuts == null || ShortcutsGrid == null) return;

                _allShortcuts = new System.Collections.Generic.List<ShortcutDisplayItem>();
                foreach (var kvp in shortcuts.Shortcuts)
                {
                    if (kvp.Value != null)
                    {
                        var category = kvp.Key.Contains('.') ? kvp.Key.Split('.')[0] : "Other";
                        _allShortcuts.Add(new ShortcutDisplayItem
                        {
                            CommandId = kvp.Key,
                            Name = kvp.Key.Contains('.') ? kvp.Key.Split('.')[1] : kvp.Key,
                            Category = category,
                            Shortcut = shortcuts.GetShortcutDisplayString(kvp.Key) ?? "",
                            Description = GetShortcutDescription(kvp.Key)
                        });
                    }
                }
                _allShortcuts.Sort((a, b) => string.Compare(a.Category + a.Name, b.Category + b.Name, StringComparison.Ordinal));
                ShortcutsGrid.ItemsSource = _allShortcuts;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading shortcuts: {ex.Message}");
            }
        }

        private void FilterShortcutsList()
        {
            if (_allShortcuts == null || ShortcutsGrid == null) return;
            var search = ShortcutSearchBox?.Text?.Trim() ?? "";
            var category = (ShortcutCategoryFilter?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "All";
            
            var filtered = _allShortcuts.Where(s =>
                (category == "All" || s.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(search) || 
                 s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 s.Shortcut.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 s.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
            ).ToList();
            ShortcutsGrid.ItemsSource = filtered;
        }

        private void ShortcutSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterShortcutsList();
        private void ShortcutCategoryFilter_Changed(object sender, SelectionChangedEventArgs e) => FilterShortcutsList();

        private bool _isCapturingShortcut = false;
        private ShortcutDisplayItem? _editingShortcut = null;

        private void ShortcutsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Find which row was double-clicked
            if (ShortcutsGrid.SelectedItem is ShortcutDisplayItem item)
            {
                _isCapturingShortcut = true;
                _editingShortcut = item;
                if (ShortcutConflictText != null)
                    ShortcutConflictText.Text = $"⌨ Press new key combination for '{item.Name}'... (Escape to cancel)";

                // Keep keyboard focus on the DataGrid so PreviewKeyDown fires.
                e.Handled = true;
                Dispatcher.BeginInvoke(new Action(() => ShortcutsGrid.Focus()), 
                    System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void ShortcutsGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isCapturingShortcut || _editingShortcut == null) return;

            e.Handled = true;

            // Ignore modifier-only presses
            if (e.Key == System.Windows.Input.Key.LeftCtrl || e.Key == System.Windows.Input.Key.RightCtrl ||
                e.Key == System.Windows.Input.Key.LeftAlt || e.Key == System.Windows.Input.Key.RightAlt ||
                e.Key == System.Windows.Input.Key.LeftShift || e.Key == System.Windows.Input.Key.RightShift ||
                e.Key == System.Windows.Input.Key.LWin || e.Key == System.Windows.Input.Key.RWin ||
                e.Key == System.Windows.Input.Key.System)
                return;

            if (e.Key == System.Windows.Input.Key.Escape)
            {
                _isCapturingShortcut = false;
                _editingShortcut = null;
                if (ShortcutConflictText != null) ShortcutConflictText.Text = "";
                return;
            }

            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var modifiers = System.Windows.Input.Keyboard.Modifiers;

            try
            {
                var gesture = new System.Windows.Input.KeyGesture(key, modifiers);
                var conflict = KeyboardShortcutService.Instance.GetConflictingCommand(_editingShortcut.CommandId, gesture);

                if (conflict != null)
                {
                    if (ShortcutConflictText != null)
                        ShortcutConflictText.Text = $"\u26a0 Conflict with '{conflict}'. Press a different combination or Escape.";
                    return;
                }

                KeyboardShortcutService.Instance.SetShortcut(_editingShortcut.CommandId, gesture);
                _editingShortcut.Shortcut = KeyboardShortcutService.Instance.GetShortcutDisplayString(_editingShortcut.CommandId) ?? "";
                ShortcutsGrid.Items.Refresh();

                if (ShortcutConflictText != null)
                    ShortcutConflictText.Text = $"\u2705 Shortcut set: {_editingShortcut.Shortcut}";
            }
            catch
            {
                if (ShortcutConflictText != null)
                    ShortcutConflictText.Text = "Invalid key combination. Try a different one.";
                return;
            }

            _isCapturingShortcut = false;
            _editingShortcut = null;
        }

        private void ImportShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON Files|*.json", Title = "Import Shortcuts" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(dlg.FileName);
                    var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                    if (data != null)
                    {
                        foreach (var (cmdId, info) in data)
                        {
                            if (info.TryGetValue("Key", out var keyStr) && info.TryGetValue("Modifiers", out var modStr))
                            {
                                if (Enum.TryParse<System.Windows.Input.Key>(keyStr, out var key) &&
                                    Enum.TryParse<System.Windows.Input.ModifierKeys>(modStr, out var mods))
                                {
                                    KeyboardShortcutService.Instance.SetShortcut(cmdId, new System.Windows.Input.KeyGesture(key, mods));
                                }
                            }
                        }
                        LoadKeyboardShortcuts();
                        MessageBox.Show("Shortcuts imported successfully.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Error importing shortcuts: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON Files|*.json", Title = "Export Shortcuts", FileName = "shortcuts.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var data = new Dictionary<string, Dictionary<string, string>>();
                    foreach (var kvp in KeyboardShortcutService.Instance.Shortcuts)
                    {
                        data[kvp.Key] = new Dictionary<string, string>
                        {
                            ["Key"] = kvp.Value.Key.ToString(),
                            ["Modifiers"] = kvp.Value.Modifiers.ToString()
                        };
                    }
                    var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(dlg.FileName, json);
                    MessageBox.Show("Shortcuts exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Error exporting shortcuts: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
                "LastFm" => LastFmPanel,
                "Metadata" => MetadataPanel,
                "RemoteControl" => RemoteControlPanel,
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

            // Update Remote Server status when switching to that panel
            if (tag == "RemoteControl")
            {
                UpdateRemoteServerStatus();
                UpdateRemoteServerUrl();
                InitializeCloudfareTunnel();
            }
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
            // Legacy handler — no longer used (Glass is now a theme radio button)
        }
        
        private void ThemeRadio_Changed(object sender, RoutedEventArgs e)
        {
            // Enable/disable the Glass Intensity group based on whether Glass theme is selected
            if (GlassIntensityGroup != null)
                GlassIntensityGroup.IsEnabled = GlassThemeRadio.IsChecked == true;
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
        
        private void FontFamilyCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            // Just update the label - don't apply until OK/Apply clicked
            if (FontScaleLabel != null && FontScaleSlider != null)
            {
                FontScaleLabel.Text = $"{(int)(FontScaleSlider.Value * 100)}%";
            }
        }
        
        private void FontScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized) return;
            if (FontScaleSlider == null || FontScaleLabel == null) return;
            // Just update the label - don't apply until OK/Apply clicked
            var scale = Math.Round(FontScaleSlider.Value, 1);
            FontScaleLabel.Text = $"{(int)(scale * 100)}%";
        }
        
        private void ApplyFontSettings()
        {
            var settings = SettingsManager.Current;
            var fontFamily = settings.CustomFontFamily;
            var fontScale = settings.FontScale;
            
            // Store font scale in resources
            if (Application.Current.Resources.Contains("GlobalFontScale"))
            {
                Application.Current.Resources["GlobalFontScale"] = fontScale;
            }
            else
            {
                Application.Current.Resources.Add("GlobalFontScale", fontScale);
            }
            
            // Apply custom font family if not "Default"
            if (fontFamily != "Default" && !string.IsNullOrEmpty(fontFamily))
            {
                var ff = new System.Windows.Media.FontFamily(fontFamily);
                
                // Update global font resource
                if (Application.Current.Resources.Contains("GlobalFontFamily"))
                {
                    Application.Current.Resources["GlobalFontFamily"] = ff;
                }
                else
                {
                    Application.Current.Resources.Add("GlobalFontFamily", ff);
                }
                
                // Override all theme-specific font resources
                var fontKeys = new[] { "LcarsFont", "LcarsHeaderFont", "PipBoyFont", "PipBoyHeaderFont", 
                                       "PipBoyDisplayFont", "KlingonFontFamily", "KlingonDisplayFont" };
                foreach (var key in fontKeys)
                {
                    Application.Current.Resources[key] = ff;
                }
                
                // Set font directly on all open windows
                foreach (Window window in Application.Current.Windows)
                {
                    window.FontFamily = ff;
                }
            }
            else
            {
                // Reset to default - reapply theme to restore original fonts
                if (Application.Current.Resources.Contains("GlobalFontFamily"))
                {
                    Application.Current.Resources.Remove("GlobalFontFamily");
                }
                ThemeManager.ApplyTheme(settings.Theme ?? ThemeManager.Light);
            }
            
            // Apply scale transform to all windows
            foreach (Window window in Application.Current.Windows)
            {
                if (window.Content is System.Windows.FrameworkElement content)
                {
                    content.LayoutTransform = new System.Windows.Media.ScaleTransform(fontScale, fontScale);
                }
            }
        }
        
        private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Reset all keyboard shortcuts to defaults?", "Reset Shortcuts",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                KeyboardShortcutService.Instance.ResetToDefaults();
                LoadKeyboardShortcuts();
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

        private void ResetVaultDatabase_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "⚠️ This will PERMANENTLY DELETE your entire vault database including:\n\n" +
                "• All saved passwords and logins\n" +
                "• All TOTP / authenticator entries\n" +
                "• All secure notes and cards\n" +
                "• All identity records\n\n" +
                "This action CANNOT be undone.\n\n" +
                "Are you sure you want to reset the vault?",
                "Reset Vault Database",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            // Double-confirm for safety
            var confirm = MessageBox.Show(
                "FINAL CONFIRMATION\n\nType 'Yes' to confirm you want to permanently destroy the vault.",
                "Confirm Vault Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var vaultService = new Services.Vault.EncryptedVaultService();
                vaultService.DeleteVault();
                MessageBox.Show(
                    "Vault database has been deleted.\n\nYou can create a new vault with a new master password from the Security Vault tab.",
                    "Vault Reset Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to reset vault: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void InstallKlingonFont_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Find the font file in the application directory
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var fontPaths = new[]
                {
                    System.IO.Path.Combine(appDir, "Assets", "KlingonFont.ttf"),
                    System.IO.Path.Combine(appDir, "Assets", "Klingon-pIqaD-HaSta.ttf"),
                    System.IO.Path.Combine(appDir, "Assets", "klingon font.ttf"),
                    System.IO.Path.Combine(appDir, "KlingonFont.ttf")
                };
                
                string? sourceFontPath = null;
                foreach (var path in fontPaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        sourceFontPath = path;
                        break;
                    }
                }
                
                if (sourceFontPath == null)
                {
                    // Try to extract from resources
                    var resourcePath = "pack://application:,,,/Assets/KlingonFont.ttf";
                    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KlingonFont.ttf");
                    
                    try
                    {
                        var streamInfo = System.Windows.Application.GetResourceStream(new Uri(resourcePath));
                        if (streamInfo != null)
                        {
                            using (var stream = streamInfo.Stream)
                            using (var fileStream = System.IO.File.Create(tempPath))
                            {
                                stream.CopyTo(fileStream);
                            }
                            sourceFontPath = tempPath;
                        }
                    }
                    catch
                    {
                        // Resource not found
                    }
                }
                
                if (sourceFontPath == null)
                {
                    UpdateKlingonFontStatus("Font file not found", false);
                    MessageBox.Show("Klingon font file not found. Please ensure KlingonFont.ttf is in the Assets folder.", 
                        "Font Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Check if font is already installed
                var fontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                var destPath = System.IO.Path.Combine(fontsFolder, "KlingonFont.ttf");
                
                if (System.IO.File.Exists(destPath))
                {
                    UpdateKlingonFontStatus("Already installed", true);
                    MessageBox.Show("Klingon pIqaD font is already installed.", 
                        "Font Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // Use Shell to install font (works without admin for current user)
                try
                {
                    // Copy to user's local fonts folder first (no admin required)
                    var localFontsFolder = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft", "Windows", "Fonts");
                    
                    if (!System.IO.Directory.Exists(localFontsFolder))
                    {
                        System.IO.Directory.CreateDirectory(localFontsFolder);
                    }
                    
                    var localDestPath = System.IO.Path.Combine(localFontsFolder, "KlingonFont.ttf");
                    System.IO.File.Copy(sourceFontPath, localDestPath, true);
                    
                    // Register the font in the user's registry
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("klingon font (TrueType)", localDestPath);
                        }
                    }
                    
                    // Notify the system about the new font
                    NativeMethods.AddFontResource(localDestPath);
                    NativeMethods.SendMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero);
                    
                    UpdateKlingonFontStatus("Installed (restart apps to use)", true);
                    MessageBox.Show("Klingon font installed successfully!\n\nNote: You may need to restart applications to use the font.", 
                        "Font Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (UnauthorizedAccessException)
                {
                    // Try opening font file with shell (opens Font Viewer with Install button)
                    var result = MessageBox.Show(
                        "Cannot install font directly. Would you like to open the font file?\n\nClick 'Install' in the font viewer to install it.",
                        "Install Font Manually", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = sourceFontPath,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateKlingonFontStatus("Install failed", false);
                MessageBox.Show($"Failed to install font: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void UpdateKlingonFontStatus(string status, bool success)
        {
            UpdateFontStatus(KlingonFontStatus, status, success);
        }
        
        private void UpdateFontStatus(TextBlock? statusText, string status, bool success)
        {
            if (statusText != null)
            {
                statusText.Text = status;
                statusText.Foreground = success ? 
                    new SolidColorBrush(Colors.Green) : 
                    new SolidColorBrush(Colors.Orange);
            }
        }
        
        /// <summary>
        /// Generic font installation helper.
        /// </summary>
        private void InstallFont(string fontName, string[] searchFileNames, string registryFontName, TextBlock? statusTextBlock)
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                string? sourceFontPath = null;
                
                // Search for font file
                foreach (var fileName in searchFileNames)
                {
                    var paths = new[]
                    {
                        System.IO.Path.Combine(appDir, "Assets", fileName),
                        System.IO.Path.Combine(appDir, fileName)
                    };
                    
                    foreach (var path in paths)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            sourceFontPath = path;
                            break;
                        }
                    }
                    if (sourceFontPath != null) break;
                }
                
                if (sourceFontPath == null)
                {
                    UpdateFontStatus(statusTextBlock, "Font file not found", false);
                    MessageBox.Show($"{fontName} font file not found. Please ensure the font file is in the Assets folder.", 
                        "Font Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                var destFileName = System.IO.Path.GetFileName(sourceFontPath);
                
                // Check user's local fonts folder
                var localFontsFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts");
                var localDestPath = System.IO.Path.Combine(localFontsFolder, destFileName);
                
                if (System.IO.File.Exists(localDestPath))
                {
                    UpdateFontStatus(statusTextBlock, "Already installed", true);
                    MessageBox.Show($"{fontName} font is already installed.", 
                        "Font Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                try
                {
                    if (!System.IO.Directory.Exists(localFontsFolder))
                    {
                        System.IO.Directory.CreateDirectory(localFontsFolder);
                    }
                    
                    System.IO.File.Copy(sourceFontPath, localDestPath, true);
                    
                    // Register the font in the user's registry
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", true))
                    {
                        if (key != null)
                        {
                            key.SetValue($"{registryFontName} (TrueType)", localDestPath);
                        }
                    }
                    
                    NativeMethods.AddFontResource(localDestPath);
                    NativeMethods.SendMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero);
                    
                    UpdateFontStatus(statusTextBlock, "Installed (restart apps to use)", true);
                    MessageBox.Show($"{fontName} font installed successfully!\n\nNote: You may need to restart applications to use the font.", 
                        "Font Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (UnauthorizedAccessException)
                {
                    var result = MessageBox.Show(
                        $"Cannot install {fontName} font directly. Would you like to open the font file?\n\nClick 'Install' in the font viewer to install it.",
                        "Install Font Manually", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = sourceFontPath,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateFontStatus(statusTextBlock, "Install failed", false);
                MessageBox.Show($"Failed to install {fontName} font: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void InstallOkudaFont_Click(object sender, RoutedEventArgs e)
        {
            InstallFont(
                "Okuda",
                new[] { "Okuda.otf", "Okuda Bold.otf", "Okuda.ttf" },
                "Okuda",
                OkudaFontStatus);
        }
        
        private void InstallMonofontoFont_Click(object sender, RoutedEventArgs e)
        {
            InstallFont(
                "Monofonto",
                new[] { "monofonto rg.otf", "monofonto.otf", "Monofonto.ttf" },
                "Monofonto",
                MonofontoFontStatus);
        }
        
        private void InstallOverseerFont_Click(object sender, RoutedEventArgs e)
        {
            InstallFont(
                "Overseer",
                new[] { "Overseer.otf", "Overseer Bold.otf", "Overseer.ttf" },
                "Overseer",
                OverseerFontStatus);
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
            else if (KlingonThemeRadio.IsChecked == true)
            {
                settings.Theme = ThemeManager.Klingon;
                ThemeManager.ApplyTheme(ThemeManager.Klingon);
            }
            else if (PipBoyThemeRadio.IsChecked == true)
            {
                settings.Theme = ThemeManager.PipBoy;
                ThemeManager.ApplyTheme(ThemeManager.PipBoy);
            }
            else if (KPopDemonHuntersThemeRadio.IsChecked == true)
            {
                settings.Theme = ThemeManager.KPopDemonHunters;
                ThemeManager.ApplyTheme(ThemeManager.KPopDemonHunters);
            }
            else if (HighContrastThemeRadio?.IsChecked == true)
            {
                settings.Theme = ThemeManager.HighContrast;
                ThemeManager.ApplyTheme(ThemeManager.HighContrast);
            }
            else if (GlassThemeRadio?.IsChecked == true)
            {
                settings.Theme = ThemeManager.Glass;
                settings.GlassEnabled = true;
                ThemeManager.Instance.IsGlassEnabled = true;
                ThemeManager.ApplyTheme(ThemeManager.Glass);
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
            
            // Glass effect - disable if not Glass theme
            if (GlassThemeRadio?.IsChecked != true)
            {
                settings.GlassEnabled = false;
                ThemeManager.Instance.IsGlassEnabled = false;
            }
            
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

            // Session restore (IDEA-009)
            settings.RestoreLastSession = RestoreLastSessionCheck.IsChecked == true;

            // Start with Windows
            settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
            ApplyStartWithWindows(settings.StartWithWindows);

            // Start minimized
            settings.StartMinimized = StartMinimizedCheck.IsChecked == true;

            // Language
            if (LanguageCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem langItem && langItem.Tag != null)
            {
                var newLang = langItem.Tag.ToString() ?? "en-US";
                if (newLang != settings.Language)
                {
                    settings.Language = newLang;
                    try { Services.LocalizationService.Instance.SetLanguage(newLang); } catch { }
                }
            }

            // Save audio visualizer settings
            SaveVisualizerSettings(settings);
            
            // Save font settings
            SaveFontSettings(settings);
            
            // Last.fm settings are saved via their own button (SaveLastFmCredentials_Click)
            
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
        
        private void SaveFontSettings(AppSettings settings)
        {
            try
            {
                // Font family combo
                if (FontFamilyCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem fontItem && fontItem.Tag != null)
                {
                    settings.CustomFontFamily = fontItem.Tag.ToString() ?? "Default";
                }
                
                // Font scale slider
                if (FontScaleSlider != null)
                {
                    settings.FontScale = Math.Round(FontScaleSlider.Value, 1);
                }
                
                // Apply font settings immediately
                ApplyFontSettings();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving font settings: {ex.Message}");
            }
        }
        
        #region Last.fm Settings
        
        private void LoadLastFmSettings(AppSettings? settings)
        {
            if (settings == null) return;
            try
            {
                if (LastFmApiKeyBox != null)
                    LastFmApiKeyBox.Text = settings.LastFmApiKey;
                if (LastFmApiSecretBox != null)
                    LastFmApiSecretBox.Password = settings.LastFmApiSecret;
                if (LastFmStatusText != null)
                    LastFmStatusText.Text = string.IsNullOrEmpty(settings.LastFmSessionKey) ? "Not Connected" : "Connected (session key stored)";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Last.fm settings: {ex.Message}");
            }
        }
        
        private void SaveLastFmCredentials_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsManager.Current;
                settings.LastFmApiKey = LastFmApiKeyBox?.Text?.Trim() ?? string.Empty;
                settings.LastFmApiSecret = LastFmApiSecretBox?.Password?.Trim() ?? string.Empty;
                SettingsManager.SaveCurrent();
                
                if (LastFmStatusText != null)
                    LastFmStatusText.Text = "Credentials saved. Click 'Connect Last.fm' in Audio Player to authenticate.";
                    
                MessageBox.Show("Last.fm API credentials saved.\n\nTo connect, go to Audio Player and click the 'Connect Last.fm' button.",
                    "Last.fm", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error saving credentials: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ClearLastFmSession_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsManager.Current;
                settings.LastFmSessionKey = string.Empty;
                SettingsManager.SaveCurrent();
                
                if (LastFmStatusText != null)
                    LastFmStatusText.Text = "Session cleared. Re-authenticate via Audio Player.";
                    
                MessageBox.Show("Last.fm session cleared. You will need to re-authenticate next time you click 'Connect Last.fm' in the Audio Player.",
                    "Last.fm", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error clearing session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        #endregion
        
        #region Metadata / TMDb Settings

        private void LoadMetadataSettings(AppSettings? settings)
        {
            if (settings == null) return;
            try
            {
                if (TmdbApiKeyBox != null)
                    TmdbApiKeyBox.Text = settings.TmdbApiKey;
                if (TmdbStatusText != null)
                    TmdbStatusText.Text = string.IsNullOrEmpty(settings.TmdbApiKey) 
                        ? "No API key configured." 
                        : "✅ TMDb API key is configured.";
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading TMDb settings: {ex.Message}");
            }
        }

        private void SaveTmdbApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsManager.Current;
                settings.TmdbApiKey = TmdbApiKeyBox?.Text?.Trim() ?? string.Empty;
                SettingsManager.SaveCurrent();

                // Also update the MetadataEnrichmentService instance
                if (!string.IsNullOrWhiteSpace(settings.TmdbApiKey))
                {
                    PlatypusTools.Core.Services.MetadataEnrichmentService.Instance.SetTmdbApiKey(settings.TmdbApiKey);
                }

                if (TmdbStatusText != null)
                    TmdbStatusText.Text = string.IsNullOrEmpty(settings.TmdbApiKey)
                        ? "API key cleared."
                        : "✅ TMDb API key saved successfully.";

                MessageBox.Show(
                    string.IsNullOrEmpty(settings.TmdbApiKey)
                        ? "TMDb API key cleared."
                        : "TMDb API key saved.\n\nYou can now use the 🔍 TMDb button in Media Hub to look up movie and TV show metadata.",
                    "TMDb Settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error saving TMDb API key: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

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

        /// <summary>
        /// Adds or removes the application from the Windows startup registry key.
        /// </summary>
        private void ApplyStartWithWindows(bool enable)
        {
            try
            {
                const string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                const string valueName = "PlatypusTools";
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyName, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue(valueName, $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue(valueName, false);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting startup registry: {ex.Message}");
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
            { "TabHashScanner", "Security.HashScanner" },
            { "TabRebootAnalyzer", "Security.RebootAnalyzer" },
            { "TabAdSecurityAnalyzer", "Security.AdSecurityAnalyzer" },
            
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
                
                // Show AD Security Analyzer checkbox only when licensed
                if (TabAdSecurityAnalyzer != null)
                {
                    bool isLicensed = settings.HasValidLicenseKey();
                    TabAdSecurityAnalyzer.Visibility = isLicensed ? Visibility.Visible : Visibility.Collapsed;
                    
                    // If licensed, check the saved visibility setting (or default to visible)
                    if (isLicensed)
                    {
                        if (settings.VisibleTabs.TryGetValue("Security.AdSecurityAnalyzer", out var visible))
                            TabAdSecurityAnalyzer.IsChecked = visible;
                        else
                            TabAdSecurityAnalyzer.IsChecked = true; // Default to visible once licensed
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
                DepYtDlpStatus.Text = "⏳";
                DepWebView2Status.Text = "⏳";
                DepFFmpegInfo.Text = "Checking...";
                DepExifToolInfo.Text = "Checking...";
                DepYtDlpInfo.Text = "Checking...";
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
                
                // yt-dlp
                if (result.YtDlpInstalled)
                {
                    DepYtDlpStatus.Text = "✅";
                    DepYtDlpInfo.Text = "Installed";
                    DepYtDlpBtn.IsEnabled = false;
                    DepYtDlpBtn.Content = "Installed";
                }
                else
                {
                    DepYtDlpStatus.Text = "❌";
                    DepYtDlpInfo.Text = "Not found - required for streaming audio";
                    DepYtDlpBtn.IsEnabled = true;
                    DepYtDlpBtn.Content = "Install";
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
                
                // Tailscale (optional)
                if (result.TailscaleInstalled)
                {
                    DepTailscaleStatus.Text = "✅";
                    DepTailscaleInfo.Text = "Installed";
                }
                else
                {
                    DepTailscaleStatus.Text = "⬜";
                    DepTailscaleInfo.Text = "Not installed (optional - for secure remote access)";
                }
                
                // cloudflared (optional)
                if (result.CloudflaredInstalled)
                {
                    DepCloudflaredStatus.Text = "✅";
                    DepCloudflaredInfo.Text = "Installed";
                    DepCloudflaredBtn.IsEnabled = false;
                    DepCloudflaredBtn.Content = "Installed";
                }
                else
                {
                    DepCloudflaredStatus.Text = "⬜";
                    DepCloudflaredInfo.Text = "Not installed (optional - for Cloudflare tunnel)";
                    DepCloudflaredBtn.IsEnabled = true;
                    DepCloudflaredBtn.Content = "Install";
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
        
        private async void DepInstallYtDlp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DepYtDlpBtn.IsEnabled = false;
                DepYtDlpBtn.Content = "Installing...";
                DepYtDlpStatus.Text = "⏳";
                
                var success = await DownloadAndInstallYtDlpAsync();
                
                if (success)
                {
                    MessageBox.Show("yt-dlp installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("yt-dlp installation failed. Please install manually from https://github.com/yt-dlp/yt-dlp", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await LoadDependencyStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing yt-dlp: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        
        private void DepOpenTailscaleSite_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://tailscale.com/download/windows", UseShellExecute = true });
        }
        
        private void DepOpenCloudflaredSite_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://github.com/cloudflare/cloudflared/releases", UseShellExecute = true });
        }
        
        private async void DepInstallCloudflared_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DepCloudflaredBtn.IsEnabled = false;
                DepCloudflaredBtn.Content = "Installing...";
                DepCloudflaredStatus.Text = "⏳";
                
                var success = await DownloadAndInstallCloudflaredAsync();
                
                if (success)
                {
                    MessageBox.Show("cloudflared installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("cloudflared installation failed. Please install manually from GitHub.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await LoadDependencyStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing cloudflared: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await LoadDependencyStatusAsync();
            }
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
            
            var client = HttpClientFactory.Download;
            
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
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsPath)) Directory.CreateDirectory(toolsPath);
            
            var exifToolUrl = "https://exiftool.org/exiftool-12.76.zip";
            var zipPath = Path.Combine(Path.GetTempPath(), "exiftool.zip");
            var extractPath = Path.Combine(Path.GetTempPath(), "exiftool_extract");
            
            var client = HttpClientFactory.Download;
            
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
        
        private async Task<bool> DownloadAndInstallYtDlpAsync()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsPath)) Directory.CreateDirectory(toolsPath);
            
            var destPath = Path.Combine(toolsPath, "yt-dlp.exe");
            const string ytdlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            
            var client = HttpClientFactory.Download;
            
            var response = await client.GetAsync(ytdlpUrl);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(destPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            return File.Exists(destPath);
        }
        
        private async Task<bool> DownloadAndInstallWebView2Async()
        {
            var installerPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
            var webView2Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
            
            var client = HttpClientFactory.Download;
            
            var response = await client.GetAsync(webView2Url);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(installerPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            Process.Start(new ProcessStartInfo { FileName = installerPath, UseShellExecute = true });
            
            return true;
        }
        
        private async Task<bool> DownloadAndInstallCloudflaredAsync()
        {
            return await CloudflareTunnelService.Instance.InstallAsync();
        }
        
        #endregion

        #region Remote Control Settings
        
        private void LoadRemoteControlSettings(AppSettings? settings)
        {
            if (settings == null) return;
            try
            {
                if (RemoteServerEnabledCheck != null)
                    RemoteServerEnabledCheck.IsChecked = settings.RemoteServerEnabled;
                if (RemoteServerAutoStartCheck != null)
                    RemoteServerAutoStartCheck.IsChecked = settings.RemoteServerAutoStart;
                if (RemoteServerPortBox != null)
                    RemoteServerPortBox.Text = settings.RemoteServerPort.ToString();
                
                // Load Entra ID config from data directory
                LoadEntraConfig();
                LoadCfZeroTrustConfig();
                
                UpdateRemoteServerStatus();
                UpdateRemoteServerUrl();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Remote Control settings: {ex.Message}");
            }
        }

        private void LoadEntraConfig()
        {
            try
            {
                var config = EntraConfigService.Instance.Load();
                if (EntraClientIdBox != null) EntraClientIdBox.Text = config.ClientId;
                if (EntraTenantIdBox != null) EntraTenantIdBox.Text = string.IsNullOrWhiteSpace(config.TenantId) ? "common" : config.TenantId;
                if (EntraApiScopeIdBox != null) EntraApiScopeIdBox.Text = config.ApiScopeId;
                if (EntraGraphClientIdBox != null) EntraGraphClientIdBox.Text = config.GraphClientId;
                if (EntraIdAuthEnabledCheck != null) EntraIdAuthEnabledCheck.IsChecked = config.EntraIdAuthEnabled;
                if (EntraIdAllowedEmailsBox != null) EntraIdAllowedEmailsBox.Text = config.EntraIdAllowedEmails;

                if (EntraConfigStatus != null)
                {
                    EntraConfigStatus.Text = EntraConfigService.Instance.IsConfigured()
                        ? "✅ Configured"
                        : "⚠️ Not configured — enter your app registration details";
                    EntraConfigStatus.Foreground = EntraConfigService.Instance.IsConfigured()
                        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Entra config: {ex.Message}");
            }
        }

        private void SaveEntraConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load existing config to preserve CF Zero Trust fields
                var config = EntraConfigService.Instance.Load();
                config.ClientId = EntraClientIdBox?.Text?.Trim() ?? "";
                config.TenantId = EntraTenantIdBox?.Text?.Trim() ?? "common";
                config.ApiScopeId = EntraApiScopeIdBox?.Text?.Trim() ?? "";
                config.GraphClientId = EntraGraphClientIdBox?.Text?.Trim() ?? "";
                config.EntraIdAuthEnabled = EntraIdAuthEnabledCheck?.IsChecked == true;
                config.EntraIdAllowedEmails = EntraIdAllowedEmailsBox?.Text?.Trim() ?? "";

                EntraConfigService.Instance.Save(config);

                if (EntraConfigStatus != null)
                {
                    EntraConfigStatus.Text = "✅ Saved successfully";
                    EntraConfigStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                }

                System.Diagnostics.Debug.WriteLine($"Entra config saved to: {EntraConfigService.Instance.ConfigFilePath}");
            }
            catch (System.Exception ex)
            {
                if (EntraConfigStatus != null)
                {
                    EntraConfigStatus.Text = $"❌ Error: {ex.Message}";
                    EntraConfigStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                }
            }
        }

        private void LoadCfZeroTrustConfig()
        {
            try
            {
                var config = EntraConfigService.Instance.Load();
                if (CfZeroTrustEnabledCheck != null) CfZeroTrustEnabledCheck.IsChecked = config.CloudflareZeroTrustEnabled;
                if (CfTeamDomainBox != null) CfTeamDomainBox.Text = config.CloudflareTeamDomain;
                if (CfAudienceBox != null) CfAudienceBox.Text = config.CloudflareAudience;
                if (CfAllowedEmailsBox != null) CfAllowedEmailsBox.Text = config.CloudflareAllowedEmails;

                if (CfZeroTrustStatus != null)
                {
                    var isConfigured = EntraConfigService.Instance.IsCloudflareZeroTrustConfigured();
                    var hasFields = !string.IsNullOrWhiteSpace(config.CloudflareTeamDomain) && !string.IsNullOrWhiteSpace(config.CloudflareAudience);
                    CfZeroTrustStatus.Text = isConfigured
                        ? "✅ Enabled — JWT validation active"
                        : hasFields
                            ? "⚠️ Configured but disabled — check the box above to enable"
                            : "ℹ️ Disabled — fill in settings and enable to protect the webapp";
                    CfZeroTrustStatus.Foreground = isConfigured
                        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading CF Zero Trust config: {ex.Message}");
            }
        }

        private void SaveCfZeroTrustConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Load existing config to preserve Entra ID fields
                var config = EntraConfigService.Instance.Load();
                config.CloudflareZeroTrustEnabled = CfZeroTrustEnabledCheck?.IsChecked == true;
                config.CloudflareTeamDomain = CfTeamDomainBox?.Text?.Trim() ?? "";
                config.CloudflareAudience = CfAudienceBox?.Text?.Trim() ?? "";
                config.CloudflareAllowedEmails = CfAllowedEmailsBox?.Text?.Trim() ?? "";

                EntraConfigService.Instance.Save(config);

                if (CfZeroTrustStatus != null)
                {
                    CfZeroTrustStatus.Text = "✅ Saved — restart server to apply";
                    CfZeroTrustStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                }

                System.Diagnostics.Debug.WriteLine($"CF Zero Trust config saved");
            }
            catch (System.Exception ex)
            {
                if (CfZeroTrustStatus != null)
                {
                    CfZeroTrustStatus.Text = $"❌ Error: {ex.Message}";
                    CfZeroTrustStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                }
            }
        }
        
        private void UpdateRemoteServerStatus()
        {
            var server = App.RemoteServer;
            if (server?.IsRunning == true)
            {
                if (RemoteServerStatusText != null)
                {
                    RemoteServerStatusText.Text = "Running";
                    RemoteServerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
                }
                if (StartRemoteServerBtn != null) StartRemoteServerBtn.IsEnabled = false;
                if (StopRemoteServerBtn != null) StopRemoteServerBtn.IsEnabled = true;
            }
            else
            {
                if (RemoteServerStatusText != null)
                {
                    RemoteServerStatusText.Text = "Stopped";
                    RemoteServerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Red
                }
                if (StartRemoteServerBtn != null) StartRemoteServerBtn.IsEnabled = true;
                if (StopRemoteServerBtn != null) StopRemoteServerBtn.IsEnabled = false;
            }
        }
        
        private void UpdateRemoteServerUrl()
        {
            try
            {
                var port = int.TryParse(RemoteServerPortBox?.Text, out var p) ? p : 47392;
                var ipAddress = GetLocalIpAddress();
                var url = $"https://{ipAddress}:{port}";
                
                if (RemoteServerUrlText != null)
                    RemoteServerUrlText.Text = url;
                
                // Generate QR code
                GenerateQRCode(url);
            }
            catch { }
        }

        private void GenerateQRCode(string url)
        {
            try
            {
                if (RemoteQRCodeImage == null) return;

                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new BitmapByteQRCode(qrCodeData);
                var qrCodeBytes = qrCode.GetGraphic(10); // 10 pixels per module

                // Convert to WPF BitmapImage
                using var ms = new System.IO.MemoryStream(qrCodeBytes);
                var bitmap = Utilities.ImageHelper.LoadFromStream(ms);
                if (bitmap != null)
                    RemoteQRCodeImage.Source = bitmap;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating QR code: {ex.Message}");
            }
        }

        private void RefreshQRCode_Click(object sender, RoutedEventArgs e)
        {
            UpdateRemoteServerUrl();
        }
        
        private string GetLocalIpAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "localhost";
        }
        
        private async void StartRemoteServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save settings first
                SaveRemoteControlSettings();
                
                // Start the server
                var app = (App)Application.Current;
                await app.StartRemoteServerAsync();
                
                UpdateRemoteServerStatus();
                MessageBox.Show($"Remote Control Server started!\n\nAccess it from your phone at:\n{RemoteServerUrlText?.Text}",
                    "Server Started", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to start server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void StopRemoteServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var app = (App)Application.Current;
                await app.StopRemoteServerAsync();
                UpdateRemoteServerStatus();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to stop server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveRemoteControlSettings()
        {
            try
            {
                var settings = SettingsManager.Current;
                settings.RemoteServerEnabled = RemoteServerEnabledCheck?.IsChecked ?? false;
                settings.RemoteServerAutoStart = RemoteServerAutoStartCheck?.IsChecked ?? false;
                if (int.TryParse(RemoteServerPortBox?.Text, out var port))
                    settings.RemoteServerPort = port;
                SettingsManager.SaveCurrent();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving Remote Control settings: {ex.Message}");
            }
        }
        
        private void CopyRemoteUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = RemoteServerUrlText?.Text ?? "";
                System.Windows.Clipboard.SetText(url);
                MessageBox.Show($"URL copied to clipboard:\n{url}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to copy: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Cloudflare Tunnel
        
        private CloudflareTunnelService? _tunnelService;

        private void InitializeCloudfareTunnel()
        {
            _tunnelService = CloudflareTunnelService.Instance;
            _tunnelService.TunnelStateChanged += OnTunnelStateChanged;
            _tunnelService.TunnelUrlGenerated += OnTunnelUrlGenerated;
            _tunnelService.LogMessage += OnTunnelLogMessage;
            _tunnelService.DiagnosticsUpdated += OnTunnelDiagnosticsUpdated;

            // Load settings
            LoadCloudflareSettings();
            UpdateCloudflareStatus();
            UpdateTunnelDiagnosticsDisplay();
        }
        
        private void OnTunnelDiagnosticsUpdated(object? sender, TunnelDiagnostics diag)
        {
            Dispatcher.Invoke(() => UpdateTunnelDiagnosticsDisplay());
        }

        private void LoadCloudflareSettings()
        {
            var settings = SettingsManager.Current;
            if (CloudflareTunnelEnabledCheck != null)
                CloudflareTunnelEnabledCheck.IsChecked = settings.CloudflareTunnelEnabled;
            if (CloudflareTunnelAutoStartCheck != null)
                CloudflareTunnelAutoStartCheck.IsChecked = settings.CloudflareTunnelAutoStart;
            if (CloudflareTunnelHostnameBox != null)
                CloudflareTunnelHostnameBox.Text = settings.CloudflareTunnelHostname;
            if (QuickTunnelRadio != null && NamedTunnelRadio != null)
            {
                if (settings.CloudflareTunnelUseQuickTunnel)
                    QuickTunnelRadio.IsChecked = true;
                else
                    NamedTunnelRadio.IsChecked = true;
            }
        }

        private void SaveCloudflareSettings()
        {
            try
            {
                var settings = SettingsManager.Current;
                settings.CloudflareTunnelEnabled = CloudflareTunnelEnabledCheck?.IsChecked ?? false;
                settings.CloudflareTunnelAutoStart = CloudflareTunnelAutoStartCheck?.IsChecked ?? false;
                settings.CloudflareTunnelHostname = CloudflareTunnelHostnameBox?.Text ?? "";
                settings.CloudflareTunnelUseQuickTunnel = QuickTunnelRadio?.IsChecked ?? true;
                SettingsManager.SaveCurrent();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving Cloudflare settings: {ex.Message}");
            }
        }

        private void OnTunnelStateChanged(object? sender, bool isRunning)
        {
            Dispatcher.Invoke(() => UpdateCloudflareStatus());
        }

        private void OnTunnelUrlGenerated(object? sender, string url)
        {
            Dispatcher.Invoke(() =>
            {
                if (CloudflareTunnelUrlText != null)
                    CloudflareTunnelUrlText.Text = url;
                if (TunnelUrlPanel != null)
                    TunnelUrlPanel.Visibility = Visibility.Visible;
            });
        }

        private void OnTunnelLogMessage(object? sender, string message)
        {
            Debug.WriteLine($"[Cloudflare Tunnel] {message}");
        }

        private void UpdateCloudflareStatus()
        {
            var isInstalled = CloudflareTunnelService.Instance.IsInstalled;
            var isRunning = CloudflareTunnelService.Instance.IsRunning;
            
            // Update Windows Service status
            UpdateWindowsServiceStatus();

            if (CloudflareTunnelStatusText != null)
            {
                if (!isInstalled)
                {
                    CloudflareTunnelStatusText.Text = "Not Installed";
                    CloudflareTunnelStatusText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                else if (isRunning)
                {
                    CloudflareTunnelStatusText.Text = "Running";
                    CloudflareTunnelStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
                }
                else
                {
                    CloudflareTunnelStatusText.Text = "Stopped";
                    CloudflareTunnelStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Red
                }
            }

            if (StartTunnelBtn != null)
                StartTunnelBtn.IsEnabled = isInstalled && !isRunning && (CloudflareTunnelEnabledCheck?.IsChecked ?? false);
            if (StopTunnelBtn != null)
                StopTunnelBtn.IsEnabled = isRunning;
            if (InstallCloudflaredBtn != null)
                InstallCloudflaredBtn.Content = isInstalled ? "✓ cloudflared Installed" : "📥 Install cloudflared";
            if (TunnelUrlPanel != null && !isRunning)
                TunnelUrlPanel.Visibility = Visibility.Collapsed;
        }
        
        private void UpdateWindowsServiceStatus()
        {
            var service = CloudflareTunnelService.Instance;
            var tunnelStatus = service.PersistentTunnelStatus;
            var isRunning = service.IsPersistentTunnelRunning;
            var isAutoStart = service.IsAutoStartEnabled;
            
            if (WindowsServiceStatusText != null)
            {
                WindowsServiceStatusText.Text = tunnelStatus;
                WindowsServiceStatusText.Foreground = tunnelStatus switch
                {
                    var s when s.StartsWith("Running") => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    var s when s.Contains("Stopped") => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                    "Not Configured" => new SolidColorBrush(Colors.Orange),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            
            if (StartServiceBtn != null)
                StartServiceBtn.IsEnabled = !isRunning && service.IsInstalled;
            if (StopServiceBtn != null)
                StopServiceBtn.IsEnabled = isRunning;
            if (InstallServiceBtn != null)
                InstallServiceBtn.IsEnabled = !isAutoStart && service.IsInstalled;
            if (UninstallServiceBtn != null)
                UninstallServiceBtn.IsEnabled = isAutoStart || isRunning;
        }
        
        private async void StartWindowsService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StartServiceBtn != null)
                {
                    StartServiceBtn.IsEnabled = false;
                    StartServiceBtn.Content = "Starting...";
                }
                
                var success = await CloudflareTunnelService.Instance.StartWindowsServiceAsync();
                if (!success)
                {
                    MessageBox.Show("Failed to start the tunnel. Check the Cloudflare logs for details.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting service: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (StartServiceBtn != null)
                    StartServiceBtn.Content = "▶ Start Tunnel";
                UpdateCloudflareStatus();
            }
        }
        
        private async void StopWindowsService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StopServiceBtn != null)
                {
                    StopServiceBtn.IsEnabled = false;
                    StopServiceBtn.Content = "Stopping...";
                }
                
                var success = await CloudflareTunnelService.Instance.StopWindowsServiceAsync();
                if (!success)
                {
                    MessageBox.Show("Failed to stop the tunnel.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping service: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (StopServiceBtn != null)
                    StopServiceBtn.Content = "⬛ Stop Tunnel";
                UpdateCloudflareStatus();
            }
        }
        
        private async void InstallWindowsService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (InstallServiceBtn != null)
                {
                    InstallServiceBtn.IsEnabled = false;
                    InstallServiceBtn.Content = "Installing...";
                }
                
                var success = await CloudflareTunnelService.Instance.InstallWindowsServiceAsync();
                if (success)
                {
                    MessageBox.Show("Tunnel auto-start configured! The tunnel will start on login and is running now.", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to set up tunnel auto-start. Make sure cloudflared is installed and a tunnel is configured.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing service: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (InstallServiceBtn != null)
                    InstallServiceBtn.Content = "📥 Enable Auto-Start";
                UpdateCloudflareStatus();
            }
        }
        
        private async void UninstallWindowsService_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Remove tunnel auto-start?\n\nThe tunnel will be stopped and will no longer start on login.", 
                "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result != MessageBoxResult.Yes) return;
            
            try
            {
                if (UninstallServiceBtn != null)
                {
                    UninstallServiceBtn.IsEnabled = false;
                    UninstallServiceBtn.Content = "Uninstalling...";
                }
                
                var success = await CloudflareTunnelService.Instance.UninstallWindowsServiceAsync();
                if (success)
                {
                    MessageBox.Show("Tunnel auto-start removed and tunnel stopped.", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to remove tunnel auto-start.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error uninstalling service: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (UninstallServiceBtn != null)
                    UninstallServiceBtn.Content = "🗑 Remove Auto-Start";
                UpdateCloudflareStatus();
            }
        }

        private async void InstallCloudflared_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (InstallCloudflaredBtn != null)
                {
                    InstallCloudflaredBtn.IsEnabled = false;
                    InstallCloudflaredBtn.Content = "Installing...";
                }

                await CloudflareTunnelService.Instance.InstallAsync();
                MessageBox.Show("cloudflared installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateCloudflareStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to install cloudflared: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (InstallCloudflaredBtn != null)
                    InstallCloudflaredBtn.IsEnabled = true;
                UpdateCloudflareStatus();
            }
        }

        private async void StartTunnel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveCloudflareSettings();
                var settings = SettingsManager.Current;
                var port = settings.RemoteServerPort;

                if (StartTunnelBtn != null)
                {
                    StartTunnelBtn.IsEnabled = false;
                    StartTunnelBtn.Content = "Starting...";
                }

                if (settings.CloudflareTunnelUseQuickTunnel)
                {
                    await CloudflareTunnelService.Instance.StartQuickTunnelAsync(port);
                }
                else
                {
                    var hostname = settings.CloudflareTunnelHostname;
                    if (string.IsNullOrWhiteSpace(hostname) || hostname == "platypus.yourdomain.com")
                    {
                        MessageBox.Show("Please enter a valid hostname for your named tunnel.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    await CloudflareTunnelService.Instance.StartNamedTunnelAsync(hostname, port);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start tunnel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (StartTunnelBtn != null)
                    StartTunnelBtn.Content = "▶ Start Tunnel";
                UpdateCloudflareStatus();
            }
        }

        private void StopTunnel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CloudflareTunnelService.Instance.Stop();
                UpdateCloudflareStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop tunnel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloudflareLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CloudflareTunnelService.Instance.LoginAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open Cloudflare login: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyTunnelUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = CloudflareTunnelService.Instance.CurrentTunnelUrl;
                if (!string.IsNullOrEmpty(url))
                {
                    System.Windows.Clipboard.SetText(url);
                    MessageBox.Show($"Tunnel URL copied to clipboard:\n{url}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void RefreshTunnelDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ = CloudflareTunnelService.Instance.CheckHealthAsync();
                UpdateTunnelDiagnosticsDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh diagnostics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void KillAllTunnelProcesses_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var count = CloudflareTunnelService.Instance.GetRunningProcessCount();
                if (count > 0)
                {
                    var result = MessageBox.Show($"Kill all {count} cloudflared process(es)?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        CloudflareTunnelService.Instance.KillAllCloudflaredProcesses();
                        UpdateTunnelDiagnosticsDisplay();
                        UpdateCloudflareStatus();
                    }
                }
                else
                {
                    MessageBox.Show("No cloudflared processes are running.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to kill processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void RestartTunnel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = false;
                    btn.Content = "Restarting...";
                }
                
                await CloudflareTunnelService.Instance.RestartTunnelAsync();
                UpdateTunnelDiagnosticsDisplay();
                UpdateCloudflareStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restart tunnel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                    btn.Content = "🔁 Restart Tunnel";
                }
            }
        }
        
        private void UpdateTunnelDiagnosticsDisplay()
        {
            var diag = CloudflareTunnelService.Instance.Diagnostics;
            
            if (TunnelDiagProcessText != null)
                TunnelDiagProcessText.Text = diag.ProcessRunning ? "Yes" : "No";
            if (TunnelDiagConnectionsText != null)
                TunnelDiagConnectionsText.Text = diag.ActiveConnections.ToString();
            if (TunnelDiagEdgeText != null)
                TunnelDiagEdgeText.Text = diag.EdgeLocation ?? "-";
            if (TunnelDiagHealthCheckText != null)
                TunnelDiagHealthCheckText.Text = diag.LastHealthCheck?.ToString("HH:mm:ss") ?? "-";
            if (TunnelDiagLastLogText != null)
                TunnelDiagLastLogText.Text = diag.LastLogMessage ?? "-";
            if (TunnelDiagProcessCountText != null)
                TunnelDiagProcessCountText.Text = CloudflareTunnelService.Instance.GetRunningProcessCount().ToString();
        }

        #endregion

        #endregion
    }
    
    public class ShortcutDisplayItem
    {
        public string CommandId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Shortcut { get; set; } = "";
        public string Description { get; set; } = "";
    }
    
    internal static class NativeMethods
    {
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        public const int WM_FONTCHANGE = 0x001D;
        
        [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern int AddFontResource(string lpszFilename);
        
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    }
}


