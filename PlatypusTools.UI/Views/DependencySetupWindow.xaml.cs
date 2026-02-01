using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Window for setting up external dependencies on first run or from settings.
    /// </summary>
    public partial class DependencySetupWindow : Window
    {
        private readonly DependencyCheckerService _checker = new();
        private DependencyCheckResult? _lastResult;
        
        public DependencySetupWindow()
        {
            InitializeComponent();
            
            // Show tools path
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            ToolsPathText.Text = toolsPath;
            
            Loaded += async (s, e) => await CheckDependenciesAsync();
        }
        
        private async Task CheckDependenciesAsync()
        {
            StatusMessage.Text = "Checking dependencies...";
            
            _lastResult = await _checker.CheckAllDependenciesAsync();
            
            // Update FFmpeg status
            if (_lastResult.FFmpegInstalled)
            {
                FFmpegStatus.Text = "✅";
                FFmpegPath.Text = "Installed";
                FFmpegInstallBtn.IsEnabled = false;
                FFmpegInstallBtn.Content = "Installed";
            }
            else
            {
                FFmpegStatus.Text = "❌";
                FFmpegPath.Text = "Not found";
                FFmpegInstallBtn.IsEnabled = true;
                FFmpegInstallBtn.Content = "Install";
            }
            
            // Update ExifTool status
            if (_lastResult.ExifToolInstalled)
            {
                ExifToolStatus.Text = "✅";
                ExifToolPath.Text = "Installed";
                ExifToolInstallBtn.IsEnabled = false;
                ExifToolInstallBtn.Content = "Installed";
            }
            else
            {
                ExifToolStatus.Text = "❌";
                ExifToolPath.Text = "Not found";
                ExifToolInstallBtn.IsEnabled = true;
                ExifToolInstallBtn.Content = "Install";
            }
            
            // Update WebView2 status
            if (_lastResult.WebView2Installed)
            {
                WebView2Status.Text = "✅";
                WebView2Path.Text = "Installed";
                WebView2InstallBtn.IsEnabled = false;
                WebView2InstallBtn.Content = "Installed";
            }
            else
            {
                WebView2Status.Text = "❌";
                WebView2Path.Text = "Not found";
                WebView2InstallBtn.IsEnabled = true;
                WebView2InstallBtn.Content = "Install";
            }
            
            // Update status message
            if (_lastResult.AllDependenciesMet)
            {
                StatusMessage.Text = "✅ All dependencies are installed! You're ready to use all features.";
            }
            else
            {
                var missing = new System.Collections.Generic.List<string>();
                if (!_lastResult.FFmpegInstalled) missing.Add("FFmpeg");
                if (!_lastResult.ExifToolInstalled) missing.Add("ExifTool");
                if (!_lastResult.WebView2Installed) missing.Add("WebView2");
                StatusMessage.Text = $"⚠️ Missing: {string.Join(", ", missing)}. Some features may not work.";
            }
        }
        
        private async void InstallFFmpeg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FFmpegInstallBtn.IsEnabled = false;
                FFmpegInstallBtn.Content = "Installing...";
                FFmpegStatus.Text = "⏳";
                
                var success = await DownloadAndInstallFFmpegAsync();
                
                if (success)
                {
                    MessageBox.Show("FFmpeg installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("FFmpeg installation failed. Please install manually.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await CheckDependenciesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing FFmpeg: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await CheckDependenciesAsync();
            }
        }
        
        private async void InstallExifTool_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExifToolInstallBtn.IsEnabled = false;
                ExifToolInstallBtn.Content = "Installing...";
                ExifToolStatus.Text = "⏳";
                
                var success = await DownloadAndInstallExifToolAsync();
                
                if (success)
                {
                    MessageBox.Show("ExifTool installed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("ExifTool installation failed. Please install manually.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await CheckDependenciesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing ExifTool: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await CheckDependenciesAsync();
            }
        }
        
        private async void InstallWebView2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WebView2InstallBtn.IsEnabled = false;
                WebView2InstallBtn.Content = "Installing...";
                WebView2Status.Text = "⏳";
                
                var success = await DownloadAndInstallWebView2Async();
                
                if (success)
                {
                    MessageBox.Show("WebView2 Runtime installer launched. Please follow the installer prompts.", "WebView2", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("WebView2 installation failed. Please install manually from Microsoft.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                
                await Task.Delay(3000); // Wait for installer
                await CheckDependenciesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing WebView2: {ex.Message}\n\nPlease install manually.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await CheckDependenciesAsync();
            }
        }
        
        private void OpenFFmpegSite_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ffmpeg.org/download.html",
                UseShellExecute = true
            });
        }
        
        private void OpenExifToolSite_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://exiftool.org/",
                UseShellExecute = true
            });
        }
        
        private void OpenWebView2Site_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                UseShellExecute = true
            });
        }
        
        private void OpenToolsFolder_Click(object sender, RoutedEventArgs e)
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsPath))
            {
                Directory.CreateDirectory(toolsPath);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = toolsPath,
                UseShellExecute = true
            });
        }
        
        private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            await CheckDependenciesAsync();
        }
        
        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            // Save preference
            if (DontShowAgainCheck.IsChecked == true)
            {
                var settings = SettingsManager.Current;
                settings.HasSeenDependencyPrompt = true;
                SettingsManager.SaveCurrent();
            }
            
            DialogResult = true;
            Close();
        }
        
        #region Download and Install Methods
        
        private async Task<bool> DownloadAndInstallFFmpegAsync()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsPath)) Directory.CreateDirectory(toolsPath);
            
            // Download FFmpeg from gyan.dev (most reliable Windows builds)
            var ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
            var zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
            var extractPath = Path.Combine(Path.GetTempPath(), "ffmpeg_extract");
            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            
            // Download
            var response = await client.GetAsync(ffmpegUrl);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(zipPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            // Extract
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            
            // Find ffmpeg.exe in extracted folder
            var ffmpegExe = Directory.GetFiles(extractPath, "ffmpeg.exe", SearchOption.AllDirectories);
            if (ffmpegExe.Length == 0) return false;
            
            // Copy to tools folder
            var destPath = Path.Combine(toolsPath, "ffmpeg.exe");
            File.Copy(ffmpegExe[0], destPath, true);
            
            // Also copy ffprobe if available
            var ffprobeExe = Directory.GetFiles(extractPath, "ffprobe.exe", SearchOption.AllDirectories);
            if (ffprobeExe.Length > 0)
            {
                File.Copy(ffprobeExe[0], Path.Combine(toolsPath, "ffprobe.exe"), true);
            }
            
            // Cleanup
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(extractPath, true); } catch { }
            
            return File.Exists(destPath);
        }
        
        private async Task<bool> DownloadAndInstallExifToolAsync()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            if (!Directory.Exists(toolsPath)) Directory.CreateDirectory(toolsPath);
            
            // Download ExifTool Windows executable
            var exifToolUrl = "https://exiftool.org/exiftool-12.76.zip";
            var zipPath = Path.Combine(Path.GetTempPath(), "exiftool.zip");
            var extractPath = Path.Combine(Path.GetTempPath(), "exiftool_extract");
            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            
            // Download
            var response = await client.GetAsync(exifToolUrl);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(zipPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            // Extract
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            
            // Find exiftool executable (it's named exiftool(-k).exe in the zip)
            var exeFiles = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories);
            if (exeFiles.Length == 0) return false;
            
            // Rename to exiftool.exe and copy
            var destPath = Path.Combine(toolsPath, "exiftool.exe");
            File.Copy(exeFiles[0], destPath, true);
            
            // Cleanup
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(extractPath, true); } catch { }
            
            return File.Exists(destPath);
        }
        
        private async Task<bool> DownloadAndInstallWebView2Async()
        {
            var installerPath = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
            
            // Download WebView2 bootstrapper
            var webView2Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(2);
            
            var response = await client.GetAsync(webView2Url);
            if (!response.IsSuccessStatusCode) return false;
            
            await using var fs = new FileStream(installerPath, FileMode.Create);
            await response.Content.CopyToAsync(fs);
            fs.Close();
            
            // Run installer
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            };
            
            Process.Start(psi);
            
            return true;
        }
        
        #endregion
    }
}
