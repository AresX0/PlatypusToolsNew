using System;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class MultimediaEditorView : UserControl
    {
        public MultimediaEditorView()
        {
            InitializeComponent();
        }

        private void VlcHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                viewModel.VlcHostHandle = host.Handle;
            }
        }

        private void AudacityHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                viewModel.AudacityHostHandle = host.Handle;
            }
        }

        private void GimpHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                viewModel.GimpHostHandle = host.Handle;
            }
        }
    }
}
