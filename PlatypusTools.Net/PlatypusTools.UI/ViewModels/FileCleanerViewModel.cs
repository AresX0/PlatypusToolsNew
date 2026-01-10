using PlatypusTools.Core.Models;
using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class FileCleanerViewModel : BindableBase
    {
        private readonly IFileRenamerService _renamerService;
        private List<(string oldPath, string newPath)> _lastOperationBackup = new();

        public ObservableCollection<RenameOperation> PreviewItems { get; } = new();

        // Folder Selection
        private string _folderPath = string.Empty;
        public string FolderPath { get => _folderPath; set { _folderPath = value; RaisePropertyChanged(); } }

        private bool _includeSubfolders = true;
        public bool IncludeSubfolders { get => _includeSubfolders; set { _includeSubfolders = value; RaisePropertyChanged(); } }

        private FileTypeFilter _fileTypeFilter = FileTypeFilter.All;
        public FileTypeFilter FileTypeFilter { get => _fileTypeFilter; set { _fileTypeFilter = value; RaisePropertyChanged(); } }

        // Prefix Management
        private string _newPrefix = string.Empty;
        public string NewPrefix { get => _newPrefix; set { _newPrefix = value; RaisePropertyChanged(); } }

        private string _oldPrefix = string.Empty;
        public string OldPrefix { get => _oldPrefix; set { _oldPrefix = value; RaisePropertyChanged(); } }

        private string _detectedPrefix = string.Empty;
        public string DetectedPrefix { get => _detectedPrefix; set { _detectedPrefix = value; RaisePropertyChanged(); } }

        private string _ignorePrefix = string.Empty;
        public string IgnorePrefix { get => _ignorePrefix; set { _ignorePrefix = value; RaisePropertyChanged(); } }

        private bool _addPrefixToAll;
        public bool AddPrefixToAll { get => _addPrefixToAll; set { _addPrefixToAll = value; RaisePropertyChanged(); } }

        private bool _normalizeCasing;
        public bool NormalizeCasing { get => _normalizeCasing; set { _normalizeCasing = value; RaisePropertyChanged(); } }

        private bool _onlyFilesWithOldPrefix;
        public bool OnlyFilesWithOldPrefix { get => _onlyFilesWithOldPrefix; set { _onlyFilesWithOldPrefix = value; RaisePropertyChanged(); } }

        // Season/Episode Management
        private bool _addSeason;
        public bool AddSeason { get => _addSeason; set { _addSeason = value; RaisePropertyChanged(); } }

        private int _seasonNumber = 1;
        public int SeasonNumber { get => _seasonNumber; set { _seasonNumber = value; RaisePropertyChanged(); } }

        private int _seasonLeadingZeros = 2;
        public int SeasonLeadingZeros { get => _seasonLeadingZeros; set { _seasonLeadingZeros = value; RaisePropertyChanged(); } }

        private bool _addEpisode;
        public bool AddEpisode { get => _addEpisode; set { _addEpisode = value; RaisePropertyChanged(); } }

        private int _startEpisodeNumber = 1;
        public int StartEpisodeNumber { get => _startEpisodeNumber; set { _startEpisodeNumber = value; RaisePropertyChanged(); } }

        private int _episodeLeadingZeros = 2;
        public int EpisodeLeadingZeros { get => _episodeLeadingZeros; set { _episodeLeadingZeros = value; RaisePropertyChanged(); } }

        private bool _renumberAlphabetically;
        public bool RenumberAlphabetically { get => _renumberAlphabetically; set { _renumberAlphabetically = value; RaisePropertyChanged(); } }

        private bool _seasonBeforeEpisode = true;
        public bool SeasonBeforeEpisode { get => _seasonBeforeEpisode; set { _seasonBeforeEpisode = value; RaisePropertyChanged(); } }

        // Filename Cleaning
        private bool _remove720p;
        public bool Remove720p { get => _remove720p; set { _remove720p = value; RaisePropertyChanged(); } }

        private bool _remove1080p;
        public bool Remove1080p { get => _remove1080p; set { _remove1080p = value; RaisePropertyChanged(); } }

        private bool _remove4k;
        public bool Remove4k { get => _remove4k; set { _remove4k = value; RaisePropertyChanged(); } }

        private bool _removeHD;
        public bool RemoveHD { get => _removeHD; set { _removeHD = value; RaisePropertyChanged(); } }

        private string _customTokens = string.Empty;
        public string CustomTokens { get => _customTokens; set { _customTokens = value; RaisePropertyChanged(); } }

        // Normalization
        private NormalizationPreset _normalizationPreset = NormalizationPreset.None;
        public NormalizationPreset NormalizationPreset { get => _normalizationPreset; set { _normalizationPreset = value; RaisePropertyChanged(); } }

        // Operations
        private bool _dryRun = true;
        public bool DryRun { get => _dryRun; set { _dryRun = value; RaisePropertyChanged(); } }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; RaisePropertyChanged(); } }

        private int _selectedCount;
        public int SelectedCount { get => _selectedCount; set { _selectedCount = value; RaisePropertyChanged(); } }

        private int _totalCount;
        public int TotalCount { get => _totalCount; set { _totalCount = value; RaisePropertyChanged(); } }

        // Commands
        public ICommand BrowseFolderCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand DetectPrefixCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand ApplyChangesCommand { get; }
        public ICommand UndoLastCommand { get; }
        public ICommand ResetAllCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ApplyPreviewCommand { get; }

        public FileCleanerViewModel() : this(new FileRenamerService()) { }

        public FileCleanerViewModel(IFileRenamerService renamerService)
        {
            _renamerService = renamerService;

            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            ScanCommand = new RelayCommand(_ => Scan());
            DetectPrefixCommand = new RelayCommand(_ => DetectPrefix());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());
            ApplyChangesCommand = new RelayCommand(_ => ApplyChanges(), _ => PreviewItems.Any(p => p.IsSelected));
            UndoLastCommand = new RelayCommand(_ => UndoLast(), _ => _lastOperationBackup.Any());
            ResetAllCommand = new RelayCommand(_ => ResetAll());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv(), _ => PreviewItems.Any());
            ApplyPreviewCommand = new RelayCommand(_ => ApplyPreview());
        }

        private void BrowseFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder to scan for files",
                ShowNewFolderButton = false
            };

            if (!string.IsNullOrEmpty(FolderPath) && Directory.Exists(FolderPath))
                dialog.SelectedPath = FolderPath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FolderPath = dialog.SelectedPath;
            }
        }

        private void Scan()
        {
            try
            {
                if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
                {
                    MessageBox.Show("Please select a valid folder path.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = "Scanning...";
                PreviewItems.Clear();

                var operations = _renamerService.ScanFolder(FolderPath, IncludeSubfolders, FileTypeFilter);
                
                foreach (var op in operations)
                {
                    PreviewItems.Add(op);
                }

                TotalCount = PreviewItems.Count;
                SelectedCount = PreviewItems.Count(p => p.IsSelected);
                StatusMessage = $"Found {PreviewItems.Count} files";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Scan failed";
            }
        }

        private void DetectPrefix()
        {
            if (!PreviewItems.Any())
            {
                MessageBox.Show("Please scan a folder first.", "No Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var detected = _renamerService.DetectCommonPrefix(PreviewItems.ToList());
            DetectedPrefix = detected;

            if (string.IsNullOrEmpty(detected))
            {
                MessageBox.Show("No common prefix detected.", "Detection Result", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Detected prefix: {detected}", "Detection Result", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ApplyPreview()
        {
            if (!PreviewItems.Any())
                return;

            StatusMessage = "Generating preview...";

            var operations = PreviewItems.ToList();

            // Apply rules in order
            if (!string.IsNullOrEmpty(NewPrefix) || !string.IsNullOrEmpty(OldPrefix))
            {
                _renamerService.ApplyPrefixRules(operations, NewPrefix, OldPrefix, IgnorePrefix, NormalizeCasing, OnlyFilesWithOldPrefix, AddPrefixToAll);
            }

            if (AddSeason || AddEpisode)
            {
                _renamerService.ApplySeasonEpisodeNumbering(operations, 
                    AddSeason ? SeasonNumber : null, 
                    SeasonLeadingZeros, 
                    StartEpisodeNumber, 
                    EpisodeLeadingZeros, 
                    RenumberAlphabetically, 
                    SeasonBeforeEpisode);
            }

            if (Remove720p || Remove1080p || Remove4k || RemoveHD || !string.IsNullOrEmpty(CustomTokens))
            {
                var customTokenArray = string.IsNullOrEmpty(CustomTokens) 
                    ? null 
                    : CustomTokens.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToArray();
                
                _renamerService.ApplyCleaningRules(operations, Remove720p, Remove1080p, Remove4k, RemoveHD, customTokenArray);
            }

            if (NormalizationPreset != NormalizationPreset.None)
            {
                _renamerService.ApplyNormalization(operations, NormalizationPreset);
            }

            // Update UI
            PreviewItems.Clear();
            foreach (var op in operations)
            {
                // Mark status as pending if changes detected
                if (!op.ProposedPath.Equals(op.OriginalPath, StringComparison.OrdinalIgnoreCase))
                    op.Status = RenameStatus.Pending;
                else
                    op.Status = RenameStatus.NoChange;

                PreviewItems.Add(op);
            }

            SelectedCount = PreviewItems.Count(p => p.IsSelected);
            StatusMessage = $"Preview updated - {PreviewItems.Count(p => p.Status == RenameStatus.Pending)} files will be renamed";
        }

        private void SelectAll()
        {
            foreach (var item in PreviewItems)
                item.IsSelected = true;
            SelectedCount = PreviewItems.Count;
        }

        private void SelectNone()
        {
            foreach (var item in PreviewItems)
                item.IsSelected = false;
            SelectedCount = 0;
        }

        private void ApplyChanges()
        {
            try
            {
                var selectedOps = PreviewItems.Where(p => p.IsSelected && p.Status == RenameStatus.Pending).ToList();
                if (!selectedOps.Any())
                {
                    MessageBox.Show("No pending changes to apply.", "No Changes", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var message = DryRun 
                    ? $"Simulate renaming {selectedOps.Count} files?" 
                    : $"Rename {selectedOps.Count} files? This cannot be easily undone.";

                var result = MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;

                StatusMessage = DryRun ? "Simulating..." : "Applying changes...";

                _lastOperationBackup = _renamerService.ApplyChanges(selectedOps, DryRun);

                // Refresh the preview items
                var updatedOps = PreviewItems.ToList();
                PreviewItems.Clear();
                foreach (var op in updatedOps)
                    PreviewItems.Add(op);

                var successCount = PreviewItems.Count(p => p.Status == RenameStatus.Success || p.Status == RenameStatus.DryRun);
                var failCount = PreviewItems.Count(p => p.Status == RenameStatus.Failed);

                StatusMessage = DryRun 
                    ? $"Dry run complete - {successCount} files would be renamed" 
                    : $"Complete - {successCount} files renamed, {failCount} failed";

                MessageBox.Show(StatusMessage, "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Operation failed";
            }
        }

        private void UndoLast()
        {
            try
            {
                if (!_lastOperationBackup.Any())
                {
                    MessageBox.Show("No operations to undo.", "Undo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show($"Undo last operation ({_lastOperationBackup.Count} files)?", 
                    "Confirm Undo", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;

                StatusMessage = "Undoing...";
                _renamerService.UndoChanges(_lastOperationBackup);
                _lastOperationBackup.Clear();

                // Rescan to refresh
                Scan();
                StatusMessage = "Undo complete";
                MessageBox.Show("Operation undone successfully.", "Undo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error undoing changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Undo failed";
            }
        }

        private void ResetAll()
        {
            PreviewItems.Clear();
            NewPrefix = string.Empty;
            OldPrefix = string.Empty;
            DetectedPrefix = string.Empty;
            IgnorePrefix = string.Empty;
            AddPrefixToAll = false;
            NormalizeCasing = false;
            OnlyFilesWithOldPrefix = false;
            AddSeason = false;
            AddEpisode = false;
            Remove720p = false;
            Remove1080p = false;
            Remove4k = false;
            RemoveHD = false;
            CustomTokens = string.Empty;
            NormalizationPreset = NormalizationPreset.None;
            StatusMessage = "Reset complete";
        }

        private void ExportCsv()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = $"FileCleanerExport_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    using var writer = new StreamWriter(dialog.FileName);
                    writer.WriteLine("Selected,Original Name,Proposed Name,Status,Directory");

                    foreach (var item in PreviewItems)
                    {
                        writer.WriteLine($"{item.IsSelected},{item.OriginalFileName},{item.ProposedFileName},{item.Status},{item.Directory}");
                    }

                    MessageBox.Show($"Exported {PreviewItems.Count} items to CSV.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting CSV: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
