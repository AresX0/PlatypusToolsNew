using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class FileDiffViewModel : BindableBase
    {
        private readonly FileDiffService _service = new();

        public FileDiffViewModel()
        {
            CompareCommand = new RelayCommand(async _ => await CompareAsync(), _ => !IsComparing);
            BrowseLeftCommand = new RelayCommand(_ => BrowseLeft());
            BrowseRightCommand = new RelayCommand(_ => BrowseRight());
            SwapCommand = new RelayCommand(_ => { (LeftFile, RightFile) = (RightFile, LeftFile); });
            CopyDiffCommand = new RelayCommand(_ => CopyDiff(), _ => DiffLines.Count > 0);
        }

        private string _leftFile = "";
        public string LeftFile { get => _leftFile; set => SetProperty(ref _leftFile, value); }

        private string _rightFile = "";
        public string RightFile { get => _rightFile; set => SetProperty(ref _rightFile, value); }

        private bool _isComparing;
        public bool IsComparing { get => _isComparing; set => SetProperty(ref _isComparing, value); }

        private string _statusMessage = "Select two files to compare";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _areIdentical;
        public bool AreIdentical { get => _areIdentical; set => SetProperty(ref _areIdentical, value); }

        private int _addedCount;
        public int AddedCount { get => _addedCount; set => SetProperty(ref _addedCount, value); }

        private int _removedCount;
        public int RemovedCount { get => _removedCount; set => SetProperty(ref _removedCount, value); }

        private int _unchangedCount;
        public int UnchangedCount { get => _unchangedCount; set => SetProperty(ref _unchangedCount, value); }

        public ObservableCollection<FileDiffService.DiffLine> DiffLines { get; } = new();

        public ICommand CompareCommand { get; }
        public ICommand BrowseLeftCommand { get; }
        public ICommand BrowseRightCommand { get; }
        public ICommand SwapCommand { get; }
        public ICommand CopyDiffCommand { get; }

        private async System.Threading.Tasks.Task CompareAsync()
        {
            if (string.IsNullOrEmpty(LeftFile) || string.IsNullOrEmpty(RightFile)) return;

            IsComparing = true;
            StatusMessage = "Comparing files...";
            DiffLines.Clear();

            try
            {
                var result = await _service.CompareFilesAsync(LeftFile, RightFile);
                foreach (var line in result.Lines)
                    DiffLines.Add(line);

                AreIdentical = result.AreIdentical;
                AddedCount = result.AddedCount;
                RemovedCount = result.RemovedCount;
                UnchangedCount = result.UnchangedCount;

                StatusMessage = result.AreIdentical
                    ? $"Files are identical ({result.Duration.TotalMilliseconds:F0}ms)"
                    : $"+{result.AddedCount} -{result.RemovedCount} ({result.Duration.TotalMilliseconds:F0}ms)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsComparing = false;
            }
        }

        private void BrowseLeft()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select Left File" };
            if (dlg.ShowDialog() == true) LeftFile = dlg.FileName;
        }

        private void BrowseRight()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select Right File" };
            if (dlg.ShowDialog() == true) RightFile = dlg.FileName;
        }

        private void CopyDiff()
        {
            try
            {
                var lines = DiffLines.Select(d =>
                {
                    var prefix = d.Type switch
                    {
                        FileDiffService.DiffLineType.Added => "+ ",
                        FileDiffService.DiffLineType.Removed => "- ",
                        _ => "  "
                    };
                    return prefix + (d.Type == FileDiffService.DiffLineType.Added ? d.RightText : d.LeftText);
                });
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
            }
            catch { }
        }
    }
}
