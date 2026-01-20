using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class ImageConverterViewModel : BindableBase
    {
        private readonly Func<string, string, int?, int?, long, SvgConversionMode, Task<bool>> _converter;
        private CancellationTokenSource? _cts;

        public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> OutputFormats { get; } = new ObservableCollection<string> { "JPG", "PNG", "BMP", "GIF", "TIFF", "WebP", "SVG" };
        public ObservableCollection<string> SvgModes { get; } = new ObservableCollection<string> { "Embed Raster", "Trace (Vectorize)" };
        
        private string _selectedOutputFormat = "JPG";
        public string SelectedOutputFormat 
        { 
            get => _selectedOutputFormat; 
            set 
            { 
                _selectedOutputFormat = value; 
                RaisePropertyChanged(); 
                RaisePropertyChanged(nameof(IsSvgFormat));
            } 
        }

        private string _selectedSvgMode = "Embed Raster";
        public string SelectedSvgMode { get => _selectedSvgMode; set { _selectedSvgMode = value; RaisePropertyChanged(); } }

        public bool IsSvgFormat => SelectedOutputFormat?.ToUpper() == "SVG";
        
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
        public ICommand ClearFilesCommand { get; }
        public ICommand RemoveSelectedFileCommand { get; }

        public ImageConverterViewModel(Func<string, string, int?, int?, long, SvgConversionMode, Task<bool>>? converter = null)
        {
            try { SimpleLogger.Info("ImageConverterViewModel constructed"); } catch {}
            _converter = converter ?? DefaultConverterAsync;
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            ConvertCommand = new AsyncRelayCommand(ConvertAsync, () => !IsRunning && Files.Any());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
            ClearFilesCommand = new RelayCommand(_ => { Files.Clear(); ((AsyncRelayCommand)ConvertCommand).RaiseCanExecuteChanged(); });
            RemoveSelectedFileCommand = new RelayCommand(p => { if (p is string file) { Files.Remove(file); ((AsyncRelayCommand)ConvertCommand).RaiseCanExecuteChanged(); } });
        }

        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|All Files|*.*" };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames) Files.Add(f);
                ((AsyncRelayCommand)ConvertCommand).RaiseCanExecuteChanged();
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

        private string GetOutputExtension()
        {
            return SelectedOutputFormat.ToLower() switch
            {
                "jpg" => ".jpg",
                "jpeg" => ".jpg",
                "png" => ".png",
                "bmp" => ".bmp",
                "gif" => ".gif",
                "tiff" => ".tiff",
                "webp" => ".webp",
                "svg" => ".svg",
                _ => ".jpg"
            };
        }

        private SvgConversionMode GetSvgMode()
        {
            return SelectedSvgMode switch
            {
                "Trace (Vectorize)" => SvgConversionMode.Trace,
                _ => SvgConversionMode.EmbedRaster
            };
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
                var extension = GetOutputExtension();
                var svgMode = GetSvgMode();

                foreach (var src in Files.ToList())
                {
                    if (_cts?.Token.IsCancellationRequested == true)
                    {
                        Log += "Operation cancelled.\n";
                        break;
                    }
                    var destName = System.IO.Path.GetFileNameWithoutExtension(src) + extension;
                    var dest = string.IsNullOrWhiteSpace(OutputFolder) ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src) ?? string.Empty, destName) : System.IO.Path.Combine(OutputFolder, destName);
                    bool ok = false;
                    string? errorMsg = null;
                    try
                    {
                        ok = await _converter(src, dest, MaxWidth, MaxHeight, JpegQuality, svgMode);
                        if (!ok)
                        {
                            errorMsg = "Conversion returned false - check if source file exists and is valid";
                        }
                    }
                    catch (Exception ex) 
                    { 
                        errorMsg = ex.Message;
                    }
                    
                    if (ok) 
                    {
                        Log += $"✓ {System.IO.Path.GetFileName(src)} -> {System.IO.Path.GetFileName(dest)}\n";
                    }
                    else 
                    {
                        Log += $"✗ {System.IO.Path.GetFileName(src)}: Failed{(errorMsg != null ? $" - {errorMsg}" : "")}\n";
                    }
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

        private static Task<bool> DefaultConverterAsync(string src, string dest, int? mw, int? mh, long q, SvgConversionMode svgMode)
        {
            return Task.Run(() => PlatypusTools.Core.Services.ImageConversionService.ConvertImage(src, dest, mw, mh, q, svgMode));
        }
    }
}