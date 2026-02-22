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
            ViewChangelogCommand = new RelayCommand(_ => ToggleChangelog(), _ => UpdateAvailable != null);
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
                    RaisePropertyChanged(nameof(ChangelogText));
                    ((AsyncRelayCommand)DownloadUpdateCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ViewReleaseNotesCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)ViewChangelogCommand).RaiseCanExecuteChanged();
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

        /// <summary>
        /// Changelog / release notes body from GitHub (Markdown formatted).
        /// </summary>
        public string ChangelogText => UpdateAvailable?.Body ?? string.Empty;

        /// <summary>
        /// Whether the changelog panel is visible.
        /// </summary>
        private bool _isChangelogVisible;
        public bool IsChangelogVisible
        {
            get => _isChangelogVisible;
            set => SetProperty(ref _isChangelogVisible, value);
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
        public ICommand ViewChangelogCommand { get; }
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
                    IsChangelogVisible = true; // IDEA-013: auto-show changelog
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
                StatusMessage = $"Downloading update ({UpdateAvailable.FileSizeDisplay})...";

                var filePath = await _updateService.DownloadUpdateAsync(UpdateAvailable);
                
                if (!string.IsNullOrEmpty(filePath))
                {
                    DownloadedFilePath = filePath;
                    StatusMessage = "Download complete and verified. Ready to install.";
                }
                else
                {
                    StatusMessage = "Download failed — file may be corrupt or network error. Try again.";
                    DownloadedFilePath = string.Empty;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download error: {ex.Message}";
                DownloadedFilePath = string.Empty;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void InstallUpdate()
        {
            if (string.IsNullOrEmpty(DownloadedFilePath)) return;

            // Verify the file still exists before prompting
            if (!System.IO.File.Exists(DownloadedFilePath))
            {
                StatusMessage = "Downloaded file no longer exists. Please download again.";
                DownloadedFilePath = string.Empty;
                return;
            }

            var result = System.Windows.MessageBox.Show(
                "The application will close to install the update. Continue?",
                "Install Update",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    _updateService.LaunchInstaller(DownloadedFilePath, closeApp: true);
                }
                catch (InvalidOperationException ex)
                {
                    // Validation failed — show user-friendly error
                    System.Windows.MessageBox.Show(
                        $"The installer file appears to be corrupt:\n\n{ex.Message}\n\nPlease try downloading the update again.",
                        "Install Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    StatusMessage = "Installer validation failed. Please re-download.";
                    DownloadedFilePath = string.Empty;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Error launching installer:\n\n{ex.Message}",
                        "Install Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ViewReleaseNotes()
        {
            if (UpdateAvailable != null)
            {
                _updateService.OpenReleasePage(UpdateAvailable);
            }
        }

        private void ToggleChangelog()
        {
            IsChangelogVisible = !IsChangelogVisible;
        }

        private void Dismiss()
        {
            IsVisible = false;
        }

        private void SaveSettings()
        {
            try
            {
                SettingsManager.Current.CheckForUpdatesOnStartup = CheckOnStartup;
                SettingsManager.SaveCurrent();
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
                _checkOnStartup = SettingsManager.Current.CheckForUpdatesOnStartup;
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
