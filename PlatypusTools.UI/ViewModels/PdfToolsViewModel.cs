using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PlatypusTools.Core.Services;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the PDF Tools.
    /// </summary>
    public class PdfToolsViewModel : BindableBase
    {
        private readonly PdfService _service;
        private CancellationTokenSource? _cts;
        private PdfDocument? _currentPdfDocument;
        
        public PdfToolsViewModel()
        {
            _service = Services.ServiceLocator.PdfTools;
            _service.ProgressChanged += OnProgressChanged;
            
            InputFiles = new ObservableCollection<PdfFileItem>();
            PageList = new ObservableCollection<PdfPageItem>();
            
            // Commands
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedFile != null);
            ClearFilesCommand = new RelayCommand(_ => ClearFiles(), _ => InputFiles.Any());
            MoveUpCommand = new RelayCommand(_ => MoveUp(), _ => CanMoveUp());
            MoveDownCommand = new RelayCommand(_ => MoveDown(), _ => CanMoveDown());
            
            MergeCommand = new AsyncRelayCommand(MergeAsync, () => InputFiles.Count > 1);
            SplitCommand = new AsyncRelayCommand(SplitAsync, () => SelectedFile != null);
            ExtractPagesCommand = new AsyncRelayCommand(ExtractPagesAsync, () => SelectedFile != null && !string.IsNullOrEmpty(PageRanges));
            RotateCommand = new AsyncRelayCommand(RotateAsync, () => SelectedFile != null);
            DeletePagesCommand = new AsyncRelayCommand(DeletePagesAsync, () => SelectedFile != null && !string.IsNullOrEmpty(PageRanges));
            WatermarkCommand = new AsyncRelayCommand(WatermarkAsync, () => SelectedFile != null && !string.IsNullOrEmpty(WatermarkText));
            EncryptCommand = new AsyncRelayCommand(EncryptAsync, () => SelectedFile != null);
            DecryptCommand = new AsyncRelayCommand(DecryptAsync, () => SelectedFile != null);
            ImagesToPdfCommand = new AsyncRelayCommand(ImagesToPdfAsync, () => InputFiles.Any());
            
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsProcessing);
            
            // Preview commands
            PreviousPageCommand = new RelayCommand(_ => PreviousPage(), _ => CurrentPreviewPage > 1);
            NextPageCommand = new RelayCommand(_ => NextPage(), _ => CurrentPreviewPage < TotalPreviewPages);
        }
        
        #region Properties
        
        public ObservableCollection<PdfFileItem> InputFiles { get; }
        public ObservableCollection<PdfPageItem> PageList { get; }
        
        private PdfFileItem? _selectedFile;
        public PdfFileItem? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (SetProperty(ref _selectedFile, value))
                {
                    LoadPageList();
                    RaiseCommandsCanExecuteChanged();
                    _ = LoadPreviewAsync();
                }
            }
        }
        
        private BitmapImage? _previewImage;
        public BitmapImage? PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }
        
        private bool _isLoadingPreview;
        public bool IsLoadingPreview
        {
            get => _isLoadingPreview;
            set => SetProperty(ref _isLoadingPreview, value);
        }
        
        private int _currentPreviewPage = 1;
        public int CurrentPreviewPage
        {
            get => _currentPreviewPage;
            set
            {
                if (SetProperty(ref _currentPreviewPage, value))
                {
                    _ = LoadPreviewAsync();
                }
            }
        }
        
        private int _totalPreviewPages = 1;
        public int TotalPreviewPages
        {
            get => _totalPreviewPages;
            set => SetProperty(ref _totalPreviewPages, value);
        }
        
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
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
        
        private string _pageRanges = string.Empty;
        public string PageRanges
        {
            get => _pageRanges;
            set
            {
                if (SetProperty(ref _pageRanges, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private int _rotationDegrees = 90;
        public int RotationDegrees
        {
            get => _rotationDegrees;
            set => SetProperty(ref _rotationDegrees, value);
        }
        
        private string _watermarkText = string.Empty;
        public string WatermarkText
        {
            get => _watermarkText;
            set
            {
                if (SetProperty(ref _watermarkText, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private double _watermarkOpacity = 0.3;
        public double WatermarkOpacity
        {
            get => _watermarkOpacity;
            set => SetProperty(ref _watermarkOpacity, value);
        }
        
        private double _watermarkFontSize = 48;
        public double WatermarkFontSize
        {
            get => _watermarkFontSize;
            set => SetProperty(ref _watermarkFontSize, value);
        }
        
        #endregion
        
        #region Commands
        
        public ICommand AddFilesCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        
        public ICommand MergeCommand { get; }
        public ICommand SplitCommand { get; }
        public ICommand ExtractPagesCommand { get; }
        public ICommand RotateCommand { get; }
        public ICommand DeletePagesCommand { get; }
        public ICommand WatermarkCommand { get; }
        public ICommand EncryptCommand { get; }
        public ICommand DecryptCommand { get; }
        public ICommand ImagesToPdfCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        
        #endregion
        
        #region Methods
        
        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf|Image Files (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files (*.*)|*.*",
                Multiselect = true,
                Title = "Select PDF or Image Files"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var files = dialog.FileNames.ToList();
                var dispatcher = System.Windows.Application.Current.Dispatcher;
                
                IsProcessing = true;
                StatusMessage = $"Loading {files.Count} file(s)...";
                
                // Process all files on background thread to avoid UI freezing
                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var file in files)
                        {
                            var extension = Path.GetExtension(file).ToLowerInvariant();
                            var fileName = Path.GetFileName(file);
                            
                            await dispatcher.InvokeAsync(() => StatusMessage = $"Loading {fileName}...");
                            
                            if (extension == ".pdf")
                            {
                                // For PDFs, just add with basic info - don't try to parse
                                // PdfSharpCore can hang on some PDFs indefinitely
                                var fileInfo = new FileInfo(file);
                                await dispatcher.InvokeAsync(() =>
                                {
                                    InputFiles.Add(new PdfFileItem
                                    {
                                        FilePath = file,
                                        FileName = fileInfo.Name,
                                        PageCount = 0, // Will be determined when needed
                                        FileSize = fileInfo.Length,
                                        IsPdf = true
                                    });
                                    StatusMessage = $"Added {fileName}";
                                    RaiseCommandsCanExecuteChanged();
                                });
                            }
                            else
                            {
                                // Image file
                                var fileInfo = new FileInfo(file);
                                await dispatcher.InvokeAsync(() =>
                                {
                                    InputFiles.Add(new PdfFileItem
                                    {
                                        FilePath = file,
                                        FileName = fileInfo.Name,
                                        PageCount = 1,
                                        FileSize = fileInfo.Length,
                                        IsPdf = false
                                    });
                                    StatusMessage = $"Added {fileName}";
                                    RaiseCommandsCanExecuteChanged();
                                });
                            }
                        }
                        
                        await dispatcher.InvokeAsync(() =>
                        {
                            IsProcessing = false;
                            StatusMessage = $"Loaded {files.Count} file(s)";
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AddFiles Error: {ex}");
                        await dispatcher.InvokeAsync(() =>
                        {
                            IsProcessing = false;
                            StatusMessage = $"Error: {ex.Message}";
                        });
                    }
                });
            }
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
            PageList.Clear();
            SelectedFile = null;
            StatusMessage = "File list cleared";
            RaiseCommandsCanExecuteChanged();
        }
        
        private bool CanMoveUp() => SelectedFile != null && InputFiles.IndexOf(SelectedFile) > 0;
        private bool CanMoveDown() => SelectedFile != null && InputFiles.IndexOf(SelectedFile) < InputFiles.Count - 1;
        
        private void MoveUp()
        {
            if (SelectedFile == null) return;
            int index = InputFiles.IndexOf(SelectedFile);
            if (index > 0)
            {
                InputFiles.Move(index, index - 1);
                RaiseCommandsCanExecuteChanged();
            }
        }
        
        private void MoveDown()
        {
            if (SelectedFile == null) return;
            int index = InputFiles.IndexOf(SelectedFile);
            if (index < InputFiles.Count - 1)
            {
                InputFiles.Move(index, index + 1);
                RaiseCommandsCanExecuteChanged();
            }
        }
        
        private void LoadPageList()
        {
            PageList.Clear();
            if (SelectedFile?.IsPdf == true)
            {
                for (int i = 1; i <= SelectedFile.PageCount; i++)
                {
                    PageList.Add(new PdfPageItem { PageNumber = i });
                }
            }
        }
        
        private async Task MergeAsync()
        {
            var pdfFiles = InputFiles.Where(f => f.IsPdf).Select(f => f.FilePath).ToList();
            if (pdfFiles.Count < 2)
            {
                StatusMessage = "Need at least 2 PDF files to merge";
                return;
            }
            
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Merged PDF",
                FileName = "merged.pdf"
            };
            
            if (dialog.ShowDialog() == true)
            {
                await ExecuteWithProgress(async ct =>
                {
                    await _service.MergePdfsAsync(pdfFiles, dialog.FileName, ct);
                }, "Merging PDFs...");
            }
        }
        
        private async Task SplitAsync()
        {
            if (SelectedFile?.IsPdf != true) return;
            
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select output folder for split PDFs"
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await ExecuteWithProgress(async ct =>
                {
                    var files = await _service.SplitPdfAsync(SelectedFile.FilePath, dialog.SelectedPath, null, ct);
                    StatusMessage = $"Split into {files.Count} files";
                }, "Splitting PDF...");
            }
        }
        
        private async Task ExtractPagesAsync()
        {
            if (SelectedFile?.IsPdf != true || string.IsNullOrEmpty(PageRanges)) return;
            
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Extracted Pages",
                FileName = $"{Path.GetFileNameWithoutExtension(SelectedFile.FileName)}_extracted.pdf"
            };
            
            if (dialog.ShowDialog() == true)
            {
                // Use a large max page number since we don't know the actual count
                // The service will handle out-of-range pages gracefully
                var pageNumbers = ParsePageNumbers(PageRanges, 9999);
                if (pageNumbers.Count == 0)
                {
                    StatusMessage = "No valid page numbers specified";
                    return;
                }
                await ExecuteWithProgress(async ct =>
                {
                    await _service.ExtractPagesAsync(SelectedFile.FilePath, dialog.FileName, pageNumbers, ct);
                }, "Extracting pages...");
            }
        }
        
        private async Task RotateAsync()
        {
            if (SelectedFile?.IsPdf != true) return;
            
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Rotated PDF",
                FileName = $"{Path.GetFileNameWithoutExtension(SelectedFile.FileName)}_rotated.pdf"
            };
            
            if (dialog.ShowDialog() == true)
            {
                // Use a large max page number since we don't know the actual count
                List<int>? pages = string.IsNullOrEmpty(PageRanges) ? null : ParsePageNumbers(PageRanges, 9999);
                await ExecuteWithProgress(async ct =>
                {
                    await _service.RotatePagesAsync(SelectedFile.FilePath, dialog.FileName, RotationDegrees, pages, ct);
                }, "Rotating pages...");
            }
        }
        
        private async Task DeletePagesAsync()
        {
            if (SelectedFile?.IsPdf != true || string.IsNullOrEmpty(PageRanges)) return;
            
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save PDF with Deleted Pages",
                FileName = $"{Path.GetFileNameWithoutExtension(SelectedFile.FileName)}_modified.pdf"
            };
            
            if (dialog.ShowDialog() == true)
            {
                // Use a large max page number since we don't know the actual count
                var pageNumbers = ParsePageNumbers(PageRanges, 9999);
                if (pageNumbers.Count == 0)
                {
                    StatusMessage = "No valid page numbers specified";
                    return;
                }
                await ExecuteWithProgress(async ct =>
                {
                    await _service.DeletePagesAsync(SelectedFile.FilePath, dialog.FileName, pageNumbers, ct);
                }, "Deleting pages...");
            }
        }
        
        private async Task WatermarkAsync()
        {
            if (SelectedFile?.IsPdf != true || string.IsNullOrEmpty(WatermarkText)) return;
            
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Watermarked PDF",
                FileName = $"{Path.GetFileNameWithoutExtension(SelectedFile.FileName)}_watermarked.pdf"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var options = new PdfWatermarkOptions
                {
                    FontSize = WatermarkFontSize,
                    Opacity = WatermarkOpacity
                };
                
                await ExecuteWithProgress(async ct =>
                {
                    await _service.AddWatermarkAsync(SelectedFile.FilePath, dialog.FileName, WatermarkText, options, ct);
                }, "Adding watermark...");
            }
        }
        
        private async Task EncryptAsync()
        {
            if (SelectedFile?.IsPdf != true) return;
            
            // Show password dialog
            var passwordWindow = new Views.PromptPasswordWindow
            {
                Title = "Encrypt PDF - Set Password",
                Owner = System.Windows.Application.Current.MainWindow
            };
            
            if (passwordWindow.ShowDialog() == true)
            {
                var password = passwordWindow.EnteredPassword;
                if (string.IsNullOrEmpty(password))
                {
                    StatusMessage = "Password is required";
                    return;
                }
                
                var dialog = new SaveFileDialog
                {
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    Title = "Save Encrypted PDF",
                    FileName = $"{Path.GetFileNameWithoutExtension(SelectedFile.FileName)}_encrypted.pdf"
                };
                
                if (dialog.ShowDialog() == true)
                {
                    var options = new PdfEncryptionOptions
                    {
                        AllowPrint = true,
                        AllowCopy = false,
                        AllowModify = false,
                        AllowAnnotations = false
                    };
                    
                    await ExecuteWithProgress(async ct =>
                    {
                        await _service.EncryptPdfAsync(SelectedFile.FilePath, dialog.FileName, password, password, options, ct);
                    }, "Encrypting PDF...");
                }
            }
        }
        
        private async Task DecryptAsync()
        {
            if (SelectedFile?.IsPdf != true) return;
            
            // Check if file is encrypted
            if (!_service.IsPdfEncrypted(SelectedFile.FilePath))
            {
                StatusMessage = "This PDF is not encrypted";
                return;
            }
            
            // Show password dialog
            var passwordWindow = new Views.PromptPasswordWindow
            {
                Title = "Decrypt PDF - Enter Password",
                Owner = System.Windows.Application.Current.MainWindow
            };
            
            if (passwordWindow.ShowDialog() == true)
            {
                var password = passwordWindow.EnteredPassword;
                
                var dialog = new SaveFileDialog
                {
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    Title = "Save Decrypted PDF",
                    FileName = $"{Path.GetFileNameWithoutExtension(SelectedFile.FileName)}_decrypted.pdf"
                };
                
                if (dialog.ShowDialog() == true)
                {
                    await ExecuteWithProgress(async ct =>
                    {
                        await _service.DecryptPdfAsync(SelectedFile.FilePath, dialog.FileName, password, ct);
                    }, "Decrypting PDF...");
                }
            }
        }
        
        private async Task ImagesToPdfAsync()
        {
            var imageFiles = InputFiles.Where(f => !f.IsPdf).Select(f => f.FilePath).ToList();
            if (!imageFiles.Any())
            {
                StatusMessage = "No image files in the list";
                return;
            }
            
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Save Images as PDF",
                FileName = "images.pdf"
            };
            
            if (dialog.ShowDialog() == true)
            {
                await ExecuteWithProgress(async ct =>
                {
                    await _service.ImagesToPdfAsync(imageFiles, dialog.FileName, ct);
                }, "Converting images to PDF...");
            }
        }
        
        private async Task ExecuteWithProgress(Func<CancellationToken, Task> operation, string startMessage)
        {
            IsProcessing = true;
            Progress = 0;
            StatusMessage = startMessage;
            _cts = new CancellationTokenSource();
            
            try
            {
                await operation(_cts.Token);
                StatusMessage = "Operation completed successfully";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled";
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
        
        private void OnProgressChanged(object? sender, PdfProgressEventArgs e)
        {
            Progress = e.ProgressPercentage;
            StatusMessage = e.Message;
        }
        
        private List<int> ParsePageNumbers(string ranges, int maxPages)
        {
            var result = new List<int>();
            var parts = ranges.Split(',', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains('-'))
                {
                    var rangeParts = trimmed.Split('-');
                    if (rangeParts.Length == 2 &&
                        int.TryParse(rangeParts[0], out int start) &&
                        int.TryParse(rangeParts[1], out int end))
                    {
                        start = Math.Max(1, Math.Min(start, maxPages));
                        end = Math.Max(1, Math.Min(end, maxPages));
                        for (int i = start; i <= end; i++)
                        {
                            if (!result.Contains(i))
                                result.Add(i);
                        }
                    }
                }
                else if (int.TryParse(trimmed, out int page))
                {
                    page = Math.Max(1, Math.Min(page, maxPages));
                    if (!result.Contains(page))
                        result.Add(page);
                }
            }
            
            return result.OrderBy(p => p).ToList();
        }
        
        private void RaiseCommandsCanExecuteChanged()
        {
            (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearFilesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MergeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SplitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExtractPagesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (RotateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (DeletePagesCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (WatermarkCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ImagesToPdfCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (EncryptCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (DecryptCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        
        #region Preview Methods
        
        private void PreviousPage()
        {
            if (CurrentPreviewPage > 1)
                CurrentPreviewPage--;
        }
        
        private void NextPage()
        {
            if (CurrentPreviewPage < TotalPreviewPages)
                CurrentPreviewPage++;
        }
        
        private async Task LoadPreviewAsync()
        {
            var file = SelectedFile;
            if (file == null)
            {
                PreviewImage = null;
                _currentPdfDocument = null;
                return;
            }
            
            IsLoadingPreview = true;
            
            try
            {
                if (file.IsPdf)
                {
                    await LoadPdfPreviewAsync(file.FilePath);
                }
                else
                {
                    // Load image preview
                    await LoadImagePreviewAsync(file.FilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preview error: {ex.Message}");
                PreviewImage = null;
            }
            finally
            {
                IsLoadingPreview = false;
            }
        }
        
        private async Task LoadPdfPreviewAsync(string filePath)
        {
            try
            {
                // Clear previous document
                _currentPdfDocument = null;
                
                // Use Windows.Data.Pdf API to render PDF
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                _currentPdfDocument = await PdfDocument.LoadFromFileAsync(file);
                
                TotalPreviewPages = (int)_currentPdfDocument.PageCount;
                
                // Update the selected file's page count now that we know it
                if (SelectedFile != null)
                {
                    SelectedFile.PageCount = TotalPreviewPages;
                    OnPropertyChanged(nameof(SelectedFile));
                }
                
                // Ensure current page is valid
                if (CurrentPreviewPage > TotalPreviewPages)
                    _currentPreviewPage = 1; // Don't use property to avoid recursion
                if (CurrentPreviewPage < 1)
                    _currentPreviewPage = 1;
                
                await RenderCurrentPageAsync();
                RaiseCommandsCanExecuteChanged();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PDF preview error: {ex.Message}");
                PreviewImage = null;
                TotalPreviewPages = 0;
            }
        }
        
        private async Task RenderCurrentPageAsync()
        {
            if (_currentPdfDocument == null || CurrentPreviewPage < 1)
                return;
            
            var pageIndex = (uint)(CurrentPreviewPage - 1);
            if (pageIndex >= _currentPdfDocument.PageCount)
                return;
            
            using var page = _currentPdfDocument.GetPage(pageIndex);
            
            // Render at good quality
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Min(page.Size.Width * 2, 1600),
                DestinationHeight = (uint)Math.Min(page.Size.Height * 2, 2000)
            };
            
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options);
            
            // Convert to BitmapImage on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream.AsStreamForRead();
                bitmap.EndInit();
                bitmap.Freeze();
                
                PreviewImage = bitmap;
            });
        }
        
        private async Task LoadImagePreviewAsync(string filePath)
        {
            _currentPdfDocument = null;
            
            CurrentPreviewPage = 1;
            TotalPreviewPages = 1;
            
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(filePath);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    PreviewImage = bitmap;
                }
                catch
                {
                    PreviewImage = null;
                }
            });
        }
        
        #endregion
        
        #endregion
    }
    
    /// <summary>
    /// Represents a PDF or image file in the list.
    /// </summary>
    public class PdfFileItem : BindableBase
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public long FileSize { get; set; }
        public bool IsPdf { get; set; }
        
        public string FileSizeFormatted => FileSize < 1024 * 1024 
            ? $"{FileSize / 1024.0:F1} KB" 
            : $"{FileSize / (1024.0 * 1024.0):F1} MB";
        
        public string TypeDisplay => IsPdf ? "PDF" : "Image";
        
        public string PageCountDisplay => IsPdf 
            ? (PageCount > 0 ? $"{PageCount} pages" : "? pages") 
            : "-";
    }
    
    /// <summary>
    /// Represents a page in a PDF file.
    /// </summary>
    public class PdfPageItem : BindableBase
    {
        public int PageNumber { get; set; }
        
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
