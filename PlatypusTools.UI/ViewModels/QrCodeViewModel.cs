using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class QrCodeViewModel : BindableBase
    {
        private readonly QrCodeService _service = new();

        public QrCodeViewModel()
        {
            GenerateCommand = new RelayCommand(_ => Generate(), _ => !string.IsNullOrWhiteSpace(InputText));
            CopyCommand = new RelayCommand(_ => CopyToClipboard(), _ => QrImageSource != null);
            SaveCommand = new RelayCommand(_ => SaveToFile(), _ => _lastQrBytes != null);
            GenerateWifiCommand = new RelayCommand(_ => GenerateWifi(), _ => !string.IsNullOrWhiteSpace(WifiSsid));
            GenerateVCardCommand = new RelayCommand(_ => GenerateVCard(), _ => !string.IsNullOrWhiteSpace(VCardFirstName));
        }

        // Tab selection
        private int _selectedTabIndex;
        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

        // Text/URL mode
        private string _inputText = "";
        public string InputText { get => _inputText; set => SetProperty(ref _inputText, value); }

        // WiFi mode
        private string _wifiSsid = "";
        public string WifiSsid { get => _wifiSsid; set => SetProperty(ref _wifiSsid, value); }

        private string _wifiPassword = "";
        public string WifiPassword { get => _wifiPassword; set => SetProperty(ref _wifiPassword, value); }

        private string _wifiAuthType = "WPA";
        public string WifiAuthType { get => _wifiAuthType; set => SetProperty(ref _wifiAuthType, value); }

        private bool _wifiHidden;
        public bool WifiHidden { get => _wifiHidden; set => SetProperty(ref _wifiHidden, value); }

        // vCard mode
        private string _vCardFirstName = "";
        public string VCardFirstName { get => _vCardFirstName; set => SetProperty(ref _vCardFirstName, value); }

        private string _vCardLastName = "";
        public string VCardLastName { get => _vCardLastName; set => SetProperty(ref _vCardLastName, value); }

        private string _vCardPhone = "";
        public string VCardPhone { get => _vCardPhone; set => SetProperty(ref _vCardPhone, value); }

        private string _vCardEmail = "";
        public string VCardEmail { get => _vCardEmail; set => SetProperty(ref _vCardEmail, value); }

        private string _vCardOrganization = "";
        public string VCardOrganization { get => _vCardOrganization; set => SetProperty(ref _vCardOrganization, value); }

        // Settings
        private int _pixelsPerModule = 20;
        public int PixelsPerModule { get => _pixelsPerModule; set => SetProperty(ref _pixelsPerModule, value); }

        private string _darkColor = "#000000";
        public string DarkColor { get => _darkColor; set => SetProperty(ref _darkColor, value); }

        private string _lightColor = "#FFFFFF";
        public string LightColor { get => _lightColor; set => SetProperty(ref _lightColor, value); }

        // Output
        private ImageSource? _qrImageSource;
        public ImageSource? QrImageSource { get => _qrImageSource; set => SetProperty(ref _qrImageSource, value); }

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private byte[]? _lastQrBytes;

        // Commands
        public ICommand GenerateCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand GenerateWifiCommand { get; }
        public ICommand GenerateVCardCommand { get; }

        private void Generate()
        {
            try
            {
                _lastQrBytes = _service.GenerateQrCode(InputText, PixelsPerModule, DarkColor, LightColor);
                QrImageSource = BytesToImageSource(_lastQrBytes);
                StatusMessage = $"QR code generated ({_lastQrBytes.Length:N0} bytes)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void GenerateWifi()
        {
            try
            {
                _lastQrBytes = _service.GenerateWifiQrCode(WifiSsid, WifiPassword, WifiAuthType, WifiHidden, PixelsPerModule);
                QrImageSource = BytesToImageSource(_lastQrBytes);
                StatusMessage = $"WiFi QR code generated for '{WifiSsid}'";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void GenerateVCard()
        {
            try
            {
                _lastQrBytes = _service.GenerateVCardQrCode(VCardFirstName, VCardLastName,
                    VCardPhone, VCardEmail, VCardOrganization, pixelsPerModule: PixelsPerModule);
                QrImageSource = BytesToImageSource(_lastQrBytes);
                StatusMessage = $"vCard QR code generated for '{VCardFirstName} {VCardLastName}'";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void CopyToClipboard()
        {
            if (QrImageSource is BitmapSource bitmap)
            {
                try
                {
                    Clipboard.SetImage(bitmap);
                    StatusMessage = "QR code copied to clipboard.";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }
        }

        private void SaveToFile()
        {
            if (_lastQrBytes == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|All Files|*.*",
                DefaultExt = ".png",
                FileName = "qrcode.png"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _service.SaveToFile(_lastQrBytes, dialog.FileName);
                    StatusMessage = $"Saved to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }
        }

        private static ImageSource BytesToImageSource(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
