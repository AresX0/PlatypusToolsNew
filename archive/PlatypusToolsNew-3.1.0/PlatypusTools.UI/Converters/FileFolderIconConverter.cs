using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Converters
{
    public class FileFolderIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                bool isFile = value is bool b && b;
                string icon = isFile ? "pack://application:,,,/PlatypusTools.UI;component/Assets/file.png" : "pack://application:,,,/PlatypusTools.UI;component/Assets/folder.png";
                return new BitmapImage(new Uri(icon));
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"FileFolderIconConverter failed: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}