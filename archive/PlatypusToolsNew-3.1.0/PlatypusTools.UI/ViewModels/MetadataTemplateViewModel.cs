using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using PlatypusTools.Core.Models.Metadata;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for managing metadata templates.
    /// </summary>
    public class MetadataTemplateViewModel : BindableBase
    {
        private readonly MetadataTemplateService _service;
        private CancellationTokenSource? _cts;
        
        public MetadataTemplateViewModel()
        {
            _service = MetadataTemplateService.Instance;
            Templates = new ObservableCollection<MetadataTemplate>();
            Categories = new ObservableCollection<string>();
            ApplyResults = new ObservableCollection<MetadataApplyResult>();
            ApplyOptions = new MetadataApplyOptions();
            
            // Wire up events
            _service.TemplateAdded += (s, t) => RefreshTemplates();
            _service.TemplateUpdated += (s, t) => RefreshTemplates();
            _service.TemplateDeleted += (s, t) => RefreshTemplates();
            _service.FileProcessed += (s, r) => OnFileProcessed(r);
            
            // Commands
            CreateTemplateCommand = new RelayCommand(_ => CreateTemplate());
            EditTemplateCommand = new RelayCommand(_ => EditTemplate(), _ => SelectedTemplate != null && !SelectedTemplate.IsBuiltIn);
            DeleteTemplateCommand = new RelayCommand(async _ => await DeleteTemplateAsync(), _ => SelectedTemplate != null && !SelectedTemplate.IsBuiltIn);
            DuplicateTemplateCommand = new RelayCommand(async _ => await DuplicateTemplateAsync(), _ => SelectedTemplate != null);
            ImportTemplateCommand = new RelayCommand(async _ => await ImportTemplateAsync());
            ExportTemplateCommand = new RelayCommand(async _ => await ExportTemplateAsync(), _ => SelectedTemplate != null);
            
            ApplyTemplateCommand = new RelayCommand(async _ => await ApplyTemplateAsync(), _ => SelectedTemplate != null && SelectedFiles.Any());
            CopyMetadataCommand = new RelayCommand(async _ => await CopyMetadataAsync(), _ => SourceFile != null && SelectedFiles.Any());
            CancelOperationCommand = new RelayCommand(_ => CancelOperation(), _ => IsProcessing);
            
            AddFilesCommand = new RelayCommand(_ => AddFiles());
            AddFolderCommand = new RelayCommand(_ => AddFolder());
            ClearFilesCommand = new RelayCommand(_ => ClearFiles(), _ => SelectedFiles.Any());
            SelectSourceFileCommand = new RelayCommand(_ => SelectSourceFile());
            
            ToggleFavoriteCommand = new RelayCommand(_ => ToggleFavorite(), _ => SelectedTemplate != null);
        }
        
        /// <summary>
        /// Initializes the service with the templates directory.
        /// </summary>
        public async Task InitializeAsync(string templatesDirectory, string? exiftoolPath = null)
        {
            await _service.InitializeAsync(templatesDirectory, exiftoolPath);
            RefreshTemplates();
        }
        
        #region Properties
        
        public ObservableCollection<MetadataTemplate> Templates { get; }
        public ObservableCollection<string> Categories { get; }
        public ObservableCollection<MetadataApplyResult> ApplyResults { get; }
        public ObservableCollection<string> SelectedFiles { get; } = new();
        
        private MetadataTemplate? _selectedTemplate;
        public MetadataTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    RaisePropertyChanged(nameof(CanEditTemplate));
                    RaisePropertyChanged(nameof(CanDeleteTemplate));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private string? _selectedCategory;
        public string? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    FilterByCategory();
                }
            }
        }
        
        private string? _sourceFile;
        public string? SourceFile
        {
            get => _sourceFile;
            set
            {
                if (SetProperty(ref _sourceFile, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }
        
        private MetadataApplyOptions _applyOptions = new();
        public MetadataApplyOptions ApplyOptions
        {
            get => _applyOptions;
            set => SetProperty(ref _applyOptions, value);
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
        
        private int _successCount;
        public int SuccessCount
        {
            get => _successCount;
            set => SetProperty(ref _successCount, value);
        }
        
        private int _failedCount;
        public int FailedCount
        {
            get => _failedCount;
            set => SetProperty(ref _failedCount, value);
        }
        
        public bool CanEditTemplate => SelectedTemplate != null && !SelectedTemplate.IsBuiltIn;
        public bool CanDeleteTemplate => SelectedTemplate != null && !SelectedTemplate.IsBuiltIn;
        public bool ShowFavoritesOnly { get; set; }
        
        private string _newTemplateName = string.Empty;
        public string NewTemplateName
        {
            get => _newTemplateName;
            set => SetProperty(ref _newTemplateName, value);
        }
        
        #endregion
        
        #region Commands
        
        public ICommand CreateTemplateCommand { get; }
        public ICommand EditTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand DuplicateTemplateCommand { get; }
        public ICommand ImportTemplateCommand { get; }
        public ICommand ExportTemplateCommand { get; }
        
        public ICommand ApplyTemplateCommand { get; }
        public ICommand CopyMetadataCommand { get; }
        public ICommand CancelOperationCommand { get; }
        
        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand ClearFilesCommand { get; }
        public ICommand SelectSourceFileCommand { get; }
        
        public ICommand ToggleFavoriteCommand { get; }
        
        #endregion
        
        #region Template Management
        
        private void RefreshTemplates()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Templates.Clear();
                Categories.Clear();
                
                var templates = ShowFavoritesOnly 
                    ? _service.GetFavorites() 
                    : _service.Templates;
                
                foreach (var template in templates.OrderBy(t => t.Category).ThenBy(t => t.Name))
                {
                    if (string.IsNullOrEmpty(SelectedCategory) || template.Category == SelectedCategory)
                    {
                        Templates.Add(template);
                    }
                }
                
                foreach (var category in _service.GetCategories())
                {
                    Categories.Add(category);
                }
            });
        }
        
        private void FilterByCategory()
        {
            RefreshTemplates();
        }
        
        private void CreateTemplate()
        {
            if (string.IsNullOrWhiteSpace(NewTemplateName))
            {
                NewTemplateName = $"Template {DateTime.Now:yyyy-MM-dd HH:mm}";
            }
            
            _ = _service.CreateTemplateAsync(NewTemplateName, SelectedCategory ?? "Custom");
            NewTemplateName = string.Empty;
        }
        
        private void EditTemplate()
        {
            // Would open a template editor dialog
            // For now, just notify
            StatusMessage = "Template editing UI not yet implemented";
        }
        
        private async Task DeleteTemplateAsync()
        {
            if (SelectedTemplate == null || SelectedTemplate.IsBuiltIn) return;
            
            var result = System.Windows.MessageBox.Show(
                $"Delete template '{SelectedTemplate.Name}'?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await _service.DeleteTemplateAsync(SelectedTemplate);
                SelectedTemplate = Templates.FirstOrDefault();
            }
        }
        
        private async Task DuplicateTemplateAsync()
        {
            if (SelectedTemplate == null) return;
            
            var newTemplate = await _service.DuplicateTemplateAsync(SelectedTemplate);
            SelectedTemplate = newTemplate;
            StatusMessage = $"Created copy: {newTemplate.Name}";
        }
        
        private async Task ImportTemplateAsync()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Template Files|*.json|All Files|*.*",
                Title = "Import Template"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var template = await _service.ImportTemplateAsync(dialog.FileName);
                if (template != null)
                {
                    SelectedTemplate = template;
                    StatusMessage = $"Imported: {template.Name}";
                }
                else
                {
                    StatusMessage = "Failed to import template";
                }
            }
        }
        
        private async Task ExportTemplateAsync()
        {
            if (SelectedTemplate == null) return;
            
            var dialog = new SaveFileDialog
            {
                Filter = "Template Files|*.json|All Files|*.*",
                Title = "Export Template",
                FileName = $"{SelectedTemplate.Name}.json"
            };
            
            if (dialog.ShowDialog() == true)
            {
                await _service.ExportTemplateAsync(SelectedTemplate, dialog.FileName);
                StatusMessage = $"Exported: {SelectedTemplate.Name}";
            }
        }
        
        private void ToggleFavorite()
        {
            if (SelectedTemplate == null) return;
            
            SelectedTemplate.IsFavorite = !SelectedTemplate.IsFavorite;
            _ = _service.SaveTemplateAsync(SelectedTemplate);
        }
        
        #endregion
        
        #region File Selection
        
        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.tiff;*.raw;*.cr2;*.nef;*.arw|All Files|*.*",
                Title = "Select Files"
            };
            
            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!SelectedFiles.Contains(file))
                    {
                        SelectedFiles.Add(file);
                    }
                }
                RaisePropertyChanged(nameof(SelectedFiles));
                RaiseCommandsCanExecuteChanged();
            }
        }
        
        private void AddFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing files",
                ShowNewFolderButton = false
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var searchOption = ApplyOptions.IncludeSubdirectories 
                    ? SearchOption.AllDirectories 
                    : SearchOption.TopDirectoryOnly;
                
                var extensions = ApplyOptions.FileTypes;
                var files = extensions.SelectMany(ext =>
                    Directory.GetFiles(dialog.SelectedPath, $"*{ext}", searchOption));
                
                foreach (var file in files)
                {
                    if (!SelectedFiles.Contains(file))
                    {
                        SelectedFiles.Add(file);
                    }
                }
                
                RaisePropertyChanged(nameof(SelectedFiles));
                RaiseCommandsCanExecuteChanged();
                StatusMessage = $"Added {SelectedFiles.Count} files";
            }
        }
        
        private void ClearFiles()
        {
            SelectedFiles.Clear();
            ApplyResults.Clear();
            RaisePropertyChanged(nameof(SelectedFiles));
            RaiseCommandsCanExecuteChanged();
        }
        
        private void SelectSourceFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.tiff;*.raw|All Files|*.*",
                Title = "Select Source File for Metadata Copy"
            };
            
            if (dialog.ShowDialog() == true)
            {
                SourceFile = dialog.FileName;
            }
        }
        
        #endregion
        
        #region Apply Operations
        
        private async Task ApplyTemplateAsync()
        {
            if (SelectedTemplate == null || !SelectedFiles.Any()) return;
            
            IsProcessing = true;
            _cts = new CancellationTokenSource();
            ApplyResults.Clear();
            SuccessCount = 0;
            FailedCount = 0;
            Progress = 0;
            
            StatusMessage = $"Applying template '{SelectedTemplate.Name}'...";
            StatusBarViewModel.Instance.StartOperation("Applying metadata template", SelectedFiles.Count);
            
            try
            {
                var results = await _service.ApplyTemplateAsync(
                    SelectedTemplate,
                    SelectedFiles,
                    ApplyOptions,
                    _cts.Token,
                    new Progress<double>(p => Progress = p));
                
                SuccessCount = results.Count(r => r.Success);
                FailedCount = results.Count(r => !r.Success);
                
                StatusMessage = $"Applied to {SuccessCount} files ({FailedCount} failed)";
                StatusBarViewModel.Instance.CompleteOperation($"Template applied: {SuccessCount} succeeded, {FailedCount} failed");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled";
                StatusBarViewModel.Instance.CompleteOperation("Template application cancelled");
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        private async Task CopyMetadataAsync()
        {
            if (string.IsNullOrEmpty(SourceFile) || !SelectedFiles.Any()) return;
            
            IsProcessing = true;
            _cts = new CancellationTokenSource();
            ApplyResults.Clear();
            SuccessCount = 0;
            FailedCount = 0;
            Progress = 0;
            
            // Remove source file from targets if present
            var targetFiles = SelectedFiles.Where(f => f != SourceFile);
            
            StatusMessage = $"Copying metadata from '{Path.GetFileName(SourceFile)}'...";
            StatusBarViewModel.Instance.StartOperation("Copying metadata", targetFiles.Count());
            
            try
            {
                var results = await _service.CopyMetadataAsync(
                    SourceFile,
                    targetFiles,
                    null, // Copy all tags
                    null, // No exclusions
                    _cts.Token,
                    new Progress<double>(p => Progress = p));
                
                SuccessCount = results.Count(r => r.Success);
                FailedCount = results.Count(r => !r.Success);
                
                StatusMessage = $"Copied to {SuccessCount} files ({FailedCount} failed)";
                StatusBarViewModel.Instance.CompleteOperation($"Metadata copied: {SuccessCount} succeeded, {FailedCount} failed");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled";
                StatusBarViewModel.Instance.CompleteOperation("Metadata copy cancelled");
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        private void CancelOperation()
        {
            _cts?.Cancel();
        }
        
        private void OnFileProcessed(MetadataApplyResult result)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ApplyResults.Add(result);
                
                if (result.Success)
                    SuccessCount++;
                else
                    FailedCount++;
                
                StatusBarViewModel.Instance.UpdateProgress(SuccessCount + FailedCount, $"{SuccessCount + FailedCount} of {SelectedFiles.Count}");
            });
        }
        
        #endregion
        
        private void RaiseCommandsCanExecuteChanged()
        {
            ((RelayCommand)EditTemplateCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteTemplateCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DuplicateTemplateCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ExportTemplateCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ApplyTemplateCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CopyMetadataCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelOperationCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ClearFilesCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ToggleFavoriteCommand).RaiseCanExecuteChanged();
        }
    }
}
