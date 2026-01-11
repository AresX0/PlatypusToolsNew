using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlatypusTools.UI.ViewModels
{
    public class DuplicateFileViewModel : INotifyPropertyChanged
    {
        public DuplicateFileViewModel(string path)
        {
            Path = path;
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }

        public string Path { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}