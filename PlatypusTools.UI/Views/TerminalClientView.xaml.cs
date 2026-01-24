using PlatypusTools.Core.Services;
using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class TerminalClientView : UserControl
    {
        public TerminalClientView()
        {
            InitializeComponent();
            
            // Auto-scroll terminal output
            if (DataContext is ViewModels.TerminalClientViewModel vm)
            {
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(vm.OutputText))
                    {
                        OutputScroller.ScrollToEnd();
                    }
                };
                vm.LoadCredentialRequested += OnLoadCredentialRequested;
            }

            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is ViewModels.TerminalClientViewModel oldVm)
                {
                    oldVm.LoadCredentialRequested -= OnLoadCredentialRequested;
                }

                if (e.NewValue is ViewModels.TerminalClientViewModel newVm)
                {
                    newVm.PropertyChanged += (s2, e2) =>
                    {
                        if (e2.PropertyName == nameof(newVm.OutputText))
                        {
                            OutputScroller.ScrollToEnd();
                        }
                    };
                    newVm.LoadCredentialRequested += OnLoadCredentialRequested;
                }
            };
        }

        private void OnLoadCredentialRequested(object? sender, System.EventArgs e)
        {
            // Show the credential picker dialog, defaulting to SSH credentials
            var picker = new CredentialPickerWindow(CredentialType.SSH)
            {
                Owner = Window.GetWindow(this)
            };

            if (picker.ShowDialog() == true && picker.SelectedCredential != null)
            {
                var cred = picker.SelectedCredential;
                if (DataContext is ViewModels.TerminalClientViewModel vm)
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
                }
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.TerminalClientViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        }
    }
}
