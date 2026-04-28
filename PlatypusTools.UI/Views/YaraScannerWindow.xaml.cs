using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using PlatypusTools.UI.Services.Security;

namespace PlatypusTools.UI.Views
{
    public partial class YaraScannerWindow : Window
    {
        public ObservableCollection<YaraMatch> Results { get; } = new();
        private CancellationTokenSource? _cts;

        public YaraScannerWindow()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = Results;
        }

        private void BrowseRules_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "YARA rules (*.yar;*.yara)|*.yar;*.yara|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) == true) RulesBox.Text = dlg.FileName;
        }

        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            if (dlg.ShowDialog(this) == true) TargetBox.Text = dlg.FolderName;
        }

        private void SampleRule_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "YARA rules (*.yar)|*.yar", FileName = "sample.yar" };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllText(dlg.FileName,
@"rule SuspiciousPowerShell
{
    meta:
        author = ""PlatypusTools""
        description = ""Detects common PowerShell download cradles""
    strings:
        $a = ""IEX""
        $b = ""DownloadString""
        $c = ""Invoke-Expression""
    condition:
        any of them
}
");
            RulesBox.Text = dlg.FileName;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            Results.Clear();
            if (string.IsNullOrWhiteSpace(RulesBox.Text) || string.IsNullOrWhiteSpace(TargetBox.Text))
            {
                StatusText.Text = "Provide rules file and target.";
                return;
            }
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            StatusText.Text = "Scanning...";
            try
            {
                var svc = new YaraScannerService();
                var matches = await svc.ScanAsync(RulesBox.Text, TargetBox.Text, RecursiveBox.IsChecked == true, _cts.Token);
                foreach (var m in matches) Results.Add(m);
                StatusText.Text = $"{Results.Count} match(es).";
            }
            catch (Exception ex) { StatusText.Text = "Error: " + ex.Message; }
        }
    }
}
