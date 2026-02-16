using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// Status bar control displaying operation progress, elapsed time, cancel button,
    /// performance monitor (CPU/RAM/FPS), and notification bell (IDEA-008/012).
    /// </summary>
    public partial class StatusBarControl : UserControl
    {
        private Popup? _notificationPopup;

        public StatusBarControl()
        {
            InitializeComponent();
            
            // Subscribe to unread count changes for badge
            ToastNotificationService.Instance.UnreadCountChanged += (s, count) =>
            {
                Dispatcher.Invoke(() =>
                {
                    NotificationBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    NotificationBadgeText.Text = count > 99 ? "99+" : count.ToString();
                });
            };
        }

        private void PerformanceMonitor_Click(object sender, MouseButtonEventArgs e)
        {
            Services.PerformanceMonitorService.Instance.IsEnabled = !Services.PerformanceMonitorService.Instance.IsEnabled;
        }

        private void NotificationBell_Click(object sender, MouseButtonEventArgs e)
        {
            if (_notificationPopup == null)
            {
                _notificationPopup = CreateNotificationPopup();
            }
            
            _notificationPopup.IsOpen = !_notificationPopup.IsOpen;
            if (_notificationPopup.IsOpen)
            {
                ToastNotificationService.Instance.IsPanelOpen = true;
                ToastNotificationService.Instance.MarkAllRead();
            }
            else
            {
                ToastNotificationService.Instance.IsPanelOpen = false;
            }
        }

        private Popup CreateNotificationPopup()
        {
            var popup = new Popup
            {
                PlacementTarget = this,
                Placement = PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                Width = 360,
                MaxHeight = 450
            };

            popup.Closed += (s, e) => ToastNotificationService.Instance.IsPanelOpen = false;

            var border = new Border
            {
                Background = Application.Current.FindResource("ControlBackgroundBrush") as System.Windows.Media.Brush 
                    ?? System.Windows.Media.Brushes.White,
                BorderBrush = Application.Current.FindResource("ControlBorderBrush") as System.Windows.Media.Brush 
                    ?? System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(0)
            };

            var rootStack = new StackPanel();

            // Header
            var header = new Border
            {
                Background = Application.Current.FindResource("AccentBrush") as System.Windows.Media.Brush 
                    ?? System.Windows.Media.Brushes.DodgerBlue,
                Padding = new Thickness(12, 8, 12, 8)
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = new TextBlock
            {
                Text = "🔔 Notification Center",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(headerText, 0);
            headerGrid.Children.Add(headerText);

            var clearBtn = new Button
            {
                Content = "Clear All",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128, 255, 255, 255)),
                Cursor = Cursors.Hand
            };
            clearBtn.Click += (s, ev) => ToastNotificationService.Instance.ClearHistory();
            Grid.SetColumn(clearBtn, 1);
            headerGrid.Children.Add(clearBtn);
            header.Child = headerGrid;
            rootStack.Children.Add(header);

            // Notification list
            var listBox = new ListBox
            {
                ItemsSource = ToastNotificationService.Instance.NotificationHistory,
                MaxHeight = 380,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0)
            };
            listBox.ItemTemplate = CreateNotificationItemTemplate();
            rootStack.Children.Add(listBox);

            // Empty state
            var emptyText = new TextBlock
            {
                Text = "No notifications yet",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20),
                FontStyle = FontStyles.Italic,
                Foreground = System.Windows.Media.Brushes.Gray
            };
            // Bind visibility to empty collection
            emptyText.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Count")
            {
                Source = ToastNotificationService.Instance.NotificationHistory,
                Converter = new ZeroToVisibleConverter()
            });
            rootStack.Children.Add(emptyText);

            border.Child = rootStack;
            popup.Child = border;
            return popup;
        }

        private DataTemplate CreateNotificationItemTemplate()
        {
            var template = new DataTemplate(typeof(ToastNotification));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            factory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding 
            { 
                Source = Application.Current, 
                Path = new PropertyPath("Resources[ControlBorderBrush]") 
            });

            var grid = new FrameworkElementFactory(typeof(Grid));
            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(col0);
            grid.AppendChild(col1);
            grid.AppendChild(col2);

            // Icon
            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Icon"));
            icon.SetValue(TextBlock.FontSizeProperty, 16.0);
            icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
            icon.SetValue(Grid.ColumnProperty, 0);
            grid.AppendChild(icon);

            // Content
            var contentStack = new FrameworkElementFactory(typeof(StackPanel));
            contentStack.SetValue(Grid.ColumnProperty, 1);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.FontSizeProperty, 12.0);
            contentStack.AppendChild(title);

            var msg = new FrameworkElementFactory(typeof(TextBlock));
            msg.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Message"));
            msg.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            msg.SetValue(TextBlock.FontSizeProperty, 11.0);
            msg.SetValue(TextBlock.OpacityProperty, 0.8);
            contentStack.AppendChild(msg);

            grid.AppendChild(contentStack);

            // Timestamp
            var ts = new FrameworkElementFactory(typeof(TextBlock));
            ts.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Timestamp") 
            { 
                StringFormat = "HH:mm" 
            });
            ts.SetValue(TextBlock.FontSizeProperty, 10.0);
            ts.SetValue(TextBlock.OpacityProperty, 0.5);
            ts.SetValue(Grid.ColumnProperty, 2);
            ts.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top);
            grid.AppendChild(ts);

            factory.AppendChild(grid);
            template.VisualTree = factory;
            return template;
        }

        private void Pi_Click(object sender, MouseButtonEventArgs e)
        {
            // Open in default browser - WebView2 can't write to Program Files
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://open.spotify.com/show/5dJA1aqxZeh5Wma6DEfLRP",
                UseShellExecute = true
            });
        }
    }

    /// <summary>
    /// Converts 0 to Visible, anything else to Collapsed.
    /// </summary>
    internal class ZeroToVisibleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (value is int count && count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotSupportedException();
        }
    }
}
