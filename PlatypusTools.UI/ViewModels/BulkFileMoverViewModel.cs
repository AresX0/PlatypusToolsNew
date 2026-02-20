using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class BulkFileMoverViewModel : BindableBase
    {
        private readonly BulkFileMoverService _service = new();
        private CancellationTokenSource? _cts;

        public BulkFileMoverViewModel()
        {
            Profiles = new ObservableCollection<string>();
            Rules = new ObservableCollection<BulkFileMoverService.MoverRule>();
            PreviewResults = new ObservableCollection<BulkFileMoverService.MoveResult>();

            AddRuleCommand = new RelayCommand(_ => AddRule(), _ => !string.IsNullOrEmpty(NewRulePattern));
            RemoveRuleCommand = new RelayCommand(_ => RemoveSelectedRule(), _ => SelectedRule != null);
            ExecuteCommand = new RelayCommand(async _ => await ExecuteAsync(), _ => !IsRunning);
            PreviewCommand = new RelayCommand(_ => Preview(), _ => !IsRunning);
            SaveProfileCommand = new RelayCommand(async _ => await SaveProfileAsync(), _ => !string.IsNullOrEmpty(ProfileName));
            LoadProfileCommand = new RelayCommand(async _ => await LoadProfileAsync(), _ => SelectedProfile != null);
            DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => SelectedProfile != null);
            NewProfileCommand = new RelayCommand(_ => NewProfile());
            BrowseSourceCommand = new RelayCommand(_ => BrowseSource());
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);

            LoadSavedProfiles();
        }

        private string _sourceFolder = "";
        public string SourceFolder { get => _sourceFolder; set => SetProperty(ref _sourceFolder, value); }

        private bool _includeSubfolders;
        public bool IncludeSubfolders { get => _includeSubfolders; set => SetProperty(ref _includeSubfolders, value); }

        private string _profileName = "";
        public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

        private string? _selectedProfile;
        public string? SelectedProfile { get => _selectedProfile; set => SetProperty(ref _selectedProfile, value); }

        /// <summary>XAML alias for SelectedProfile</summary>
        public string? SelectedProfileName { get => SelectedProfile; set => SelectedProfile = value; }

        private BulkFileMoverService.MoverRule? _selectedRule;
        public BulkFileMoverService.MoverRule? SelectedRule { get => _selectedRule; set => SetProperty(ref _selectedRule, value); }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { SetProperty(ref _isRunning, value); OnPropertyChanged(nameof(IsExecuting)); } }

        /// <summary>XAML alias for IsRunning</summary>
        public bool IsExecuting => IsRunning;

        private int _progress;
        public int Progress { get => _progress; set => SetProperty(ref _progress, value); }

        // New rule entry fields (bound from XAML)
        private object? _newRuleType;
        public object? NewRuleType { get => _newRuleType; set => SetProperty(ref _newRuleType, value); }

        private string _newRulePattern = "";
        public string NewRulePattern { get => _newRulePattern; set => SetProperty(ref _newRulePattern, value); }

        private string _newRuleDestination = "";
        public string NewRuleDestination { get => _newRuleDestination; set => SetProperty(ref _newRuleDestination, value); }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ObservableCollection<string> Profiles { get; }
        /// <summary>XAML alias for Profiles</summary>
        public ObservableCollection<string> ProfileNames => Profiles;
        public ObservableCollection<BulkFileMoverService.MoverRule> Rules { get; }
        public ObservableCollection<BulkFileMoverService.MoveResult> PreviewResults { get; }

        public ICommand AddRuleCommand { get; }
        public ICommand RemoveRuleCommand { get; }
        public ICommand ExecuteCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand LoadProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand NewProfileCommand { get; }
        public ICommand BrowseSourceCommand { get; }
        public ICommand CancelCommand { get; }

        private void AddRule()
        {
            // Parse condition type from ComboBox selection
            var conditionStr = "Extension";
            if (NewRuleType is System.Windows.Controls.ComboBoxItem cbi)
                conditionStr = cbi.Content?.ToString() ?? "Extension";
            else if (NewRuleType is string s)
                conditionStr = s;

            if (!Enum.TryParse<BulkFileMoverService.RuleCondition>(conditionStr, true, out var condition))
                condition = BulkFileMoverService.RuleCondition.Extension;

            Rules.Add(new BulkFileMoverService.MoverRule
            {
                Name = $"Rule {Rules.Count + 1}",
                Condition = condition,
                Value = NewRulePattern,
                DestinationFolder = NewRuleDestination
            });

            // Clear the input fields
            NewRulePattern = "";
            NewRuleDestination = "";
            StatusMessage = $"Rule added: {conditionStr} = {NewRulePattern}";
        }

        private void NewProfile()
        {
            ProfileName = "";
            SourceFolder = "";
            IncludeSubfolders = false;
            Rules.Clear();
            PreviewResults.Clear();
            SelectedProfile = null;
            StatusMessage = "New profile — set a name and source folder";
        }

        private void RemoveSelectedRule()
        {
            if (SelectedRule != null) Rules.Remove(SelectedRule);
        }

        private void Preview()
        {
            PreviewResults.Clear();
            var profile = BuildProfile();
            var results = _service.PreviewProfile(profile);
            foreach (var r in results) PreviewResults.Add(r);
            StatusMessage = $"Preview: {results.Count} files would be {(results.Any(r => r.Action == BulkFileMoverService.RuleAction.Move) ? "moved" : "copied")}";
        }

        private async Task ExecuteAsync()
        {
            IsRunning = true;
            _cts = new CancellationTokenSource();
            PreviewResults.Clear();

            try
            {
                var profile = BuildProfile();
                var progressReporter = new Progress<int>(p => Progress = p);
                var results = await _service.ExecuteProfileAsync(profile, progressReporter, _cts.Token);

                foreach (var r in results) PreviewResults.Add(r);
                var success = results.Count(r => r.Success);
                var failed = results.Count(r => !r.Success);
                StatusMessage = $"Complete: {success} succeeded, {failed} failed";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private BulkFileMoverService.MoverProfile BuildProfile()
        {
            return new BulkFileMoverService.MoverProfile
            {
                Name = string.IsNullOrEmpty(ProfileName) ? "Default" : ProfileName,
                SourceFolder = SourceFolder,
                IncludeSubfolders = IncludeSubfolders,
                Rules = Rules.ToList()
            };
        }

        private async Task SaveProfileAsync()
        {
            var profile = BuildProfile();
            await _service.SaveProfileAsync(profile);
            LoadSavedProfiles();
            StatusMessage = $"Profile '{ProfileName}' saved";
        }

        private async Task LoadProfileAsync()
        {
            if (SelectedProfile == null) return;
            var profile = await _service.LoadProfileAsync(SelectedProfile);
            if (profile == null) return;

            SourceFolder = profile.SourceFolder;
            IncludeSubfolders = profile.IncludeSubfolders;
            ProfileName = profile.Name;
            Rules.Clear();
            foreach (var r in profile.Rules) Rules.Add(r);
            StatusMessage = $"Profile '{profile.Name}' loaded";
        }

        private void DeleteProfile()
        {
            if (SelectedProfile == null) return;
            _service.DeleteProfile(SelectedProfile);
            LoadSavedProfiles();
            StatusMessage = "Profile deleted";
        }

        private void LoadSavedProfiles()
        {
            Profiles.Clear();
            foreach (var p in _service.GetSavedProfiles()) Profiles.Add(p);
        }

        private void BrowseSource()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SourceFolder = dlg.SelectedPath;
        }
    }
}
