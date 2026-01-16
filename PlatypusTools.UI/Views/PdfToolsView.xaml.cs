using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for PdfToolsView.xaml
    /// </summary>
    public partial class PdfToolsView : UserControl
    {
        public PdfToolsView()
        {
            InitializeComponent();
        }
        
        private void RotationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ViewModels.PdfToolsViewModel vm && sender is ComboBox combo)
            {
                vm.RotationDegrees = combo.SelectedIndex switch
                {
                    0 => 90,
                    1 => 180,
                    2 => 270,
                    _ => 90
                };
            }
        }
    }
}
