using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PlatypusTools.UI.Controls
{
    public partial class NotificationBanner : UserControl
    {
        private DispatcherTimer? _autoHideTimer;
        private Action? _actionCallback;

        public enum NotificationType { Info, Success, Warning, Error }

        public event EventHandler? Closed;

        public NotificationBanner()
        {
            InitializeComponent();
        }

        public void Show(string message, NotificationType type = NotificationType.Info, 
            int autoHideMs = 5000, string? actionText = null, Action? actionCallback = null)
        {
            MessageText.Text = message;
            _actionCallback = actionCallback;

            // Set colors and icon based on type
            var (background, icon) = type switch
            {
                NotificationType.Success => (new SolidColorBrush(Color.FromRgb(46, 125, 50)), "✓"),
                NotificationType.Warning => (new SolidColorBrush(Color.FromRgb(237, 108, 2)), "⚠"),
                NotificationType.Error => (new SolidColorBrush(Color.FromRgb(211, 47, 47)), "✕"),
                _ => (new SolidColorBrush(Color.FromRgb(25, 118, 210)), "ℹ")
            };

            BannerBorder.Background = background;
            IconText.Text = icon;

            // Action button
            if (!string.IsNullOrEmpty(actionText) && actionCallback != null)
            {
                ActionButton.Content = actionText;
                ActionButton.Visibility = Visibility.Visible;
            }
            else
            {
                ActionButton.Visibility = Visibility.Collapsed;
            }

            // Show with animation
            Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            BeginAnimation(OpacityProperty, fadeIn);

            // Auto-hide
            if (autoHideMs > 0)
            {
                _autoHideTimer?.Stop();
                _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoHideMs) };
                _autoHideTimer.Tick += (s, e) =>
                {
                    _autoHideTimer.Stop();
                    Hide();
                };
                _autoHideTimer.Start();
            }
        }

        public void Hide()
        {
            _autoHideTimer?.Stop();
            
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) =>
            {
                Visibility = Visibility.Collapsed;
                Closed?.Invoke(this, EventArgs.Empty);
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private void Action_Click(object sender, RoutedEventArgs e)
        {
            _actionCallback?.Invoke();
            Hide();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        // Static helper methods
        public static NotificationBanner ShowInfo(Panel container, string message, int autoHideMs = 5000)
        {
            var banner = GetOrCreateBanner(container);
            banner.Show(message, NotificationType.Info, autoHideMs);
            return banner;
        }

        public static NotificationBanner ShowSuccess(Panel container, string message, int autoHideMs = 5000)
        {
            var banner = GetOrCreateBanner(container);
            banner.Show(message, NotificationType.Success, autoHideMs);
            return banner;
        }

        public static NotificationBanner ShowWarning(Panel container, string message, int autoHideMs = 8000)
        {
            var banner = GetOrCreateBanner(container);
            banner.Show(message, NotificationType.Warning, autoHideMs);
            return banner;
        }

        public static NotificationBanner ShowError(Panel container, string message, int autoHideMs = 0)
        {
            var banner = GetOrCreateBanner(container);
            banner.Show(message, NotificationType.Error, autoHideMs);
            return banner;
        }

        private static NotificationBanner GetOrCreateBanner(Panel container)
        {
            foreach (var child in container.Children)
            {
                if (child is NotificationBanner existing)
                    return existing;
            }

            var banner = new NotificationBanner();
            container.Children.Insert(0, banner);
            return banner;
        }
    }
}
