using System;
using System.Collections.Generic;
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
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class DuplicatesViewModel : BindableBase
    {
        private CancellationTokenSource? _scanCancellationTokenSource;
        private readonly ImageSimilarityService _similarityService;
        private readonly VideoSimilarityService _videoSimilarityService;
        private readonly AudioSimilarityService _audioSimilarityService;
        private readonly MediaFingerprintCacheService _fingerprintCacheService;
        private long _lastProgressTicks;
        private long _lastLogTicks;
        private static readonly long ProgressThrottleTicks = TimeSpan.FromMilliseconds(150).Ticks;
        private static readonly long LogThrottleTicks = TimeSpan.FromMilliseconds(250).Ticks;

        public DuplicatesViewModel()
        {
            Groups = new ObservableCollection<DuplicateGroupViewModel>();
            SimilarImageGroups = new ObservableCollection<SimilarImageGroupViewModel>();
            SimilarVideoGroups = new ObservableCollection<SimilarVideoGroupViewModel>();
            SimilarAudioGroups = new ObservableCollection<SimilarAudioGroupViewModel>();
            UseRecycleBin = true; // default to safe operation

            // Initialize fingerprint cache service for faster subsequent scans
            _fingerprintCacheService = new MediaFingerprintCacheService();
            _ = _fingerprintCacheService.InitializeAsync();

            _similarityService = new ImageSimilarityService();
            _similarityService.ProgressChanged += (s, p) => 
            {
                var now = DateTime.UtcNow.Ticks;
                if (now - Interlocked.Read(ref _lastProgressTicks) < ProgressThrottleTicks && p.ProgressPercent < 100)
                    return;
                Interlocked.Exchange(ref _lastProgressTicks, now);
                _ = App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"Processing: {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles})";
                    ScanProgress = p.ProgressPercent;
                    StatusBarViewModel.Instance.UpdateProgress(p.ProgressPercent, StatusMessage);
                });
            };
            
            _videoSimilarityService = new VideoSimilarityService(_fingerprintCacheService);
            _videoSimilarityService.ProgressChanged += (s, p) =>
            {
                var now = DateTime.UtcNow.Ticks;
                if (now - Interlocked.Read(ref _lastProgressTicks) < ProgressThrottleTicks && p.ProgressPercent < 100)
                    return;
                Interlocked.Exchange(ref _lastProgressTicks, now);
                _ = App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"{p.CurrentPhase} {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles})";
                    ScanProgress = p.ProgressPercent;
                    StatusBarViewModel.Instance.UpdateProgress(p.ProgressPercent, StatusMessage);
                });
            };
            _videoSimilarityService.LogMessage += (s, msg) => AddScanLogEntry(msg);
            
            _audioSimilarityService = new AudioSimilarityService(_fingerprintCacheService);
            _audioSimilarityService.ProgressChanged += (s, p) =>
            {
                var now = DateTime.UtcNow.Ticks;
                if (now - Interlocked.Read(ref _lastProgressTicks) < ProgressThrottleTicks && p.ProgressPercent < 100)
                    return;
                Interlocked.Exchange(ref _lastProgressTicks, now);
                _ = App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = $"{p.CurrentPhase} {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles})";
                    ScanProgress = p.ProgressPercent;
                    StatusBarViewModel.Instance.UpdateProgress(p.ProgressPercent, StatusMessage);
                });
            };
            _audioSimilarityService.LogMessage += (s, msg) => AddScanLogEntry(msg);
            
            BrowseCommand = new RelayCommand(_ => Browse());
            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !IsScanning && !IsDeleting);
            ScanSimilarCommand = new RelayCommand(async _ => await ScanSimilarAsync(), _ => !IsScanning && !IsDeleting);
            ScanSimilarVideosCommand = new RelayCommand(async _ => await ScanSimilarVideosAsync(), _ => !IsScanning && !IsDeleting);
            ScanSimilarAudioCommand = new RelayCommand(async _ => await ScanSimilarAudioAsync(), _ => !IsScanning && !IsDeleting);
            CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
            ToggleScanLogCommand = new RelayCommand(_ => ShowScanLog = !ShowScanLog);
            ClearScanLogCommand = new RelayCommand(_ => ScanLog = string.Empty);
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => !IsDeleting && (Groups.Any(g => g.Files.Any(f => f.IsSelected)) || SimilarImageGroups.Any(g => g.Images.Any(i => i.IsSelected)) || SimilarVideoGroups.Any(g => g.Videos.Any(v => v.IsSelected)) || SimilarAudioGroups.Any(g => g.AudioFiles.Any(a => a.IsSelected))));

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
        
        public ObservableCollection<SimilarAudioGroupViewModel> SimilarAudioGroups { get; }
        
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
        
        private int _audioSimilarityThreshold = 85;
        public int AudioSimilarityThreshold 
        { 
            get => _audioSimilarityThreshold; 
            set { _audioSimilarityThreshold = value; RaisePropertyChanged(); } 
        }
        
        private Core.Services.AudioScanMode _audioScanMode = Core.Services.AudioScanMode.Fast;
        public Core.Services.AudioScanMode AudioScanMode 
        { 
            get => _audioScanMode; 
            set { _audioScanMode = value; RaisePropertyChanged(); } 
        }
        
        private Core.Services.VideoScanMode _videoScanMode = Core.Services.VideoScanMode.Fast;
        public Core.Services.VideoScanMode VideoScanMode 
        { 
            get => _videoScanMode; 
            set { _videoScanMode = value; RaisePropertyChanged(); } 
        }
        
        /// <summary>
        /// Gets available audio scan modes for UI binding.
        /// </summary>
        public IEnumerable<Core.Services.AudioScanMode> AudioScanModes => 
            Enum.GetValues(typeof(Core.Services.AudioScanMode)).Cast<Core.Services.AudioScanMode>();
        
        /// <summary>
        /// Gets available video scan modes for UI binding.
        /// </summary>
        public IEnumerable<Core.Services.VideoScanMode> VideoScanModes => 
            Enum.GetValues(typeof(Core.Services.VideoScanMode)).Cast<Core.Services.VideoScanMode>();
        
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
        
        private bool _showSimilarAudio = false;
        public bool ShowSimilarAudio 
        { 
            get => _showSimilarAudio; 
            set { _showSimilarAudio = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowDuplicatesGrid)); } 
        }
        
        /// <summary>
        /// Shows the duplicates grid when no similarity mode is active.
        /// </summary>
        public bool ShowDuplicatesGrid => !_showSimilarImages && !_showSimilarVideos && !_showSimilarAudio;
        
        private double _scanProgress = 0;
        public double ScanProgress 
        { 
            get => _scanProgress; 
            set { _scanProgress = value; RaisePropertyChanged(); } 
        }

        private static readonly int MaxCpuThreads = Math.Max(1, (int)(Environment.ProcessorCount * 0.75));

        private int _scanThreadCount = 0;
        /// <summary>
        /// Number of threads for parallel hashing. 0 = auto (75% of CPU cores).
        /// </summary>
        public int ScanThreadCount 
        { 
            get => _scanThreadCount; 
            set { _scanThreadCount = Math.Max(0, Math.Min(value, MaxCpuThreads)); RaisePropertyChanged(); RaisePropertyChanged(nameof(ScanThreadCountDisplay)); } 
        }
        
        /// <summary>
        /// Display text for the thread count slider.
        /// </summary>
        public string ScanThreadCountDisplay => ScanThreadCount == 0 ? $"Auto ({MaxCpuThreads})" : ScanThreadCount.ToString();

        /// <summary>
        /// Maximum allowed thread count for UI slider binding.
        /// </summary>
        public int MaxThreadCount => MaxCpuThreads;

        private int _ffmpegTimeoutSeconds = 20;
        /// <summary>
        /// Per-file FFmpeg timeout in seconds. Files exceeding this are skipped.
        /// </summary>
        public int FfmpegTimeoutSeconds 
        { 
            get => _ffmpegTimeoutSeconds; 
            set { _ffmpegTimeoutSeconds = Math.Max(5, Math.Min(value, 300)); RaisePropertyChanged(); } 
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

        private string _scanLog = string.Empty;
        public string ScanLog { get => _scanLog; set { _scanLog = value; RaisePropertyChanged(); } }

        private bool _showScanLog = false;
        public bool ShowScanLog { get => _showScanLog; set { _showScanLog = value; RaisePropertyChanged(); } }

        public ICommand ToggleScanLogCommand { get; }
        public ICommand ClearScanLogCommand { get; }

        public ICommand BrowseCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand ScanSimilarCommand { get; }
        public ICommand ScanSimilarVideosCommand { get; }
        public ICommand ScanSimilarAudioCommand { get; }
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

        /// <summary>
        /// Exposes ScanAsync for testability. Use ScanCommand for UI binding.
        /// </summary>
        public Task ScanForDuplicatesAsync() => ScanAsync();

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
            ScanProgress = 0;
            
            // Update global status bar
            StatusBarViewModel.Instance.StartOperation("Scanning for duplicates...", isCancellable: true);
            Groups.Clear();
            SimilarImageGroups.Clear();
            SimilarVideoGroups.Clear();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                // Use async version with progress reporting and batch updates
                var groups = await DuplicatesScanner.FindDuplicatesAsync(
                    new[] { FolderPath },
                    recurse: true,
                    onProgress: (current, total, message) =>
                    {
                        var now = DateTime.UtcNow.Ticks;
                        if (now - Interlocked.Read(ref _lastProgressTicks) < ProgressThrottleTicks && current < total)
                            return;
                        Interlocked.Exchange(ref _lastProgressTicks, now);
                        _ = App.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            StatusMessage = message;
                            if (total > 0)
                                ScanProgress = (current * 100) / total;
                        });
                    },
                    cancellationToken: cancellationToken,
                    onBatchProcessed: (batchGroups) =>
                    {
                        // Update UI with intermediate results
                        _ = App.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            Groups.Clear();
                            foreach (var g in batchGroups)
                            {
                                Groups.Add(new DuplicateGroupViewModel(g));
                            }
                        });
                    },
                    threadCount: ScanThreadCount);
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    Groups.Clear();
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
                ScanProgress = 0;
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

        private void AddScanLogEntry(string message, bool force = false)
        {
            var now = DateTime.UtcNow.Ticks;
            if (!force && now - Interlocked.Read(ref _lastLogTicks) < LogThrottleTicks)
                return;
            Interlocked.Exchange(ref _lastLogTicks, now);
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var entry = $"[{timestamp}] {message}";
            _ = App.Current?.Dispatcher.InvokeAsync(() =>
            {
                ScanLog = string.IsNullOrEmpty(ScanLog) ? entry : ScanLog + Environment.NewLine + entry;
            });
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
            ShowSimilarVideos = false;
            ShowSimilarAudio = false;
            StatusMessage = "Scanning for similar images...";
            ScanProgress = 0;
            
            StatusBarViewModel.Instance.StartOperation("Scanning for similar images...", isCancellable: true);
            SimilarImageGroups.Clear();
            SimilarVideoGroups.Clear();
            SimilarAudioGroups.Clear();
            Groups.Clear();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarAudioCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                var groups = await Task.Run(() => _similarityService.FindSimilarImagesAsync(
                    new[] { FolderPath }, 
                    SimilarityThreshold, 
                    true, 
                    cancellationToken));
                
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
                ((RelayCommand)ScanSimilarAudioCommand).RaiseCanExecuteChanged();
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
            ShowSimilarAudio = false;
            var modeText = VideoScanMode == Core.Services.VideoScanMode.Fast ? "(Fast mode)" : "(Thorough mode)";
            StatusMessage = $"Scanning for similar videos {modeText}...";
            ScanProgress = 0;
            
            StatusBarViewModel.Instance.StartOperation($"Scanning for similar videos {modeText}...", isCancellable: true);
            SimilarImageGroups.Clear();
            SimilarVideoGroups.Clear();
            SimilarAudioGroups.Clear();
            Groups.Clear();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarAudioCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                // Set the scan mode before scanning
                _videoSimilarityService.ScanMode = VideoScanMode;
                _videoSimilarityService.FileAnalysisTimeoutSeconds = FfmpegTimeoutSeconds;
                AddScanLogEntry($"Starting video similarity scan - {modeText} in {FolderPath} (timeout: {FfmpegTimeoutSeconds}s)", force: true);
                
                var groups = await Task.Run(() => _videoSimilarityService.FindSimilarVideosAsync(
                    new[] { FolderPath }, 
                    VideoSimilarityThreshold, 
                    true, 
                    cancellationToken));
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var g in groups)
                    {
                        SimilarVideoGroups.Add(new SimilarVideoGroupViewModel(g));
                    }
                    
                    StatusMessage = $"Found {SimilarVideoGroups.Count} similar video groups ({SimilarVideoGroups.Sum(g => g.Videos.Count)} total files)";
                    AddScanLogEntry($"Scan complete: {SimilarVideoGroups.Count} groups found ({SimilarVideoGroups.Sum(g => g.Videos.Count)} files)", force: true);
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
                ((RelayCommand)ScanSimilarAudioCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
            }
        }

        private async Task ScanSimilarAudioAsync()
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
            ShowSimilarAudio = true;
            var modeText = AudioScanMode == Core.Services.AudioScanMode.Fast ? "(Fast mode)" : "(Thorough mode)";
            StatusMessage = $"Scanning for similar sounds {modeText}...";
            ScanProgress = 0;
            
            StatusBarViewModel.Instance.StartOperation($"Scanning for similar sounds {modeText}...", isCancellable: true);
            SimilarImageGroups.Clear();
            SimilarVideoGroups.Clear();
            SimilarAudioGroups.Clear();
            Groups.Clear();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarAudioCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();

            try
            {
                // Set the scan mode before scanning
                _audioSimilarityService.ScanMode = AudioScanMode;
                _audioSimilarityService.FileAnalysisTimeoutSeconds = FfmpegTimeoutSeconds;
                AddScanLogEntry($"Starting audio similarity scan - {modeText} in {FolderPath} (timeout: {FfmpegTimeoutSeconds}s)", force: true);
                
                var groups = await Task.Run(() => _audioSimilarityService.FindSimilarAudioAsync(
                    new[] { FolderPath }, 
                    AudioSimilarityThreshold, 
                    true, 
                    cancellationToken));
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var g in groups)
                    {
                        SimilarAudioGroups.Add(new SimilarAudioGroupViewModel(g));
                    }
                    
                    StatusMessage = $"Found {SimilarAudioGroups.Count} similar audio groups ({SimilarAudioGroups.Sum(g => g.AudioFiles.Count)} total files)";
                    AddScanLogEntry($"Scan complete: {SimilarAudioGroups.Count} groups found ({SimilarAudioGroups.Sum(g => g.AudioFiles.Count)} files)", force: true);
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
                ((RelayCommand)ScanSimilarAudioCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
            }
        }

        private void SelectNewest()
        {
            if (ShowSimilarImages)
            {
                foreach (var g in SimilarImageGroups)
                {
                    var chosen = g.Images.OrderByDescending(f => File.GetLastWriteTimeUtc(f.FilePath)).FirstOrDefault();
                    foreach (var f in g.Images) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarVideos)
            {
                foreach (var g in SimilarVideoGroups)
                {
                    var chosen = g.Videos.OrderByDescending(f => File.GetLastWriteTimeUtc(f.FilePath)).FirstOrDefault();
                    foreach (var f in g.Videos) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarAudio)
            {
                foreach (var g in SimilarAudioGroups)
                {
                    var chosen = g.AudioFiles.OrderByDescending(f => File.GetLastWriteTimeUtc(f.FilePath)).FirstOrDefault();
                    foreach (var f in g.AudioFiles) f.IsSelected = f == chosen;
                }
            }
            else
            {
                foreach (var g in Groups)
                {
                    var chosen = g.Files.OrderByDescending(f => File.GetLastWriteTimeUtc(f.Path)).FirstOrDefault();
                    foreach (var f in g.Files) f.IsSelected = f == chosen;
                }
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void SelectOldest()
        {
            if (ShowSimilarImages)
            {
                foreach (var g in SimilarImageGroups)
                {
                    var chosen = g.Images.OrderBy(f => File.GetLastWriteTimeUtc(f.FilePath)).FirstOrDefault();
                    foreach (var f in g.Images) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarVideos)
            {
                foreach (var g in SimilarVideoGroups)
                {
                    var chosen = g.Videos.OrderBy(f => File.GetLastWriteTimeUtc(f.FilePath)).FirstOrDefault();
                    foreach (var f in g.Videos) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarAudio)
            {
                foreach (var g in SimilarAudioGroups)
                {
                    var chosen = g.AudioFiles.OrderBy(f => File.GetLastWriteTimeUtc(f.FilePath)).FirstOrDefault();
                    foreach (var f in g.AudioFiles) f.IsSelected = f == chosen;
                }
            }
            else
            {
                foreach (var g in Groups)
                {
                    var chosen = g.Files.OrderBy(f => File.GetLastWriteTimeUtc(f.Path)).FirstOrDefault();
                    foreach (var f in g.Files) f.IsSelected = f == chosen;
                }
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void SelectLargest()
        {
            if (ShowSimilarImages)
            {
                foreach (var g in SimilarImageGroups)
                {
                    var chosen = g.Images.OrderByDescending(f => f.FileSize).FirstOrDefault();
                    foreach (var f in g.Images) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarVideos)
            {
                foreach (var g in SimilarVideoGroups)
                {
                    var chosen = g.Videos.OrderByDescending(f => f.FileSize).FirstOrDefault();
                    foreach (var f in g.Videos) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarAudio)
            {
                foreach (var g in SimilarAudioGroups)
                {
                    var chosen = g.AudioFiles.OrderByDescending(f => f.FileSize).FirstOrDefault();
                    foreach (var f in g.AudioFiles) f.IsSelected = f == chosen;
                }
            }
            else
            {
                foreach (var g in Groups)
                {
                    var chosen = g.Files.OrderByDescending(f => new FileInfo(f.Path).Length).FirstOrDefault();
                    foreach (var f in g.Files) f.IsSelected = f == chosen;
                }
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void SelectSmallest()
        {
            if (ShowSimilarImages)
            {
                foreach (var g in SimilarImageGroups)
                {
                    var chosen = g.Images.OrderBy(f => f.FileSize).FirstOrDefault();
                    foreach (var f in g.Images) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarVideos)
            {
                foreach (var g in SimilarVideoGroups)
                {
                    var chosen = g.Videos.OrderBy(f => f.FileSize).FirstOrDefault();
                    foreach (var f in g.Videos) f.IsSelected = f == chosen;
                }
            }
            else if (ShowSimilarAudio)
            {
                foreach (var g in SimilarAudioGroups)
                {
                    var chosen = g.AudioFiles.OrderBy(f => f.FileSize).FirstOrDefault();
                    foreach (var f in g.AudioFiles) f.IsSelected = f == chosen;
                }
            }
            else
            {
                foreach (var g in Groups)
                {
                    var chosen = g.Files.OrderBy(f => new FileInfo(f.Path).Length).FirstOrDefault();
                    foreach (var f in g.Files) f.IsSelected = f == chosen;
                }
            }
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void KeepOnePerGroup()
        {
            if (ShowSimilarImages)
            {
                foreach (var g in SimilarImageGroups)
                {
                    var keep = g.Images.FirstOrDefault();
                    foreach (var f in g.Images) f.IsSelected = f != keep;
                }
            }
            else if (ShowSimilarVideos)
            {
                foreach (var g in SimilarVideoGroups)
                {
                    var keep = g.Videos.FirstOrDefault();
                    foreach (var f in g.Videos) f.IsSelected = f != keep;
                }
            }
            else if (ShowSimilarAudio)
            {
                foreach (var g in SimilarAudioGroups)
                {
                    var keep = g.AudioFiles.FirstOrDefault();
                    foreach (var f in g.AudioFiles) f.IsSelected = f != keep;
                }
            }
            else
            {
                foreach (var g in Groups)
                {
                    var keep = g.Files.FirstOrDefault();
                    foreach (var f in g.Files) f.IsSelected = f != keep;
                }
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
            try
            {
                File.Move(path, dest);
                UndoRedoService.Instance.RecordRenameWithToast(path, dest);
            }
            catch { System.Windows.MessageBox.Show("Rename failed.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
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
            UndoRedoService.Instance.RecordCopy(path, destPath);
            // write metadata so staging UI can know original path
            try
            {
                File.WriteAllText(destPath + ".meta", path);
            }
            catch { }
            return destPath;
        }
        public bool UseRecycleBin { get; set; }

        private async void DeleteSelected()
        {
            // Handle duplicates (exact matches)
            var files = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Select(f => f.Path).ToList();
            // Handle similar images
            var imageFiles = SimilarImageGroups.SelectMany(g => g.Images).Where(i => i.IsSelected).Select(i => i.FilePath).ToList();
            // Handle similar videos
            var videoFiles = SimilarVideoGroups.SelectMany(g => g.Videos).Where(v => v.IsSelected).Select(v => v.FilePath).ToList();
            // Handle similar audio
            var audioFiles = SimilarAudioGroups.SelectMany(g => g.AudioFiles).Where(a => a.IsSelected).Select(a => a.FilePath).ToList();
            
            var allFiles = files.Concat(imageFiles).Concat(videoFiles).Concat(audioFiles).ToList();
            if (allFiles.Count == 0) return;
            
            if (DryRun)
            {
                System.Windows.MessageBox.Show($"Dry-run: would remove {allFiles.Count} files.", "Preview", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var confirm = System.Windows.MessageBox.Show($"Proceed to remove {allFiles.Count} files?", "Confirm", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            await DeleteSelectedConfirmedAsync();
        }

        private bool _isDeleting = false;
        public bool IsDeleting { get => _isDeleting; set { _isDeleting = value; RaisePropertyChanged(); } }
        
        private double _deleteProgress = 0;
        public double DeleteProgress { get => _deleteProgress; set { _deleteProgress = value; RaisePropertyChanged(); } }

        // Public API for tests and non-UI callers to delete selected files without confirmation dialogs
        public void DeleteSelectedConfirmed()
        {
            // Synchronous wrapper for backwards compatibility with tests
            _ = DeleteSelectedConfirmedAsync();
        }
        
        // Async version with progress updates
        public async Task DeleteSelectedConfirmedAsync()
        {
            if (IsDeleting) return;
            
            int deletedCount = 0;
            var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Collect all selected files from all modes
            var filesToDelete = new List<string>();
            filesToDelete.AddRange(Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Select(f => f.Path));
            filesToDelete.AddRange(SimilarImageGroups.SelectMany(g => g.Images).Where(i => i.IsSelected).Select(i => i.FilePath));
            filesToDelete.AddRange(SimilarVideoGroups.SelectMany(g => g.Videos).Where(v => v.IsSelected).Select(v => v.FilePath));
            filesToDelete.AddRange(SimilarAudioGroups.SelectMany(g => g.AudioFiles).Where(a => a.IsSelected).Select(a => a.FilePath));
            
            if (filesToDelete.Count == 0) return;
            
            IsDeleting = true;
            DeleteProgress = 0;
            int totalFiles = filesToDelete.Count;
            int processedFiles = 0;

            var undoOps = new List<FileOperation>();
            var undoBackupDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "UndoBackups");
            try
            {
                await Task.Run(async () =>
                {
                    foreach (var f in filesToDelete)
                    {
                        try
                        {
                            string? backupPath = null;
                            if (!UseRecycleBin && File.Exists(f))
                            {
                                // Backup file before hard-delete so undo can restore it
                                Directory.CreateDirectory(undoBackupDir);
                                backupPath = Path.Combine(undoBackupDir, Guid.NewGuid().ToString("N") + Path.GetExtension(f));
                                File.Copy(f, backupPath);
                            }
                            if (UseRecycleBin)
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }
                            else
                            {
                                File.Delete(f);
                            }
                            undoOps.Add(new FileOperation { Type = OperationType.Delete, OriginalPath = f, BackupPath = backupPath, Timestamp = DateTime.Now });
                            deletedPaths.Add(f);
                            deletedCount++;
                        }
                        catch { }
                        
                        processedFiles++;
                        var progress = (double)processedFiles / totalFiles * 100;
                        var fileName = Path.GetFileName(f);
                        
                        // Update UI on dispatcher thread
                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            DeleteProgress = progress;
                            StatusMessage = $"Deleting: {fileName} ({processedFiles}/{totalFiles})";
                            StatusBarViewModel.Instance.UpdateProgress(progress, StatusMessage);
                        });
                    }
                });
            }
            finally
            {
                IsDeleting = false;
                DeleteProgress = 0;
            }
            
            // Record batch undo operation
            if (undoOps.Count > 0)
            {
                UndoRedoService.Instance.RecordBatch(undoOps, $"Delete {undoOps.Count} duplicate files");
            }
            
            // Remove deleted files from the UI collections (instead of rescanning)
            // Process duplicate groups
            foreach (var group in Groups.ToList())
            {
                var filesToRemove = group.Files.Where(f => deletedPaths.Contains(f.Path)).ToList();
                foreach (var file in filesToRemove)
                {
                    group.Files.Remove(file);
                }
                
                // If group has 0 or 1 files remaining, remove the group (no longer duplicates)
                if (group.Files.Count <= 1)
                {
                    Groups.Remove(group);
                }
            }
            
            // Process similar image groups
            foreach (var group in SimilarImageGroups.ToList())
            {
                var imagesToRemove = group.Images.Where(i => deletedPaths.Contains(i.FilePath)).ToList();
                foreach (var image in imagesToRemove)
                {
                    group.Images.Remove(image);
                }
                
                // If group has 0 or 1 images remaining, remove the group
                if (group.Images.Count <= 1)
                {
                    SimilarImageGroups.Remove(group);
                }
            }
            
            // Process similar video groups
            foreach (var group in SimilarVideoGroups.ToList())
            {
                var videosToRemove = group.Videos.Where(v => deletedPaths.Contains(v.FilePath)).ToList();
                foreach (var video in videosToRemove)
                {
                    group.Videos.Remove(video);
                }
                
                // If group has 0 or 1 videos remaining, remove the group
                if (group.Videos.Count <= 1)
                {
                    SimilarVideoGroups.Remove(group);
                }
            }
            
            // Process similar audio groups
            foreach (var group in SimilarAudioGroups.ToList())
            {
                var audiosToRemove = group.AudioFiles.Where(a => deletedPaths.Contains(a.FilePath)).ToList();
                foreach (var audio in audiosToRemove)
                {
                    group.AudioFiles.Remove(audio);
                }
                
                // If group has 0 or 1 audio files remaining, remove the group
                if (group.AudioFiles.Count <= 1)
                {
                    SimilarAudioGroups.Remove(group);
                }
            }
            
            StatusMessage = $"Deleted {deletedCount} file(s). {Groups.Count} duplicate groups, {SimilarImageGroups.Count} similar image groups, {SimilarVideoGroups.Count} similar video groups, {SimilarAudioGroups.Count} similar audio groups remaining.";
            StatusBarViewModel.Instance.UpdateProgress(0, StatusMessage);
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarVideosCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ScanSimilarAudioCommand).RaiseCanExecuteChanged();
        }
    }
}
