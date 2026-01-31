using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Screen Recorder view.
    /// </summary>
    public class ScreenRecorderViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ScreenRecorderService _recorderService;
        private readonly DispatcherTimer _durationTimer;
        private readonly DispatcherTimer _countdownTimer;
        
        private bool _isRecording;
        private bool _isStopping;
        private bool _isCountingDown;
        private int _countdownSeconds;
        private bool _recordAudio = false; // Microphone recording disabled - causes failures
        private bool _recordSystemAudio = true;
        private bool _useStartDelay;
        private string? _selectedAudioDevice;
        private int _frameRate = 30;
        private VideoCodec _selectedCodec = VideoCodec.H264;
        private string _outputFolder;
        private string _statusMessage = "Ready to record (Ctrl+Shift+R to start, Ctrl+Shift+S to stop)";
        private string _recordingDuration = "00:00:00";
        private string? _lastRecordingPath;
        private ObservableCollection<AudioDevice> _audioDevices = new();
        private ObservableCollection<string> _logMessages = new();

        public ScreenRecorderViewModel()
        {
            _recorderService = new ScreenRecorderService();
            _recorderService.RecordingStatusChanged += OnRecordingStatusChanged;
            _recorderService.RecordingError += OnRecordingError;
            _recorderService.RecordingProgress += OnRecordingProgress;

            _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _durationTimer.Tick += (s, e) => UpdateDuration();

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;

            // Default output folder
            _outputFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "Screen Recordings");

            // Initialize commands - using sync wrappers to avoid async void issues
            // CanExecute always true for Stop/Cancel since buttons are only visible when recording
            StartRecordingCommand = new RelayCommand(_ => ExecuteStartRecording(), _ => !IsRecording && !_isStopping && !_isCountingDown);
            StopRecordingCommand = new RelayCommand(_ => ExecuteStopRecording());
            CancelRecordingCommand = new RelayCommand(_ => CancelRecording());
            BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
            OpenOutputFolderCommand = new RelayCommand(_ => OpenOutputFolder(), _ => Directory.Exists(OutputFolder));
            OpenLastRecordingCommand = new RelayCommand(_ => OpenLastRecording(), _ => !string.IsNullOrEmpty(LastRecordingPath) && File.Exists(LastRecordingPath));
            RefreshAudioDevicesCommand = new RelayCommand(_ => ExecuteRefreshAudioDevices());
            ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());

            // Load audio devices on startup
            _ = LoadAudioDevicesAsync();
        }

        #region Properties

        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (_isRecording != value)
                {
                    _isRecording = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotRecording));
                    OnPropertyChanged(nameof(RecordButtonContent));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsNotRecording => !IsRecording;

        public string RecordButtonContent => IsRecording ? "⏹ Stop Recording" : "⏺ Start Recording";

        public bool RecordAudio
        {
            get => _recordAudio;
            set { _recordAudio = value; OnPropertyChanged(); }
        }

        public bool RecordSystemAudio
        {
            get => _recordSystemAudio;
            set { _recordSystemAudio = value; OnPropertyChanged(); }
        }

        public bool UseStartDelay
        {
            get => _useStartDelay;
            set { _useStartDelay = value; OnPropertyChanged(); }
        }

        public bool IsCountingDown
        {
            get => _isCountingDown;
            private set { _isCountingDown = value; OnPropertyChanged(); OnPropertyChanged(nameof(CountdownDisplay)); }
        }

        public string CountdownDisplay => _countdownSeconds > 0 ? _countdownSeconds.ToString() : "";

        public string? SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set { _selectedAudioDevice = value; OnPropertyChanged(); }
        }

        public int FrameRate
        {
            get => _frameRate;
            set { _frameRate = Math.Clamp(value, 1, 120); OnPropertyChanged(); }
        }

        public VideoCodec SelectedCodec
        {
            get => _selectedCodec;
            set { _selectedCodec = value; OnPropertyChanged(); }
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set { _outputFolder = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string RecordingDuration
        {
            get => _recordingDuration;
            private set { _recordingDuration = value; OnPropertyChanged(); }
        }

        public string? LastRecordingPath
        {
            get => _lastRecordingPath;
            private set
            {
                _lastRecordingPath = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public ObservableCollection<AudioDevice> AudioDevices
        {
            get => _audioDevices;
            set { _audioDevices = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> LogMessages
        {
            get => _logMessages;
            set { _logMessages = value; OnPropertyChanged(); }
        }

        public int[] FrameRateOptions { get; } = { 15, 24, 30, 60 };

        public VideoCodec[] CodecOptions { get; } = { VideoCodec.H264, VideoCodec.H265, VideoCodec.VP9 };

        #endregion

        #region Commands

        public ICommand StartRecordingCommand { get; }
        public ICommand StopRecordingCommand { get; }
        public ICommand CancelRecordingCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand OpenLastRecordingCommand { get; }
        public ICommand RefreshAudioDevicesCommand { get; }
        public ICommand ClearLogCommand { get; }

        #endregion

        #region Methods

        private async Task LoadAudioDevicesAsync()
        {
            try
            {
                var devices = await _recorderService.GetAudioDevicesAsync();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AudioDevices.Clear();
                    foreach (var device in devices)
                    {
                        AudioDevices.Add(device);
                    }
                    
                    // Select first device if available
                    if (AudioDevices.Count > 0 && string.IsNullOrEmpty(SelectedAudioDevice))
                    {
                        SelectedAudioDevice = AudioDevices[0].Name;
                    }
                    
                    AddLogMessage($"Found {devices.Count} audio device(s)");
                });
            }
            catch (Exception ex)
            {
                AddLogMessage($"Error loading audio devices: {ex.Message}");
            }
        }

        private void ExecuteRefreshAudioDevices()
        {
            AddLogMessage("Refreshing audio devices...");
            _ = LoadAudioDevicesAsync();
        }

        private void ExecuteStartRecording()
        {
            AddLogMessage("Start recording button clicked");
            
            if (UseStartDelay)
            {
                // Start countdown
                _countdownSeconds = 3;
                _isCountingDown = true;
                OnPropertyChanged(nameof(IsCountingDown));
                OnPropertyChanged(nameof(CountdownDisplay));
                StatusMessage = $"Recording starts in {_countdownSeconds}...";
                AddLogMessage($"Starting {_countdownSeconds} second countdown...");
                _countdownTimer.Start();
                CommandManager.InvalidateRequerySuggested();
            }
            else
            {
                _ = StartRecordingAsync();
            }
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            _countdownSeconds--;
            OnPropertyChanged(nameof(CountdownDisplay));
            
            if (_countdownSeconds > 0)
            {
                StatusMessage = $"Recording starts in {_countdownSeconds}...";
                AddLogMessage($"Countdown: {_countdownSeconds}");
            }
            else
            {
                _countdownTimer.Stop();
                _isCountingDown = false;
                OnPropertyChanged(nameof(IsCountingDown));
                StatusMessage = "Starting recording...";
                AddLogMessage("Countdown complete, starting recording");
                _ = StartRecordingAsync();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ExecuteStopRecording()
        {
            AddLogMessage($"Stop recording button clicked. IsRecording={IsRecording}, _isStopping={_isStopping}");
            AddLogMessage($"Service.IsRecording={_recorderService.IsRecording}");
            _ = StopRecordingAsync();
        }

        private async Task StartRecordingAsync()
        {
            if (IsRecording) return;

            // Generate output filename
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var extension = SelectedCodec == VideoCodec.VP9 ? ".webm" : ".mp4";
            var outputPath = Path.Combine(OutputFolder, $"Recording_{timestamp}{extension}");

            var options = new ScreenRecordingOptions
            {
                OutputPath = outputPath,
                RecordAudio = RecordAudio,
                RecordSystemAudio = RecordSystemAudio,
                AudioDevice = RecordAudio ? SelectedAudioDevice : null,
                FrameRate = FrameRate,
                VideoCodec = SelectedCodec
            };

            StatusMessage = "Starting recording...";
            AddLogMessage($"Starting recording to: {outputPath}");
            AddLogMessage($"Audio: SystemAudio={RecordSystemAudio}");

            var success = await _recorderService.StartRecordingAsync(options);
            
            if (success)
            {
                IsRecording = true;
                StatusMessage = "Recording...";
                _durationTimer.Start();
            }
        }

        private async Task StopRecordingAsync()
        {
            AddLogMessage($"StopRecordingAsync entered. IsRecording={IsRecording}, _isStopping={_isStopping}");
            
            if (!IsRecording || _isStopping)
            {
                AddLogMessage($"StopRecordingAsync early return: IsRecording={IsRecording}, _isStopping={_isStopping}");
                return;
            }

            _isStopping = true;
            AddLogMessage("_isStopping set to true");
            CommandManager.InvalidateRequerySuggested();
            
            StatusMessage = "Stopping recording...";
            _durationTimer.Stop();

            try
            {
                AddLogMessage("Calling _recorderService.StopRecordingAsync()...");
                var outputPath = await _recorderService.StopRecordingAsync();
                AddLogMessage($"StopRecordingAsync returned: {outputPath ?? "null"}");
                
                if (!string.IsNullOrEmpty(outputPath))
                {
                    LastRecordingPath = outputPath;
                    StatusMessage = $"Recording saved: {Path.GetFileName(outputPath)}";
                    AddLogMessage($"Recording saved: {outputPath}");
                }
                else
                {
                    StatusMessage = "Recording failed or was cancelled";
                    AddLogMessage("Recording failed or was cancelled (null output path)");
                }
            }
            catch (Exception ex)
            {
                AddLogMessage($"StopRecordingAsync exception: {ex.GetType().Name}: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRecording = false;
                _isStopping = false;
                RecordingDuration = "00:00:00";
                AddLogMessage("StopRecordingAsync completed, state reset");
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void CancelRecording()
        {
            _recorderService.CancelRecording();
            _durationTimer.Stop();
            IsRecording = false;
            RecordingDuration = "00:00:00";
            StatusMessage = "Recording cancelled";
        }

        private void BrowseOutputFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select output folder for recordings",
                SelectedPath = OutputFolder,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputFolder = dialog.SelectedPath;
            }
        }

        private void OpenOutputFolder()
        {
            if (Directory.Exists(OutputFolder))
            {
                System.Diagnostics.Process.Start("explorer.exe", OutputFolder);
            }
        }

        private void OpenLastRecording()
        {
            if (!string.IsNullOrEmpty(LastRecordingPath) && File.Exists(LastRecordingPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LastRecordingPath,
                    UseShellExecute = true
                });
            }
        }

        private void UpdateDuration()
        {
            if (_recorderService.IsRecording)
            {
                RecordingDuration = _recorderService.RecordingDuration.ToString(@"hh\:mm\:ss");
            }
        }

        private void AddLogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Insert(0, $"[{timestamp}] {message}");
                
                // Keep log manageable
                while (LogMessages.Count > 100)
                {
                    LogMessages.RemoveAt(LogMessages.Count - 1);
                }
            });
        }

        private void OnRecordingStatusChanged(object? sender, bool isRecording)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsRecording = isRecording;
            });
        }

        private void OnRecordingError(object? sender, string error)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Error: {error}";
                AddLogMessage($"ERROR: {error}");
            });
        }

        private void OnRecordingProgress(object? sender, string progress)
        {
            AddLogMessage(progress);
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _durationTimer.Stop();
            _recorderService.Dispose();
        }

        #endregion
    }
}
