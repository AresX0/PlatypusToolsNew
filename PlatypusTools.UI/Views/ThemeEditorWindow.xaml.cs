using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Window for editing and customizing application themes.
    /// </summary>
    public partial class ThemeEditorWindow : Window
    {
        public ObservableCollection<CustomTheme> Themes { get; } = new();
        private CustomTheme? _selectedTheme;
        private bool _suppressEvents;
        
        public ThemeEditorWindow()
        {
            InitializeComponent();
            LoadThemes();
            ThemeList.ItemsSource = Themes;
            if (Themes.Count > 0)
            {
                ThemeList.SelectedIndex = 0;
            }
        }
        
        private void LoadThemes()
        {
            Themes.Clear();
            
            // Add built-in themes
            Themes.Add(new CustomTheme
            {
                Name = "Light",
                IsBuiltIn = true,
                PrimaryColor = "#007ACC",
                AccentColor = "#0E639C",
                BackgroundColor = "#FFFFFF",
                SurfaceColor = "#F5F5F5",
                CardBackgroundColor = "#FAFAFA",
                ControlBackgroundColor = "#FFFFFF",
                ControlBorderColor = "#CCCCCC",
                TextColor = "#1E1E1E",
                SecondaryTextColor = "#6E6E6E"
            });
            
            Themes.Add(new CustomTheme
            {
                Name = "Dark",
                IsBuiltIn = true,
                PrimaryColor = "#007ACC",
                AccentColor = "#0E639C",
                BackgroundColor = "#1E1E1E",
                SurfaceColor = "#252526",
                CardBackgroundColor = "#2D2D30",
                ControlBackgroundColor = "#3C3C3C",
                ControlBorderColor = "#555555",
                TextColor = "#D4D4D4",
                SecondaryTextColor = "#808080",
                // Menu colors for Dark
                MenuBackgroundColor = "#2D2D30",
                MenuForegroundColor = "#D4D4D4",
                MenuHoverBackgroundColor = "#3E3E42",
                MenuBorderColor = "#555555"
            });
            
            // Add LCARS theme (Star Trek inspired)
            Themes.Add(new CustomTheme
            {
                Name = "LCARS",
                IsBuiltIn = true,
                PrimaryColor = "#FFCC00",      // LcarsGold
                AccentColor = "#FF6600",        // LcarsOrange  
                AccentHoverColor = "#CC5500",   // LcarsPaneOrange
                BackgroundColor = "#000000",    // Black
                SurfaceColor = "#1A1A2E",       // Dark blue-gray
                CardBackgroundColor = "#0D0D1A",
                ControlBackgroundColor = "#0D0D1A", // Dark blue-black
                ControlBorderColor = "#CC99FF", // LcarsAfricanViolet
                TextColor = "#FFCC00",          // Gold text
                SecondaryTextColor = "#CC99FF", // Lavender
                HeaderColor = "#CC5500",        // Orange header
                HeaderForegroundColor = "#000000",
                FontFamily = "Okuda",
                // Menu colors for LCARS
                MenuBackgroundColor = "#1A1A2E",
                MenuForegroundColor = "#FFCC00",
                MenuHoverBackgroundColor = "#FF6600",
                MenuBorderColor = "#CC99FF"
            });
            
            // Add PipBoy theme (Fallout inspired)
            Themes.Add(new CustomTheme
            {
                Name = "PipBoy",
                IsBuiltIn = true,
                PrimaryColor = "#1BFF80",       // PipBoyGreen
                AccentColor = "#0F8B4A",        // PipBoyGreenDark
                AccentHoverColor = "#00FF41",   // PipBoyGreenBright
                BackgroundColor = "#0A0E0D",    // PipBoyBlack
                SurfaceColor = "#0D1210",       // PipBoyBlackLight
                CardBackgroundColor = "#1A2420", // PipBoyGray
                ControlBackgroundColor = "#0D1210", // PipBoyBlackLight
                ControlBorderColor = "#0F8B4A", // Green border
                TextColor = "#1BFF80",          // Green text
                SecondaryTextColor = "#0F9955", // PipBoyTextDim
                HeaderColor = "#0F8B4A",        // Dark green header
                HeaderForegroundColor = "#1BFF80",
                FontFamily = "Monofonto",
                // Menu colors for PipBoy
                MenuBackgroundColor = "#0D1210",
                MenuForegroundColor = "#1BFF80",
                MenuHoverBackgroundColor = "#0F8B4A",
                MenuBorderColor = "#0F8B4A"
            });
            
            // Add Klingon theme (Klingon Empire inspired)
            Themes.Add(new CustomTheme
            {
                Name = "Klingon",
                IsBuiltIn = true,
                PrimaryColor = "#8B0000",       // KlingonBloodRed
                AccentColor = "#D4A574",        // KlingonGold
                AccentHoverColor = "#FFD700",   // KlingonGoldBright
                BackgroundColor = "#1A0505",    // Deep black-red
                SurfaceColor = "#2A0808",       // Dark red surface
                CardBackgroundColor = "#3A0A0A",
                ControlBackgroundColor = "#2A0808", // Dark red control bg
                ControlBorderColor = "#8B0000", // Blood red border
                TextColor = "#FFFFFF",          // White text
                SecondaryTextColor = "#D4A574", // Gold secondary
                HeaderColor = "#8B0000",        // Blood red header
                HeaderForegroundColor = "#FFD700", // Gold header text
                FontFamily = "Klingon",
                // Menu colors for Klingon
                MenuBackgroundColor = "#2A0808",
                MenuForegroundColor = "#FFFFFF",
                MenuHoverBackgroundColor = "#8B0000",
                MenuBorderColor = "#8B0000"
            });
            
            // Load custom themes from file
            try
            {
                var themesFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools", "themes.json");
                    
                if (File.Exists(themesFile))
                {
                    var json = File.ReadAllText(themesFile);
                    var customThemes = JsonSerializer.Deserialize<CustomTheme[]>(json);
                    if (customThemes != null)
                    {
                        foreach (var theme in customThemes)
                        {
                            theme.IsBuiltIn = false;
                            Themes.Add(theme);
                        }
                    }
                }
            }
            catch { }
        }
        
        private void SaveCustomThemes()
        {
            try
            {
                var customThemes = new System.Collections.Generic.List<CustomTheme>();
                foreach (var theme in Themes)
                {
                    if (!theme.IsBuiltIn)
                    {
                        customThemes.Add(theme);
                    }
                }
                
                var themesFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools", "themes.json");
                    
                Directory.CreateDirectory(Path.GetDirectoryName(themesFile)!);
                var json = JsonSerializer.Serialize(customThemes, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(themesFile, json);
            }
            catch { }
        }
        
        private void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeList.SelectedItem is CustomTheme theme)
            {
                _selectedTheme = theme;
                _suppressEvents = true;
                
                // Update theme name box
                ThemeNameBox.Text = theme.Name;
                ThemeNameBox.IsEnabled = !theme.IsBuiltIn;
                
                // Update color previews - Primary & Accent
                UpdateColorDisplay(PrimaryColorPreview, PrimaryColorText, theme.PrimaryColor);
                UpdateColorDisplay(AccentColorPreview, AccentColorText, theme.AccentColor);
                UpdateColorDisplay(AccentHoverColorPreview, AccentHoverColorText, theme.AccentHoverColor);
                
                // Background colors
                UpdateColorDisplay(BackgroundColorPreview, BackgroundColorText, theme.BackgroundColor);
                UpdateColorDisplay(SurfaceColorPreview, SurfaceColorText, theme.SurfaceColor);
                UpdateColorDisplay(CardBackgroundColorPreview, CardBackgroundColorText, theme.CardBackgroundColor);
                UpdateColorDisplay(ControlBorderColorPreview, ControlBorderColorText, theme.ControlBorderColor);
                
                // Text colors
                UpdateColorDisplay(TextColorPreview, TextColorText, theme.TextColor);
                UpdateColorDisplay(SecondaryTextColorPreview, SecondaryTextColorText, theme.SecondaryTextColor);
                
                // Header colors
                UpdateColorDisplay(HeaderColorPreview, HeaderColorText, theme.HeaderColor);
                UpdateColorDisplay(HeaderForegroundColorPreview, HeaderForegroundColorText, theme.HeaderForegroundColor);
                
                // Menu colors
                UpdateColorDisplay(MenuBackgroundColorPreview, MenuBackgroundColorText, theme.MenuBackgroundColor);
                UpdateColorDisplay(MenuForegroundColorPreview, MenuForegroundColorText, theme.MenuForegroundColor);
                UpdateColorDisplay(MenuHoverBackgroundColorPreview, MenuHoverBackgroundColorText, theme.MenuHoverBackgroundColor);
                UpdateColorDisplay(MenuBorderColorPreview, MenuBorderColorText, theme.MenuBorderColor);
                
                // Update font settings
                SelectFontFamily(theme.FontFamily);
                FontSizeSlider.Value = theme.FontSize;
                FontSizeDisplay.Text = $"{theme.FontSize} pt";
                
                // Update logo preview
                UpdateLogoPreview();
                
                DeleteButton.IsEnabled = !theme.IsBuiltIn;
                
                _suppressEvents = false;
                UpdatePreview();
            }
        }
        
        private void SelectFontFamily(string fontFamily)
        {
            foreach (ComboBoxItem item in FontFamilyCombo.Items)
            {
                if (item.Tag?.ToString() == fontFamily)
                {
                    FontFamilyCombo.SelectedItem = item;
                    return;
                }
            }
            // Default to Segoe UI if not found
            FontFamilyCombo.SelectedIndex = 0;
        }
        
        private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents || _selectedTheme == null) return;
            if (FontFamilyCombo.SelectedItem is ComboBoxItem item && item.Tag is string fontName)
            {
                EnsureThemeEditable();
                _selectedTheme.FontFamily = fontName;
                UpdatePreview();
                SaveCustomThemes();
            }
        }
        
        private void FontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressEvents || _selectedTheme == null) return;
            if (FontSizeDisplay != null)
            {
                FontSizeDisplay.Text = $"{FontSizeSlider.Value:F0} pt";
            }
            EnsureThemeEditable();
            _selectedTheme.FontSize = FontSizeSlider.Value;
            UpdatePreview();
            SaveCustomThemes();
        }
        
        private void BrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTheme == null) return;
            
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
                Title = "Select Logo Image"
            };
            
            if (dlg.ShowDialog() == true)
            {
                EnsureThemeEditable();
                _selectedTheme.LogoPath = dlg.FileName;
                UpdateLogoPreview();
                SaveCustomThemes();
            }
        }
        
        private void ResetLogo_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTheme == null) return;
            
            EnsureThemeEditable();
            _selectedTheme.LogoPath = null;
            UpdateLogoPreview();
            SaveCustomThemes();
        }
        
        private void UpdateLogoPreview()
        {
            if (_selectedTheme == null) return;
            
            try
            {
                if (!string.IsNullOrEmpty(_selectedTheme.LogoPath) && File.Exists(_selectedTheme.LogoPath))
                {
                    LogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(_selectedTheme.LogoPath));
                    LogoPathText.Text = Path.GetFileName(_selectedTheme.LogoPath);
                }
                else
                {
                    // Show default logo
                    var defaultPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PlatypusToolsLogo.png");
                    if (File.Exists(defaultPath))
                    {
                        LogoPreview.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(defaultPath));
                    }
                    else
                    {
                        LogoPreview.Source = null;
                    }
                    LogoPathText.Text = "(Default Logo)";
                }
            }
            catch
            {
                LogoPreview.Source = null;
                LogoPathText.Text = "(Error loading)";
            }
        }
        
        /// <summary>
        /// If a built-in theme is being edited, create a copy and switch to it.
        /// </summary>
        private void EnsureThemeEditable()
        {
            if (_selectedTheme == null || !_selectedTheme.IsBuiltIn) return;
            
            // Create a modifiable copy of the built-in theme
            var copy = _selectedTheme.Clone();
            copy.Name = $"{_selectedTheme.Name} (Modified)";
            copy.IsBuiltIn = false;
            
            Themes.Add(copy);
            _suppressEvents = true;
            ThemeList.SelectedItem = copy;
            _selectedTheme = copy;
            ThemeNameBox.Text = copy.Name;
            ThemeNameBox.IsEnabled = true;
            DeleteButton.IsEnabled = true;
            _suppressEvents = false;
            
            SaveCustomThemes();
        }
        
        private void ThemeName_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents || _selectedTheme == null || _selectedTheme.IsBuiltIn) return;
            _selectedTheme.Name = ThemeNameBox.Text;
        }
        
        private void UpdateColorDisplay(System.Windows.Controls.Border preview, TextBox textBox, string color)
        {
            try
            {
                preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                textBox.Text = color;
            }
            catch { }
        }
        
        private void ColorPreview_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border && border.Tag is string colorName)
            {
                // Get current color
                var currentColor = System.Drawing.Color.White;
                if (border.Background is SolidColorBrush brush)
                {
                    currentColor = System.Drawing.Color.FromArgb(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B);
                }
                
                // Open Windows color picker
                using var colorDialog = new System.Windows.Forms.ColorDialog
                {
                    Color = currentColor,
                    FullOpen = true,
                    AnyColor = true
                };
                
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selectedColor = colorDialog.Color;
                    var hexColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
                    
                    // Find the corresponding TextBox and update it (this triggers ColorText_Changed)
                    var textBox = FindTextBoxForColorName(colorName);
                    if (textBox != null)
                    {
                        textBox.Text = hexColor;
                    }
                }
            }
        }
        
        private TextBox? FindTextBoxForColorName(string colorName)
        {
            return colorName switch
            {
                "Primary" => PrimaryColorText,
                "Accent" => AccentColorText,
                "AccentHover" => AccentHoverColorText,
                "Background" => BackgroundColorText,
                "Surface" => SurfaceColorText,
                "CardBackground" => CardBackgroundColorText,
                "ControlBorder" => ControlBorderColorText,
                "Text" => TextColorText,
                "SecondaryText" => SecondaryTextColorText,
                "Header" => HeaderColorText,
                "HeaderForeground" => HeaderForegroundColorText,
                "MenuBackground" => MenuBackgroundColorText,
                "MenuForeground" => MenuForegroundColorText,
                "MenuHoverBackground" => MenuHoverBackgroundColorText,
                "MenuBorder" => MenuBorderColorText,
                _ => null
            };
        }
        
        private void ColorText_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents || _selectedTheme == null) return;
            if (sender is not TextBox textBox || textBox.Tag is not string colorName) return;
            
            var colorValue = textBox.Text.Trim();
            if (!colorValue.StartsWith('#')) return;
            
            // If editing a built-in theme, create a copy
            EnsureThemeEditable();
            
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorValue);
                
                switch (colorName)
                {
                    case "Primary":
                        _selectedTheme.PrimaryColor = colorValue;
                        PrimaryColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "Accent":
                        _selectedTheme.AccentColor = colorValue;
                        AccentColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "AccentHover":
                        _selectedTheme.AccentHoverColor = colorValue;
                        AccentHoverColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "Background":
                        _selectedTheme.BackgroundColor = colorValue;
                        BackgroundColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "Surface":
                        _selectedTheme.SurfaceColor = colorValue;
                        SurfaceColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "CardBackground":
                        _selectedTheme.CardBackgroundColor = colorValue;
                        CardBackgroundColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "ControlBorder":
                        _selectedTheme.ControlBorderColor = colorValue;
                        ControlBorderColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "Text":
                        _selectedTheme.TextColor = colorValue;
                        TextColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "SecondaryText":
                        _selectedTheme.SecondaryTextColor = colorValue;
                        SecondaryTextColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "Header":
                        _selectedTheme.HeaderColor = colorValue;
                        HeaderColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "HeaderForeground":
                        _selectedTheme.HeaderForegroundColor = colorValue;
                        HeaderForegroundColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "MenuBackground":
                        _selectedTheme.MenuBackgroundColor = colorValue;
                        MenuBackgroundColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "MenuForeground":
                        _selectedTheme.MenuForegroundColor = colorValue;
                        MenuForegroundColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "MenuHoverBackground":
                        _selectedTheme.MenuHoverBackgroundColor = colorValue;
                        MenuHoverBackgroundColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "MenuBorder":
                        _selectedTheme.MenuBorderColor = colorValue;
                        MenuBorderColorPreview.Background = new SolidColorBrush(color);
                        break;
                }
                
                UpdatePreview();
            }
            catch { }
        }
        
        private void UpdatePreview()
        {
            if (_selectedTheme == null) return;
            
            try
            {
                // Update preview panel with theme colors
                PreviewPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.BackgroundColor));
                PreviewHeader.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.HeaderColor));
                PreviewHeaderText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.HeaderForegroundColor));
                PreviewPrimaryText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.TextColor));
                PreviewSecondaryText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.SecondaryTextColor));
                PreviewButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.PrimaryColor));
                PreviewSecondaryButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.PrimaryColor));
                PreviewSecondaryButtonText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.PrimaryColor));
                
                // Update font in preview
                var fontFamily = new FontFamily(_selectedTheme.FontFamily);
                PreviewHeaderText.FontFamily = fontFamily;
                PreviewPrimaryText.FontFamily = fontFamily;
                PreviewSecondaryText.FontFamily = fontFamily;
                PreviewPrimaryText.FontSize = _selectedTheme.FontSize;
                PreviewSecondaryText.FontSize = _selectedTheme.FontSize - 1;
            }
            catch { }
        }
        
        private void NewTheme_Click(object sender, RoutedEventArgs e)
        {
            var newTheme = new CustomTheme
            {
                Name = $"Custom Theme {Themes.Count + 1}",
                IsBuiltIn = false,
                PrimaryColor = "#007ACC",
                AccentColor = "#0E639C",
                BackgroundColor = "#FFFFFF",
                SurfaceColor = "#F5F5F5",
                TextColor = "#1E1E1E",
                SecondaryTextColor = "#6E6E6E"
            };
            
            Themes.Add(newTheme);
            ThemeList.SelectedItem = newTheme;
            SaveCustomThemes();
        }
        
        private void DuplicateTheme_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTheme == null) return;
            
            var duplicate = new CustomTheme
            {
                Name = $"{_selectedTheme.Name} (Copy)",
                IsBuiltIn = false,
                PrimaryColor = _selectedTheme.PrimaryColor,
                AccentColor = _selectedTheme.AccentColor,
                BackgroundColor = _selectedTheme.BackgroundColor,
                SurfaceColor = _selectedTheme.SurfaceColor,
                TextColor = _selectedTheme.TextColor,
                SecondaryTextColor = _selectedTheme.SecondaryTextColor
            };
            
            Themes.Add(duplicate);
            ThemeList.SelectedItem = duplicate;
            SaveCustomThemes();
        }
        
        private void DeleteTheme_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTheme == null || _selectedTheme.IsBuiltIn) return;
            
            var result = MessageBox.Show($"Delete theme '{_selectedTheme.Name}'?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                // Delete the XAML file from CustomThemes folder
                try
                {
                    var themesDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "PlatypusTools", "CustomThemes");
                    var safeName = string.Join("_", _selectedTheme.Name.Split(Path.GetInvalidFileNameChars()));
                    var themePath = Path.Combine(themesDir, $"{safeName}.xaml");
                    if (File.Exists(themePath))
                    {
                        File.Delete(themePath);
                    }
                }
                catch { }
                
                Themes.Remove(_selectedTheme);
                if (Themes.Count > 0) ThemeList.SelectedIndex = 0;
                SaveCustomThemes();
            }
        }
        
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Theme files (*.json)|*.json"
            };
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var theme = JsonSerializer.Deserialize<CustomTheme>(json);
                    if (theme != null)
                    {
                        theme.IsBuiltIn = false;
                        Themes.Add(theme);
                        ThemeList.SelectedItem = theme;
                        SaveCustomThemes();
                        MessageBox.Show("Theme imported successfully.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing theme: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTheme == null) return;
            
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Theme files (*.json)|*.json",
                FileName = $"{_selectedTheme.Name}.json"
            };
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_selectedTheme, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);
                    MessageBox.Show("Theme exported successfully.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting theme: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ExportXaml_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTheme == null) return;
            
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "XAML files (*.xaml)|*.xaml",
                FileName = $"{_selectedTheme.Name}.xaml"
            };
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var xaml = _selectedTheme.GenerateXaml();
                    File.WriteAllText(dlg.FileName, xaml);
                    MessageBox.Show("Theme XAML exported successfully.\n\nYou can use this file in other WPF applications or share it.", 
                        "Export XAML", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting XAML: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ResetDefault_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Reset all custom themes to defaults?", "Reset",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                LoadThemes();
                if (Themes.Count > 0) ThemeList.SelectedIndex = 0;
            }
        }
        
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTheme == null) return;
            
            try
            {
                SaveCustomThemes();
                
                if (_selectedTheme.IsBuiltIn)
                {
                    // Use built-in theme
                    ThemeManager.ApplyTheme(_selectedTheme.Name);
                }
                else
                {
                    // Generate and apply custom theme
                    var themePath = _selectedTheme.SaveThemeXaml();
                    ThemeManager.ApplyCustomTheme(_selectedTheme.Name, themePath);
                }
                
                // Save theme preference
                SettingsManager.Current.Theme = _selectedTheme.Name;
                SettingsManager.SaveCurrent();
                
                MessageBox.Show($"Theme '{_selectedTheme.Name}' applied successfully!", "Theme Editor", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying theme: {ex.Message}", "Theme Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
    
    public class CustomTheme
    {
        public string Name { get; set; } = "Custom Theme";
        public bool IsBuiltIn { get; set; }
        
        // Primary colors
        public string PrimaryColor { get; set; } = "#007ACC";
        public string AccentColor { get; set; } = "#0E639C";
        public string AccentHoverColor { get; set; } = "#1177BB";
        
        // Background colors
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public string SurfaceColor { get; set; } = "#F5F5F5";
        public string CardBackgroundColor { get; set; } = "#FAFAFA";
        public string ControlBackgroundColor { get; set; } = "#FFFFFF";
        public string ControlBorderColor { get; set; } = "#D0D0D0";
        
        // Text colors
        public string TextColor { get; set; } = "#1E1E1E";
        public string SecondaryTextColor { get; set; } = "#6E6E6E";
        public string DisabledTextColor { get; set; } = "#A0A0A0";
        
        // Header colors
        public string HeaderColor { get; set; } = "#007ACC";
        public string HeaderForegroundColor { get; set; } = "#FFFFFF";
        
        // Title bar colors
        public string TitleBarBackgroundColor { get; set; } = "#007ACC";
        public string TitleBarForegroundColor { get; set; } = "#FFFFFF";
        
        // Selection and focus
        public string SelectionBackgroundColor { get; set; } = "#0078D4";
        
        // Status colors
        public string WarningBackgroundColor { get; set; } = "#FFF3CD";
        public string WarningBorderColor { get; set; } = "#FF9800";
        public string WarningForegroundColor { get; set; } = "#856404";
        public string InfoBackgroundColor { get; set; } = "#D1ECF1";
        public string InfoBorderColor { get; set; } = "#2196F3";
        public string InfoForegroundColor { get; set; } = "#0C5460";
        public string SuccessBackgroundColor { get; set; } = "#D4EDDA";
        public string SuccessBorderColor { get; set; } = "#4CAF50";
        public string SuccessForegroundColor { get; set; } = "#155724";
        public string ErrorBackgroundColor { get; set; } = "#F8D7DA";
        public string ErrorBorderColor { get; set; } = "#F44336";
        public string ErrorForegroundColor { get; set; } = "#721C24";
        
        // Menu colors
        public string MenuBackgroundColor { get; set; } = "#F5F5F5";
        public string MenuForegroundColor { get; set; } = "#1E1E1E";
        public string MenuHoverBackgroundColor { get; set; } = "#E0E0E0";
        public string MenuBorderColor { get; set; } = "#D0D0D0";
        
        // Font settings
        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 12;
        
        // Custom logo path (null or empty means use default)
        public string? LogoPath { get; set; }
        
        /// <summary>
        /// Determines if this is a dark theme (for title bar styling).
        /// </summary>
        public bool IsDarkTheme => IsColorDark(BackgroundColor);
        
        private static bool IsColorDark(string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                // Calculate relative luminance
                double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
                return luminance < 0.5;
            }
            catch { return false; }
        }
        
        /// <summary>
        /// Creates a deep copy of this theme.
        /// </summary>
        public CustomTheme Clone()
        {
            return new CustomTheme
            {
                Name = Name,
                IsBuiltIn = false,
                PrimaryColor = PrimaryColor,
                AccentColor = AccentColor,
                AccentHoverColor = AccentHoverColor,
                BackgroundColor = BackgroundColor,
                SurfaceColor = SurfaceColor,
                CardBackgroundColor = CardBackgroundColor,
                ControlBackgroundColor = ControlBackgroundColor,
                ControlBorderColor = ControlBorderColor,
                TextColor = TextColor,
                SecondaryTextColor = SecondaryTextColor,
                DisabledTextColor = DisabledTextColor,
                HeaderColor = HeaderColor,
                HeaderForegroundColor = HeaderForegroundColor,
                TitleBarBackgroundColor = TitleBarBackgroundColor,
                TitleBarForegroundColor = TitleBarForegroundColor,
                SelectionBackgroundColor = SelectionBackgroundColor,
                WarningBackgroundColor = WarningBackgroundColor,
                WarningBorderColor = WarningBorderColor,
                WarningForegroundColor = WarningForegroundColor,
                InfoBackgroundColor = InfoBackgroundColor,
                InfoBorderColor = InfoBorderColor,
                InfoForegroundColor = InfoForegroundColor,
                SuccessBackgroundColor = SuccessBackgroundColor,
                SuccessBorderColor = SuccessBorderColor,
                SuccessForegroundColor = SuccessForegroundColor,
                ErrorBackgroundColor = ErrorBackgroundColor,
                ErrorBorderColor = ErrorBorderColor,
                ErrorForegroundColor = ErrorForegroundColor,
                MenuBackgroundColor = MenuBackgroundColor,
                MenuForegroundColor = MenuForegroundColor,
                MenuHoverBackgroundColor = MenuHoverBackgroundColor,
                MenuBorderColor = MenuBorderColor,
                FontFamily = FontFamily,
                FontSize = FontSize,
                LogoPath = LogoPath
            };
        }
        
        /// <summary>
        /// Generates a XAML ResourceDictionary for this theme.
        /// </summary>
        public string GenerateXaml()
        {
            return $@"<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    
    <!-- Custom Theme: {Name} -->
    <!-- Generated by PlatypusTools Theme Editor -->
    
    <!-- Font Settings -->
    <FontFamily x:Key=""AppFontFamily"">{FontFamily}</FontFamily>
    <sys:Double x:Key=""AppFontSize"" xmlns:sys=""clr-namespace:System;assembly=mscorlib"">{FontSize}</sys:Double>
    
    <!-- Core Brushes -->
    <SolidColorBrush x:Key=""WindowBackgroundBrush"" Color=""{BackgroundColor}"" />
    <SolidColorBrush x:Key=""WindowForegroundBrush"" Color=""{TextColor}"" />
    <SolidColorBrush x:Key=""MenuForegroundBrush"" Color=""{MenuForegroundColor}"" />
    <SolidColorBrush x:Key=""ControlBackgroundBrush"" Color=""{ControlBackgroundColor}"" />
    <SolidColorBrush x:Key=""ControlBorderBrush"" Color=""{ControlBorderColor}"" />
    <SolidColorBrush x:Key=""AccentBrush"" Color=""{AccentColor}"" />
    <SolidColorBrush x:Key=""AccentHoverBrush"" Color=""{AccentHoverColor}"" />
    <SolidColorBrush x:Key=""DisabledForegroundBrush"" Color=""{DisabledTextColor}"" />
    <SolidColorBrush x:Key=""SelectionBackgroundBrush"" Color=""{SelectionBackgroundColor}"" />
    
    <!-- Title Bar brushes -->
    <SolidColorBrush x:Key=""TitleBarBackgroundBrush"" Color=""{TitleBarBackgroundColor}""/>
    <SolidColorBrush x:Key=""TitleBarForegroundBrush"" Color=""{TitleBarForegroundColor}""/>
    <SolidColorBrush x:Key=""TitleBarButtonHoverBrush"" Color=""#33{(IsDarkTheme ? "FFFFFF" : "000000")}""/>
    <SolidColorBrush x:Key=""TitleBarCloseHoverBrush"" Color=""#FFE81123""/>
    
    <!-- Header brush for MainWindow -->
    <SolidColorBrush x:Key=""HeaderBrush"" Color=""{HeaderColor}""/>
    <SolidColorBrush x:Key=""HeaderForegroundBrush"" Color=""{HeaderForegroundColor}""/>
    
    <!-- Additional resource keys used by views -->
    <SolidColorBrush x:Key=""CardBackground"" Color=""{CardBackgroundColor}"" />
    <SolidColorBrush x:Key=""CardBackgroundBrush"" Color=""{CardBackgroundColor}"" />
    <SolidColorBrush x:Key=""TextPrimary"" Color=""{TextColor}"" />
    <SolidColorBrush x:Key=""TextSecondary"" Color=""{SecondaryTextColor}"" />
    <SolidColorBrush x:Key=""TextPrimaryBrush"" Color=""{TextColor}"" />
    <SolidColorBrush x:Key=""TextSecondaryBrush"" Color=""{SecondaryTextColor}"" />
    <SolidColorBrush x:Key=""SecondaryTextBrush"" Color=""{SecondaryTextColor}"" />
    <SolidColorBrush x:Key=""BackgroundBrush"" Color=""{BackgroundColor}"" />
    <SolidColorBrush x:Key=""SurfaceBrush"" Color=""{SurfaceColor}"" />
    <SolidColorBrush x:Key=""PrimaryBrush"" Color=""{PrimaryColor}"" />
    <SolidColorBrush x:Key=""BorderBrush"" Color=""{ControlBorderColor}"" />
    <SolidColorBrush x:Key=""ForegroundDimBrush"" Color=""{SecondaryTextColor}"" />
    
    <!-- Warning/Info box brushes -->
    <SolidColorBrush x:Key=""WarningBackgroundBrush"" Color=""{WarningBackgroundColor}"" />
    <SolidColorBrush x:Key=""WarningBorderBrush"" Color=""{WarningBorderColor}"" />
    <SolidColorBrush x:Key=""WarningForegroundBrush"" Color=""{WarningForegroundColor}"" />
    <SolidColorBrush x:Key=""InfoBackgroundBrush"" Color=""{InfoBackgroundColor}"" />
    <SolidColorBrush x:Key=""InfoBorderBrush"" Color=""{InfoBorderColor}"" />
    <SolidColorBrush x:Key=""InfoForegroundBrush"" Color=""{InfoForegroundColor}"" />
    <SolidColorBrush x:Key=""SuccessBackgroundBrush"" Color=""{SuccessBackgroundColor}"" />
    <SolidColorBrush x:Key=""SuccessBorderBrush"" Color=""{SuccessBorderColor}"" />
    <SolidColorBrush x:Key=""SuccessForegroundBrush"" Color=""{SuccessForegroundColor}"" />
    <SolidColorBrush x:Key=""ErrorBackgroundBrush"" Color=""{ErrorBackgroundColor}"" />
    <SolidColorBrush x:Key=""ErrorBorderBrush"" Color=""{ErrorBorderColor}"" />
    <SolidColorBrush x:Key=""ErrorForegroundBrush"" Color=""{ErrorForegroundColor}"" />

    <!-- Window Style -->
    <Style TargetType=""Window"">
        <Setter Property=""Background"" Value=""{{StaticResource WindowBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""FontSize"" Value=""{{StaticResource AppFontSize}}"" />
    </Style>

    <!-- Grid, StackPanel, DockPanel inherit from parent -->
    <Style TargetType=""Grid"">
        <Setter Property=""Background"" Value=""Transparent"" />
    </Style>
    
    <Style TargetType=""DockPanel"">
        <Setter Property=""Background"" Value=""{{StaticResource WindowBackgroundBrush}}"" />
    </Style>

    <!-- TextBlock -->
    <Style TargetType=""TextBlock"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
    </Style>

    <!-- Label -->
    <Style TargetType=""Label"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
    </Style>

    <!-- TextBox -->
    <Style TargetType=""TextBox"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""CaretBrush"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""SelectionBrush"" Value=""{{StaticResource SelectionBackgroundBrush}}"" />
        <Setter Property=""BorderThickness"" Value=""1"" />
        <Setter Property=""Padding"" Value=""4,2"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""TextBox"">
                    <Border x:Name=""border""
                            Background=""{{TemplateBinding Background}}""
                            BorderBrush=""{{TemplateBinding BorderBrush}}""
                            BorderThickness=""{{TemplateBinding BorderThickness}}""
                            CornerRadius=""2""
                            SnapsToDevicePixels=""True"">
                        <ScrollViewer x:Name=""PART_ContentHost""
                                      Focusable=""False""
                                      HorizontalScrollBarVisibility=""Hidden""
                                      VerticalScrollBarVisibility=""Hidden""
                                      Margin=""{{TemplateBinding Padding}}"" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""border"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}"" />
                        </Trigger>
                        <Trigger Property=""IsFocused"" Value=""True"">
                            <Setter TargetName=""border"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}"" />
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Foreground"" Value=""{{StaticResource DisabledForegroundBrush}}"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Button -->
    <Style TargetType=""Button"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""BorderThickness"" Value=""1"" />
        <Setter Property=""Padding"" Value=""10,5"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""Button"">
                    <Border x:Name=""border""
                            Background=""{{TemplateBinding Background}}""
                            BorderBrush=""{{TemplateBinding BorderBrush}}""
                            BorderThickness=""{{TemplateBinding BorderThickness}}""
                            CornerRadius=""3""
                            SnapsToDevicePixels=""True"">
                        <ContentPresenter x:Name=""contentPresenter""
                                          HorizontalAlignment=""{{TemplateBinding HorizontalContentAlignment}}""
                                          VerticalAlignment=""{{TemplateBinding VerticalContentAlignment}}""
                                          Margin=""{{TemplateBinding Padding}}""
                                          RecognizesAccessKey=""True""
                                          SnapsToDevicePixels=""{{TemplateBinding SnapsToDevicePixels}}"">
                            <ContentPresenter.Resources>
                                <Style TargetType=""TextBlock"">
                                    <Setter Property=""Foreground"" Value=""{{Binding Foreground, RelativeSource={{RelativeSource AncestorType=Button}}}}"" />
                                </Style>
                            </ContentPresenter.Resources>
                        </ContentPresenter>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""border"" Property=""Background"" Value=""{{StaticResource AccentHoverBrush}}"" />
                            <Setter TargetName=""border"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}"" />
                        </Trigger>
                        <Trigger Property=""IsPressed"" Value=""True"">
                            <Setter TargetName=""border"" Property=""Background"" Value=""{{StaticResource AccentBrush}}"" />
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Foreground"" Value=""{{StaticResource DisabledForegroundBrush}}"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- TabControl -->
    <Style TargetType=""TabControl"">
        <Setter Property=""Background"" Value=""{{StaticResource WindowBackgroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
    </Style>

    <!-- TabItem -->
    <Style TargetType=""TabItem"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""Background"" Value=""Transparent"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""Padding"" Value=""12,6"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""TabItem"">
                    <Border x:Name=""border""
                            Background=""{{TemplateBinding Background}}""
                            BorderBrush=""{{StaticResource ControlBorderBrush}}""
                            BorderThickness=""1,1,1,0""
                            CornerRadius=""4,4,0,0""
                            Padding=""{{TemplateBinding Padding}}"">
                        <ContentPresenter x:Name=""contentPresenter""
                                          ContentSource=""Header""
                                          HorizontalAlignment=""Center""
                                          VerticalAlignment=""Center"">
                            <ContentPresenter.Resources>
                                <Style TargetType=""TextBlock"">
                                    <Setter Property=""Foreground"" Value=""{{Binding Foreground, RelativeSource={{RelativeSource AncestorType=TabItem}}}}"" />
                                </Style>
                            </ContentPresenter.Resources>
                        </ContentPresenter>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsSelected"" Value=""True"">
                            <Setter TargetName=""border"" Property=""Background"" Value=""{{StaticResource SurfaceBrush}}"" />
                            <Setter Property=""Foreground"" Value=""{{StaticResource AccentBrush}}"" />
                        </Trigger>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""border"" Property=""Background"" Value=""{{StaticResource CardBackgroundBrush}}"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ListBox -->
    <Style TargetType=""ListBox"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
    </Style>

    <!-- ListBoxItem -->
    <Style TargetType=""ListBoxItem"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""Padding"" Value=""8,4"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Style.Triggers>
            <Trigger Property=""IsSelected"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource SelectionBackgroundBrush}}"" />
                <Setter Property=""Foreground"" Value=""White"" />
            </Trigger>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource AccentHoverBrush}}"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ListView -->
    <Style TargetType=""ListView"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""BorderThickness"" Value=""1""/>
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
    </Style>

    <!-- ListViewItem -->
    <Style TargetType=""ListViewItem"">
        <Setter Property=""Background"" Value=""Transparent""/>
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""Padding"" Value=""8,4"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Style.Triggers>
            <Trigger Property=""IsSelected"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource SelectionBackgroundBrush}}"" />
            </Trigger>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource AccentHoverBrush}}"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ComboBox Toggle Button Template -->
    <ControlTemplate x:Key=""CustomComboBoxToggleButton"" TargetType=""ToggleButton"">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition />
                <ColumnDefinition Width=""20"" />
            </Grid.ColumnDefinitions>
            <Border x:Name=""Border"" 
                    Grid.ColumnSpan=""2""
                    Background=""{{StaticResource ControlBackgroundBrush}}""
                    BorderBrush=""{{StaticResource ControlBorderBrush}}""
                    BorderThickness=""1""
                    CornerRadius=""2"" />
            <Border Grid.Column=""0""
                    Margin=""1""
                    Background=""{{StaticResource ControlBackgroundBrush}}"" 
                    BorderThickness=""0"" />
            <Path x:Name=""Arrow""
                  Grid.Column=""1""     
                  Fill=""{{StaticResource WindowForegroundBrush}}""
                  HorizontalAlignment=""Center""
                  VerticalAlignment=""Center""
                  Data=""M0,0 L4,4 L8,0 Z"" />
        </Grid>
        <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""Border"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}"" />
            </Trigger>
            <Trigger Property=""IsChecked"" Value=""True"">
                <Setter TargetName=""Border"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}"" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <ControlTemplate x:Key=""CustomComboBoxTextBox"" TargetType=""TextBox"">
        <Border x:Name=""PART_ContentHost"" Focusable=""False"" Background=""{{TemplateBinding Background}}"" />
    </ControlTemplate>

    <!-- ComboBox -->
    <Style TargetType=""ComboBox"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
        <Setter Property=""OverridesDefaultStyle"" Value=""True""/>
        <Setter Property=""ScrollViewer.HorizontalScrollBarVisibility"" Value=""Auto""/>
        <Setter Property=""ScrollViewer.VerticalScrollBarVisibility"" Value=""Auto""/>
        <Setter Property=""ScrollViewer.CanContentScroll"" Value=""True""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""ComboBox"">
                    <Grid>
                        <ToggleButton x:Name=""ToggleButton"" 
                                      Template=""{{StaticResource CustomComboBoxToggleButton}}""
                                      Focusable=""False""
                                      IsChecked=""{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource TemplatedParent}}}}""
                                      ClickMode=""Press"" />
                        <ContentPresenter x:Name=""ContentSite""
                                          IsHitTestVisible=""False"" 
                                          Content=""{{TemplateBinding SelectionBoxItem}}""
                                          ContentTemplate=""{{TemplateBinding SelectionBoxItemTemplate}}""
                                          ContentTemplateSelector=""{{TemplateBinding ItemTemplateSelector}}""
                                          Margin=""6,3,23,3""
                                          VerticalAlignment=""Center""
                                          HorizontalAlignment=""Left"">
                            <ContentPresenter.Resources>
                                <Style TargetType=""TextBlock"">
                                    <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
                                </Style>
                            </ContentPresenter.Resources>
                        </ContentPresenter>
                        <TextBox x:Name=""PART_EditableTextBox""
                                 Style=""{{x:Null}}"" 
                                 Template=""{{StaticResource CustomComboBoxTextBox}}"" 
                                 HorizontalAlignment=""Left"" 
                                 VerticalAlignment=""Center"" 
                                 Margin=""3,3,23,3""
                                 Focusable=""True"" 
                                 Background=""Transparent""
                                 Foreground=""{{StaticResource WindowForegroundBrush}}""
                                 CaretBrush=""{{StaticResource AccentBrush}}""
                                 Visibility=""Hidden""
                                 IsReadOnly=""{{TemplateBinding IsReadOnly}}"" />
                        <Popup x:Name=""Popup""
                               Placement=""Bottom""
                               IsOpen=""{{TemplateBinding IsDropDownOpen}}""
                               AllowsTransparency=""True"" 
                               Focusable=""False""
                               PopupAnimation=""Slide"">
                            <Grid x:Name=""DropDown""
                                  SnapsToDevicePixels=""True""                
                                  MinWidth=""{{TemplateBinding ActualWidth}}""
                                  MaxHeight=""{{TemplateBinding MaxDropDownHeight}}"">
                                <Border x:Name=""DropDownBorder""
                                        Background=""{{StaticResource ControlBackgroundBrush}}""
                                        BorderThickness=""1""
                                        BorderBrush=""{{StaticResource AccentBrush}}""/>
                                <ScrollViewer Margin=""4,6,4,6"" SnapsToDevicePixels=""True"">
                                    <StackPanel IsItemsHost=""True"" KeyboardNavigation.DirectionalNavigation=""Contained"" />
                                </ScrollViewer>
                            </Grid>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""HasItems"" Value=""False"">
                            <Setter TargetName=""DropDownBorder"" Property=""MinHeight"" Value=""95"" />
                        </Trigger>
                        <Trigger Property=""IsEditable"" Value=""True"">
                            <Setter TargetName=""PART_EditableTextBox"" Property=""Visibility"" Value=""Visible"" />
                            <Setter TargetName=""ContentSite"" Property=""Visibility"" Value=""Hidden"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    
    <!-- ComboBoxItem -->
    <Style TargetType=""ComboBoxItem"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}""/>
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}""/>
        <Setter Property=""Padding"" Value=""6,4""/>
        <Setter Property=""SnapsToDevicePixels"" Value=""True""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""ComboBoxItem"">
                    <Border x:Name=""Border""
                            Padding=""{{TemplateBinding Padding}}""
                            Background=""{{TemplateBinding Background}}""
                            SnapsToDevicePixels=""True"">
                        <ContentPresenter>
                            <ContentPresenter.Resources>
                                <Style TargetType=""TextBlock"">
                                    <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
                                </Style>
                            </ContentPresenter.Resources>
                        </ContentPresenter>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsHighlighted"" Value=""True"">
                            <Setter TargetName=""Border"" Property=""Background"" Value=""{{StaticResource AccentHoverBrush}}"" />
                        </Trigger>
                        <Trigger Property=""IsSelected"" Value=""True"">
                            <Setter TargetName=""Border"" Property=""Background"" Value=""{{StaticResource SelectionBackgroundBrush}}"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- CheckBox -->
    <Style TargetType=""CheckBox"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""Cursor"" Value=""Hand""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""CheckBox"">
                    <StackPanel Orientation=""Horizontal"">
                        <Border x:Name=""checkBorder""
                                Width=""18"" Height=""18""
                                Background=""{{StaticResource ControlBackgroundBrush}}""
                                BorderBrush=""{{StaticResource ControlBorderBrush}}""
                                BorderThickness=""1""
                                CornerRadius=""2""
                                Margin=""0,0,8,0"">
                            <TextBlock x:Name=""checkMark""
                                       Text=""✓""
                                       FontSize=""14""
                                       FontWeight=""Bold""
                                       Foreground=""{{StaticResource AccentBrush}}""
                                       HorizontalAlignment=""Center""
                                       VerticalAlignment=""Center""
                                       Visibility=""Collapsed""/>
                        </Border>
                        <ContentPresenter VerticalAlignment=""Center""/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsChecked"" Value=""True"">
                            <Setter TargetName=""checkMark"" Property=""Visibility"" Value=""Visible""/>
                            <Setter TargetName=""checkBorder"" Property=""Background"" Value=""{{StaticResource AccentHoverBrush}}""/>
                            <Setter TargetName=""checkBorder"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}""/>
                        </Trigger>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""checkBorder"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}""/>
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Foreground"" Value=""{{StaticResource DisabledForegroundBrush}}""/>
                            <Setter TargetName=""checkBorder"" Property=""BorderBrush"" Value=""{{StaticResource DisabledForegroundBrush}}""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- RadioButton -->
    <Style TargetType=""RadioButton"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""Cursor"" Value=""Hand""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""RadioButton"">
                    <StackPanel Orientation=""Horizontal"">
                        <Border x:Name=""radioBorder""
                                Width=""18"" Height=""18""
                                Background=""{{StaticResource ControlBackgroundBrush}}""
                                BorderBrush=""{{StaticResource ControlBorderBrush}}""
                                BorderThickness=""1""
                                CornerRadius=""9""
                                Margin=""0,0,8,0"">
                            <Ellipse x:Name=""radioMark""
                                     Width=""10"" Height=""10""
                                     Fill=""{{StaticResource AccentBrush}}""
                                     HorizontalAlignment=""Center""
                                     VerticalAlignment=""Center""
                                     Visibility=""Collapsed""/>
                        </Border>
                        <ContentPresenter VerticalAlignment=""Center""/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsChecked"" Value=""True"">
                            <Setter TargetName=""radioMark"" Property=""Visibility"" Value=""Visible""/>
                            <Setter TargetName=""radioBorder"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}""/>
                        </Trigger>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""radioBorder"" Property=""BorderBrush"" Value=""{{StaticResource AccentBrush}}""/>
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Foreground"" Value=""{{StaticResource DisabledForegroundBrush}}""/>
                            <Setter TargetName=""radioBorder"" Property=""BorderBrush"" Value=""{{StaticResource DisabledForegroundBrush}}""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- GroupBox -->
    <Style TargetType=""GroupBox"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}""/>
        <Setter Property=""Padding"" Value=""8""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""GroupBox"">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""Auto""/>
                            <RowDefinition Height=""*""/>
                        </Grid.RowDefinitions>
                        <Border Background=""{{StaticResource SurfaceBrush}}"" 
                                BorderBrush=""{{StaticResource ControlBorderBrush}}"" 
                                BorderThickness=""1,1,1,0""
                                Padding=""12,6"">
                            <ContentPresenter ContentSource=""Header"" 
                                              TextElement.Foreground=""{{StaticResource WindowForegroundBrush}}""
                                              TextElement.FontWeight=""Bold""/>
                        </Border>
                        <Border Grid.Row=""1"" 
                                Background=""{{TemplateBinding Background}}""
                                BorderBrush=""{{StaticResource ControlBorderBrush}}""
                                BorderThickness=""1,0,1,1""
                                Padding=""{{TemplateBinding Padding}}"">
                            <ContentPresenter/>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Menu -->
    <Style TargetType=""Menu"">
        <Setter Property=""Background"" Value=""{MenuBackgroundColor}"" />
        <Setter Property=""Foreground"" Value=""{MenuForegroundColor}"" />
        <Setter Property=""BorderBrush"" Value=""{MenuBorderColor}"" />
        <Setter Property=""BorderThickness"" Value=""0"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
    </Style>

    <!-- MenuItem - Full template for proper theming -->
    <Style TargetType=""MenuItem"">
        <Setter Property=""Background"" Value=""Transparent"" />
        <Setter Property=""Foreground"" Value=""{MenuForegroundColor}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""Padding"" Value=""8,4""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""MenuItem"">
                    <Border x:Name=""Border"" 
                            Background=""{{TemplateBinding Background}}"" 
                            BorderBrush=""Transparent"" 
                            BorderThickness=""0""
                            Padding=""{{TemplateBinding Padding}}"">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width=""Auto"" SharedSizeGroup=""Icon""/>
                                <ColumnDefinition Width=""*""/>
                                <ColumnDefinition Width=""Auto"" SharedSizeGroup=""Shortcut""/>
                                <ColumnDefinition Width=""Auto""/>
                            </Grid.ColumnDefinitions>
                            <ContentPresenter x:Name=""Icon"" Grid.Column=""0"" Margin=""0,0,6,0"" 
                                              VerticalAlignment=""Center"" ContentSource=""Icon""/>
                            <ContentPresenter x:Name=""HeaderHost"" Grid.Column=""1"" 
                                              ContentSource=""Header""
                                              RecognizesAccessKey=""True""
                                              VerticalAlignment=""Center""/>
                            <TextBlock x:Name=""InputGestureText"" Grid.Column=""2"" 
                                       Text=""{{TemplateBinding InputGestureText}}"" 
                                       Margin=""24,0,0,0"" VerticalAlignment=""Center""
                                       Opacity=""0.7""/>
                            <Path x:Name=""Arrow"" Grid.Column=""3"" 
                                  Data=""M0,0 L4,4 L0,8 Z"" 
                                  Fill=""{MenuForegroundColor}""
                                  VerticalAlignment=""Center"" Margin=""6,0,0,0""
                                  Visibility=""Collapsed""/>
                            <Popup x:Name=""PART_Popup"" 
                                   IsOpen=""{{Binding IsSubmenuOpen, RelativeSource={{RelativeSource TemplatedParent}}}}""
                                   Placement=""Bottom"" 
                                   AllowsTransparency=""True"" 
                                   Focusable=""False""
                                   PopupAnimation=""Fade"">
                                <Border Background=""{MenuBackgroundColor}"" 
                                        BorderBrush=""{MenuBorderColor}"" 
                                        BorderThickness=""1"">
                                    <StackPanel IsItemsHost=""True"" KeyboardNavigation.DirectionalNavigation=""Cycle""/>
                                </Border>
                            </Popup>
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""Role"" Value=""TopLevelHeader"">
                            <Setter TargetName=""Arrow"" Property=""Visibility"" Value=""Collapsed""/>
                            <Setter TargetName=""PART_Popup"" Property=""Placement"" Value=""Bottom""/>
                        </Trigger>
                        <Trigger Property=""Role"" Value=""TopLevelItem"">
                            <Setter TargetName=""Arrow"" Property=""Visibility"" Value=""Collapsed""/>
                        </Trigger>
                        <Trigger Property=""Role"" Value=""SubmenuHeader"">
                            <Setter TargetName=""Arrow"" Property=""Visibility"" Value=""Visible""/>
                            <Setter TargetName=""PART_Popup"" Property=""Placement"" Value=""Right""/>
                        </Trigger>
                        <Trigger Property=""Role"" Value=""SubmenuItem"">
                            <Setter TargetName=""Arrow"" Property=""Visibility"" Value=""Collapsed""/>
                        </Trigger>
                        <Trigger Property=""IsHighlighted"" Value=""True"">
                            <Setter TargetName=""Border"" Property=""Background"" Value=""{MenuHoverBackgroundColor}""/>
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Foreground"" Value=""{{StaticResource DisabledForegroundBrush}}""/>
                        </Trigger>
                        <Trigger Property=""Icon"" Value=""{{x:Null}}"">
                            <Setter TargetName=""Icon"" Property=""Visibility"" Value=""Collapsed""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ContextMenu -->
    <Style TargetType=""ContextMenu"">
        <Setter Property=""Background"" Value=""{MenuBackgroundColor}"" />
        <Setter Property=""Foreground"" Value=""{MenuForegroundColor}"" />
        <Setter Property=""BorderBrush"" Value=""{MenuBorderColor}"" />
        <Setter Property=""BorderThickness"" Value=""1"" />
    </Style>

    <!-- Separator -->
    <Style TargetType=""Separator"">
        <Setter Property=""Background"" Value=""{MenuBorderColor}""/>
        <Setter Property=""Height"" Value=""1""/>
        <Setter Property=""Margin"" Value=""4,2""/>
    </Style>

    <!-- ScrollViewer and ScrollBar -->
    <Style TargetType=""ScrollViewer"">
        <Setter Property=""Background"" Value=""Transparent"" />
    </Style>

    <!-- ProgressBar -->
    <Style TargetType=""ProgressBar"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource AccentBrush}}"" />
    </Style>

    <!-- Slider -->
    <Style TargetType=""Slider"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBorderBrush}}"" />
    </Style>

    <!-- DataGrid -->
    <Style TargetType=""DataGrid"">
        <Setter Property=""Background"" Value=""{{StaticResource WindowBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""GridLinesVisibility"" Value=""None"" />
        <Setter Property=""RowBackground"" Value=""{{StaticResource WindowBackgroundBrush}}"" />
        <Setter Property=""AlternatingRowBackground"" Value=""{{StaticResource SurfaceBrush}}"" />
    </Style>

    <Style TargetType=""DataGridColumnHeader"">
        <Setter Property=""Background"" Value=""{{StaticResource SurfaceBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
        <Setter Property=""Padding"" Value=""8,4"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""BorderThickness"" Value=""0,0,1,1"" />
    </Style>

    <Style TargetType=""DataGridCell"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderThickness"" Value=""0"" />
        <Style.Triggers>
            <Trigger Property=""IsSelected"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource SelectionBackgroundBrush}}"" />
                <Setter Property=""Foreground"" Value=""White"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType=""DataGridRow"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Style.Triggers>
            <Trigger Property=""IsSelected"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource SelectionBackgroundBrush}}"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- TreeView -->
    <Style TargetType=""TreeView"">
        <Setter Property=""Background"" Value=""{{StaticResource WindowBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
        <Setter Property=""FontFamily"" Value=""{{StaticResource AppFontFamily}}"" />
    </Style>

    <Style TargetType=""TreeViewItem"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Style.Triggers>
            <Trigger Property=""IsSelected"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource SelectionBackgroundBrush}}"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- Expander -->
    <Style TargetType=""Expander"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
    </Style>

    <!-- ToolTip -->
    <Style TargetType=""ToolTip"">
        <Setter Property=""Background"" Value=""{{StaticResource SurfaceBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
    </Style>

    <!-- StatusBar -->
    <Style TargetType=""StatusBar"">
        <Setter Property=""Background"" Value=""{{StaticResource SurfaceBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
    </Style>

</ResourceDictionary>";
        }
        
        /// <summary>
        /// Saves the generated XAML to the custom themes folder.
        /// </summary>
        public string SaveThemeXaml()
        {
            var themesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "CustomThemes");
            Directory.CreateDirectory(themesDir);
            
            var safeName = string.Join("_", Name.Split(Path.GetInvalidFileNameChars()));
            var themePath = Path.Combine(themesDir, $"{safeName}.xaml");
            
            File.WriteAllText(themePath, GenerateXaml());
            return themePath;
        }
    }
    
    /// <summary>
    /// Converter for displaying "Built-in" or "Custom" based on IsBuiltIn property.
    /// </summary>
    public class BoolToBuiltInConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool isBuiltIn && isBuiltIn ? "Built-in" : "Custom";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
