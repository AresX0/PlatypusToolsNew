using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class HiderRecordViewModel : BindableBase
    {
        public HiderRecordViewModel(HiderRecord r)
        {
            Record = r;
        }

        public HiderRecord Record { get; }

        public string FolderPath
        {
            get => Record.FolderPath;
            set { Record.FolderPath = value; RaisePropertyChanged(); }
        }

        public bool AclRestricted
        {
            get => Record.AclRestricted;
            set { Record.AclRestricted = value; RaisePropertyChanged(); }
        }

        public bool EfsEnabled
        {
            get => Record.EfsEnabled;
            set { Record.EfsEnabled = value; RaisePropertyChanged(); }
        }

        public bool IsHidden => PlatypusTools.Core.Services.HiderService.GetHiddenState(FolderPath);

        public void RefreshHiddenState() => RaisePropertyChanged(nameof(IsHidden));
    }

    public class HiderViewModel : BindableBase
    {
        private string _configPath;

        public HiderViewModel()
        {
            Records = new ObservableCollection<HiderRecordViewModel>();

            _configPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "hider.json");

            LoadConfig();

            AddFolderCommand = new RelayCommand(_ => AddFolder(), _ => !string.IsNullOrWhiteSpace(NewFolderPath));
            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            RemoveRecordCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) RemoveRecord(vm); });
            EditRecordCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) EditRecord(vm); });

            SetHiddenCommand = new RelayCommand(_ => SetSelectedHidden(), _ => SelectedRecord != null);
            ClearHiddenCommand = new RelayCommand(_ => ClearSelectedHidden(), _ => SelectedRecord != null);
            ApplyAclCommand = new RelayCommand(_ => ApplyAclToSelected(), _ => SelectedRecord != null);

            LoadConfigCommand = new RelayCommand(_ => LoadConfig());
            SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        }

        public HiderViewModel(string configPath) : this()
        {
            _configPath = configPath;
            LoadConfig();
        }

        public ObservableCollection<HiderRecordViewModel> Records { get; }

        private string _newFolderPath = string.Empty;
        public string NewFolderPath { get => _newFolderPath; set { _newFolderPath = value; RaisePropertyChanged(); ((RelayCommand)AddFolderCommand).RaiseCanExecuteChanged(); } }

        private HiderRecordViewModel? _selectedRecord;
        public HiderRecordViewModel? SelectedRecord { get => _selectedRecord; set { _selectedRecord = value; RaisePropertyChanged(); ((RelayCommand)SetHiddenCommand).RaiseCanExecuteChanged(); ((RelayCommand)ClearHiddenCommand).RaiseCanExecuteChanged(); ((RelayCommand)ApplyAclCommand).RaiseCanExecuteChanged(); } }

        public ICommand AddFolderCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand RemoveRecordCommand { get; }
        public ICommand EditRecordCommand { get; }
        public ICommand SetHiddenCommand { get; }
        public ICommand ClearHiddenCommand { get; }
        public ICommand ApplyAclCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand SaveConfigCommand { get; }

        private void AddFolder()
        {
            var rec = new HiderRecord { FolderPath = NewFolderPath };
            var cfg = HiderService.LoadConfig(_configPath) ?? HiderService.GetDefaultConfig();
            HiderService.AddRecord(cfg, rec);
            HiderService.SaveConfig(cfg, _configPath);
            Records.Add(new HiderRecordViewModel(rec));
            NewFolderPath = string.Empty;
        }

        private void BrowseFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.ShowNewFolderButton = true;
            if (!string.IsNullOrWhiteSpace(NewFolderPath))
                dlg.SelectedPath = NewFolderPath;
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                NewFolderPath = dlg.SelectedPath;
            }
        }

        private void RemoveRecord(HiderRecordViewModel vm)
        {
            if (vm == null) return;
            var cfg = HiderService.LoadConfig(_configPath) ?? HiderService.GetDefaultConfig();
            HiderService.RemoveRecord(cfg, vm.FolderPath);
            HiderService.SaveConfig(cfg, _configPath);
            Records.Remove(vm);
        }

        private void EditRecord(HiderRecordViewModel vm)
        {
            if (vm == null) return;
            var editVm = new HiderEditViewModel(vm.Record);
            var dlg = new Views.HiderEditWindow { Owner = System.Windows.Application.Current?.MainWindow, DataContext = editVm };
            var result = dlg.ShowDialog();
            if (result == true)
            {
                // Apply edits back
                var oldPath = vm.FolderPath;
                vm.FolderPath = editVm.FolderPath;
                vm.AclRestricted = editVm.AclRestricted;
                vm.EfsEnabled = editVm.EfsEnabled;

                // Persist
                var cfg = HiderService.LoadConfig(_configPath) ?? HiderService.GetDefaultConfig();
                HiderService.UpdateRecord(cfg, oldPath, r => {
                    r.FolderPath = vm.FolderPath;
                    r.AclRestricted = vm.AclRestricted;
                    r.EfsEnabled = vm.EfsEnabled;
                    if (!string.IsNullOrEmpty(editVm.Password))
                    {
                        r.PasswordRecord = HiderService.CreatePasswordRecord(editVm.Password);
                    }
                });
                HiderService.SaveConfig(cfg, _configPath);
            }
        }

        private void SetSelectedHidden()
        {
            if (SelectedRecord == null) return;
            HiderService.SetHidden(SelectedRecord.FolderPath, true);
            SelectedRecord.RefreshHiddenState();
        }

        private void ClearSelectedHidden()
        {
            if (SelectedRecord == null) return;

            // If a password is set for this record, prompt for it before un-hiding
            var pr = SelectedRecord.Record.PasswordRecord;
            if (pr != null)
            {
                var msg = $"Unhide '{SelectedRecord.FolderPath}'";
                var dlg = new Views.PromptPasswordWindow(msg) { Owner = System.Windows.Application.Current?.MainWindow };
                var res = dlg.ShowDialog();
                if (res != true) return;
                var entered = dlg.EnteredPassword;
                if (!HiderService.TestPassword(entered, pr))
                {
                    System.Windows.MessageBox.Show("Incorrect password.", "Unhide failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
            }

            HiderService.SetHidden(SelectedRecord.FolderPath, false);
            SelectedRecord.RefreshHiddenState();
        }

        private void ApplyAclToSelected()
        {
            if (SelectedRecord == null) return;
            SecurityService.RestrictFolderToAdministrators(SelectedRecord.FolderPath);
            SelectedRecord.AclRestricted = true;
        }

        private void LoadConfig()
        {
            Records.Clear();
            var cfg = HiderService.LoadConfig(_configPath) ?? HiderService.GetDefaultConfig();
            foreach (var r in cfg.Folders)
            {
                var vm = new HiderRecordViewModel(r);
                vm.RefreshHiddenState();
                Records.Add(vm);
            }
        }

        private void SaveConfig()
        {
            var cfg = new HiderConfig();
            cfg.Folders.AddRange(Records.Select(r => r.Record));
            HiderService.SaveConfig(cfg, _configPath);
        }
    }
}