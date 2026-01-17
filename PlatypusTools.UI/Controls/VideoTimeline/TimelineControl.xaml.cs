using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.Core.Models.Video;

using IOPath = System.IO.Path;

namespace PlatypusTools.UI.Controls.VideoTimeline
{
    /// <summary>
    /// Enum for clip trim edge being dragged.
    /// </summary>
    public enum TrimEdge
    {
        None,
        Left,
        Right
    }

    /// <summary>
    /// Timeline control for video editing with multi-track support.
    /// </summary>
    public partial class TimelineControl : UserControl
    {
        private bool _isDraggingPlayhead;
        private bool _isDraggingClip;
        private bool _isTrimmingClip;
        private TrimEdge _trimEdge = TrimEdge.None;
        private Point _dragStart;
        private TimeSpan _clipOriginalStart;
        private TimeSpan _clipOriginalDuration;
        private TimelineClip? _draggedClip;
        private ContentPresenter? _draggedClipContainer;
        private FrameworkElement? _draggedClipVisual;
        private double _pixelsPerSecond = 50; // Default zoom level
        
        public TimelineControl()
        {
            InitializeComponent();
            Loaded += TimelineControl_Loaded;
            SizeChanged += TimelineControl_SizeChanged;
            
            // Enable file drop on the entire control
            AllowDrop = true;
            Drop += TimelineControl_Drop;
            DragOver += TimelineControl_DragOver;
        }
        
        private void TimelineControl_Loaded(object sender, RoutedEventArgs e)
        {
            DrawTimeRuler();
            SyncScrollViewers();
            
            // Subscribe to zoom changes
            if (DataContext is TimelineViewModel vm)
            {
                _pixelsPerSecond = vm.PixelsPerSecond;
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(TimelineViewModel.ZoomLevel) ||
                        args.PropertyName == nameof(TimelineViewModel.PixelsPerSecond))
                    {
                        _pixelsPerSecond = vm.PixelsPerSecond;
                        DrawTimeRuler();
                        UpdatePlayheadPosition();
                    }
                    else if (args.PropertyName == nameof(TimelineViewModel.PlayheadPosition))
                    {
                        UpdatePlayheadPosition();
                    }
                };
            }
        }
        
        private void TimelineControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawTimeRuler();
        }
        
        private void SyncScrollViewers()
        {
            // Sync vertical scroll between track headers and content
            // Also sync horizontal scroll with ruler
            TracksScroll.ScrollChanged += (s, e) =>
            {
                if (e.VerticalChange != 0)
                {
                    TrackHeadersScroll.ScrollToVerticalOffset(TracksScroll.VerticalOffset);
                }
                if (e.HorizontalChange != 0)
                {
                    // Move the ruler canvas to sync with horizontal scroll
                    Canvas.SetLeft(TimeRuler, -TracksScroll.HorizontalOffset);
                }
            };
        }
        
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            
            // Ctrl + Scroll = Zoom
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var vm = DataContext as TimelineViewModel;
                if (vm != null)
                {
                    if (e.Delta > 0)
                        vm.ZoomInCommand.Execute(null);
                    else
                        vm.ZoomOutCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
        
        private void DrawTimeRuler()
        {
            if (TimeRuler == null) return;
            
            TimeRuler.Children.Clear();
            
            var vm = DataContext as TimelineViewModel;
            var totalDuration = vm?.Duration ?? TimeSpan.FromMinutes(10);
            var rulerWidth = totalDuration.TotalSeconds * _pixelsPerSecond;
            
            // Set ruler canvas width to match timeline duration
            TimeRuler.Width = rulerWidth;
            
            // Calculate tick intervals based on zoom
            double majorInterval = CalculateMajorInterval();
            double minorInterval = majorInterval / 4;
            
            for (double seconds = 0; seconds <= totalDuration.TotalSeconds; seconds += minorInterval)
            {
                double x = seconds * _pixelsPerSecond;
                bool isMajor = Math.Abs(seconds % majorInterval) < 0.01;
                
                var line = new Line
                {
                    X1 = x,
                    Y1 = isMajor ? 0 : 15,
                    X2 = x,
                    Y2 = 30,
                    Stroke = new SolidColorBrush(isMajor ? Color.FromRgb(200, 200, 200) : Color.FromRgb(100, 100, 100)),
                    StrokeThickness = isMajor ? 1 : 0.5
                };
                TimeRuler.Children.Add(line);
                
                // Add time label for major ticks
                if (isMajor)
                {
                    var ts = TimeSpan.FromSeconds(seconds);
                    string format = ts.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss";
                    var text = new TextBlock
                    {
                        Text = ts.ToString(format),
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                        FontSize = 10
                    };
                    Canvas.SetLeft(text, x + 2);
                    Canvas.SetTop(text, 2);
                    TimeRuler.Children.Add(text);
                }
            }
        }
        
        private double CalculateMajorInterval()
        {
            // Adjust major tick interval based on zoom level
            if (_pixelsPerSecond > 200) return 1;    // Every second
            if (_pixelsPerSecond > 100) return 5;    // Every 5 seconds
            if (_pixelsPerSecond > 50) return 10;    // Every 10 seconds
            if (_pixelsPerSecond > 25) return 30;    // Every 30 seconds
            return 60; // Every minute
        }
        
        private void UpdatePlayheadPosition()
        {
            if (Playhead == null) return;
            
            var vm = DataContext as TimelineViewModel;
            if (vm == null) return;
            
            double x = vm.PlayheadPosition.TotalSeconds * _pixelsPerSecond;
            Canvas.SetLeft(Playhead, x);
        }
        
        /// <summary>
        /// Detects if the click is on a trim handle based on element name.
        /// </summary>
        private TrimEdge DetectTrimHandle(FrameworkElement? element)
        {
            while (element != null)
            {
                if (element.Name == "LeftTrimHandle")
                    return TrimEdge.Left;
                if (element.Name == "RightTrimHandle")
                    return TrimEdge.Right;
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }
            return TrimEdge.None;
        }
        
        /// <summary>
        /// Finds the visual element (Grid) that represents the clip.
        /// </summary>
        private FrameworkElement? FindClipVisual(FrameworkElement? element)
        {
            while (element != null)
            {
                if (element is Grid grid && grid.Tag is TimelineClip)
                    return grid;
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }
            return null;
        }
        
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            
            var pos = e.GetPosition(TracksScroll);
            var vm = DataContext as TimelineViewModel;
            
            if (vm == null) return;
            
            // Check if clicking on a clip (via visual tree)
            var hitElement = e.OriginalSource as FrameworkElement;
            var (clickedClip, container) = FindClipFromElement(hitElement);
            
            if (clickedClip != null && container != null)
            {
                vm.SelectedClip = clickedClip;
                _draggedClip = clickedClip;
                _draggedClipContainer = container;
                _draggedClipVisual = FindClipVisual(hitElement);
                _dragStart = pos;
                _clipOriginalStart = clickedClip.StartPosition;
                _clipOriginalDuration = clickedClip.Duration;
                
                // Check if clicking on a trim handle
                var trimEdge = DetectTrimHandle(hitElement);
                if (trimEdge != TrimEdge.None)
                {
                    // Start trimming
                    _isTrimmingClip = true;
                    _trimEdge = trimEdge;
                }
                else
                {
                    // Start dragging
                    _isDraggingClip = true;
                }
                
                CaptureMouse();
                e.Handled = true;
            }
            else if (e.OriginalSource is Canvas)
            {
                // Clicked on empty track area - move playhead
                var seconds = (pos.X + TracksScroll.HorizontalOffset) / _pixelsPerSecond;
                vm.PlayheadPosition = TimeSpan.FromSeconds(Math.Max(0, seconds));
                _isDraggingPlayhead = true;
                CaptureMouse();
            }
        }
        
        /// <summary>
        /// Finds the TimelineClip associated with a visual element by walking up the DataContext chain.
        /// Also returns the ContentPresenter container for direct Canvas manipulation.
        /// </summary>
        private (TimelineClip? clip, ContentPresenter? container) FindClipFromElement(FrameworkElement? element)
        {
            ContentPresenter? container = null;
            while (element != null)
            {
                if (element is ContentPresenter cp && cp.Content is TimelineClip)
                    container = cp;
                    
                if (element.DataContext is TimelineClip clip && container != null)
                    return (clip, container);
                    
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }
            return (null, null);
        }
        
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            
            if (e.LeftButton != MouseButtonState.Pressed) return;
            
            var pos = e.GetPosition(TracksScroll);
            var vm = DataContext as TimelineViewModel;
            if (vm == null) return;
            
            if (_isDraggingPlayhead)
            {
                var seconds = (pos.X + TracksScroll.HorizontalOffset) / _pixelsPerSecond;
                vm.PlayheadPosition = TimeSpan.FromSeconds(Math.Max(0, seconds));
            }
            else if (_isDraggingClip && _draggedClip != null && _draggedClipContainer != null)
            {
                // Calculate drag delta and update clip position
                double deltaX = pos.X - _dragStart.X;
                double deltaSeconds = deltaX / _pixelsPerSecond;
                var newStart = _clipOriginalStart + TimeSpan.FromSeconds(deltaSeconds);
                
                // Clamp to non-negative
                if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
                
                // Snap to grid if enabled
                if (vm.SnapToGrid)
                {
                    newStart = SnapTime(newStart, vm.GridInterval);
                }
                
                // Update the model
                _draggedClip.StartPosition = newStart;
                
                // Directly update the visual position for immediate feedback
                double newX = newStart.TotalSeconds * _pixelsPerSecond;
                Canvas.SetLeft(_draggedClipContainer, newX);
            }
            else if (_isTrimmingClip && _draggedClip != null && _draggedClipContainer != null && _draggedClipVisual != null)
            {
                // Calculate drag delta
                double deltaX = pos.X - _dragStart.X;
                double deltaSeconds = deltaX / _pixelsPerSecond;
                
                const double MinDuration = 0.1; // Minimum clip duration in seconds
                
                if (_trimEdge == TrimEdge.Left)
                {
                    // Trimming left edge: adjust start position and reduce duration
                    var newStart = _clipOriginalStart + TimeSpan.FromSeconds(deltaSeconds);
                    var newDuration = _clipOriginalDuration - TimeSpan.FromSeconds(deltaSeconds);
                    
                    // Clamp to valid ranges
                    if (newStart < TimeSpan.Zero)
                    {
                        newDuration -= (TimeSpan.Zero - newStart);
                        newStart = TimeSpan.Zero;
                    }
                    if (newDuration.TotalSeconds < MinDuration)
                    {
                        newStart = _clipOriginalStart + _clipOriginalDuration - TimeSpan.FromSeconds(MinDuration);
                        newDuration = TimeSpan.FromSeconds(MinDuration);
                    }
                    
                    // Snap if enabled
                    if (vm.SnapToGrid)
                    {
                        newStart = SnapTime(newStart, vm.GridInterval);
                        newDuration = _clipOriginalStart + _clipOriginalDuration - newStart;
                        if (newDuration.TotalSeconds < MinDuration)
                            newDuration = TimeSpan.FromSeconds(MinDuration);
                    }
                    
                    _draggedClip.StartPosition = newStart;
                    _draggedClip.Duration = newDuration;
                    
                    // Update visuals
                    Canvas.SetLeft(_draggedClipContainer, newStart.TotalSeconds * _pixelsPerSecond);
                    _draggedClipVisual.Width = Math.Max(newDuration.TotalSeconds * _pixelsPerSecond, 10);
                }
                else if (_trimEdge == TrimEdge.Right)
                {
                    // Trimming right edge: only adjust duration
                    var newDuration = _clipOriginalDuration + TimeSpan.FromSeconds(deltaSeconds);
                    
                    // Clamp to minimum
                    if (newDuration.TotalSeconds < MinDuration)
                        newDuration = TimeSpan.FromSeconds(MinDuration);
                    
                    // Snap if enabled
                    if (vm.SnapToGrid)
                    {
                        var newEnd = _clipOriginalStart + newDuration;
                        newEnd = SnapTime(newEnd, vm.GridInterval);
                        newDuration = newEnd - _clipOriginalStart;
                        if (newDuration.TotalSeconds < MinDuration)
                            newDuration = TimeSpan.FromSeconds(MinDuration);
                    }
                    
                    _draggedClip.Duration = newDuration;
                    
                    // Update visual width
                    _draggedClipVisual.Width = Math.Max(newDuration.TotalSeconds * _pixelsPerSecond, 10);
                }
            }
        }
        
        /// <summary>
        /// Snaps a time value to the nearest grid interval.
        /// </summary>
        private TimeSpan SnapTime(TimeSpan time, TimeSpan interval)
        {
            double ticks = Math.Round(time.Ticks / (double)interval.Ticks) * interval.Ticks;
            return TimeSpan.FromTicks((long)ticks);
        }
        
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            
            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                ReleaseMouseCapture();
            }
            else if (_isDraggingClip || _isTrimmingClip)
            {
                _isDraggingClip = false;
                _isTrimmingClip = false;
                _trimEdge = TrimEdge.None;
                _draggedClip = null;
                _draggedClipContainer = null;
                _draggedClipVisual = null;
                ReleaseMouseCapture();
            }
        }
        
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            var vm = DataContext as TimelineViewModel;
            if (vm == null) return;
            
            switch (e.Key)
            {
                case Key.Space:
                    if (vm.IsPlaying)
                        vm.PauseCommand.Execute(null);
                    else
                        vm.PlayCommand.Execute(null);
                    e.Handled = true;
                    break;
                    
                case Key.S:
                    vm.SplitClipCommand.Execute(null);
                    e.Handled = true;
                    break;
                    
                case Key.Delete:
                    vm.DeleteClipCommand.Execute(null);
                    e.Handled = true;
                    break;
                    
                case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                    vm.UndoCommand.Execute(null);
                    e.Handled = true;
                    break;
                    
                case Key.Y when Keyboard.Modifiers == ModifierKeys.Control:
                    vm.RedoCommand.Execute(null);
                    e.Handled = true;
                    break;
                    
                case Key.Home:
                    vm.PlayheadPosition = TimeSpan.Zero;
                    e.Handled = true;
                    break;
                    
                case Key.End:
                    vm.PlayheadPosition = vm.Duration;
                    e.Handled = true;
                    break;
                    
                case Key.Left:
                    vm.PlayheadPosition = TimeSpan.FromSeconds(
                        Math.Max(0, vm.PlayheadPosition.TotalSeconds - (Keyboard.Modifiers == ModifierKeys.Shift ? 5 : 1)));
                    e.Handled = true;
                    break;
                    
                case Key.Right:
                    vm.PlayheadPosition = TimeSpan.FromSeconds(
                        Math.Min(vm.Duration.TotalSeconds, vm.PlayheadPosition.TotalSeconds + (Keyboard.Modifiers == ModifierKeys.Shift ? 5 : 1)));
                    e.Handled = true;
                    break;
            }
        }
        
        /// <summary>
        /// Converts time to pixel position on the timeline.
        /// </summary>
        public double TimeToPixels(TimeSpan time)
        {
            return time.TotalSeconds * _pixelsPerSecond;
        }
        
        /// <summary>
        /// Converts pixel position to time on the timeline.
        /// </summary>
        public TimeSpan PixelsToTime(double pixels)
        {
            return TimeSpan.FromSeconds(pixels / _pixelsPerSecond);
        }
        
        /// <summary>
        /// Scrolls the timeline to ensure the playhead is visible.
        /// </summary>
        public void EnsurePlayheadVisible()
        {
            var vm = DataContext as TimelineViewModel;
            if (vm == null) return;
            
            double playheadX = TimeToPixels(vm.PlayheadPosition);
            double viewLeft = TracksScroll.HorizontalOffset;
            double viewRight = viewLeft + TracksScroll.ViewportWidth;
            
            if (playheadX < viewLeft)
            {
                TracksScroll.ScrollToHorizontalOffset(playheadX - 50);
            }
            else if (playheadX > viewRight)
            {
                TracksScroll.ScrollToHorizontalOffset(playheadX - TracksScroll.ViewportWidth + 50);
            }
        }
        
        #region Drag and Drop
        
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv" };
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".aac", ".flac", ".ogg", ".wma", ".m4a" };
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff" };
        
        private void TimelineControl_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Any(f => IsMediaFile(f)))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
        
        private void TimelineControl_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
                return;
            
            var vm = DataContext as TimelineViewModel;
            if (vm == null) return;
            
            // Get drop position relative to tracks scroll
            var dropPos = e.GetPosition(TracksScroll);
            var dropTimeSeconds = (dropPos.X + TracksScroll.HorizontalOffset) / _pixelsPerSecond;
            var dropTime = TimeSpan.FromSeconds(Math.Max(0, dropTimeSeconds));
            
            // Process each dropped file
            foreach (var file in files.Where(f => IsMediaFile(f)))
            {
                AddMediaToTimeline(vm, file, dropTime);
                // Offset next clip by 5 seconds to avoid overlap
                dropTime = dropTime.Add(TimeSpan.FromSeconds(5));
            }
        }
        
        private void AddMediaToTimeline(TimelineViewModel vm, string filePath, TimeSpan position)
        {
            var ext = IOPath.GetExtension(filePath).ToLowerInvariant();
            TimelineTrack? targetTrack = null;
            TimeSpan duration = TimeSpan.FromSeconds(10); // Default duration
            
            if (VideoExtensions.Contains(ext))
            {
                // Add to first video track, or create one
                targetTrack = vm.Tracks.FirstOrDefault(t => t.Type == TrackType.Video);
                if (targetTrack == null)
                {
                    vm.AddVideoTrackCommand.Execute(null);
                    targetTrack = vm.Tracks.FirstOrDefault(t => t.Type == TrackType.Video);
                }
                
                // Try to get actual duration using FFprobe
                duration = GetMediaDuration(filePath) ?? TimeSpan.FromSeconds(30);
            }
            else if (AudioExtensions.Contains(ext))
            {
                // Add to first audio track, or create one
                targetTrack = vm.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio);
                if (targetTrack == null)
                {
                    vm.AddAudioTrackCommand.Execute(null);
                    targetTrack = vm.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio);
                }
                
                duration = GetMediaDuration(filePath) ?? TimeSpan.FromSeconds(30);
            }
            else if (ImageExtensions.Contains(ext))
            {
                // Add images as title/overlay clips
                targetTrack = vm.Tracks.FirstOrDefault(t => t.Type == TrackType.Title);
                if (targetTrack == null)
                {
                    vm.AddTitleTrackCommand.Execute(null);
                    targetTrack = vm.Tracks.FirstOrDefault(t => t.Type == TrackType.Title);
                }
                
                duration = TimeSpan.FromSeconds(5); // Default image duration
            }
            
            if (targetTrack != null)
            {
                vm.AddClip(targetTrack, filePath, position, duration);
            }
        }
        
        private bool IsMediaFile(string path)
        {
            var ext = IOPath.GetExtension(path).ToLowerInvariant();
            return VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext) || ImageExtensions.Contains(ext);
        }
        
        private TimeSpan? GetMediaDuration(string filePath)
        {
            try
            {
                // Use FFprobe to get actual duration
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v quiet -show_entries format=duration -of csv=p=0 \"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                
                if (double.TryParse(output, System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
            catch
            {
                // FFprobe not available or error - use default
            }
            
            return null;
        }
        
        #endregion
    }
}
