using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for RebootAnalyzerView.xaml
    /// </summary>
    public partial class RebootAnalyzerView : UserControl
    {
        public RebootAnalyzerView()
        {
            InitializeComponent();
        }

        private void DaysComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ViewModels.RebootAnalyzerViewModel viewModel && DaysComboBox.SelectedItem is ComboBoxItem item)
            {
                var text = item.Content?.ToString() ?? "";
                if (text.Contains("7"))
                    viewModel.DaysToAnalyze = 7;
                else if (text.Contains("30"))
                    viewModel.DaysToAnalyze = 30;
                else if (text.Contains("90"))
                    viewModel.DaysToAnalyze = 90;
                else if (text.Contains("365"))
                    viewModel.DaysToAnalyze = 365;
            }
        }
    }
}
