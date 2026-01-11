using System;
using System.Windows;

namespace PlatypusTools.UI.Services
{
    public static class ThemeManager
    {
        public const string Light = "Light";
        public const string Dark = "Dark";

        public static void ApplyTheme(string name)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;
                // Remove existing theme dictionaries
                for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var md = app.Resources.MergedDictionaries[i];
                    if (md.Source != null && (md.Source.OriginalString.Contains("Themes/Light.xaml") || md.Source.OriginalString.Contains("Themes/Dark.xaml")))
                    {
                        app.Resources.MergedDictionaries.RemoveAt(i);
                    }
                }

                var dict = new ResourceDictionary();
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var themePath = name == Dark ? System.IO.Path.Combine(baseDir, "Themes", "Dark.xaml") : System.IO.Path.Combine(baseDir, "Themes", "Light.xaml");
                if (!System.IO.File.Exists(themePath))
                {
                    // try assembly path
                    var asm = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                    var asmDir = asm != null ? System.IO.Path.GetDirectoryName(asm) ?? string.Empty : string.Empty;
                    themePath = System.IO.Path.Combine(asmDir, "Themes", name + ".xaml");
                }

                if (System.IO.File.Exists(themePath))
                {
                    dict.Source = new Uri(themePath, UriKind.Absolute);
                    app.Resources.MergedDictionaries.Add(dict);
                }
            }
            catch { }
        }
    }
}