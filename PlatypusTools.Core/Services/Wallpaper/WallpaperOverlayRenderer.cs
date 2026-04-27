using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlatypusTools.Core.Services.Wallpaper
{
    /// <summary>
    /// Resizes images to screen size and burns a multi-line text overlay onto the top-right corner.
    /// </summary>
    public static class WallpaperOverlayRenderer
    {
        /// <summary>
        /// Loads an image, fits/fills/stretches/centers it to the target size, optionally burns an overlay,
        /// and saves the result to the given output path. Returns true on success.
        /// </summary>
        public static bool Render(
            string inputPath,
            string outputPath,
            int screenWidth,
            int screenHeight,
            string fitMode,
            IReadOnlyList<string>? overlayLines,
            double overlayOpacity)
        {
            try
            {
                using var img = Image.Load<Rgba32>(inputPath);
                using var canvas = FitImage(img, screenWidth, screenHeight, fitMode);

                if (overlayLines is { Count: > 0 })
                    BurnOverlay(canvas, overlayLines, overlayOpacity);

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                canvas.SaveAsBmp(outputPath); // BMP — accepted by SystemParametersInfo on every Windows
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Image<Rgba32> FitImage(Image<Rgba32> src, int sw, int sh, string fitMode)
        {
            var canvas = new Image<Rgba32>(sw, sh, new Rgba32(0, 0, 0, 255));

            switch ((fitMode ?? "fit").ToLowerInvariant())
            {
                case "stretch":
                {
                    var resized = src.Clone(c => c.Resize(sw, sh));
                    canvas.Mutate(c => c.DrawImage(resized, new Point(0, 0), 1f));
                    break;
                }
                case "fill":
                {
                    double ratio = Math.Max((double)sw / src.Width, (double)sh / src.Height);
                    int nw = (int)(src.Width * ratio);
                    int nh = (int)(src.Height * ratio);
                    var resized = src.Clone(c => c.Resize(nw, nh));
                    int x = -(nw - sw) / 2;
                    int y = -(nh - sh) / 2;
                    canvas.Mutate(c => c.DrawImage(resized, new Point(x, y), 1f));
                    break;
                }
                case "center":
                {
                    int x = (sw - src.Width) / 2;
                    int y = (sh - src.Height) / 2;
                    canvas.Mutate(c => c.DrawImage(src, new Point(x, y), 1f));
                    break;
                }
                default: // "fit"
                {
                    double ratio = Math.Min((double)sw / src.Width, (double)sh / src.Height);
                    int nw = Math.Max(1, (int)(src.Width * ratio));
                    int nh = Math.Max(1, (int)(src.Height * ratio));
                    var resized = src.Clone(c => c.Resize(nw, nh));
                    int x = (sw - nw) / 2;
                    int y = (sh - nh) / 2;
                    canvas.Mutate(c => c.DrawImage(resized, new Point(x, y), 1f));
                    break;
                }
            }

            return canvas;
        }

        private static void BurnOverlay(Image<Rgba32> img, IReadOnlyList<string> lines, double opacity)
        {
            int w = img.Width, h = img.Height;
            var family = ResolveMonoFamily();
            var font = family.CreateFont(Math.Max(12f, h / 70f), FontStyle.Regular);
            var titleFont = family.CreateFont(Math.Max(14f, h / 60f), FontStyle.Bold);

            int padding = 16;
            int lineHeight = (int)Math.Max(20f, h / 50f);
            int maxVisible = Math.Max(1, (h - 60) / lineHeight);

            var visible = lines.Count > maxVisible
                ? new List<string>(lines).GetRange(0, maxVisible)
                : new List<string>(lines);

            // Measure widest line
            float maxTextW = 0f;
            foreach (var line in visible)
            {
                var f = line.StartsWith("═") ? titleFont : font;
                var size = TextMeasurer.MeasureSize(line, new TextOptions(f));
                if (size.Width > maxTextW) maxTextW = size.Width;
            }

            int boxW = (int)maxTextW + padding * 2;
            int boxH = visible.Count * lineHeight + padding * 2;
            int xRight = w - 20;
            int yTop = 20;
            int boxX = xRight - boxW;

            byte alpha = (byte)Math.Clamp((int)(255 * opacity), 0, 255);
            var bg = new Rgba32(10, 10, 30, alpha);
            var border = new Rgba32(80, 120, 200, alpha);

            img.Mutate(ctx =>
            {
                var rect = new SixLabors.ImageSharp.Drawing.RectangularPolygon(boxX, yTop, boxW, boxH);
                ctx.Fill(bg, rect);
                ctx.Draw(border, 1f, rect);

                int y = yTop + padding;
                foreach (var line in visible)
                {
                    var (color, f) = StyleFor(line, font, titleFont);
                    ctx.DrawText(line, f, color, new PointF(boxX + padding, y));
                    y += lineHeight;
                }
            });
        }

        private static (Color, Font) StyleFor(string line, Font normal, Font title)
        {
            if (line.StartsWith("═")) return (Color.FromRgb(0x4F, 0xC3, 0xF7), title);
            if (line.StartsWith("✓")) return (Color.FromRgb(0x66, 0xBB, 0x6A), normal);
            if (line.StartsWith("●")) return (Color.FromRgb(0xFF, 0xA7, 0x26), normal);
            if (line.StartsWith("◇")) return (Color.FromRgb(0x90, 0xCA, 0xF9), normal);
            if (line.StartsWith("▸")) return (Color.FromRgb(0xCE, 0x93, 0xD8), normal);
            return (Color.FromRgb(0xE0, 0xE0, 0xE0), normal);
        }

        private static FontFamily _monoFamily;
        private static bool _monoResolved;
        private static FontFamily ResolveMonoFamily()
        {
            if (_monoResolved) return _monoFamily;

            string[] candidates = { "Consolas", "Cascadia Mono", "Lucida Console", "Courier New", "DejaVu Sans Mono" };
            foreach (var name in candidates)
            {
                if (SystemFonts.TryGet(name, out var family))
                {
                    _monoFamily = family;
                    _monoResolved = true;
                    return _monoFamily;
                }
            }

            // Last resort: any installed family
            foreach (var family in SystemFonts.Families)
            {
                _monoFamily = family;
                _monoResolved = true;
                return _monoFamily;
            }

            throw new InvalidOperationException("No system fonts available for overlay rendering.");
        }
    }
}
