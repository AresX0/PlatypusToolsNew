using System;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the update notification and download UI.
    /// </summary>
    public class UpdateViewModel : BindableBase
    {
        private readonly UpdateService _updateService;

        public UpdateViewModel()
        {
            _updateService = UpdateService.Instance;
            _updateService.DownloadProgress += (s, p) => DownloadProgress = p;

            CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
            DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync, () => UpdateAvailable != null && !IsDownloading);
            InstallUpdateCommand = new RelayCommand(_ => InstallUpdate(), _ => !string.IsNullOrEmpty(DownloadedFilePath));
            ViewReleaseNotesCommand = new RelayCommand(_ => ViewReleaseNotes(), _ => UpdateAvailable != null);
            DismissCommand = new RelayCommand(_ => Dismiss());
        }

        #region Properties

        public string CurrentVersion => _updateService.CurrentVersion;

        private UpdateInfo? _updateAvailable;
        public UpdateInfo? UpdateAvailable
        {
            get => _updateAvailable;
            set
            {
                if (SetProperty(ref _updateAvailable, value))
                {
                    RaisePropertyChanged(nameof(HasUpdate));
                    RaisePropertyChanged(nameof(UpdateMessage));
                    ((AsyncRelayCommand)DownloadUpdateCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ViewReleaseNotesCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasUpdate => UpdateAvailable != null;

        public string UpdateMessage => UpdateAvailable != null
            ? $"Version {UpdateAvailable.Version} is available!"
            : "No updates available.";

        private bool _isChecking;
        public bool IsChecking
        {
            get => _isChecking;
            set => SetProperty(ref _isChecking, value);
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (SetProperty(ref _isDownloading, value))
                {
                    ((AsyncRelayCommand)DownloadUpdateCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        private string _downloadedFilePath = string.Empty;
        public string DownloadedFilePath
        {
            get => _downloadedFilePath;
            set
            {
                if (SetProperty(ref _downloadedFilePath, value))
                {
                    ((RelayCommand)InstallUpdateCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        private bool _checkOnStartup = true;
        public bool CheckOnStartup
        {
            get => _checkOnStartup;
            set
            {
                if (SetProperty(ref _checkOnStartup, value))
                {
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Commands

        public ICommand CheckForUpdatesCommand { get; }
        public ICommand DownloadUpdateCommand { get; }
        public ICommand InstallUpdateCommand { get; }
        public ICommand ViewReleaseNotesCommand { get; }
        public ICommand DismissCommand { get; }

        #endregion

        #region Methods

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                IsChecking = true;
                StatusMessage = "Checking for updates...";

                var update = await _updateService.CheckForUpdatesAsync();
                
                if (update != null)
                {
                    UpdateAvailable = update;
                    StatusMessage = $"Update {update.Version} available!";
                    IsVisible = true;
                }
                else
                {
                    StatusMessage = "You're running the latest version.";
                    UpdateAvailable = null;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error checking for updates: {ex.Message}";
            }
            finally
            {
                IsChecking = false;
            }
        }

        private async Task DownloadUpdateAsync()
        {
            if (UpdateAvailable == null) return;

            try
            {
                IsDownloading = true;
                DownloadProgress = 0;
                StatusMessage = "Downloading update...";

                var filePath = await _updateService.DownloadUpdateAsync(UpdateAvailable);
                
                if (!string.IsNullOrEmpty(filePath))
                {
                    DownloadedFilePath = filePath;
                    StatusMessage = "Download complete. Ready to install.";
                }
                else
                {
                    StatusMessage = "Download failed.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error downloading update: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void InstallUpdate()
        {
            if (string.IsNullOrEmpty(DownloadedFilePath)) return;

            var result = System.Windows.MessageBox.Show(
                "The application will close to install the update. Continue?",
                "Install Update",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _updateService.LaunchInstaller(DownloadedFilePath, closeApp: true);
            }
        }

        private void ViewReleaseNotes()
        {
            if (UpdateAvailable != null)
            {
                _updateService.OpenReleasePage(UpdateAvailable);
            }
        }

        private void Dismiss()
        {
            IsVisible = false;
        }

        private void SaveSettings()
        {
            try
            {
                var settings = SettingsManager.Load();
                settings.CheckForUpdatesOnStartup = CheckOnStartup;
                SettingsManager.Save(settings);
            }
            catch
            {
                // Ignore settings save errors
            }
        }

        public void LoadSettings()
        {
            try
            {
                var settings = SettingsManager.Load();
                _checkOnStartup = settings.CheckForUpdatesOnStartup;
                RaisePropertyChanged(nameof(CheckOnStartup));
            }
            catch
            {
                // Use defaults
            }
        }

        #endregion
    }
}
