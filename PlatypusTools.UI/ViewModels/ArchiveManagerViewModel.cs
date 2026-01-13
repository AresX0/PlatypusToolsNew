using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using PlatypusTools.Core.Models.Archive;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Archive Manager tool.
    /// </summary>
    public class ArchiveManagerViewModel : BindableBase
    {
        private readonly ArchiveService _service;
        private CancellationTokenSource? _cts;
        
        public ArchiveManagerViewModel()
        {
            _service = new ArchiveService();
            Entries = new ObservableCollection<ArchiveEntry>();
            SelectedEntries = new ObservableCollection<ArchiveEntry>();
            
            // Commands
            OpenArchiveCommand = new RelayCommand(_ => OpenArchive());
            ExtractAllCommand = new AsyncRelayCommand(ExtractAllAsync, () => Entries.Any());
            ExtractSelectedCommand = new AsyncRelayCommand(ExtractSelectedAsync, () => SelectedEntries.Any());
            CreateArchiveCommand = new RelayCommand(_ => CreateArchive());
            AddFilesCommand = new RelayCommand(_ => AddFiles(), _ => !string.IsNullOrEmpty(CurrentArchivePath) && CurrentArchivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            TestArchiveCommand = new AsyncRelayCommand(TestArchiveAsync, () => !string.IsNullOrEmpty(CurrentArchivePath));
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsProcessing);
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());
        }
        
        #region Properties
        
        public ObservableCollection<ArchiveEntry> Entries { get; }
        public ObservableCollection<ArchiveEntry> SelectedEntries { get; }
        
        private string? _currentArchivePath;
        public string? CurrentArchivePath
        {
            get => _currentArchivePath;
            set
            {
                if (SetProperty(ref _currentArchivePath, value))
                {
                    RaisePropertyChanged(nameof(ArchiveFileName));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        public string ArchiveFileName => Path.GetFileName(CurrentArchivePath ?? "");
        
        private ArchiveInfo? _archiveInfo;
        public ArchiveInfo? ArchiveInfo
        {
            get => _archiveInfo;
            set => SetProperty(ref _archiveInfo, value);
        }
        
        private ArchiveEntry? _selectedEntry;
        public ArchiveEntry? SelectedEntry
        {
            get => _selectedEntry;
            set => SetProperty(ref _selectedEntry, value);
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
        
        private string _currentFile = string.Empty;
        public string CurrentFile
        {
            get => _currentFile;
            set => SetProperty(ref _currentFile, value);
        }
        
        // Create archive settings
        private ArchiveFormat _selectedFormat = ArchiveFormat.Zip;
        public ArchiveFormat SelectedFormat
        {
            get => _selectedFormat;
            set => SetProperty(ref _selectedFormat, value);
        }
        
        private CompressionLevel _selectedLevel = CompressionLevel.Normal;
        public CompressionLevel SelectedLevel
        {
            get => _selectedLevel;
            set => SetProperty(ref _selectedLevel, value);
        }
        
        private string? _password;
        public string? Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }
        
        public ArchiveFormat[] AvailableFormats { get; } = Enum.GetValues<ArchiveFormat>();
        public CompressionLevel[] CompressionLevels { get; } = Enum.GetValues<CompressionLevel>();
        
        #endregion
        
        #region Commands
        
        public ICommand OpenArchiveCommand { get; }
        public ICommand ExtractAllCommand { get; }
        public ICommand ExtractSelectedCommand { get; }
        public ICommand CreateArchiveCommand { get; }
        public ICommand AddFilesCommand { get; }
        public ICommand TestArchiveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        
        #endregion
        
        #region Methods
        
        private void OpenArchive()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Archive Files|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz|ZIP Files|*.zip|7-Zip Files|*.7z|RAR Files|*.rar|TAR Files|*.tar;*.gz;*.tgz|All Files|*.*",
                Title = "Open Archive"
            };
            
            if (dialog.ShowDialog() == true)
            {
                _ = LoadArchiveAsync(dialog.FileName);
            }
        }
        
        private async Task LoadArchiveAsync(string path)
        {
            try
            {
                IsProcessing = true;
                StatusMessage = "Loading archive...";
                CurrentArchivePath = path;
                
                Entries.Clear();
                SelectedEntries.Clear();
                
                // Get archive info
                ArchiveInfo = _service.GetArchiveInfo(path);
                
                // Load entries
                var entries = await _service.GetEntriesAsync(path, Password);
                foreach (var entry in entries)
                {
                    Entries.Add(entry);
                }
                
                StatusMessage = $"Loaded {Entries.Count} entries";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        private async Task ExtractAllAsync()
        {
            var outputDir = SelectOutputDirectory();
            if (string.IsNullOrEmpty(outputDir)) return;
            
            await ExtractAsync(outputDir, null);
        }
        
        private async Task ExtractSelectedAsync()
        {
            if (!SelectedEntries.Any()) return;
            
            var outputDir = SelectOutputDirectory();
            if (string.IsNullOrEmpty(outputDir)) return;
            
            var selected = SelectedEntries.Select(e => e.Path).ToList();
            await ExtractAsync(outputDir, selected);
        }
        
        private async Task ExtractAsync(string outputDir, System.Collections.Generic.List<string>? selectedPaths)
        {
            if (string.IsNullOrEmpty(CurrentArchivePath)) return;
            
            try
            {
                IsProcessing = true;
                _cts = new CancellationTokenSource();
                
                var options = new ArchiveExtractOptions
                {
                    OutputDirectory = outputDir,
                    Password = Password,
                    OverwriteExisting = true,
                    PreserveFolderStructure = true,
                    SelectedEntries = selectedPaths
                };
                
                var progress = new Progress<ArchiveProgress>(p =>
                {
                    Progress = p.PercentComplete;
                    CurrentFile = p.CurrentFile;
                    StatusMessage = $"Extracting {p.CurrentFileIndex}/{p.TotalFiles}: {Path.GetFileName(p.CurrentFile)}";
                });
                
                await _service.ExtractAsync(CurrentArchivePath, options, progress, _cts.Token);
                
                StatusMessage = "Extraction complete";
                
                // Open output folder
                System.Diagnostics.Process.Start("explorer.exe", outputDir);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Extraction cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                Progress = 0;
                CurrentFile = string.Empty;
            }
        }
        
        private void CreateArchive()
        {
            // Select files/folders to add
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to archive",
                Filter = "All Files|*.*"
            };
            
            if (dialog.ShowDialog() != true || !dialog.FileNames.Any())
                return;
            
            // Select output file
            var saveDialog = new SaveFileDialog
            {
                Filter = GetSaveFilter(),
                Title = "Save Archive As",
                DefaultExt = GetDefaultExtension()
            };
            
            if (saveDialog.ShowDialog() != true)
                return;
            
            _ = CreateArchiveAsync(saveDialog.FileName, dialog.FileNames);
        }
        
        private async Task CreateArchiveAsync(string outputPath, string[] sourcePaths)
        {
            try
            {
                IsProcessing = true;
                _cts = new CancellationTokenSource();
                
                var options = new ArchiveCreateOptions
                {
                    Format = SelectedFormat,
                    Level = SelectedLevel,
                    Password = Password,
                    IncludeRootFolder = false,
                    PreserveFolderStructure = true
                };
                
                var progress = new Progress<ArchiveProgress>(p =>
                {
                    Progress = p.PercentComplete;
                    CurrentFile = p.CurrentFile;
                    StatusMessage = $"Compressing {p.CurrentFileIndex}/{p.TotalFiles}: {p.CurrentFile}";
                });
                
                await _service.CreateAsync(outputPath, sourcePaths, options, progress, _cts.Token);
                
                StatusMessage = $"Archive created: {Path.GetFileName(outputPath)}";
                
                // Load the created archive
                await LoadArchiveAsync(outputPath);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Creation cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                Progress = 0;
            }
        }
        
        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to add",
                Filter = "All Files|*.*"
            };
            
            if (dialog.ShowDialog() == true && dialog.FileNames.Any())
            {
                _ = AddFilesAsync(dialog.FileNames);
            }
        }
        
        private async Task AddFilesAsync(string[] files)
        {
            if (string.IsNullOrEmpty(CurrentArchivePath)) return;
            
            try
            {
                IsProcessing = true;
                _cts = new CancellationTokenSource();
                
                var progress = new Progress<ArchiveProgress>(p =>
                {
                    Progress = (double)p.CurrentFileIndex / p.TotalFiles * 100;
                    StatusMessage = $"Adding {p.CurrentFileIndex}/{p.TotalFiles}: {p.CurrentFile}";
                });
                
                await _service.AddToArchiveAsync(CurrentArchivePath, files, progress, _cts.Token);
                
                // Reload archive
                await LoadArchiveAsync(CurrentArchivePath);
                
                StatusMessage = $"Added {files.Length} files";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                Progress = 0;
            }
        }
        
        private async Task TestArchiveAsync()
        {
            if (string.IsNullOrEmpty(CurrentArchivePath)) return;
            
            try
            {
                IsProcessing = true;
                StatusMessage = "Testing archive integrity...";
                
                var (isValid, errors) = await _service.TestArchiveAsync(CurrentArchivePath, Password);
                
                if (isValid)
                {
                    StatusMessage = "Archive is valid - no errors found";
                }
                else
                {
                    StatusMessage = $"Archive has {errors.Count} error(s)";
                    // Could show errors in a dialog
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Test error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        private void Cancel()
        {
            _cts?.Cancel();
            StatusMessage = "Cancelling...";
        }
        
        private void SelectAll()
        {
            SelectedEntries.Clear();
            foreach (var entry in Entries)
            {
                entry.IsSelected = true;
                SelectedEntries.Add(entry);
            }
            RaiseCommandsCanExecuteChanged();
        }
        
        private void SelectNone()
        {
            foreach (var entry in Entries)
            {
                entry.IsSelected = false;
            }
            SelectedEntries.Clear();
            RaiseCommandsCanExecuteChanged();
        }
        
        private string? SelectOutputDirectory()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select extraction destination",
                ShowNewFolderButton = true
            };
            
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK 
                ? dialog.SelectedPath 
                : null;
        }
        
        private string GetSaveFilter()
        {
            return SelectedFormat switch
            {
                ArchiveFormat.Zip => "ZIP Archive|*.zip",
                ArchiveFormat.SevenZip => "7-Zip Archive|*.7z",
                ArchiveFormat.Tar => "TAR Archive|*.tar",
                ArchiveFormat.GZip => "GZip Archive|*.gz",
                ArchiveFormat.TarGz => "TAR.GZ Archive|*.tar.gz",
                _ => "ZIP Archive|*.zip"
            };
        }
        
        private string GetDefaultExtension()
        {
            return SelectedFormat switch
            {
                ArchiveFormat.Zip => ".zip",
                ArchiveFormat.SevenZip => ".7z",
                ArchiveFormat.Tar => ".tar",
                ArchiveFormat.GZip => ".gz",
                ArchiveFormat.TarGz => ".tar.gz",
                _ => ".zip"
            };
        }
        
        private void RaiseCommandsCanExecuteChanged()
        {
            ((AsyncRelayCommand)ExtractAllCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ExtractSelectedCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)TestArchiveCommand).RaiseCanExecuteChanged();
            ((RelayCommand)AddFilesCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
        }
        
        #endregion
    }
}
