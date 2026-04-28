using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace PlatypusTools.UI.Converters
{
    /// <summary>
    /// Coerces an arbitrary ToolTip value (string, ToolTip object, or any object) to a plain
    /// string for binding to AutomationProperties.Name. Returns empty string for null or
    /// unconvertible values so the binding never throws during deferred style instantiation.
    /// </summary>
    public sealed class ToolTipToStringConverter : IValueConverter
    {
        public static readonly ToolTipToStringConverter Instance = new ToolTipToStringConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return string.Empty;
                if (value is string s) return s;
                if (value is ToolTip tt && tt.Content != null)
                {
                    return tt.Content as string ?? tt.Content.ToString() ?? string.Empty;
                }
                return value.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
