using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services;

/// <summary>
/// Service for capturing screenshots with various modes and annotations.
/// </summary>
public class ScreenCaptureService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Captures the entire screen (all monitors).
    /// </summary>
    /// <returns>Bitmap of the full screen.</returns>
    public Bitmap CaptureFullScreen()
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        
        return bitmap;
    }

    /// <summary>
    /// Captures the primary monitor.
    /// </summary>
    /// <returns>Bitmap of the primary monitor.</returns>
    public Bitmap CapturePrimaryScreen()
    {
        int width = GetSystemMetrics(SM_CXSCREEN);
        int height = GetSystemMetrics(SM_CYSCREEN);
        
        if (width <= 0 || height <= 0)
            return CaptureFullScreen();
            
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        
        return bitmap;
    }

    /// <summary>
    /// Captures the currently active window.
    /// </summary>
    /// <returns>Bitmap of the active window.</returns>
    public Bitmap CaptureActiveWindow()
    {
        IntPtr hWnd = GetForegroundWindow();
        
        if (hWnd == IntPtr.Zero)
            return CaptureFullScreen();
        
        if (!GetWindowRect(hWnd, out RECT rect))
            return CaptureFullScreen();
        
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        
        if (width <= 0 || height <= 0)
            return CaptureFullScreen();
        
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        
        return bitmap;
    }

    /// <summary>
    /// Captures a specific region of the screen.
    /// </summary>
    /// <param name="region">The region to capture.</param>
    /// <returns>Bitmap of the region.</returns>
    public Bitmap CaptureRegion(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentException("Invalid region dimensions");
        
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        
        return bitmap;
    }

    /// <summary>
    /// Saves a screenshot to a file.
    /// </summary>
    /// <param name="bitmap">The screenshot bitmap.</param>
    /// <param name="filePath">Output file path.</param>
    /// <param name="format">Image format.</param>
    public void SaveScreenshot(Bitmap bitmap, string filePath, ImageFormat? format = null)
    {
        format ??= GetFormatFromExtension(filePath);
        
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        
        if (format.Equals(ImageFormat.Jpeg))
        {
            var encoder = GetEncoder(ImageFormat.Jpeg);
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
            bitmap.Save(filePath, encoder, encoderParams);
        }
        else
        {
            bitmap.Save(filePath, format);
        }
    }

    /// <summary>
    /// Generates a default filename for a screenshot.
    /// </summary>
    /// <param name="prefix">Filename prefix.</param>
    /// <param name="extension">File extension.</param>
    /// <returns>Generated filename.</returns>
    public string GenerateFilename(string prefix = "Screenshot", string extension = ".png")
    {
        return $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{extension}";
    }

    private ImageFormat GetFormatFromExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".bmp" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".tiff" or ".tif" => ImageFormat.Tiff,
            _ => ImageFormat.Png
        };
    }

    private ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        return codecs[0];
    }

    #region Scrolling Capture

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(System.Drawing.Point Point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

    private const int WM_VSCROLL = 0x0115;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int SB_PAGEDOWN = 3;
    private const int SB_BOTTOM = 7;
    private const int SB_VERT = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    private const uint SIF_ALL = 0x17;

    /// <summary>
    /// Captures a scrolling window by taking multiple screenshots and stitching them together.
    /// </summary>
    /// <returns>A stitched bitmap of the full scrolling content, or null if cancelled.</returns>
    public async Task<Bitmap?> CaptureScrollingWindowAsync()
    {
        // Give user time to select/click the target window
        await Task.Delay(1000);

        // Get the foreground window (the one the user clicked on)
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return null;

        if (!GetWindowRect(hWnd, out RECT windowRect))
            return null;

        int windowWidth = windowRect.Right - windowRect.Left;
        int windowHeight = windowRect.Bottom - windowRect.Top;

        if (windowWidth <= 0 || windowHeight <= 0)
            return null;

        // Get client area for better capture
        if (!GetClientRect(hWnd, out RECT clientRect))
            return null;

        var clientOrigin = new System.Drawing.Point(0, 0);
        ClientToScreen(hWnd, ref clientOrigin);

        int clientWidth = clientRect.Right - clientRect.Left;
        int clientHeight = clientRect.Bottom - clientRect.Top;

        // Capture screenshots while scrolling
        var screenshots = new List<Bitmap>();
        var overlapHeight = clientHeight / 8; // 12.5% overlap for better stitching
        int maxScrolls = 50; // Safety limit
        int scrollCount = 0;

        // Capture initial screenshot
        await Task.Delay(100);
        var initial = CaptureRegion(new Rectangle(clientOrigin.X, clientOrigin.Y, clientWidth, clientHeight));
        screenshots.Add(initial);

        // Get initial scroll position
        var scrollInfo = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
        GetScrollInfo(hWnd, SB_VERT, ref scrollInfo);
        int lastScrollPos = scrollInfo.nPos;
        int maxScroll = scrollInfo.nMax - (int)scrollInfo.nPage;

        while (scrollCount < maxScrolls)
        {
            // Scroll down using mouse wheel simulation (more universal)
            SendMessage(hWnd, WM_MOUSEWHEEL, -120 * 3 << 16, 0); // Scroll down 3 notches
            await Task.Delay(200); // Wait for scroll animation

            // Check new scroll position
            scrollInfo = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            GetScrollInfo(hWnd, SB_VERT, ref scrollInfo);

            // Check if we've reached the bottom or no scroll happened
            if (scrollInfo.nPos <= lastScrollPos || scrollInfo.nPos >= maxScroll)
            {
                // Capture final screenshot
                var finalShot = CaptureRegion(new Rectangle(clientOrigin.X, clientOrigin.Y, clientWidth, clientHeight));
                screenshots.Add(finalShot);
                break;
            }

            lastScrollPos = scrollInfo.nPos;
            scrollCount++;

            // Capture this section
            var screenshot = CaptureRegion(new Rectangle(clientOrigin.X, clientOrigin.Y, clientWidth, clientHeight));
            screenshots.Add(screenshot);
        }

        if (screenshots.Count == 0)
            return null;

        if (screenshots.Count == 1)
            return screenshots[0];

        // Stitch the screenshots together
        return StitchScreenshots(screenshots, overlapHeight);
    }

    private Bitmap StitchScreenshots(List<Bitmap> screenshots, int overlapHeight)
    {
        if (screenshots.Count == 0)
            throw new ArgumentException("No screenshots to stitch");

        if (screenshots.Count == 1)
            return screenshots[0];

        int width = screenshots[0].Width;
        
        // Calculate total height accounting for overlap
        int effectiveHeightPerShot = screenshots[0].Height - overlapHeight;
        int totalHeight = screenshots[0].Height + (screenshots.Count - 1) * effectiveHeightPerShot;

        var result = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);
        int currentY = 0;
        
        using (var graphics = Graphics.FromImage(result))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < screenshots.Count; i++)
            {
                var shot = screenshots[i];
                
                if (i == 0)
                {
                    // First screenshot: draw entirely
                    graphics.DrawImage(shot, 0, 0);
                    currentY = shot.Height - overlapHeight;
                }
                else
                {
                    // Skip the overlapping portion at the top
                    var srcRect = new Rectangle(0, overlapHeight, shot.Width, shot.Height - overlapHeight);
                    var destRect = new Rectangle(0, currentY, shot.Width, shot.Height - overlapHeight);
                    graphics.DrawImage(shot, destRect, srcRect, GraphicsUnit.Pixel);
                    currentY += shot.Height - overlapHeight;
                }
            }
        }

        // Dispose original screenshots
        foreach (var shot in screenshots)
        {
            shot.Dispose();
        }

        // Crop to actual content height
        int finalHeight = Math.Min(currentY, totalHeight);
        var croppedResult = new Bitmap(width, finalHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(croppedResult))
        {
            g.DrawImage(result, new Rectangle(0, 0, width, finalHeight), 
                        new Rectangle(0, 0, width, finalHeight), GraphicsUnit.Pixel);
        }
        result.Dispose();

        return croppedResult;
    }

    #endregion
}

/// <summary>
/// Service for annotating screenshots.
/// </summary>
public class ScreenshotAnnotationService
{
    /// <summary>
    /// Draws an arrow annotation on the bitmap.
    /// </summary>
    public void DrawArrow(Bitmap bitmap, System.Drawing.Point start, System.Drawing.Point end, Color color, int thickness = 3)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        
        using var pen = new Pen(color, thickness);
        pen.CustomEndCap = new AdjustableArrowCap(5, 5);
        
        graphics.DrawLine(pen, start, end);
    }

    /// <summary>
    /// Draws a rectangle annotation on the bitmap.
    /// </summary>
    public void DrawRectangle(Bitmap bitmap, Rectangle rect, Color color, int thickness = 3, bool filled = false)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        
        if (filled)
        {
            using var brush = new SolidBrush(Color.FromArgb(50, color));
            graphics.FillRectangle(brush, rect);
        }
        
        using var pen = new Pen(color, thickness);
        graphics.DrawRectangle(pen, rect);
    }

    /// <summary>
    /// Draws an ellipse annotation on the bitmap.
    /// </summary>
    public void DrawEllipse(Bitmap bitmap, Rectangle bounds, Color color, int thickness = 3, bool filled = false)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        
        if (filled)
        {
            using var brush = new SolidBrush(Color.FromArgb(50, color));
            graphics.FillEllipse(brush, bounds);
        }
        
        using var pen = new Pen(color, thickness);
        graphics.DrawEllipse(pen, bounds);
    }

    /// <summary>
    /// Draws text annotation on the bitmap.
    /// </summary>
    public void DrawText(Bitmap bitmap, string text, System.Drawing.Point location, Color color, string fontFamily = "Arial", float fontSize = 14)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        
        using var font = new Font(fontFamily, fontSize, System.Drawing.FontStyle.Regular);
        using var brush = new SolidBrush(color);
        
        graphics.DrawString(text, font, brush, location);
    }

    /// <summary>
    /// Draws a highlight (semi-transparent rectangle) on the bitmap.
    /// </summary>
    public void DrawHighlight(Bitmap bitmap, Rectangle rect, Color color)
    {
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(Color.FromArgb(100, color));
        
        graphics.FillRectangle(brush, rect);
    }

    /// <summary>
    /// Applies a blur/pixelate effect to a region.
    /// </summary>
    public void BlurRegion(Bitmap bitmap, Rectangle region, int pixelSize = 10)
    {
        if (region.Width <= 0 || region.Height <= 0) return;
        
        // Clamp region to bitmap bounds
        region = Rectangle.Intersect(region, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (region.IsEmpty) return;
        
        for (int y = region.Top; y < region.Bottom; y += pixelSize)
        {
            for (int x = region.Left; x < region.Right; x += pixelSize)
            {
                int blockWidth = Math.Min(pixelSize, region.Right - x);
                int blockHeight = Math.Min(pixelSize, region.Bottom - y);
                
                // Get average color of block
                int r = 0, g = 0, b = 0;
                int count = 0;
                
                for (int by = 0; by < blockHeight; by++)
                {
                    for (int bx = 0; bx < blockWidth; bx++)
                    {
                        var pixel = bitmap.GetPixel(x + bx, y + by);
                        r += pixel.R;
                        g += pixel.G;
                        b += pixel.B;
                        count++;
                    }
                }
                
                if (count > 0)
                {
                    var avgColor = Color.FromArgb(r / count, g / count, b / count);
                    
                    // Fill block with average color
                    for (int by = 0; by < blockHeight; by++)
                    {
                        for (int bx = 0; bx < blockWidth; bx++)
                        {
                            bitmap.SetPixel(x + bx, y + by, avgColor);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws a freehand line on the bitmap.
    /// </summary>
    public void DrawFreehand(Bitmap bitmap, System.Drawing.Point[] points, Color color, int thickness = 3)
    {
        if (points.Length < 2) return;
        
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        
        using var pen = new Pen(color, thickness);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        pen.LineJoin = LineJoin.Round;
        
        graphics.DrawLines(pen, points);
    }

    /// <summary>
    /// Crops the bitmap to the specified region.
    /// </summary>
    public Bitmap Crop(Bitmap bitmap, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (region.IsEmpty)
            throw new ArgumentException("Invalid crop region");
        
        var cropped = new Bitmap(region.Width, region.Height, bitmap.PixelFormat);
        
        using var graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(bitmap, 0, 0, region, GraphicsUnit.Pixel);
        
        return cropped;
    }
}
