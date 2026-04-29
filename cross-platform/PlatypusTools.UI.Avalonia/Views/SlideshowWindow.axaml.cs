using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class SlideshowWindow : Window
{
    private readonly List<string> _files;
    private int _index;
    private readonly DispatcherTimer _timer;

    public SlideshowWindow() : this(new List<string>(), 5) { }

    public SlideshowWindow(List<string> files, int intervalSeconds)
    {
        InitializeComponent();
        _files = files;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)) };
        _timer.Tick += (_, _) => Next();
        Opened += (_, _) => { Next(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
        KeyDown += OnKey;
        PointerPressed += OnClick;
    }

    private void Next()
    {
        if (_files.Count == 0) { Close(); return; }
        var f = _files[_index % _files.Count];
        _index++;
        try
        {
            var img = this.FindControl<Image>("Img");
            using var s = File.OpenRead(f);
            if (img != null) img.Source = new Bitmap(s);
        }
        catch { }
    }

    private void OnKey(object? s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
    private void OnClick(object? s, PointerPressedEventArgs e) => Close();
}
