using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PlatypusTools.UI.Converters
{
    /// <summary>
    /// Converts TimeSpan to pixel position on timeline.
    /// </summary>
    public class TimeToPixelConverter : IValueConverter
    {
        /// <summary>
        /// Pixels per second at zoom level 1.0.
        /// </summary>
        public static double PixelsPerSecond { get; set; } = 100;

        /// <summary>
        /// Current zoom level (1.0 = 100%).
        /// </summary>
        public static double ZoomLevel { get; set; } = 1.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan time)
            {
                return time.TotalSeconds * PixelsPerSecond * ZoomLevel;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double pixels)
            {
                var seconds = pixels / (PixelsPerSecond * ZoomLevel);
                return TimeSpan.FromSeconds(seconds);
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Converts TimeSpan duration to width in pixels (simple version).
    /// </summary>
    public class SimpleDurationToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan duration)
            {
                var width = duration.TotalSeconds * TimeToPixelConverter.PixelsPerSecond * TimeToPixelConverter.ZoomLevel;
                return Math.Max(width, 20); // Minimum width
            }
            return 20.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                var seconds = width / (TimeToPixelConverter.PixelsPerSecond * TimeToPixelConverter.ZoomLevel);
                return TimeSpan.FromSeconds(seconds);
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Converts boolean to play/pause icon.
    /// </summary>
    public class PlayPauseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlaying)
            {
                return isPlaying ? "⏸" : "▶";
            }
            return "▶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Multiplies a value by a factor.
    /// </summary>
    public class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && parameter is string ps && double.TryParse(ps, out var factor))
            {
                return d * factor;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && parameter is string ps && double.TryParse(ps, out var factor) && factor != 0)
            {
                return d / factor;
            }
            return value;
        }
    }

    /// <summary>
    /// Formats TimeSpan to string.
    /// </summary>
    public class TimeSpanFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan ts)
            {
                var format = parameter as string ?? @"hh\:mm\:ss\.ff";
                return ts.ToString(format);
            }
            return "00:00:00.00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && TimeSpan.TryParse(s, out var ts))
            {
                return ts;
            }
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Converts enum value to description or display name.
    /// </summary>
    public class EnumDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            
            var type = value.GetType();
            if (!type.IsEnum) return value.ToString() ?? string.Empty;

            var name = Enum.GetName(type, value);
            if (name == null) return value.ToString() ?? string.Empty;

            var field = type.GetField(name);
            if (field == null) return name;

            var attr = (System.ComponentModel.DescriptionAttribute?)Attribute.GetCustomAttribute(
                field, typeof(System.ComponentModel.DescriptionAttribute));

            return attr?.Description ?? name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Returns whether value equals parameter.
    /// </summary>
    public class EqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Equals(value, parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return parameter;
            }
            return Binding.DoNothing;
        }
    }
    
    /// <summary>
    /// Converts boolean to height for timeline ruler markers (major=12, minor=6).
    /// </summary>
    public class BoolToHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isMajor)
            {
                return isMajor ? 12.0 : 6.0;
            }
            return 6.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts boolean to Brush color (true = green/success, false = orange/warning).
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSuccess)
            {
                return isSuccess 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)) // Green
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)); // Orange/Warning
            }
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB)); // Gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts TrackType to background color (Shotcut-style: darker shades for different track types).
    /// </summary>
    public class TrackTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PlatypusTools.Core.Models.Video.TrackType trackType)
            {
                return trackType switch
                {
                    PlatypusTools.Core.Models.Video.TrackType.Video => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x45, 0x4C)), // Darker slate
                    PlatypusTools.Core.Models.Video.TrackType.Audio => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0x3D, 0x44)), // Slightly lighter
                    PlatypusTools.Core.Models.Video.TrackType.Overlay => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x45, 0x4D, 0x54)), // Lightest
                    _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B, 0x50, 0x58))
                };
            }
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B, 0x50, 0x58)); // Default gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts boolean IsLocked to opacity (locked = 0.5, unlocked = 1.0).
    /// </summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isLocked)
            {
                return isLocked ? 0.6 : 1.0;
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts boolean IsMuted to tooltip text.
    /// </summary>
    public class BoolToMuteTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isMuted)
            {
                return isMuted ? "Unmute track" : "Mute track";
            }
            return "Mute track";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts boolean IsLocked to tooltip text.
    /// </summary>
    public class BoolToLockTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isLocked)
            {
                return isLocked ? "Unlock track" : "Lock track";
            }
            return "Lock track";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    /// <summary>
    /// Converts boolean IsVisible to tooltip text.
    /// </summary>
    public class BoolToVisibilityTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isVisible)
            {
                return isVisible ? "Hide track" : "Show track";
            }
            return "Hide track";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
