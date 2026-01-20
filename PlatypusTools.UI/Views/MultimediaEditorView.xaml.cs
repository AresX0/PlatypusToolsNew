using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PlatypusTools.UI.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace PlatypusTools.UI.Views
{
    public partial class MultimediaEditorView : UserControl
    {
        private DispatcherTimer? _videoTimer;
        private bool _isDraggingSlider;
        private SixLabors.ImageSharp.Image? _currentImage;
        private string? _originalImagePath;

        public MultimediaEditorView()
        {
            InitializeComponent();
            DataContextChanged += MultimediaEditorView_DataContextChanged;
            InitializeVideoTimer();
        }

        private void InitializeVideoTimer()
        {
            _videoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _videoTimer.Tick += VideoTimer_Tick;
        }

        private void MultimediaEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MultimediaEditorViewModel viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MultimediaEditorViewModel.FilePath) && sender is MultimediaEditorViewModel vm)
            {
                LoadMediaFile(vm.FilePath);
            }
        }

        private void LoadMediaFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg" };
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp" };

            // Load video in native player
            if (Array.Exists(videoExtensions, ext => ext == extension))
            {
                try
                {
                    NativeVideoPlayer.Source = new Uri(filePath);
                    NativeVideoPlayer.Volume = VideoVolumeSlider.Value;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading video: {ex.Message}");
                }
            }

            // Load image for editing
            if (Array.Exists(imageExtensions, ext => ext == extension))
            {
                LoadImageForEditing(filePath);
            }
        }

        private void LoadImageForEditing(string filePath)
        {
            try
            {
                _originalImagePath = filePath;
                _currentImage?.Dispose();
                _currentImage = SixLabors.ImageSharp.Image.Load(filePath);
                UpdateImagePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateImagePreview()
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

                ImageEditPreview.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating preview: {ex.Message}");
            }
        }

        #region Video Player Handlers

        private void VideoTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isDraggingSlider && NativeVideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = NativeVideoPlayer.NaturalDuration.TimeSpan;
                var position = NativeVideoPlayer.Position;
                
                VideoSeekSlider.Maximum = duration.TotalSeconds;
                VideoSeekSlider.Value = position.TotalSeconds;
                VideoPositionText.Text = position.ToString(@"hh\:mm\:ss");
                VideoDurationText.Text = duration.ToString(@"hh\:mm\:ss");
            }
        }

        private void NativeVideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (NativeVideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = NativeVideoPlayer.NaturalDuration.TimeSpan;
                VideoSeekSlider.Maximum = duration.TotalSeconds;
                VideoDurationText.Text = duration.ToString(@"hh\:mm\:ss");
            }
            _videoTimer?.Start();
        }

        private void NativeVideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _videoTimer?.Stop();
            NativeVideoPlayer.Position = TimeSpan.Zero;
            VideoSeekSlider.Value = 0;
        }

        private void VideoPlay_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel vm && !string.IsNullOrEmpty(vm.FilePath))
            {
                if (NativeVideoPlayer.Source == null || NativeVideoPlayer.Source.LocalPath != vm.FilePath)
                {
                    NativeVideoPlayer.Source = new Uri(vm.FilePath);
                }
            }
            NativeVideoPlayer.Play();
            _videoTimer?.Start();
        }

        private void VideoPause_Click(object sender, RoutedEventArgs e)
        {
            NativeVideoPlayer.Pause();
        }

        private void VideoStop_Click(object sender, RoutedEventArgs e)
        {
            NativeVideoPlayer.Stop();
            _videoTimer?.Stop();
            VideoSeekSlider.Value = 0;
            VideoPositionText.Text = "00:00:00";
        }

        private void VideoBack_Click(object sender, RoutedEventArgs e)
        {
            var newPosition = NativeVideoPlayer.Position - TimeSpan.FromSeconds(10);
            if (newPosition < TimeSpan.Zero) newPosition = TimeSpan.Zero;
            NativeVideoPlayer.Position = newPosition;
        }

        private void VideoForward_Click(object sender, RoutedEventArgs e)
        {
            if (NativeVideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var newPosition = NativeVideoPlayer.Position + TimeSpan.FromSeconds(10);
                if (newPosition > NativeVideoPlayer.NaturalDuration.TimeSpan)
                    newPosition = NativeVideoPlayer.NaturalDuration.TimeSpan;
                NativeVideoPlayer.Position = newPosition;
            }
        }

        private void VideoSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider || !NativeVideoPlayer.NaturalDuration.HasTimeSpan) return;
            
            // Only seek if value changed significantly (to avoid feedback loop)
            var difference = Math.Abs(NativeVideoPlayer.Position.TotalSeconds - e.NewValue);
            if (difference > 0.5)
            {
                NativeVideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        private void VideoVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (NativeVideoPlayer != null)
            {
                NativeVideoPlayer.Volume = e.NewValue;
            }
        }

        private void VideoMute_Click(object sender, RoutedEventArgs e)
        {
            NativeVideoPlayer.IsMuted = !NativeVideoPlayer.IsMuted;
        }

        private void VideoFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MultimediaEditorViewModel vm || string.IsNullOrEmpty(vm.FilePath))
            {
                MessageBox.Show("Please select a video file first.", "No Video", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var currentPosition = NativeVideoPlayer.Position;
            var wasPlaying = _videoTimer?.IsEnabled ?? false;
            NativeVideoPlayer.Pause();

            var fullscreenWindow = new Window
            {
                Title = "Video Player - Fullscreen",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None,
                Background = System.Windows.Media.Brushes.Black,
                ResizeMode = System.Windows.ResizeMode.NoResize
            };

            var mainGrid = new Grid();
            
            var fullscreenPlayer = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Source = new Uri(vm.FilePath),
                Volume = VideoVolumeSlider.Value
            };
            mainGrid.Children.Add(fullscreenPlayer);

            // Control panel overlay
            var controlPanel = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 30, 30, 30)),
                Height = 100,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Margin = new Thickness(50, 0, 50, 0),
                Visibility = Visibility.Collapsed
            };

            var controlStack = new StackPanel { Margin = new Thickness(20, 10, 20, 10) };
            
            var seekGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            
            var posText = new TextBlock { Text = "00:00:00", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            Grid.SetColumn(posText, 0);
            var seekSlider = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            Grid.SetColumn(seekSlider, 1);
            var durText = new TextBlock { Text = "00:00:00", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            Grid.SetColumn(durText, 2);
            seekGrid.Children.Add(posText);
            seekGrid.Children.Add(seekSlider);
            seekGrid.Children.Add(durText);
            controlStack.Children.Add(seekGrid);

            var buttonPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var playBtn = new Button { Content = "▶ Play", Width = 80, Height = 35, Margin = new Thickness(5) };
            var pauseBtn = new Button { Content = "⏸ Pause", Width = 80, Height = 35, Margin = new Thickness(5) };
            var stopBtn = new Button { Content = "⏹ Stop", Width = 80, Height = 35, Margin = new Thickness(5) };
            var backBtn = new Button { Content = "⏪ -10s", Width = 70, Height = 35, Margin = new Thickness(5) };
            var fwdBtn = new Button { Content = "⏩ +10s", Width = 70, Height = 35, Margin = new Thickness(5) };
            var exitBtn = new Button { Content = "✕ Exit", Width = 70, Height = 35, Margin = new Thickness(5), Background = System.Windows.Media.Brushes.IndianRed };
            var volLabel = new TextBlock { Text = "Vol:", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 5, 0) };
            var volSlider = new Slider { Minimum = 0, Maximum = 1, Value = VideoVolumeSlider.Value, Width = 100, VerticalAlignment = VerticalAlignment.Center };
            var muteBtn = new Button { Content = "🔇", Width = 40, Height = 35, Margin = new Thickness(5) };

            buttonPanel.Children.Add(playBtn);
            buttonPanel.Children.Add(pauseBtn);
            buttonPanel.Children.Add(stopBtn);
            buttonPanel.Children.Add(backBtn);
            buttonPanel.Children.Add(fwdBtn);
            buttonPanel.Children.Add(volLabel);
            buttonPanel.Children.Add(volSlider);
            buttonPanel.Children.Add(muteBtn);
            buttonPanel.Children.Add(exitBtn);
            controlStack.Children.Add(buttonPanel);

            controlPanel.Child = controlStack;
            mainGrid.Children.Add(controlPanel);
            fullscreenWindow.Content = mainGrid;

            var fsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            fsTimer.Tick += (s, args) =>
            {
                if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                {
                    var dur = fullscreenPlayer.NaturalDuration.TimeSpan;
                    var pos = fullscreenPlayer.Position;
                    seekSlider.Maximum = dur.TotalSeconds;
                    seekSlider.Value = pos.TotalSeconds;
                    posText.Text = pos.ToString(@"hh\:mm\:ss");
                    durText.Text = dur.ToString(@"hh\:mm\:ss");
                }
            };

            DateTime lastMouseMove = DateTime.Now;
            DispatcherTimer hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            hideTimer.Tick += (s, args) =>
            {
                if ((DateTime.Now - lastMouseMove).TotalSeconds > 2.5)
                {
                    controlPanel.Visibility = Visibility.Collapsed;
                    fullscreenWindow.Cursor = System.Windows.Input.Cursors.None;
                }
            };
            hideTimer.Start();

            mainGrid.MouseMove += (s, args) =>
            {
                lastMouseMove = DateTime.Now;
                fullscreenWindow.Cursor = System.Windows.Input.Cursors.Arrow;
                var mousePos = args.GetPosition(mainGrid);
                if (mousePos.Y > mainGrid.ActualHeight - 150)
                    controlPanel.Visibility = Visibility.Visible;
            };

            playBtn.Click += (s, args) => { fullscreenPlayer.Play(); fsTimer.Start(); };
            pauseBtn.Click += (s, args) => fullscreenPlayer.Pause();
            stopBtn.Click += (s, args) => { fullscreenPlayer.Stop(); fsTimer.Stop(); };
            backBtn.Click += (s, args) =>
            {
                var newPos = fullscreenPlayer.Position - TimeSpan.FromSeconds(10);
                if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
                fullscreenPlayer.Position = newPos;
            };
            fwdBtn.Click += (s, args) =>
            {
                if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                {
                    var newPos = fullscreenPlayer.Position + TimeSpan.FromSeconds(10);
                    if (newPos > fullscreenPlayer.NaturalDuration.TimeSpan)
                        newPos = fullscreenPlayer.NaturalDuration.TimeSpan;
                    fullscreenPlayer.Position = newPos;
                }
            };
            volSlider.ValueChanged += (s, args) => fullscreenPlayer.Volume = args.NewValue;
            muteBtn.Click += (s, args) => fullscreenPlayer.IsMuted = !fullscreenPlayer.IsMuted;
            
            Action exitFullscreen = () =>
            {
                fsTimer.Stop();
                hideTimer.Stop();
                var exitPos = fullscreenPlayer.Position;
                fullscreenPlayer.Stop();
                fullscreenWindow.Close();
                NativeVideoPlayer.Position = exitPos;
                if (wasPlaying) NativeVideoPlayer.Play();
                _videoTimer?.Start();
            };
            exitBtn.Click += (s, args) => exitFullscreen();

            seekSlider.ValueChanged += (s, args) =>
            {
                if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                {
                    var diff = Math.Abs(fullscreenPlayer.Position.TotalSeconds - args.NewValue);
                    if (diff > 1) fullscreenPlayer.Position = TimeSpan.FromSeconds(args.NewValue);
                }
            };

            fullscreenPlayer.MediaOpened += (s, args) =>
            {
                fullscreenPlayer.Position = currentPosition;
                fullscreenPlayer.Play();
                fsTimer.Start();
            };

            fullscreenPlayer.MediaEnded += (s, args) =>
            {
                fullscreenPlayer.Position = TimeSpan.Zero;
                fullscreenPlayer.Play();
            };

            fullscreenWindow.KeyDown += (s, args) =>
            {
                switch (args.Key)
                {
                    case System.Windows.Input.Key.Escape: exitFullscreen(); break;
                    case System.Windows.Input.Key.Space:
                        if (fullscreenPlayer.CanPause) { var p = fullscreenPlayer.Position; fullscreenPlayer.Pause(); fullscreenPlayer.Position = p; }
                        break;
                    case System.Windows.Input.Key.Left:
                        var bPos = fullscreenPlayer.Position - TimeSpan.FromSeconds(10);
                        fullscreenPlayer.Position = bPos < TimeSpan.Zero ? TimeSpan.Zero : bPos;
                        break;
                    case System.Windows.Input.Key.Right:
                        if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                        {
                            var fPos = fullscreenPlayer.Position + TimeSpan.FromSeconds(10);
                            fullscreenPlayer.Position = fPos > fullscreenPlayer.NaturalDuration.TimeSpan ? fullscreenPlayer.NaturalDuration.TimeSpan : fPos;
                        }
                        break;
                    case System.Windows.Input.Key.Up: fullscreenPlayer.Volume = Math.Min(1.0, fullscreenPlayer.Volume + 0.1); volSlider.Value = fullscreenPlayer.Volume; break;
                    case System.Windows.Input.Key.Down: fullscreenPlayer.Volume = Math.Max(0.0, fullscreenPlayer.Volume - 0.1); volSlider.Value = fullscreenPlayer.Volume; break;
                    case System.Windows.Input.Key.M: fullscreenPlayer.IsMuted = !fullscreenPlayer.IsMuted; break;
                }
            };

            fullscreenPlayer.MouseLeftButtonDown += (s, args) => { if (args.ClickCount == 2) exitFullscreen(); };

            fullscreenWindow.ShowDialog();
        }

        #endregion

        #region Audio Trim Handlers

        private async void AudioTrim_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MultimediaEditorViewModel vm || string.IsNullOrEmpty(vm.FilePath))
            {
                MessageBox.Show("Please select an audio file first.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startTime = AudioTrimStart.Text;
            var endTime = AudioTrimEnd.Text;
            var format = (AudioTrimFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "mp3";

            var saveDialog = new SaveFileDialog
            {
                Filter = $"{format.ToUpperInvariant()} Files (*.{format})|*.{format}|All Files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(vm.FilePath) + $"_trimmed.{format}",
                Title = "Save Trimmed Audio"
            };

            if (saveDialog.ShowDialog() != true) return;

            try
            {
                AudioTrimButton.IsEnabled = false;
                vm.StatusMessage = "Trimming audio...";

                var args = $"-i \"{vm.FilePath}\" -ss {startTime} -to {endTime} -c copy \"{saveDialog.FileName}\" -y";
                var result = await PlatypusTools.Core.Services.FFmpegService.RunAsync(args);

                if (result.Success)
                {
                    vm.StatusMessage = $"Audio trimmed successfully: {Path.GetFileName(saveDialog.FileName)}";
                    MessageBox.Show($"Audio trimmed successfully!\n\nSaved to: {saveDialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    vm.StatusMessage = "Audio trim failed";
                    MessageBox.Show($"Failed to trim audio:\n\n{result.StdErr}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error trimming audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AudioTrimButton.IsEnabled = true;
            }
        }

        private void AudioPreview_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MultimediaEditorViewModel vm || string.IsNullOrEmpty(vm.FilePath))
            {
                MessageBox.Show("Please select an audio file first.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Use the native video player for audio preview too
            NativeVideoPlayer.Source = new Uri(vm.FilePath);
            NativeVideoPlayer.Play();
            _videoTimer?.Start();
        }

        private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel vm && !string.IsNullOrEmpty(vm.FilePath))
            {
                var folder = Path.GetDirectoryName(vm.FilePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
        }

        #endregion

        #region Image Edit Handlers

        private void ImageRotateLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Rotate(-90));
            UpdateImagePreview();
            UpdateStatus("Rotated left 90°");
        }

        private void ImageRotateRight_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Rotate(90));
            UpdateImagePreview();
            UpdateStatus("Rotated right 90°");
        }

        private void ImageFlipH_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Flip(FlipMode.Horizontal));
            UpdateImagePreview();
            UpdateStatus("Flipped horizontally");
        }

        private void ImageFlipV_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Flip(FlipMode.Vertical));
            UpdateImagePreview();
            UpdateStatus("Flipped vertically");
        }

        private void ImageGrayscale_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Grayscale());
            UpdateImagePreview();
            UpdateStatus("Converted to grayscale");
        }

        private void ImageInvert_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            _currentImage.Mutate(x => x.Invert());
            UpdateImagePreview();
            UpdateStatus("Colors inverted");
        }

        private void ImageSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("Please load an image first.", "No Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var format = (ImageOutputFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToUpperInvariant() ?? "PNG";
            var extension = format.ToLowerInvariant();

            var saveDialog = new SaveFileDialog
            {
                Filter = $"{format} Files (*.{extension})|*.{extension}|All Files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(_originalImagePath ?? "image") + $"_edited.{extension}",
                Title = "Save Edited Image"
            };

            if (saveDialog.ShowDialog() != true) return;

            try
            {
                switch (format)
                {
                    case "PNG":
                        _currentImage.SaveAsPng(saveDialog.FileName);
                        break;
                    case "JPEG":
                        _currentImage.SaveAsJpeg(saveDialog.FileName);
                        break;
                    case "BMP":
                        _currentImage.SaveAsBmp(saveDialog.FileName);
                        break;
                    case "GIF":
                        _currentImage.SaveAsGif(saveDialog.FileName);
                        break;
                    case "WEBP":
                        _currentImage.SaveAsWebp(saveDialog.FileName);
                        break;
                    default:
                        _currentImage.SaveAsPng(saveDialog.FileName);
                        break;
                }

                UpdateStatus($"Image saved: {Path.GetFileName(saveDialog.FileName)}");
                MessageBox.Show($"Image saved successfully!\n\nSaved to: {saveDialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImageReset_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_originalImagePath))
            {
                LoadImageForEditing(_originalImagePath);
                UpdateStatus("Image reset to original");
            }
        }

        #endregion

        #region Helper Methods

        private void UpdateStatus(string message)
        {
            if (DataContext is MultimediaEditorViewModel vm)
            {
                vm.StatusMessage = message;
            }
        }

        #endregion

        #region Embedding Hosts

        private void VlcHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                // Get the Panel's handle for embedding, or fall back to host handle
                if (host.Child is System.Windows.Forms.Panel panel)
                {
                    viewModel.VlcHostHandle = panel.Handle;
                }
                else
                {
                    viewModel.VlcHostHandle = host.Handle;
                }
            }
        }

        private void AudacityHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                // Get the Panel's handle for embedding, or fall back to host handle
                if (host.Child is System.Windows.Forms.Panel panel)
                {
                    viewModel.AudacityHostHandle = panel.Handle;
                }
                else
                {
                    viewModel.AudacityHostHandle = host.Handle;
                }
            }
        }

        private void GimpHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                // Get the Panel's handle for embedding, or fall back to host handle
                if (host.Child is System.Windows.Forms.Panel panel)
                {
                    viewModel.GimpHostHandle = panel.Handle;
                }
                else
                {
                    viewModel.GimpHostHandle = host.Handle;
                }
            }
        }

        private void ShotcutHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                // Get the Panel's handle for embedding, or fall back to host handle
                if (host.Child is System.Windows.Forms.Panel panel)
                {
                    viewModel.ShotcutHostHandle = panel.Handle;
                }
                else
                {
                    viewModel.ShotcutHostHandle = host.Handle;
                }
            }
        }

        private void FreecadHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is MultimediaEditorViewModel viewModel && sender is System.Windows.Forms.Integration.WindowsFormsHost host)
            {
                // Get the Panel's handle for embedding, or fall back to host handle
                if (host.Child is System.Windows.Forms.Panel panel)
                {
                    viewModel.FreecadHostHandle = panel.Handle;
                }
                else
                {
                    viewModel.FreecadHostHandle = host.Handle;
                }
            }
        }

        #endregion
    }
}
