using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PlatypusTools.UI.Converters
{
    /// <summary>
    /// Converts a TimeSpan to pixel position based on pixels-per-second scale.
    /// </summary>
    public class TimeSpanToPixelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is TimeSpan timeSpan &&
                values[1] is double pixelsPerSecond)
            {
                return timeSpan.TotalSeconds * pixelsPerSecond;
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts TimeSpan to pixel width for clip duration.
    /// </summary>
    public class DurationToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is TimeSpan duration &&
                values[1] is double pixelsPerSecond)
            {
                double width = duration.TotalSeconds * pixelsPerSecond;
                return Math.Max(width, 10); // Minimum width of 10 pixels
            }
            return 50.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a string hex color to a SolidColorBrush.
    /// </summary>
    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorString && !string.IsNullOrEmpty(colorString))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorString);
                    return new SolidColorBrush(color);
                }
                catch
                {
                    return new SolidColorBrush(Colors.DodgerBlue);
                }
            }
            return new SolidColorBrush(Colors.DodgerBlue);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts boolean IsSelected to a border brush.
    /// </summary>
    public class IsSelectedToBorderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return new SolidColorBrush(Colors.White);
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
