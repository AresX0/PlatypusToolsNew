using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlatypusTools.UI.Services.RemoteDesktop;

/// <summary>
/// Captures the screen contents as JPEG-encoded byte arrays.
/// Uses GDI BitBlt for screen capture (compatible with all Windows versions).
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    // ── P/Invoke ──
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon,
        int cxWidth, int cyHeight, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CURSORINFO_SIZE = 20 + 8; // sizeof(CURSORINFO) on x64 — includes POINT(8 bytes) and padding
    private const int CURSOR_SHOWING = 0x00000001;
    private const uint DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // ── Cached encoder ──
    private readonly ImageCodecInfo _jpegCodec;
    private int _quality = 50;

    /// <summary>JPEG quality (1-100). Higher = better quality, larger frames.</summary>
    public int Quality
    {
        get => _quality;
        set => _quality = Math.Clamp(value, 1, 100);
    }

    /// <summary>Whether to draw the cursor in the captured image.</summary>
    public bool ShowCursor { get; set; } = true;

    /// <summary>Index of the monitor to capture (0-based). -1 = virtual screen (all monitors).</summary>
    public int MonitorIndex { get; set; } = -1;

    public ScreenCaptureService()
    {
        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }

    /// <summary>
    /// Returns information about all monitors.
    /// </summary>
    public List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            monitors.Add(new MonitorInfo
            {
                Index = i,
                Name = screens[i].DeviceName,
                X = screens[i].Bounds.X,
                Y = screens[i].Bounds.Y,
                Width = screens[i].Bounds.Width,
                Height = screens[i].Bounds.Height,
                IsPrimary = screens[i].Primary
            });
        }
        return monitors;
    }

    /// <summary>
    /// Gets the capture bounds for the current monitor setting.
    /// </summary>
    public Rectangle GetCaptureBounds()
    {
        if (MonitorIndex < 0 || MonitorIndex >= Screen.AllScreens.Length)
        {
            // Virtual screen (all monitors combined)
            return SystemInformation.VirtualScreen;
        }
        return Screen.AllScreens[MonitorIndex].Bounds;
    }

    /// <summary>
    /// Captures the screen and returns JPEG-encoded bytes.
    /// Thread-safe — can be called from any thread.
    /// </summary>
    public byte[] CaptureFrame()
    {
        var bounds = GetCaptureBounds();
        var hDesktopDC = GetDC(IntPtr.Zero);
        var hMemDC = CreateCompatibleDC(hDesktopDC);
        var hBitmap = CreateCompatibleBitmap(hDesktopDC, bounds.Width, bounds.Height);
        var hOldBitmap = SelectObject(hMemDC, hBitmap);

        try
        {
            // Copy screen pixels
            BitBlt(hMemDC, 0, 0, bounds.Width, bounds.Height,
                hDesktopDC, bounds.X, bounds.Y, SRCCOPY);

            // Draw cursor if enabled
            if (ShowCursor)
            {
                DrawCursorOnDC(hMemDC, bounds);
            }

            // Convert HBITMAP to System.Drawing.Bitmap
            using var bitmap = Image.FromHbitmap(hBitmap);

            // Encode to JPEG
            using var ms = new MemoryStream();
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, (long)_quality);
            bitmap.Save(ms, _jpegCodec, encoderParams);
            return ms.ToArray();
        }
        finally
        {
            SelectObject(hMemDC, hOldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(hMemDC);
            ReleaseDC(IntPtr.Zero, hDesktopDC);
        }
    }

    /// <summary>
    /// Captures a downscaled frame for lower bandwidth usage.
    /// </summary>
    public byte[] CaptureFrameScaled(int maxWidth, int maxHeight)
    {
        var bounds = GetCaptureBounds();
        var scale = Math.Min((double)maxWidth / bounds.Width, (double)maxHeight / bounds.Height);
        if (scale >= 1.0)
            return CaptureFrame();

        var scaledWidth = (int)(bounds.Width * scale);
        var scaledHeight = (int)(bounds.Height * scale);

        var hDesktopDC = GetDC(IntPtr.Zero);
        var hMemDC = CreateCompatibleDC(hDesktopDC);
        var hBitmap = CreateCompatibleBitmap(hDesktopDC, bounds.Width, bounds.Height);
        var hOldBitmap = SelectObject(hMemDC, hBitmap);

        try
        {
            BitBlt(hMemDC, 0, 0, bounds.Width, bounds.Height,
                hDesktopDC, bounds.X, bounds.Y, SRCCOPY);

            if (ShowCursor)
                DrawCursorOnDC(hMemDC, bounds);

            using var fullBitmap = Image.FromHbitmap(hBitmap);
            using var scaled = new Bitmap(scaledWidth, scaledHeight);
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(fullBitmap, 0, 0, scaledWidth, scaledHeight);

            using var ms = new MemoryStream();
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, (long)_quality);
            scaled.Save(ms, _jpegCodec, encoderParams);
            return ms.ToArray();
        }
        finally
        {
            SelectObject(hMemDC, hOldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(hMemDC);
            ReleaseDC(IntPtr.Zero, hDesktopDC);
        }
    }

    private void DrawCursorOnDC(IntPtr hMemDC, Rectangle bounds)
    {
        var ci = new CURSORINFO();
        ci.cbSize = (uint)Marshal.SizeOf<CURSORINFO>();
        if (GetCursorInfo(ref ci) && (ci.flags & CURSOR_SHOWING) != 0)
        {
            var cursorX = ci.ptScreenPos.X - bounds.X;
            var cursorY = ci.ptScreenPos.Y - bounds.Y;
            DrawIconEx(hMemDC, cursorX, cursorY, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
        }
    }

    public void Dispose()
    {
        // No persistent resources to release — all handles are freed per-capture
    }
}
