using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;
using PlatypusTools.UI.Services.Visualization;

namespace PlatypusTools.UI.Views
{
    public partial class TreeMapDiskWindow : Window
    {
        private List<TreeMapItem> _items = new();

        public TreeMapDiskWindow()
        {
            InitializeComponent();
            PathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            if (!string.IsNullOrEmpty(PathBox.Text) && Directory.Exists(PathBox.Text))
                dlg.InitialDirectory = PathBox.Text;
            if (dlg.ShowDialog(this) == true)
                PathBox.Text = dlg.FolderName;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            string path = PathBox.Text?.Trim() ?? "";
            if (!Directory.Exists(path))
            {
                StatusText.Text = "Folder does not exist.";
                return;
            }
            ScanButton.IsEnabled = false;
            StatusText.Text = "Scanning...";
            try
            {
                _items = await Task.Run(() => GatherTopLevel(path));
                Render();
                    StatusText.Text = $"{_items.Count} top-level entries · total {FormatBytes((long)_items.Sum(i => i.Value))}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
            }
            finally
            {
                ScanButton.IsEnabled = true;
            }
        }

        private static List<TreeMapItem> GatherTopLevel(string path)
        {
            var list = new List<TreeMapItem>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    try
                    {
                        long size = DirSize(new DirectoryInfo(dir));
                        list.Add(new TreeMapItem { Name = IOPath.GetFileName(dir), Value = size, Tag = dir });
                    }
                    catch { }
                }
                foreach (var f in Directory.EnumerateFiles(path))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        list.Add(new TreeMapItem { Name = fi.Name, Value = fi.Length, Tag = f });
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        private static long DirSize(DirectoryInfo dir)
        {
            long total = 0;
            try
            {
                foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { total += f.Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

        private void Render()
        {
            MapCanvas.Children.Clear();
            if (_items.Count == 0 || MapCanvas.ActualWidth < 4 || MapCanvas.ActualHeight < 4) return;
            var bounds = new Rect(0, 0, MapCanvas.ActualWidth, MapCanvas.ActualHeight);
            var nodes = TreeMapLayout.Layout(_items, bounds);
            foreach (var n in nodes)
            {
                if (n.Rect.Width < 2 || n.Rect.Height < 2) continue;
                var rect = new Rectangle
                {
                    Width = n.Rect.Width,
                    Height = n.Rect.Height,
                    Fill = ColorFor(n.Item.Name),
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.5,
                    ToolTip = $"{n.Item.Name} — {FormatBytes((long)n.Item.Value)}",
                    Tag = n.Item.Tag,
                    Cursor = Cursors.Hand
                };
                rect.MouseLeftButtonDown += Rect_Click;
                Canvas.SetLeft(rect, n.Rect.X);
                Canvas.SetTop(rect, n.Rect.Y);
                MapCanvas.Children.Add(rect);
                if (n.Rect.Width > 60 && n.Rect.Height > 18)
                {
                    var tb = new TextBlock
                    {
                        Text = n.Item.Name,
                        Foreground = Brushes.White,
                        FontSize = 11,
                        Margin = new Thickness(2),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = n.Rect.Width - 4,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(tb, n.Rect.X + 2);
                    Canvas.SetTop(tb, n.Rect.Y + 2);
                    MapCanvas.Children.Add(tb);
                }
            }
        }

        private void Rect_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Rectangle r && r.Tag is string p)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true }); }
                catch { }
            }
        }

        private static Brush ColorFor(string name)
        {
            int h = name.GetHashCode();
            byte r = (byte)((h & 0xFF0000) >> 16);
            byte g = (byte)((h & 0x00FF00) >> 8);
            byte b = (byte)(h & 0xFF);
            // shift toward darker palette
            return new SolidColorBrush(Color.FromRgb((byte)(40 + r / 2), (byte)(40 + g / 2), (byte)(60 + b / 2)));
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {units[i]}";
        }
    }
}
