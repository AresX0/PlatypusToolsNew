using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class UpscalerViewModel : BindableBase
    {
        private readonly UpscalerService _service;
        private CancellationTokenSource? _cts;
        public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();
        public string OutputFolder { get; set; } = string.Empty;
        public int SelectedScale { get; set; } = 2;
        public int[] ScaleOptions { get; } = new[] { 2, 3, 4 };

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; RaisePropertyChanged(); ((AsyncRelayCommand)UpscaleCommand).RaiseCanExecuteChanged(); ((RelayCommand)CancelCommand).RaiseCanExecuteChanged(); } }

        private string _log = string.Empty;
        public string Log { get => _log; set { _log = value; RaisePropertyChanged(); } }

        public ICommand AddFilesCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand UpscaleCommand { get; }
        public ICommand CancelCommand { get; }

        public UpscalerViewModel(UpscalerService? svc = null)
        {
            _service = svc ?? new UpscalerService();
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            UpscaleCommand = new AsyncRelayCommand(UpscaleAsync, () => !IsRunning);
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
        }

        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Video Files|*.mp4;*.mkv;*.mov;*.avi;*.ts|All Files|*.*" };
            if (dlg.ShowDialog() == true) foreach (var f in dlg.FileNames) Files.Add(f);
        }

        private void BrowseOutput()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK) { OutputFolder = dlg.SelectedPath; RaisePropertyChanged(nameof(OutputFolder)); }
        }

        private void Cancel()
        {
            _cts?.Cancel();
            Log += "Cancellation requested...\n";
        }

        public virtual async Task UpscaleAsync()
        {
            if (!Files.Any()) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
            Log = string.Empty;

            foreach (var src in Files.ToList())
            {
                var outName = System.IO.Path.GetFileNameWithoutExtension(src) + $"_x{SelectedScale}.mp4";
                var dest = string.IsNullOrWhiteSpace(OutputFolder) ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src) ?? string.Empty, outName) : System.IO.Path.Combine(OutputFolder, outName);
                var progress = new Progress<string>(s => Log += s + Environment.NewLine);
                try
                {
                    var res = await _service.RunAsync(src, dest, SelectedScale, progress, _cts.Token);
                    Log += $"Result: ExitCode={res.ExitCode}\n";
                }
                catch (OperationCanceledException) { Log += $"{src}: Cancelled\n"; break; }
                catch (Exception ex) { Log += $"Error: {ex.Message}\n"; }
            }

            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }
}