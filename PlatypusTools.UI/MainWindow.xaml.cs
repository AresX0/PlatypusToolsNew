using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Hook up closing for cleanup and exit prompt
            this.Closing += MainWindow_Closing;
            this.Closed += MainWindow_Closed;
            
            this.Loaded += (s, e) =>
            {
                StatusBarViewModel.Instance.Reset();
                RefreshRecentWorkspacesMenu();
            };
            
            // Subscribe to workspace changes
            RecentWorkspacesService.Instance.WorkspacesChanged += (s, e) => RefreshRecentWorkspacesMenu();
        }
        
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Show custom exit dialog with clear options
            var dialog = new Window
            {
                Title = "Exit PlatypusTools",
                Width = 380,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            
            var mainPanel = new StackPanel { Margin = new Thickness(15) };
            mainPanel.Children.Add(new TextBlock 
            { 
                Text = "What would you like to do?", 
                FontSize = 14, 
                Margin = new Thickness(0, 0, 0, 15),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            var exitBtn = new Button { Content = "Exit Application", Width = 120, Height = 30, Margin = new Thickness(5) };
            var minimizeBtn = new Button { Content = "Minimize", Width = 100, Height = 30, Margin = new Thickness(5) };
            var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(5) };
            
            string? choice = null;
            exitBtn.Click += (s, args) => { choice = "exit"; dialog.Close(); };
            minimizeBtn.Click += (s, args) => { choice = "minimize"; dialog.Close(); };
            cancelBtn.Click += (s, args) => { choice = "cancel"; dialog.Close(); };
            
            buttonPanel.Children.Add(exitBtn);
            buttonPanel.Children.Add(minimizeBtn);
            buttonPanel.Children.Add(cancelBtn);
            mainPanel.Children.Add(buttonPanel);
            dialog.Content = mainPanel;
            
            dialog.ShowDialog();
            
            switch (choice)
            {
                case "exit":
                    // User wants to exit - let the close proceed
                    break;
                    
                case "minimize":
                    // Minimize to taskbar instead of closing
                    e.Cancel = true;
                    this.WindowState = WindowState.Minimized;
                    break;
                    
                case "cancel":
                default:
                    // Cancel the close
                    e.Cancel = true;
                    break;
            }
        }
        
        private void MainWindow_Closed(object? sender, System.EventArgs e)
        {
            // Force complete shutdown - kill all processes
            try
            {
                // Shutdown the application completely
                Application.Current.Shutdown();
                
                // Force kill the process to ensure complete cleanup
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }

        private void RefreshRecentWorkspacesMenu()
        {
            if (RecentWorkspacesMenu == null) return;
            
            RecentWorkspacesMenu.Items.Clear();
            
            var recentWorkspaces = RecentWorkspacesService.Instance.RecentWorkspaces;
            
            if (recentWorkspaces.Count == 0)
            {
                var placeholder = new MenuItem
                {
                    Header = "(No recent workspaces)",
                    IsEnabled = false
                };
                RecentWorkspacesMenu.Items.Add(placeholder);
            }
            else
            {
                int index = 1;
                foreach (var workspace in recentWorkspaces.Take(10))
                {
                    var menuItem = new MenuItem
                    {
                        Header = $"_{index}. {workspace.Name}",
                        ToolTip = workspace.Path,
                        Tag = workspace.Path
                    };
                    menuItem.Click += RecentWorkspaceItem_Click;
                    RecentWorkspacesMenu.Items.Add(menuItem);
                    index++;
                }
                
                if (recentWorkspaces.Count > 10)
                {
                    RecentWorkspacesMenu.Items.Add(new Separator());
                    var moreItem = new MenuItem
                    {
                        Header = "More...",
                        Command = (DataContext as MainWindowViewModel)?.OpenRecentWorkspacesCommand
                    };
                    RecentWorkspacesMenu.Items.Add(moreItem);
                }
            }
        }

        private void RecentWorkspaceItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string path)
            {
                var vm = DataContext as MainWindowViewModel;
                if (vm != null)
                {
                    vm.SelectedFolder = path;
                    RecentWorkspacesService.Instance.AddWorkspace(path);
                }
            }
        }

        private void ClearRecentList_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all recent workspaces?",
                "Clear Recent Workspaces",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                RecentWorkspacesService.Instance.Clear();
                RefreshRecentWorkspacesMenu();
            }
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

