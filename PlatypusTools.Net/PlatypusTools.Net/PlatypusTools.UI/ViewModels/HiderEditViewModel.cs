using PlatypusTools.Core.Models;
using System.ComponentModel;

namespace PlatypusTools.UI.ViewModels
{
    public class HiderEditViewModel : INotifyPropertyChanged
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
        public string FolderPath { get => _folderPath; set { _folderPath = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderPath))); } }

        private string? _password;
        public string? Password { get => _password; set { _password = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Password))); } }

        private bool _aclRestricted;
        public bool AclRestricted { get => _aclRestricted; set { _aclRestricted = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AclRestricted))); } }

        private bool _efsEnabled;
        public bool EfsEnabled { get => _efsEnabled; set { _efsEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EfsEnabled))); } }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}