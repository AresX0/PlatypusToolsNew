using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlatypusTools.UI.Converters
{
    public class FileFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isFile = value is bool b && b;
            return isFile ? FontWeights.Normal : FontWeights.Bold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}