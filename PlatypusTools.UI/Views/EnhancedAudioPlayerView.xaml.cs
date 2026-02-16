using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Services;
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
    private bool _fullscreenVisualizerHidden;
    private AudioVisualizerView? _fullscreenVisualizer;
    private Grid? _fullscreenAlbumArtPanel;
    private TextBlock? _fullscreenModeToast;
    private DispatcherTimer? _modeToastTimer;
    private bool _isSidebarHidden;
    private GridLength _savedSidebarWidth = new GridLength(2, GridUnitType.Star);
    
    public EnhancedAudioPlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += EnhancedAudioPlayerView_PreviewKeyDown;
    }

    private void EnhancedAudioPlayerView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleSidebar();
            e.Handled = true;
        }
    }
    
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Subscribe to ViewModel's scroll request event
        if (e.OldValue is EnhancedAudioPlayerViewModel oldVm)
        {
            oldVm.RequestScrollToCurrentTrack -= OnRequestScrollToCurrentTrack;
        }
        if (e.NewValue is EnhancedAudioPlayerViewModel newVm)
        {
            newVm.RequestScrollToCurrentTrack += OnRequestScrollToCurrentTrack;
        }
    }
    
    private void OnRequestScrollToCurrentTrack(object? sender, EventArgs e)
    {
        // Scroll the queue listbox to the selected item
        Dispatcher.BeginInvoke(() =>
        {
            if (QueueListBox != null && DataContext is EnhancedAudioPlayerViewModel vm && vm.SelectedQueueTrack != null)
            {
                QueueListBox.ScrollIntoView(vm.SelectedQueueTrack);
                QueueListBox.Focus();
            }
        });
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
        // Reset the visualizer and animate album art crossfade when track changes
        Dispatcher.BeginInvoke(() =>
        {
            VisualizerControl?.Reset();
            AnimateAlbumArtCrossfade();
            System.Diagnostics.Debug.WriteLine($"EnhancedAudioPlayerView: Track changed, visualizer reset. Track: {track?.Title ?? "null"}");
        });
    }
    
    /// <summary>
    /// Crossfade animation: old art fades out while new art fades in.
    /// </summary>
    private void AnimateAlbumArtCrossfade()
    {
        try
        {
            if (AlbumArtImageMain == null || AlbumArtImageOld == null) return;
            
            // Copy current image to the "old" layer
            if (AlbumArtImageMain.Source != null)
            {
                AlbumArtImageOld.Source = AlbumArtImageMain.Source;
                AlbumArtImageOld.Opacity = 1.0;
            }
            
            // Fade out old image
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            AlbumArtImageOld.BeginAnimation(OpacityProperty, fadeOut);
            
            // Fade in new image (binding will update Source)
            AlbumArtImageMain.Opacity = 0.0;
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(400))
            {
                BeginTime = TimeSpan.FromMilliseconds(100), // Slight delay for crossfade overlap
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            AlbumArtImageMain.BeginAnimation(OpacityProperty, fadeIn);
        }
        catch { /* Animation is non-critical */ }
    }
    
    private void OnSpectrumDataUpdated(object? sender, float[] data)
    {
        // Forward spectrum data to the visualizer control
        Dispatcher.BeginInvoke(() =>
        {
            if (VisualizerControl != null && data != null && data.Length > 0)
            {
                // Skip sending data when album art view is shown (save CPU/GPU)
                var vm = DataContext as EnhancedAudioPlayerViewModel;
                if (vm?.ShowAlbumArtView == true) return;
                // Convert float[] to double[]
                var doubleData = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                    doubleData[i] = data[i];
                
                // Get mode and settings from ViewModel
                string mode = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0);
                int density = vm?.Density ?? 32;
                int colorIndex = vm?.ColorSchemeIndex ?? 0;
                double sensitivity = vm?.VisualizerSensitivity ?? 0.7;
                int fps = vm?.VisualizerFps ?? 22;
                double crawlSpeed = vm?.CrawlScrollSpeed ?? 1.0;
                
                VisualizerControl.UpdateSpectrumData(doubleData, mode, density, colorIndex, sensitivity, fps, crawlSpeed);
            }
        });
    }
    
    private static string GetVisualizerModeName(int index) => index switch
    {
        0 => "Bars",
        1 => "Mirror",
        2 => "Waveform",
        3 => "VU Meter",
        4 => "Oscilloscope",
        5 => "Circular",
        6 => "Radial",
        7 => "Aurora",
        8 => "Wave Grid",
        9 => "3D Bars",
        10 => "Waterfall",
        11 => "Star Wars Crawl",
        12 => "Particles",
        13 => "Starfield",
        14 => "Toasters",
        15 => "Matrix",
        16 => "Stargate",
        17 => "Klingon",
        18 => "Federation",
        19 => "Jedi",
        20 => "TimeLord",
        21 => "Milkdrop",
        22 => "Honmoon",
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

    #region Sidebar Toggle
    
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidebar();
    }
    
    private void ToggleSidebar()
    {
        _isSidebarHidden = !_isSidebarHidden;
        
        if (_isSidebarHidden)
        {
            // Save current width before hiding
            _savedSidebarWidth = SidebarColumn.Width;
            SidebarColumn.Width = new GridLength(0);
            SidebarColumn.MinWidth = 0;
            SidebarTabControl.Visibility = Visibility.Collapsed;
            SidebarSplitter.Visibility = Visibility.Collapsed;
            SidebarToggleButton.Content = "\u25c0"; // ◀ arrow to indicate "expand"
            SidebarToggleButton.ToolTip = "Show Sidebar (Ctrl+B)";
        }
        else
        {
            SidebarColumn.Width = _savedSidebarWidth;
            SidebarColumn.MinWidth = 280;
            SidebarTabControl.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
            SidebarToggleButton.Content = "\ud83d\udccc"; // 📌
            SidebarToggleButton.ToolTip = "Toggle Sidebar (Ctrl+B)";
        }
    }
    
    #endregion

    #region Smart Playlist Editor

    private void OpenSmartPlaylistEditor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var editor = new SmartPlaylistEditorWindow
            {
                Owner = Window.GetWindow(this)
            };

            // Provide tracks for preview if available
            if (DataContext is ViewModels.EnhancedAudioPlayerViewModel vm)
            {
                editor.PreviewTracks = vm.GetAllLibraryTracks();
            }

            if (editor.ShowDialog() == true && editor.ResultRuleSet != null)
            {
                if (DataContext is ViewModels.EnhancedAudioPlayerViewModel viewModel)
                {
                    viewModel.CreateSmartPlaylist(editor.PlaylistName, editor.ResultRuleSet);
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening smart playlist editor: {ex.Message}");
        }
    }

    #endregion
    
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
            
            // Create visualizer for fullscreen — mark as externally driven BEFORE OnLoaded fires
            _fullscreenVisualizerHidden = false;
            var fullscreenVisualizer = new AudioVisualizerView();
            fullscreenVisualizer.SetExternallyDriven(); // Prevent double subscription, use external mode/data only
            _fullscreenVisualizer = fullscreenVisualizer;
            
            // Set initial mode and color before first render
            string initialMode = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0);
            fullscreenVisualizer.SetColorScheme(vm?.ColorSchemeIndex ?? 0);
            fullscreenVisualizer.UpdateSpectrumData(new double[128], initialMode, vm?.Density ?? 32, vm?.ColorSchemeIndex ?? 0, vm?.VisualizerSensitivity ?? 0.7, vm?.VisualizerFps ?? 22, vm?.CrawlScrollSpeed ?? 1.0);
            
            // Create album art + lyrics panel (hidden by default, shown when visualizer is toggled off with V)
            _fullscreenAlbumArtPanel = CreateFullscreenAlbumArtPanel(vm);
            
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
            
            // Separator between playback and mode controls
            controlsPanel.Children.Add(new TextBlock
            {
                Text = "│",
                FontSize = 24,
                Foreground = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            });
            
            // Visualizer mode: Previous
            var prevModeBtn = new Button
            {
                Content = "◀",
                FontSize = 16,
                Width = 36,
                Height = 36,
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = "Previous Visualizer (← arrow key)"
            };
            
            // Visualizer mode label
            var modeLabel = new TextBlock
            {
                Text = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(233, 168, 32)),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                MinWidth = 90,
                Margin = new Thickness(4, 0, 4, 0),
                Tag = "OsdModeLabel"
            };
            
            // Visualizer mode: Next
            var nextModeBtn = new Button
            {
                Content = "▶",
                FontSize = 16,
                Width = 36,
                Height = 36,
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = "Next Visualizer (→ arrow key)"
            };
            
            prevModeBtn.Click += (s, a) =>
            {
                if (vm != null)
                {
                    int maxMode = 22;
                    vm.VisualizerModeIndex = (vm.VisualizerModeIndex - 1 + maxMode + 1) % (maxMode + 1);
                    var modeName = GetVisualizerModeName(vm.VisualizerModeIndex);
                    modeLabel.Text = modeName;
                    ShowFullscreenModeToast(modeName);
                    fullscreenVisualizer.ForceVisualizationMode(modeName);
                }
            };
            nextModeBtn.Click += (s, a) =>
            {
                if (vm != null)
                {
                    int maxMode = 22;
                    vm.VisualizerModeIndex = (vm.VisualizerModeIndex + 1) % (maxMode + 1);
                    var modeName = GetVisualizerModeName(vm.VisualizerModeIndex);
                    modeLabel.Text = modeName;
                    ShowFullscreenModeToast(modeName);
                    fullscreenVisualizer.ForceVisualizationMode(modeName);
                }
            };
            
            controlsPanel.Children.Add(prevModeBtn);
            controlsPanel.Children.Add(modeLabel);
            controlsPanel.Children.Add(nextModeBtn);
            
            Grid.SetColumn(controlsPanel, 1);
            osdGrid.Children.Add(controlsPanel);
            
            // Right: Sensitivity/Density controls + Progress display
            var rightPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            
            // Sensitivity row
            var sensRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var sensLabel = new TextBlock
            {
                Text = $"Sens: {(vm?.VisualizerSensitivity ?? 0.7):F1}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 60,
                Tag = "OsdSensLabel"
            };
            var sensDownBtn = new Button
            {
                Content = "−",
                FontSize = 14,
                Width = 28, Height = 28,
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 2, 0),
                ToolTip = "Decrease Sensitivity (S key)"
            };
            var sensUpBtn = new Button
            {
                Content = "+",
                FontSize = 14,
                Width = 28, Height = 28,
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "Increase Sensitivity (D key)"
            };
            sensDownBtn.Click += (s, a) =>
            {
                if (vm != null)
                {
                    vm.VisualizerSensitivity = Math.Max(0.1, vm.VisualizerSensitivity - 0.1);
                    sensLabel.Text = $"Sens: {vm.VisualizerSensitivity:F1}";
                    ShowFullscreenModeToast($"Sensitivity: {vm.VisualizerSensitivity:F1}");
                }
            };
            sensUpBtn.Click += (s, a) =>
            {
                if (vm != null)
                {
                    vm.VisualizerSensitivity = Math.Min(3.0, vm.VisualizerSensitivity + 0.1);
                    sensLabel.Text = $"Sens: {vm.VisualizerSensitivity:F1}";
                    ShowFullscreenModeToast($"Sensitivity: {vm.VisualizerSensitivity:F1}");
                }
            };
            sensRow.Children.Add(sensLabel);
            sensRow.Children.Add(sensDownBtn);
            sensRow.Children.Add(sensUpBtn);
            rightPanel.Children.Add(sensRow);
            
            // Density row
            var densRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var densLabel = new TextBlock
            {
                Text = $"Density: {vm?.Density ?? 32}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 60,
                Tag = "OsdDensLabel"
            };
            var densDownBtn = new Button
            {
                Content = "−",
                FontSize = 14,
                Width = 28, Height = 28,
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 2, 0),
                ToolTip = "Decrease Density"
            };
            var densUpBtn = new Button
            {
                Content = "+",
                FontSize = 14,
                Width = 28, Height = 28,
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "Increase Density"
            };
            densDownBtn.Click += (s, a) =>
            {
                if (vm != null)
                {
                    vm.Density = Math.Max(8, vm.Density - 8);
                    densLabel.Text = $"Density: {vm.Density}";
                    ShowFullscreenModeToast($"Density: {vm.Density}");
                }
            };
            densUpBtn.Click += (s, a) =>
            {
                if (vm != null)
                {
                    vm.Density = Math.Min(256, vm.Density + 8);
                    densLabel.Text = $"Density: {vm.Density}";
                    ShowFullscreenModeToast($"Density: {vm.Density}");
                }
            };
            densRow.Children.Add(densLabel);
            densRow.Children.Add(densDownBtn);
            densRow.Children.Add(densUpBtn);
            rightPanel.Children.Add(densRow);
            
            // Progress display
            var progressBlock = new TextBlock
            {
                Text = $"{vm?.PositionDisplay ?? "0:00"} / {vm?.DurationDisplay ?? "0:00"}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(233, 168, 32))
            };
            rightPanel.Children.Add(progressBlock);
            Grid.SetColumn(rightPanel, 2);
            osdGrid.Children.Add(rightPanel);
            
            _fullscreenOsd.Child = osdGrid;
            
            // Create lyrics overlay for fullscreen — ALWAYS create it so toggling works at runtime
            // Visibility is controlled dynamically by the data pump based on ShowLyricsOverlay
            var lyricsOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20, 10, 20, 10),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20, 0, 20, 100), // Above the OSD
                Visibility = Visibility.Collapsed // Start hidden, data pump will show when lyrics are active
            };
            var lyricsText = new TextBlock
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
            
            // Container grid
            var container = new Grid();
            container.Children.Add(fullscreenVisualizer);
            _fullscreenAlbumArtPanel.Visibility = Visibility.Collapsed;
            container.Children.Add(_fullscreenAlbumArtPanel);
            container.Children.Add(lyricsOverlay);
            container.Children.Add(_fullscreenOsd);
            
            _fullscreenWindow.Content = container;
            
            // Subscribe to spectrum data for fullscreen visualizer
            // Dispatch every event directly — matching how the normal view works.
            // The old Interlocked frame-skip guard prevented queue buildup but starved
            // the fullscreen of data: during heavy renders (Honmoon, Klingon, Jedi, etc.)
            // the UI thread was busy painting, the guard never released, and ALL incoming
            // spectrum events were dropped. Result: flat, non-reactive visualizations.
            // Without the guard, multiple dispatches may queue during renders, but each is
            // just a lightweight array copy (microseconds) and only the final one matters
            // since each overwrites _spectrumData. Lyrics also sync now since they're
            // updated on every dispatch.
            
            void OnSpectrumData(object? s, float[] data)
            {
                if (data == null || data.Length == 0) return;
                
                // Don't process spectrum data when visualizer is fully stopped (V key = album art)
                // Only update album art panel info, skip all visualizer work
                if (_fullscreenVisualizerHidden)
                {
                    fullscreenVisualizer.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background, () =>
                    {
                        try
                        {
                            // Only update album art panel track info
                            if (_fullscreenAlbumArtPanel != null)
                            {
                                UpdateTaggedElement(_fullscreenAlbumArtPanel, "FullscreenTrackTitle",
                                    e => ((TextBlock)e).Text = vm?.CurrentTrackTitle ?? "");
                                UpdateTaggedElement(_fullscreenAlbumArtPanel, "FullscreenTrackArtist",
                                    e => ((TextBlock)e).Text = $"{vm?.CurrentTrackArtist} — {vm?.CurrentTrackAlbum}");
                            }
                        }
                        catch { }
                    });
                    return;
                }
                
                // Convert float[] to double[] on background thread
                var doubleData = new double[data.Length];
                for (int i = 0; i < data.Length; i++)
                    doubleData[i] = data[i];
                
                // Read settings from VM on background thread (all simple property reads)
                string mode = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0);
                int density = vm?.Density ?? 32;
                int color = vm?.ColorSchemeIndex ?? 0;
                double sensitivity = vm?.VisualizerSensitivity ?? 0.7;
                int fps = vm?.VisualizerFps ?? 22;
                double crawlSpeed = vm?.CrawlScrollSpeed ?? 1.0;
                
                // Fast path: just write the raw data. The render tick's ApplySmoothing()
                // will pick it up. Only do a full UpdateSpectrumData when settings change.
                // This avoids expensive per-frame mode/density/color/sensitivity/fps
                // reconfiguration that starves the UI thread of render cycles.
                fullscreenVisualizer.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Send, () =>
                {
                    try
                    {
                        // Always push fresh audio data via the fast path
                        fullscreenVisualizer.UpdateSpectrumDataFast(doubleData);
                        
                        // Reconfigure settings only when they actually change
                        // (mode changes are handled by key handlers calling ForceVisualizationMode)
                        fullscreenVisualizer.SetSensitivity(sensitivity);
                        fullscreenVisualizer.SetColorScheme(color);
                    
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
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Fullscreen spectrum data error: {ex.Message}");
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
                            // Update mode label in OSD
                            foreach (var child in ctrlPanel.Children)
                            {
                                if (child is TextBlock tb && tb.Tag?.ToString() == "OsdModeLabel")
                                {
                                    tb.Text = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0);
                                    break;
                                }
                            }
                        }
                        // Update progress + sensitivity/density labels in right panel
                        if (grid.Children[2] is StackPanel rightPnl)
                        {
                            // Update sens/dens labels by Tag
                            foreach (var child in rightPnl.Children)
                            {
                                if (child is StackPanel row)
                                {
                                    foreach (var rowChild in row.Children)
                                    {
                                        if (rowChild is TextBlock tb2)
                                        {
                                            if (tb2.Tag?.ToString() == "OsdSensLabel")
                                                tb2.Text = $"Sens: {vm?.VisualizerSensitivity ?? 0.7:F1}";
                                            else if (tb2.Tag?.ToString() == "OsdDensLabel")
                                                tb2.Text = $"Density: {vm?.Density ?? 32}";
                                        }
                                    }
                                }
                                // Progress is the last TextBlock directly in the panel
                                else if (child is TextBlock progTb && progTb.Tag == null)
                                {
                                    progTb.Text = $"{vm?.PositionDisplay ?? "0:00"} / {vm?.DurationDisplay ?? "0:00"}";
                                }
                            }
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
            
            // Show a brief hint about V key
            var hintText = new TextBlock
            {
                Text = "◀ ▶  Switch Visualizer  ·  S/D  Sensitivity  ·  A/F  Density  ·  V  Album Art  ·  Space  Play/Pause  ·  Esc  Exit",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 20, 0, 0)
            };
            container.Children.Add(hintText);
            
            // Mode name toast (centered, large, fades out on mode switch)
            _fullscreenModeToast = new TextBlock
            {
                Text = "",
                FontSize = 36,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.8
                }
            };
            container.Children.Add(_fullscreenModeToast);
            var hintFade = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            hintFade.Tick += (hs, ha) => { hintText.Visibility = Visibility.Collapsed; hintFade.Stop(); };
            hintFade.Start();
            
            // Close on Escape, toggle visualizer on V, play/pause on Space
            // Use PreviewKeyDown for higher priority — ensures Escape works even under heavy rendering load
            _fullscreenWindow.PreviewKeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    EnhancedAudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumData;
                    ExitFullscreenVisualizer();
                }
                else if (args.Key == Key.Space)
                {
                    vm?.PlayPauseCommand?.Execute(null);
                }
                else if (args.Key == Key.V)
                {
                    ToggleFullscreenVisualizerVisibility(vm);
                }
                else if (args.Key == Key.Right || args.Key == Key.Down)
                {
                    if (vm != null)
                    {
                        int maxMode = 22; // 0-22 = 23 modes
                        vm.VisualizerModeIndex = (vm.VisualizerModeIndex + 1) % (maxMode + 1);
                        var modeName = GetVisualizerModeName(vm.VisualizerModeIndex);
                        modeLabel.Text = modeName;
                        ShowFullscreenModeToast(modeName);
                        
                        // Force mode change through immediately
                        fullscreenVisualizer.ForceVisualizationMode(modeName);
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.Left || args.Key == Key.Up)
                {
                    if (vm != null)
                    {
                        int maxMode = 22;
                        vm.VisualizerModeIndex = (vm.VisualizerModeIndex - 1 + maxMode + 1) % (maxMode + 1);
                        var modeName = GetVisualizerModeName(vm.VisualizerModeIndex);
                        modeLabel.Text = modeName;
                        ShowFullscreenModeToast(modeName);
                        
                        // Force mode change through immediately
                        fullscreenVisualizer.ForceVisualizationMode(modeName);
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.S)
                {
                    // Decrease sensitivity
                    if (vm != null)
                    {
                        vm.VisualizerSensitivity = Math.Max(0.1, vm.VisualizerSensitivity - 0.1);
                        sensLabel.Text = $"Sens: {vm.VisualizerSensitivity:F1}";
                        ShowFullscreenModeToast($"Sensitivity: {vm.VisualizerSensitivity:F1}");
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.D)
                {
                    // Increase sensitivity
                    if (vm != null)
                    {
                        vm.VisualizerSensitivity = Math.Min(3.0, vm.VisualizerSensitivity + 0.1);
                        sensLabel.Text = $"Sens: {vm.VisualizerSensitivity:F1}";
                        ShowFullscreenModeToast($"Sensitivity: {vm.VisualizerSensitivity:F1}");
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.A)
                {
                    // Decrease density
                    if (vm != null)
                    {
                        vm.Density = Math.Max(8, vm.Density - 8);
                        densLabel.Text = $"Density: {vm.Density}";
                        ShowFullscreenModeToast($"Density: {vm.Density}");
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.F)
                {
                    // Increase density
                    if (vm != null)
                    {
                        vm.Density = Math.Min(256, vm.Density + 8);
                        densLabel.Text = $"Density: {vm.Density}";
                        ShowFullscreenModeToast($"Density: {vm.Density}");
                    }
                    args.Handled = true;
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
                // Resume the normal-view visualizer when fullscreen is closed
                VisualizerControl?.ResumeRendering();
            };
            
            _fullscreenWindow.Show();
            _isVisualizerFullscreen = true;
            
            // Pause the normal-view visualizer to free up UI thread and GPU for fullscreen
            VisualizerControl?.PauseRendering();
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
            _fullscreenVisualizerHidden = false;
            _fullscreenVisualizer = null;
            _fullscreenAlbumArtPanel = null;
            _fullscreenModeToast = null;
            _modeToastTimer?.Stop();
            _modeToastTimer = null;
            _osdHideTimer?.Stop();
            
            // Resume the normal-view visualizer now that fullscreen is closed
            VisualizerControl?.ResumeRendering();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error exiting fullscreen: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Shows a brief centered toast with the visualizer mode name, then fades out.
    /// </summary>
    private void ShowFullscreenModeToast(string modeName)
    {
        if (_fullscreenModeToast == null) return;
        
        // Cancel any pending fade
        _modeToastTimer?.Stop();
        _fullscreenModeToast.BeginAnimation(OpacityProperty, null);
        
        _fullscreenModeToast.Text = modeName;
        _fullscreenModeToast.Opacity = 1;
        _fullscreenModeToast.Visibility = Visibility.Visible;
        
        // Fade out after 1.5 seconds
        _modeToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _modeToastTimer.Tick += (s, e) =>
        {
            _modeToastTimer.Stop();
            if (_fullscreenModeToast != null)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(500));
                fadeOut.Completed += (fs, fe) =>
                {
                    if (_fullscreenModeToast != null)
                        _fullscreenModeToast.Visibility = Visibility.Collapsed;
                };
                _fullscreenModeToast.BeginAnimation(OpacityProperty, fadeOut);
            }
        };
        _modeToastTimer.Start();
    }
    
    /// <summary>
    /// Creates the album art + lyrics panel shown in fullscreen when visualizer is hidden.
    /// Large centered album art with blurred background, track info, and scrolling lyrics.
    /// </summary>
    private Grid CreateFullscreenAlbumArtPanel(EnhancedAudioPlayerViewModel? vm)
    {
        var panel = new Grid { Background = new SolidColorBrush(Color.FromRgb(10, 14, 39)) };
        
        // Blurred album art background
        var bgImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 0.2,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1.3, 1.3)
        };
        bgImage.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 60 };
        if (vm?.AlbumArtImage != null)
            bgImage.Source = vm.AlbumArtImage;
        panel.Children.Add(bgImage);
        
        // Dark overlay for readability
        panel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) });
        
        // Content layout
        var contentGrid = new Grid { Margin = new Thickness(40) };
        
        // Centered: Album art + track info
        var artBorder = new Border
        {
            Width = 400,
            Height = 400,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 0,
                Opacity = 0.6,
                Color = Colors.Black
            }
        };
        var artImage = new Image
        {
            Stretch = Stretch.UniformToFill
        };
        if (vm?.AlbumArtImage != null)
            artImage.Source = vm.AlbumArtImage;
        // Tag images for later updates
        artImage.Tag = "FullscreenAlbumArt";
        bgImage.Tag = "FullscreenAlbumBg";
        artBorder.Child = artImage;
        
        var centerPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        centerPanel.Children.Add(artBorder);
        
        // Track info below art
        var trackTitle = new TextBlock
        {
            Text = vm?.CurrentTrackTitle ?? "",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 20, 0, 4),
            Tag = "FullscreenTrackTitle"
        };
        centerPanel.Children.Add(trackTitle);
        
        var trackArtist = new TextBlock
        {
            Text = $"{vm?.CurrentTrackArtist} — {vm?.CurrentTrackAlbum}",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Tag = "FullscreenTrackArtist"
        };
        centerPanel.Children.Add(trackArtist);
        
        contentGrid.Children.Add(centerPanel);
        
        panel.Children.Add(contentGrid);
        
        return panel;
    }
    
    /// <summary>
    /// Toggles between visualizer and album art + lyrics display in fullscreen mode.
    /// Triggered by pressing 'V' key or clicking the Art toggle button.
    /// FULLY STOPS the visualizer when hiding, FULLY STARTS when showing.
    /// </summary>
    private void ToggleFullscreenVisualizerVisibility(EnhancedAudioPlayerViewModel? vm)
    {
        _fullscreenVisualizerHidden = !_fullscreenVisualizerHidden;
        
        if (_fullscreenVisualizer != null)
        {
            if (_fullscreenVisualizerHidden)
            {
                // FULL STOP — no rendering while hidden
                _fullscreenVisualizer.StopRendering();
                _fullscreenVisualizer.Visibility = Visibility.Collapsed;
            }
            else
            {
                // FULL START — fresh from zero
                _fullscreenVisualizer.Visibility = Visibility.Visible;
                _fullscreenVisualizer.StartRendering();
            }
        }
        
        if (_fullscreenAlbumArtPanel != null)
        {
            _fullscreenAlbumArtPanel.Visibility = _fullscreenVisualizerHidden ? Visibility.Visible : Visibility.Collapsed;
            
            // Update album art and track info when showing
            if (_fullscreenVisualizerHidden)
                UpdateFullscreenAlbumArtPanel(vm);
        }
    }
    
    /// <summary>
    /// Updates the fullscreen album art panel with current track data.
    /// </summary>
    private void UpdateFullscreenAlbumArtPanel(EnhancedAudioPlayerViewModel? vm)
    {
        if (_fullscreenAlbumArtPanel == null || vm == null) return;
        
        // Walk the visual tree and update tagged elements
        UpdateTaggedElement(_fullscreenAlbumArtPanel, "FullscreenAlbumArt", e => ((Image)e).Source = vm.AlbumArtImage);
        UpdateTaggedElement(_fullscreenAlbumArtPanel, "FullscreenAlbumBg", e => ((Image)e).Source = vm.AlbumArtImage);
        UpdateTaggedElement(_fullscreenAlbumArtPanel, "FullscreenTrackTitle", e => ((TextBlock)e).Text = vm.CurrentTrackTitle ?? "");
        UpdateTaggedElement(_fullscreenAlbumArtPanel, "FullscreenTrackArtist", e => ((TextBlock)e).Text = $"{vm.CurrentTrackArtist} — {vm.CurrentTrackAlbum}");
    }
    
    private static void UpdateTaggedElement(DependencyObject parent, string tag, Action<FrameworkElement> update)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Tag is string t && t == tag)
            {
                try { update(fe); } catch { }
                return;
            }
            UpdateTaggedElement(child, tag, update);
        }
    }
    
    /// <summary>
    /// Double-click a radio station to play it.
    /// </summary>
    private void RadioStation_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is EnhancedAudioPlayerViewModel vm && vm.SelectedRadioStation != null)
        {
            vm.StreamUrl = vm.SelectedRadioStation.Url;
            vm.PlayStreamCommand.Execute(null);
        }
    }
    
    #region Seek Preview
    
    private float[]? _cachedWaveform;
    private string? _cachedWaveformTrack;
    
    private void SeekSlider_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not EnhancedAudioPlayerViewModel vm) return;
        if (vm.DurationSeconds <= 0) return;
        
        var slider = SeekSlider;
        var pos = e.GetPosition(slider);
        double fraction = Math.Clamp(pos.X / slider.ActualWidth, 0, 1);
        double seekSeconds = fraction * vm.DurationSeconds;
        
        // Update time label
        var ts = TimeSpan.FromSeconds(seekSeconds);
        SeekPreviewTimeText.Text = ts.ToString(ts.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        
        // Position the popup horizontally
        SeekPreviewPopup.HorizontalOffset = pos.X - 76; // Center the 140px popup
        
        // Load waveform data (cache per track)
        string? currentTrack = vm.CurrentTrackTitle;
        if (_cachedWaveform == null || _cachedWaveformTrack != currentTrack)
        {
            _cachedWaveform = vm.GetWaveformData(140);
            _cachedWaveformTrack = currentTrack;
        }
        
        // Draw mini waveform
        DrawSeekPreviewWaveform(fraction);
        
        SeekPreviewPopup.IsOpen = true;
    }
    
    private void SeekSlider_MouseLeave(object sender, MouseEventArgs e)
    {
        SeekPreviewPopup.IsOpen = false;
    }
    
    private void DrawSeekPreviewWaveform(double seekFraction)
    {
        var canvas = SeekPreviewWaveform;
        canvas.Children.Clear();
        
        if (_cachedWaveform == null || _cachedWaveform.Length == 0) return;
        
        double w = canvas.Width;
        double h = canvas.Height;
        double barWidth = w / _cachedWaveform.Length;
        int seekIndex = (int)(seekFraction * _cachedWaveform.Length);
        
        for (int i = 0; i < _cachedWaveform.Length; i++)
        {
            double barHeight = Math.Max(1, _cachedWaveform[i] * h * 0.9);
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(0.5, barWidth - 0.3),
                Height = barHeight,
                Fill = i <= seekIndex 
                    ? new SolidColorBrush(Color.FromArgb(200, 76, 175, 80))   // Green for played
                    : new SolidColorBrush(Color.FromArgb(120, 150, 150, 150))  // Gray for unplayed
            };
            Canvas.SetLeft(rect, i * barWidth);
            Canvas.SetTop(rect, h - barHeight);
            canvas.Children.Add(rect);
        }
        
        // Seek position indicator line
        double lineX = seekFraction * w;
        var line = new System.Windows.Shapes.Line
        {
            X1 = lineX, X2 = lineX,
            Y1 = 0, Y2 = h,
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        };
        canvas.Children.Add(line);
    }
    
    #endregion
    
    #region Streaming Credentials
    
    private string? GetSelectedCredentialKey()
    {
        if (StreamCredServiceCombo?.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString();
        return null;
    }
    
    private string GetSelectedServiceName()
    {
        if (StreamCredServiceCombo?.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? "Service";
        return "Service";
    }
    
    private void SaveStreamCredential_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = GetSelectedCredentialKey();
            if (string.IsNullOrEmpty(key))
            {
                StreamCredStatus.Text = "⚠ Select a service first.";
                return;
            }
            
            var username = StreamCredUsername?.Text?.Trim();
            var password = StreamCredPassword?.Password;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                StreamCredStatus.Text = "⚠ Username and password are required.";
                return;
            }
            
            var serviceName = GetSelectedServiceName();
            CredentialManagerService.Instance.SaveCredential(
                key, username, password,
                $"{serviceName} streaming credentials",
                CredentialType.StreamingService);
            
            StreamCredUsername!.Text = "";
            StreamCredPassword!.Password = "";
            StreamCredStatus.Text = $"✅ {serviceName} credentials saved securely.";
            StreamCredStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        catch (Exception ex)
        {
            StreamCredStatus.Text = $"❌ Error: {ex.Message}";
            StreamCredStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        }
    }
    
    private void RemoveStreamCredential_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = GetSelectedCredentialKey();
            if (string.IsNullOrEmpty(key))
            {
                StreamCredStatus.Text = "⚠ Select a service first.";
                return;
            }
            
            var serviceName = GetSelectedServiceName();
            bool removed = CredentialManagerService.Instance.DeleteCredential(key);
            if (removed)
            {
                StreamCredStatus.Text = $"🗑 {serviceName} credentials removed.";
                StreamCredStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
            }
            else
            {
                StreamCredStatus.Text = $"ℹ No saved credentials for {serviceName}.";
                StreamCredStatus.Foreground = (Brush?)FindResource("TextSecondaryBrush") 
                    ?? new SolidColorBrush(Colors.Gray);
            }
        }
        catch (Exception ex)
        {
            StreamCredStatus.Text = $"❌ Error: {ex.Message}";
            StreamCredStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        }
    }
    
    private void LoadStreamCredential_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = GetSelectedCredentialKey();
            if (string.IsNullOrEmpty(key))
            {
                StreamCredStatus.Text = "⚠ Select a service first.";
                return;
            }
            
            var serviceName = GetSelectedServiceName();
            var credential = CredentialManagerService.Instance.GetCredential(key);
            if (credential != null)
            {
                StreamCredUsername.Text = credential.Username;
                StreamCredPassword.Password = CredentialManagerService.Instance.GetPassword(key) ?? "";
                StreamCredStatus.Text = $"✅ {serviceName} credentials loaded.";
                StreamCredStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
            }
            else
            {
                StreamCredStatus.Text = $"ℹ No saved credentials for {serviceName}.";
                StreamCredStatus.Foreground = (Brush?)FindResource("TextSecondaryBrush") 
                    ?? new SolidColorBrush(Colors.Gray);
            }
        }
        catch (Exception ex)
        {
            StreamCredStatus.Text = $"❌ Error: {ex.Message}";
            StreamCredStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        }
    }
    
    #endregion
}
