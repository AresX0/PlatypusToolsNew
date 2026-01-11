using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class ImageConverterViewModel : BindableBase
    {
        private readonly Func<string, string, int?, int?, long, Task<bool>> _converter;
        private CancellationTokenSource? _cts;

        public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();
        public string OutputFolder { get; set; } = string.Empty;
        public int? MaxWidth { get; set; }
        public int? MaxHeight { get; set; }
        public int JpegQuality { get; set; } = 85;

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; RaisePropertyChanged(); ((AsyncRelayCommand)ConvertCommand).RaiseCanExecuteChanged(); ((RelayCommand?)CancelCommand)?.RaiseCanExecuteChanged(); } }

        private double _progress;
        public double Progress { get => _progress; set { _progress = value; RaisePropertyChanged(); } }

        private string _log = string.Empty;
        public string Log { get => _log; set { _log = value; RaisePropertyChanged(); } }

        public ICommand AddFilesCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand ConvertCommand { get; }
        public ICommand CancelCommand { get; }

        public ImageConverterViewModel(Func<string, string, int?, int?, long, Task<bool>>? converter = null)
        {
            _converter = converter ?? DefaultConverterAsync;
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => !IsRunning);
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
        }

        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All Files|*.*" };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames) Files.Add(f);
            }
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

        public virtual async Task ConvertAsync()
        {
            if (!Files.Any()) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
            try
            {
                Log = string.Empty;
                Progress = 0;

                int total = Files.Count;
                int done = 0;

                foreach (var src in Files.ToList())
                {
                    if (_cts?.Token.IsCancellationRequested == true)
                    {
                        Log += "Operation cancelled.\n";
                        break;
                    }
                    var destName = System.IO.Path.GetFileNameWithoutExtension(src) + ".jpg";
                    var dest = string.IsNullOrWhiteSpace(OutputFolder) ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src) ?? string.Empty, destName) : System.IO.Path.Combine(OutputFolder, destName);
                    bool ok = false;
                    try
                    {
                        ok = await _converter(src, dest, MaxWidth, MaxHeight, JpegQuality);
                    }
                    catch (Exception ex) { Log += $"{src}: ERROR {ex.Message}\n"; }
                    if (ok) Log += $"{src}: Converted -> {dest}\n";
                    else Log += $"{src}: Failed\n";
                    done++;
                    Progress = (double)done / total * 100.0;
                }
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsRunning = false;
            }
        }

        private static Task<bool> DefaultConverterAsync(string src, string dest, int? mw, int? mh, long q)
        {
            return Task.Run(() => PlatypusTools.Core.Services.ImageConversionService.ConvertImage(src, dest, mw, mh, q));
        }
    }
}