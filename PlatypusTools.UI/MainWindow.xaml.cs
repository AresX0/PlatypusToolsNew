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
        private void OpenLogViewer(object sender, RoutedEventArgs e)
        {
            try
            {
                var logWindow = new Views.LogViewerWindow();
                logWindow.Owner = this;
                logWindow.Show();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error opening log viewer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void CheckForUpdates(object sender, RoutedEventArgs e)
        {
            try
            {
                var updateService = Services.UpdateService.Instance;
                var updateInfo = await updateService.CheckForUpdatesAsync();
                
                if (updateInfo != null)
                {
                    var updateWindow = new Views.UpdateWindow(updateInfo);
                    updateWindow.Owner = this;
                    updateWindow.ShowDialog();
                }
                else
                {
                    MessageBox.Show("You are running the latest version.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BackupSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PlatypusTools Backup (*.ptbackup)|*.ptbackup|All files (*.*)|*.*",
                    FileName = Services.SettingsBackupService.Instance.GetDefaultBackupFileName()
                };
                
                if (dlg.ShowDialog() == true)
                {
                    var success = await Services.SettingsBackupService.Instance.CreateBackupAsync(dlg.FileName);
                    if (success)
                    {
                        MessageBox.Show("Settings backed up successfully.", "Backup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to create backup.", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error backing up settings: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void RestoreSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "PlatypusTools Backup (*.ptbackup)|*.ptbackup|All files (*.*)|*.*"
                };
                
                if (dlg.ShowDialog() == true)
                {
                    var result = MessageBox.Show(
                        "Restoring settings will overwrite your current configuration.\n\nA backup of your current settings will be created first.\n\nContinue?",
                        "Restore Settings",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        var success = await Services.SettingsBackupService.Instance.RestoreBackupAsync(dlg.FileName);
                        if (success)
                        {
                            MessageBox.Show("Settings restored successfully.\n\nPlease restart the application for changes to take effect.", "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("Failed to restore settings.", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error restoring settings: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    
        private void OpenSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new Views.SettingsWindow();
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error opening settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
