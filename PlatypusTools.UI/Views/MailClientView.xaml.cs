using System.Windows;
using System.Windows.Controls;
using PlatypusTools.Core.Models.Mail;

namespace PlatypusTools.UI.Views
{
    public partial class MailClientView : UserControl
    {
        public MailClientView()
        {
            InitializeComponent();
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is MailFolder folder && DataContext is ViewModels.MailClientViewModel vm)
            {
                if (vm.SelectFolderCommand.CanExecute(folder))
                    vm.SelectFolderCommand.Execute(folder);
            }
        }

        /// <summary>
        /// Capture password from PasswordBox (can't bind PasswordBox.Password in WPF).
        /// </summary>
        private void SaveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MailClientViewModel vm && FindName("PasswordBox") is System.Windows.Controls.PasswordBox pb)
            {
                vm.EditPassword = pb.Password;
            }
        }

        private void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MailClientViewModel vm && FindName("PasswordBox") is System.Windows.Controls.PasswordBox pb)
            {
                vm.EditPassword = pb.Password;
            }
        }
    }
}
