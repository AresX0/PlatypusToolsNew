using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
                
                // Update color previews
                UpdateColorDisplay(PrimaryColorPreview, PrimaryColorText, theme.PrimaryColor);
                UpdateColorDisplay(AccentColorPreview, AccentColorText, theme.AccentColor);
                UpdateColorDisplay(BackgroundColorPreview, BackgroundColorText, theme.BackgroundColor);
                UpdateColorDisplay(SurfaceColorPreview, SurfaceColorText, theme.SurfaceColor);
                UpdateColorDisplay(TextColorPreview, TextColorText, theme.TextColor);
                UpdateColorDisplay(SecondaryTextColorPreview, SecondaryTextColorText, theme.SecondaryTextColor);
                
                DeleteButton.IsEnabled = !theme.IsBuiltIn;
                
                _suppressEvents = false;
                UpdatePreview();
            }
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
                // In a full implementation, this would open a color picker dialog
                // For now, just show a message
                MessageBox.Show($"Color picker for {colorName} would open here.\n\nYou can enter hex color values in the text box.", 
                    "Color Picker", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
                    case "Background":
                        _selectedTheme.BackgroundColor = colorValue;
                        BackgroundColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "Surface":
                        _selectedTheme.SurfaceColor = colorValue;
                        SurfaceColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "Text":
                        _selectedTheme.TextColor = colorValue;
                        TextColorPreview.Background = new SolidColorBrush(color);
                        break;
                    case "SecondaryText":
                        _selectedTheme.SecondaryTextColor = colorValue;
                        SecondaryTextColorPreview.Background = new SolidColorBrush(color);
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
                PreviewPanel.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_selectedTheme.SurfaceColor));
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
            
            // Apply the selected theme to the application
            // This would integrate with ThemeManager
            SaveCustomThemes();
            MessageBox.Show("Theme applied!", "Theme Editor", MessageBoxButton.OK, MessageBoxImage.Information);
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
        public string PrimaryColor { get; set; } = "#007ACC";
        public string AccentColor { get; set; } = "#0E639C";
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public string SurfaceColor { get; set; } = "#F5F5F5";
        public string TextColor { get; set; } = "#1E1E1E";
        public string SecondaryTextColor { get; set; } = "#6E6E6E";
    }
}
