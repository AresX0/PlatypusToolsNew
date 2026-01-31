using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PlatypusTools.UI.Views
{
    public partial class NativeAudioTrimView : UserControl
    {
        private string? _currentFilePath;
        private MediaPlayer? _previewPlayer;

        public NativeAudioTrimView()
        {
            InitializeComponent();
            _previewPlayer = new MediaPlayer();
        }

        private void BrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.ogg;*.m4a;*.wma|All Files|*.*",
                Title = "Select Audio File"
            };
            if (dialog.ShowDialog() == true)
            {
                _currentFilePath = dialog.FileName;
                AudioFilePathBox.Text = dialog.FileName;
                StatusText.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
            }
        }

        private async void TrimAudio_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || !File.Exists(_currentFilePath))
            {
                MessageBox.Show("Please select an audio file first.", "No File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startTime = TrimStart.Text;
            var endTime = TrimEnd.Text;
            var format = (OutputFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "mp3";

            var saveDialog = new SaveFileDialog
            {
                Filter = $"{format.ToUpperInvariant()} Files (*.{format})|*.{format}|All Files (*.*)|*.*",
                FileName = $"{Path.GetFileNameWithoutExtension(_currentFilePath)}_trimmed.{format}",
                Title = "Save Trimmed Audio"
            };

            if (saveDialog.ShowDialog() != true) return;

            StatusText.Text = "Trimming audio...";
            try
            {
                await TrimWithFFmpegAsync(_currentFilePath, saveDialog.FileName, startTime, endTime);
                StatusText.Text = $"Saved: {Path.GetFileName(saveDialog.FileName)}";
                MessageBox.Show($"Audio trimmed successfully!\n\nSaved to: {saveDialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Trim failed - check FFmpeg installation";
                MessageBox.Show($"Error trimming audio: {ex.Message}\n\nMake sure FFmpeg is installed and in PATH.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task TrimWithFFmpegAsync(string inputPath, string outputPath, string startTime, string endTime)
        {
            var args = $"-i \"{inputPath}\" -ss {startTime} -to {endTime} -c copy \"{outputPath}\" -y";
            
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {error}");
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                MessageBox.Show("Please select an audio file first.", "No File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!TimeSpan.TryParse(TrimStart.Text, out var startTime))
                {
                    MessageBox.Show("Invalid start time format. Use HH:MM:SS", "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _previewPlayer?.Close();
                _previewPlayer = new MediaPlayer();
                _previewPlayer.Open(new Uri(_currentFilePath));
                _previewPlayer.Position = startTime;
                _previewPlayer.Play();
                StatusText.Text = $"Playing preview from {startTime}...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error playing preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopPreview_Click(object sender, RoutedEventArgs e)
        {
            _previewPlayer?.Stop();
            StatusText.Text = "Preview stopped";
        }

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                var folder = Path.GetDirectoryName(_currentFilePath);
                if (Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", folder);
                }
            }
            else
            {
                Process.Start("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            }
        }
    }
}
