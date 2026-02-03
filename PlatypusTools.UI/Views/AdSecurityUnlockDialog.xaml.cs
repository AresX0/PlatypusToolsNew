using System.Windows;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Dialog for activating the AD Security Analyzer tab with a license key.
    /// </summary>
    public partial class AdSecurityUnlockDialog : Window
    {
        public bool WasUnlocked { get; private set; }
        private bool _isFormatting = false;

        public AdSecurityUnlockDialog()
        {
            InitializeComponent();
            
            // Check if already licensed
            var settings = SettingsManager.Current;
            if (settings.HasValidLicenseKey())
            {
                // Already licensed - show confirmation
                LicensedPanel.Visibility = Visibility.Visible;
                LicenseKeyDisplay.Text = MaskLicenseKey(settings.AdSecurityLicenseKey);
                LicenseKeyBox.Text = settings.AdSecurityLicenseKey;
                LicenseKeyBox.IsEnabled = false;
                ActivateButton.Content = "🔓 Open";
            }
            
            Loaded += (s, e) => LicenseKeyBox.Focus();
        }

        private void LicenseKeyBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryActivate();
            }
        }

        private void LicenseKeyBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isFormatting) return;
            
            _isFormatting = true;
            
            // Auto-format with dashes
            var text = LicenseKeyBox.Text.Replace("-", "").Replace(" ", "").ToUpperInvariant();
            if (text.Length > 16) text = text.Substring(0, 16);
            
            var formatted = "";
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && i % 4 == 0) formatted += "-";
                formatted += text[i];
            }
            
            var caretIndex = LicenseKeyBox.CaretIndex;
            LicenseKeyBox.Text = formatted;
            LicenseKeyBox.CaretIndex = formatted.Length;
            
            _isFormatting = false;
            
            // Hide error when typing
            ErrorMessage.Visibility = Visibility.Collapsed;
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            TryActivate();
        }

        private void TryActivate()
        {
            var settings = SettingsManager.Current;
            
            // If already licensed, just unlock
            if (settings.HasValidLicenseKey())
            {
                settings.IsAdSecurityUnlocked = true;
                WasUnlocked = true;
                TabVisibilityService.Instance.RefreshFromSettings();
                DialogResult = true;
                Close();
                return;
            }
            
            // Try to validate the entered key
            var licenseKey = LicenseKeyBox.Text;
            
            if (settings.ValidateLicenseKey(licenseKey))
            {
                // Valid key - save and unlock
                SettingsManager.SaveCurrent();
                WasUnlocked = true;
                TabVisibilityService.Instance.RefreshFromSettings();
                DialogResult = true;
                Close();
            }
            else
            {
                // Invalid key
                ErrorMessage.Text = "Invalid license key. Please check and try again.";
                ErrorMessage.Visibility = Visibility.Visible;
                LicenseKeyBox.SelectAll();
                LicenseKeyBox.Focus();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Masks a license key for display (shows only last 4 chars).
        /// </summary>
        private static string MaskLicenseKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 4)
                return key;
            return "XXXX-XXXX-XXXX-" + key.Substring(key.Length - 4);
        }
    }
}
