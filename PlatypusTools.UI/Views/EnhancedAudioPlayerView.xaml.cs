using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.UI.Services;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

/// <summary>
/// Interaction logic for EnhancedAudioPlayerView.xaml
/// Implements drag-drop queue reordering, speed button handling,
/// visualizer integration, and fullscreen support.
/// </summary>
public partial class EnhancedAudioPlayerView : UserControl
{
    private Point _dragStartPoint;
    private int _dragStartIndex = -1;
    private bool _isVisualizerFullscreen;
    private Window? _fullscreenWindow;
    private DispatcherTimer? _osdHideTimer;
    private Border? _fullscreenOsd;
    
    public EnhancedAudioPlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Subscribe to Enhanced Audio Player Service spectrum data
        EnhancedAudioPlayerService.Instance.SpectrumDataUpdated += OnSpectrumDataUpdated;
        EnhancedAudioPlayerService.Instance.TrackChanged += OnTrackChanged;
        System.Diagnostics.Debug.WriteLine("EnhancedAudioPlayerView: Subscribed to EnhancedAudioPlayerService spectrum data and track changes");
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from spectrum data
        EnhancedAudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumDataUpdated;
        EnhancedAudioPlayerService.Instance.TrackChanged -= OnTrackChanged;
        _osdHideTimer?.Stop();
    }
    
    private void OnTrackChanged(object? sender, AudioTrack? track)
    {
        // Reset the visualizer when track changes
        Dispatcher.BeginInvoke(() =>
        {
            VisualizerControl?.Reset();
            System.Diagnostics.Debug.WriteLine($"EnhancedAudioPlayerView: Track changed, visualizer reset. Track: {track?.Title ?? "null"}");
        });
    }
    
    private void OnSpectrumDataUpdated(object? sender, float[] data)
    {
        // Forward spectrum data to the visualizer control
        Dispatcher.BeginInvoke(() =>
        {
            if (VisualizerControl != null && data != null && data.Length > 0)
            {
                // Convert float[] to double[]
                var doubleData = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                    doubleData[i] = data[i];
                
                // Get mode and settings from ViewModel
                var vm = DataContext as EnhancedAudioPlayerViewModel;
                string mode = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0);
                int barCount = vm?.BarCount ?? 32;
                int colorIndex = vm?.ColorSchemeIndex ?? 0;
                
                VisualizerControl.SetColorScheme(colorIndex);
                VisualizerControl.UpdateSpectrumData(doubleData, mode, barCount);
            }
        });
    }
    
    private static string GetVisualizerModeName(int index) => index switch
    {
        0 => "Bars",
        1 => "Mirror",
        2 => "Waveform",
        3 => "Circular",
        4 => "Radial",
        5 => "Particles",
        6 => "Aurora",
        7 => "WaveGrid",
        _ => "Bars"
    };
    
    /// <summary>
    /// Handle speed preset button clicks (AP-002)
    /// </summary>
    private void SpeedButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string speedStr)
        {
            if (double.TryParse(speedStr, out double speed) && DataContext is EnhancedAudioPlayerViewModel vm)
            {
                vm.PlaybackSpeed = speed;
            }
        }
    }
    
    /// <summary>
    /// Scroll queue up
    /// </summary>
    private void QueueScrollUp_Click(object sender, RoutedEventArgs e)
    {
        if (QueueListBox != null)
        {
            var scrollViewer = GetScrollViewer(QueueListBox);
            scrollViewer?.ScrollToVerticalOffset(scrollViewer.VerticalOffset - 100);
        }
    }
    
    /// <summary>
    /// Scroll queue down
    /// </summary>
    private void QueueScrollDown_Click(object sender, RoutedEventArgs e)
    {
        if (QueueListBox != null)
        {
            var scrollViewer = GetScrollViewer(QueueListBox);
            scrollViewer?.ScrollToVerticalOffset(scrollViewer.VerticalOffset + 100);
        }
    }
    
    /// <summary>
    /// Get ScrollViewer from ListBox
    /// </summary>
    private ScrollViewer? GetScrollViewer(DependencyObject o)
    {
        if (o is ScrollViewer) return (ScrollViewer)o;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(o, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
    
    /// <summary>
    /// Start drag operation for queue reordering (AP-004)
    /// </summary>
    private void Queue_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            _dragStartPoint = e.GetPosition(listBox);
            
            // Find the item being dragged
            var item = GetItemAtPoint(listBox, _dragStartPoint);
            if (item != null)
            {
                _dragStartIndex = listBox.Items.IndexOf(item);
            }
        }
    }
    
    /// <summary>
    /// Handle drop for queue reordering (AP-004)
    /// </summary>
    private void Queue_Drop(object sender, DragEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is EnhancedAudioPlayerViewModel vm)
        {
            var dropPoint = e.GetPosition(listBox);
            var targetItem = GetItemAtPoint(listBox, dropPoint);
            
            if (targetItem != null && _dragStartIndex >= 0)
            {
                int targetIndex = listBox.Items.IndexOf(targetItem);
                if (targetIndex >= 0 && targetIndex != _dragStartIndex)
                {
                    vm.MoveQueueItem(_dragStartIndex, targetIndex);
                }
            }
            
            _dragStartIndex = -1;
        }
    }
    
    /// <summary>
    /// Get the item at a specific point in the ListBox
    /// </summary>
    private object? GetItemAtPoint(ListBox listBox, Point point)
    {
        var element = listBox.InputHitTest(point) as DependencyObject;
        while (element != null && element != listBox)
        {
            if (element is ListBoxItem item)
            {
                return item.DataContext;
            }
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }
    
    /// <summary>
    /// Toggle fullscreen visualizer mode
    /// </summary>
    private void OnToggleFullscreenVisualizerClick(object sender, RoutedEventArgs e)
    {
        if (_isVisualizerFullscreen)
            ExitFullscreenVisualizer();
        else
            EnterFullscreenVisualizer();
    }
    
    private void EnterFullscreenVisualizer()
    {
        try
        {
            var vm = DataContext as EnhancedAudioPlayerViewModel;
            
            // Create a new fullscreen window
            _fullscreenWindow = new Window
            {
                Title = "PlatypusTools Enhanced Visualizer",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None,
                Background = new SolidColorBrush(Color.FromRgb(10, 14, 39)),
                Topmost = true,
                Cursor = Cursors.None // Hide cursor
            };
            
            // Create visualizer for fullscreen
            var fullscreenVisualizer = new AudioVisualizerView();
            fullscreenVisualizer.SetColorScheme(vm?.ColorSchemeIndex ?? 0);
            
            // Create controls overlay with track info and playback buttons
            _fullscreenOsd = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = 1
            };
            
            var osdGrid = new Grid { Margin = new Thickness(20, 15, 20, 15) };
            osdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            osdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            osdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            // Left: Track info
            var trackInfoPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            var titleBlock = new TextBlock
            {
                Text = vm?.CurrentTrackTitle ?? "No Track",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            trackInfoPanel.Children.Add(titleBlock);
            var artistBlock = new TextBlock
            {
                Text = $"{vm?.CurrentTrackArtist} • {vm?.CurrentTrackAlbum}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                Margin = new Thickness(0, 3, 0, 0)
            };
            trackInfoPanel.Children.Add(artistBlock);
            Grid.SetColumn(trackInfoPanel, 0);
            osdGrid.Children.Add(trackInfoPanel);
            
            // Center: Playback controls
            var controlsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            var prevBtn = new Button
            {
                Content = "⏮",
                FontSize = 22,
                Width = 50,
                Height = 50,
                Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(5, 0, 5, 0)
            };
            prevBtn.Click += (s, a) => vm?.PreviousCommand?.Execute(null);
            controlsPanel.Children.Add(prevBtn);
            
            var playPauseBtn = new Button
            {
                Content = vm?.IsPlaying == true ? "⏸" : "▶",
                FontSize = 28,
                Width = 60,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(233, 168, 32)),
                Foreground = Brushes.Black,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(5, 0, 5, 0)
            };
            playPauseBtn.Click += (s, a) =>
            {
                vm?.PlayPauseCommand?.Execute(null);
                playPauseBtn.Content = vm?.IsPlaying == true ? "⏸" : "▶";
            };
            controlsPanel.Children.Add(playPauseBtn);
            
            var nextBtn = new Button
            {
                Content = "⏭",
                FontSize = 22,
                Width = 50,
                Height = 50,
                Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(5, 0, 5, 0)
            };
            nextBtn.Click += (s, a) => vm?.NextCommand?.Execute(null);
            controlsPanel.Children.Add(nextBtn);
            
            Grid.SetColumn(controlsPanel, 1);
            osdGrid.Children.Add(controlsPanel);
            
            // Right: Progress display
            var progressPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var progressBlock = new TextBlock
            {
                Text = $"{vm?.PositionDisplay ?? "0:00"} / {vm?.DurationDisplay ?? "0:00"}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(233, 168, 32))
            };
            progressPanel.Children.Add(progressBlock);
            Grid.SetColumn(progressPanel, 2);
            osdGrid.Children.Add(progressPanel);
            
            _fullscreenOsd.Child = osdGrid;
            
            // Create lyrics overlay for fullscreen
            Border? lyricsOverlay = null;
            TextBlock? lyricsText = null;
            if (vm?.ShowLyricsOverlay == true)
            {
                lyricsOverlay = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(20, 10, 20, 10),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 0, 20, 100) // Above the OSD
                };
                lyricsText = new TextBlock
                {
                    Text = vm?.CurrentLyricLineText ?? "",
                    FontSize = 24,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 800
                };
                lyricsOverlay.Child = lyricsText;
            }
            
            // Container grid
            var container = new Grid();
            container.Children.Add(fullscreenVisualizer);
            if (lyricsOverlay != null)
                container.Children.Add(lyricsOverlay);
            container.Children.Add(_fullscreenOsd);
            
            _fullscreenWindow.Content = container;
            
            // Subscribe to spectrum data for fullscreen visualizer
            void OnSpectrumData(object? s, float[] data)
            {
                if (data == null || data.Length == 0) return;
                
                var doubleData = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                    doubleData[i] = data[i];
                
                string mode = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0);
                int barCount = vm?.BarCount ?? 32;
                int colorIndex = vm?.ColorSchemeIndex ?? 0;
                
                fullscreenVisualizer.Dispatcher.Invoke(() =>
                {
                    fullscreenVisualizer.SetColorScheme(colorIndex);
                    fullscreenVisualizer.UpdateSpectrumData(doubleData, mode, barCount);
                    
                    // Update lyrics if enabled
                    if (lyricsText != null && vm?.ShowLyricsOverlay == true)
                    {
                        lyricsText.Text = vm?.CurrentLyricLineText ?? "";
                    }
                    if (lyricsOverlay != null)
                    {
                        lyricsOverlay.Visibility = vm?.ShowLyricsOverlay == true && !string.IsNullOrWhiteSpace(vm?.CurrentLyricLineText)
                            ? Visibility.Visible : Visibility.Collapsed;
                    }
                });
            }
            
            EnhancedAudioPlayerService.Instance.SpectrumDataUpdated += OnSpectrumData;
            
            // Setup OSD auto-hide timer (3 seconds)
            _osdHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _osdHideTimer.Tick += (s, args) =>
            {
                if (_fullscreenOsd != null)
                {
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
                    _fullscreenOsd.BeginAnimation(OpacityProperty, fadeOut);
                }
                _osdHideTimer?.Stop();
            };
            _osdHideTimer.Start();
            
            // Show OSD on mouse move
            _fullscreenWindow.MouseMove += (s, args) =>
            {
                _fullscreenWindow.Cursor = Cursors.Arrow;
                if (_fullscreenOsd != null)
                {
                    _fullscreenOsd.BeginAnimation(OpacityProperty, null);
                    _fullscreenOsd.Opacity = 1;
                    
                    // Update track info and progress
                    if (_fullscreenOsd.Child is Grid grid && grid.Children.Count >= 3)
                    {
                        // Update title and artist
                        if (grid.Children[0] is StackPanel infoPanel && infoPanel.Children.Count >= 2)
                        {
                            ((TextBlock)infoPanel.Children[0]).Text = vm?.CurrentTrackTitle ?? "No Track";
                            ((TextBlock)infoPanel.Children[1]).Text = $"{vm?.CurrentTrackArtist} • {vm?.CurrentTrackAlbum}";
                        }
                        // Update play/pause button state
                        if (grid.Children[1] is StackPanel ctrlPanel && ctrlPanel.Children.Count >= 2)
                        {
                            if (ctrlPanel.Children[1] is Button ppBtn)
                            {
                                ppBtn.Content = vm?.IsPlaying == true ? "⏸" : "▶";
                            }
                        }
                        // Update progress
                        if (grid.Children[2] is StackPanel progPanel && progPanel.Children.Count > 0)
                        {
                            ((TextBlock)progPanel.Children[0]).Text = $"{vm?.PositionDisplay ?? "0:00"} / {vm?.DurationDisplay ?? "0:00"}";
                        }
                    }
                }
                
                _osdHideTimer?.Stop();
                _osdHideTimer?.Start();
                
                // Hide cursor after delay
                var cursorHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                cursorHideTimer.Tick += (cs, ca) =>
                {
                    if (_fullscreenWindow != null)
                        _fullscreenWindow.Cursor = Cursors.None;
                    cursorHideTimer.Stop();
                };
                cursorHideTimer.Start();
            };
            
            // Close on Escape or double-click
            _fullscreenWindow.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    EnhancedAudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumData;
                    ExitFullscreenVisualizer();
                }
                else if (args.Key == Key.Space)
                {
                    vm?.PlayPauseCommand?.Execute(null);
                }
            };
            
            _fullscreenWindow.MouseDoubleClick += (s, args) =>
            {
                EnhancedAudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumData;
                ExitFullscreenVisualizer();
            };
            
            _fullscreenWindow.Closed += (s, args) =>
            {
                EnhancedAudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumData;
                _isVisualizerFullscreen = false;
                _fullscreenWindow = null;
                _osdHideTimer?.Stop();
            };
            
            _fullscreenWindow.Show();
            _isVisualizerFullscreen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error entering fullscreen: {ex.Message}");
            MessageBox.Show($"Could not enter fullscreen mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void ExitFullscreenVisualizer()
    {
        try
        {
            _fullscreenWindow?.Close();
            _fullscreenWindow = null;
            _isVisualizerFullscreen = false;
            _osdHideTimer?.Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error exiting fullscreen: {ex.Message}");
        }
    }
}
