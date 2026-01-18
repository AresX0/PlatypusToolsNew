using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using PlatypusTools.Core.Services;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.ViewModels
{
    public class DuplicatesViewModel : BindableBase
    {
        private CancellationTokenSource? _scanCancellationTokenSource;
        private readonly ImageSimilarityService _similarityService;
        private readonly VideoSimilarityService _videoSimilarityService;

        public DuplicatesViewModel()
        {
            Groups = new ObservableCollection<DuplicateGroupViewModel>();
            SimilarImageGroups = new ObservableCollection<SimilarImageGroupViewModel>();
            SimilarVideoGroups = new ObservableCollection<SimilarVideoGroupViewModel>();
            UseRecycleBin = true; // default to safe operation
            _similarityService = new ImageSimilarityService();
            _similarityService.ProgressChanged += (s, p) => 
            {
                App.Current?.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Processing: {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles})";
                    ScanProgress = p.ProgressPercent;
                    StatusBarViewModel.Instance.UpdateProgress(p.ProgressPercent, StatusMessage);
                });
            };
            
            _videoSimilarityService = new VideoSimilarityService();
            _videoSimilarityService.ProgressChanged += (s, p) =>
            {
                App.Current?.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"{p.CurrentPhase} {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles})";
                    ScanProgress = p.ProgressPercent;
                    StatusBarViewModel.Instance.UpdateProgress(p.ProgressPercent, StatusMessage);
                });
            };
            
            BrowseCommand = new RelayCommand(_ => Browse());
            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !IsScanning);
            ScanSimilarCommand = new RelayCommand(async _ => await ScanSimilarAsync(), _ => !IsScanning);
            ScanSimilarVideosCommand = new RelayCommand(async _ => await ScanSimilarVideosAsync(), _ => !IsScanning);
            CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => Groups.Any(g => g.Files.Any(f => f.IsSelected)) || SimilarImageGroups.Any(g => g.Images.Any(i => i.IsSelected)) || SimilarVideoGroups.Any(g => g.Videos.Any(v => v.IsSelected)));

            OpenFileCommand = new RelayCommand(obj => OpenFile(obj as string));
            OpenFolderCommand = new RelayCommand(obj => OpenFolder(obj as string));
            RenameFileCommand = new RelayCommand(obj => RenameFile(obj as string));
            PreviewFileCommand = new RelayCommand(obj => PreviewFile(obj as string));
            StageFileCommand = new RelayCommand(obj => StageFile(obj as string));
            OpenStagingCommand = new RelayCommand(_ => OpenStaging());
            ToggleStagingCommand = new RelayCommand(_ => StagingVisible = !StagingVisible);
            // Per-group selection commands
            SelectNewestCommand = new RelayCommand(_ => SelectNewest());
            SelectOldestCommand = new RelayCommand(_ => SelectOldest());
            SelectLargestCommand = new RelayCommand(_ => SelectLargest());
            SelectSmallestCommand = new RelayCommand(_ => SelectSmallest());
            KeepOneCommand = new RelayCommand(_ => KeepOnePerGroup());

        }

        public ObservableCollection<DuplicateGroupViewModel> Groups { get; }
        
        public ObservableCollection<SimilarImageGroupViewModel> SimilarImageGroups { get; }
        
        public ObservableCollection<SimilarVideoGroupViewModel> SimilarVideoGroups { get; }
        
        private int _similarityThreshold = 90;
        public int SimilarityThreshold 
        { 
            get => _similarityThreshold; 
            set { _similarityThreshold = value; RaisePropertyChanged(); } 
        }
        
        private int _videoSimilarityThreshold = 85;
        public int VideoSimilarityThreshold 
        { 
            get => _videoSimilarityThreshold; 
            set { _videoSimilarityThreshold = value; RaisePropertyChanged(); } 
        }
        
        private bool _showSimilarImages = false;
        public bool ShowSimilarImages 
        { 
            get => _showSimilarImages; 
            set { _showSimilarImages = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowDuplicatesGrid)); } 
        }
        
        private bool _showSimilarVideos = false;
        public bool ShowSimilarVideos 
        { 
            get => _showSimilarVideos; 
            set { _showSimilarVideos = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowDuplicatesGrid)); } 
        }
        
        /// <summary>
        /// Shows the duplicates grid when neither similar images nor videos mode is active.
        /// </summary>
        public bool ShowDuplicatesGrid => !_showSimilarImages && !_showSimilarVideos;
        
        private double _scanProgress = 0;
        public double ScanProgress 
        { 
            get => _scanProgress; 
            set { _scanProgress = value; RaisePropertyChanged(); } 
        }

        public StagingViewModel Staging { get; } = new StagingViewModel();

        private bool _stagingVisible = false;
        public bool StagingVisible { get => _stagingVisible; set { _stagingVisible = value; RaisePropertyChanged(); } }

        public ICommand ToggleStagingCommand { get; }

        private string _folderPath = string.Empty;
        public string FolderPath { get => _folderPath; set { _folderPath = value; RaisePropertyChanged(); } }

        private bool _dryRun = true;
        public bool DryRun { get => _dryRun; set { _dryRun = value; RaisePropertyChanged(); } }

        private bool _isScanning = false;
        public bool IsScanning { get => _isScanning; set { _isScanning = value; RaisePropertyChanged(); } }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; RaisePropertyChanged(); } }

        public ICommand BrowseCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand ScanSimilarCommand { get; }
        public ICommand ScanSimilarVideosCommand { get; }
        public ICommand CancelScanCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand RenameFileCommand { get; }
        public ICommand StageFileCommand { get; }
        public ICommand OpenStagingCommand { get; }

        public ICommand SelectNewestCommand { get; }
        public ICommand SelectOldestCommand { get; }
        public ICommand SelectLargestCommand { get; }
        public ICommand SelectSmallestCommand { get; }
        public ICommand KeepOneCommand { get; }
        public ICommand PreviewFileCommand { get; }
        private void Browse()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.ShowNewFolderButton = true;
            if (!string.IsNullOrWhiteSpace(FolderPath)) dlg.SelectedPath = FolderPath;
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK) FolderPath = dlg.SelectedPath;
        }

        private async Task ScanAsync()
        {
            if (IsScanning) return;
            if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
            {
                StatusMessage = "Please select a valid folder";
                return;
            }

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _scanCancellationTokenSource.Token;

            IsScanning = true;
            ShowSimilarImages = false;
            ShowSimilarVideos = false;
            StatusMessage = "Scanning for duplicates...";
            
            // Update global status bar
            StatusBarViewModel.Instance.StartOperation("Scanning for duplicates...", isCancellable: true);
            Groups.Clear();
            SimilarImageGroups.Clear();
            SimilarVideoGroups.Clear();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                var groups = await Task.Run(() => DuplicatesScanner.FindDuplicates(new[] { FolderPath }, recurse: true).ToList(), cancellationToken);
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var g in groups)
                    {
                        Groups.Add(new DuplicateGroupViewModel(g));
                    }
                    
                    StatusMessage = $"Found {Groups.Count} duplicate groups ({Groups.Sum(g => g.Files.Count)} total files)";
                }
                else
                {
                    StatusMessage = "Scan canceled";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan canceled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                StatusBarViewModel.Instance.CompleteOperation(StatusMessage);
                ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
            }
        }

        private void CancelScan()
        {
            _scanCancellationTokenSource?.Cancel();
            StatusMessage = "Canceling...";
        }
        
        private async Task ScanSimilarAsync()
        {
            if (IsScanning) return;
            if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
            {
                StatusMessage = "Please select a valid folder";
                return;
            }

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _scanCancellationTokenSource.Token;

            IsScanning = true;
            ShowSimilarImages = true;
            StatusMessage = "Scanning for similar images...";
            ScanProgress = 0;
            
            StatusBarViewModel.Instance.StartOperation("Scanning for similar images...", isCancellable: true);
            SimilarImageGroups.Clear();
            SimilarVideoGroups.Clear();
            Groups.Clear();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                var groups = await _similarityService.FindSimilarImagesAsync(
                    new[] { FolderPath }, 
                    SimilarityThreshold, 
                    true, 
                    cancellationToken);
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var g in groups)
                    {
                        SimilarImageGroups.Add(new SimilarImageGroupViewModel(g));
                    }
                    
                    StatusMessage = $"Found {SimilarImageGroups.Count} similar image groups ({SimilarImageGroups.Sum(g => g.Images.Count)} total images)";
                }
                else
                {
                    StatusMessage = "Scan canceled";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan canceled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                ScanProgress = 100;
                StatusBarViewModel.Instance.CompleteOperation(StatusMessage);
                ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
            }
        }

        private async Task ScanSimilarVideosAsync()
        {
            if (IsScanning) return;
            if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
            {
                StatusMessage = "Please select a valid folder";
                return;
            }

            _scanCancellationTokenSource?.Cancel();
            _scanCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _scanCancellationTokenSource.Token;

            IsScanning = true;
            ShowSimilarImages = false;
            ShowSimilarVideos = true;
            StatusMessage = "Scanning for similar videos...";
            ScanProgress = 0;
            
            StatusBarViewModel.Instance.StartOperation("Scanning for similar videos...", isCancellable: true);
            SimilarImageGroups.Clear();
            SimilarVideoGroups.Clear();
            Groups.Clear();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                var groups = await _videoSimilarityService.FindSimilarVideosAsync(
                    new[] { FolderPath }, 
                    VideoSimilarityThreshold, 
                    true, 
                    cancellationToken);
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var g in groups)
                    {
                        SimilarVideoGroups.Add(new SimilarVideoGroupViewModel(g));
                    }
                    
                    StatusMessage = $"Found {SimilarVideoGroups.Count} similar video groups ({SimilarVideoGroups.Sum(g => g.Videos.Count)} total videos)";
                }
                else
                {
                    StatusMessage = "Scan canceled";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan canceled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                ScanProgress = 100;
                StatusBarViewModel.Instance.CompleteOperation(StatusMessage);
                ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
            }
        }

        private void SelectNewest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderByDescending(f => File.GetLastWriteTimeUtc(f.Path)).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void SelectOldest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderBy(f => File.GetLastWriteTimeUtc(f.Path)).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void SelectLargest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderByDescending(f => new FileInfo(f.Path).Length).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void SelectSmallest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderBy(f => new FileInfo(f.Path).Length).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void KeepOnePerGroup()
        {
            foreach (var g in Groups)
            {
                var keep = g.Files.FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f != keep;
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void OpenFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        private void OpenFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var dir = Path.GetDirectoryName(path) ?? path;
            if (!Directory.Exists(dir)) return;
            try { Process.Start(new ProcessStartInfo("explorer", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
        }

        private void PreviewFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            LastPreviewedFilePath = path;
            if (PreviewVisible)
            {
                // If embedded preview is enabled, update property and avoid modal window
                return;
            }
            var dlg = new Views.PreviewWindow(path) { Owner = System.Windows.Application.Current?.MainWindow };
            dlg.ShowDialog();
        }

        private string _lastPreviewedFilePath = string.Empty;
        public string LastPreviewedFilePath { get => _lastPreviewedFilePath; set { _lastPreviewedFilePath = value; RaisePropertyChanged(); } }

        private bool _previewVisible = false;
        public bool PreviewVisible { get => _previewVisible; set { _previewVisible = value; RaisePropertyChanged(); } }

        private void OpenStaging()
        {
            var dlg = new Views.StagingWindow() { Owner = System.Windows.Application.Current?.MainWindow };
            dlg.ShowDialog();
        }

        private void RenameFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var dlg = new Views.InputDialogWindow("Rename duplicate", Path.GetFileName(path)) { Owner = System.Windows.Application.Current?.MainWindow };
            var res = dlg.ShowDialog();
            if (res != true) return;
            var newName = dlg.EnteredText;
            if (string.IsNullOrWhiteSpace(newName)) return;
            var dest = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, newName);
            try { File.Move(path, dest); } catch { System.Windows.MessageBox.Show("Rename failed.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
            // Refresh scan
            _ = ScanAsync();
        }

        private void StageFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                var destPath = StageFileToStaging(path);
                if (destPath == null) return;
                var stagingRoot = Path.GetDirectoryName(destPath) ?? Path.Combine(Path.GetTempPath(), "PlatypusTools", "DuplicatesStaging");
                var res = System.Windows.MessageBox.Show($"Staged '{path}' to '{destPath}'.\nOpen staging folder?", "Staged", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                if (res == System.Windows.MessageBoxResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"\"{stagingRoot}\"") { UseShellExecute = true }); } catch { }
                }
            }
            catch
            {
                System.Windows.MessageBox.Show("Staging failed.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public string? StageFileToStaging(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var stagingRoot = Path.Combine(Path.GetTempPath(), "PlatypusTools", "DuplicatesStaging");
            Directory.CreateDirectory(stagingRoot);
            var destName = Path.GetFileName(path);
            var destPath = Path.Combine(stagingRoot, destName);
            var i = 1;
            while (File.Exists(destPath))
            {
                destPath = Path.Combine(stagingRoot, Path.GetFileNameWithoutExtension(destName) + $" ({i})" + Path.GetExtension(destName));
                i++;
            }
            File.Copy(path, destPath);
            // write metadata so staging UI can know original path
            try
            {
                File.WriteAllText(destPath + ".meta", path);
            }
            catch { }
            return destPath;
        }
        public bool UseRecycleBin { get; set; }

        private void DeleteSelected()
        {
            var files = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Select(f => f.Path).ToList();
            if (files.Count == 0) return;
            if (DryRun)
            {
                System.Windows.MessageBox.Show($"Dry-run: would remove {files.Count} files.", "Preview", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var confirm = System.Windows.MessageBox.Show($"Proceed to remove {files.Count} files?", "Confirm", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            DeleteSelectedConfirmed();
        }

        // Public API for tests and non-UI callers to delete selected files without confirmation dialogs
        public void DeleteSelectedConfirmed()
        {
            var files = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Select(f => f.Path).ToList();
            if (files.Count == 0) return;

            foreach (var f in files)
            {
                try
                {
                    if (UseRecycleBin)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        File.Delete(f);
                    }
                }
                catch { }
            }
            // Refresh scan
            _ = ScanAsync();
        }
    }
}
