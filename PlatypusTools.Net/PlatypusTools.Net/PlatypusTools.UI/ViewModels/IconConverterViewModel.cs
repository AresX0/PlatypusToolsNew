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

public class IconConverterViewModel : BindableBase
{
    private readonly IconConverterService _iconConverterService;
    private string _imagesFolder = string.Empty;
    private string _outputFolder = string.Empty;
    private string _outputName = string.Empty;
    private int _iconSize = 256;
    private bool _overwriteExisting;
    private string _selectedFormat = "PNG";
    private string _statusMessage = "Ready";
    private bool _isConverting;
    private CancellationTokenSource? _cancellationTokenSource;

    public IconConverterViewModel() : this(new IconConverterService())
    {
    }

    public IconConverterViewModel(IconConverterService iconConverterService)
    {
        _iconConverterService = iconConverterService;
        
        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        AddFilesCommand = new RelayCommand(_ => AddFiles());
        AddFolderCommand = new RelayCommand(_ => AddFolder());
        ClearListCommand = new RelayCommand(_ => ClearList(), _ => FileList.Any());
        ConvertToIcoCommand = new AsyncRelayCommand(ConvertToIcoAsync, () => FileList.Any() && !IsConverting);
        ConvertFormatCommand = new AsyncRelayCommand(ConvertFormatAsync, () => FileList.Any() && !IsConverting);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsConverting);
        SelectAllCommand = new RelayCommand(_ => SetAllChecked(true));
        SelectNoneCommand = new RelayCommand(_ => SetAllChecked(false));
    }

    public ObservableCollection<ConvertibleFile> FileList { get; } = new();

    public string ImagesFolder
    {
        get => _imagesFolder;
        set { _imagesFolder = value; RaisePropertyChanged(); }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set { _outputFolder = value; RaisePropertyChanged(); }
    }

    public string OutputName
    {
        get => _outputName;
        set { _outputName = value; RaisePropertyChanged(); }
    }

    public int IconSize
    {
        get => _iconSize;
        set { _iconSize = value; RaisePropertyChanged(); }
    }

    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set { _overwriteExisting = value; RaisePropertyChanged(); }
    }

    public string SelectedFormat
    {
        get => _selectedFormat;
        set { _selectedFormat = value; RaisePropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; RaisePropertyChanged(); }
    }

    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            _isConverting = value;
            RaisePropertyChanged();
            ((AsyncRelayCommand)ConvertToIcoCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ConvertFormatCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ClearListCommand).RaiseCanExecuteChanged();
        }
    }

    public ICommand BrowseFolderCommand { get; }
    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand ClearListCommand { get; }
    public ICommand ConvertToIcoCommand { get; }
    public ICommand ConvertFormatCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Images Folder",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            ImagesFolder = dialog.SelectedPath;
        }
    }

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Image Files",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico|All Files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            foreach (var file in dialog.FileNames)
            {
                if (!FileList.Any(f => f.Path == file))
                {
                    FileList.Add(new ConvertibleFile
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        Convert = true
                    });
                }
            }
            
            ((RelayCommand)ClearListCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ConvertToIcoCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ConvertFormatCommand).RaiseCanExecuteChanged();
            StatusMessage = $"{FileList.Count} files ready";
        }
    }

    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Folder Containing Images",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico" };
            var files = Directory.GetFiles(dialog.SelectedPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            foreach (var file in files)
            {
                if (!FileList.Any(f => f.Path == file))
                {
                    FileList.Add(new ConvertibleFile
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        Convert = true
                    });
                }
            }

            ((RelayCommand)ClearListCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ConvertToIcoCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ConvertFormatCommand).RaiseCanExecuteChanged();
            StatusMessage = $"{FileList.Count} files ready";
        }
    }

    private void ClearList()
    {
        FileList.Clear();
        StatusMessage = "Ready";
        ((RelayCommand)ClearListCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)ConvertToIcoCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)ConvertFormatCommand).RaiseCanExecuteChanged();
    }

    private void SetAllChecked(bool isChecked)
    {
        foreach (var file in FileList)
        {
            file.Convert = isChecked;
        }
    }

    private async Task ConvertToIcoAsync()
    {
        var filesToConvert = FileList.Where(f => f.Convert).ToList();
        if (!filesToConvert.Any())
        {
            StatusMessage = "No files selected for conversion";
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

        IsConverting = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ConversionProgress>(p =>
            {
                StatusMessage = p.Message;
            });

            var filePaths = filesToConvert.Select(f => f.Path).ToList();
            var result = await _iconConverterService.BatchConvertToIcoAsync(
                filePaths,
                OutputFolder,
                IconSize,
                OverwriteExisting,
                progress,
                _cancellationTokenSource.Token);

            StatusMessage = $"Completed: {result.SuccessCount} successful, {result.FailureCount} failed";

            // Update file statuses
            foreach (var file in filesToConvert)
            {
                var conversionResult = result.Results.FirstOrDefault(r => 
                    r.OutputPath?.Contains(Path.GetFileNameWithoutExtension(file.Path)) == true);
                
                if (conversionResult != null)
                {
                    file.Status = conversionResult.IsSuccess ? "✓ Success" : $"✗ {conversionResult.Message}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Conversion cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task ConvertFormatAsync()
    {
        var filesToConvert = FileList.Where(f => f.Convert).ToList();
        if (!filesToConvert.Any())
        {
            StatusMessage = "No files selected for conversion";
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

        // Map selected format to ImageFormat
        var (imageFormat, extension) = SelectedFormat switch
        {
            "PNG" => (ImageFormat.Png, "png"),
            "JPG" => (ImageFormat.Jpeg, "jpg"),
            "BMP" => (ImageFormat.Bmp, "bmp"),
            "GIF" => (ImageFormat.Gif, "gif"),
            "TIFF" => (ImageFormat.Tiff, "tiff"),
            _ => (ImageFormat.Png, "png")
        };

        IsConverting = true;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ConversionProgress>(p =>
            {
                StatusMessage = p.Message;
            });

            var filePaths = filesToConvert.Select(f => f.Path).ToList();
            var result = await _iconConverterService.BatchConvertFormatAsync(
                filePaths,
                OutputFolder,
                imageFormat,
                extension,
                OverwriteExisting,
                progress,
                _cancellationTokenSource.Token);

            StatusMessage = $"Completed: {result.SuccessCount} successful, {result.FailureCount} failed";

            // Update file statuses
            foreach (var file in filesToConvert)
            {
                var conversionResult = result.Results.FirstOrDefault(r => 
                    r.OutputPath?.Contains(Path.GetFileNameWithoutExtension(file.Path)) == true);
                
                if (conversionResult != null)
                {
                    file.Status = conversionResult.IsSuccess ? "✓ Success" : $"✗ {conversionResult.Message}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Conversion cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }
}

public class ConvertibleFile : BindableBase
{
    private bool _convert;
    private string _status = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    
    public bool Convert
    {
        get => _convert;
        set { _convert = value; RaisePropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; RaisePropertyChanged(); }
    }
}
