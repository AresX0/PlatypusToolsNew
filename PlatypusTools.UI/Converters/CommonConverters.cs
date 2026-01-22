using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PlatypusTools.UI.Converters
{
    /// <summary>
    /// Converts count to Visibility (0 = Collapsed)
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var count = value is int i ? i : 0;
            var hasItems = count > 0;
            if (Invert) hasItems = !hasItems;
            return hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Inverts a boolean value
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }

    /// <summary>
    /// Converts DateTime to relative time string (e.g., "2 hours ago")
    /// </summary>
    public class RelativeTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DateTime dateTime) return "";

            var span = DateTime.Now - dateTime;

            if (span.TotalSeconds < 60) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} days ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)} weeks ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} months ago";
            return $"{(int)(span.TotalDays / 365)} years ago";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts file path to just the filename
    /// </summary>
    public class PathToFilenameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string path ? Path.GetFileName(path) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts file extension to icon or color
    /// </summary>
    public class FileExtensionToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var ext = value is string s ? Path.GetExtension(s).ToLowerInvariant() : "";
            
            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => new SolidColorBrush(Colors.Green),
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => new SolidColorBrush(Colors.Purple),
                ".mp3" or ".wav" or ".flac" or ".aac" => new SolidColorBrush(Colors.Orange),
                ".doc" or ".docx" or ".pdf" => new SolidColorBrush(Colors.Blue),
                ".xls" or ".xlsx" or ".csv" => new SolidColorBrush(Colors.DarkGreen),
                ".zip" or ".rar" or ".7z" => new SolidColorBrush(Colors.Brown),
                ".exe" or ".msi" => new SolidColorBrush(Colors.Red),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts percentage to progress bar color
    /// </summary>
    public class PercentageToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var percent = value is double d ? d : 0;

            return percent switch
            {
                > 90 => new SolidColorBrush(Colors.Red),
                > 75 => new SolidColorBrush(Colors.Orange),
                > 50 => new SolidColorBrush(Colors.Yellow),
                _ => new SolidColorBrush(Colors.Green)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Multibinding converter for combining values
    /// </summary>
    public class MultiValueToStringConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var format = parameter as string ?? "{0}";
            return string.Format(format, values);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts enum value to boolean (for radio button binding)
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.Equals(parameter) ?? false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? parameter : Binding.DoNothing;
    }

    /// <summary>
    /// Converts a double value to a height for UI elements
    /// </summary>
    public class DoubleToHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && double.IsNaN(d) == false && d > 0)
                return d;
            return 5.0; // Default height
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts boolean IsFavorite to star icon (filled or empty)
    /// </summary>
    public class FavoriteIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isFavorite = value is bool b && b;
            return isFavorite ? "★" : "☆";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    
    /// <summary>
    /// Converts boolean to foreground brush (Gray if false, Black if true)
    /// </summary>
    public class BoolToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isEnabled = value is bool b && b;
            return new SolidColorBrush(isEnabled ? Colors.Black : Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts a tab key to Visibility based on settings.
    /// Pass the tab key as ConverterParameter.
    /// Usage: Visibility="{Binding Source={x:Static services:SettingsManager.Current}, 
    ///        Converter={StaticResource TabVisibilityConverter}, ConverterParameter='Multimedia.Audio'}"
    /// </summary>
    public class TabVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var tabKey = parameter as string;
            if (string.IsNullOrEmpty(tabKey)) return Visibility.Visible;

            var settings = value as Services.AppSettings ?? Services.SettingsManager.Current;
            return settings.IsTabVisible(tabKey) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Multi-value converter for tab visibility that responds to VisibleTabs dictionary changes.
    /// Bind to both the AppSettings.VisibleTabs and pass tab key as parameter.
    /// </summary>
    public class TabVisibilityMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var tabKey = parameter as string;
            if (string.IsNullOrEmpty(tabKey)) return Visibility.Visible;

            // values[0] = VisibleTabs dictionary (for change notification)
            // We still use SettingsManager.Current for the actual lookup
            var settings = Services.SettingsManager.Current;
            return settings.IsTabVisible(tabKey) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    
    /// <summary>
    /// Shows element only when output format is JPEG/JPG.
    /// </summary>
    public class JpegVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var format = value as string;
            if (string.IsNullOrEmpty(format)) return Visibility.Collapsed;
            
            return format.Equals("jpg", StringComparison.OrdinalIgnoreCase) || 
                   format.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts boolean connection state to background brush (green = connected, red = disconnected)
    /// </summary>
    public class BoolToConnectionBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isConnected = value is bool b && b;
            return new SolidColorBrush(isConnected ? Color.FromRgb(76, 175, 80) : Color.FromRgb(158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts boolean connection state to status text
    /// </summary>
    public class BoolToConnectionTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isConnected = value is bool b && b;
            return isConnected ? "Connected" : "Disconnected";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts boolean IsFavorite to filled/empty star icon
    /// </summary>
    public class BoolToFavoriteIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isFavorite = value is bool b && b;
            return isFavorite ? "★" : "☆";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
