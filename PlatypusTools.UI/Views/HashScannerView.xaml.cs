using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for HashScannerView.xaml
    /// File hash calculator (MD5, SHA1, SHA256, SHA512, CRC32)
    /// </summary>
    public partial class HashScannerView : UserControl
    {
        public HashScannerView()
        {
            InitializeComponent();
            
            // Set DataContext if not already bound
            if (DataContext == null)
            {
                DataContext = new HashScannerViewModel();
            }
        }

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is HashScannerViewModel vm)
                {
                    vm.HandleFileDrop(files);
                }
            }
        }

        private void UserControl_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
    }
}
