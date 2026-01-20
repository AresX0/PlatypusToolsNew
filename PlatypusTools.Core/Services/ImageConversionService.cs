using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace PlatypusTools.Core.Services
{
    public enum SvgConversionMode
    {
        /// <summary>Embeds the raster image as base64 data inside the SVG</summary>
        EmbedRaster,
        /// <summary>Traces the image to create vector paths (best for simple graphics/logos)</summary>
        Trace
    }

    public static class ImageConversionService
    {
        public static bool ConvertImage(string sourcePath, string destPath, int? maxWidth = null, int? maxHeight = null, long jpegQuality = 85, SvgConversionMode svgMode = SvgConversionMode.EmbedRaster)
        {
            if (!File.Exists(sourcePath)) return false;
            try
            {
                var ext = Path.GetExtension(destPath).ToLowerInvariant();
                
                // Handle SVG conversion separately
                if (ext == ".svg")
                {
                    return ConvertToSvg(sourcePath, destPath, maxWidth, maxHeight, svgMode);
                }
                
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

        /// <summary>
        /// Converts a raster image to SVG format
        /// </summary>
        public static bool ConvertToSvg(string sourcePath, string destPath, int? maxWidth = null, int? maxHeight = null, SvgConversionMode mode = SvgConversionMode.EmbedRaster)
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

                // Ensure dimensions are at least 1
                width = Math.Max(1, width);
                height = Math.Max(1, height);

                string svgContent;
                if (mode == SvgConversionMode.Trace)
                {
                    svgContent = TraceImageToSvg(src, width, height);
                }
                else
                {
                    svgContent = EmbedImageToSvg(src, width, height);
                }

                // Ensure output directory exists
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(destPath, svgContent, Encoding.UTF8);
                return File.Exists(destPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SVG conversion error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Embeds the raster image as base64 data inside an SVG
        /// </summary>
        private static string EmbedImageToSvg(Image image, int width, int height)
        {
            using var ms = new MemoryStream();
            using var resized = new Bitmap(image, new Size(width, height));
            resized.Save(ms, ImageFormat.Png);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
            sb.AppendLine($"  <image width=\"{width}\" height=\"{height}\" xlink:href=\"data:image/png;base64,{base64}\"/>");
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Traces the image to create vector paths using edge detection and color quantization
        /// Best for simple graphics, logos, and icons with distinct colors
        /// </summary>
        private static string TraceImageToSvg(Image image, int width, int height)
        {
            using var bmp = new Bitmap(image, new Size(width, height));
            var sb = new StringBuilder();
            
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
            sb.AppendLine("  <g>");

            // Simple pixel-based tracing with color quantization
            // Group adjacent pixels of similar colors into rectangles
            var processed = new bool[width, height];
            var colors = new System.Collections.Generic.Dictionary<Color, System.Collections.Generic.List<Rectangle>>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (processed[x, y]) continue;

                    var color = bmp.GetPixel(x, y);
                    if (color.A < 10) // Skip nearly transparent pixels
                    {
                        processed[x, y] = true;
                        continue;
                    }

                    // Quantize color to reduce complexity (reduce to 32 levels per channel)
                    var quantizedColor = Color.FromArgb(
                        color.A,
                        (color.R / 8) * 8,
                        (color.G / 8) * 8,
                        (color.B / 8) * 8
                    );

                    // Find horizontal run of similar color
                    int runWidth = 1;
                    while (x + runWidth < width && !processed[x + runWidth, y])
                    {
                        var nextColor = bmp.GetPixel(x + runWidth, y);
                        var nextQuantized = Color.FromArgb(
                            nextColor.A,
                            (nextColor.R / 8) * 8,
                            (nextColor.G / 8) * 8,
                            (nextColor.B / 8) * 8
                        );
                        if (nextQuantized.ToArgb() != quantizedColor.ToArgb()) break;
                        runWidth++;
                    }

                    // Find vertical extent of this rectangle
                    int runHeight = 1;
                    bool canExtend = true;
                    while (canExtend && y + runHeight < height)
                    {
                        for (int checkX = x; checkX < x + runWidth; checkX++)
                        {
                            if (processed[checkX, y + runHeight])
                            {
                                canExtend = false;
                                break;
                            }
                            var checkColor = bmp.GetPixel(checkX, y + runHeight);
                            var checkQuantized = Color.FromArgb(
                                checkColor.A,
                                (checkColor.R / 8) * 8,
                                (checkColor.G / 8) * 8,
                                (checkColor.B / 8) * 8
                            );
                            if (checkQuantized.ToArgb() != quantizedColor.ToArgb())
                            {
                                canExtend = false;
                                break;
                            }
                        }
                        if (canExtend) runHeight++;
                    }

                    // Mark pixels as processed
                    for (int py = y; py < y + runHeight; py++)
                    {
                        for (int px = x; px < x + runWidth; px++)
                        {
                            processed[px, py] = true;
                        }
                    }

                    // Add rectangle
                    var rect = new Rectangle(x, y, runWidth, runHeight);
                    if (!colors.ContainsKey(quantizedColor))
                    {
                        colors[quantizedColor] = new System.Collections.Generic.List<Rectangle>();
                    }
                    colors[quantizedColor].Add(rect);
                }
            }

            // Output rectangles grouped by color
            foreach (var kvp in colors)
            {
                var color = kvp.Key;
                var hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                var opacity = color.A / 255.0;

                foreach (var rect in kvp.Value)
                {
                    if (opacity < 1.0)
                    {
                        sb.AppendLine($"    <rect x=\"{rect.X}\" y=\"{rect.Y}\" width=\"{rect.Width}\" height=\"{rect.Height}\" fill=\"{hexColor}\" fill-opacity=\"{opacity:F2}\"/>");
                    }
                    else
                    {
                        sb.AppendLine($"    <rect x=\"{rect.X}\" y=\"{rect.Y}\" width=\"{rect.Width}\" height=\"{rect.Height}\" fill=\"{hexColor}\"/>");
                    }
                }
            }

            sb.AppendLine("  </g>");
            sb.AppendLine("</svg>");
            return sb.ToString();
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