using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class GifMakerViewModel : BindableBase
    {
        private readonly GifMakerService _service = new();
        private CancellationTokenSource? _cts;

        public GifMakerViewModel()
        {
            BrowseInputCommand = new RelayCommand(_ => BrowseInput());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            CreateGifCommand = new RelayCommand(async _ => await CreateGifAsync(), _ => !IsCreating && !string.IsNullOrEmpty(InputFile));
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsCreating);
            OpenOutputCommand = new RelayCommand(_ => OpenOutput(), _ => File.Exists(OutputFile));

            _service.LogMessage += (s, msg) => LogOutput += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            _service.ProgressChanged += (s, p) => Progress = p;
        }

        private string _inputFile = "";
        public string InputFile
        {
            get => _inputFile;
            set
            {
                SetProperty(ref _inputFile, value);
                if (!string.IsNullOrEmpty(value))
                    OutputFile = Path.ChangeExtension(value, ".gif");
                _ = LoadDurationAsync();
            }
        }

        private string _outputFile = "";
        public string OutputFile { get => _outputFile; set => SetProperty(ref _outputFile, value); }

        private double _startTime;
        public double StartTime { get => _startTime; set => SetProperty(ref _startTime, value); }

        private double _duration = 5.0;
        public double Duration { get => _duration; set => SetProperty(ref _duration, value); }

        private double _videoDuration;
        public double VideoDuration { get => _videoDuration; set => SetProperty(ref _videoDuration, value); }

        private int _width = 480;
        public int Width { get => _width; set => SetProperty(ref _width, value); }

        private int _fps = 15;
        public int Fps { get => _fps; set => SetProperty(ref _fps, value); }

        private bool _highQuality = true;
        public bool HighQuality { get => _highQuality; set => SetProperty(ref _highQuality, value); }

        private string _textOverlay = "";
        public string TextOverlay { get => _textOverlay; set => SetProperty(ref _textOverlay, value); }

        private bool _isCreating;
        public bool IsCreating { get => _isCreating; set => SetProperty(ref _isCreating, value); }

        private int _progress;
        public int Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private string _statusMessage = "Select a video file to create a GIF";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _logOutput = "";
        public string LogOutput { get => _logOutput; set => SetProperty(ref _logOutput, value); }

        public ICommand BrowseInputCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand CreateGifCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenOutputCommand { get; }

        private async Task CreateGifAsync()
        {
            if (string.IsNullOrEmpty(InputFile)) return;

            IsCreating = true;
            _cts = new CancellationTokenSource();
            LogOutput = "";
            Progress = 0;

            try
            {
                var options = new GifMakerService.GifOptions
                {
                    InputPath = InputFile,
                    OutputPath = OutputFile,
                    StartTime = StartTime,
                    Duration = Duration,
                    Width = Width,
                    Fps = Fps,
                    UseHighQuality = HighQuality,
                    TextOverlay = string.IsNullOrWhiteSpace(TextOverlay) ? null : TextOverlay
                };

                var success = await _service.CreateGifAsync(options, _cts.Token);
                StatusMessage = success
                    ? $"GIF created: {new FileInfo(OutputFile).Length / 1024:N0} KB"
                    : "Failed to create GIF";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsCreating = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task LoadDurationAsync()
        {
            if (string.IsNullOrEmpty(InputFile) || !File.Exists(InputFile)) return;
            VideoDuration = await _service.GetVideoDurationAsync(InputFile);
            if (Duration > VideoDuration && VideoDuration > 0) Duration = Math.Min(5, VideoDuration);
        }

        private void BrowseInput()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v|All files|*.*"
            };
            if (dlg.ShowDialog() == true) InputFile = dlg.FileName;
        }

        private void BrowseOutput()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "GIF files (*.gif)|*.gif" };
            if (dlg.ShowDialog() == true) OutputFile = dlg.FileName;
        }

        private void OpenOutput()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OutputFile) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
