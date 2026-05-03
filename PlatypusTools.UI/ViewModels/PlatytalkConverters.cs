using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PlatypusTools.Core.Services.Platytalk;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>Static singleton converters consumed by PlatytalkView.xaml.</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBoolToVisibilityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Collapsed;
    }

    public sealed class DirectionToAlignmentConverter : IValueConverter
    {
        public static readonly DirectionToAlignmentConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is MessageDirection.Outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class DirectionToBrushConverter : IValueConverter
    {
        public static readonly DirectionToBrushConverter Instance = new();
        private static readonly Brush Outgoing = new SolidColorBrush(Color.FromRgb(0x12, 0x2A, 0x44));
        private static readonly Brush Incoming = new SolidColorBrush(Color.FromRgb(0x10, 0x1B, 0x29));
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is MessageDirection.Outgoing ? Outgoing : Incoming;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
