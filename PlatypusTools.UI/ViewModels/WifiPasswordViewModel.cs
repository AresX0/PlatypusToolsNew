using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class WifiPasswordViewModel : BindableBase
    {
        private readonly WifiPasswordService _service = new();

        public WifiPasswordViewModel()
        {
            Profiles = new ObservableCollection<WifiProfile>();

            RefreshCommand = new RelayCommand(async _ => await LoadProfilesAsync());
            CopyPasswordCommand = new RelayCommand(_ => CopyPassword(), _ => SelectedProfile?.HasPassword == true);
            ExportCommand = new RelayCommand(async _ => await ExportAsync());
            DeleteProfileCommand = new RelayCommand(async _ => await DeleteProfileAsync(), _ => SelectedProfile != null);
            CopyAllCommand = new RelayCommand(_ => CopyAllPasswords(), _ => Profiles.Count > 0);
            GenerateQrCommand = new RelayCommand(_ => GenerateQr(), _ => SelectedProfile?.HasPassword == true);

            _ = LoadProfilesAsync();
        }

        public ObservableCollection<WifiProfile> Profiles { get; }

        private WifiProfile? _selectedProfile;
        public WifiProfile? SelectedProfile
        {
            get => _selectedProfile;
            set => SetProperty(ref _selectedProfile, value);
        }

        private WifiConnectionInfo? _currentConnection;
        public WifiConnectionInfo? CurrentConnection
        {
            get => _currentConnection;
            set
            {
                if (SetProperty(ref _currentConnection, value))
                    RaisePropertyChanged(nameof(CurrentConnectionDisplay));
            }
        }

        public string CurrentConnectionDisplay => CurrentConnection != null
            ? $"Connected to: {CurrentConnection.Ssid} | Signal: {CurrentConnection.Signal} | {CurrentConnection.RadioType} | {CurrentConnection.Authentication}"
            : "Not connected to WiFi";

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private bool _showPasswords;
        public bool ShowPasswords { get => _showPasswords; set => SetProperty(ref _showPasswords, value); }

        private string _searchQuery = "";
        public string SearchQuery { get => _searchQuery; set => SetProperty(ref _searchQuery, value); }

        public ICommand RefreshCommand { get; }
        public ICommand CopyPasswordCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand CopyAllCommand { get; }
        public ICommand GenerateQrCommand { get; }

        private async Task LoadProfilesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading WiFi profiles...";

                var profiles = await _service.GetSavedProfilesAsync();
                var connection = await _service.GetCurrentConnectionAsync();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Profiles.Clear();
                    foreach (var p in profiles)
                        Profiles.Add(p);
                    CurrentConnection = connection;
                });

                StatusMessage = $"Found {profiles.Count} saved WiFi profile(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void CopyPassword()
        {
            if (SelectedProfile == null || !SelectedProfile.HasPassword) return;
            try
            {
                System.Windows.Clipboard.SetText(SelectedProfile.Password);
                StatusMessage = $"Password for '{SelectedProfile.ProfileName}' copied to clipboard.";
            }
            catch { }
        }

        private void CopyAllPasswords()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("SSID\tPassword\tAuthentication");
                foreach (var p in Profiles)
                {
                    sb.AppendLine($"{p.ProfileName}\t{p.PasswordDisplay}\t{p.Authentication}");
                }
                System.Windows.Clipboard.SetText(sb.ToString());
                StatusMessage = "All profiles copied to clipboard.";
            }
            catch { }
        }

        private async Task ExportAsync()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Export Folder"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _service.ExportProfilesAsync(dialog.FolderName);
                    StatusMessage = $"Profiles exported to {dialog.FolderName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }
        }

        private async Task DeleteProfileAsync()
        {
            if (SelectedProfile == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Delete WiFi profile '{SelectedProfile.ProfileName}'?\nThis will forget this network.",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    var success = await _service.DeleteProfileAsync(SelectedProfile.ProfileName);
                    StatusMessage = success
                        ? $"Profile '{SelectedProfile.ProfileName}' deleted."
                        : "Failed to delete profile.";
                    await LoadProfilesAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }
        }

        private void GenerateQr()
        {
            if (SelectedProfile == null) return;
            StatusMessage = $"QR generation for '{SelectedProfile.ProfileName}' - Use the QR Code Generator tab for full QR features.";
        }
    }
}
