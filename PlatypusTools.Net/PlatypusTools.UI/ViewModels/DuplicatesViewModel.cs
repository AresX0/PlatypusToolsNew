using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using PlatypusTools.Core.Services;
using System.Linq;
using System.IO;
using System.Diagnostics;

namespace PlatypusTools.UI.ViewModels
{
    public class DuplicatesViewModel : BindableBase
    {
        public DuplicatesViewModel()
        {
            Groups = new ObservableCollection<DuplicateGroupViewModel>();
            UseRecycleBin = true; // default to safe operation
            BrowseCommand = new RelayCommand(_ => Browse());
            ScanCommand = new RelayCommand(_ => Scan());
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => Groups.Any(g => g.Files.Any(f => f.IsSelected)));

            OpenFileCommand = new RelayCommand(obj => OpenFile(obj as string));
            OpenFolderCommand = new RelayCommand(obj => OpenFolder(obj as string));
            RenameFileCommand = new RelayCommand(obj => RenameFile(obj as string));
            PreviewFileCommand = new RelayCommand(obj => PreviewFile(obj as string));
            StageFileCommand = new RelayCommand(obj => StageFile(obj as string));
            OpenStagingCommand = new RelayCommand(_ => OpenStaging());
            ToggleStagingCommand = new RelayCommand(_ => StagingVisible = !StagingVisible);
            // Per-group selection commands
            SelectNewestCommand = new RelayCommand(_ => SelectNewest());
            SelectOldestCommand = new RelayCommand(_ => SelectOldest());
            SelectLargestCommand = new RelayCommand(_ => SelectLargest());
            SelectSmallestCommand = new RelayCommand(_ => SelectSmallest());
            KeepOneCommand = new RelayCommand(_ => KeepOnePerGroup());

        }

        public ObservableCollection<DuplicateGroupViewModel> Groups { get; }

        public StagingViewModel Staging { get; } = new StagingViewModel();

        private bool _stagingVisible = false;
        public bool StagingVisible { get => _stagingVisible; set { _stagingVisible = value; RaisePropertyChanged(); } }

        public ICommand ToggleStagingCommand { get; }

        private string _folderPath = string.Empty;
        public string FolderPath { get => _folderPath; set { _folderPath = value; RaisePropertyChanged(); } }

        private bool _dryRun = true;
        public bool DryRun { get => _dryRun; set { _dryRun = value; RaisePropertyChanged(); } }

        public ICommand BrowseCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand RenameFileCommand { get; }
        public ICommand StageFileCommand { get; }
        public ICommand OpenStagingCommand { get; }

        public ICommand SelectNewestCommand { get; }
        public ICommand SelectOldestCommand { get; }
        public ICommand SelectLargestCommand { get; }
        public ICommand SelectSmallestCommand { get; }
        public ICommand KeepOneCommand { get; }
        public ICommand PreviewFileCommand { get; }
        private void Browse()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.ShowNewFolderButton = true;
            if (!string.IsNullOrWhiteSpace(FolderPath)) dlg.SelectedPath = FolderPath;
            var res = dlg.ShowDialog();
            if (res == System.Windows.Forms.DialogResult.OK) FolderPath = dlg.SelectedPath;
        }

        private void Scan()
        {
            Groups.Clear();
            if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath)) return;
            var groups = DuplicatesScanner.FindDuplicates(new[] { FolderPath }, recurse: true);
            foreach (var g in groups) Groups.Add(new DuplicateGroupViewModel(g));
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }

        private void SelectNewest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderByDescending(f => File.GetLastWriteTimeUtc(f.Path)).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
        }

        private void SelectOldest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderBy(f => File.GetLastWriteTimeUtc(f.Path)).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
        }

        private void SelectLargest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderByDescending(f => new FileInfo(f.Path).Length).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
        }

        private void SelectSmallest()
        {
            foreach (var g in Groups)
            {
                var chosen = g.Files.OrderBy(f => new FileInfo(f.Path).Length).FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f == chosen;
            }
        }

        private void KeepOnePerGroup()
        {
            foreach (var g in Groups)
            {
                var keep = g.Files.FirstOrDefault();
                foreach (var f in g.Files) f.IsSelected = f != keep;
            }
        }

        private void OpenFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        private void OpenFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var dir = Path.GetDirectoryName(path) ?? path;
            if (!Directory.Exists(dir)) return;
            try { Process.Start(new ProcessStartInfo("explorer", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
        }

        private void PreviewFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            LastPreviewedFilePath = path;
            if (PreviewVisible)
            {
                // If embedded preview is enabled, update property and avoid modal window
                return;
            }
            var dlg = new Views.PreviewWindow(path) { Owner = System.Windows.Application.Current?.MainWindow };
            dlg.ShowDialog();
        }

        private string _lastPreviewedFilePath = string.Empty;
        public string LastPreviewedFilePath { get => _lastPreviewedFilePath; set { _lastPreviewedFilePath = value; RaisePropertyChanged(); } }

        private bool _previewVisible = false;
        public bool PreviewVisible { get => _previewVisible; set { _previewVisible = value; RaisePropertyChanged(); } }

        private void OpenStaging()
        {
            var dlg = new Views.StagingWindow() { Owner = System.Windows.Application.Current?.MainWindow };
            dlg.ShowDialog();
        }

        private void RenameFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var dlg = new Views.InputDialogWindow("Rename duplicate", Path.GetFileName(path)) { Owner = System.Windows.Application.Current?.MainWindow };
            var res = dlg.ShowDialog();
            if (res != true) return;
            var newName = dlg.EnteredText;
            if (string.IsNullOrWhiteSpace(newName)) return;
            var dest = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, newName);
            try { File.Move(path, dest); } catch { System.Windows.MessageBox.Show("Rename failed.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
            // Refresh scan
            Scan();
        }

        private void StageFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                var destPath = StageFileToStaging(path);
                if (destPath == null) return;
                var stagingRoot = Path.GetDirectoryName(destPath) ?? Path.Combine(Path.GetTempPath(), "PlatypusTools", "DuplicatesStaging");
                var res = System.Windows.MessageBox.Show($"Staged '{path}' to '{destPath}'.\nOpen staging folder?", "Staged", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                if (res == System.Windows.MessageBoxResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"\"{stagingRoot}\"") { UseShellExecute = true }); } catch { }
                }
            }
            catch
            {
                System.Windows.MessageBox.Show("Staging failed.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public string? StageFileToStaging(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var stagingRoot = Path.Combine(Path.GetTempPath(), "PlatypusTools", "DuplicatesStaging");
            Directory.CreateDirectory(stagingRoot);
            var destName = Path.GetFileName(path);
            var destPath = Path.Combine(stagingRoot, destName);
            var i = 1;
            while (File.Exists(destPath))
            {
                destPath = Path.Combine(stagingRoot, Path.GetFileNameWithoutExtension(destName) + $" ({i})" + Path.GetExtension(destName));
                i++;
            }
            File.Copy(path, destPath);
            // write metadata so staging UI can know original path
            try
            {
                File.WriteAllText(destPath + ".meta", path);
            }
            catch { }
            return destPath;
        }
        public bool UseRecycleBin { get; set; }

        private void DeleteSelected()
        {
            var files = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Select(f => f.Path).ToList();
            if (files.Count == 0) return;
            if (DryRun)
            {
                System.Windows.MessageBox.Show($"Dry-run: would remove {files.Count} files.", "Preview", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var confirm = System.Windows.MessageBox.Show($"Proceed to remove {files.Count} files?", "Confirm", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            DeleteSelectedConfirmed();
        }

        // Public API for tests and non-UI callers to delete selected files without confirmation dialogs
        public void DeleteSelectedConfirmed()
        {
            var files = Groups.SelectMany(g => g.Files).Where(f => f.IsSelected).Select(f => f.Path).ToList();
            if (files.Count == 0) return;

            foreach (var f in files)
            {
                try
                {
                    if (UseRecycleBin)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        File.Delete(f);
                    }
                }
                catch { }
            }
            // Refresh scan
            Scan();
        }
    }
}