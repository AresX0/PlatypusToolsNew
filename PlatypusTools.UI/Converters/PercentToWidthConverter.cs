using System;
using System.Globalization;
using System.Windows.Data;

namespace PlatypusTools.UI.Converters
{
    /// <summary>
    /// Converts a percentage value and a container width into a pixel width.
    /// Usage: MultiBinding with Binding[0]=percent (0-100), Binding[1]=container ActualWidth.
    /// </summary>
    public class PercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is double percent &&
                values[1] is double containerWidth &&
                containerWidth > 0)
            {
                return Math.Max(0, Math.Min(containerWidth, containerWidth * percent / 100.0));
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
