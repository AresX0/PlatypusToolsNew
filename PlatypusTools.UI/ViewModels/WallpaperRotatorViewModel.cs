using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using PlatypusTools.Core.Services.Wallpaper;

namespace PlatypusTools.UI.ViewModels
{
    public class WallpaperRotatorViewModel : BindableBase
    {
        private readonly WallpaperRotatorConfig _config;
        private readonly WallpaperRotatorService _service = WallpaperRotatorService.Instance;

        public WallpaperRotatorViewModel()
        {
            _config = WallpaperRotatorConfig.Load();

            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            StartCommand = new RelayCommand(_ => Start(), _ => !IsRunning && !string.IsNullOrWhiteSpace(ImagesDirectory));
            StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning);
            ApplyNowCommand = new RelayCommand(async _ => await ApplyNowAsync(), _ => !string.IsNullOrWhiteSpace(ImagesDirectory));
            SaveSettingsCommand = new RelayCommand(_ => Save());
            RefreshNasaCommand = new RelayCommand(async _ => await RefreshNasaAsync());

            _service.StateChanged += OnStateChanged;
            UpdateFromService();
        }

        private void OnStateChanged(object? sender, RotatorStateChangedEventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                IsRunning = e.IsRunning;
                CurrentImagePath = e.CurrentImagePath ?? "";
                StatusMessage = e.Status;
                NextChangeText = e.NextChangeAt.HasValue
                    ? $"Next change: {e.NextChangeAt.Value:HH:mm:ss}"
                    : "";
            });
        }

        private void UpdateFromService()
        {
            IsRunning = _service.IsRunning;
            CurrentImagePath = _service.CurrentImagePath ?? "";
            StatusMessage = _service.LastStatus;
        }

        public string ImagesDirectory
        {
            get => _config.ImagesDirectory;
            set { if (_config.ImagesDirectory != value) { _config.ImagesDirectory = value ?? ""; RaisePropertyChanged(); } }
        }

        public int WallpaperIntervalSeconds
        {
            get => _config.WallpaperIntervalSeconds;
            set { if (_config.WallpaperIntervalSeconds != value) { _config.WallpaperIntervalSeconds = Math.Max(30, value); RaisePropertyChanged(); } }
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

        public bool BurnOverlayOnWallpaper
        {
            get => _config.BurnOverlayOnWallpaper;
            set { if (_config.BurnOverlayOnWallpaper != value) { _config.BurnOverlayOnWallpaper = value; RaisePropertyChanged(); } }
        }

        public bool ApplyToLockScreen
        {
            get => _config.ApplyToLockScreen;
            set { if (_config.ApplyToLockScreen != value) { _config.ApplyToLockScreen = value; RaisePropertyChanged(); } }
        }

        public double OverlayOpacity
        {
            get => _config.OverlayOpacity;
            set { if (Math.Abs(_config.OverlayOpacity - value) > 0.001) { _config.OverlayOpacity = Math.Clamp(value, 0.1, 1.0); RaisePropertyChanged(); } }
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

        public bool RunAtLogin
        {
            get => _config.RunAtLogin;
            set
            {
                if (_config.RunAtLogin != value)
                {
                    _config.RunAtLogin = value;
                    RaisePropertyChanged();
                    AutostartHelper.SetWallpaperDaemonAutostart(value);
                }
            }
        }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

        private string _currentImagePath = "";
        public string CurrentImagePath
        {
            get => _currentImagePath;
            set
            {
                if (SetProperty(ref _currentImagePath, value))
                    RaisePropertyChanged(nameof(CurrentImageDisplay));
            }
        }

        public string CurrentImageDisplay => string.IsNullOrEmpty(CurrentImagePath)
            ? "(none yet)"
            : Path.GetFileName(CurrentImagePath);

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _nextChangeText = "";
        public string NextChangeText { get => _nextChangeText; set => SetProperty(ref _nextChangeText, value); }

        public ICommand BrowseFolderCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ApplyNowCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand RefreshNasaCommand { get; }

        private void BrowseFolder()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select wallpaper image folder",
                ShowNewFolderButton = false,
            };
            if (!string.IsNullOrEmpty(ImagesDirectory) && Directory.Exists(ImagesDirectory))
                dlg.SelectedPath = ImagesDirectory;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ImagesDirectory = dlg.SelectedPath;
                Save();
            }
        }

        private void Start()
        {
            Save();
            _service.Start(_config);
        }

        private void Stop()
        {
            _service.Stop();
            _config.RotatorRunning = false;
            Save();
        }

        private async Task ApplyNowAsync()
        {
            Save();
            await _service.ApplyNextNowAsync(_config);
        }

        private void Save()
        {
            try
            {
                _config.RotatorRunning = IsRunning;
                _config.Save();
                StatusMessage = "Settings saved.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save error: {ex.Message}";
            }
        }

        private async Task RefreshNasaAsync()
        {
            try
            {
                StatusMessage = "Refreshing NASA data…";
                var svc = new PlatypusTools.Core.Services.Wallpaper.NasaInfoService();
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
