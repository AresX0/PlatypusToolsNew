using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PlatypusTools.UI.Models.VideoEditor;

namespace PlatypusTools.UI.Controls.VideoEditor
{
    /// <summary>
    /// Timeline panel control for video editing.
    /// Modeled after Shotcut's TimelineDock.
    /// </summary>
    public partial class TimelinePanel : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty TimelineModelProperty =
            DependencyProperty.Register(nameof(TimelineModel), typeof(TimelineModel), typeof(TimelinePanel),
                new PropertyMetadata(null, OnTimelineModelChanged));

        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(nameof(Position), typeof(TimeSpan), typeof(TimelinePanel),
                new PropertyMetadata(TimeSpan.Zero, OnPositionChanged));

        public static readonly DependencyProperty ZoomProperty =
            DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(TimelinePanel),
                new PropertyMetadata(1.0, OnZoomChanged));

        public TimelineModel? TimelineModel
        {
            get => (TimelineModel?)GetValue(TimelineModelProperty);
            set => SetValue(TimelineModelProperty, value);
        }

        public TimeSpan Position
        {
            get => (TimeSpan)GetValue(PositionProperty);
            set => SetValue(PositionProperty, value);
        }

        public double Zoom
        {
            get => (double)GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        #endregion

        #region Events

        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<TimelineClip>? ClipSelected;
        public event EventHandler<TimelineClip>? ClipDoubleClicked;
        public event EventHandler? SelectionChanged;

        #endregion

        private const double BasePixelsPerSecond = 100;
        private bool _isDraggingPlayhead;
        private bool _isDraggingClip;
        private TimelineClip? _draggedClip;
        private Point _dragStartPoint;
        private TimeSpan _clipDragStartTime;
        private readonly Dictionary<TimelineClip, Border> _clipVisuals = new();

        public TimelinePanel()
        {
            InitializeComponent();
            DataContext = this;
        }

        private static void OnTimelineModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelinePanel panel)
            {
                panel.BindToModel();
                panel.RedrawAllClips();
            }
        }

        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelinePanel panel)
            {
                panel.UpdatePlayheadPosition();
                panel.PositionChanged?.Invoke(panel, panel.Position);
            }
        }

        private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelinePanel panel)
            {
                panel.ZoomSlider.Value = panel.Zoom;
                panel.RedrawAllClips();
            }
        }

        private void BindToModel()
        {
            if (TimelineModel != null)
            {
                TrackHeaders.ItemsSource = TimelineModel.Tracks;
                TracksContainer.ItemsSource = TimelineModel.Tracks;
                TimelineRuler.Duration = TimelineModel.Duration;
                
                // Subscribe to model property changes
                TimelineModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(TimelineModel.Duration))
                    {
                        TimelineRuler.Duration = TimelineModel.Duration;
                        RedrawAllClips();
                    }
                };
            }
        }

        #region Drawing

        private double PixelsPerSecond => BasePixelsPerSecond * Zoom;

        private double TimeToPixels(TimeSpan time) => time.TotalSeconds * PixelsPerSecond;
        private TimeSpan PixelsToTime(double pixels) => TimeSpan.FromSeconds(pixels / PixelsPerSecond);

        private void UpdatePlayheadPosition()
        {
            double x = TimeToPixels(Position);
            Canvas.SetLeft(PlayheadLine, x);
        }

        private void RedrawAllClips()
        {
            _clipVisuals.Clear();

            if (TimelineModel == null)
                return;

            // Update timeline width
            double totalWidth = TimeToPixels(TimelineModel.Duration);
            TimelineGrid.Width = Math.Max(totalWidth, TimelineScroll.ActualWidth);

            // Redraw clips after layout pass
            Dispatcher.InvokeAsync(() =>
            {
                DrawClipsOnTracks();
                UpdatePlayheadPosition();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void DrawClipsOnTracks()
        {
            if (TimelineModel == null)
                return;

            // Find track canvases
            var trackIndex = 0;
            foreach (var track in TimelineModel.Tracks)
            {
                var container = TracksContainer.ItemContainerGenerator.ContainerFromIndex(trackIndex) as ContentPresenter;
                if (container != null)
                {
                    var canvas = FindVisualChild<Canvas>(container);
                    if (canvas != null)
                    {
                        canvas.Children.Clear();
                        canvas.Width = TimeToPixels(TimelineModel.Duration);

                        foreach (var clip in track.Clips)
                        {
                            var clipVisual = CreateClipVisual(clip, track.Type);
                            canvas.Children.Add(clipVisual);
                            _clipVisuals[clip] = clipVisual;

                            // Position clip
                            double x = TimeToPixels(clip.StartTime);
                            double width = TimeToPixels(clip.Duration);
                            Canvas.SetLeft(clipVisual, x);
                            clipVisual.Width = Math.Max(width, 2);
                        }
                    }
                }
                trackIndex++;
            }
        }

        private Border CreateClipVisual(TimelineClip clip, TrackType trackType)
        {
            var baseColor = trackType == TrackType.Video
                ? Color.FromRgb(0x5B, 0x8A, 0x5B)
                : Color.FromRgb(0x5B, 0x5B, 0x8A);

            var border = new Border
            {
                Background = new SolidColorBrush(baseColor),
                BorderBrush = clip.IsSelected
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = clip.IsSelected ? new Thickness(2) : new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Height = 46,
                Margin = new Thickness(0, 2, 0, 2),
                Tag = clip,
                Cursor = Cursors.Hand,
                ToolTip = $"{clip.Name}\n{clip.Duration:hh\\:mm\\:ss\\.ff}",
                ClipToBounds = true
            };

            // Clip content
            var grid = new Grid();
            
            // Thumbnail strip (tile thumbnails across clip width for video clips)
            if (trackType == TrackType.Video && clip.Thumbnail != null)
            {
                var thumbPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Opacity = 0.45,
                    IsHitTestVisible = false
                };
                
                // Calculate how many thumbnails fit across the clip width
                double clipPixelWidth = TimeToPixels(clip.Duration);
                double thumbWidth = 60;
                int thumbCount = Math.Max(1, (int)Math.Ceiling(clipPixelWidth / thumbWidth));
                
                for (int i = 0; i < thumbCount; i++)
                {
                    var thumb = new Image
                    {
                        Source = clip.Thumbnail,
                        Stretch = Stretch.UniformToFill,
                        Width = thumbWidth,
                        Height = 46
                    };
                    thumbPanel.Children.Add(thumb);
                }
                grid.Children.Add(thumbPanel);
            }
            // Audio waveform placeholder for audio clips
            else if (trackType == TrackType.Audio)
            {
                var waveformBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0xFF)),
                    IsHitTestVisible = false
                };
                grid.Children.Add(waveformBorder);
            }

            // Clip name
            var nameLabel = new TextBlock
            {
                Text = clip.Name,
                Foreground = Brushes.White,
                FontSize = 10,
                Margin = new Thickness(4, 2, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Top
            };
            grid.Children.Add(nameLabel);

            // Duration label
            var durationLabel = new TextBlock
            {
                Text = clip.Duration.ToString(@"m\:ss\.ff"),
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize = 9,
                Margin = new Thickness(4, 0, 4, 2),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            grid.Children.Add(durationLabel);

            border.Child = grid;

            // Events
            border.MouseLeftButtonDown += ClipVisual_MouseLeftButtonDown;
            border.MouseLeftButtonUp += ClipVisual_MouseLeftButtonUp;
            border.MouseMove += ClipVisual_MouseMove;

            return border;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        #endregion

        #region Clip Interaction

        private void ClipVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is TimelineClip clip)
            {
                // Select clip
                TimelineModel?.ClearSelection();
                clip.IsSelected = true;
                UpdateClipVisual(clip);
                ClipSelected?.Invoke(this, clip);
                SelectionChanged?.Invoke(this, EventArgs.Empty);

                // Start drag
                _isDraggingClip = true;
                _draggedClip = clip;
                _dragStartPoint = e.GetPosition(TimelineGrid);
                _clipDragStartTime = clip.StartTime;
                border.CaptureMouse();

                e.Handled = true;
            }
        }

        private void ClipVisual_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                border.ReleaseMouseCapture();
                _isDraggingClip = false;
                _draggedClip = null;

                // Check for double-click
                if (e.ClickCount == 2 && border.Tag is TimelineClip clip)
                {
                    ClipDoubleClicked?.Invoke(this, clip);
                }
            }
        }

        private void ClipVisual_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingClip && _draggedClip != null && sender is Border border)
            {
                var currentPos = e.GetPosition(TimelineGrid);
                var deltaX = currentPos.X - _dragStartPoint.X;
                var deltaTime = PixelsToTime(deltaX);

                var newStartTime = _clipDragStartTime + deltaTime;
                if (newStartTime < TimeSpan.Zero)
                    newStartTime = TimeSpan.Zero;

                _draggedClip.StartTime = newStartTime;

                // Update visual position
                Canvas.SetLeft(border, TimeToPixels(newStartTime));

                // Update timeline duration if needed
                TimelineModel?.RecalculateDuration();
            }
        }

        private void UpdateClipVisual(TimelineClip clip)
        {
            if (_clipVisuals.TryGetValue(clip, out var border))
            {
                border.BorderBrush = clip.IsSelected
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                border.BorderThickness = clip.IsSelected ? new Thickness(2) : new Thickness(1);
            }
        }

        #endregion

        #region Timeline Interaction

        private void TimelineGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(TimelineGrid);

            // Click on ruler to seek
            if (point.Y < 30)
            {
                _isDraggingPlayhead = true;
                TimelineGrid.CaptureMouse();
                SeekToPosition(point.X);
            }
            else
            {
                // Click on empty area - deselect
                TimelineModel?.ClearSelection();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void TimelineGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingPlayhead)
            {
                SeekToPosition(e.GetPosition(TimelineGrid).X);
            }
        }

        private void TimelineGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                TimelineGrid.ReleaseMouseCapture();
            }
        }

        private void SeekToPosition(double x)
        {
            var time = PixelsToTime(x);
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            if (TimelineModel != null && time > TimelineModel.Duration)
                time = TimelineModel.Duration;
            Position = time;
            if (TimelineModel != null)
                TimelineModel.Position = time;
        }

        #endregion

        #region Drag and Drop

        private void TimelineGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(PlaylistItem)) || 
                e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void TimelineGrid_Drop(object sender, DragEventArgs e)
        {
            var position = e.GetPosition(TimelineGrid);
            var dropTime = PixelsToTime(position.X);

            // Determine which track
            int trackIndex = (int)((position.Y - 30) / 50);
            if (trackIndex < 0) trackIndex = 0;
            if (TimelineModel != null && trackIndex >= TimelineModel.Tracks.Count)
                trackIndex = TimelineModel.Tracks.Count - 1;

            if (e.Data.GetData(typeof(PlaylistItem)) is PlaylistItem item)
            {
                AddClipToTimeline(item, trackIndex, dropTime);
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                // Create a quick playlist item
                var newItem = new PlaylistItem
                {
                    Name = System.IO.Path.GetFileName(files[0]),
                    FilePath = files[0],
                    Duration = TimeSpan.FromSeconds(10) // Will be updated when probed
                };
                AddClipToTimeline(newItem, trackIndex, dropTime);
            }
        }

        private void AddClipToTimeline(PlaylistItem item, int trackIndex, TimeSpan startTime)
        {
            if (TimelineModel == null || trackIndex < 0 || trackIndex >= TimelineModel.Tracks.Count)
                return;

            var track = TimelineModel.Tracks[trackIndex];
            
            var clip = new TimelineClip
            {
                Name = item.Name,
                SourcePath = item.FilePath,
                StartTime = startTime,
                Duration = item.Duration > TimeSpan.Zero ? item.Duration : TimeSpan.FromSeconds(5),
                InPoint = item.InPoint,
                OutPoint = item.OutPoint > TimeSpan.Zero ? item.OutPoint : item.Duration,
                SourceDuration = item.Duration,
                Thumbnail = item.Thumbnail
            };

            track.Clips.Add(clip);
            TimelineModel.RecalculateDuration();
            RedrawAllClips();
        }

        #endregion

        #region Toolbar Actions

        private void CutButton_Click(object sender, RoutedEventArgs e)
        {
            SplitAtPlayhead();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedClips();
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            Zoom = Math.Min(Zoom * 1.5, 10);
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            Zoom = Math.Max(Zoom / 1.5, 0.1);
        }

        private void ZoomFitButton_Click(object sender, RoutedEventArgs e)
        {
            if (TimelineModel != null && TimelineModel.Duration.TotalSeconds > 0)
            {
                Zoom = TimelineScroll.ActualWidth / (TimelineModel.Duration.TotalSeconds * BasePixelsPerSecond);
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Math.Abs(Zoom - e.NewValue) > 0.01)
            {
                Zoom = e.NewValue;
            }
        }

        #endregion

        #region Editing Operations

        public void SplitAtPlayhead()
        {
            if (TimelineModel == null) return;

            var clipAtPosition = TimelineModel.GetClipAtPosition(Position);
            if (clipAtPosition != null)
            {
                // Find the track
                foreach (var track in TimelineModel.Tracks)
                {
                    if (track.Clips.Contains(clipAtPosition))
                    {
                        SplitClip(track, clipAtPosition, Position);
                        break;
                    }
                }
            }
        }

        private void SplitAtPlayhead_Click(object sender, RoutedEventArgs e)
        {
            SplitAtPlayhead();
        }

        private void InsertClip_Click(object sender, RoutedEventArgs e)
        {
            // Raise an event to let the parent handle file selection
            OnInsertClipRequested();
        }

        private void AppendToTrack_Click(object sender, RoutedEventArgs e)
        {
            OnAppendToTrackRequested();
        }

        private void RemoveClip_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedClips();
        }

        private void LiftClip_Click(object sender, RoutedEventArgs e)
        {
            LiftSelectedClips();
        }

        private void AddVideoTrack_Click(object sender, RoutedEventArgs e)
        {
            if (TimelineModel != null)
            {
                var trackNumber = TimelineModel.Tracks.Count(t => t.Type == TrackType.Video) + 1;
                TimelineModel.Tracks.Add(new TimelineTrack
                {
                    Name = $"V{trackNumber}",
                    Type = TrackType.Video
                });
                RedrawAllClips();
            }
        }

        private void AddAudioTrack_Click(object sender, RoutedEventArgs e)
        {
            if (TimelineModel != null)
            {
                var trackNumber = TimelineModel.Tracks.Count(t => t.Type == TrackType.Audio) + 1;
                TimelineModel.Tracks.Add(new TimelineTrack
                {
                    Name = $"A{trackNumber}",
                    Type = TrackType.Audio
                });
                RedrawAllClips();
            }
        }

        private void TimelineGrid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Select clip at position before showing context menu
            var point = e.GetPosition(TimelineGrid);
            if (point.Y >= 30) // Below ruler
            {
                var clickTime = PixelsToTime(point.X);
                int trackIndex = (int)((point.Y - 30) / 50);
                
                if (TimelineModel != null && trackIndex >= 0 && trackIndex < TimelineModel.Tracks.Count)
                {
                    var track = TimelineModel.Tracks[trackIndex];
                    var clipAtPoint = track.Clips.FirstOrDefault(c => 
                        clickTime >= c.StartTime && clickTime < c.EndTime);
                    
                    if (clipAtPoint != null)
                    {
                        TimelineModel.ClearSelection();
                        clipAtPoint.IsSelected = true;
                        UpdateClipVisual(clipAtPoint);
                        ClipSelected?.Invoke(this, clipAtPoint);
                    }
                }
            }
        }

        // Events for external handling
        public event EventHandler? InsertClipRequested;
        public event EventHandler? AppendToTrackRequested;

        protected virtual void OnInsertClipRequested()
        {
            InsertClipRequested?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnAppendToTrackRequested()
        {
            AppendToTrackRequested?.Invoke(this, EventArgs.Empty);
        }

        public void LiftSelectedClips()
        {
            // Lift removes clip but leaves gap (unlike Ripple Delete)
            RemoveSelectedClips();
        }

        private void SplitClip(TimelineTrack track, TimelineClip clip, TimeSpan splitPoint)
        {
            if (splitPoint <= clip.StartTime || splitPoint >= clip.EndTime)
                return;

            var splitOffset = splitPoint - clip.StartTime;

            // Create second half
            var secondHalf = new TimelineClip
            {
                Name = clip.Name,
                SourcePath = clip.SourcePath,
                StartTime = splitPoint,
                Duration = clip.Duration - splitOffset,
                InPoint = clip.InPoint + splitOffset,
                OutPoint = clip.OutPoint,
                SourceDuration = clip.SourceDuration,
                Thumbnail = clip.Thumbnail
            };

            // Modify first half
            clip.Duration = splitOffset;
            clip.OutPoint = clip.InPoint + splitOffset;

            // Add second half after first
            var index = track.Clips.IndexOf(clip);
            track.Clips.Insert(index + 1, secondHalf);

            RedrawAllClips();
        }

        public void RemoveSelectedClips()
        {
            if (TimelineModel == null) return;

            var clipsToRemove = new List<(TimelineTrack track, TimelineClip clip)>();
            
            foreach (var track in TimelineModel.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.IsSelected)
                    {
                        clipsToRemove.Add((track, clip));
                    }
                }
            }

            foreach (var (track, clip) in clipsToRemove)
            {
                track.Clips.Remove(clip);
                _clipVisuals.Remove(clip);
            }

            TimelineModel.RecalculateDuration();
            RedrawAllClips();
        }

        #endregion

        #region Scroll Sync

        private void TimelineScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Sync track header scroll with timeline scroll
            TrackHeaderScroll.ScrollToVerticalOffset(e.VerticalOffset);
        }

        #endregion
    }
}
