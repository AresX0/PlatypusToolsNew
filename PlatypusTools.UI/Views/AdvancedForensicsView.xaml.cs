using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PlatypusTools.UI.Views
{
    public partial class AdvancedForensicsView : UserControl
    {
        public AdvancedForensicsView()
        {
            InitializeComponent();
        }

        /// <summary>Phase 4.3 — open a Sigma YAML file, translate to KQL, load into the editor.</summary>
        private void SigmaToKql_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open Sigma Rule",
                Filter = "Sigma YAML (*.yml;*.yaml)|*.yml;*.yaml|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var yaml = System.IO.File.ReadAllText(dlg.FileName);
                var result = Services.Security.SigmaToKqlTranslator.Translate(yaml);
                if (DataContext is ViewModels.AdvancedForensicsViewModel vm)
                {
                    vm.LocalKqlQuery = result.Kql;
                }
                if (result.Notes.Count > 0)
                {
                    MessageBox.Show(
                        "Translation notes:\n\n" + string.Join("\n", result.Notes),
                        "Sigma → KQL", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to translate Sigma rule: {ex.Message}",
                    "Sigma → KQL", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
