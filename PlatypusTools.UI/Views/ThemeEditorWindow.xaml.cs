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
                TextColor = "#D4D4D4",
                SecondaryTextColor = "#808080"
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
                
                DeleteButton.IsEnabled = !theme.IsBuiltIn;
                
                _suppressEvents = false;
                UpdatePreview();
            }
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
                _ => null
            };
        }
        
        private void ColorText_Changed(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents || _selectedTheme == null) return;
            if (sender is not TextBox textBox || textBox.Tag is not string colorName) return;
            
            var colorValue = textBox.Text.Trim();
            if (!colorValue.StartsWith("#")) return;
            
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
        public string MenuForegroundColor { get; set; } = "#1E1E1E";
        
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
                MenuForegroundColor = MenuForegroundColor
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
    </Style>

    <!-- Label -->
    <Style TargetType=""Label"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
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
    </Style>

    <!-- TabItem -->
    <Style TargetType=""TabItem"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""Background"" Value=""Transparent"" />
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
    </Style>

    <!-- ListBoxItem -->
    <Style TargetType=""ListBoxItem"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""Padding"" Value=""8,4"" />
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

    <!-- ComboBox -->
    <Style TargetType=""ComboBox"">
        <Setter Property=""Background"" Value=""{{StaticResource ControlBackgroundBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
    </Style>

    <!-- CheckBox -->
    <Style TargetType=""CheckBox"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
    </Style>

    <!-- RadioButton -->
    <Style TargetType=""RadioButton"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
    </Style>

    <!-- GroupBox -->
    <Style TargetType=""GroupBox"">
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
        <Setter Property=""BorderBrush"" Value=""{{StaticResource ControlBorderBrush}}"" />
    </Style>

    <!-- Menu -->
    <Style TargetType=""Menu"">
        <Setter Property=""Background"" Value=""{{StaticResource SurfaceBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource MenuForegroundBrush}}"" />
    </Style>

    <!-- MenuItem -->
    <Style TargetType=""MenuItem"">
        <Setter Property=""Foreground"" Value=""{{StaticResource MenuForegroundBrush}}"" />
        <Style.Triggers>
            <Trigger Property=""IsHighlighted"" Value=""True"">
                <Setter Property=""Background"" Value=""{{StaticResource AccentHoverBrush}}"" />
            </Trigger>
        </Style.Triggers>
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
        <Setter Property=""GridLinesVisibility"" Value=""None"" />
        <Setter Property=""RowBackground"" Value=""{{StaticResource WindowBackgroundBrush}}"" />
        <Setter Property=""AlternatingRowBackground"" Value=""{{StaticResource SurfaceBrush}}"" />
    </Style>

    <Style TargetType=""DataGridColumnHeader"">
        <Setter Property=""Background"" Value=""{{StaticResource SurfaceBrush}}"" />
        <Setter Property=""Foreground"" Value=""{{StaticResource WindowForegroundBrush}}"" />
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
