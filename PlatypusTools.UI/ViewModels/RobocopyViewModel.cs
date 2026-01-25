using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class RobocopySwitchViewModel : BindableBase
    {
        private bool _isSelected;
        private string _value = string.Empty;

        public string Switch { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool RequiresValue { get; set; }
        public string ValueHint { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; RaisePropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; RaisePropertyChanged(); }
        }

        public RobocopySwitch ToSwitch() => new()
        {
            Switch = Switch,
            Description = Description,
            Category = Category,
            IsSelected = IsSelected,
            Value = Value,
            RequiresValue = RequiresValue,
            ValueHint = ValueHint
        };
    }

    public class RobocopyViewModel : DisposableBindableBase
    {
        private readonly RobocopyService _service = new();
        private CancellationTokenSource? _cts;
        
        // Directories
        private string _sourceDirectory = string.Empty;
        private string _destinationDirectory = string.Empty;
        private string _filePattern = "*.*";
        private bool _createDestination = true;

        // State
        private bool _isRunning;
        private bool _isPreviewMode;
        private int _progress;
        private string _statusMessage = "Configure Robocopy options and click Execute to start.";
        
        // Output
        private string _output = string.Empty;
        private string _commandPreview = string.Empty;
        private RobocopyResult? _lastResult;

        // Filter
        private string _categoryFilter = "All";
        private string _searchFilter = string.Empty;

        public RobocopyViewModel()
        {
            Switches = new ObservableCollection<RobocopySwitchViewModel>();
            Categories = new ObservableCollection<string> { "All" };
            OutputLines = new ObservableCollection<string>();
            ErrorLines = new ObservableCollection<string>();
            FailedFiles = new ObservableCollection<RobocopyFailedFile>();

            // Initialize commands
            BrowseSourceCommand = new RelayCommand(_ => BrowseSource());
            BrowseDestinationCommand = new RelayCommand(_ => BrowseDestination());
            ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute());
            PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => CanExecute());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
            ExportResultCommand = new AsyncRelayCommand(ExportResultAsync, () => LastResult != null);
            ClearOutputCommand = new RelayCommand(_ => ClearOutput());
            SelectAllInCategoryCommand = new RelayCommand(_ => SelectAllInCategory());
            ClearAllInCategoryCommand = new RelayCommand(_ => ClearAllInCategory());
            ApplyPresetCommand = new RelayCommand(ApplyPreset);

            // Load switches
            LoadSwitches();

            // Set up collection view for filtering
            SwitchesView = CollectionViewSource.GetDefaultView(Switches);
            SwitchesView.Filter = FilterSwitches;
            SwitchesView.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

            // Subscribe to service events
            _service.OutputReceived += OnOutputReceived;
            _service.ErrorReceived += OnErrorReceived;
            _service.ProgressChanged += OnProgressChanged;
        }

        #region Properties

        public string SourceDirectory
        {
            get => _sourceDirectory;
            set
            {
                if (SetProperty(ref _sourceDirectory, value))
                {
                    UpdateCommandPreview();
                    RaiseCanExecuteChanged();
                }
            }
        }

        public string DestinationDirectory
        {
            get => _destinationDirectory;
            set
            {
                if (SetProperty(ref _destinationDirectory, value))
                {
                    UpdateCommandPreview();
                    RaiseCanExecuteChanged();
                }
            }
        }

        public string FilePattern
        {
            get => _filePattern;
            set
            {
                if (SetProperty(ref _filePattern, value))
                    UpdateCommandPreview();
            }
        }

        public bool CreateDestination
        {
            get => _createDestination;
            set => SetProperty(ref _createDestination, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                    RaiseCanExecuteChanged();
            }
        }

        public bool IsPreviewMode
        {
            get => _isPreviewMode;
            set => SetProperty(ref _isPreviewMode, value);
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string Output
        {
            get => _output;
            set => SetProperty(ref _output, value);
        }

        public string CommandPreview
        {
            get => _commandPreview;
            set => SetProperty(ref _commandPreview, value);
        }

        public RobocopyResult? LastResult
        {
            get => _lastResult;
            set
            {
                if (SetProperty(ref _lastResult, value))
                    (ExportResultCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string CategoryFilter
        {
            get => _categoryFilter;
            set
            {
                if (SetProperty(ref _categoryFilter, value))
                    SwitchesView?.Refresh();
            }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                    SwitchesView?.Refresh();
            }
        }

        public ObservableCollection<RobocopySwitchViewModel> Switches { get; }
        public ObservableCollection<string> Categories { get; }
        public ObservableCollection<string> OutputLines { get; }
        public ObservableCollection<string> ErrorLines { get; }
        public ObservableCollection<RobocopyFailedFile> FailedFiles { get; }
        public ICollectionView? SwitchesView { get; }

        #endregion

        #region Commands

        public ICommand BrowseSourceCommand { get; }
        public ICommand BrowseDestinationCommand { get; }
        public ICommand ExecuteCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ExportResultCommand { get; }
        public ICommand ClearOutputCommand { get; }
        public ICommand SelectAllInCategoryCommand { get; }
        public ICommand ClearAllInCategoryCommand { get; }
        public ICommand ApplyPresetCommand { get; }

        #endregion

        #region Methods

        private void LoadSwitches()
        {
            var switches = RobocopySwitches.GetAllSwitches();
            
            foreach (var sw in switches)
            {
                Switches.Add(new RobocopySwitchViewModel
                {
                    Switch = sw.Switch,
                    Description = sw.Description,
                    Category = sw.Category,
                    RequiresValue = sw.RequiresValue,
                    ValueHint = sw.ValueHint ?? string.Empty
                });

                if (!Categories.Contains(sw.Category))
                    Categories.Add(sw.Category);
            }

            // Wire up property changed for command preview
            foreach (var sw in Switches)
            {
                sw.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(RobocopySwitchViewModel.IsSelected) ||
                        e.PropertyName == nameof(RobocopySwitchViewModel.Value))
                    {
                        UpdateCommandPreview();
                    }
                };
            }
        }

        private bool FilterSwitches(object obj)
        {
            if (obj is not RobocopySwitchViewModel sw)
                return false;

            // Category filter
            if (CategoryFilter != "All" && sw.Category != CategoryFilter)
                return false;

            // Search filter
            if (!string.IsNullOrWhiteSpace(SearchFilter))
            {
                var search = SearchFilter.ToLowerInvariant();
                return sw.Switch.ToLowerInvariant().Contains(search) ||
                       sw.Description.ToLowerInvariant().Contains(search);
            }

            return true;
        }

        private void BrowseSource()
        {
            var folder = FileDialogService.BrowseForFolder("Select Source Directory");
            if (!string.IsNullOrEmpty(folder))
                SourceDirectory = folder;
        }

        private void BrowseDestination()
        {
            var folder = FileDialogService.BrowseForFolder("Select Destination Directory");
            if (!string.IsNullOrEmpty(folder))
                DestinationDirectory = folder;
        }

        private bool CanExecute()
        {
            return !IsRunning &&
                   !string.IsNullOrWhiteSpace(SourceDirectory) &&
                   !string.IsNullOrWhiteSpace(DestinationDirectory);
        }

        private void RaiseCanExecuteChanged()
        {
            (ExecuteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PreviewCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async System.Threading.Tasks.Task ExecuteAsync()
        {
            if (!ConfirmExecution())
                return;

            await RunRobocopyAsync(false);
        }

        private async System.Threading.Tasks.Task PreviewAsync()
        {
            await RunRobocopyAsync(true);
        }

        private bool ConfirmExecution()
        {
            var selectedSwitches = Switches.Where(s => s.IsSelected).ToList();
            
            var message = $"Robocopy will copy files from:\n\n" +
                         $"Source: {SourceDirectory}\n" +
                         $"To: {DestinationDirectory}\n\n" +
                         $"Selected options: {selectedSwitches.Count}\n";

            // Warn about dangerous options
            if (selectedSwitches.Any(s => s.Switch == "/MIR" || s.Switch == "/PURGE"))
            {
                message += "\n⚠️ WARNING: /MIR or /PURGE is selected. Files in destination that don't exist in source will be DELETED!\n";
            }

            if (selectedSwitches.Any(s => s.Switch == "/MOV" || s.Switch == "/MOVE"))
            {
                message += "\n⚠️ WARNING: /MOV or /MOVE is selected. Source files will be DELETED after copying!\n";
            }

            message += "\nDo you want to continue?";

            var result = MessageBox.Show(message, "Confirm Robocopy", MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        private async System.Threading.Tasks.Task RunRobocopyAsync(bool previewOnly)
        {
            IsRunning = true;
            IsPreviewMode = previewOnly;
            Progress = 0;
            ClearOutput();

            _cts = new CancellationTokenSource();
            StatusBarViewModel.Instance.StartOperation(previewOnly ? "Preview (List Only)..." : "Executing Robocopy...", isCancellable: true);

            try
            {
                var selectedSwitches = Switches.Select(s => s.ToSwitch()).ToList();
                var pattern = string.IsNullOrWhiteSpace(FilePattern) || FilePattern == "*.*" ? null : FilePattern;

                RobocopyResult result;

                if (previewOnly)
                {
                    StatusMessage = "Running preview (list only mode)...";
                    result = await _service.PreviewAsync(SourceDirectory, DestinationDirectory, selectedSwitches, pattern, _cts.Token);
                }
                else
                {
                    StatusMessage = "Copying files...";
                    result = await _service.ExecuteAsync(SourceDirectory, DestinationDirectory, selectedSwitches, pattern, CreateDestination, _cts.Token);
                }

                LastResult = result;

                // Update failed files
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FailedFiles.Clear();
                    foreach (var f in result.FilesFailed)
                        FailedFiles.Add(f);
                });

                // Set status based on result
                if (result.Success)
                {
                    var stats = result.Statistics;
                    StatusMessage = previewOnly
                        ? $"Preview complete: {stats.FilesTotal} files would be copied ({stats.BytesTotalFormatted})"
                        : $"Complete: {stats.FilesCopied} files copied ({stats.BytesCopiedFormatted}), {stats.FilesFailed} failed";
                    Progress = 100;
                }
                else
                {
                    StatusMessage = $"Completed with errors. Exit code: {result.ExitCode} - {result.ExitCodeDescription}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorLines.Add(ex.Message);
                });
            }
            finally
            {
                IsRunning = false;
                StatusBarViewModel.Instance.CompleteOperation();
            }
        }

        private void Cancel()
        {
            _cts?.Cancel();
            StatusMessage = "Cancelling...";
        }

        private async System.Threading.Tasks.Task ExportResultAsync()
        {
            if (LastResult == null)
                return;

            var path = FileDialogService.SaveFile("JSON Files (*.json)|*.json", "Export Result", "robocopy_result.json");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                await _service.ExportToJsonAsync(LastResult, path);
                StatusMessage = $"Result exported to {path}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearOutput()
        {
            Output = string.Empty;
            OutputLines.Clear();
            ErrorLines.Clear();
            FailedFiles.Clear();
        }

        private void SelectAllInCategory()
        {
            foreach (var sw in Switches.Where(s => CategoryFilter == "All" || s.Category == CategoryFilter))
            {
                if (!sw.RequiresValue)
                    sw.IsSelected = true;
            }
        }

        private void ClearAllInCategory()
        {
            foreach (var sw in Switches.Where(s => CategoryFilter == "All" || s.Category == CategoryFilter))
            {
                sw.IsSelected = false;
            }
        }

        private void ApplyPreset(object? parameter)
        {
            if (parameter is not string preset)
                return;

            // Clear all first
            foreach (var sw in Switches)
                sw.IsSelected = false;

            switch (preset)
            {
                case "Mirror":
                    // Full mirror - most common for backups
                    SetSwitch("/MIR", true);
                    SetSwitch("/R:n", true, "3");
                    SetSwitch("/W:n", true, "5");
                    SetSwitch("/MT[:n]", true, "8");
                    SetSwitch("/NP", true);
                    break;

                case "Copy":
                    // Simple copy with subdirectories
                    SetSwitch("/E", true);
                    SetSwitch("/R:n", true, "3");
                    SetSwitch("/W:n", true, "5");
                    SetSwitch("/MT[:n]", true, "8");
                    break;

                case "Move":
                    // Move files (delete source after copy)
                    SetSwitch("/MOVE", true);
                    SetSwitch("/E", true);
                    SetSwitch("/R:n", true, "3");
                    SetSwitch("/W:n", true, "5");
                    break;

                case "Sync":
                    // Sync without deleting extra files
                    SetSwitch("/E", true);
                    SetSwitch("/XO", true); // Exclude older files
                    SetSwitch("/R:n", true, "3");
                    SetSwitch("/W:n", true, "5");
                    SetSwitch("/MT[:n]", true, "8");
                    break;

                case "Backup":
                    // Backup with all attributes
                    SetSwitch("/E", true);
                    SetSwitch("/COPYALL", true);
                    SetSwitch("/R:n", true, "3");
                    SetSwitch("/W:n", true, "5");
                    SetSwitch("/MT[:n]", true, "8");
                    SetSwitch("/ZB", true);
                    break;

                case "ListOnly":
                    // Preview mode
                    SetSwitch("/L", true);
                    SetSwitch("/E", true);
                    SetSwitch("/V", true);
                    break;
            }

            UpdateCommandPreview();
        }

        private void SetSwitch(string switchName, bool selected, string? value = null)
        {
            var sw = Switches.FirstOrDefault(s => s.Switch == switchName || s.Switch.StartsWith(switchName.TrimEnd(':')));
            if (sw != null)
            {
                sw.IsSelected = selected;
                if (value != null)
                    sw.Value = value;
            }
        }

        private void UpdateCommandPreview()
        {
            var selected = Switches.Where(s => s.IsSelected).Select(s => s.ToSwitch());
            var pattern = string.IsNullOrWhiteSpace(FilePattern) || FilePattern == "*.*" ? "" : FilePattern;
            
            var src = string.IsNullOrWhiteSpace(SourceDirectory) ? "<source>" : $"\"{SourceDirectory}\"";
            var dst = string.IsNullOrWhiteSpace(DestinationDirectory) ? "<destination>" : $"\"{DestinationDirectory}\"";

            var args = $"robocopy {src} {dst}";
            if (!string.IsNullOrEmpty(pattern))
                args += $" {pattern}";

            foreach (var sw in selected)
            {
                if (sw.RequiresValue && !string.IsNullOrWhiteSpace(sw.Value))
                {
                    var colonIndex = sw.Switch.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        args += $" {sw.Switch.Substring(0, colonIndex + 1)}{sw.Value}";
                    }
                    else
                    {
                        args += $" {sw.Switch} {sw.Value}";
                    }
                }
                else if (!sw.RequiresValue)
                {
                    args += $" {sw.Switch}";
                }
            }

            CommandPreview = args;
        }

        private void OnOutputReceived(string line)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OutputLines.Add(line);
                Output += line + Environment.NewLine;
            });
        }

        private void OnErrorReceived(string line)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ErrorLines.Add(line);
            });
        }

        private void OnProgressChanged(int percent)
        {
            Progress = percent;
        }

        protected override void DisposeManagedResources()
        {
            _service.OutputReceived -= OnOutputReceived;
            _service.ErrorReceived -= OnErrorReceived;
            _service.ProgressChanged -= OnProgressChanged;
            _cts?.Cancel();
            _cts?.Dispose();
            base.DisposeManagedResources();
        }

        #endregion
    }
}
