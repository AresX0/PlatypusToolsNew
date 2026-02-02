using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Configuration window for the Windows screensaver settings.
    /// This is shown when the user clicks "Settings..." in Windows Screen Saver Settings.
    /// </summary>
    public partial class ScreensaverConfigWindow : Window
    {
        private ScreensaverSettings _settings;
        
        public ScreensaverConfigWindow()
        {
            InitializeComponent();
            
            // Load current settings
            _settings = ScreensaverSettings.Load();
            
            // Apply to UI
            SelectComboBoxItem(ModeComboBox, _settings.VisualizerMode);
            if (ColorSchemeComboBox.Items.Count > _settings.ColorSchemeIndex)
                ColorSchemeComboBox.SelectedIndex = _settings.ColorSchemeIndex;
        }
        
        private void SelectComboBoxItem(ComboBox comboBox, string value)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && 
                    item.Content?.ToString()?.Equals(value, System.StringComparison.OrdinalIgnoreCase) == true)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }
        
        private string GetSelectedMode()
        {
            if (ModeComboBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                return item.Content.ToString() ?? "Starfield";
            }
            return "Starfield";
        }
        
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            // Launch a preview of the screensaver
            var previewWindow = new ScreensaverWindow(GetSelectedMode(), ColorSchemeComboBox.SelectedIndex);
            previewWindow.Show();
        }
        
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Save settings
            _settings.VisualizerMode = GetSelectedMode();
            _settings.ColorSchemeIndex = ColorSchemeComboBox.SelectedIndex;
            _settings.Save();
            
            MessageBox.Show("Screensaver settings saved!", "PlatypusTools Screensaver", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            
            DialogResult = true;
            Close();
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
