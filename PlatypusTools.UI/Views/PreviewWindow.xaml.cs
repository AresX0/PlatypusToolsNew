using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PlatypusTools.UI.Utilities;

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
                    // Use ImageHelper for memory-efficient loading
                    PreviewImage.Source = ImageHelper.LoadFromFile(_path);
                }
                else
                {
                    // Fallback: show a small icon generated from the file
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(_path);
                    PreviewImage.Source = ImageHelper.FromIcon(icon);
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