using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PlatypusTools.Core.Services.Wallpaper;

namespace PlatypusTools.UI.Views
{
    public partial class SlideshowScreensaverWindow : Window
    {
        private readonly WallpaperRotatorConfig _config;
        private List<string> _images = new();
        private int _index = 0;
        private bool _useImageA = true;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _scrollTimer;
        private Point? _startMouse;
        private CancellationTokenSource _cts = new();
        private List<OverlayLine> _allOverlayLines = new();

        public SlideshowScreensaverWindow(WallpaperRotatorConfig config)
        {
            InitializeComponent();
            _config = config;

            Loaded += OnLoaded;
            Closed += (_, _) => _cts.Cancel();
            PreviewKeyDown += (_, _) => Close();
            MouseDown += (_, _) => Close();
            MouseMove += OnMouseMove;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_startMouse == null) { _startMouse = p; return; }
            if (Math.Abs(p.X - _startMouse.Value.X) > 5 || Math.Abs(p.Y - _startMouse.Value.Y) > 5)
                Close();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _images = WallpaperImageScanner.Scan(_config.ImagesDirectory, _config.Shuffle);
            if (_images.Count == 0)
            {
                MessageBox.Show("No images found in the configured folder.", "PlatypusTools Slideshow",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
                return;
            }

            if (_config.ShowOverlay)
                await LoadOverlayAsync().ConfigureAwait(true);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(2, _config.SlideshowIntervalSeconds)) };
            _timer.Tick += async (_, _) => await ShowNextAsync();
            _timer.Start();

            await ShowNextAsync();
        }

        private async Task LoadOverlayAsync()
        {
            try
            {
                IReadOnlyList<string> lines;
                if (string.Equals(_config.OverlaySource, "custom", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = (_config.CustomOverlayText ?? "").Replace("\r", "").Split('\n');
                    lines = new List<string>(raw);
                }
                else if (string.Equals(_config.OverlaySource, "none", StringComparison.OrdinalIgnoreCase))
                {
                    OverlayPanel.Visibility = Visibility.Collapsed;
                    return;
                }
                else
                {
                    var snap = await new NasaInfoService().GetAsync(forceRefresh: false, _cts.Token).ConfigureAwait(true);
                    lines = NasaInfoService.BuildOverlayLines(snap, maxLines: 30);
                }

                _allOverlayLines.Clear();
                foreach (var line in lines)
                    _allOverlayLines.Add(BuildOverlayLine(line));

                OverlayItems.ItemsSource = _allOverlayLines;
                OverlayPanel.Visibility = Visibility.Visible;

                int scrollSec = Math.Max(5, _config.OverlayScrollSpeedSeconds);
                _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(scrollSec) };
                _scrollTimer.Tick += (_, _) => ScrollOverlay();
                _scrollTimer.Start();
            }
            catch
            {
                OverlayPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ScrollOverlay()
        {
            if (OverlayScroll.ScrollableHeight <= 0) return;
            double next = OverlayScroll.VerticalOffset + OverlayScroll.ViewportHeight * 0.6;
            if (next >= OverlayScroll.ScrollableHeight) next = 0;
            OverlayScroll.ScrollToVerticalOffset(next);
        }

        private static OverlayLine BuildOverlayLine(string text)
        {
            Color color = Color.FromRgb(0xE0, 0xE0, 0xE0);
            FontWeight weight = FontWeights.Normal;
            double size = 14;

            if (text.StartsWith("═")) { color = Color.FromRgb(0x4F, 0xC3, 0xF7); weight = FontWeights.Bold; size = 16; }
            else if (text.StartsWith("✓")) color = Color.FromRgb(0x66, 0xBB, 0x6A);
            else if (text.StartsWith("●")) color = Color.FromRgb(0xFF, 0xA7, 0x26);
            else if (text.StartsWith("◇")) color = Color.FromRgb(0x90, 0xCA, 0xF9);
            else if (text.StartsWith("▸")) color = Color.FromRgb(0xCE, 0x93, 0xD8);

            return new OverlayLine
            {
                Text = text,
                Brush = new SolidColorBrush(color),
                Weight = weight,
                Size = size,
            };
        }

        private async Task ShowNextAsync()
        {
            if (_images.Count == 0) return;
            var path = _images[_index % _images.Count];
            _index = (_index + 1) % _images.Count;

            try
            {
                var bmp = await Task.Run(() => LoadFitted(path,
                    (int)(SystemParameters.PrimaryScreenWidth),
                    (int)(SystemParameters.PrimaryScreenHeight)));
                if (bmp == null) return;

                var incoming = _useImageA ? ImageA : ImageB;
                var outgoing = _useImageA ? ImageB : ImageA;
                _useImageA = !_useImageA;

                incoming.Source = bmp;

                if (string.Equals(_config.Transition, "cut", StringComparison.OrdinalIgnoreCase))
                {
                    incoming.Opacity = 1;
                    outgoing.Opacity = 0;
                }
                else
                {
                    incoming.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
                    outgoing.BeginAnimation(Image.OpacityProperty, new DoubleAnimation(outgoing.Opacity, 0, TimeSpan.FromMilliseconds(700)));
                }
            }
            catch
            {
                /* skip on error */
            }
        }

        private static BitmapImage? LoadFitted(string path, int sw, int sh)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = sw > 0 ? sw : 1920;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public class OverlayLine
        {
            public string Text { get; set; } = "";
            public Brush Brush { get; set; } = Brushes.White;
            public FontWeight Weight { get; set; } = FontWeights.Normal;
            public double Size { get; set; } = 14;
        }
    }
}
