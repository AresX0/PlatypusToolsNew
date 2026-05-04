using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class PlatytalkView : UserControl
    {
        private PlatytalkLockManager? _lock;

        public PlatytalkView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            // Hook activity events at preview level so they fire even when child controls handle them.
            PreviewMouseMove += (_, _) => _lock?.ResetIdle();
            PreviewKeyDown += (_, _) => _lock?.ResetIdle();
            PreviewMouseDown += (_, _) => _lock?.ResetIdle();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_lock != null) _lock.PropertyChanged -= OnLockPropertyChanged;
            _lock = (DataContext as PlatytalkViewModel)?.Lock;
            if (_lock != null)
            {
                _lock.PropertyChanged += OnLockPropertyChanged;
                _lock.ResetIdle();
            }
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

        // ----- PIN-lock plumbing -----
        private void LockPinBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_lock != null && sender is PasswordBox pb)
                _lock.CurrentPin = pb.Password;
        }

        private void LockPinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _lock?.SubmitPinCommand.CanExecute(null) == true)
            {
                _lock.SubmitPinCommand.Execute(null);
                ClearLockBox();
                e.Handled = true;
            }
        }

        private void LockSubmit_Click(object sender, RoutedEventArgs e)
        {
            // The bound Command also fires; we just need to clear the PasswordBox display.
            ClearLockBox();
        }

        private void ClearLockBox()
        {
            if (LockPinBox != null) LockPinBox.Password = string.Empty;
        }

        private void OnLockPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlatytalkLockManager.CurrentPin))
            {
                // When the VM clears CurrentPin (post-submit), mirror to the PasswordBox.
                if (_lock != null && string.IsNullOrEmpty(_lock.CurrentPin) && LockPinBox != null && LockPinBox.Password.Length > 0)
                    LockPinBox.Password = string.Empty;
            }
            else if (e.PropertyName == nameof(PlatytalkLockManager.Stage))
            {
                ClearLockBox();
                LockPinBox?.Focus();
            }
            else if (e.PropertyName == nameof(PlatytalkLockManager.IsLocked))
            {
                if (_lock?.IsLocked == true) LockPinBox?.Focus();
            }
        }
    }
}
