using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using PlatypusTools.Core.Services;
using System.Windows.Forms;

namespace PlatypusTools.UI.ViewModels
{
    public class FileAnalyzerViewModel : BindableBase
    {
        private readonly FileAnalyzerService _service;
        private CancellationTokenSource? _cancellationTokenSource;

        private string _directoryPath = string.Empty;
        private bool _includeSubdirectories = true;
        private bool _isAnalyzing = false;
        private string _statusMessage = string.Empty;
        private string? _selectedView = "Summary";

        private int _totalFiles;
        private long _totalSize;
        private int _totalDirectories;

        public FileAnalyzerViewModel()
        {
            try { SimpleLogger.Info("FileAnalyzerViewModel constructed"); } catch {}
            _service = new FileAnalyzerService();
            
            FileTypeStats = new ObservableCollection<FileTypeStatsViewModel>();
            LargestFiles = new ObservableCollection<FileInfoViewModel>();
            OldestFiles = new ObservableCollection<FileInfoViewModel>();
            NewestFiles = new ObservableCollection<FileInfoViewModel>();
            FilesByAge = new ObservableCollection<FileAgeViewModel>();
            DuplicateGroups = new ObservableCollection<FileDuplicateGroupViewModel>();
            TreeNodes = new ObservableCollection<DirectoryTreeNodeViewModel>();

            AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAsync(), _ => !IsAnalyzing && !string.IsNullOrWhiteSpace(DirectoryPath));
            FindDuplicatesCommand = new RelayCommand(async _ => await FindDuplicatesAsync(), _ => !IsAnalyzing && !string.IsNullOrWhiteSpace(DirectoryPath));
            BuildTreeCommand = new RelayCommand(async _ => await BuildTreeAsync(), _ => !IsAnalyzing && !string.IsNullOrWhiteSpace(DirectoryPath));
            BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory());
            ExportCommand = new RelayCommand(_ => Export(), _ => TotalFiles > 0);
            ClearCommand = new RelayCommand(_ => Clear());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsAnalyzing);
        }

        public string DirectoryPath
        {
            get => _directoryPath;
            set
            {
                if (SetProperty(ref _directoryPath, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IncludeSubdirectories
        {
            get => _includeSubdirectories;
            set => SetProperty(ref _includeSubdirectories, value);
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (SetProperty(ref _isAnalyzing, value))
                {
                    RaiseCommandsCanExecuteChanged();
                    ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string? SelectedView
        {
            get => _selectedView;
            set => SetProperty(ref _selectedView, value);
        }

        public int TotalFiles
        {
            get => _totalFiles;
            set
            {
                if (SetProperty(ref _totalFiles, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public long TotalSize
        {
            get => _totalSize;
            set => SetProperty(ref _totalSize, value);
        }

        public int TotalDirectories
        {
            get => _totalDirectories;
            set => SetProperty(ref _totalDirectories, value);
        }

        public string TotalSizeFormatted => FormatSize(TotalSize);

        public ObservableCollection<FileTypeStatsViewModel> FileTypeStats { get; }
        public ObservableCollection<FileInfoViewModel> LargestFiles { get; }
        public ObservableCollection<FileInfoViewModel> OldestFiles { get; }
        public ObservableCollection<FileInfoViewModel> NewestFiles { get; }
        public ObservableCollection<FileAgeViewModel> FilesByAge { get; }
        public ObservableCollection<FileDuplicateGroupViewModel> DuplicateGroups { get; }
        public ObservableCollection<DirectoryTreeNodeViewModel> TreeNodes { get; }

        public ICommand AnalyzeCommand { get; }
        public ICommand FindDuplicatesCommand { get; }
        public ICommand BuildTreeCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CancelCommand { get; }

        public async Task AnalyzeAsync()
        {
            if (!Directory.Exists(DirectoryPath))
            {
                StatusMessage = "Directory does not exist";
                return;
            }

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            IsAnalyzing = true;
            StatusMessage = "Analyzing directory...";
            Clear();
            try { SimpleLogger.Info($"FileAnalyzer: starting analysis for '{DirectoryPath}' includeSubdirs={IncludeSubdirectories}"); } catch {}

            try
            {
                var result = await Task.Run(() => 
                {
                    token.ThrowIfCancellationRequested();
                    return _service.AnalyzeDirectory(DirectoryPath, IncludeSubdirectories);
                }, token);

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    StatusMessage = $"Error: {result.ErrorMessage}";
                    return;
                }

                // Update UI on dispatcher thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    TotalFiles = result.TotalFiles;
                    TotalSize = result.TotalSize;
                    TotalDirectories = result.DirectoryStats.Count;

                    foreach (var stat in result.FilesByExtension.Values.OrderByDescending(s => s.TotalSize))
                    {
                        FileTypeStats.Add(new FileTypeStatsViewModel(stat));
                    }

                    foreach (var file in result.LargestFiles.Take(50))
                    {
                        LargestFiles.Add(new FileInfoViewModel(file));
                    }

                    foreach (var file in result.OldestFiles.Take(50))
                    {
                        OldestFiles.Add(new FileInfoViewModel(file));
                    }

                    foreach (var file in result.NewestFiles.Take(50))
                    {
                        NewestFiles.Add(new FileInfoViewModel(file));
                    }

                    foreach (var age in result.FilesByAge)
                    {
                        FilesByAge.Add(new FileAgeViewModel { AgeRange = age.Key, Count = age.Value });
                    }

                    OnPropertyChanged(nameof(TotalSizeFormatted));
                });

                StatusMessage = $"Analysis complete: {TotalFiles} files, {TotalSizeFormatted}";
                try { SimpleLogger.Info($"FileAnalyzer: completed analysis for '{DirectoryPath}' files={TotalFiles} size={TotalSize}"); } catch {}
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Analysis cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                try { SimpleLogger.Error("FileAnalyzer AnalyzeAsync exception: " + ex.ToString()); } catch {}
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            IsAnalyzing = false;
            StatusMessage = "Cancelled";
        }

        public async Task FindDuplicatesAsync()
        {
            if (!Directory.Exists(DirectoryPath))
            {
                StatusMessage = "Directory does not exist";
                return;
            }

            IsAnalyzing = true;
            StatusMessage = "Finding duplicates...";
            DuplicateGroups.Clear();

            try
            {
                var duplicates = await Task.Run(() => _service.FindDuplicates(DirectoryPath, IncludeSubdirectories));

                foreach (var group in duplicates.OrderByDescending(g => g.Size))
                {
                    DuplicateGroups.Add(new FileDuplicateGroupViewModel(group));
                }

                StatusMessage = $"Found {DuplicateGroups.Count} duplicate groups";
                SelectedView = "Duplicates";
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

        public async Task BuildTreeAsync()
        {
            if (!Directory.Exists(DirectoryPath))
            {
                StatusMessage = "Directory does not exist";
                return;
            }

            IsAnalyzing = true;
            StatusMessage = "Building directory tree...";
            TreeNodes.Clear();

            try
            {
                var tree = await Task.Run(() => _service.BuildDirectoryTree(DirectoryPath));
                TreeNodes.Add(new DirectoryTreeNodeViewModel(tree));

                StatusMessage = "Directory tree built";
                SelectedView = "Tree";
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

        private void BrowseDirectory()
        {
            try
            {
                SimpleLogger.Info("FileAnalyzer: BrowseDirectory called");
                System.Windows.MessageBox.Show("BrowseDirectory called - debug", "Debug", System.Windows.MessageBoxButton.OK);
                
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select directory to analyze"
                };

                // Get the main window handle for dialog parenting
                var result = DialogHelper.ShowFolderDialog(dialog);
                
                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    DirectoryPath = dialog.SelectedPath;
                    StatusMessage = $"Selected: {dialog.SelectedPath}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error browsing directory: {ex.Message}";
            }
        }

        private void Export()
        {
            if (TotalFiles == 0)
            {
                StatusMessage = "No analysis data to export";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Analysis Results",
                DefaultExt = ".csv",
                Filter = "CSV Files|*.csv|Text Files|*.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ExportToCSV(dialog.FileName);
                    StatusMessage = $"Export completed: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        private void ExportToCSV(string filePath)
        {
            using (var writer = new System.IO.StreamWriter(filePath))
            {
                // Summary section
                writer.WriteLine("FILE ANALYSIS REPORT");
                writer.WriteLine($"Directory,{DirectoryPath}");
                writer.WriteLine($"Total Files,{TotalFiles}");
                writer.WriteLine($"Total Size,{TotalSizeFormatted}");
                writer.WriteLine($"Total Directories,{TotalDirectories}");
                writer.WriteLine($"Analysis Date,{DateTime.Now:G}");
                writer.WriteLine();

                // File Types section
                writer.WriteLine("FILE TYPES");
                writer.WriteLine("Extension,Count,Total Size");
                foreach (var ft in FileTypeStats)
                {
                    writer.WriteLine($"\"{ft.Extension}\",{ft.Count},{ft.TotalSize}");
                }
                writer.WriteLine();

                // Largest Files section
                writer.WriteLine("LARGEST FILES");
                writer.WriteLine("Filename,Size,Full Path");
                foreach (var file in LargestFiles.Take(100))
                {
                    writer.WriteLine($"\"{file.Name}\",{file.Size},\"{file.FullPath}\"");
                }
                writer.WriteLine();

                // Oldest Files section
                writer.WriteLine("OLDEST FILES");
                writer.WriteLine("Filename,Modified Date,Full Path");
                foreach (var file in OldestFiles.Take(100))
                {
                    writer.WriteLine($"\"{file.Name}\",{file.Modified:G},\"{file.FullPath}\"");
                }
                writer.WriteLine();

                // Newest Files section
                writer.WriteLine("NEWEST FILES");
                writer.WriteLine("Filename,Modified Date,Full Path");
                foreach (var file in NewestFiles.Take(100))
                {
                    writer.WriteLine($"\"{file.Name}\",{file.Modified:G},\"{file.FullPath}\"");
                }
                writer.WriteLine();

                // Files by Age section
                writer.WriteLine("FILES BY AGE");
                writer.WriteLine("Age Range,Count");
                foreach (var age in FilesByAge)
                {
                    writer.WriteLine($"\"{age.AgeRange}\",{age.Count}");
                }
            }
        }

        private void Clear()
        {
            FileTypeStats.Clear();
            LargestFiles.Clear();
            OldestFiles.Clear();
            NewestFiles.Clear();
            FilesByAge.Clear();
            DuplicateGroups.Clear();
            TreeNodes.Clear();
            TotalFiles = 0;
            TotalSize = 0;
            TotalDirectories = 0;
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (AnalyzeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FindDuplicatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BuildTreeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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

    public class FileTypeStatsViewModel
    {
        public FileTypeStatsViewModel(FileTypeStats stats)
        {
            Extension = stats.Extension;
            Count = stats.Count;
            TotalSize = stats.TotalSize;
        }

        public string Extension { get; }
        public int Count { get; }
        public long TotalSize { get; }
        public string TotalSizeFormatted => FormatSize(TotalSize);

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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

    public class FileInfoViewModel
    {
        public FileInfoViewModel(System.IO.FileInfo file)
        {
            Name = file.Name;
            FullPath = file.FullName;
            Size = file.Length;
            Created = file.CreationTime;
            Modified = file.LastWriteTime;
        }

        public string Name { get; }
        public string FullPath { get; }
        public long Size { get; }
        public DateTime Created { get; }
        public DateTime Modified { get; }
        public string SizeFormatted => FormatSize(Size);

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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

    public class FileAgeViewModel
    {
        public string AgeRange { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class FileDuplicateGroupViewModel
    {
        public FileDuplicateGroupViewModel(DuplicateFileGroup group)
        {
            Hash = group.Hash;
            Size = group.Size;
            Files = new ObservableCollection<string>(group.Files);
            FileCount = group.Files.Count;
        }

        public string Hash { get; }
        public long Size { get; }
        public int FileCount { get; }
        public ObservableCollection<string> Files { get; }
        public string SizeFormatted => FormatSize(Size);
        public string WastedSpace => FormatSize(Size * (FileCount - 1));

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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

    public class DirectoryTreeNodeViewModel
    {
        public DirectoryTreeNodeViewModel(DirectoryTreeNode node)
        {
            Name = node.Name;
            FullPath = node.FullPath;
            IsDirectory = node.IsDirectory;
            Size = node.Size;
            FileCount = node.FileCount;
            Children = new ObservableCollection<DirectoryTreeNodeViewModel>(
                node.Children.Select(c => new DirectoryTreeNodeViewModel(c)));
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public long Size { get; }
        public int FileCount { get; }
        public ObservableCollection<DirectoryTreeNodeViewModel> Children { get; }
        public string SizeFormatted => FormatSize(Size);

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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
}
