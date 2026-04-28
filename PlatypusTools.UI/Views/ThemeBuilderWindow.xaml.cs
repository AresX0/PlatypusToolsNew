using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlatypusTools.UI.Views
{
    public partial class ThemeBuilderWindow : Window
    {
        private readonly Dictionary<string, TextBox> _editors = new();

        // Default palette
        private readonly Dictionary<string, string> _palette = new()
        {
            { "BackgroundColor", "#FF1E1E1E" },
            { "PanelColor", "#FF252525" },
            { "AccentColor", "#FF6200EA" },
            { "PrimaryButtonColor", "#FF2196F3" },
            { "SuccessColor", "#FF4CAF50" },
            { "WarningColor", "#FFFF9800" },
            { "DangerColor", "#FFF44336" },
            { "TextColor", "#FFFFFFFF" },
            { "MutedTextColor", "#FFAAAAAA" },
            { "BorderColor", "#FF444444" }
        };

        public ThemeBuilderWindow()
        {
            InitializeComponent();
            BuildEditors();
            ApplyPreview();
        }

        private void BuildEditors()
        {
            ColorListPanel.Children.Clear();
            _editors.Clear();
            foreach (var kv in _palette)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                var label = new TextBlock { Text = kv.Key, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
                Grid.SetColumn(label, 0);
                var box = new TextBox { Text = kv.Value, Padding = new Thickness(4), Background = new SolidColorBrush(Color.FromRgb(0x25,0x25,0x25)), Foreground = Brushes.White };
                Grid.SetColumn(box, 1);
                var swatch = new Border { Margin = new Thickness(6,0,0,0), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) };
                Grid.SetColumn(swatch, 2);
                box.TextChanged += (_, _) => { swatch.Background = ParseBrush(box.Text); ApplyPreview(); };
                swatch.Background = ParseBrush(box.Text);
                row.Children.Add(label);
                row.Children.Add(box);
                row.Children.Add(swatch);
                ColorListPanel.Children.Add(row);
                _editors[kv.Key] = box;
            }
        }

        private static Brush ParseBrush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.Magenta; }
        }

        private string Get(string key) => _editors.TryGetValue(key, out var b) ? b.Text : _palette[key];

        private void ApplyPreview()
        {
            PreviewRoot.Background = ParseBrush(Get("BackgroundColor"));
            PreviewHeader.Background = ParseBrush(Get("AccentColor"));
            PreviewBody.Foreground = ParseBrush(Get("TextColor"));
            PreviewButton.Background = ParseBrush(Get("PrimaryButtonColor"));
            PreviewButton.Foreground = ParseBrush(Get("TextColor"));
            PreviewInput.Background = ParseBrush(Get("PanelColor"));
            PreviewInput.Foreground = ParseBrush(Get("TextColor"));
            PreviewInput.BorderBrush = ParseBrush(Get("BorderColor"));
            PreviewStatus.Background = ParseBrush(Get("PanelColor"));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "WPF ResourceDictionary (*.xaml)|*.xaml",
                FileName = "PlatypusTheme.xaml"
            };
            if (dlg.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            foreach (var kv in _editors)
            {
                sb.AppendLine($"  <Color x:Key=\"{kv.Key}\">{kv.Value.Text}</Color>");
                sb.AppendLine($"  <SolidColorBrush x:Key=\"{kv.Key}Brush\" Color=\"{{StaticResource {kv.Key}}}\" />");
            }
            sb.AppendLine("</ResourceDictionary>");
            try { File.WriteAllText(dlg.FileName, sb.ToString()); MessageBox.Show("Saved.", "Theme Builder", MessageBoxButton.OK, MessageBoxImage.Information); }
            catch (Exception ex) { MessageBox.Show("Save failed: " + ex.Message, "Theme Builder", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "WPF ResourceDictionary (*.xaml)|*.xaml" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var text = File.ReadAllText(dlg.FileName);
                var rx = new Regex("<Color\\s+x:Key=\"([^\"]+)\">(#[0-9A-Fa-f]{6,8})</Color>");
                foreach (Match m in rx.Matches(text))
                {
                    var key = m.Groups[1].Value;
                    var val = m.Groups[2].Value;
                    if (_editors.TryGetValue(key, out var box)) box.Text = val;
                }
                ApplyPreview();
            }
            catch (Exception ex) { MessageBox.Show("Load failed: " + ex.Message, "Theme Builder", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
