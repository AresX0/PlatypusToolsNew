using PlatypusTools.Core.Models;

namespace PlatypusTools.UI.ViewModels
{
    public class HiderEditViewModel : BindableBase
    {
        public HiderEditViewModel() { }
        public HiderEditViewModel(HiderRecord rec)
        {
            FolderPath = rec.FolderPath;
            AclRestricted = rec.AclRestricted;
            EfsEnabled = rec.EfsEnabled;
            // Password handling mirrors PS behaviour: stored in PasswordRecord if used (opaque)
            // For now we expose a simple string for UI entry; pluggable secure store can be added later.
        }

        private string _folderPath = string.Empty;
        public string FolderPath { get => _folderPath; set => SetProperty(ref _folderPath, value); }

        private string? _password;
        public string? Password { get => _password; set => SetProperty(ref _password, value); }

        private bool _aclRestricted;
        public bool AclRestricted { get => _aclRestricted; set => SetProperty(ref _aclRestricted, value); }

        private bool _efsEnabled;
        public bool EfsEnabled { get => _efsEnabled; set => SetProperty(ref _efsEnabled, value); }
    }
}