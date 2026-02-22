using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlatypusTools.UI.ViewModels
{
    public class ColorPickerViewModel : BindableBase
    {
        private readonly DispatcherTimer _timer;
        private bool _isPickerActive;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        public ColorPickerViewModel()
        {
            PickColorCommand = new RelayCommand(_ => TogglePicker());
            CopyHexCommand = new RelayCommand(_ => CopyToClipboard(HexColor));
            CopyRgbCommand = new RelayCommand(_ => CopyToClipboard(RgbColor));
            CopyHslCommand = new RelayCommand(_ => CopyToClipboard(HslColor));

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += Timer_Tick;

            PickedColor = Colors.Black;
            MagnifierZoom = 4;
        }

        private Color _pickedColor;
        public Color PickedColor
        {
            get => _pickedColor;
            set
            {
                if (SetProperty(ref _pickedColor, value))
                {
                    RaisePropertyChanged(nameof(HexColor));
                    RaisePropertyChanged(nameof(RgbColor));
                    RaisePropertyChanged(nameof(HslColor));
                    RaisePropertyChanged(nameof(ColorBrush));
                }
            }
        }

        public string HexColor => $"#{PickedColor.R:X2}{PickedColor.G:X2}{PickedColor.B:X2}";
        public string RgbColor => $"rgb({PickedColor.R}, {PickedColor.G}, {PickedColor.B})";
        public string HslColor
        {
            get
            {
                var (h, s, l) = RgbToHsl(PickedColor.R, PickedColor.G, PickedColor.B);
                return $"hsl({h:F0}, {s:F0}%, {l:F0}%)";
            }
        }
        public SolidColorBrush ColorBrush => new(PickedColor);

        private int _cursorX;
        public int CursorX { get => _cursorX; set => SetProperty(ref _cursorX, value); }

        private int _cursorY;
        public int CursorY { get => _cursorY; set => SetProperty(ref _cursorY, value); }

        private bool _isActive;
        public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

        private string _statusMessage = "Click 'Pick Color' then click anywhere on screen.";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _magnifierZoom;
        public int MagnifierZoom { get => _magnifierZoom; set => SetProperty(ref _magnifierZoom, value); }

        private ImageSource? _magnifierImage;
        public ImageSource? MagnifierImage { get => _magnifierImage; set => SetProperty(ref _magnifierImage, value); }

        // Color history
        private System.Collections.ObjectModel.ObservableCollection<Color> _colorHistory = new();
        public System.Collections.ObjectModel.ObservableCollection<Color> ColorHistory => _colorHistory;

        public ICommand PickColorCommand { get; }
        public ICommand CopyHexCommand { get; }
        public ICommand CopyRgbCommand { get; }
        public ICommand CopyHslCommand { get; }

        private void TogglePicker()
        {
            _isPickerActive = !_isPickerActive;
            if (_isPickerActive)
            {
                _timer.Start();
                IsActive = true;
                StatusMessage = "Move cursor and click to pick a color. Press Escape to cancel.";
            }
            else
            {
                StopPicker();
            }
        }

        private void StopPicker()
        {
            _timer.Stop();
            IsActive = false;
            _isPickerActive = false;
            StatusMessage = "Click 'Pick Color' to start.";
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isPickerActive) return;

            try
            {
                if (GetCursorPos(out POINT pt))
                {
                    CursorX = pt.X;
                    CursorY = pt.Y;

                    var hdc = GetDC(IntPtr.Zero);
                    if (hdc != IntPtr.Zero)
                    {
                        uint pixel = GetPixel(hdc, pt.X, pt.Y);
                        ReleaseDC(IntPtr.Zero, hdc);

                        var r = (byte)(pixel & 0xFF);
                        var g = (byte)((pixel >> 8) & 0xFF);
                        var b = (byte)((pixel >> 16) & 0xFF);

                        PickedColor = Color.FromRgb(r, g, b);
                        CaptureMagnifier(pt.X, pt.Y);
                    }
                }

                // Check if mouse button is pressed (using interop)
                if ((GetAsyncKeyState(0x01) & 0x8000) != 0 && _isPickerActive)
                {
                    // Add to history
                    if (!_colorHistory.Contains(PickedColor))
                    {
                        _colorHistory.Insert(0, PickedColor);
                        if (_colorHistory.Count > 20) _colorHistory.RemoveAt(20);
                    }

                    StatusMessage = $"Picked: {HexColor}";
                    StopPicker();
                }

                // Check escape key
                if ((GetAsyncKeyState(0x1B) & 0x8000) != 0)
                {
                    StopPicker();
                }
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private void CaptureMagnifier(int x, int y)
        {
            try
            {
                int captureSize = 15;
                int half = captureSize / 2;

                var hdc = GetDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero) return;

                var bitmap = new WriteableBitmap(captureSize, captureSize, 96, 96, PixelFormats.Bgra32, null);
                byte[] pixels = new byte[captureSize * captureSize * 4];

                for (int py = 0; py < captureSize; py++)
                {
                    for (int px = 0; px < captureSize; px++)
                    {
                        uint pixel = GetPixel(hdc, x - half + px, y - half + py);
                        int offset = (py * captureSize + px) * 4;
                        pixels[offset] = (byte)((pixel >> 16) & 0xFF); // B
                        pixels[offset + 1] = (byte)((pixel >> 8) & 0xFF); // G
                        pixels[offset + 2] = (byte)(pixel & 0xFF); // R
                        pixels[offset + 3] = 255; // A
                    }
                }

                ReleaseDC(IntPtr.Zero, hdc);

                bitmap.WritePixels(new Int32Rect(0, 0, captureSize, captureSize),
                    pixels, captureSize * 4, 0);
                bitmap.Freeze();

                MagnifierImage = bitmap;
            }
            catch { }
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                StatusMessage = $"Copied: {text}";
            }
            catch { }
        }

        private static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double h = 0, s, l = (max + min) / 2.0;

            if (Math.Abs(max - min) < 0.001)
            {
                h = s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (Math.Abs(max - rd) < 0.001)
                    h = (gd - bd) / d + (gd < bd ? 6 : 0);
                else if (Math.Abs(max - gd) < 0.001)
                    h = (bd - rd) / d + 2;
                else
                    h = (rd - gd) / d + 4;

                h *= 60;
            }

            return (h, s * 100, l * 100);
        }
    }
}
