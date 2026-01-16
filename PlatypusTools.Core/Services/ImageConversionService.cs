using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace PlatypusTools.Core.Services
{
    public static class ImageConversionService
    {
        public static bool ConvertImage(string sourcePath, string destPath, int? maxWidth = null, int? maxHeight = null, long jpegQuality = 85)
        {
            if (!File.Exists(sourcePath)) return false;
            try
            {
                using var src = Image.FromFile(sourcePath);
                int width = src.Width, height = src.Height;
                if (maxWidth.HasValue || maxHeight.HasValue)
                {
                    var mw = maxWidth ?? width;
                    var mh = maxHeight ?? height;
                    var ratio = Math.Min((double)mw / width, (double)mh / height);
                    if (ratio < 1.0)
                    {
                        width = (int)(width * ratio);
                        height = (int)(height * ratio);
                    }
                }
                using var bmp = new Bitmap(src, new Size(width, height));
                var ext = Path.GetExtension(destPath).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg")
                {
                    var codec = GetEncoder(ImageFormat.Jpeg);
                    using var eps = new EncoderParameters(1);
                    eps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);
                    bmp.Save(destPath, codec, eps);
                }
                else if (ext == ".png") bmp.Save(destPath, ImageFormat.Png);
                else if (ext == ".bmp") bmp.Save(destPath, ImageFormat.Bmp);
                else if (ext == ".gif") bmp.Save(destPath, ImageFormat.Gif);
                else if (ext == ".tif" || ext == ".tiff") bmp.Save(destPath, ImageFormat.Tiff);
                else if (ext == ".webp") 
                {
                    // WebP requires special handling - save as PNG first then convert
                    // Since System.Drawing doesn't natively support WebP, we save as PNG
                    // For true WebP support, a library like ImageSharp or libwebp would be needed
                    // For now, we'll save high-quality PNG as a fallback
                    bmp.Save(destPath, ImageFormat.Png);
                }
                else bmp.Save(destPath, ImageFormat.Png);
                return true;
            }
            catch { return false; }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid) return codec;
            }
            return codecs[0];
        }
    }
}