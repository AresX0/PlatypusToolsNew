using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Models;

namespace PlatypusTools.UI.ViewModels
{
    public class VideoCombinerViewModel : BindableBase
    {
        private readonly VideoCombinerService _combiner;
        private CancellationTokenSource? _cts;

        public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();

        public string OutputPath { get; set; } = string.Empty;

        public string FfmpegFoundText => FFmpegService.FindFfmpeg() != null ? "Available" : "Not found";

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; RaisePropertyChanged(); ((AsyncRelayCommand)CombineCommand).RaiseCanExecuteChanged(); ((RelayCommand)CancelCommand).RaiseCanExecuteChanged(); } }

        private string _log = string.Empty;
        public string Log { get => _log; set { _log = value; RaisePropertyChanged(); } }

        private string _lastProgress = string.Empty;
        public string LastProgress { get => _lastProgress; set { _lastProgress = value; RaisePropertyChanged(); } }

        private double _progressPercent;
        public double ProgressPercent { get => _progressPercent; set { _progressPercent = value; RaisePropertyChanged(); } }

        public ICommand AddFilesCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand CombineCommand { get; }
        public ICommand CancelCommand { get; }

        public VideoCombinerViewModel(VideoCombinerService combiner)
        {
            _combiner = combiner;

            AddFilesCommand = new RelayCommand(_ => AddFiles());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            CombineCommand = new AsyncRelayCommand(CombineAsync, () => !IsRunning);
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
        }

        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Video Files|*.mp4;*.mkv;*.mov;*.ts;*.avi|All Files|*.*" };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                {
                    Files.Add(f);
                }
            }
        }

        private void BrowseOutput()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "MP4 File|*.mp4|MKV File|*.mkv|All Files|*.*" };
            if (dlg.ShowDialog() == true)
            {
                OutputPath = dlg.FileName;
                RaisePropertyChanged(nameof(OutputPath));
            }
        }

        private void Cancel()
        {
            _cts?.Cancel();
            Log += "Cancellation requested...\n";
        }

        public virtual async Task CombineAsync()
        {
            IsRunning = true;
            _cts = new CancellationTokenSource();
            Log = string.Empty;
            LastProgress = string.Empty;
            ProgressPercent = 0;
            // compute total duration of inputs (sum of ffprobe durations) so we can compute percent
            double totalSeconds = 0;
            foreach (var f in Files)
            {
                try
                {
                    var d = await PlatypusTools.Core.Services.FFprobeService.GetDurationSecondsAsync(f);
                    if (d > 0) totalSeconds += d;
                }
                catch { }
            }

            var progress = new Progress<string>(s => {
                Log += s + Environment.NewLine;
                if (PlatypusTools.Core.Services.FFmpegProgressParser.TryParseOutTimeMs(s, out var ms))
                {
                    LastProgress = $"time={ms}ms";
                    if (totalSeconds > 0)
                    {
                        var pct = Math.Min(100.0, Math.Max(0.0, (double)ms / (totalSeconds * 1000.0) * 100.0));
                        ProgressPercent = pct;
                    }
                }
            });
            try
            {
                var result = await _combiner.CombineAsync(Files, OutputPath, progress, _cts.Token);
                var outStr = $"Exit: {result.ExitCode}\n\nStdOut:\n{result.StdOut}\n\nStdErr:\n{result.StdErr}";
                Log += outStr + Environment.NewLine;
                // Avoid showing modal dialogs during test runs or when no main window is present
                try
                {
                    if (System.Windows.Application.Current?.MainWindow != null)
                    {
                        var wnd = new PlatypusTools.UI.Views.ScriptOutputWindow(outStr);
                        wnd.Owner = System.Windows.Application.Current.MainWindow;
                        wnd.ShowDialog();
                    }
                }
                catch { }
            }
            catch (OperationCanceledException)
            {
                Log += "Operation cancelled.\n";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsRunning = false;
            }
        }
    }
}