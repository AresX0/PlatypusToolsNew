using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// Before/after comparison viewer with slider, side-by-side, and overlay modes.
    /// Used for comparing original and processed images.
    /// </summary>
    public partial class ComparisonViewer : UserControl
    {
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

        /// <summary>
        /// Loads images from file paths.
        /// </summary>
        public void LoadImages(string leftPath, string rightPath)
        {
            try
            {
                LeftImage = new BitmapImage(new System.Uri(leftPath));
            }
            catch { }

            try
            {
                RightImage = new BitmapImage(new System.Uri(rightPath));
            }
            catch { }
        }

        /// <summary>
        /// Clears both images.
        /// </summary>
        public void Clear()
        {
            LeftImage = null;
            RightImage = null;
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
