using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class StagedFileViewModel : BindableBase
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string StagedPath { get; set; } = string.Empty;
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; RaisePropertyChanged(); } }
    }

    /// <summary>
    /// ViewModel for staged files. Uses async initialization to defer file scanning.
    /// </summary>
    public class StagingViewModel : AsyncBindableBase
    {
        public StagingViewModel()
        {
            StagedFiles = new ObservableCollection<StagedFileViewModel>();
            OpenFolderCommand = new RelayCommand(_ => OpenFolder());
            RestoreSelectedCommand = new RelayCommand(_ => RestoreSelected(), _ => StagedFiles.Any(sf => sf.IsSelected));
            RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected(), _ => StagedFiles.Any(sf => sf.IsSelected));
            CommitSelectedCommand = new RelayCommand(_ => CommitSelected(), _ => StagedFiles.Any(sf => sf.IsSelected));
            // Deferred - will load when view is shown
        }

        /// <summary>
        /// Async initialization - loads staged files when view is loaded.
        /// </summary>
        protected override Task OnInitializeAsync()
        {
            LoadStagedFiles();
            return Task.CompletedTask;
        }

        public ObservableCollection<StagedFileViewModel> StagedFiles { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand RestoreSelectedCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand CommitSelectedCommand { get; }

        private string StagingRoot => Path.Combine(Path.GetTempPath(), "PlatypusTools", "DuplicatesStaging");

        public void LoadStagedFiles()
        {
            StagedFiles.Clear();
            if (!Directory.Exists(StagingRoot)) return;
            foreach (var f in Directory.GetFiles(StagingRoot))
            {
                if (f.EndsWith(".meta")) continue;
                var meta = f + ".meta";
                var original = File.Exists(meta) ? File.ReadAllText(meta) : string.Empty;
                StagedFiles.Add(new StagedFileViewModel { OriginalPath = original, StagedPath = f });
            }
        }

        private void OpenFolder()
        {
            if (!Directory.Exists(StagingRoot)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"\"{StagingRoot}\"") { UseShellExecute = true }); } catch { }
        }

        public void RestoreSelected()
        {
            foreach (var s in StagedFiles.Where(s => s.IsSelected).ToList())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(s.OriginalPath)) continue;
                    var destDir = Path.GetDirectoryName(s.OriginalPath) ?? string.Empty;
                    Directory.CreateDirectory(destDir);
                    var dest = s.OriginalPath;
                    var i = 1;
                    while (File.Exists(dest))
                    {
                        dest = Path.Combine(destDir, Path.GetFileNameWithoutExtension(s.OriginalPath) + $" ({i})" + Path.GetExtension(s.OriginalPath));
                        i++;
                    }
                    File.Copy(s.StagedPath, dest);
                    UndoRedoService.Instance.RecordCopy(s.StagedPath, dest);
                }
                catch { }
            }
            LoadStagedFiles();
        }

        public void RemoveSelected()
        {
            var undoOps = new System.Collections.Generic.List<FileOperation>();
            var undoBackupDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "UndoBackups");
            foreach (var s in StagedFiles.Where(s => s.IsSelected).ToList())
            {
                try
                {
                    // Backup for undo before deleting
                    string? backupPath = null;
                    if (File.Exists(s.StagedPath))
                    {
                        System.IO.Directory.CreateDirectory(undoBackupDir);
                        backupPath = Path.Combine(undoBackupDir, System.Guid.NewGuid().ToString("N") + Path.GetExtension(s.StagedPath));
                        File.Copy(s.StagedPath, backupPath);
                    }
                    File.Delete(s.StagedPath);
                    undoOps.Add(new FileOperation { Type = OperationType.Delete, OriginalPath = s.StagedPath, BackupPath = backupPath, Timestamp = System.DateTime.Now });
                    var meta = s.StagedPath + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }
                catch { }
            }
            if (undoOps.Count > 0)
                UndoRedoService.Instance.RecordBatch(undoOps, $"Remove {undoOps.Count} staged files");
            LoadStagedFiles();
        }

        public void CommitSelected()
        {
            var undoOps = new System.Collections.Generic.List<FileOperation>();
            var undoBackupDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "UndoBackups");
            // Commit defined here as deleting the original file that the staged copy came from
            foreach (var s in StagedFiles.Where(s => s.IsSelected).ToList())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(s.OriginalPath) && File.Exists(s.OriginalPath))
                    {
                        // Backup original for undo
                        System.IO.Directory.CreateDirectory(undoBackupDir);
                        var backupPath = Path.Combine(undoBackupDir, System.Guid.NewGuid().ToString("N") + Path.GetExtension(s.OriginalPath));
                        File.Copy(s.OriginalPath, backupPath);
                        File.Delete(s.OriginalPath);
                        undoOps.Add(new FileOperation { Type = OperationType.Delete, OriginalPath = s.OriginalPath, BackupPath = backupPath, Timestamp = System.DateTime.Now });
                    }
                    File.Delete(s.StagedPath);
                    var meta = s.StagedPath + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }
                catch { }
            }
            if (undoOps.Count > 0)
                UndoRedoService.Instance.RecordBatch(undoOps, $"Commit {undoOps.Count} staged files");
            LoadStagedFiles();
        }
    }
}