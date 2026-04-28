using System;
using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Services.LazyLoad
{
    // Attached behavior: defers UserControl construction until the parent TabItem becomes selected
    // for the first time. Apply with: ui:LazyTabContent.UserControlType="{x:Type local:HeavyView}"
    // on a TabItem (the TabItem.Content will be set on first selection).
    public static class LazyTabContent
    {
        public static readonly DependencyProperty UserControlTypeProperty = DependencyProperty.RegisterAttached(
            "UserControlType", typeof(Type), typeof(LazyTabContent),
            new PropertyMetadata(null, OnTypeChanged));

        public static void SetUserControlType(DependencyObject d, Type value) => d.SetValue(UserControlTypeProperty, value);
        public static Type GetUserControlType(DependencyObject d) => (Type)d.GetValue(UserControlTypeProperty);

        private static void OnTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TabItem tab) return;
            tab.RequestBringIntoView -= TryLoad;
            tab.GotFocus -= TryLoad;
            tab.IsVisibleChanged -= TabOnIsVisibleChanged;
            tab.RequestBringIntoView += TryLoad;
            tab.GotFocus += TryLoad;
            tab.IsVisibleChanged += TabOnIsVisibleChanged;
        }

        private static void TabOnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TabItem t && t.IsVisible) Load(t);
        }

        private static void TryLoad(object? sender, EventArgs e)
        {
            if (sender is TabItem t) Load(t);
        }

        private static void Load(TabItem tab)
        {
            if (tab.Content != null) return;
            var type = GetUserControlType(tab);
            if (type == null) return;
            try
            {
                tab.Content = Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                tab.Content = new TextBlock
                {
                    Text = $"Failed to load: {ex.Message}",
                    Margin = new Thickness(12),
                    Foreground = System.Windows.Media.Brushes.OrangeRed
                };
            }
        }
    }
}
