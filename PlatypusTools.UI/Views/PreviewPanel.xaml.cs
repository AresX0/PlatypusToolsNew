using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class PreviewPanel : System.Windows.Controls.UserControl
    {
        public PreviewPanel()
        {
            InitializeComponent();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PlatypusTools.UI.ViewModels.DuplicatesViewModel vm)
            {
                var path = vm.LastPreviewedFilePath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var dlg = new PreviewWindow(path) { Owner = Application.Current?.MainWindow };
                    dlg.ShowDialog();
                }
            }
        }
    }
}