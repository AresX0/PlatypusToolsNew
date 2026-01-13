using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlatypusTools.UI.Converters
{
    public class FileCountVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isFile = value is bool b && b;
            return isFile ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}