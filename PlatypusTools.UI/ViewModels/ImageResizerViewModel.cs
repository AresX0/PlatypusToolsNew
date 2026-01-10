using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Drawing.Imaging;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels;

public class ImageResizerViewModel : BindableBase
{
    private readonly ImageResizerService _imageResizerService;
    private string _outputFolder = string.Empty;
    private int _maxWidth = 1920;
    private int _maxHeight = 1080;
    private int _quality = 90;
    private string _selectedFormat = "Same";
    private bool _overwriteExisting;
    private bool _maintainAspectRatio = true;
    private string _statusMessage = "Ready";
    private bool _isResizing;
    private CancellationTokenSource? _cancellationTokenSource;

    public ImageResizerViewModel() : this(new ImageResizerService())
    {
    }

    public ImageResizerViewModel(ImageResizerService imageResizerService)
    {
        _imageResizerService = imageResizerService;
        
        AddFilesCommand = new RelayCommand(_ => AddFiles());
        ClearListCommand = new RelayCommand(_ => ClearList(), _ => FileList.Any());
        ResizeImagesCommand = new AsyncRelayCommand(ResizeImagesAsync, () => FileList.Any() && !IsResizing);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsResizing);
        SelectAllCommand = new RelayCommand(_ => SetAllChecked(true));
        SelectNoneCommand = new RelayCommand(_ => SetAllChecked(false));
        BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
    }

    public ObservableCollection<ResizableImage> FileList { get; } = new();

    public string OutputFolder
    {
        get => _outputFolder;
        set { _outputFolder = value; RaisePropertyChanged(); }
    }

    public int MaxWidth
    {
        get => _maxWidth;
        set { _maxWidth = value; RaisePropertyChanged(); }
    }

    public int MaxHeight
    {
        get => _maxHeight;
        set { _maxHeight = value; RaisePropertyChanged(); }
    }

    public int Quality
    {
        get => _quality;
        set { _quality = value; RaisePropertyChanged(); }
    }

    public string SelectedFormat
    {
        get => _selectedFormat;
        set { _selectedFormat = value; RaisePropertyChanged(); }
    }

    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set { _overwriteExisting = value; RaisePropertyChanged(); }
    }

    public bool MaintainAspectRatio
    {
        get => _maintainAspectRatio;
        set { _maintainAspectRatio = value; RaisePropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; RaisePropertyChanged(); }
    }

    public bool IsResizing
    {
        get => _isResizing;
        private set
        {
            _isResizing = value;
            RaisePropertyChanged();
            ((AsyncRelayCommand)ResizeImagesCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ClearListCommand).RaiseCanExecuteChanged();
        }
    }

    public ICommand AddFilesCommand { get; }
    public ICommand ClearListCommand { get; }
    public ICommand ResizeImagesCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand BrowseOutputFolderCommand { get; }

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Image Files",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All Files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            foreach (var file in dialog.FileNames)
            {
                if (!FileList.Any(f => f.Path == file))
                {
                    var fileInfo = new FileInfo(file);
                    try
                    {
                        using var img = System.Drawing.Image.FromFile(file);
                        FileList.Add(new ResizableImage
                        {
                            Name = Path.GetFileName(file),
                            Path = file,
                            Dimensions = $"{img.Width}x{img.Height}",
                            Size = FormatFileSize(fileInfo.Length),
                            Process = true
                        });
                    }
                    catch
                    {
                        // If we can't load it as an image, skip it
                    }
                }
            }
            
            ((RelayCommand)ClearListCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ResizeImagesCommand).RaiseCanExecuteChanged();
            StatusMessage = $"{FileList.Count} files ready";
        }
    }

    private void ClearList()
    {
        FileList.Clear();
        StatusMessage = "Ready";
        ((RelayCommand)ClearListCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)ResizeImagesCommand).RaiseCanExecuteChanged();
    }

    private void SetAllChecked(bool isChecked)
    {
        foreach (var file in FileList)
        {
            file.Process = isChecked;
        }
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Output Folder",
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            OutputFolder = dialog.SelectedPath;
        }
    }

    private async Task ResizeImagesAsync()
    {
        var filesToProcess = FileList.Where(f => f.Process).ToList();
        if (!filesToProcess.Any())
        {
            StatusMessage = "No files selected for processing";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            StatusMessage = "Please select an output folder";
            return;
        }

        if (!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder);
        }

        // Determine target format and extension
        ImageFormat? targetFormat = null;
        string? targetExtension = null;

        if (SelectedFormat != "Same")
        {
            (targetFormat, targetExtension) = SelectedFormat switch
            {
                "JPG" => (ImageFormat.Jpeg, ".jpg"),
                "PNG" => (ImageFormat.Png, ".png"),
                "BMP" => (ImageFormat.Bmp, ".bmp"),
                "GIF" => (ImageFormat.Gif, ".gif"),
                "TIFF" => (ImageFormat.Tiff, ".tiff"),
                _ => (null, null)
            };
        }

        IsResizing = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ResizeProgress>(p =>
            {
                StatusMessage = p.Message;
            });

            var filePaths = filesToProcess.Select(f => f.Path).ToList();
            var result = await _imageResizerService.BatchResizeAsync(
                filePaths,
                OutputFolder,
                MaxWidth,
                MaxHeight,
                Quality,
                targetFormat,
                targetExtension,
                MaintainAspectRatio,
                OverwriteExisting,
                progress,
                _cancellationTokenSource.Token);

            StatusMessage = $"Completed: {result.SuccessCount} successful, {result.FailureCount} failed";

            // Update file statuses
            foreach (var file in filesToProcess)
            {
                var resizeResult = result.Results.FirstOrDefault(r => 
                    r.OutputPath?.Contains(Path.GetFileNameWithoutExtension(file.Path)) == true);
                
                if (resizeResult != null && resizeResult.IsSuccess)
                {
                    file.Status = $"✓ {resizeResult.Width}x{resizeResult.Height} ({FormatFileSize(resizeResult.FileSize)})";
                }
                else if (resizeResult != null)
                {
                    file.Status = $"✗ {resizeResult.Message}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Resize cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsResizing = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public class ResizableImage : BindableBase
{
    private bool _process;
    private string _status = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Dimensions { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    
    public bool Process
    {
        get => _process;
        set { _process = value; RaisePropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; RaisePropertyChanged(); }
    }
}
