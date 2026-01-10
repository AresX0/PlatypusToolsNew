using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.Views
{
    public partial class PreviewWindow : Window
    {
        private readonly string _path;
        public PreviewWindow(string path)
        {
            InitializeComponent();
            _path = path;
            Loaded += PreviewWindow_Loaded;
        }

        private void PreviewWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_path)) return;
            try
            {
                var ext = Path.GetExtension(_path).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".tif" || ext == ".tiff")
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(_path);
                    bmp.EndInit();
                    PreviewImage.Source = bmp;
                }
                else
                {
                    // Fallback: show a small icon generated from the file
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(_path);
                    if (icon != null)
                    {
                        var bmp = icon.ToBitmap();
                        using (var ms = new MemoryStream())
                        {
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Seek(0, SeekOrigin.Begin);
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.StreamSource = ms;
                            bi.EndInit();
                            PreviewImage.Source = bi;
                        }
                    }
                }
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"/select,\"{_path}\"") { UseShellExecute = true }); } catch { }
        }
    }
}