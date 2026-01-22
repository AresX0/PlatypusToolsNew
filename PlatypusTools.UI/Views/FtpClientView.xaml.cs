using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class FtpClientView : UserControl
    {
        public FtpClientView()
        {
            InitializeComponent();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FtpClientViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        }
    }
}
