using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class SymlinkManagerViewModel : BindableBase
    {
        private readonly SymlinkManagerService _service = new();

        public SymlinkManagerViewModel()
        {
            Links = new ObservableCollection<SymlinkManagerService.LinkInfo>();

            ScanCommand = new RelayCommand(async _ => await ScanAsync(), _ => !IsScanning);
            CreateSymlinkCommand = new RelayCommand(async _ => await CreateLinkAsync(SymlinkManagerService.LinkType.SymbolicLink));
            CreateHardLinkCommand = new RelayCommand(async _ => await CreateLinkAsync(SymlinkManagerService.LinkType.HardLink));
            CreateJunctionCommand = new RelayCommand(async _ => await CreateLinkAsync(SymlinkManagerService.LinkType.Junction));
            DeleteLinkCommand = new RelayCommand(_ => DeleteSelectedLink(), _ => SelectedLink != null);
            ValidateCommand = new RelayCommand(_ => ValidateLinks());
            BrowseScanFolderCommand = new RelayCommand(_ => BrowseScanFolder());
            BrowseLinkPathCommand = new RelayCommand(_ => BrowseLinkPath());
            BrowseTargetPathCommand = new RelayCommand(_ => BrowseTargetPath());
            CopyPathCommand = new RelayCommand(_ => { if (SelectedLink != null) Clipboard.SetText(SelectedLink.Path); }, _ => SelectedLink != null);
            CopyTargetCommand = new RelayCommand(_ => { if (SelectedLink != null) Clipboard.SetText(SelectedLink.Target); }, _ => SelectedLink != null);
        }

        private string _scanFolder = "";
        public string ScanFolder { get => _scanFolder; set => SetProperty(ref _scanFolder, value); }

        private bool _recursive = true;
        public bool Recursive { get => _recursive; set => SetProperty(ref _recursive, value); }

        private string _newLinkPath = "";
        public string NewLinkPath { get => _newLinkPath; set => SetProperty(ref _newLinkPath, value); }

        private string _newTargetPath = "";
        public string NewTargetPath { get => _newTargetPath; set => SetProperty(ref _newTargetPath, value); }

        private bool _isScanning;
        public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private SymlinkManagerService.LinkInfo? _selectedLink;
        public SymlinkManagerService.LinkInfo? SelectedLink { get => _selectedLink; set => SetProperty(ref _selectedLink, value); }

        public ObservableCollection<SymlinkManagerService.LinkInfo> Links { get; }

        public ICommand ScanCommand { get; }
        public ICommand CreateSymlinkCommand { get; }
        public ICommand CreateHardLinkCommand { get; }
        public ICommand CreateJunctionCommand { get; }
        public ICommand DeleteLinkCommand { get; }
        public ICommand ValidateCommand { get; }
        public ICommand BrowseScanFolderCommand { get; }
        public ICommand BrowseLinkPathCommand { get; }
        public ICommand BrowseTargetPathCommand { get; }
        public ICommand CopyPathCommand { get; }
        public ICommand CopyTargetCommand { get; }

        private async Task ScanAsync()
        {
            if (string.IsNullOrEmpty(ScanFolder)) return;
            IsScanning = true;
            Links.Clear();
            StatusMessage = "Scanning for links...";

            try
            {
                var progress = new Progress<string>(msg => StatusMessage = msg);
                var results = await _service.ScanDirectoryAsync(ScanFolder, Recursive, progress);
                foreach (var link in results) Links.Add(link);

                var valid = results.Count(l => l.IsValid);
                var broken = results.Count(l => !l.IsValid);
                StatusMessage = $"Found {results.Count} links ({valid} valid, {broken} broken)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        private async Task CreateLinkAsync(SymlinkManagerService.LinkType type)
        {
            if (string.IsNullOrEmpty(NewLinkPath) || string.IsNullOrEmpty(NewTargetPath)) return;

            bool success = type switch
            {
                SymlinkManagerService.LinkType.SymbolicLink => await _service.CreateSymbolicLinkAsync(NewLinkPath, NewTargetPath),
                SymlinkManagerService.LinkType.HardLink => await _service.CreateHardLinkAsync(NewLinkPath, NewTargetPath),
                SymlinkManagerService.LinkType.Junction => await _service.CreateJunctionAsync(NewLinkPath, NewTargetPath),
                _ => false
            };

            StatusMessage = success ? $"{type} created successfully" : $"Failed to create {type} (may require admin rights)";

            if (success && !string.IsNullOrEmpty(ScanFolder))
                await ScanAsync();
        }

        private void DeleteSelectedLink()
        {
            if (SelectedLink == null) return;
            if (MessageBox.Show($"Delete link '{System.IO.Path.GetFileName(SelectedLink.Path)}'?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            if (_service.DeleteLink(SelectedLink.Path))
            {
                Links.Remove(SelectedLink);
                StatusMessage = "Link deleted";
            }
            else
            {
                StatusMessage = "Failed to delete link";
            }
        }

        private void ValidateLinks()
        {
            _service.ValidateLinks(Links.ToList());
            // Force refresh
            var items = Links.ToList();
            Links.Clear();
            foreach (var item in items) Links.Add(item);
            var broken = items.Count(l => !l.IsValid);
            StatusMessage = $"Validated: {broken} broken links found";
        }

        private void BrowseScanFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                ScanFolder = dlg.SelectedPath;
        }

        private void BrowseLinkPath()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Select Link Location" };
            if (dlg.ShowDialog() == true) NewLinkPath = dlg.FileName;
        }

        private void BrowseTargetPath()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select Target" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                NewTargetPath = dlg.SelectedPath;
        }
    }
}
