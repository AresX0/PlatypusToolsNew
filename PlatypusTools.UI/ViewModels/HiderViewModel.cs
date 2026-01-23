using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Utilities;
using System;

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
            // Debug entry
            SimpleLogger.Debug("Initializing HiderViewModel");
            
            try
            {
                Records = new ObservableCollection<HiderRecordViewModel>();

                _configPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools",
                    "hider.json");

                SimpleLogger.Debug($"Config path: {_configPath}");

            LoadConfig();

            AddFolderCommand = new RelayCommand(_ => AddFolder(), _ => !string.IsNullOrWhiteSpace(NewFolderPath));
            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            RemoveRecordCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) RemoveRecord(vm); });
            EditRecordCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) EditRecord(vm); });

            SetHiddenCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) SetHidden(vm); });
            ClearHiddenCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) ClearHidden(vm); });
            ApplyAclCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) ApplyAcl(vm); });
            ApplyEfsCommand = new RelayCommand(obj => { if (obj is HiderRecordViewModel vm) ApplyEfs(vm); });

            LoadConfigCommand = new RelayCommand(_ => LoadConfig());
            SaveConfigCommand = new RelayCommand(_ => SaveConfig());
            
                SimpleLogger.Debug("HiderViewModel initialized successfully");
                // Debug exit
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("HiderViewModel constructor" + " - " + ex.Message);
                throw;
            }
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
        public ICommand ApplyEfsCommand { get; }
        public ICommand LoadConfigCommand { get; }
        public ICommand SaveConfigCommand { get; }

        private void AddFolder()
        {
            // Debug entry
            SimpleLogger.Debug($"Adding folder: {NewFolderPath}");
            
            try
            {
                var rec = new HiderRecord { FolderPath = NewFolderPath };
                var cfg = HiderService.LoadConfig(_configPath) ?? HiderService.GetDefaultConfig();
                HiderService.AddRecord(cfg, rec);
                HiderService.SaveConfig(cfg, _configPath);
                Records.Add(new HiderRecordViewModel(rec));
                NewFolderPath = string.Empty;
                SimpleLogger.Debug("Folder added successfully");
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("AddFolder" + " - " + ex.Message);
                throw;
            }
            finally
            {
                // Debug exit
            }
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

        private void SetHidden(HiderRecordViewModel vm)
        {
            if (vm == null) return;
            HiderService.SetHidden(vm.FolderPath, true);
            vm.RefreshHiddenState();
            SaveConfig();
        }

        private void ClearHidden(HiderRecordViewModel vm)
        {
            if (vm == null) return;

            // If a password is set for this record, prompt for it before un-hiding
            var pr = vm.Record.PasswordRecord;
            if (pr != null)
            {
                var msg = $"Unhide '{vm.FolderPath}'";
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

            HiderService.SetHidden(vm.FolderPath, false);
            vm.RefreshHiddenState();
            SaveConfig();
        }

        private void ApplyAcl(HiderRecordViewModel vm)
        {
            if (vm == null) return;
            try
            {
                if (vm.AclRestricted)
                {
                    SecurityService.RestrictFolderToAdministrators(vm.FolderPath);
                    System.Windows.MessageBox.Show($"ACL restrictions applied to:\n{vm.FolderPath}", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("Enable 'Restrict to Administrators' checkbox first, then click ACL button.", "Info", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                SaveConfig();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to apply ACL:\n{ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ApplyEfs(HiderRecordViewModel vm)
        {
            if (vm == null) return;
            try
            {
                if (vm.EfsEnabled)
                {
                    // Check if folder can be encrypted
                    if (!HiderService.CanEncrypt(vm.FolderPath))
                    {
                        var status = HiderService.GetEncryptionStatusText(vm.FolderPath);
                        System.Windows.MessageBox.Show($"Cannot encrypt folder:\n{vm.FolderPath}\n\nStatus: {status}\n\nNote: EFS requires an NTFS file system.", 
                            "EFS Encryption", 
                            System.Windows.MessageBoxButton.OK, 
                            System.Windows.MessageBoxImage.Warning);
                        return;
                    }

                    // Check if already encrypted
                    if (HiderService.IsEncrypted(vm.FolderPath))
                    {
                        System.Windows.MessageBox.Show($"Folder is already encrypted:\n{vm.FolderPath}", 
                            "EFS Encryption", 
                            System.Windows.MessageBoxButton.OK, 
                            System.Windows.MessageBoxImage.Information);
                        return;
                    }

                    // Encrypt the folder
                    if (HiderService.EncryptFolder(vm.FolderPath))
                    {
                        System.Windows.MessageBox.Show($"✅ Folder encrypted successfully:\n{vm.FolderPath}\n\nAll files in this folder are now protected with Windows EFS.", 
                            "EFS Encryption", 
                            System.Windows.MessageBoxButton.OK, 
                            System.Windows.MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"❌ Failed to encrypt folder:\n{vm.FolderPath}\n\nCheck that you have permission and the drive uses NTFS.", 
                            "EFS Encryption", 
                            System.Windows.MessageBoxButton.OK, 
                            System.Windows.MessageBoxImage.Error);
                    }
                }
                else
                {
                    // Decrypt if checkbox is unchecked
                    if (HiderService.IsEncrypted(vm.FolderPath))
                    {
                        var result = System.Windows.MessageBox.Show($"Remove EFS encryption from:\n{vm.FolderPath}?\n\nThis will decrypt all files in the folder.", 
                            "Remove EFS Encryption", 
                            System.Windows.MessageBoxButton.YesNo, 
                            System.Windows.MessageBoxImage.Question);
                        
                        if (result == System.Windows.MessageBoxResult.Yes)
                        {
                            if (HiderService.DecryptFolder(vm.FolderPath))
                            {
                                System.Windows.MessageBox.Show($"✅ Folder decrypted successfully:\n{vm.FolderPath}", 
                                    "EFS Decryption", 
                                    System.Windows.MessageBoxButton.OK, 
                                    System.Windows.MessageBoxImage.Information);
                            }
                            else
                            {
                                System.Windows.MessageBox.Show($"❌ Failed to decrypt folder:\n{vm.FolderPath}", 
                                    "EFS Decryption", 
                                    System.Windows.MessageBoxButton.OK, 
                                    System.Windows.MessageBoxImage.Error);
                            }
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Enable 'Use EFS' checkbox first, then click EFS button to encrypt.", "Info", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
                SaveConfig();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to apply EFS:\n{ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
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
