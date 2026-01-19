namespace PlatypusTools.UI.ViewModels
{
    public class DuplicateFileViewModel : BindableBase
    {
        public DuplicateFileViewModel(string path)
        {
            Path = path;
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        public string Path { get; }
    }
}