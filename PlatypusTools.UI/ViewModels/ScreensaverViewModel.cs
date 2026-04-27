using System;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services.Wallpaper;

namespace PlatypusTools.UI.ViewModels
{
    public class ScreensaverViewModel : BindableBase
    {
        private readonly WallpaperRotatorConfig _config;

        public ScreensaverViewModel()
        {
            _config = WallpaperRotatorConfig.Load();

            PreviewCommand = new RelayCommand(_ => PreviewSlideshow());
            InstallScrCommand = new RelayCommand(_ => InstallScr());
            UninstallScrCommand = new RelayCommand(_ => UninstallScr());
            ApplyTimeoutCommand = new RelayCommand(_ => ApplyTimeout());
            SaveSettingsCommand = new RelayCommand(_ => Save());
            RefreshNasaCommand = new RelayCommand(async _ => await RefreshNasaAsync());

            RefreshInstallStatus();

            // Pull current Windows idle timeout
            var t = ScreensaverInstaller.GetIdleTimeoutSeconds();
            if (t.HasValue) _idleTimeoutMinutes = Math.Max(1, t.Value / 60);
        }

        // ── Slideshow settings (shared with rotator config so user has one source folder) ──

        public string ImagesDirectory
        {
            get => _config.ImagesDirectory;
            set { if (_config.ImagesDirectory != value) { _config.ImagesDirectory = value ?? ""; RaisePropertyChanged(); } }
        }

        public int SlideshowIntervalSeconds
        {
            get => _config.SlideshowIntervalSeconds;
            set { if (_config.SlideshowIntervalSeconds != value) { _config.SlideshowIntervalSeconds = Math.Max(2, value); RaisePropertyChanged(); } }
        }

        public bool Shuffle
        {
            get => _config.Shuffle;
            set { if (_config.Shuffle != value) { _config.Shuffle = value; RaisePropertyChanged(); } }
        }

        public string FitMode
        {
            get => _config.FitMode;
            set { if (_config.FitMode != value) { _config.FitMode = value ?? "fit"; RaisePropertyChanged(); } }
        }

        public string Transition
        {
            get => _config.Transition;
            set { if (_config.Transition != value) { _config.Transition = value ?? "fade"; RaisePropertyChanged(); } }
        }

        public bool ShowOverlay
        {
            get => _config.ShowOverlay;
            set { if (_config.ShowOverlay != value) { _config.ShowOverlay = value; RaisePropertyChanged(); } }
        }

        public int OverlayScrollSpeedSeconds
        {
            get => _config.OverlayScrollSpeedSeconds;
            set { if (_config.OverlayScrollSpeedSeconds != value) { _config.OverlayScrollSpeedSeconds = Math.Max(5, value); RaisePropertyChanged(); } }
        }

        public string OverlaySource
        {
            get => _config.OverlaySource;
            set { if (_config.OverlaySource != value) { _config.OverlaySource = value ?? "nasa"; RaisePropertyChanged(); } }
        }

        public string CustomOverlayText
        {
            get => _config.CustomOverlayText;
            set { if (_config.CustomOverlayText != value) { _config.CustomOverlayText = value ?? ""; RaisePropertyChanged(); } }
        }

        // ── Idle timeout & install state ──

        private int _idleTimeoutMinutes = 10;
        public int IdleTimeoutMinutes
        {
            get => _idleTimeoutMinutes;
            set => SetProperty(ref _idleTimeoutMinutes, Math.Clamp(value, 1, 120));
        }

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (SetProperty(ref _isInstalled, value))
                    RaisePropertyChanged(nameof(InstallStatusText));
            }
        }

        public string InstallStatusText => IsInstalled
            ? "Installed as Windows screensaver."
            : "Not installed as a Windows screensaver.";

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand PreviewCommand { get; }
        public ICommand InstallScrCommand { get; }
        public ICommand UninstallScrCommand { get; }
        public ICommand ApplyTimeoutCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand RefreshNasaCommand { get; }

        private void RefreshInstallStatus()
        {
            IsInstalled = ScreensaverInstaller.IsInstalled();
        }

        private void Save()
        {
            try
            {
                _config.Save();
                StatusMessage = "Settings saved.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
            }
        }

        private void PreviewSlideshow()
        {
            try
            {
                Save();
                var win = new PlatypusTools.UI.Views.SlideshowScreensaverWindow(_config);
                win.Show();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Preview failed: {ex.Message}";
            }
        }

        private void InstallScr()
        {
            if (ScreensaverInstaller.Install(out var msg))
            {
                StatusMessage = msg;
                RefreshInstallStatus();
            }
            else
            {
                StatusMessage = $"Install failed: {msg}";
            }
        }

        private void UninstallScr()
        {
            if (ScreensaverInstaller.Uninstall(out var msg))
            {
                StatusMessage = msg;
                RefreshInstallStatus();
            }
            else
            {
                StatusMessage = $"Uninstall failed: {msg}";
            }
        }

        private void ApplyTimeout()
        {
            if (ScreensaverInstaller.SetIdleTimeoutSeconds(IdleTimeoutMinutes * 60))
                StatusMessage = $"Idle timeout set to {IdleTimeoutMinutes} minute(s).";
            else
                StatusMessage = "Failed to set idle timeout.";
        }

        private async Task RefreshNasaAsync()
        {
            try
            {
                StatusMessage = "Refreshing NASA data…";
                var svc = new NasaInfoService();
                var snap = await svc.GetAsync(forceRefresh: true).ConfigureAwait(true);
                StatusMessage = $"NASA refreshed: {snap.Missions.Count} missions, {snap.Launches.Count} launches.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"NASA refresh failed: {ex.Message}";
            }
        }
    }
}
