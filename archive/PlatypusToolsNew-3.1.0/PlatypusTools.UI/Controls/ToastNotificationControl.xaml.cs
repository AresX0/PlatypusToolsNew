using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// Control for displaying toast notifications in the application.
    /// </summary>
    public partial class ToastNotificationControl : UserControl
    {
        public ToastNotificationControl()
        {
            InitializeComponent();
            DataContext = this;
            Notifications = ToastNotificationService.Instance.ActiveNotifications;
        }
        
        public ObservableCollection<ToastNotification> Notifications { get; }
        
        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ToastNotification notification)
            {
                notification.Action?.Invoke();
                ToastNotificationService.Instance.Dismiss(notification);
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ToastNotification notification)
            {
                ToastNotificationService.Instance.Dismiss(notification);
            }
        }
    }
}
