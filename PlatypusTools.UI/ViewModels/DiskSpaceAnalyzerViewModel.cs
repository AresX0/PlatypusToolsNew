using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class DirectoryNodeViewModel : BindableBase
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _fullPath = string.Empty;
        public string FullPath { get => _fullPath; set => SetProperty(ref _fullPath, value); }

        private long _size;
        public long Size { get => _size; set => SetProperty(ref _size, value); }

        private string _sizeDisplay = string.Empty;
        public string SizeDisplay { get => _sizeDisplay; set => SetProperty(ref _sizeDisplay, value); }

        private int _fileCount;
        public int FileCount { get => _fileCount; set => SetProperty(ref _fileCount, value); }

        private bool _isExpanded;
        public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

        private bool _isFile;
        public bool IsFile { get => _isFile; set => SetProperty(ref _isFile, value); }

        private bool _isHidden;
        public bool IsHidden { get => _isHidden; set => SetProperty(ref _isHidden, value); }

        private bool _isSystem;
        public bool IsSystem { get => _isSystem; set => SetProperty(ref _isSystem, value); }

        public ObservableCollection<DirectoryNodeViewModel> Children { get; } = new();
    }

    public class DiskSpaceAnalyzerViewModel : BindableBase
    {
        private readonly DiskSpaceAnalyzerService _analyzerService;
        private CancellationTokenSource? _cancellationTokenSource;

        public DiskSpaceAnalyzerViewModel()
        {
            _analyzerService = new DiskSpaceAnalyzerService();

            AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAsync(), _ => !IsAnalyzing && !string.IsNullOrWhiteSpace(RootPath));
            BrowsePathCommand = new RelayCommand(_ => BrowsePath());
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsAnalyzing);
            ExportCommand = new RelayCommand(async _ => await ExportAsync(), _ => DirectoryTree.Any());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsAnalyzing);
        }

        public ObservableCollection<DirectoryNodeViewModel> DirectoryTree { get; } = new();

        private string _rootPath = string.Empty;
        public string RootPath 
        { 
            get => _rootPath; 
            set 
            { 
                SetProperty(ref _rootPath, value); 
                ((RelayCommand)AnalyzeCommand).RaiseCanExecuteChanged();
            } 
        }

        private bool _isAnalyzing;
        public bool IsAnalyzing 
        { 
            get => _isAnalyzing; 
            set 
            { 
                SetProperty(ref _isAnalyzing, value); 
                ((RelayCommand)AnalyzeCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            } 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private long _totalSize;
        public long TotalSize { get => _totalSize; set => SetProperty(ref _totalSize, value); }

        private string _totalSizeDisplay = "0 bytes";
        public string TotalSizeDisplay { get => _totalSizeDisplay; set => SetProperty(ref _totalSizeDisplay, value); }

        public ICommand AnalyzeCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CancelCommand { get; }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
        }

        private async Task AnalyzeAsync()
        {
            if (string.IsNullOrWhiteSpace(RootPath) || !Directory.Exists(RootPath))
            {
                StatusMessage = "Invalid root path";
                return;
            }

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            IsAnalyzing = true;
            StatusMessage = "Analyzing disk space...";
            DirectoryTree.Clear();

            try
            {
                var analysis = await Task.Run(() => 
                {
                    token.ThrowIfCancellationRequested();
                    return _analyzerService.GetDirectoryTree(RootPath);
                }, token);
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var rootNode = CreateNodeViewModel(analysis);
                    DirectoryTree.Add(rootNode);
                    TotalSize = rootNode.Size; // Use the calculated size from the node
                    TotalSizeDisplay = FormatSize(TotalSize);
                });

                StatusMessage = $"Analysis complete. Total size: {TotalSizeDisplay}";
                ((RelayCommand)ExportCommand).RaiseCanExecuteChanged();
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Analysis cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private DirectoryNodeViewModel CreateNodeViewModel(DirectoryNode analysisNode)
        {
            var node = new DirectoryNodeViewModel
            {
                Name = analysisNode.Name,
                FullPath = analysisNode.Path,
                Size = analysisNode.Size,
                SizeDisplay = FormatSize(analysisNode.Size),
                FileCount = analysisNode.FileCount,
                IsFile = analysisNode.IsFile,
                IsHidden = analysisNode.IsHidden,
                IsSystem = analysisNode.IsSystem
            };

            if (analysisNode.Children != null && analysisNode.Children.Any())
            {
                foreach (var child in analysisNode.Children)
                {
                    node.Children.Add(CreateNodeViewModel(child));
                }
            }

            return node;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "bytes", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private async Task RefreshAsync()
        {
            await AnalyzeAsync();
        }

        private void BrowsePath()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select directory to analyze";
            dialog.UseDescriptionForTitle = true;
            if (!string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath))
            {
                dialog.SelectedPath = RootPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                RootPath = dialog.SelectedPath;
            }
        }

        private async Task ExportAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"DiskSpaceAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dialog.ShowDialog() == true)
                {
                    // TODO: Implement basic export functionality
                    // await Task.Run(() => _analyzerService.ExportToFile(dialog.FileName));
                    StatusMessage = $"Export not yet implemented";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export error: {ex.Message}";
            }
        }
    }
}
