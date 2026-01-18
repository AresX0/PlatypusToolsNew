using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace PlatypusTools.UI
{
    /// <summary>
    /// Interaction logic for PrerequisitesWindow.xaml
    /// </summary>
    public partial class PrerequisitesWindow : Window
    {
        public PrerequisitesWindow()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch { }
            e.Handled = true;
        }
    }
}
