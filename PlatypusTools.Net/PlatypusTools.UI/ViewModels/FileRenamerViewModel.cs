using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Models;

namespace PlatypusTools.UI.ViewModels
{
    public class FileRenamerViewModel : BindableBase
    {
        private readonly FileRenamerService _service = new();
        private List<(string oldPath, string newPath)> _lastBackup = new();

        public ObservableCollection<RenameOperation> Operations { get; } = new();

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

        private string _ignorePrefix = string.Empty;
        public string IgnorePrefix { get => _ignorePrefix; set { _ignorePrefix = value; RaisePropertyChanged(); } }

        private bool _normalizeCasing = false;
        public bool NormalizeCasing { get => _normalizeCasing; set { _normalizeCasing = value; RaisePropertyChanged(); } }

        private bool _onlyFilesWithOldPrefix = false;
        public bool OnlyFilesWithOldPrefix { get => _onlyFilesWithOldPrefix; set { _onlyFilesWithOldPrefix = value; RaisePropertyChanged(); } }

        // Season/Episode Numbering
        private int? _seasonNumber;
        public int? SeasonNumber { get => _seasonNumber; set { _seasonNumber = value; RaisePropertyChanged(); } }

        private int _seasonLeadingZeros = 2;
        public int SeasonLeadingZeros { get => _seasonLeadingZeros; set { _seasonLeadingZeros = Math.Clamp(value, 1, 4); RaisePropertyChanged(); } }

        private int _startEpisodeNumber = 1;
        public int StartEpisodeNumber { get => _startEpisodeNumber; set { _startEpisodeNumber = value; RaisePropertyChanged(); } }

        private int _episodeLeadingZeros = 4;
        public int EpisodeLeadingZeros { get => _episodeLeadingZeros; set { _episodeLeadingZeros = Math.Clamp(value, 1, 4); RaisePropertyChanged(); } }

        private bool _renumberAlphabetically = false;
        public bool RenumberAlphabetically { get => _renumberAlphabetically; set { _renumberAlphabetically = value; RaisePropertyChanged(); } }

        private bool _includeSeasonInFormat = false;
        public bool IncludeSeasonInFormat { get => _includeSeasonInFormat; set { _includeSeasonInFormat = value; RaisePropertyChanged(); } }

        // Cleaning
        private bool _removeCommonTokens = false;
        public bool RemoveCommonTokens { get => _removeCommonTokens; set { _removeCommonTokens = value; RaisePropertyChanged(); } }

        private string _customTokens = string.Empty;
        public string CustomTokens { get => _customTokens; set { _customTokens = value; RaisePropertyChanged(); } }

        // Normalization
        private SpaceHandling _spaceHandling = SpaceHandling.None;
        public SpaceHandling SpaceHandling { get => _spaceHandling; set { _spaceHandling = value; RaisePropertyChanged(); } }

        private SymbolConversion _symbolConversion = SymbolConversion.None;
        public SymbolConversion SymbolConversion { get => _symbolConversion; set { _symbolConversion = value; RaisePropertyChanged(); } }

        // Status
        private bool _isProcessing;
        public bool IsProcessing { get => _isProcessing; set { _isProcessing = value; RaisePropertyChanged(); } }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; RaisePropertyChanged(); } }

        public ICommand BrowseFolderCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand PreviewChangesCommand { get; }
        public ICommand ApplyChangesCommand { get; }
        public ICommand UndoLastChangesCommand { get; }
        public ICommand ResetOptionsCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }

        public FileRenamerViewModel()
        {
            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            ScanCommand = new AsyncRelayCommand(ScanAsync);
            PreviewChangesCommand = new AsyncRelayCommand(PreviewChangesAsync);
            ApplyChangesCommand = new AsyncRelayCommand(ApplyChangesAsync);
            UndoLastChangesCommand = new RelayCommand(_ => UndoLastChanges());
            ResetOptionsCommand = new RelayCommand(_ => ResetOptions());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());
        }

        private void BrowseFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FolderPath = dlg.SelectedPath;
            }
        }

        public async Task ScanAsync()
        {
            if (string.IsNullOrWhiteSpace(FolderPath))
            {
                StatusMessage = "Please select a folder";
                return;
            }

            IsProcessing = true;
            StatusMessage = "Scanning...";
            Operations.Clear();

            var ops = await Task.Run(() => _service.ScanFolder(FolderPath, IncludeSubfolders, FileTypeFilter));
            
            // Update UI in batch on UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var op in ops)
                {
                    Operations.Add(op);
                }
            });

            StatusMessage = $"Found {Operations.Count} files";
            IsProcessing = false;
        }

        public async Task PreviewChangesAsync()
        {
            if (!Operations.Any())
            {
                StatusMessage = "No files scanned. Please scan first.";
                return;
            }

            IsProcessing = true;
            StatusMessage = "Generating preview...";

            await Task.Run(() =>
            {
                var ops = Operations.ToList();

                // Apply prefix rules
                if (!string.IsNullOrEmpty(NewPrefix) || !string.IsNullOrEmpty(OldPrefix))
                {
                    _service.ApplyPrefixRules(ops, NewPrefix, OldPrefix, IgnorePrefix, NormalizeCasing, OnlyFilesWithOldPrefix, false);
                }

                // Apply season/episode numbering
                if (RenumberAlphabetically)
                {
                    _service.ApplySeasonEpisodeNumbering(ops, SeasonNumber, SeasonLeadingZeros, StartEpisodeNumber, EpisodeLeadingZeros, RenumberAlphabetically, IncludeSeasonInFormat);
                }

                // Apply cleaning
                if (RemoveCommonTokens || !string.IsNullOrEmpty(CustomTokens))
                {
                    var customTokensArray = string.IsNullOrEmpty(CustomTokens) ? null : CustomTokens.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();
                    _service.ApplyCleaningRules(ops, RemoveCommonTokens, false, false, false, customTokensArray);
                }

                // Apply normalization
                if (SpaceHandling != SpaceHandling.None || SymbolConversion != SymbolConversion.None)
                {
                    var preset = ConvertToPreset(SpaceHandling, SymbolConversion);
                    _service.ApplyNormalization(ops, preset);
                }
            });

            var changedCount = Operations.Count(o => o.ProposedFileName != o.OriginalFileName);
            StatusMessage = $"Preview ready: {changedCount} files will be renamed";
            IsProcessing = false;
        }

        public async Task ApplyChangesAsync()
        {
            var selectedOps = Operations.Where(o => o.IsSelected && o.ProposedPath != o.OriginalPath).ToList();
            if (!selectedOps.Any())
            {
                StatusMessage = "No files selected for renaming";
                return;
            }

            IsProcessing = true;
            StatusMessage = $"Applying changes to {selectedOps.Count} files...";

            await Task.Run(() =>
            {
                _lastBackup = _service.ApplyChanges(Operations.ToList());
            });

            var successCount = Operations.Count(o => o.Status == RenameStatus.Success);
            var failCount = Operations.Count(o => o.Status == RenameStatus.Failed);
            StatusMessage = $"Complete: {successCount} succeeded, {failCount} failed";
            IsProcessing = false;
        }

        private void UndoLastChanges()
        {
            if (!_lastBackup.Any())
            {
                StatusMessage = "No changes to undo";
                return;
            }

            StatusMessage = "Undoing changes...";
            _service.UndoChanges(_lastBackup);
            _lastBackup.Clear();
            StatusMessage = "Changes undone";
        }

        private void ResetOptions()
        {
            NewPrefix = string.Empty;
            OldPrefix = string.Empty;
            IgnorePrefix = string.Empty;
            NormalizeCasing = false;
            OnlyFilesWithOldPrefix = false;
            SeasonNumber = null;
            SeasonLeadingZeros = 2;
            StartEpisodeNumber = 1;
            EpisodeLeadingZeros = 4;
            RenumberAlphabetically = false;
            IncludeSeasonInFormat = false;
            RemoveCommonTokens = false;
            CustomTokens = string.Empty;
            SpaceHandling = SpaceHandling.None;
            SymbolConversion = SymbolConversion.None;
            StatusMessage = "Options reset";
        }

        private void SelectAll()
        {
            foreach (var op in Operations)
                op.IsSelected = true;
        }

        private void SelectNone()
        {
            foreach (var op in Operations)
                op.IsSelected = false;
        }

        private NormalizationPreset ConvertToPreset(SpaceHandling space, SymbolConversion symbol)
        {
            if (space == SpaceHandling.SpacesToDashes && symbol == SymbolConversion.None)
                return NormalizationPreset.SpacesToDashes;
            if (space == SpaceHandling.SpacesToUnderscores && symbol == SymbolConversion.None)
                return NormalizationPreset.SpacesToUnderscores;
            if (space == SpaceHandling.RemoveSpaces && symbol == SymbolConversion.None)
                return NormalizationPreset.RemoveSpaces;
            if (space == SpaceHandling.DashesToSpaces && symbol == SymbolConversion.DashesToSpaces)
                return NormalizationPreset.DashesToSpaces;
            if (space == SpaceHandling.UnderscoresToSpaces && symbol == SymbolConversion.UnderscoresToSpaces)
                return NormalizationPreset.UnderscoresToSpaces;
            if (symbol == SymbolConversion.DashesToUnderscores)
                return NormalizationPreset.DashesToUnderscores;
            if (symbol == SymbolConversion.UnderscoresToDashes)
                return NormalizationPreset.UnderscoresToDashes;
            return NormalizationPreset.None;
        }
    }
}
