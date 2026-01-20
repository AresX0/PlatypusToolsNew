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

        // Transition support
        private bool _enableTransitions;
        public bool EnableTransitions { get => _enableTransitions; set { _enableTransitions = value; RaisePropertyChanged(); } }

        private string _selectedTransition = "Cross Dissolve";
        public string SelectedTransition { get => _selectedTransition; set { _selectedTransition = value; RaisePropertyChanged(); } }

        private double _transitionDurationSeconds = 1.0;
        public double TransitionDurationSeconds { get => _transitionDurationSeconds; set { _transitionDurationSeconds = value; RaisePropertyChanged(); } }

        public ObservableCollection<string> AvailableTransitions { get; } = new ObservableCollection<string>
        {
            "None",
            "Cross Dissolve",
            "Fade to Black",
            "Fade to White",
            "Wipe Left",
            "Wipe Right",
            "Wipe Up",
            "Wipe Down",
            "Slide Left",
            "Slide Right"
        };

        private string? _selectedFile;
        public string? SelectedFile { get => _selectedFile; set { _selectedFile = value; RaisePropertyChanged(); } }

        public ICommand AddFilesCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand CombineCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        public VideoCombinerViewModel(VideoCombinerService combiner)
        {
            _combiner = combiner;

            AddFilesCommand = new RelayCommand(_ => AddFiles());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            CombineCommand = new AsyncRelayCommand(CombineAsync, () => !IsRunning);
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
            RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedFile != null);
            MoveUpCommand = new RelayCommand(_ => MoveUp(), _ => CanMoveUp());
            MoveDownCommand = new RelayCommand(_ => MoveDown(), _ => CanMoveDown());
        }

        private void RemoveSelected()
        {
            if (SelectedFile != null && Files.Contains(SelectedFile))
            {
                Files.Remove(SelectedFile);
                SelectedFile = null;
            }
        }

        private bool CanMoveUp() => SelectedFile != null && Files.IndexOf(SelectedFile) > 0;
        private bool CanMoveDown() => SelectedFile != null && Files.IndexOf(SelectedFile) < Files.Count - 1;

        private void MoveUp()
        {
            if (SelectedFile == null) return;
            var index = Files.IndexOf(SelectedFile);
            if (index > 0)
            {
                Files.Move(index, index - 1);
            }
        }

        private void MoveDown()
        {
            if (SelectedFile == null) return;
            var index = Files.IndexOf(SelectedFile);
            if (index < Files.Count - 1)
            {
                Files.Move(index, index + 1);
            }
        }

        private void AddFiles()
        {
            var files = Services.FileDialogService.OpenVideoFiles();
            foreach (var f in files)
            {
                Files.Add(f);
            }
        }

        private void BrowseOutput()
        {
            var path = Services.FileDialogService.SaveFile(
                "MP4 File|*.mp4|MKV File|*.mkv|All Files|*.*",
                "Save Combined Video");
            if (path != null)
            {
                OutputPath = path;
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
                FFmpegResult result;
                if (EnableTransitions && SelectedTransition != "None" && Files.Count > 1)
                {
                    // Use transition-aware combine
                    result = await _combiner.CombineWithTransitionsAsync(
                        Files, 
                        OutputPath, 
                        SelectedTransition,
                        TransitionDurationSeconds,
                        progress, 
                        _cts.Token);
                }
                else
                {
                    // Use fast stream copy combine
                    result = await _combiner.CombineAsync(Files, OutputPath, progress, _cts.Token);
                }
                var outStr = $"Exit: {result.ExitCode}\n\nStdOut:\n{result.StdOut}\n\nStdErr:\n{result.StdErr}";
                Log += outStr + Environment.NewLine;
                // Avoid showing modal dialogs during test runs or when no main window is present
                try
                {
                    var mainWnd = System.Windows.Application.Current?.MainWindow;
                    if (mainWnd != null && mainWnd.IsLoaded)
                    {
                        var wnd = new PlatypusTools.UI.Views.ScriptOutputWindow(outStr);
                        wnd.Owner = mainWnd;
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