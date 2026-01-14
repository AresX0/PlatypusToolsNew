using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class VideoConverterViewModel : INotifyPropertyChanged
    {
        private readonly IVideoConverterService _service;
        private CancellationTokenSource? _cancellationTokenSource;

        public ObservableCollection<VideoConversionTask> ConversionTasks { get; } = new();

        private string _sourceFolderPath = string.Empty;
        public string SourceFolderPath
        {
            get => _sourceFolderPath;
            set 
            { 
                if (_sourceFolderPath != value)
                {
                    _sourceFolderPath = value; 
                    OnPropertyChanged(); 
                    // Refresh the Scan command when path changes
                    (ScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _outputFolderPath = string.Empty;
        public string OutputFolderPath
        {
            get => _outputFolderPath;
            set { _outputFolderPath = value; OnPropertyChanged(); }
        }

        private bool _includeSubfolders;
        public bool IncludeSubfolders
        {
            get => _includeSubfolders;
            set { _includeSubfolders = value; OnPropertyChanged(); }
        }

        private VideoFormat _targetFormat = VideoFormat.MP4;
        public VideoFormat TargetFormat
        {
            get => _targetFormat;
            set { _targetFormat = value; OnPropertyChanged(); UpdateOutputPaths(); }
        }

        private VideoQuality _quality = VideoQuality.Medium;
        public VideoQuality Quality
        {
            get => _quality;
            set { _quality = value; OnPropertyChanged(); }
        }

        private bool _useSourceFolder = true;
        public bool UseSourceFolder
        {
            get => _useSourceFolder;
            set { _useSourceFolder = value; OnPropertyChanged(); UpdateOutputPaths(); }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isConverting;
        public bool IsConverting
        {
            get => _isConverting;
            set { _isConverting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartConversion)); }
        }

        private double _overallProgress;
        public double OverallProgress
        {
            get => _overallProgress;
            set { _overallProgress = value; OnPropertyChanged(); }
        }

        private int _completedCount;
        public int CompletedCount
        {
            get => _completedCount;
            set { _completedCount = value; OnPropertyChanged(); }
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set { _totalCount = value; OnPropertyChanged(); }
        }

        public bool CanStartConversion => ConversionTasks.Any(t => t.IsSelected) && !IsConverting;

        public ICommand BrowseSourceFolderCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand StartConversionCommand { get; }
        public ICommand CancelConversionCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand ClearCompletedCommand { get; }

        public VideoConverterViewModel() : this(new VideoConverterService()) { }

        public VideoConverterViewModel(IVideoConverterService service)
        {
            _service = service;

            BrowseSourceFolderCommand = new RelayCommand(_ => BrowseSourceFolder());
            BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
            ScanCommand = new RelayCommand(_ => Scan(), _ => !string.IsNullOrEmpty(SourceFolderPath));
            StartConversionCommand = new RelayCommand(_ => StartConversion(), _ => CanStartConversion);
            CancelConversionCommand = new RelayCommand(_ => CancelConversion(), _ => IsConverting);
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());
            ClearCompletedCommand = new RelayCommand(_ => ClearCompleted());

            // Check FFmpeg availability
            if (!_service.IsFFmpegAvailable())
            {
                StatusMessage = "⚠️ FFmpeg not found. Please install FFmpeg to use this feature.";
            }
        }

        private void BrowseSourceFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select source folder with videos",
                ShowNewFolderButton = false
            };

            if (!string.IsNullOrEmpty(SourceFolderPath) && Directory.Exists(SourceFolderPath))
                dialog.SelectedPath = SourceFolderPath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourceFolderPath = dialog.SelectedPath;
                if (UseSourceFolder)
                    OutputFolderPath = dialog.SelectedPath;
            }
        }

        private void BrowseOutputFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select output folder for converted videos",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrEmpty(OutputFolderPath) && Directory.Exists(OutputFolderPath))
                dialog.SelectedPath = OutputFolderPath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputFolderPath = dialog.SelectedPath;
            }
        }

        private async void Scan()
        {
            if (!_service.IsFFmpegAvailable())
            {
                StatusMessage = "FFmpeg not available";
                return;
            }

            StatusMessage = "Scanning...";
            
            // Unsubscribe from existing tasks before clearing
            foreach (var task in ConversionTasks)
            {
                task.PropertyChanged -= Task_PropertyChanged;
            }
            ConversionTasks.Clear();

            try
            {
                var extensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".m4v" };
                var tasks = await _service.ScanFolder(SourceFolderPath, IncludeSubfolders, extensions);

                foreach (var task in tasks)
                {
                    task.TargetFormat = TargetFormat;
                    task.Quality = Quality;
                    task.OutputPath = GetOutputPath(task.SourcePath);
                    task.IsSelected = false; // User must manually select files
                    task.PropertyChanged += Task_PropertyChanged;
                    ConversionTasks.Add(task);
                }

                TotalCount = ConversionTasks.Count;
                StatusMessage = $"Found {TotalCount} video files";
                RefreshCommandStates();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideoConversionTask.IsSelected))
            {
                RefreshCommandStates();
            }
        }

        private void RefreshCommandStates()
        {
            OnPropertyChanged(nameof(CanStartConversion));
            (StartConversionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private async void StartConversion()
        {
            if (!CanStartConversion) return;

            IsConverting = true;
            CompletedCount = 0;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var selectedTasks = ConversionTasks.Where(t => t.IsSelected).ToList();
                TotalCount = selectedTasks.Count;
                
                // Update global status bar
                StatusBarViewModel.Instance.StartOperation($"Converting videos...", TotalCount, true);

                var progress = new Progress<(int completed, int total)>(p =>
                {
                    CompletedCount = p.completed;
                    OverallProgress = (double)p.completed / p.total * 100;
                    StatusMessage = $"Converting {p.completed}/{p.total}...";
                    StatusBarViewModel.Instance.UpdateProgress(p.completed, $"Converting {p.completed}/{p.total}...");
                });

                await _service.ConvertBatch(selectedTasks, progress, _cancellationTokenSource.Token);

                StatusMessage = $"Completed {CompletedCount} of {TotalCount} conversions";
                StatusBarViewModel.Instance.CompleteOperation($"Completed {CompletedCount} of {TotalCount} conversions");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Conversion cancelled";
                StatusBarViewModel.Instance.CompleteOperation("Conversion cancelled");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                StatusBarViewModel.Instance.CompleteOperation($"Error: {ex.Message}");
            }
            finally
            {
                IsConverting = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelConversion()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
        }

        private void SelectAll()
        {
            foreach (var task in ConversionTasks)
                task.IsSelected = true;
        }

        private void SelectNone()
        {
            foreach (var task in ConversionTasks)
                task.IsSelected = false;
        }

        private void ClearCompleted()
        {
            var completed = ConversionTasks.Where(t => t.Status == ConversionStatus.Completed).ToList();
            foreach (var task in completed)
                ConversionTasks.Remove(task);
            
            StatusMessage = $"Cleared {completed.Count} completed tasks";
        }

        private void UpdateOutputPaths()
        {
            foreach (var task in ConversionTasks)
            {
                task.OutputPath = GetOutputPath(task.SourcePath);
            }
        }

        private string GetOutputPath(string sourcePath)
        {
            var outputDir = UseSourceFolder ? Path.GetDirectoryName(sourcePath) : OutputFolderPath;
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = "." + TargetFormat.ToString().ToLower();
            
            return Path.Combine(outputDir ?? string.Empty, fileName + extension);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
