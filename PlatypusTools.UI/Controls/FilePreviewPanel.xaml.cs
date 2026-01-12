using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.Controls
{
    public partial class FilePreviewPanel : UserControl
    {
        private string? _currentFilePath;
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico" };
        private static readonly string[] TextExtensions = { ".txt", ".md", ".json", ".xml", ".html", ".htm", ".css", ".js", ".ts", ".cs", ".py", ".ps1", ".sh", ".bat", ".cmd", ".log", ".csv", ".yaml", ".yml", ".ini", ".cfg", ".conf" };
        private const int MaxTextPreviewBytes = 1024 * 1024; // 1 MB
        private const int MaxHexPreviewBytes = 1024; // 1 KB

        public event EventHandler? PreviewClosed;

        public FilePreviewPanel()
        {
            InitializeComponent();
        }

        public void PreviewFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                ShowNoSelection();
                return;
            }

            _currentFilePath = filePath;
            HideAllPreviews();
            LoadingPanel.Visibility = Visibility.Visible;

            try
            {
                var fileInfo = new FileInfo(filePath);
                FileNameText.Text = fileInfo.Name;
                UpdateFileInfo(fileInfo);

                var ext = fileInfo.Extension.ToLowerInvariant();

                if (Array.Exists(ImageExtensions, e => e == ext))
                {
                    ShowImagePreview(filePath);
                }
                else if (Array.Exists(TextExtensions, e => e == ext))
                {
                    ShowTextPreview(filePath, fileInfo.Length);
                }
                else if (fileInfo.Length < MaxHexPreviewBytes * 10)
                {
                    ShowHexPreview(filePath);
                }
                else
                {
                    ShowUnsupported(ext);
                }
            }
            catch (Exception ex)
            {
                ShowUnsupported($"Error: {ex.Message}");
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        public void ClearPreview()
        {
            ShowNoSelection();
        }

        private void HideAllPreviews()
        {
            NoSelectionText.Visibility = Visibility.Collapsed;
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            TextScrollViewer.Visibility = Visibility.Collapsed;
            HexScrollViewer.Visibility = Visibility.Collapsed;
            UnsupportedPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            FileInfoPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowNoSelection()
        {
            HideAllPreviews();
            _currentFilePath = null;
            FileNameText.Text = "No file selected";
            NoSelectionText.Visibility = Visibility.Visible;
        }

        private void ShowImagePreview(string filePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath);
                bitmap.EndInit();
                bitmap.Freeze();

                ImagePreview.Source = bitmap;
                ImageScrollViewer.Visibility = Visibility.Visible;
                FileInfoPanel.Visibility = Visibility.Visible;
                FileDimensionsText.Text = $"{bitmap.PixelWidth}×{bitmap.PixelHeight}";
            }
            catch
            {
                ShowUnsupported("Could not load image");
            }
        }

        private void ShowTextPreview(string filePath, long fileSize)
        {
            try
            {
                string content;
                if (fileSize > MaxTextPreviewBytes)
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var reader = new StreamReader(stream, Encoding.UTF8, true);
                    var buffer = new char[MaxTextPreviewBytes / 2];
                    reader.ReadBlock(buffer, 0, buffer.Length);
                    content = new string(buffer) + $"\n\n... [File truncated, showing first {FormatSize(MaxTextPreviewBytes)}]";
                }
                else
                {
                    content = File.ReadAllText(filePath);
                }

                TextPreview.Text = content;
                TextScrollViewer.Visibility = Visibility.Visible;
                FileInfoPanel.Visibility = Visibility.Visible;
                FileDimensionsText.Text = "";
            }
            catch
            {
                ShowHexPreview(filePath);
            }
        }

        private void ShowHexPreview(string filePath)
        {
            try
            {
                var bytes = new byte[MaxHexPreviewBytes];
                int bytesRead;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    bytesRead = stream.Read(bytes, 0, bytes.Length);
                }

                var sb = new StringBuilder();
                for (int i = 0; i < bytesRead; i += 16)
                {
                    sb.Append($"{i:X8}  ");
                    
                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < bytesRead)
                            sb.Append($"{bytes[i + j]:X2} ");
                        else
                            sb.Append("   ");
                        
                        if (j == 7) sb.Append(' ');
                    }

                    sb.Append(" |");
                    for (int j = 0; j < 16 && i + j < bytesRead; j++)
                    {
                        var b = bytes[i + j];
                        sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                    }
                    sb.AppendLine("|");
                }

                if (bytesRead == MaxHexPreviewBytes)
                {
                    sb.AppendLine($"\n... [Showing first {MaxHexPreviewBytes} bytes]");
                }

                HexPreview.Text = sb.ToString();
                HexScrollViewer.Visibility = Visibility.Visible;
                FileInfoPanel.Visibility = Visibility.Visible;
                FileDimensionsText.Text = "Binary file";
            }
            catch
            {
                ShowUnsupported("Could not read file");
            }
        }

        private void ShowUnsupported(string typeInfo)
        {
            UnsupportedTypeText.Text = typeInfo;
            UnsupportedPanel.Visibility = Visibility.Visible;
            FileInfoPanel.Visibility = Visibility.Visible;
            FileDimensionsText.Text = "";
        }

        private void UpdateFileInfo(FileInfo fileInfo)
        {
            FileSizeText.Text = FormatSize(fileInfo.Length);
            FileModifiedText.Text = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void ClosePreview_Click(object sender, RoutedEventArgs e)
        {
            ClearPreview();
            PreviewClosed?.Invoke(this, EventArgs.Empty);
        }

        private void OpenWithDefault_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_currentFilePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
