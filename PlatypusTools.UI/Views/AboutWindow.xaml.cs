using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            VersionText.Text = version?.ToString() ?? "3.0.0.0";
            
            var buildDate = GetBuildDate(assembly);
            BuildDateText.Text = buildDate.ToString("yyyy-MM-dd HH:mm:ss");
            
            DotNetText.Text = $"{Environment.Version} ({RuntimeInformation.FrameworkDescription})";
            OSText.Text = $"{Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";
        }

        private static DateTime GetBuildDate(Assembly assembly)
        {
            try
            {
                // For single-file apps, Assembly.Location is empty - use AppContext.BaseDirectory
                var location = assembly.Location;
                if (string.IsNullOrEmpty(location))
                {
                    // Try the EXE file in base directory
                    var exePath = System.IO.Path.Combine(AppContext.BaseDirectory, "PlatypusTools.UI.exe");
                    if (System.IO.File.Exists(exePath))
                    {
                        return System.IO.File.GetLastWriteTime(exePath);
                    }
                }
                else if (System.IO.File.Exists(location))
                {
                    return System.IO.File.GetLastWriteTime(location);
                }
            }
            catch { }
            return DateTime.Now;
        }

        private void GitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/AresX0/PlatypusToolsNew");
        }

        private void Docs_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/AresX0/PlatypusToolsNew/wiki");
        }

        private void Issue_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/AresX0/PlatypusToolsNew/issues");
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
