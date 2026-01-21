using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Utilities
{
    /// <summary>
    /// Helper class for applying theme resources to controls.
    /// Provides attached properties for automatic theme application.
    /// </summary>
    public static class ThemeHelper
    {
        /// <summary>
        /// Attached property to apply theme background automatically.
        /// Set to True on any Panel or UserControl to have it inherit WindowBackgroundBrush.
        /// </summary>
        public static readonly DependencyProperty ApplyThemeBackgroundProperty =
            DependencyProperty.RegisterAttached(
                "ApplyThemeBackground",
                typeof(bool),
                typeof(ThemeHelper),
                new PropertyMetadata(false, OnApplyThemeBackgroundChanged));

        public static bool GetApplyThemeBackground(DependencyObject obj) 
            => (bool)obj.GetValue(ApplyThemeBackgroundProperty);

        public static void SetApplyThemeBackground(DependencyObject obj, bool value) 
            => obj.SetValue(ApplyThemeBackgroundProperty, value);

        private static void OnApplyThemeBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                if (d is Panel panel)
                {
                    panel.SetResourceReference(Panel.BackgroundProperty, "WindowBackgroundBrush");
                }
                else if (d is Control control)
                {
                    control.SetResourceReference(Control.BackgroundProperty, "WindowBackgroundBrush");
                    control.SetResourceReference(Control.ForegroundProperty, "WindowForegroundBrush");
                }
                else if (d is Border border)
                {
                    border.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                }
            }
        }

        /// <summary>
        /// Applies theme background to a control's root panel at runtime.
        /// Call this in code-behind for views not using the attached property.
        /// </summary>
        public static void ApplyThemeToUserControl(UserControl userControl)
        {
            if (userControl.Content is Panel panel)
            {
                panel.SetResourceReference(Panel.BackgroundProperty, "WindowBackgroundBrush");
            }
        }
    }
}
