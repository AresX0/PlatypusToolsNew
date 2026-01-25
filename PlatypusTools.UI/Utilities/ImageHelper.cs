using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.Utilities
{
    /// <summary>
    /// Helper class for memory-efficient image loading and caching.
    /// Implements best practices for WPF BitmapImage to avoid memory leaks.
    /// </summary>
    public static class ImageHelper
    {
        /// <summary>
        /// Loads a BitmapImage from file with memory-efficient settings.
        /// Uses CacheOption.OnLoad and Freeze() to prevent memory leaks.
        /// </summary>
        /// <param name="filePath">Path to the image file</param>
        /// <param name="decodePixelWidth">Optional width to decode to (saves memory for thumbnails)</param>
        /// <param name="decodePixelHeight">Optional height to decode to</param>
        /// <returns>Frozen BitmapImage or null if failed</returns>
        public static BitmapImage? LoadFromFile(string filePath, int? decodePixelWidth = null, int? decodePixelHeight = null)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load into memory, release file handle
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; // Faster loading
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                
                if (decodePixelWidth.HasValue)
                    bitmap.DecodePixelWidth = decodePixelWidth.Value;
                if (decodePixelHeight.HasValue)
                    bitmap.DecodePixelHeight = decodePixelHeight.Value;
                
                bitmap.EndInit();
                bitmap.Freeze(); // Make cross-thread accessible and reduce memory
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Loads a BitmapImage from a stream with memory-efficient settings.
        /// </summary>
        /// <param name="stream">Stream containing image data (must be seekable)</param>
        /// <param name="decodePixelWidth">Optional width to decode to</param>
        /// <returns>Frozen BitmapImage or null if failed</returns>
        public static BitmapImage? LoadFromStream(Stream stream, int? decodePixelWidth = null)
        {
            if (stream == null || !stream.CanSeek)
                return null;

            try
            {
                stream.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                
                if (decodePixelWidth.HasValue)
                    bitmap.DecodePixelWidth = decodePixelWidth.Value;
                
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Loads a thumbnail-sized image for preview purposes.
        /// Uses small decode size to minimize memory usage.
        /// </summary>
        /// <param name="filePath">Path to the image file</param>
        /// <param name="maxSize">Maximum dimension (width or height)</param>
        /// <returns>Frozen BitmapImage thumbnail or null if failed</returns>
        public static BitmapImage? LoadThumbnail(string filePath, int maxSize = 150)
        {
            return LoadFromFile(filePath, decodePixelWidth: maxSize);
        }

        /// <summary>
        /// Loads a BitmapImage from file, ignoring the image cache.
        /// Use this for images that may change at the same path (e.g., extracted video frames).
        /// </summary>
        /// <param name="filePath">Path to the image file</param>
        /// <returns>Frozen BitmapImage or null if failed</returns>
        public static BitmapImage? LoadUncached(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a System.Drawing.Bitmap to a frozen WPF BitmapImage.
        /// Properly disposes the source bitmap's stream after conversion.
        /// </summary>
        /// <param name="bitmap">Source System.Drawing.Bitmap</param>
        /// <param name="decodePixelWidth">Optional width to decode to</param>
        /// <returns>Frozen BitmapImage or null if failed</returns>
        public static BitmapImage? FromDrawingBitmap(System.Drawing.Bitmap? bitmap, int? decodePixelWidth = null)
        {
            if (bitmap == null)
                return null;

            try
            {
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                return LoadFromStream(ms, decodePixelWidth);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a System.Drawing.Icon to a frozen WPF BitmapImage.
        /// </summary>
        /// <param name="icon">Source System.Drawing.Icon</param>
        /// <returns>Frozen BitmapImage or null if failed</returns>
        public static BitmapImage? FromIcon(System.Drawing.Icon? icon)
        {
            if (icon == null)
                return null;

            try
            {
                using var bmp = icon.ToBitmap();
                return FromDrawingBitmap(bmp);
            }
            catch
            {
                return null;
            }
        }
    }
}
