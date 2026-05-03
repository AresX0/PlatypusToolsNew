using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class PlatytalkView : UserControl
    {
        public PlatytalkView()
        {
            InitializeComponent();
        }

        // PasswordBox.Password is intentionally not a DependencyProperty for security;
        // forward changes to the VM via a code-behind hook instead of two-way binding.
        private void BackupPassphraseBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is PlatytalkViewModel vm && sender is PasswordBox pb)
            {
                vm.BackupPassphrase = pb.Password;
            }
        }
    }
}
