using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace PlatypusTools.UI.Views
{
    public partial class NativeImageEditView : UserControl
    {
        private SixLabors.ImageSharp.Image? _currentImage;
        private string? _originalImagePath;

        public NativeImageEditView()
        {
            InitializeComponent();
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tiff;*.tif;*.webp|All Files|*.*",
                Title = "Select Image File"
            };
            if (dialog.ShowDialog() == true)
            {
                LoadImage(dialog.FileName);
            }
        }

        private void LoadImage(string filePath)
        {
            try
            {
                _originalImagePath = filePath;
                _currentImage?.Dispose();
                _currentImage = SixLabors.ImageSharp.Image.Load(filePath);
                ImageFilePathBox.Text = filePath;
                PlaceholderText.Visibility = Visibility.Collapsed;
                UpdatePreview();
                StatusText.Text = $"Loaded: {_currentImage.Width}x{_currentImage.Height}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePreview()
        {
            if (_currentImage == null) return;

            try
            {
                using var ms = new MemoryStream();
                _currentImage.SaveAsPng(ms);
                ms.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                ImagePreview.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating preview: {ex.Message}");
            }
        }

        private void RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Rotate(RotateMode.Rotate270));
            UpdatePreview();
            StatusText.Text = "Rotated left 90°";
        }

        private void RotateRight_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Rotate(RotateMode.Rotate90));
            UpdatePreview();
            StatusText.Text = "Rotated right 90°";
        }

        private void FlipH_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Flip(FlipMode.Horizontal));
            UpdatePreview();
            StatusText.Text = "Flipped horizontally";
        }

        private void FlipV_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Flip(FlipMode.Vertical));
            UpdatePreview();
            StatusText.Text = "Flipped vertically";
        }

        private void Grayscale_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Grayscale());
            UpdatePreview();
            StatusText.Text = "Applied grayscale";
        }

        private void Invert_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Invert());
            UpdatePreview();
            StatusText.Text = "Inverted colors";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("Please load an image first.", "No Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var format = (OutputFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToUpperInvariant() ?? "PNG";
            var extension = format.ToLowerInvariant();

            var saveDialog = new SaveFileDialog
            {
                Filter = $"{format} Files (*.{extension})|*.{extension}|All Files (*.*)|*.*",
                FileName = _originalImagePath != null 
                    ? $"{Path.GetFileNameWithoutExtension(_originalImagePath)}_edited.{extension}"
                    : $"image.{extension}",
                Title = "Save Image"
            };

            if (saveDialog.ShowDialog() != true) return;

            try
            {
                switch (format)
                {
                    case "PNG":
                        _currentImage.Save(saveDialog.FileName, new PngEncoder());
                        break;
                    case "JPEG":
                        _currentImage.Save(saveDialog.FileName, new JpegEncoder { Quality = 90 });
                        break;
                    case "BMP":
                        _currentImage.Save(saveDialog.FileName, new BmpEncoder());
                        break;
                    case "GIF":
                        _currentImage.Save(saveDialog.FileName, new GifEncoder());
                        break;
                    case "WEBP":
                        _currentImage.Save(saveDialog.FileName, new WebpEncoder { Quality = 90 });
                        break;
                    default:
                        _currentImage.Save(saveDialog.FileName, new PngEncoder());
                        break;
                }

                StatusText.Text = $"Saved: {Path.GetFileName(saveDialog.FileName)}";
                MessageBox.Show($"Image saved successfully!\n\n{saveDialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_originalImagePath) && File.Exists(_originalImagePath))
            {
                LoadImage(_originalImagePath);
                StatusText.Text = "Reset to original";
            }
        }
    }
}
