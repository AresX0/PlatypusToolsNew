using System.Diagnostics;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class CredentialManagerWindow : Window
    {
        public CredentialManagerWindow()
        {
            InitializeComponent();
        }
        
        private void OpenWindowsCredentialManager(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("control", "/name Microsoft.CredentialManager") 
                { 
                    UseShellExecute = true 
                });
            }
            catch
            {
                MessageBox.Show("Could not open Windows Credential Manager.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}