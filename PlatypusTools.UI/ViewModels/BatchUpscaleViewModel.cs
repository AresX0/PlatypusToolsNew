using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PlatypusTools.Core.Models.ImageScaler;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for batch upscaling images.
    /// </summary>
    public class BatchUpscaleViewModel : BindableBase
    {
        private readonly BatchUpscaleService _service;
        private CancellationTokenSource? _cts;
        
        public BatchUpscaleViewModel()
        {
            _service = BatchUpscaleService.Instance;
            Jobs = new ObservableCollection<BatchUpscaleJob>();
            SelectedItems = new ObservableCollection<BatchUpscaleItem>();
            Settings = new BatchUpscaleSettings();
            
            // Wire up events
            _service.JobStarted += (s, job) => OnJobStarted(job);
            _service.JobCompleted += (s, job) => OnJobCompleted(job);
            _service.ItemCompleted += (s, item) => UpdateProgress();
            _service.ItemFailed += (s, e) => UpdateProgress();
            
            // Commands
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            AddFolderCommand = new RelayCommand(_ => AddFolder());
            RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedJob?.Items.Any(i => i.Status == BatchJobStatus.Queued) == true);
            ClearQueueCommand = new RelayCommand(_ => ClearQueue(), _ => Jobs.Any());
            
            StartProcessingCommand = new RelayCommand(_ => StartProcessing(), _ => Jobs.Any(j => j.Status == BatchJobStatus.Queued));
            PauseProcessingCommand = new RelayCommand(_ => PauseProcessing(), _ => IsProcessing);
            CancelProcessingCommand = new RelayCommand(_ => CancelProcessing(), _ => IsProcessing);
            
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            CompareCommand = new RelayCommand(_ => LoadPreview(), _ => SelectedItem != null);
            OpenOutputFolderCommand = new RelayCommand(_ => OpenOutputFolder(), _ => SelectedItem?.Status == BatchJobStatus.Completed);
            EditItemSettingsCommand = new RelayCommand(_ => EditItemSettings(), _ => SelectedItem != null && SelectedItem.Status == BatchJobStatus.Queued);
            ResetItemSettingsCommand = new RelayCommand(_ => ResetItemSettings(), _ => SelectedItem?.UseOverrides == true);
        }
        
        #region Properties
        
        public ObservableCollection<BatchUpscaleJob> Jobs { get; }
        public ObservableCollection<BatchUpscaleItem> SelectedItems { get; }
        
        /// <summary>
        /// Flat list of all items from the current/selected job for DataGrid binding.
        /// </summary>
        public ObservableCollection<BatchUpscaleItem> CurrentJobItems { get; } = new();
        
        private BatchUpscaleJob? _selectedJob;
        public BatchUpscaleJob? SelectedJob
        {
            get => _selectedJob;
            set
            {
                if (SetProperty(ref _selectedJob, value))
                {
                    RaisePropertyChanged(nameof(CanModifyQueue));
                    RefreshCurrentJobItems();
                }
            }
        }
        
        private BatchUpscaleItem? _selectedItem;
        public BatchUpscaleItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    LoadPreview();
                    ((RelayCommand)CompareCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)OpenOutputFolderCommand).RaiseCanExecuteChanged();
                }
            }
        }
        
        private ImageSource? _originalPreviewImage;
        public ImageSource? OriginalPreviewImage
        {
            get => _originalPreviewImage;
            set => SetProperty(ref _originalPreviewImage, value);
        }
        
        private ImageSource? _upscaledPreviewImage;
        public ImageSource? UpscaledPreviewImage
        {
            get => _upscaledPreviewImage;
            set => SetProperty(ref _upscaledPreviewImage, value);
        }
        
        private int _failedCount;
        public int FailedCount
        {
            get => _failedCount;
            set => SetProperty(ref _failedCount, value);
        }
        
        private BatchUpscaleSettings _settings = new();
        public BatchUpscaleSettings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }
        
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    RaisePropertyChanged(nameof(CanModifyQueue));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private double _overallProgress;
        public double OverallProgress
        {
            get => _overallProgress;
            set => SetProperty(ref _overallProgress, value);
        }
        
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        private int _completedCount;
        public int CompletedCount
        {
            get => _completedCount;
            set => SetProperty(ref _completedCount, value);
        }
        
        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }
        
        public bool CanModifyQueue => !IsProcessing;
        
        // Scale options
        public double[] ScaleFactors { get; } = { 1.5, 2.0, 3.0, 4.0, 8.0 };
        public string[] OutputFormats { get; } = { "png", "jpg", "webp", "tiff" };
        public UpscaleMode[] UpscaleModes { get; } = Enum.GetValues<UpscaleMode>();
        
        #endregion
        
        #region Commands
        
        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearQueueCommand { get; }
        public ICommand StartProcessingCommand { get; }
        public ICommand PauseProcessingCommand { get; }
        public ICommand CancelProcessingCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand CompareCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand EditItemSettingsCommand { get; }
        public ICommand ResetItemSettingsCommand { get; }
        
        #endregion
        
        #region Methods
        
        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.tiff;*.webp;*.gif|All Files|*.*",
                Title = "Select Images to Upscale"
            };
            
            if (dialog.ShowDialog() == true)
            {
                AddFilesToQueue(dialog.FileNames);
            }
        }
        
        private void AddFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing images",
                ShowNewFolderButton = false
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var extensions = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.tiff", "*.webp" };
                var files = extensions.SelectMany(ext => 
                    Directory.GetFiles(dialog.SelectedPath, ext, SearchOption.AllDirectories));
                AddFilesToQueue(files);
            }
        }
        
        private void AddFilesToQueue(System.Collections.Generic.IEnumerable<string> files)
        {
            var fileList = files.ToList();
            if (!fileList.Any()) return;
            
            var jobName = $"Batch {DateTime.Now:yyyy-MM-dd HH:mm}";
            var job = _service.CreateJob(jobName, fileList, Settings);
            
            Jobs.Add(job);
            SelectedJob = job;
            
            TotalCount = Jobs.Sum(j => j.Items.Count);
            StatusMessage = $"Added {fileList.Count} files to queue";
        }
        
        private void RemoveSelected()
        {
            if (SelectedJob == null) return;
            
            var toRemove = SelectedJob.Items.Where(i => i.Status == BatchJobStatus.Queued).ToList();
            foreach (var item in toRemove)
            {
                SelectedJob.Items.Remove(item);
            }
            
            if (!SelectedJob.Items.Any())
            {
                Jobs.Remove(SelectedJob);
                SelectedJob = Jobs.FirstOrDefault();
            }
            
            TotalCount = Jobs.Sum(j => j.Items.Count);
        }
        
        private void ClearQueue()
        {
            var queuedJobs = Jobs.Where(j => j.Status == BatchJobStatus.Queued).ToList();
            foreach (var job in queuedJobs)
            {
                Jobs.Remove(job);
                _service.RemoveJob(job);
            }
            
            TotalCount = Jobs.Sum(j => j.Items.Count);
            StatusMessage = "Queue cleared";
        }
        
        private void StartProcessing()
        {
            if (IsProcessing) return;
            
            _cts = new CancellationTokenSource();
            IsProcessing = true;
            CompletedCount = 0;
            
            foreach (var job in Jobs.Where(j => j.Status == BatchJobStatus.Queued))
            {
                _service.EnqueueJob(job);
            }
            
            StatusMessage = "Processing started...";
            
            // Update status bar
            StatusBarViewModel.Instance.StartOperation("Batch upscale", TotalCount);
        }
        
        private void PauseProcessing()
        {
            // Pausing not directly supported by the service
            // Would need to implement pause in BatchUpscaleService
            StatusMessage = "Pause not implemented yet";
        }
        
        private void CancelProcessing()
        {
            _cts?.Cancel();
            _service.StopProcessing();
            
            IsProcessing = false;
            StatusMessage = "Processing cancelled";
            StatusBarViewModel.Instance.CompleteOperation("Batch upscale cancelled");
        }
        
        private void BrowseOutput()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select output directory",
                ShowNewFolderButton = true
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Settings.OutputDirectory = dialog.SelectedPath;
                RaisePropertyChanged(nameof(Settings));
            }
        }
        
        private void OnJobStarted(BatchUpscaleJob job)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Processing: {job.Name}";
            });
        }
        
        private void OnJobCompleted(BatchUpscaleJob job)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Completed: {job.Name} ({job.CompletedCount}/{job.Items.Count})";
                
                // Check if all jobs are done
                if (!Jobs.Any(j => j.Status == BatchJobStatus.Queued || j.Status == BatchJobStatus.Processing))
                {
                    IsProcessing = false;
                    var totalCompleted = Jobs.Sum(j => j.CompletedCount);
                    var totalFailed = Jobs.Sum(j => j.FailedCount);
                    StatusBarViewModel.Instance.CompleteOperation($"Batch upscale complete: {totalCompleted} succeeded, {totalFailed} failed");
                }
            });
        }
        
        private void UpdateProgress()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                CompletedCount = Jobs.Sum(j => j.CompletedCount + j.FailedCount);
                OverallProgress = TotalCount > 0 ? (double)CompletedCount / TotalCount * 100 : 0;
                
                StatusBarViewModel.Instance.UpdateProgress(CompletedCount, $"{CompletedCount} of {TotalCount}");
            });
        }
        
        private void RaiseCommandsCanExecuteChanged()
        {
            ((RelayCommand)StartProcessingCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PauseProcessingCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelProcessingCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RemoveSelectedCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ClearQueueCommand).RaiseCanExecuteChanged();
        }
        
        private void RefreshCurrentJobItems()
        {
            CurrentJobItems.Clear();
            
            if (SelectedJob != null)
            {
                foreach (var item in SelectedJob.Items)
                {
                    CurrentJobItems.Add(item);
                }
            }
            else
            {
                // Show all items from all jobs if no job selected
                foreach (var job in Jobs)
                {
                    foreach (var item in job.Items)
                    {
                        CurrentJobItems.Add(item);
                    }
                }
            }
        }
        
        private void LoadPreview()
        {
            if (SelectedItem == null)
            {
                OriginalPreviewImage = null;
                UpscaledPreviewImage = null;
                return;
            }
            
            // Load original image
            if (File.Exists(SelectedItem.SourcePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(SelectedItem.SourcePath);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    OriginalPreviewImage = bitmap;
                }
                catch
                {
                    OriginalPreviewImage = null;
                }
            }
            
            // Load upscaled image if completed
            if (SelectedItem.Status == BatchJobStatus.Completed && File.Exists(SelectedItem.OutputPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(SelectedItem.OutputPath);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    UpscaledPreviewImage = bitmap;
                }
                catch
                {
                    UpscaledPreviewImage = null;
                }
            }
            else
            {
                UpscaledPreviewImage = null;
            }
        }
        
        private void OpenOutputFolder()
        {
            if (SelectedItem?.OutputPath == null) return;
            
            var folder = Path.GetDirectoryName(SelectedItem.OutputPath);
            if (folder != null && Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SelectedItem.OutputPath}\"");
            }
        }
        
        private void EditItemSettings()
        {
            if (SelectedItem == null || SelectedItem.Status != BatchJobStatus.Queued) return;
            
            // Create a dialog to edit item settings
            var dialog = new Views.ItemSettingsWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            
            // Initialize overrides from current settings if not already set
            if (!SelectedItem.UseOverrides)
            {
                SelectedItem.Overrides.CopyFromSettings(Settings);
            }
            
            dialog.DataContext = new ItemSettingsDialogViewModel(SelectedItem, UpscaleModes, ScaleFactors, OutputFormats);
            
            if (dialog.ShowDialog() == true)
            {
                SelectedItem.UseOverrides = true;
                // Force refresh of the display
                RaisePropertyChanged(nameof(SelectedItem));
            }
        }
        
        private void ResetItemSettings()
        {
            if (SelectedItem == null) return;
            SelectedItem.UseOverrides = false;
            RaisePropertyChanged(nameof(SelectedItem));
        }
        
        #endregion
    }
    
    /// <summary>
    /// ViewModel for the item settings dialog.
    /// </summary>
    public class ItemSettingsDialogViewModel : BindableBase
    {
        public ItemSettingsDialogViewModel(BatchUpscaleItem item, UpscaleMode[] modes, double[] scales, string[] formats)
        {
            Item = item;
            UpscaleModes = modes;
            ScaleFactors = scales;
            OutputFormats = formats;
        }
        
        public BatchUpscaleItem Item { get; }
        public UpscaleMode[] UpscaleModes { get; }
        public double[] ScaleFactors { get; }
        public string[] OutputFormats { get; }
        
        public UpscaleMode SelectedMode
        {
            get => Item.Overrides.Mode;
            set { Item.Overrides.Mode = value; RaisePropertyChanged(); }
        }
        
        public double SelectedScale
        {
            get => Item.Overrides.ScaleFactor;
            set { Item.Overrides.ScaleFactor = value; RaisePropertyChanged(); }
        }
        
        public string SelectedFormat
        {
            get => Item.Overrides.OutputFormat;
            set { Item.Overrides.OutputFormat = value; RaisePropertyChanged(); }
        }
        
        public int JpegQuality
        {
            get => Item.Overrides.JpegQuality;
            set { Item.Overrides.JpegQuality = value; RaisePropertyChanged(); }
        }
    }
}
