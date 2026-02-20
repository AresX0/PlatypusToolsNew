using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class EnvironmentVariableManagerViewModel : BindableBase
    {
        private readonly EnvironmentVariableService _service = new();

        public EnvironmentVariableManagerViewModel()
        {
            Variables = new ObservableCollection<EnvironmentVariableService.EnvVariable>();
            PathEntries = new ObservableCollection<string>();

            RefreshCommand = new RelayCommand(_ => LoadVariables());
            SaveVariableCommand = new RelayCommand(_ => SaveVariable(), _ => !string.IsNullOrEmpty(EditName));
            DeleteVariableCommand = new RelayCommand(_ => DeleteVariable(), _ => SelectedVariable != null);
            ExportCommand = new RelayCommand(async _ => await ExportAsync());
            ImportCommand = new RelayCommand(async _ => await ImportAsync());
            AddPathEntryCommand = new RelayCommand(_ => AddPathEntry(), _ => !string.IsNullOrEmpty(NewPathEntry));
            RemovePathEntryCommand = new RelayCommand(_ => RemovePathEntry(), _ => SelectedPathEntry != null);
            MoveUpCommand = new RelayCommand(_ => MovePathEntry(-1), _ => SelectedPathEntry != null);
            MoveDownCommand = new RelayCommand(_ => MovePathEntry(1), _ => SelectedPathEntry != null);
            CopyValueCommand = new RelayCommand(_ => CopyValue(), _ => SelectedVariable != null);
            NewVariableCommand = new RelayCommand(_ => NewVariable());
            BrowsePathCommand = new RelayCommand(_ => BrowsePath());

            LoadVariables();
        }

        private ObservableCollection<EnvironmentVariableService.EnvVariable> _variables = null!;
        public ObservableCollection<EnvironmentVariableService.EnvVariable> Variables
        {
            get => _variables;
            set => SetProperty(ref _variables, value);
        }

        public ObservableCollection<string> PathEntries { get; }

        private EnvironmentVariableService.EnvVariable? _selectedVariable;
        public EnvironmentVariableService.EnvVariable? SelectedVariable
        {
            get => _selectedVariable;
            set
            {
                SetProperty(ref _selectedVariable, value);
                if (value != null)
                {
                    EditName = value.Name;
                    EditValue = value.Value;
                    EditTarget = value.Target.ToString();
                    IsPathVariable = value.IsPath;
                    if (value.IsPath) LoadPathEntries();
                }
            }
        }

        private string _editName = "";
        public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

        private string _editValue = "";
        public string EditValue { get => _editValue; set => SetProperty(ref _editValue, value); }

        private string _editTarget = "User";
        public string EditTarget { get => _editTarget; set => SetProperty(ref _editTarget, value); }

        private bool _isPathVariable;
        public bool IsPathVariable { get => _isPathVariable; set => SetProperty(ref _isPathVariable, value); }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                LoadVariables();
            }
        }

        private string _filterTarget = "All";
        public string FilterTarget
        {
            get => _filterTarget;
            set
            {
                SetProperty(ref _filterTarget, value);
                LoadVariables();
            }
        }

        private string? _selectedPathEntry;
        public string? SelectedPathEntry { get => _selectedPathEntry; set => SetProperty(ref _selectedPathEntry, value); }

        private string _newPathEntry = "";
        public string NewPathEntry { get => _newPathEntry; set => SetProperty(ref _newPathEntry, value); }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand RefreshCommand { get; }
        public ICommand SaveVariableCommand { get; }
        public ICommand DeleteVariableCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand AddPathEntryCommand { get; }
        public ICommand RemovePathEntryCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand CopyValueCommand { get; }
        public ICommand NewVariableCommand { get; }
        public ICommand BrowsePathCommand { get; }

        private void LoadVariables()
        {
            Variables.Clear();
            var vars = string.IsNullOrEmpty(SearchText)
                ? _service.GetAll()
                : _service.Search(SearchText);

            if (FilterTarget != "All")
            {
                var target = Enum.Parse<EnvironmentVariableTarget>(FilterTarget);
                vars = vars.Where(v => v.Target == target).ToList();
            }

            foreach (var v in vars.OrderBy(v => v.Target).ThenBy(v => v.Name))
                Variables.Add(v);

            StatusMessage = $"{Variables.Count} variables";
        }

        private void SaveVariable()
        {
            try
            {
                var target = Enum.Parse<EnvironmentVariableTarget>(EditTarget);
                _service.SetVariable(EditName, EditValue, target);
                LoadVariables();
                StatusMessage = $"Saved '{EditName}'";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void DeleteVariable()
        {
            if (SelectedVariable == null) return;
            try
            {
                _service.DeleteVariable(SelectedVariable.Name, SelectedVariable.Target);
                LoadVariables();
                StatusMessage = $"Deleted '{SelectedVariable.Name}'";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void NewVariable()
        {
            SelectedVariable = null;
            EditName = "";
            EditValue = "";
            EditTarget = "User";
            IsPathVariable = false;
        }

        private void CopyValue()
        {
            if (SelectedVariable != null)
                System.Windows.Clipboard.SetText(SelectedVariable.Value);
        }

        private async System.Threading.Tasks.Task ExportAsync()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON|*.json",
                FileName = $"env_vars_{DateTime.Now:yyyyMMdd}.json"
            };
            if (dlg.ShowDialog() == true)
            {
                await _service.ExportAsync(dlg.FileName);
                StatusMessage = $"Exported to {dlg.FileName}";
            }
        }

        private async System.Threading.Tasks.Task ImportAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var result = System.Windows.MessageBox.Show(
                    "Import will overwrite existing variable values. Continue?",
                    "Import Environment Variables",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    var target = Enum.Parse<EnvironmentVariableTarget>(EditTarget);
                    int count = await _service.ImportAsync(dlg.FileName, target);
                    LoadVariables();
                    StatusMessage = $"Imported {count} variables";
                }
            }
        }

        private void LoadPathEntries()
        {
            PathEntries.Clear();
            if (SelectedVariable == null) return;
            var entries = _service.GetPathEntries(SelectedVariable.Target);
            foreach (var e in entries) PathEntries.Add(e);
        }

        private void AddPathEntry()
        {
            if (string.IsNullOrEmpty(NewPathEntry)) return;
            var target = SelectedVariable?.Target ?? EnvironmentVariableTarget.User;
            _service.AddPathEntry(NewPathEntry, target);
            NewPathEntry = "";
            LoadPathEntries();
            LoadVariables();
        }

        private void RemovePathEntry()
        {
            if (SelectedPathEntry == null || SelectedVariable == null) return;
            _service.RemovePathEntry(SelectedPathEntry, SelectedVariable.Target);
            LoadPathEntries();
            LoadVariables();
        }

        private void MovePathEntry(int direction)
        {
            if (SelectedPathEntry == null) return;
            int idx = PathEntries.IndexOf(SelectedPathEntry);
            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= PathEntries.Count) return;
            PathEntries.Move(idx, newIdx);
            // Re-save the entire PATH variable
            var target = SelectedVariable?.Target ?? EnvironmentVariableTarget.User;
            var newPath = string.Join(";", PathEntries);
            _service.SetVariable("PATH", newPath, target);
            LoadVariables();
        }

        private void BrowsePath()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                NewPathEntry = dlg.SelectedPath;
        }
    }
}
