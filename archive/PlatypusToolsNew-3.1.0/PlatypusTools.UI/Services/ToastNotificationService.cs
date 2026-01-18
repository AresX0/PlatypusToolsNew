using System;
using System.Collections.ObjectModel;

using System.Windows;
using System.Windows.Threading;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for displaying toast notifications within the application.
    /// Provides non-intrusive feedback for operations, errors, and status updates.
    /// </summary>
    public class ToastNotificationService
    {
        private static readonly Lazy<ToastNotificationService> _instance = new(() => new ToastNotificationService());
        public static ToastNotificationService Instance => _instance.Value;
        
        private readonly Dispatcher _dispatcher;
        
        public ObservableCollection<ToastNotification> ActiveNotifications { get; } = new();
        
        public event EventHandler<ToastNotification>? NotificationShown;
        public event EventHandler<ToastNotification>? NotificationDismissed;
        
        private ToastNotificationService()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }
        
        /// <summary>
        /// Shows an informational toast notification.
        /// </summary>
        public ToastNotification ShowInfo(string message, string? title = null, int durationMs = 3000)
        {
            return Show(new ToastNotification
            {
                Type = ToastType.Info,
                Title = title ?? "Information",
                Message = message,
                Duration = durationMs
            });
        }
        
        /// <summary>
        /// Shows a success toast notification.
        /// </summary>
        public ToastNotification ShowSuccess(string message, string? title = null, int durationMs = 3000)
        {
            return Show(new ToastNotification
            {
                Type = ToastType.Success,
                Title = title ?? "Success",
                Message = message,
                Duration = durationMs
            });
        }
        
        /// <summary>
        /// Shows a warning toast notification.
        /// </summary>
        public ToastNotification ShowWarning(string message, string? title = null, int durationMs = 5000)
        {
            return Show(new ToastNotification
            {
                Type = ToastType.Warning,
                Title = title ?? "Warning",
                Message = message,
                Duration = durationMs
            });
        }
        
        /// <summary>
        /// Shows an error toast notification.
        /// </summary>
        public ToastNotification ShowError(string message, string? title = null, int durationMs = 7000)
        {
            return Show(new ToastNotification
            {
                Type = ToastType.Error,
                Title = title ?? "Error",
                Message = message,
                Duration = durationMs
            });
        }
        
        /// <summary>
        /// Shows a toast with an action button.
        /// </summary>
        public ToastNotification ShowWithAction(string message, string actionText, Action action, 
            string? title = null, ToastType type = ToastType.Info, int durationMs = 5000)
        {
            return Show(new ToastNotification
            {
                Type = type,
                Title = title ?? "Action Required",
                Message = message,
                ActionText = actionText,
                Action = action,
                Duration = durationMs
            });
        }
        
        /// <summary>
        /// Shows a persistent notification that must be manually dismissed.
        /// </summary>
        public ToastNotification ShowPersistent(string message, string? title = null, ToastType type = ToastType.Info)
        {
            return Show(new ToastNotification
            {
                Type = type,
                Title = title ?? "Notice",
                Message = message,
                IsPersistent = true,
                Duration = 0
            });
        }
        
        /// <summary>
        /// Shows a custom toast notification.
        /// </summary>
        public ToastNotification Show(ToastNotification notification)
        {
            _dispatcher.Invoke(() =>
            {
                notification.Id = Guid.NewGuid();
                notification.Timestamp = DateTime.Now;
                ActiveNotifications.Add(notification);
                NotificationShown?.Invoke(this, notification);
                
                if (!notification.IsPersistent && notification.Duration > 0)
                {
                    var timer = new System.Timers.Timer(notification.Duration);
                    timer.Elapsed += (s, e) =>
                    {
                        timer.Stop();
                        timer.Dispose();
                        Dismiss(notification);
                    };
                    timer.AutoReset = false;
                    timer.Start();
                }
            });
            
            return notification;
        }
        
        /// <summary>
        /// Dismisses a specific notification.
        /// </summary>
        public void Dismiss(ToastNotification notification)
        {
            _dispatcher.Invoke(() =>
            {
                if (ActiveNotifications.Contains(notification))
                {
                    ActiveNotifications.Remove(notification);
                    NotificationDismissed?.Invoke(this, notification);
                }
            });
        }
        
        /// <summary>
        /// Dismisses a notification by its ID.
        /// </summary>
        public void Dismiss(Guid id)
        {
            _dispatcher.Invoke(() =>
            {
                var notification = ActiveNotifications.FirstOrDefault(n => n.Id == id);
                if (notification != null)
                {
                    Dismiss(notification);
                }
            });
        }
        
        /// <summary>
        /// Dismisses all active notifications.
        /// </summary>
        public void DismissAll()
        {
            _dispatcher.Invoke(() =>
            {
                while (ActiveNotifications.Count > 0)
                {
                    var notification = ActiveNotifications[0];
                    ActiveNotifications.RemoveAt(0);
                    NotificationDismissed?.Invoke(this, notification);
                }
            });
        }
    }
    
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }
    
    public class ToastNotification
    {
        public Guid Id { get; set; }
        public ToastType Type { get; set; } = ToastType.Info;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int Duration { get; set; } = 3000;
        public bool IsPersistent { get; set; }
        public string? ActionText { get; set; }
        public Action? Action { get; set; }
        
        public string Icon => Type switch
        {
            ToastType.Success => "✓",
            ToastType.Warning => "⚠",
            ToastType.Error => "✕",
            _ => "ℹ"
        };
        
        public string BackgroundColor => Type switch
        {
            ToastType.Success => "#28A745",
            ToastType.Warning => "#FFC107",
            ToastType.Error => "#DC3545",
            _ => "#17A2B8"
        };
    }
}
