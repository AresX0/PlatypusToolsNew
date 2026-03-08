using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the enhanced Website Downloader tab.
    /// Supports scanning, download queue, hash-based dedup, profiles,
    /// speed tracking, pause/resume, history, retry, and configurable options.
    /// </summary>
    public class WebsiteDownloaderViewModel : BindableBase, IDisposable
    {
        private WebsiteDownloaderService _service;
        private readonly WebDownloadHashDatabase _hashDatabase;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly System.Collections.Generic.List<DownloadHistoryEntry> _history = [];
        private readonly System.Collections.Generic.List<string> _skippedFiles = [];
        private bool _disposed;

        private string _url = string.Empty;
        private string _outputDirectory = string.Empty;
        private bool _downloadImages = true;
        private bool _downloadVideos = true;
        private bool _downloadDocuments = true;
        private bool _downloadArchives = true;
        private bool _recursiveCrawl = false;
        private int _maxDepth = 1;
        private string? _urlPattern;
        private bool _followPagination = true;
        private int _maxPaginationPages = 100;
        private int _maxConcurrentDownloads = 3;
        private int _requestDelayMs = 500;
        private bool _skipDuplicates = true;
        private int _speedLimitKbps;
        private string? _proxyUrl;
        private bool _mirrorDirectoryStructure = true;
        private bool _isScanning = false;
        private bool _isDownloading = false;
        private bool _isPaused = false;
        private int _progress = 0;
        private string _statusMessage = string.Empty;
        private SiteProfile? _selectedProfile;
        private string _logText = string.Empty;
        private string _speedText = string.Empty;
        private string _etaText = string.Empty;
        private int _completedCount;
        private int _failedCount;
        private int _skippedCount;
        private string _historyText = string.Empty;
        private string _historyFilter = string.Empty;

        public WebsiteDownloaderViewModel()
        {
            _hashDatabase = new WebDownloadHashDatabase();
            _service = new WebsiteDownloaderService(_hashDatabase);

            OutputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Website Downloads");

            DownloadItems = new ObservableCollection<DownloadItemViewModel>();
            Profiles = new ObservableCollection<SiteProfile>(SiteProfile.GetBuiltInProfiles());
            LoadCustomProfiles();

            ScanCommand = new AsyncRelayCommand(ScanUrlAsync, () => !IsScanning && !string.IsNullOrWhiteSpace(Url));
            StartDownloadCommand = new AsyncRelayCommand(StartDownloadAsync, () => !IsDownloading && DownloadItems.Any(i => i.IsSelected));
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsScanning || IsDownloading);
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            ClearCommand = new RelayCommand(_ => Clear());
            ClearLogCommand = new RelayCommand(_ => LogText = string.Empty);
            SaveProfileCommand = new RelayCommand(_ => SaveCurrentAsProfile());
            DeleteProfileCommand = new RelayCommand(_ => DeleteSelectedProfile(), _ => SelectedProfile != null && SelectedProfile.IsCustom);
            EditProfileCommand = new RelayCommand(_ => EditSelectedProfile(), _ => SelectedProfile != null && SelectedProfile.IsCustom);
        }

        #region Properties

        public string Url
        {
            get => _url;
            set { if (SetProperty(ref _url, value)) RaiseCommandsCanExecuteChanged(); }
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set => SetProperty(ref _outputDirectory, value);
        }

        public bool DownloadImages { get => _downloadImages; set => SetProperty(ref _downloadImages, value); }
        public bool DownloadVideos { get => _downloadVideos; set => SetProperty(ref _downloadVideos, value); }
        public bool DownloadDocuments { get => _downloadDocuments; set => SetProperty(ref _downloadDocuments, value); }
        public bool DownloadArchives { get => _downloadArchives; set => SetProperty(ref _downloadArchives, value); }
        public bool RecursiveCrawl { get => _recursiveCrawl; set => SetProperty(ref _recursiveCrawl, value); }
        public int MaxDepth { get => _maxDepth; set => SetProperty(ref _maxDepth, value); }
        public string? UrlPattern { get => _urlPattern; set => SetProperty(ref _urlPattern, value); }
        public bool FollowPagination { get => _followPagination; set => SetProperty(ref _followPagination, value); }
        public int MaxPaginationPages { get => _maxPaginationPages; set => SetProperty(ref _maxPaginationPages, value); }
        public int MaxConcurrentDownloads { get => _maxConcurrentDownloads; set => SetProperty(ref _maxConcurrentDownloads, value); }
        public int RequestDelayMs { get => _requestDelayMs; set => SetProperty(ref _requestDelayMs, value); }
        public bool SkipDuplicates { get => _skipDuplicates; set => SetProperty(ref _skipDuplicates, value); }
        public int SpeedLimitKbps { get => _speedLimitKbps; set => SetProperty(ref _speedLimitKbps, value); }
        public string? ProxyUrl { get => _proxyUrl; set => SetProperty(ref _proxyUrl, value); }
        public bool MirrorDirectoryStructure { get => _mirrorDirectoryStructure; set => SetProperty(ref _mirrorDirectoryStructure, value); }

        public bool IsScanning
        {
            get => _isScanning;
            set { if (SetProperty(ref _isScanning, value)) RaiseCommandsCanExecuteChanged(); }
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set { if (SetProperty(ref _isDownloading, value)) RaiseCommandsCanExecuteChanged(); }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty(ref _isPaused, value);
        }

        public int Progress { get => _progress; set => SetProperty(ref _progress, value); }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public string LogText { get => _logText; set => SetProperty(ref _logText, value); }
        public string SpeedText { get => _speedText; set => SetProperty(ref _speedText, value); }
        public string EtaText { get => _etaText; set => SetProperty(ref _etaText, value); }
        public int CompletedCount { get => _completedCount; set => SetProperty(ref _completedCount, value); }
        public int FailedCount { get => _failedCount; set => SetProperty(ref _failedCount, value); }
        public int SkippedCount { get => _skippedCount; set => SetProperty(ref _skippedCount, value); }
        public string HistoryText { get => _historyText; set => SetProperty(ref _historyText, value); }
        public string HistoryFilter
        {
            get => _historyFilter;
            set { if (SetProperty(ref _historyFilter, value)) RefreshHistory(); }
        }

        public SiteProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    if (value != null) ApplyProfile(value);
                    RaiseProfileCommandsCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<DownloadItemViewModel> DownloadItems { get; }
        public ObservableCollection<SiteProfile> Profiles { get; }

        #endregion

        #region Commands

        public ICommand ScanCommand { get; }
        public ICommand StartDownloadCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand EditProfileCommand { get; }

        #endregion

        #region Profile

        private static readonly string CustomProfilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "WebDownloaderProfiles.json");

        public void ApplyProfile(SiteProfile profile)
        {
            Url = profile.Url;
            DownloadImages = profile.Options.DownloadImages;
            DownloadVideos = profile.Options.DownloadVideos;
            DownloadDocuments = profile.Options.DownloadDocuments;
            DownloadArchives = profile.Options.DownloadArchives;
            RecursiveCrawl = profile.Options.RecursiveCrawl;
            MaxDepth = profile.Options.MaxDepth;
            UrlPattern = profile.Options.UrlPattern;
            FollowPagination = profile.Options.FollowPagination;
            MaxPaginationPages = profile.Options.MaxPaginationPages;
            MaxConcurrentDownloads = profile.Options.MaxConcurrentDownloads;
            RequestDelayMs = profile.Options.RequestDelayMs;
            SkipDuplicates = profile.Options.SkipDuplicates;
            SpeedLimitKbps = profile.Options.SpeedLimitKbps;
            MirrorDirectoryStructure = profile.Options.MirrorDirectoryStructure;

            AppendLog($"Loaded profile: {profile.Name}");
            if (profile.AdditionalUrls.Count > 0)
                AppendLog($"  {profile.AdditionalUrls.Count} sub-URLs will be scanned");
        }

        /// <summary>
        /// Saves the current UI settings as a new custom profile.
        /// </summary>
        public void SaveCurrentAsProfile()
        {
            var name = SelectedProfile?.IsCustom == true ? SelectedProfile.Name : "";
            var inputName = PromptForProfileName(name);
            if (string.IsNullOrWhiteSpace(inputName)) return;

            // Check if updating an existing custom profile
            var existing = Profiles.FirstOrDefault(p => p.IsCustom && p.Name.Equals(inputName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Url = Url;
                existing.Options = BuildOptions();
                existing.Description = $"Custom profile (updated {DateTime.Now:g})";
                SelectedProfile = existing;
                AppendLog($"Updated profile: {existing.Name}");
            }
            else
            {
                var profile = new SiteProfile
                {
                    Name = inputName,
                    Description = $"Custom profile (created {DateTime.Now:g})",
                    Url = Url,
                    Options = BuildOptions(),
                    IsCustom = true,
                };
                Profiles.Add(profile);
                SelectedProfile = profile;
                AppendLog($"Created custom profile: {inputName}");
            }

            SaveCustomProfiles();
            RaiseProfileCommandsCanExecuteChanged();
        }

        /// <summary>
        /// Updates the selected custom profile with the current UI settings.
        /// </summary>
        public void EditSelectedProfile()
        {
            if (SelectedProfile is not { IsCustom: true } profile) return;

            profile.Url = Url;
            profile.Options = BuildOptions();
            profile.Description = $"Custom profile (updated {DateTime.Now:g})";
            SaveCustomProfiles();
            AppendLog($"Updated profile: {profile.Name}");
        }

        /// <summary>
        /// Deletes the selected custom profile.
        /// </summary>
        public void DeleteSelectedProfile()
        {
            if (SelectedProfile is not { IsCustom: true } profile) return;

            var result = MessageBox.Show(
                $"Delete profile \"{profile.Name}\"?",
                "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            Profiles.Remove(profile);
            SelectedProfile = Profiles.FirstOrDefault();
            SaveCustomProfiles();
            AppendLog($"Deleted profile: {profile.Name}");
            RaiseProfileCommandsCanExecuteChanged();
        }

        private static string? PromptForProfileName(string defaultName)
        {
            // Simple input box via MessageBox + InputBox pattern
            var inputWindow = new Window
            {
                Title = "Save Profile",
                Width = 400,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Application.Current.MainWindow
            };

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Profile name:", Margin = new Thickness(0, 0, 0, 8) });
            var textBox = new System.Windows.Controls.TextBox { Text = defaultName, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(textBox);

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "Save",
                Width = 80,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);
            inputWindow.Content = panel;

            string? result = null;
            okBtn.Click += (_, _) => { result = textBox.Text; inputWindow.DialogResult = true; };

            textBox.Focus();
            textBox.SelectAll();
            inputWindow.ShowDialog();
            return result;
        }

        private void LoadCustomProfiles()
        {
            try
            {
                if (!File.Exists(CustomProfilesPath)) return;
                var json = File.ReadAllText(CustomProfilesPath);
                var custom = JsonSerializer.Deserialize<System.Collections.Generic.List<SiteProfile>>(json, _jsonOptions);
                if (custom == null) return;
                foreach (var p in custom)
                {
                    p.IsCustom = true;
                    Profiles.Add(p);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to load custom profiles: {ex.Message}");
            }
        }

        private void SaveCustomProfiles()
        {
            try
            {
                var dir = Path.GetDirectoryName(CustomProfilesPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var customProfiles = Profiles.Where(p => p.IsCustom).ToList();
                var json = JsonSerializer.Serialize(customProfiles, _jsonOptions);
                File.WriteAllText(CustomProfilesPath, json);
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to save custom profiles: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private void RaiseProfileCommandsCanExecuteChanged()
        {
            (DeleteProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EditProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region Scan

        public async Task ScanUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(Url))
                return;

            IsScanning = true;
            DownloadItems.Clear();
            ResetCounters();
            StatusMessage = "Scanning website...";
            Progress = 0;
            _cancellationTokenSource = new CancellationTokenSource();

            // Update global status bar
            StatusBarViewModel.Instance.StartOperation("Scanning website...", 0, true);

            try
            {
                // Recreate service with current proxy settings
                _service.Dispose();
                _service = new WebsiteDownloaderService(_hashDatabase, ProxyUrl);

                var options = BuildOptions();
                var scanProgress = new Progress<ScanProgress>(p =>
                {
                    StatusMessage = p.Message;
                    AppendLog($"[Scan] {p.Message}");
                });

                System.Collections.Generic.List<DownloadItem> items;

                if (_selectedProfile?.AdditionalUrls.Count > 0)
                {
                    AppendLog($"Scanning {_selectedProfile.AdditionalUrls.Count} data set URLs...");
                    items = await Task.Run(() =>
                        _service.ScanMultipleAsync(
                            _selectedProfile.AdditionalUrls, options, scanProgress, _cancellationTokenSource.Token),
                        _cancellationTokenSource.Token);
                }
                else
                {
                    items = await Task.Run(() =>
                        _service.ScanUrlAsync(Url, options, scanProgress, _cancellationTokenSource.Token),
                        _cancellationTokenSource.Token);
                }

                // Batch add for performance
                const int batchSize = 100;
                for (int i = 0; i < items.Count; i += batchSize)
                {
                    foreach (var item in items.Skip(i).Take(batchSize))
                        DownloadItems.Add(new DownloadItemViewModel(item));
                    if (i + batchSize < items.Count) await Task.Delay(10);
                }

                StatusMessage = $"Found {DownloadItems.Count} files";
                AppendLog($"Scan complete: {DownloadItems.Count} files found");
                StatusBarViewModel.Instance.CompleteOperation($"Found {DownloadItems.Count} items");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan cancelled";
                AppendLog("Scan cancelled by user");
                StatusBarViewModel.Instance.CompleteOperation("Scan cancelled");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppendLog($"[Error] {ex.Message}");
                StatusBarViewModel.Instance.CompleteOperation($"Error: {ex.Message}");
            }
            finally
            {
                IsScanning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        #endregion

        #region Download

        public async Task StartDownloadAsync()
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                StatusMessage = "Please select an output directory";
                return;
            }

            if (!Directory.Exists(OutputDirectory))
            {
                try { Directory.CreateDirectory(OutputDirectory); }
                catch (Exception ex) { StatusMessage = $"Error creating directory: {ex.Message}"; return; }
            }

            // Rebuild service with current settings
            _service.SpeedLimitKbps = SpeedLimitKbps;
            _service.MirrorDirectoryStructure = MirrorDirectoryStructure;

            IsDownloading = true;
            IsPaused = false;
            Progress = 0;
            ResetCounters();
            _cancellationTokenSource = new CancellationTokenSource();

            var selectedItems = DownloadItems.Where(i => i.IsSelected).ToList();
            var completed = 0;
            var failed = 0;
            var skippedDupes = 0;
            var total = selectedItems.Count;
            var overallTracker = new SpeedTracker();

            StatusBarViewModel.Instance.StartOperation("Downloading files...", total, true);

            try
            {
                // Pre-scan for hash dedup
                if (SkipDuplicates)
                {
                    AppendLog("Scanning existing files for duplicates...");
                    StatusMessage = "Scanning existing files for duplicates...";
                    var hashProgress = new Progress<(int scanned, int total, string currentFile)>(p =>
                        StatusMessage = $"Hashing existing files: {p.scanned}/{p.total} — {p.currentFile}");
                    await Task.Run(() =>
                        _hashDatabase.ScanExistingFilesAsync(OutputDirectory, hashProgress, _cancellationTokenSource.Token),
                        _cancellationTokenSource.Token);
                    AppendLog($"Hash database loaded: {_hashDatabase.Count} known files");
                }

                AppendLog($"Starting download of {total} files to {OutputDirectory}");
                overallTracker.Start(0);

                using var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
                var dispatcher = Application.Current.Dispatcher;

                var tasks = selectedItems.Select(async item =>
                {
                    await semaphore.WaitAsync(_cancellationTokenSource.Token);
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        dispatcher.Invoke(() => item.Status = "Downloading");

                        var progress = new Progress<DownloadProgress>(p =>
                        {
                            dispatcher.Invoke(() =>
                            {
                                item.Progress = p.Percentage;
                                overallTracker.Update(p.BytesDownloaded);
                                SpeedText = p.SpeedText;
                                EtaText = p.EtaText;
                            });
                        });

                        var result = await _service.DownloadWithRetryAsync(
                            item.Item, OutputDirectory, 3, SkipDuplicates, progress, _cancellationTokenSource.Token);

                        sw.Stop();
                        var entry = new DownloadHistoryEntry
                        {
                            Url = item.Url,
                            FileName = item.FileName,
                            BytesDownloaded = result.BytesDownloaded,
                            Hash = result.Hash,
                            Duration = sw.Elapsed
                        };

                        if (result.SkippedDuplicate)
                        {
                            dispatcher.Invoke(() => { item.Status = "Skipped (duplicate)"; item.Progress = 100; });
                            Interlocked.Increment(ref skippedDupes);
                            Interlocked.Increment(ref completed);
                            entry.Status = "Skipped";
                            lock (_skippedFiles) _skippedFiles.Add(item.FileName);
                        }
                        else if (result.Success)
                        {
                            dispatcher.Invoke(() => { item.Status = "Completed"; item.Progress = 100; });
                            Interlocked.Increment(ref completed);
                            entry.Status = "Completed";
                            entry.SpeedKbps = sw.Elapsed.TotalSeconds > 0 ? result.BytesDownloaded / 1024.0 / sw.Elapsed.TotalSeconds : 0;
                        }
                        else
                        {
                            dispatcher.Invoke(() => { item.Status = "Failed"; item.ErrorMessage = result.ErrorMessage; });
                            Interlocked.Increment(ref failed);
                            entry.Status = "Failed";
                            entry.ErrorMessage = result.ErrorMessage;
                            AppendLog($"[Failed] {item.FileName}: {result.ErrorMessage}");
                        }

                        lock (_history) _history.Add(entry);

                        var done = Interlocked.Add(ref completed, 0) + Interlocked.Add(ref failed, 0);
                        var pct = total > 0 ? (done * 100) / total : 0;
                        var dupes = Interlocked.Add(ref skippedDupes, 0);

                        dispatcher.Invoke(() =>
                        {
                            Progress = pct;
                            CompletedCount = Interlocked.Add(ref completed, 0);
                            FailedCount = Interlocked.Add(ref failed, 0);
                            SkippedCount = dupes;
                            StatusMessage = $"Downloaded {completed}/{total} ({failed} failed, {dupes} dupes skipped)";
                            StatusBarViewModel.Instance.UpdateProgress(done, StatusMessage);
                        });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                var finalDupes = Interlocked.Add(ref skippedDupes, 0);
                StatusMessage = $"Complete: {completed} downloaded, {failed} failed, {finalDupes} duplicates skipped";
                AppendLog($"Download complete: {completed} ok, {failed} failed, {finalDupes} duplicates skipped");
                SpeedText = string.Empty;
                EtaText = string.Empty;
                StatusBarViewModel.Instance.CompleteOperation(StatusMessage);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download cancelled";
                AppendLog("Download cancelled by user");
                StatusBarViewModel.Instance.CompleteOperation("Download cancelled");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppendLog($"[Error] {ex.Message}");
                StatusBarViewModel.Instance.CompleteOperation($"Error: {ex.Message}");
            }
            finally
            {
                IsDownloading = false;
                IsPaused = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        #endregion

        #region Pause / Resume / Cancel

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
            AppendLog("Cancel requested");
        }

        public void PauseDownloads()
        {
            _service.Pause();
            IsPaused = true;
            StatusMessage = "Paused";
            AppendLog("Downloads paused");
        }

        public void ResumeDownloads()
        {
            _service.Resume();
            IsPaused = false;
            StatusMessage = "Resumed";
            AppendLog("Downloads resumed");
        }

        #endregion

        #region Selection

        public void SelectAll()
        {
            foreach (var item in DownloadItems) item.IsSelected = true;
        }

        public void SelectNone()
        {
            foreach (var item in DownloadItems) item.IsSelected = false;
        }

        public void SelectByType(string type)
        {
            foreach (var item in DownloadItems)
                item.IsSelected = item.Type.Equals(type, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region History

        public void RefreshHistory()
        {
            var sb = new StringBuilder();
            var filter = HistoryFilter?.Trim() ?? "";

            System.Collections.Generic.IEnumerable<DownloadHistoryEntry> entries;
            lock (_history) entries = _history.ToList();

            if (!string.IsNullOrEmpty(filter))
            {
                entries = entries.Where(e =>
                    e.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    e.Url.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    e.Status.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var e in entries.OrderByDescending(e => e.Timestamp))
                sb.AppendLine(e.ToString());

            HistoryText = sb.ToString();
        }

        public void ClearHistory()
        {
            lock (_history) _history.Clear();
            HistoryText = string.Empty;
        }

        #endregion

        #region Test Link / Retry / Skipped

        public async Task TestLinkAsync(string url)
        {
            StatusMessage = "Testing link...";
            AppendLog($"Testing: {url}");
            var (reachable, message) = await _service.TestDownloadLinkAsync(url);
            if (reachable)
            {
                StatusMessage = $"Link OK — {message}";
                AppendLog($"Test link OK: {url} — {message}");
            }
            else
            {
                StatusMessage = $"Link failed — {message}";
                AppendLog($"Test link failed: {url} — {message}");
            }
        }

        public async Task RetryItemAsync(DownloadItemViewModel item)
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory)) return;

            item.Status = "Downloading";
            item.Progress = 0;
            item.ErrorMessage = null;

            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    item.Progress = p.Percentage;
                    SpeedText = p.SpeedText;
                });

                var result = await _service.DownloadWithRetryAsync(
                    item.Item, OutputDirectory, 3, SkipDuplicates, progress, CancellationToken.None);

                item.Status = result.Success ? "Completed" : "Failed";
                item.Progress = result.Success ? 100 : item.Progress;
                if (!result.Success) item.ErrorMessage = result.ErrorMessage;

                AppendLog(result.Success
                    ? $"Retry succeeded: {item.FileName}"
                    : $"Retry failed: {item.FileName} — {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                item.Status = "Failed";
                item.ErrorMessage = ex.Message;
                AppendLog($"Retry error: {item.FileName} — {ex.Message}");
            }
        }

        public async Task ExportFileTreeAsync(string path)
        {
            if (!Directory.Exists(OutputDirectory))
            {
                StatusMessage = "Output directory does not exist.";
                return;
            }
            await Task.Run(() => WebsiteDownloaderService.ExportFileTree(OutputDirectory, path));
            StatusMessage = $"File tree exported to {path}";
            AppendLog($"File tree exported to {path}");
        }

        public void ShowSkippedFiles()
        {
            string text;
            lock (_skippedFiles)
            {
                text = _skippedFiles.Count == 0
                    ? "No files skipped in this session."
                    : string.Join("\n", _skippedFiles);
            }
            MessageBox.Show(text, $"Skipped Files ({_skippedFiles.Count})", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Browse / Utility

        public void BrowseOutput()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Output Directory",
                InitialDirectory = OutputDirectory
            };
            if (dialog.ShowDialog() == true)
                OutputDirectory = dialog.FolderName;
        }

        private void Clear()
        {
            DownloadItems.Clear();
            ResetCounters();
            StatusMessage = string.Empty;
            Progress = 0;
        }

        private void ResetCounters()
        {
            CompletedCount = 0;
            FailedCount = 0;
            SkippedCount = 0;
            SpeedText = string.Empty;
            EtaText = string.Empty;
        }

        #endregion

        #region Internal

        private DownloadOptions BuildOptions() => new()
        {
            DownloadImages = DownloadImages,
            DownloadVideos = DownloadVideos,
            DownloadDocuments = DownloadDocuments,
            DownloadArchives = DownloadArchives,
            RecursiveCrawl = RecursiveCrawl,
            MaxDepth = MaxDepth,
            UrlPattern = UrlPattern,
            FollowPagination = FollowPagination,
            MaxPaginationPages = MaxPaginationPages,
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            RequestDelayMs = RequestDelayMs,
            SkipDuplicates = SkipDuplicates,
            ProxyUrl = ProxyUrl,
            SpeedLimitKbps = SpeedLimitKbps,
            MirrorDirectoryStructure = MirrorDirectoryStructure,
        };

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var line = $"[{timestamp}] {message}";
            LogText += line + "\n";
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (ScanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (StartDownloadCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service?.Dispose();
            _hashDatabase?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    public class DownloadItemViewModel : BindableBase
    {
        private bool _isSelected = true;
        private string _status;
        private int _progress;
        private string? _errorMessage;

        public DownloadItemViewModel(DownloadItem item)
        {
            Item = item;
            _status = item.Status;
        }

        public DownloadItem Item { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string Url => Item.Url;
        public string FileName => Item.FileName;
        public string Type => Item.Type;
        public int PageNumber => Item.PageNumber;

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }
    }
}
