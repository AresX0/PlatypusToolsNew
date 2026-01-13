using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Controls.VideoTimeline
{
    /// <summary>
    /// Timeline control for video editing with multi-track support.
    /// </summary>
    public partial class TimelineControl : UserControl
    {
        private bool _isDraggingPlayhead;
        private bool _isDraggingClip;
        private Point _dragStart;
        private double _pixelsPerSecond = 50; // Default zoom level
        
        public TimelineControl()
        {
            InitializeComponent();
            Loaded += TimelineControl_Loaded;
            SizeChanged += TimelineControl_SizeChanged;
        }
        
        private void TimelineControl_Loaded(object sender, RoutedEventArgs e)
        {
            DrawTimeRuler();
            SyncScrollViewers();
            
            // Subscribe to zoom changes
            if (DataContext is TimelineViewModel vm)
            {
                vm.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(TimelineViewModel.ZoomLevel))
                    {
                        _pixelsPerSecond = 50 * vm.ZoomLevel;
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
            TracksScroll.ScrollChanged += (s, e) =>
            {
                if (e.VerticalChange != 0)
                {
                    TrackHeadersScroll.ScrollToVerticalOffset(TracksScroll.VerticalOffset);
                }
            };
        }
        
        private void DrawTimeRuler()
        {
            if (TimeRuler == null) return;
            
            TimeRuler.Children.Clear();
            
            var vm = DataContext as TimelineViewModel;
            var totalDuration = vm?.Duration ?? TimeSpan.FromMinutes(10);
            var width = Math.Max(TracksScroll?.ActualWidth ?? 800, totalDuration.TotalSeconds * _pixelsPerSecond);
            
            // Calculate tick intervals based on zoom
            double majorInterval = CalculateMajorInterval();
            double minorInterval = majorInterval / 4;
            
            for (double seconds = 0; seconds < totalDuration.TotalSeconds; seconds += minorInterval)
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
                    var text = new TextBlock
                    {
                        Text = ts.ToString(@"mm\:ss"),
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
        
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            
            var pos = e.GetPosition(TracksScroll);
            var vm = DataContext as TimelineViewModel;
            
            if (vm != null && e.OriginalSource is Canvas)
            {
                // Clicked on empty track area - move playhead
                var seconds = (pos.X + TracksScroll.HorizontalOffset) / _pixelsPerSecond;
                vm.PlayheadPosition = TimeSpan.FromSeconds(Math.Max(0, seconds));
                _isDraggingPlayhead = true;
                CaptureMouse();
            }
        }
        
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            
            if (_isDraggingPlayhead && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(TracksScroll);
                var vm = DataContext as TimelineViewModel;
                
                if (vm != null)
                {
                    var seconds = (pos.X + TracksScroll.HorizontalOffset) / _pixelsPerSecond;
                    vm.PlayheadPosition = TimeSpan.FromSeconds(Math.Max(0, seconds));
                }
            }
        }
        
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            
            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
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
    }
}
