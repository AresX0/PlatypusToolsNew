using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PlatypusTools.UI.Services.Vault;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Code-behind for SecurityVaultView.
    /// Handles PasswordBox binding (WPF PasswordBox doesn't support data binding for security)
    /// and vault exists/create panel visibility toggle.
    /// </summary>
    public partial class SecurityVaultView : UserControl
    {
        public SecurityVaultView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is SecurityVaultViewModel vm)
            {
                // Show create panel if vault doesn't exist
                UpdateCreatePanelVisibility(vm);
                UpdateMfaPanelVisibility(vm);
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.VaultExists) || args.PropertyName == nameof(vm.IsUnlocked))
                        UpdateCreatePanelVisibility(vm);
                    if (args.PropertyName == nameof(vm.MfaRequired))
                        UpdateMfaPanelVisibility(vm);
                };
            }
        }

        private void UpdateCreatePanelVisibility(SecurityVaultViewModel vm)
        {
            if (CreatePanel != null)
                CreatePanel.Visibility = !vm.VaultExists && !vm.IsUnlocked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateMfaPanelVisibility(SecurityVaultViewModel vm)
        {
            // Hide the password unlock panel while MFA prompt is shown
            if (UnlockPanel != null)
                UnlockPanel.Visibility = vm.MfaRequired ? Visibility.Collapsed : Visibility.Visible;
        }

        // PasswordBox change handlers (PasswordBox doesn't support binding)
        private void MasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SecurityVaultViewModel vm && sender is PasswordBox pb)
                vm.MasterPasswordInput = pb.Password;
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SecurityVaultViewModel vm && sender is PasswordBox pb)
                vm.MasterPasswordInput = pb.Password;
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SecurityVaultViewModel vm && sender is PasswordBox pb)
                vm.ConfirmPasswordInput = pb.Password;
        }

        private void ChangePwBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SecurityVaultViewModel vm && sender is PasswordBox pb)
                vm.MasterPasswordInput = pb.Password;
        }

        private void ConfirmChangePwBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SecurityVaultViewModel vm && sender is PasswordBox pb)
                vm.ConfirmPasswordInput = pb.Password;
        }
    }

    /// <summary>
    /// Converts a TOTP secret string to a live TOTP code for DataGrid display.
    /// </summary>
    public class TotpCodeConverter : IValueConverter
    {
        public static readonly TotpCodeConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string secret && !string.IsNullOrWhiteSpace(secret))
            {
                return TotpService.GenerateCode(secret);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverts a boolean value (used for RadioButton binding).
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public static readonly InverseBoolConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return value;
        }
    }

    /// <summary>
    /// Converts WindowsHelloVerified bool to button text.
    /// </summary>
    public class WindowsHelloButtonConverter : IValueConverter
    {
        public static readonly WindowsHelloButtonConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true) return "✅ Windows Hello Verified";
            return "🔑 Verify with Windows Hello";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts false to Visible and true to Collapsed (inverse of BooleanToVisibilityConverter).
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBoolToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
