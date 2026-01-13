using System.Windows;

namespace PlatypusTools.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Hook up closing for cleanup if needed
            this.Closed += (s, e) => { /* no-op */ };
        }

        private void ValidateDataContext_Click(object sender, RoutedEventArgs e)
        {
            var report = DataContextValidator.ValidateMainWindow(this);
            MessageBox.Show(report, "DataContext Validation", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowFileCleanerHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#file-cleaner");
        }

        private void ShowVideoConverterHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#media-conversion");
        }

        private void ShowVideoCombinerHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#media-conversion");
        }

        private void ShowDuplicatesHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#duplicates");
        }

        private void ShowSystemCleanerHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#cleanup");
        }

        private void ShowFolderHiderHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#security");
        }

        private void OpenHelpSection(string section)
        {
            try
            {
                var helpWindow = new Views.HelpWindow();
                helpWindow.Owner = this;
                helpWindow.Show();
                // WebView2 will handle the navigation to section via URL fragment
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening help: {ex.Message}", "Help Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowAbout(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "PLATYPUSTOOLS - Advanced File Management Suite\n\n" +
                "Version: 2.0 (WPF Edition)\n" +
                "Built with: .NET 10 / WPF\n\n" +
                "FEATURES:\n" +
                "• File Cleaner & Batch Renamer\n" +
                "• Video/Image Converter & Processor\n" +
                "• Duplicate File Finder\n" +
                "• System Cleanup Tools\n" +
                "• Privacy & Security Tools\n" +
                "• Metadata Editor\n\n" +
                "Originally developed as PowerShell scripts,\n" +
                "now rebuilt as a modern Windows application.\n\n" +
                "© 2026 PlatypusTools Project",
                "About PlatypusTools",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}