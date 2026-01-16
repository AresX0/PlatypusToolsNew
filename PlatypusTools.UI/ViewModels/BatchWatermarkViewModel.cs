using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Batch Watermark tool.
    /// </summary>
    public class BatchWatermarkViewModel : BindableBase
    {
        private readonly WatermarkService _service;
        private CancellationTokenSource? _cts;
        
        public BatchWatermarkViewModel()
        {
            _service = new WatermarkService();
            _service.ProgressChanged += OnProgressChanged;
            
            InputFiles = new ObservableCollection<WatermarkFileItem>();
            Positions = new ObservableCollection<string>
            {
                "Top Left", "Top Center", "Top Right",
                "Middle Left", "Center", "Middle Right",
                "Bottom Left", "Bottom Center", "Bottom Right",
                "Tiled"
            };
            SelectedPosition = "Bottom Right";
            
            // Commands
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            AddFolderCommand = new RelayCommand(_ => AddFolder());
            RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedFile != null);
            ClearFilesCommand = new RelayCommand(_ => ClearFiles(), _ => InputFiles.Any());
            BrowseWatermarkImageCommand = new RelayCommand(_ => BrowseWatermarkImage());
            ApplyWatermarkCommand = new AsyncRelayCommand(ApplyWatermarkAsync, () => InputFiles.Any() && CanApply());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsProcessing);
            PreviewCommand = new RelayCommand(_ => GeneratePreview(), _ => SelectedFile != null);
        }
        
        #region Properties
        
        public ObservableCollection<WatermarkFileItem> InputFiles { get; }
        public ObservableCollection<string> Positions { get; }
        
        private WatermarkFileItem? _selectedFile;
        public WatermarkFileItem? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (SetProperty(ref _selectedFile, value))
                {
                    GeneratePreview();
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private bool _isTextWatermark = true;
        public bool IsTextWatermark
        {
            get => _isTextWatermark;
            set
            {
                if (SetProperty(ref _isTextWatermark, value))
                {
                    RaisePropertyChanged(nameof(IsImageWatermark));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        public bool IsImageWatermark
        {
            get => !_isTextWatermark;
            set => IsTextWatermark = !value;
        }
        
        private string _watermarkText = "© Copyright";
        public string WatermarkText
        {
            get => _watermarkText;
            set
            {
                if (SetProperty(ref _watermarkText, value))
                {
                    GeneratePreview();
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private string _fontFamily = "Arial";
        public string FontFamily
        {
            get => _fontFamily;
            set
            {
                if (SetProperty(ref _fontFamily, value))
                    GeneratePreview();
            }
        }
        
        private double _fontSize = 24;
        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (SetProperty(ref _fontSize, value))
                    GeneratePreview();
            }
        }
        
        private System.Windows.Media.Color _textColor = Colors.White;
        public System.Windows.Media.Color TextColor
        {
            get => _textColor;
            set
            {
                if (SetProperty(ref _textColor, value))
                    GeneratePreview();
            }
        }
        
        private double _opacity = 0.5;
        public double Opacity
        {
            get => _opacity;
            set
            {
                if (SetProperty(ref _opacity, value))
                    GeneratePreview();
            }
        }
        
        private string _selectedPosition = "Bottom Right";
        public string SelectedPosition
        {
            get => _selectedPosition;
            set
            {
                if (SetProperty(ref _selectedPosition, value))
                    GeneratePreview();
            }
        }
        
        private double _rotation = 0;
        public double Rotation
        {
            get => _rotation;
            set
            {
                if (SetProperty(ref _rotation, value))
                    GeneratePreview();
            }
        }
        
        private string? _watermarkImagePath;
        public string? WatermarkImagePath
        {
            get => _watermarkImagePath;
            set
            {
                if (SetProperty(ref _watermarkImagePath, value))
                {
                    GeneratePreview();
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private double _imageScale = 0.2;
        public double ImageScale
        {
            get => _imageScale;
            set
            {
                if (SetProperty(ref _imageScale, value))
                    GeneratePreview();
            }
        }
        
        private ImageSource? _previewImage;
        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }
        
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                    RaiseCommandsCanExecuteChanged();
            }
        }
        
        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }
        
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        private string _outputDirectory = string.Empty;
        public string OutputDirectory
        {
            get => _outputDirectory;
            set => SetProperty(ref _outputDirectory, value);
        }
        
        #endregion
        
        #region Commands
        
        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand BrowseWatermarkImageCommand { get; }
        public ICommand ApplyWatermarkCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand PreviewCommand { get; }
        
        #endregion
        
        #region Methods
        
        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files (*.*)|*.*",
                Multiselect = true,
                Title = "Select Images"
            };
            
            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    AddFileToList(file);
                }
            }
        }
        
        private void AddFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing images"
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                var files = Directory.GetFiles(dialog.SelectedPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                
                foreach (var file in files)
                {
                    AddFileToList(file);
                }
                
                // Set output directory to a subfolder
                if (string.IsNullOrEmpty(OutputDirectory))
                {
                    OutputDirectory = Path.Combine(dialog.SelectedPath, "watermarked");
                }
            }
        }
        
        private void AddFileToList(string filePath)
        {
            if (InputFiles.Any(f => f.FilePath == filePath)) return;
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                InputFiles.Add(new WatermarkFileItem
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length
                });
                
                RaiseCommandsCanExecuteChanged();
            }
            catch { }
        }
        
        private void RemoveSelected()
        {
            if (SelectedFile != null)
            {
                InputFiles.Remove(SelectedFile);
                SelectedFile = InputFiles.FirstOrDefault();
                RaiseCommandsCanExecuteChanged();
            }
        }
        
        private void ClearFiles()
        {
            InputFiles.Clear();
            SelectedFile = null;
            PreviewImage = null;
            RaiseCommandsCanExecuteChanged();
        }
        
        private void BrowseWatermarkImage()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                Title = "Select Watermark Image"
            };
            
            if (dialog.ShowDialog() == true)
            {
                WatermarkImagePath = dialog.FileName;
            }
        }
        
        private bool CanApply()
        {
            if (IsTextWatermark)
                return !string.IsNullOrWhiteSpace(WatermarkText);
            else
                return !string.IsNullOrEmpty(WatermarkImagePath) && File.Exists(WatermarkImagePath);
        }
        
        private WatermarkPosition ParsePosition(string position)
        {
            return position switch
            {
                "Top Left" => WatermarkPosition.TopLeft,
                "Top Center" => WatermarkPosition.TopCenter,
                "Top Right" => WatermarkPosition.TopRight,
                "Middle Left" => WatermarkPosition.MiddleLeft,
                "Center" => WatermarkPosition.Center,
                "Middle Right" => WatermarkPosition.MiddleRight,
                "Bottom Left" => WatermarkPosition.BottomLeft,
                "Bottom Center" => WatermarkPosition.BottomCenter,
                "Bottom Right" => WatermarkPosition.BottomRight,
                "Tiled" => WatermarkPosition.Tiled,
                _ => WatermarkPosition.BottomRight
            };
        }
        
        private async Task ApplyWatermarkAsync()
        {
            if (!InputFiles.Any()) return;
            
            // Get output directory
            if (string.IsNullOrEmpty(OutputDirectory))
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select output folder"
                };
                
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;
                
                OutputDirectory = dialog.SelectedPath;
            }
            
            IsProcessing = true;
            Progress = 0;
            StatusMessage = "Processing...";
            _cts = new CancellationTokenSource();
            
            try
            {
                var inputPaths = InputFiles.Select(f => f.FilePath).ToList();
                
                if (IsTextWatermark)
                {
                    var options = new TextWatermarkOptions
                    {
                        Text = WatermarkText,
                        FontFamily = FontFamily,
                        FontSize = (float)FontSize,
                        TextColor = System.Drawing.Color.FromArgb(TextColor.A, TextColor.R, TextColor.G, TextColor.B),
                        Opacity = Opacity,
                        Position = ParsePosition(SelectedPosition),
                        Rotation = (float)Rotation
                    };
                    
                    await _service.BatchAddTextWatermarkAsync(inputPaths, OutputDirectory, options, "_watermarked", _cts.Token);
                }
                else
                {
                    var options = new ImageWatermarkOptions
                    {
                        Opacity = Opacity,
                        Scale = ImageScale,
                        Position = ParsePosition(SelectedPosition)
                    };
                    
                    await _service.BatchAddImageWatermarkAsync(inputPaths, OutputDirectory, WatermarkImagePath!, options, "_watermarked", _cts.Token);
                }
                
                StatusMessage = $"Completed! {InputFiles.Count} files processed.";
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
                IsProcessing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
        
        private void Cancel()
        {
            _cts?.Cancel();
        }
        
        private CancellationTokenSource? _previewCts;
        
        private async void GeneratePreview()
        {
            if (SelectedFile == null || !File.Exists(SelectedFile.FilePath))
            {
                PreviewImage = null;
                return;
            }
            
            // Cancel any previous preview generation
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            var ct = _previewCts.Token;
            
            try
            {
                var filePath = SelectedFile.FilePath;
                
                // Generate preview on background thread
                var previewBitmap = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    
                    string tempPath = Path.Combine(Path.GetTempPath(), $"watermark_preview_{Guid.NewGuid()}.png");
                    
                    try
                    {
                        if (IsTextWatermark)
                        {
                            var options = new TextWatermarkOptions
                            {
                                Text = WatermarkText,
                                FontFamily = FontFamily,
                                FontSize = (float)FontSize,
                                TextColor = System.Drawing.Color.FromArgb(TextColor.A, TextColor.R, TextColor.G, TextColor.B),
                                Opacity = Opacity,
                                Position = ParsePosition(SelectedPosition),
                                Rotation = (float)Rotation
                            };
                            
                            if (options.Position == WatermarkPosition.Tiled)
                                _service.AddTiledWatermark(filePath, tempPath, options);
                            else
                                _service.AddTextWatermark(filePath, tempPath, options);
                        }
                        else if (!string.IsNullOrEmpty(WatermarkImagePath) && File.Exists(WatermarkImagePath))
                        {
                            var options = new ImageWatermarkOptions
                            {
                                Opacity = Opacity,
                                Scale = ImageScale,
                                Position = ParsePosition(SelectedPosition)
                            };
                            
                            _service.AddImageWatermark(filePath, tempPath, WatermarkImagePath, options);
                        }
                        else
                        {
                            // Just show original image - use stream to avoid file lock
                            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = fs;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            return bitmap;
                        }
                        
                        ct.ThrowIfCancellationRequested();
                        
                        // Load preview from temp file
                        using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var result = new BitmapImage();
                        result.BeginInit();
                        result.StreamSource = stream;
                        result.CacheOption = BitmapCacheOption.OnLoad;
                        result.EndInit();
                        result.Freeze();
                        return result;
                    }
                    finally
                    {
                        // Clean up temp file
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    }
                }, ct);
                
                if (!ct.IsCancellationRequested)
                {
                    PreviewImage = previewBitmap;
                }
            }
            catch (OperationCanceledException)
            {
                // Preview was cancelled, ignore
            }
            catch (Exception ex)
            {
                StatusMessage = $"Preview error: {ex.Message}";
            }
        }
        
        private void OnProgressChanged(object? sender, WatermarkProgressEventArgs e)
        {
            Progress = e.ProgressPercentage;
            StatusMessage = e.Message;
        }
        
        private void RaiseCommandsCanExecuteChanged()
        {
            (ApplyWatermarkCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
        
        #endregion
    }
    
    /// <summary>
    /// Represents an image file in the watermark list.
    /// </summary>
    public class WatermarkFileItem : BindableBase
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        
        public string FileSizeFormatted => FileSize < 1024 * 1024
            ? $"{FileSize / 1024.0:F1} KB"
            : $"{FileSize / (1024.0 * 1024.0):F1} MB";
    }
}
