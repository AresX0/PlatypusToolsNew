using PlatypusTools.Core.Services;
using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class FtpClientView : UserControl
    {
        public FtpClientView()
        {
            InitializeComponent();

            if (DataContext is ViewModels.FtpClientViewModel vm)
            {
                vm.LoadCredentialRequested += OnLoadCredentialRequested;
            }

            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is ViewModels.FtpClientViewModel oldVm)
                {
                    oldVm.LoadCredentialRequested -= OnLoadCredentialRequested;
                }

                if (e.NewValue is ViewModels.FtpClientViewModel newVm)
                {
                    newVm.LoadCredentialRequested += OnLoadCredentialRequested;
                }
            };
        }

        private void OnLoadCredentialRequested(object? sender, System.EventArgs e)
        {
            // Show the credential picker dialog, defaulting to FTP/SFTP credentials
            var picker = new CredentialPickerWindow(CredentialType.FTP)
            {
                Owner = Window.GetWindow(this)
            };

            if (picker.ShowDialog() == true && picker.SelectedCredential != null)
            {
                var cred = picker.SelectedCredential;
                if (DataContext is ViewModels.FtpClientViewModel vm)
                {
                    // Parse key for possible host info (format: "host" or "user@host")
                    string? hostFromKey = null;
                    if (cred.Key.Contains('@'))
                    {
                        var parts = cred.Key.Split('@');
                        if (parts.Length == 2)
                        {
                            hostFromKey = parts[1];
                        }
                    }
                    else if (!cred.Key.Contains(' '))
                    {
                        // Key might be a hostname
                        hostFromKey = cred.Key;
                    }

                    vm.ApplyCredential(cred.Username, picker.DecryptedPassword ?? string.Empty, hostFromKey);

                    // Update the PasswordBox (since it's not bound directly)
                    PasswordBox.Password = picker.DecryptedPassword ?? string.Empty;

                    // Auto-select SFTP if credential type is SFTP
                    if (cred.Type == CredentialType.SFTP)
                    {
                        vm.UseSftp = true;
                    }
                }
            }
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
