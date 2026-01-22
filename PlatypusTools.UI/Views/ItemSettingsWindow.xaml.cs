using System.Windows;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Dialog for editing per-item settings overrides in batch upscale.
    /// </summary>
    public partial class ItemSettingsWindow : Window
    {
        public ItemSettingsWindow()
        {
            InitializeComponent();
        }
        
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
