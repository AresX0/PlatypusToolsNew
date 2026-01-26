using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// Before/after comparison viewer with slider, side-by-side, and overlay modes.
    /// Supports synchronized zoom and pan across images.
    /// </summary>
    public partial class ComparisonViewer : UserControl
    {
        private bool _isSyncing = false;
        
        public ComparisonViewer()
        {
            InitializeComponent();
            Loaded += ComparisonViewer_Loaded;
            SizeChanged += ComparisonViewer_SizeChanged;
        }

        #region Dependency Properties

        /// <summary>
        /// The left (before/original) image source.
        /// </summary>
        public static readonly DependencyProperty LeftImageProperty =
            DependencyProperty.Register(nameof(LeftImage), typeof(ImageSource), typeof(ComparisonViewer),
                new PropertyMetadata(null));

        public ImageSource LeftImage
        {
            get => (ImageSource)GetValue(LeftImageProperty);
            set => SetValue(LeftImageProperty, value);
        }

        /// <summary>
        /// The right (after/processed) image source.
        /// </summary>
        public static readonly DependencyProperty RightImageProperty =
            DependencyProperty.Register(nameof(RightImage), typeof(ImageSource), typeof(ComparisonViewer),
                new PropertyMetadata(null));

        public ImageSource RightImage
        {
            get => (ImageSource)GetValue(RightImageProperty);
            set => SetValue(RightImageProperty, value);
        }

        /// <summary>
        /// The slider position (0-1).
        /// </summary>
        public static readonly DependencyProperty SliderPositionProperty =
            DependencyProperty.Register(nameof(SliderPosition), typeof(double), typeof(ComparisonViewer),
                new PropertyMetadata(0.5, OnSliderPositionChanged));

        public double SliderPosition
        {
            get => (double)GetValue(SliderPositionProperty);
            set => SetValue(SliderPositionProperty, value);
        }

        /// <summary>
        /// The overlay opacity (0-1).
        /// </summary>
        public static readonly DependencyProperty OverlayOpacityProperty =
            DependencyProperty.Register(nameof(OverlayOpacity), typeof(double), typeof(ComparisonViewer),
                new PropertyMetadata(0.5));

        public double OverlayOpacity
        {
            get => (double)GetValue(OverlayOpacityProperty);
            set => SetValue(OverlayOpacityProperty, value);
        }

        /// <summary>
        /// The comparison mode.
        /// </summary>
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(ComparisonMode), typeof(ComparisonViewer),
                new PropertyMetadata(ComparisonMode.Slider, OnModeChanged));

        public ComparisonMode Mode
        {
            get => (ComparisonMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        /// <summary>
        /// The current zoom level (1.0 = 100%).
        /// </summary>
        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(ComparisonViewer),
                new PropertyMetadata(1.0, OnZoomLevelChanged));

        public double ZoomLevel
        {
            get => (double)GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, System.Math.Clamp(value, 0.1, 10.0));
        }

        /// <summary>
        /// Whether pan is synchronized between left and right images.
        /// </summary>
        public static readonly DependencyProperty SyncPanEnabledProperty =
            DependencyProperty.Register(nameof(SyncPanEnabled), typeof(bool), typeof(ComparisonViewer),
                new PropertyMetadata(true));

        public bool SyncPanEnabled
        {
            get => (bool)GetValue(SyncPanEnabledProperty);
            set => SetValue(SyncPanEnabledProperty, value);
        }

        #endregion

        #region Methods

        private void ComparisonViewer_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSliderPosition();
        }

        private void ComparisonViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSliderPosition();
        }

        private static void OnSliderPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComparisonViewer viewer)
            {
                viewer.UpdateSliderPosition();
            }
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComparisonViewer viewer)
            {
                viewer.UpdateModeUI();
            }
        }

        private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComparisonViewer viewer)
            {
                viewer.ApplyZoom();
            }
        }

        private void UpdateSliderPosition()
        {
            if (SliderContainer.ActualWidth <= 0) return;

            var xPos = SliderPosition * SliderContainer.ActualWidth;

            // Update thumb position
            Canvas.SetLeft(SliderThumb, xPos - 2);
            SliderThumb.Height = SliderContainer.ActualHeight;

            // Update clip rectangle for left image
            LeftClip.Rect = new Rect(0, 0, xPos, SliderContainer.ActualHeight);
        }

        private void SliderThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var currentX = Canvas.GetLeft(SliderThumb);
            var newX = currentX + e.HorizontalChange;

            // Clamp to container bounds
            newX = System.Math.Clamp(newX, 0, SliderContainer.ActualWidth);

            SliderPosition = newX / SliderContainer.ActualWidth;
        }

        private void UpdateModeUI()
        {
            switch (Mode)
            {
                case ComparisonMode.Slider:
                    SliderMode.IsChecked = true;
                    break;
                case ComparisonMode.SideBySide:
                    SideBySideMode.IsChecked = true;
                    break;
                case ComparisonMode.Overlay:
                    OverlayMode.IsChecked = true;
                    break;
            }
        }

        private void ApplyZoom()
        {
            // Apply zoom to all transforms
            SliderZoomTransform.ScaleX = ZoomLevel;
            SliderZoomTransform.ScaleY = ZoomLevel;
            
            LeftZoomTransform.ScaleX = ZoomLevel;
            LeftZoomTransform.ScaleY = ZoomLevel;
            
            RightZoomTransform.ScaleX = ZoomLevel;
            RightZoomTransform.ScaleY = ZoomLevel;
            
            OverlayZoomTransform.ScaleX = ZoomLevel;
            OverlayZoomTransform.ScaleY = ZoomLevel;
        }

        #region Zoom Controls

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomLevel = System.Math.Min(ZoomLevel * 1.25, 10.0);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomLevel = System.Math.Max(ZoomLevel / 1.25, 0.1);
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ZoomLevel = 1.0;
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            // Reset to fit view
            ZoomLevel = 1.0;
            
            // Reset scroll positions
            SliderScrollViewer?.ScrollToHome();
            LeftScrollViewer?.ScrollToHome();
            RightScrollViewer?.ScrollToHome();
            OverlayScrollViewer?.ScrollToHome();
        }

        private void ComparisonArea_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Zoom with Ctrl+Scroll
                if (e.Delta > 0)
                    ZoomLevel = System.Math.Min(ZoomLevel * 1.1, 10.0);
                else
                    ZoomLevel = System.Math.Max(ZoomLevel / 1.1, 0.1);
                
                e.Handled = true;
            }
        }

        #endregion

        #region Synchronized Scrolling

        private void SyncScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!SyncPanEnabled || _isSyncing) return;
            
            if (sender is not ScrollViewer source) return;
            
            // Prevent recursive syncing
            _isSyncing = true;
            
            try
            {
                // Get the normalized scroll position (0-1)
                double hRatio = source.ScrollableWidth > 0 ? source.HorizontalOffset / source.ScrollableWidth : 0;
                double vRatio = source.ScrollableHeight > 0 ? source.VerticalOffset / source.ScrollableHeight : 0;
                
                // Apply to all other scroll viewers
                SyncScrollViewer(SliderScrollViewer, hRatio, vRatio, source);
                SyncScrollViewer(LeftScrollViewer, hRatio, vRatio, source);
                SyncScrollViewer(RightScrollViewer, hRatio, vRatio, source);
                SyncScrollViewer(OverlayScrollViewer, hRatio, vRatio, source);
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void SyncScrollViewer(ScrollViewer? target, double hRatio, double vRatio, ScrollViewer source)
        {
            if (target == null || target == source) return;
            
            double newHOffset = hRatio * target.ScrollableWidth;
            double newVOffset = vRatio * target.ScrollableHeight;
            
            target.ScrollToHorizontalOffset(newHOffset);
            target.ScrollToVerticalOffset(newVOffset);
        }

        #endregion

        /// <summary>
        /// Loads images from file paths.
        /// </summary>
        public void LoadImages(string leftPath, string rightPath)
        {
            try
            {
                LeftImage = Utilities.ImageHelper.LoadFromFile(leftPath);
            }
            catch { }

            try
            {
                RightImage = Utilities.ImageHelper.LoadFromFile(rightPath);
            }
            catch { }
        }

        /// <summary>
        /// Clears both images.
        /// </summary>
        public void Clear()
        {
            LeftImage = null!;
            RightImage = null!;
        }

        #endregion
    }

    /// <summary>
    /// Comparison viewer display modes.
    /// </summary>
    public enum ComparisonMode
    {
        /// <summary>
        /// Slider divides before/after with draggable handle.
        /// </summary>
        Slider,

        /// <summary>
        /// Before and after displayed side by side.
        /// </summary>
        SideBySide,

        /// <summary>
        /// After overlaid on before with adjustable opacity.
        /// </summary>
        Overlay
    }
}
