using PlatypusTools.Core.Services;
using PlatypusTools.Core.Utilities;
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
            try { SimpleLogger.Info("DiskSpaceAnalyzerViewModel constructed"); } catch {}
            _analyzerService = Services.ServiceLocator.DiskSpaceAnalyzer;

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

        private bool _showHiddenFiles = false;
        public bool ShowHiddenFiles 
        { 
            get => _showHiddenFiles; 
            set 
            { 
                if (SetProperty(ref _showHiddenFiles, value))
                    ApplyFilters();
            } 
        }

        private bool _showSystemFiles = false;
        public bool ShowSystemFiles 
        { 
            get => _showSystemFiles; 
            set 
            { 
                if (SetProperty(ref _showSystemFiles, value))
                    ApplyFilters();
            } 
        }

        // Store the full unfiltered tree for reapplying filters
        private DirectoryNodeViewModel? _unfilteredRootNode;

        public ICommand AnalyzeCommand { get; }
        public ICommand BrowsePathCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CancelCommand { get; }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            IsAnalyzing = false;
            StatusMessage = "Cancelled";
        }

        public async Task AnalyzeAsync()
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
            
            // Update global status bar
            StatusBarViewModel.Instance.StartOperation("Analyzing disk space...", 0, true);

            try
            {
                SimpleLogger.Info($"DiskSpaceAnalyzer: starting analysis for '{RootPath}'");

                token.ThrowIfCancellationRequested();

                // Await the analyzer service directly. Use maxDepth=10 for comprehensive view
                var analysis = await _analyzerService.GetDirectoryTree(RootPath, maxDepth: 10);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var rootNode = CreateNodeViewModel(analysis);
                    _unfilteredRootNode = rootNode; // Store unfiltered copy
                    ApplyFilters(); // Apply current filter settings
                    TotalSize = rootNode.Size; // Use the calculated size from the node
                    TotalSizeDisplay = FormatSize(TotalSize);
                });

                StatusMessage = $"Analysis complete. Total size: {TotalSizeDisplay}";
                ((RelayCommand)ExportCommand).RaiseCanExecuteChanged();
                SimpleLogger.Info($"DiskSpaceAnalyzer: completed analysis for '{RootPath}' size={TotalSize}");
                
                StatusBarViewModel.Instance.CompleteOperation($"Analysis complete. Total size: {TotalSizeDisplay}");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Analysis cancelled";
                StatusBarViewModel.Instance.CompleteOperation("Analysis cancelled");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                try { SimpleLogger.Error("DiskSpaceAnalyzer AnalyzeAsync exception: " + ex.ToString()); } catch { }
                StatusBarViewModel.Instance.CompleteOperation($"Error: {ex.Message}");
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

            if (DialogHelper.ShowFolderDialog(dialog) == System.Windows.Forms.DialogResult.OK)
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
                    StatusMessage = "Exporting...";
                    
                    await Task.Run(() =>
                    {
                        using var writer = new System.IO.StreamWriter(dialog.FileName);
                        
                        // Write header
                        if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            writer.WriteLine("Path,Size,Size (Bytes),File Count,Is Hidden,Is System");
                            ExportNodeToCsv(writer, DirectoryTree.FirstOrDefault());
                        }
                        else
                        {
                            writer.WriteLine($"Disk Space Analysis Report");
                            writer.WriteLine($"Generated: {DateTime.Now}");
                            writer.WriteLine($"Root Path: {RootPath}");
                            writer.WriteLine($"Total Size: {TotalSizeDisplay}");
                            writer.WriteLine(new string('=', 80));
                            writer.WriteLine();
                            ExportNodeToText(writer, DirectoryTree.FirstOrDefault(), 0);
                        }
                    });
                    
                    StatusMessage = $"Exported to {System.IO.Path.GetFileName(dialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export error: {ex.Message}";
            }
        }
        
        private void ExportNodeToCsv(System.IO.StreamWriter writer, DirectoryNodeViewModel? node)
        {
            if (node == null) return;
            
            writer.WriteLine($"\"{node.FullPath}\",\"{node.SizeDisplay}\",{node.Size},{node.FileCount},{node.IsHidden},{node.IsSystem}");
            
            foreach (var child in node.Children)
            {
                ExportNodeToCsv(writer, child);
            }
        }
        
        private void ExportNodeToText(System.IO.StreamWriter writer, DirectoryNodeViewModel? node, int indent)
        {
            if (node == null) return;
            
            var prefix = new string(' ', indent * 2);
            var icon = node.IsFile ? "📄" : "📁";
            writer.WriteLine($"{prefix}{icon} {node.Name} ({node.SizeDisplay})");
            
            foreach (var child in node.Children.OrderByDescending(c => c.Size))
            {
                ExportNodeToText(writer, child, indent + 1);
            }
        }

        /// <summary>
        /// Applies the current filter settings to the directory tree.
        /// </summary>
        private void ApplyFilters()
        {
            if (_unfilteredRootNode == null)
                return;

            DirectoryTree.Clear();
            var filteredRoot = FilterNode(_unfilteredRootNode);
            if (filteredRoot != null)
                DirectoryTree.Add(filteredRoot);
        }

        /// <summary>
        /// Recursively filters a node based on ShowHiddenFiles and ShowSystemFiles settings.
        /// </summary>
        private DirectoryNodeViewModel? FilterNode(DirectoryNodeViewModel source)
        {
            // If it's a hidden/system item and we're not showing those, skip it
            if (!ShowHiddenFiles && source.IsHidden)
                return null;
            if (!ShowSystemFiles && source.IsSystem)
                return null;

            // Clone the node
            var filtered = new DirectoryNodeViewModel
            {
                Name = source.Name,
                FullPath = source.FullPath,
                Size = source.Size,
                SizeDisplay = source.SizeDisplay,
                FileCount = source.FileCount,
                IsExpanded = source.IsExpanded,
                IsFile = source.IsFile,
                IsHidden = source.IsHidden,
                IsSystem = source.IsSystem
            };

            // Recursively filter children
            foreach (var child in source.Children)
            {
                var filteredChild = FilterNode(child);
                if (filteredChild != null)
                    filtered.Children.Add(filteredChild);
            }

            return filtered;
        }
    }
}
