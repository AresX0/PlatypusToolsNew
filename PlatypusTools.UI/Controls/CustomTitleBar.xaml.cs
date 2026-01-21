using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlatypusTools.UI.Controls
{
    public partial class CustomTitleBar : UserControl
    {
        private Window? _parentWindow;

        /// <summary>
        /// Dependency property for the window title text.
        /// </summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(CustomTitleBar),
                new PropertyMetadata("PlatypusTools", OnTitleChanged));

        /// <summary>
        /// Dependency property for the window icon source.
        /// </summary>
        public static readonly DependencyProperty IconSourceProperty =
            DependencyProperty.Register(nameof(IconSource), typeof(string), typeof(CustomTitleBar),
                new PropertyMetadata(null, OnIconSourceChanged));

        /// <summary>
        /// Gets or sets the window title.
        /// </summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>
        /// Gets or sets the window icon source path.
        /// </summary>
        public string IconSource
        {
            get => (string)GetValue(IconSourceProperty);
            set => SetValue(IconSourceProperty, value);
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CustomTitleBar titleBar)
            {
                titleBar.TitleText.Text = e.NewValue as string ?? "PlatypusTools";
            }
        }

        private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CustomTitleBar titleBar && e.NewValue is string iconPath && !string.IsNullOrEmpty(iconPath))
            {
                try
                {
                    var uri = new Uri(iconPath, UriKind.RelativeOrAbsolute);
                    titleBar.AppIcon.Source = new System.Windows.Media.Imaging.BitmapImage(uri);
                }
                catch
                {
                    // Fallback - try as relative path
                    try
                    {
                        var uri = new Uri($"pack://application:,,,/{iconPath}", UriKind.Absolute);
                        titleBar.AppIcon.Source = new System.Windows.Media.Imaging.BitmapImage(uri);
                    }
                    catch { }
                }
            }
        }

        public CustomTitleBar()
        {
            InitializeComponent();
            Loaded += CustomTitleBar_Loaded;
        }

        private void CustomTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            _parentWindow = Window.GetWindow(this);
            if (_parentWindow != null)
            {
                _parentWindow.StateChanged += ParentWindow_StateChanged;
                UpdateMaximizeIcon();
            }
        }

        private void ParentWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeIcon();
        }

        private void UpdateMaximizeIcon()
        {
            if (_parentWindow == null) return;

            if (_parentWindow.WindowState == WindowState.Maximized)
            {
                // Show restore icon (two overlapping squares)
                MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M0,3 L7,3 L7,10 L0,10 Z M3,0 L10,0 L10,7 L7,7 M3,3 L3,0");
                MaximizeButton.ToolTip = "Restore";
            }
            else
            {
                // Show maximize icon (single square)
                MaximizeIcon.Data = System.Windows.Media.Geometry.Parse("M0,0 L10,0 L10,10 L0,10 Z");
                MaximizeButton.ToolTip = "Maximize";
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_parentWindow == null) return;

            if (e.ClickCount == 2)
            {
                // Double-click to toggle maximize/restore
                ToggleMaximize();
            }
            else
            {
                // Simply call DragMove on the parent window
                _parentWindow.DragMove();
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // No longer needed with DragMove approach
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            // No longer needed with DragMove approach
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_parentWindow != null)
            {
                _parentWindow.WindowState = WindowState.Minimized;
            }
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            if (_parentWindow == null) return;

            _parentWindow.WindowState = _parentWindow.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _parentWindow?.Close();
        }
    }
}
