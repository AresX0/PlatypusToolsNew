using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class MultimediaEditorViewModel : BindableBase
    {
        // Win32 API imports for embedding windows
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private Process? _embeddedVlcProcess;
        private Process? _embeddedAudacityProcess;
        private Process? _embeddedGimpProcess;
        private string _filePath = string.Empty;
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); UpdateApplicationAvailability(); }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // Application paths
        private string _vlcPath = string.Empty;
        public string VlcPath
        {
            get => _vlcPath;
            set { _vlcPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsVlcAvailable)); }
        }

        private string _audacityPath = string.Empty;
        public string AudacityPath
        {
            get => _audacityPath;
            set { _audacityPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAudacityAvailable)); }
        }

        private string _gimpPath = string.Empty;
        public string GimpPath
        {
            get => _gimpPath;
            set { _gimpPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGimpAvailable)); }
        }

        private string _shotcutPath = string.Empty;
        public string ShotcutPath
        {
            get => _shotcutPath;
            set { _shotcutPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShotcutAvailable)); }
        }

        // Availability properties
        public bool IsVlcAvailable => !string.IsNullOrEmpty(VlcPath) && File.Exists(VlcPath);
        public bool IsAudacityAvailable => !string.IsNullOrEmpty(AudacityPath) && File.Exists(AudacityPath);
        public bool IsGimpAvailable => !string.IsNullOrEmpty(GimpPath) && File.Exists(GimpPath);
        public bool IsShotcutAvailable => !string.IsNullOrEmpty(ShotcutPath) && File.Exists(ShotcutPath);
        public bool HasMissingApplications => !IsVlcAvailable || !IsAudacityAvailable || !IsGimpAvailable || !IsShotcutAvailable;

        // Embedding properties
        private bool _embedVlc;
        public bool EmbedVlc
        {
            get => _embedVlc;
            set { _embedVlc = value; OnPropertyChanged(); }
        }

        private bool _embedAudacity;
        public bool EmbedAudacity
        {
            get => _embedAudacity;
            set { _embedAudacity = value; OnPropertyChanged(); }
        }

        private bool _embedGimp;
        public bool EmbedGimp
        {
            get => _embedGimp;
            set { _embedGimp = value; OnPropertyChanged(); }
        }

        private bool _embedShotcut;
        public bool EmbedShotcut
        {
            get => _embedShotcut;
            set { _embedShotcut = value; OnPropertyChanged(); }
        }

        private IntPtr _vlcHostHandle;
        public IntPtr VlcHostHandle
        {
            get => _vlcHostHandle;
            set { _vlcHostHandle = value; OnPropertyChanged(); }
        }

        private IntPtr _audacityHostHandle;
        public IntPtr AudacityHostHandle
        {
            get => _audacityHostHandle;
            set { _audacityHostHandle = value; OnPropertyChanged(); }
        }

        private IntPtr _gimpHostHandle;
        public IntPtr GimpHostHandle
        {
            get => _gimpHostHandle;
            set { _gimpHostHandle = value; OnPropertyChanged(); }
        }

        private IntPtr _shotcutHostHandle;
        public IntPtr ShotcutHostHandle
        {
            get => _shotcutHostHandle;
            set { _shotcutHostHandle = value; OnPropertyChanged(); }
        }

        // File type detection
        private string _fileType = string.Empty;
        public string FileType
        {
            get => _fileType;
            set { _fileType = value; OnPropertyChanged(); }
        }

        public bool IsVideoFile { get; private set; }
        public bool IsAudioFile { get; private set; }
        public bool IsImageFile { get; private set; }

        // Commands
        public ICommand BrowseFileCommand { get; }
        public ICommand OpenInVlcCommand { get; }
        public ICommand OpenInAudacityCommand { get; }
        public ICommand OpenInGimpCommand { get; }
        public ICommand BrowseVlcCommand { get; }
        public ICommand BrowseAudacityCommand { get; }
        public ICommand BrowseGimpCommand { get; }
        public ICommand AutoDetectApplicationsCommand { get; }
        public ICommand DownloadVlcCommand { get; }
        public ICommand DownloadAudacityCommand { get; }
        public ICommand DownloadGimpCommand { get; }
        public ICommand DownloadShotcutCommand { get; }
        public ICommand EmbedVlcCommand { get; }
        public ICommand EmbedAudacityCommand { get; }
        public ICommand EmbedGimpCommand { get; }
        public ICommand EmbedShotcutCommand { get; }
        public ICommand OpenInShotcutCommand { get; }
        public ICommand BrowseShotcutCommand { get; }

        public MultimediaEditorViewModel()
        {
            BrowseFileCommand = new RelayCommand(_ => BrowseFile());
            OpenInVlcCommand = new RelayCommand(_ => OpenInVlc(), _ => IsVlcAvailable);
            OpenInAudacityCommand = new RelayCommand(_ => OpenInAudacity(), _ => IsAudacityAvailable);
            OpenInGimpCommand = new RelayCommand(_ => OpenInGimp(), _ => IsGimpAvailable);
            BrowseVlcCommand = new RelayCommand(_ => BrowseApplication("VLC"));
            BrowseAudacityCommand = new RelayCommand(_ => BrowseApplication("Audacity"));
            BrowseGimpCommand = new RelayCommand(_ => BrowseApplication("GIMP"));
            AutoDetectApplicationsCommand = new RelayCommand(_ => AutoDetectApplications());
            DownloadVlcCommand = new RelayCommand(_ => DownloadVlc());
            DownloadAudacityCommand = new RelayCommand(_ => DownloadAudacity());
            DownloadGimpCommand = new RelayCommand(_ => DownloadGimp());
            EmbedVlcCommand = new RelayCommand(_ => EmbedVlcApp(), _ => IsVlcAvailable);
            EmbedAudacityCommand = new RelayCommand(_ => EmbedAudacityApp(), _ => IsAudacityAvailable);
            EmbedGimpCommand = new RelayCommand(_ => EmbedGimpApp(), _ => IsGimpAvailable);
            EmbedShotcutCommand = new RelayCommand(_ => EmbedShotcutApp(), _ => IsShotcutAvailable);
            OpenInShotcutCommand = new RelayCommand(_ => OpenInShotcut(), _ => IsShotcutAvailable);
            BrowseShotcutCommand = new RelayCommand(_ => BrowseApplication("Shotcut"));
            DownloadShotcutCommand = new RelayCommand(_ => DownloadShotcut());

            AutoDetectApplications();
        }

        private void BrowseFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*|Videos (*.mp4;*.mkv;*.avi;*.mov;*.wmv)|*.mp4;*.mkv;*.avi;*.mov;*.wmv|Audio (*.mp3;*.flac;*.wav;*.ogg;*.aac)|*.mp3;*.flac;*.wav;*.ogg;*.aac|Images (*.jpg;*.png;*.gif;*.bmp;*.tiff)|*.jpg;*.png;*.gif;*.bmp;*.tiff",
                Title = "Select media file to edit"
            };

            if (dialog.ShowDialog() == true)
            {
                FilePath = dialog.FileName;
                DetectFileType();
                StatusMessage = $"Loaded: {Path.GetFileName(FilePath)}";
            }
        }

        private void DetectFileType()
        {
            if (string.IsNullOrEmpty(FilePath))
                return;

            var extension = Path.GetExtension(FilePath).ToLowerInvariant();
            
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg" };
            var audioExtensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".aac", ".wma", ".m4a", ".opus" };
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".svg" };

            IsVideoFile = videoExtensions.Contains(extension);
            IsAudioFile = audioExtensions.Contains(extension);
            IsImageFile = imageExtensions.Contains(extension);

            if (IsVideoFile)
                FileType = "Video";
            else if (IsAudioFile)
                FileType = "Audio";
            else if (IsImageFile)
                FileType = "Image";
            else
                FileType = "Unknown";

            OnPropertyChanged(nameof(IsVideoFile));
            OnPropertyChanged(nameof(IsAudioFile));
            OnPropertyChanged(nameof(IsImageFile));
        }

        private void OpenInVlc()
        {
            if (!IsVlcAvailable)
            {
                StatusMessage = "VLC not available";
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = VlcPath,
                    Arguments = !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) ? $"\"{FilePath}\"" : "",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                StatusMessage = !string.IsNullOrEmpty(FilePath) ? $"Opened in VLC: {Path.GetFileName(FilePath)}" : "VLC launched";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening VLC: {ex.Message}";
            }
        }

        private void OpenInAudacity()
        {
            if (!IsAudacityAvailable)
            {
                StatusMessage = "Audacity not available";
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = AudacityPath,
                    Arguments = !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) ? $"\"{FilePath}\"" : "",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                StatusMessage = !string.IsNullOrEmpty(FilePath) ? $"Opened in Audacity: {Path.GetFileName(FilePath)}" : "Audacity launched";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening Audacity: {ex.Message}";
            }
        }

        private void OpenInGimp()
        {
            if (!IsGimpAvailable)
            {
                StatusMessage = "GIMP not available";
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = GimpPath,
                    Arguments = !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) ? $"\"{FilePath}\"" : "",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                StatusMessage = !string.IsNullOrEmpty(FilePath) ? $"Opened in GIMP: {Path.GetFileName(FilePath)}" : "GIMP launched";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening GIMP: {ex.Message}";
            }
        }

        private void BrowseApplication(string appName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = $"Locate {appName} executable"
            };

            if (dialog.ShowDialog() == true)
            {
                switch (appName)
                {
                    case "VLC":
                        VlcPath = dialog.FileName;
                        break;
                    case "Audacity":
                        AudacityPath = dialog.FileName;
                        break;
                    case "GIMP":
                        GimpPath = dialog.FileName;
                        break;
                    case "Shotcut":
                        ShotcutPath = dialog.FileName;
                        break;
                }
                StatusMessage = $"{appName} path set: {dialog.FileName}";
            }
        }

        private void AutoDetectApplications()
        {
            // Try to find VLC
            var vlcPaths = new[]
            {
                @"C:\Program Files\VideoLAN\VLC\vlc.exe",
                @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe")
            };

            foreach (var path in vlcPaths)
            {
                if (File.Exists(path))
                {
                    VlcPath = path;
                    break;
                }
            }

            // Try to find Audacity
            var audacityPaths = new[]
            {
                @"C:\Program Files\Audacity\Audacity.exe",
                @"C:\Program Files (x86)\Audacity\Audacity.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Audacity", "Audacity.exe")
            };

            foreach (var path in audacityPaths)
            {
                if (File.Exists(path))
                {
                    AudacityPath = path;
                    break;
                }
            }

            // Try to find GIMP (check GIMP 3 first, then GIMP 2)
            var gimpPaths = new[]
            {
                @"C:\Program Files\GIMP 3\bin\gimp.exe",
                @"C:\Program Files (x86)\GIMP 3\bin\gimp.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GIMP 3", "bin", "gimp.exe"),
                @"C:\Program Files\GIMP 2\bin\gimp-2.10.exe",
                @"C:\Program Files (x86)\GIMP 2\bin\gimp-2.10.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GIMP 2", "bin", "gimp-2.10.exe")
            };

            foreach (var path in gimpPaths)
            {
                if (File.Exists(path))
                {
                    GimpPath = path;
                    break;
                }
            }

            // Try to find Shotcut
            var shotcutPaths = new[]
            {
                @"C:\Program Files\Shotcut\shotcut.exe",
                @"C:\Program Files (x86)\Shotcut\shotcut.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Shotcut", "shotcut.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Shotcut", "shotcut.exe")
            };

            foreach (var path in shotcutPaths)
            {
                if (File.Exists(path))
                {
                    ShotcutPath = path;
                    break;
                }
            }

            UpdateApplicationAvailability();
            
            var foundApps = new List<string>();
            if (IsVlcAvailable) foundApps.Add("VLC");
            if (IsAudacityAvailable) foundApps.Add("Audacity");
            if (IsGimpAvailable) foundApps.Add("GIMP");
            if (IsShotcutAvailable) foundApps.Add("Shotcut");
            
            if (foundApps.Count == 4)
            {
                StatusMessage = "✔ All applications detected successfully!";
            }
            else if (foundApps.Count > 0)
            {
                StatusMessage = $"Found: {string.Join(", ", foundApps)}. Click download buttons for missing apps.";
            }
            else
            {
                StatusMessage = "⚠️ No applications detected. Click download buttons to get the apps.";
            }
        }

        private void UpdateApplicationAvailability()
        {
            OnPropertyChanged(nameof(IsVlcAvailable));
            OnPropertyChanged(nameof(IsAudacityAvailable));
            OnPropertyChanged(nameof(IsGimpAvailable));
            OnPropertyChanged(nameof(IsShotcutAvailable));
            OnPropertyChanged(nameof(HasMissingApplications));
        }

        private void DownloadVlc()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.videolan.org/vlc/",
                    UseShellExecute = true
                });
                StatusMessage = "Opening VLC download page in your browser...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening download page: {ex.Message}";
            }
        }

        private void DownloadAudacity()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.audacityteam.org/download/",
                    UseShellExecute = true
                });
                StatusMessage = "Opening Audacity download page in your browser...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening download page: {ex.Message}";
            }
        }

        private void DownloadGimp()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.gimp.org/downloads/",
                    UseShellExecute = true
                });
                StatusMessage = "Opening GIMP download page in your browser...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening download page: {ex.Message}";
            }
        }

        private void DownloadShotcut()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.shotcut.org/download/",
                    UseShellExecute = true
                });
                StatusMessage = "Opening Shotcut download page in your browser...";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening download page: {ex.Message}";
            }
        }

        private void OpenInShotcut()
        {
            if (!IsShotcutAvailable)
            {
                StatusMessage = "Shotcut not available";
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ShotcutPath,
                    Arguments = !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) ? $"\"{FilePath}\"" : "",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                StatusMessage = string.IsNullOrEmpty(FilePath) 
                    ? "Opened Shotcut" 
                    : $"Opened in Shotcut: {Path.GetFileName(FilePath)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening Shotcut: {ex.Message}";
            }
        }

        private Process? _embeddedShotcutProcess;

        private async void EmbedShotcutApp()
        {
            if (!IsShotcutAvailable)
            {
                StatusMessage = "Shotcut not available";
                return;
            }

            EmbedShotcut = true;
            StatusMessage = "Launching Shotcut for embedding...";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ShotcutPath,
                    UseShellExecute = false
                };

                _embeddedShotcutProcess = Process.Start(startInfo);

                if (_embeddedShotcutProcess != null)
                {
                    await System.Threading.Tasks.Task.Delay(3000); // Wait for Shotcut to initialize

                    if (!_embeddedShotcutProcess.HasExited && ShotcutHostHandle != IntPtr.Zero)
                    {
                        var handle = _embeddedShotcutProcess.MainWindowHandle;
                        if (handle != IntPtr.Zero)
                        {
                            SetParent(handle, ShotcutHostHandle);
                            int style = GetWindowLong(handle, GWL_STYLE);
                            style &= ~WS_CAPTION;
                            style &= ~WS_THICKFRAME;
                            SetWindowLong(handle, GWL_STYLE, style);
                            SetWindowPos(handle, IntPtr.Zero, 0, 0, 1000, 700, SWP_NOZORDER | SWP_NOACTIVATE);
                            StatusMessage = "Shotcut embedded successfully!";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error embedding Shotcut: {ex.Message}";
                EmbedShotcut = false;
            }
        }

        private async void EmbedVlcApp()
        {
            if (!IsVlcAvailable || VlcHostHandle == IntPtr.Zero)
            {
                StatusMessage = "VLC not available or host window not ready. Make sure you're on the VLC tab.";
                EmbedVlc = false;
                return;
            }

            try
            {
                // Kill existing embedded process if any
                if (_embeddedVlcProcess != null)
                {
                    try
                    {
                        if (!_embeddedVlcProcess.HasExited)
                            _embeddedVlcProcess.Kill();
                        _embeddedVlcProcess.Dispose();
                    }
                    catch { }
                    _embeddedVlcProcess = null;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = VlcPath,
                    Arguments = !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) 
                        ? $"--no-video-title-show \"{FilePath}\"" 
                        : "--no-video-title-show",
                    UseShellExecute = false
                };

                _embeddedVlcProcess = Process.Start(startInfo);
                if (_embeddedVlcProcess != null)
                {
                    // Wait for window to be created and ready for input
                    try { _embeddedVlcProcess.WaitForInputIdle(5000); } catch { }
                    await System.Threading.Tasks.Task.Delay(1500);

                    IntPtr handle = IntPtr.Zero;
                    // Try to get the handle multiple times
                    for (int i = 0; i < 10 && handle == IntPtr.Zero; i++)
                    {
                        if (_embeddedVlcProcess.HasExited) break;
                        _embeddedVlcProcess.Refresh();
                        handle = _embeddedVlcProcess.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                            await System.Threading.Tasks.Task.Delay(300);
                    }

                    if (!_embeddedVlcProcess.HasExited && handle != IntPtr.Zero)
                    {
                        // Remove window border/caption
                        var style = GetWindowLong(handle, GWL_STYLE);
                        style &= ~(WS_CAPTION | WS_THICKFRAME);
                        SetWindowLong(handle, GWL_STYLE, style);

                        // Embed the window
                        SetParent(handle, VlcHostHandle);
                        
                        // Resize to fill the host container
                        SetWindowPos(handle, IntPtr.Zero, 0, 0, 800, 600, SWP_NOZORDER | SWP_NOACTIVATE);

                        EmbedVlc = true;
                        StatusMessage = "VLC embedded in tab - resize the window to fit";
                    }
                    else
                    {
                        StatusMessage = "Could not get VLC window handle - try clicking 'Open in VLC' instead";
                        EmbedVlc = false;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error embedding VLC: {ex.Message}";
                EmbedVlc = false;
            }
        }

        private async void EmbedAudacityApp()
        {
            if (!IsAudacityAvailable || AudacityHostHandle == IntPtr.Zero)
            {
                StatusMessage = "Audacity not available or host window not ready";
                return;
            }

            try
            {
                // Kill existing embedded process if any
                if (_embeddedAudacityProcess != null && !_embeddedAudacityProcess.HasExited)
                {
                    _embeddedAudacityProcess.Kill();
                    _embeddedAudacityProcess.Dispose();
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = AudacityPath,
                    Arguments = !string.IsNullOrEmpty(FilePath) ? $"\"{FilePath}\"" : string.Empty,
                    UseShellExecute = false
                };

                _embeddedAudacityProcess = Process.Start(startInfo);
                if (_embeddedAudacityProcess != null)
                {
                    // Wait for window to be created
                    _embeddedAudacityProcess.WaitForInputIdle();
                    await System.Threading.Tasks.Task.Delay(2000);

                    IntPtr handle = IntPtr.Zero;
                    // Try to get the handle multiple times
                    for (int i = 0; i < 10 && handle == IntPtr.Zero; i++)
                    {
                        _embeddedAudacityProcess.Refresh();
                        handle = _embeddedAudacityProcess.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                            await System.Threading.Tasks.Task.Delay(300);
                    }

                    if (!_embeddedAudacityProcess.HasExited && handle != IntPtr.Zero)
                    {
                        // Remove window border/caption
                        var style = GetWindowLong(handle, GWL_STYLE);
                        style &= ~(WS_CAPTION | WS_THICKFRAME);
                        SetWindowLong(handle, GWL_STYLE, style);

                        // Embed the window
                        SetParent(handle, AudacityHostHandle);
                        SetWindowPos(handle, IntPtr.Zero, 0, 0, 800, 600, SWP_NOZORDER | SWP_NOACTIVATE);

                        EmbedAudacity = true;
                        StatusMessage = "Audacity embedded in tab - resize the window to fit";
                    }
                    else
                    {
                        StatusMessage = "Could not get Audacity window handle - try clicking 'Open in Audacity' instead";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error embedding Audacity: {ex.Message}";
            }
        }

        private async void EmbedGimpApp()
        {
            if (!IsGimpAvailable || GimpHostHandle == IntPtr.Zero)
            {
                StatusMessage = "GIMP not available or host window not ready";
                return;
            }

            try
            {
                // Kill existing embedded process if any
                if (_embeddedGimpProcess != null && !_embeddedGimpProcess.HasExited)
                {
                    _embeddedGimpProcess.Kill();
                    _embeddedGimpProcess.Dispose();
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = GimpPath,
                    Arguments = !string.IsNullOrEmpty(FilePath) ? $"\"{FilePath}\"" : string.Empty,
                    UseShellExecute = false
                };

                _embeddedGimpProcess = Process.Start(startInfo);
                if (_embeddedGimpProcess != null)
                {
                    // Wait for window to be created - GIMP takes longer
                    _embeddedGimpProcess.WaitForInputIdle();
                    await System.Threading.Tasks.Task.Delay(4000); // GIMP takes longer to start

                    IntPtr handle = IntPtr.Zero;
                    // Try to get the handle multiple times
                    for (int i = 0; i < 15 && handle == IntPtr.Zero; i++)
                    {
                        _embeddedGimpProcess.Refresh();
                        handle = _embeddedGimpProcess.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                            await System.Threading.Tasks.Task.Delay(500);
                    }

                    if (!_embeddedGimpProcess.HasExited && handle != IntPtr.Zero)
                    {
                        // Remove window border/caption
                        var style = GetWindowLong(handle, GWL_STYLE);
                        style &= ~(WS_CAPTION | WS_THICKFRAME);
                        SetWindowLong(handle, GWL_STYLE, style);

                        // Embed the window
                        SetParent(handle, GimpHostHandle);
                        SetWindowPos(handle, IntPtr.Zero, 0, 0, 800, 600, SWP_NOZORDER | SWP_NOACTIVATE);

                        EmbedGimp = true;
                        StatusMessage = "GIMP embedded in tab - resize the window to fit";
                    }
                    else
                    {
                        StatusMessage = "Could not get GIMP window handle - GIMP uses multiple windows, try 'Open in GIMP' instead";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error embedding GIMP: {ex.Message}";
            }
        }
    }
}
