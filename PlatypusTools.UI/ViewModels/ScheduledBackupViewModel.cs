using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class ScheduledBackupViewModel : BindableBase
    {
        private readonly ScheduledBackupService _service = new();
        private CancellationTokenSource? _cts;

        public ScheduledBackupViewModel()
        {
            Profiles = new ObservableCollection<ScheduledBackupService.BackupProfile>();
            SourceFolders = new ObservableCollection<string>();
            ExistingBackups = new ObservableCollection<BackupEntry>();

            RunBackupCommand = new RelayCommand(async _ => await RunBackupAsync(), _ => SelectedProfile != null && !IsRunning);
            SaveProfileCommand = new RelayCommand(async _ => await SaveProfileAsync(), _ => !string.IsNullOrEmpty(ProfileName));
            DeleteProfileCommand = new RelayCommand(async _ => await DeleteProfileAsync(), _ => SelectedProfile != null);
            AddSourceFolderCommand = new RelayCommand(_ => AddSourceFolder());
            RemoveSourceFolderCommand = new RelayCommand(_ => { if (SelectedSourceFolder != null) SourceFolders.Remove(SelectedSourceFolder); }, _ => SelectedSourceFolder != null);
            BrowseDestinationCommand = new RelayCommand(_ => BrowseDestination());
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);
            NewProfileCommand = new RelayCommand(_ => NewProfile());
            RefreshBackupsCommand = new RelayCommand(_ => RefreshBackups(), _ => SelectedProfile != null);

            _service.LogMessage += (s, msg) => LogOutput += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            _service.ProgressChanged += (s, p) => Progress = p;

            _ = LoadProfilesAsync();
        }

        public class BackupEntry
        {
            public string Path { get; set; } = "";
            public DateTime Created { get; set; }
            public string Size { get; set; } = "";
        }

        private ScheduledBackupService.BackupProfile? _selectedProfile;
        public ScheduledBackupService.BackupProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                SetProperty(ref _selectedProfile, value);
                if (value != null)
                {
                    ProfileName = value.Name;
                    DestinationFolder = value.DestinationFolder;
                    CompressBackup = value.CompressBackup;
                    Incremental = value.Incremental;
                    MaxVersions = value.MaxVersions;
                    Schedule = value.Schedule;
                    ExcludePatterns = string.Join(";", value.ExcludePatterns);
                    SourceFolders.Clear();
                    foreach (var s in value.SourceFolders) SourceFolders.Add(s);
                    RefreshBackups();
                }
            }
        }

        private string _profileName = "";
        public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

        private string _destinationFolder = "";
        public string DestinationFolder { get => _destinationFolder; set => SetProperty(ref _destinationFolder, value); }

        private bool _compressBackup = true;
        public bool CompressBackup { get => _compressBackup; set => SetProperty(ref _compressBackup, value); }

        private bool _incremental;
        public bool Incremental { get => _incremental; set => SetProperty(ref _incremental, value); }

        private int _maxVersions = 5;
        public int MaxVersions { get => _maxVersions; set => SetProperty(ref _maxVersions, value); }

        private string _schedule = "Manual";
        public string Schedule { get => _schedule; set => SetProperty(ref _schedule, value); }

        private string _excludePatterns = "*.tmp;*.log;Thumbs.db;desktop.ini";
        public string ExcludePatterns { get => _excludePatterns; set => SetProperty(ref _excludePatterns, value); }

        private string? _selectedSourceFolder;
        public string? SelectedSourceFolder { get => _selectedSourceFolder; set => SetProperty(ref _selectedSourceFolder, value); }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

        private int _progress;
        public int Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private string _statusMessage = "Create or select a backup profile";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _logOutput = "";
        public string LogOutput { get => _logOutput; set => SetProperty(ref _logOutput, value); }

        public ObservableCollection<ScheduledBackupService.BackupProfile> Profiles { get; }
        public ObservableCollection<string> SourceFolders { get; }
        public ObservableCollection<BackupEntry> ExistingBackups { get; }

        public ICommand RunBackupCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand AddSourceFolderCommand { get; }
        public ICommand RemoveSourceFolderCommand { get; }
        public ICommand BrowseDestinationCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand NewProfileCommand { get; }
        public ICommand RefreshBackupsCommand { get; }

        private async Task RunBackupAsync()
        {
            if (SelectedProfile == null) return;
            IsRunning = true;
            _cts = new CancellationTokenSource();
            LogOutput = "";
            Progress = 0;

            try
            {
                var result = await _service.RunBackupAsync(SelectedProfile, _cts.Token);
                StatusMessage = result.Success
                    ? $"Backup complete: {result.FilesCopied} files, {result.TotalSize / 1024 / 1024:N1} MB"
                    : $"Backup failed: {result.ErrorMessages.FirstOrDefault()}";
                RefreshBackups();
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

        private async Task SaveProfileAsync()
        {
            var profile = SelectedProfile ?? new ScheduledBackupService.BackupProfile();
            profile.Name = ProfileName;
            profile.SourceFolders = SourceFolders.ToList();
            profile.DestinationFolder = DestinationFolder;
            profile.CompressBackup = CompressBackup;
            profile.Incremental = Incremental;
            profile.MaxVersions = MaxVersions;
            profile.Schedule = Schedule;
            profile.ExcludePatterns = ExcludePatterns.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

            await _service.SaveProfileAsync(profile);
            await LoadProfilesAsync();
            StatusMessage = $"Profile '{ProfileName}' saved";
        }

        private async Task DeleteProfileAsync()
        {
            if (SelectedProfile == null) return;
            _service.DeleteProfile(SelectedProfile.Id);
            await LoadProfilesAsync();
            StatusMessage = "Profile deleted";
        }

        private async Task LoadProfilesAsync()
        {
            Profiles.Clear();
            var profiles = await _service.LoadAllProfilesAsync();
            foreach (var p in profiles) Profiles.Add(p);
        }

        private void NewProfile()
        {
            SelectedProfile = null;
            ProfileName = "New Backup";
            SourceFolders.Clear();
            DestinationFolder = "";
            CompressBackup = true;
            MaxVersions = 5;
            ExistingBackups.Clear();
        }

        private void AddSourceFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SourceFolders.Add(dlg.SelectedPath);
        }

        private void BrowseDestination()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                DestinationFolder = dlg.SelectedPath;
        }

        private void RefreshBackups()
        {
            ExistingBackups.Clear();
            if (SelectedProfile == null) return;

            var backups = _service.GetExistingBackups(SelectedProfile);
            foreach (var (path, created, size) in backups)
            {
                ExistingBackups.Add(new BackupEntry
                {
                    Path = path,
                    Created = created,
                    Size = size < 1024 * 1024 ? $"{size / 1024:N0} KB" : $"{size / 1024.0 / 1024:N1} MB"
                });
            }
        }
    }
}
