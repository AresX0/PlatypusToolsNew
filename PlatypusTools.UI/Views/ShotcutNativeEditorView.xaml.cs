using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PlatypusTools.UI.Models.VideoEditor;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Native Shotcut-style video editor view.
    /// Combines playlist, player, properties, and timeline panels.
    /// </summary>
    public partial class ShotcutNativeEditorView : UserControl
    {
        public ObservableCollection<PlaylistItem> PlaylistItems { get; } = new();
        public TimelineModel TimelineModel { get; } = new();

        private PlaylistItem? _selectedPlaylistItem;
        private TimelineClip? _selectedTimelineClip;
        private Point _dragStartPoint;
        private bool _isDragging;

        public ShotcutNativeEditorView()
        {
            InitializeComponent();
            DataContext = this;
            PlaylistBox.ItemsSource = PlaylistItems;
            Timeline.TimelineModel = TimelineModel;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize with sample timeline duration
            TimelineModel.Duration = TimeSpan.FromMinutes(5);
        }

        #region File Operations

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Media File",
                Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm|" +
                         "Audio Files|*.mp3;*.wav;*.aac;*.flac;*.ogg|" +
                         "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                         "All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    AddFileToPlaylist(file);
                }
            }
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Project",
                Filter = "PlatypusTools Project|*.ptproj|MLT XML|*.mlt",
                DefaultExt = ".ptproj"
            };

            if (dialog.ShowDialog() == true)
            {
                // TODO: Implement project save
                MessageBox.Show($"Project saved to:\n{dialog.FileName}", "Saved",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Video",
                Filter = "MP4 Video|*.mp4|MKV Video|*.mkv|WebM Video|*.webm",
                DefaultExt = ".mp4"
            };

            if (dialog.ShowDialog() == true)
            {
                // TODO: Implement export via FFmpeg
                MessageBox.Show($"Export to:\n{dialog.FileName}\n\n(Export feature coming soon)",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Playlist

        private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            OpenFile_Click(sender, e);
        }

        private void RemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylistItem != null)
            {
                PlaylistItems.Remove(_selectedPlaylistItem);
                _selectedPlaylistItem = null;
            }
        }

        private void AddFileToPlaylist(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var mediaType = GetMediaType(extension);

            var item = new PlaylistItem
            {
                Name = Path.GetFileName(filePath),
                FilePath = filePath,
                Type = mediaType,
                FileSize = new FileInfo(filePath).Length
            };

            // Try to get duration using FFprobe (if available)
            // For now, set a default duration
            item.Duration = TimeSpan.FromSeconds(10);

            PlaylistItems.Add(item);
        }

        private MediaType GetMediaType(string extension)
        {
            return extension switch
            {
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".webm" => MediaType.Video,
                ".mp3" or ".wav" or ".aac" or ".flac" or ".ogg" => MediaType.Audio,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => MediaType.Image,
                _ => MediaType.Video
            };
        }

        private void PlaylistBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistBox.SelectedItem is PlaylistItem item)
            {
                _selectedPlaylistItem = item;
                UpdatePropertiesPanel(item);
            }
        }

        private void PlaylistBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedPlaylistItem != null)
            {
                // Load into player
                VideoPlayer.LoadMedia(_selectedPlaylistItem.FilePath);
            }
        }

        private void PlaylistBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    AddFileToPlaylist(file);
                }
            }
        }

        private void PlaylistBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void PlaylistBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                return;
            }

            var currentPos = e.GetPosition(null);
            var diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (_selectedPlaylistItem != null && !_isDragging)
                {
                    _isDragging = true;
                    var data = new DataObject(typeof(PlaylistItem), _selectedPlaylistItem);
                    DragDrop.DoDragDrop(PlaylistBox, data, DragDropEffects.Copy);
                    _isDragging = false;
                }
            }
        }

        #endregion

        #region Timeline

        private void Timeline_ClipSelected(object? sender, TimelineClip e)
        {
            _selectedTimelineClip = e;
            UpdatePropertiesPanel(e);
            
            // Load clip into player
            if (!string.IsNullOrEmpty(e.SourcePath))
            {
                VideoPlayer.LoadMedia(e.SourcePath);
                VideoPlayer.InPoint = e.InPoint;
                VideoPlayer.OutPoint = e.OutPoint;
            }
        }

        private void Timeline_PositionChanged(object? sender, TimeSpan e)
        {
            VideoPlayer.Position = e;
        }

        #endregion

        #region Properties Panel

        private void UpdatePropertiesPanel(PlaylistItem item)
        {
            PropertiesPanel.Children.Clear();
            
            AddPropertyRow("Name", item.Name);
            AddPropertyRow("Type", item.Type.ToString());
            AddPropertyRow("Duration", item.DurationText);
            AddPropertyRow("Resolution", item.Resolution);
            AddPropertyRow("File Size", item.FileSizeText);
            AddPropertyRow("Path", item.FilePath);
        }

        private void UpdatePropertiesPanel(TimelineClip clip)
        {
            PropertiesPanel.Children.Clear();
            
            AddPropertyRow("Clip Name", clip.Name);
            AddPropertyRow("Start Time", clip.StartTime.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("Duration", clip.Duration.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("In Point", clip.InPoint.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("Out Point", clip.OutPoint.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("Gain", $"{clip.Gain:F2}");
            AddPropertyRow("Source", clip.SourcePath);
            
            // Add edit controls
            AddPropertySlider("Gain", clip.Gain, 0, 2, value => clip.Gain = value);
        }

        private void AddPropertyRow(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label + ":",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = value
            };
            Grid.SetColumn(valueBlock, 1);

            row.Children.Add(labelBlock);
            row.Children.Add(valueBlock);
            PropertiesPanel.Children.Add(row);
        }

        private void AddPropertySlider(string label, double value, double min, double max, Action<double> onChanged)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11
            };

            var valueBlock = new TextBlock
            {
                Text = value.ToString("F2"),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11
            };
            Grid.SetColumn(valueBlock, 1);

            header.Children.Add(labelBlock);
            header.Children.Add(valueBlock);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Margin = new Thickness(0, 4, 0, 0)
            };
            slider.ValueChanged += (s, e) =>
            {
                valueBlock.Text = e.NewValue.ToString("F2");
                onChanged(e.NewValue);
            };

            row.Children.Add(header);
            row.Children.Add(slider);
            PropertiesPanel.Children.Add(row);
        }

        #endregion
    }
}
