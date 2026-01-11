using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class StagingWindow : Window
    {
        public StagingWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}