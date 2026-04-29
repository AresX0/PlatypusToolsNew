using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ColorPickerViewModel : ObservableObject
{
    [ObservableProperty] private byte _r = 255;
    [ObservableProperty] private byte _g = 128;
    [ObservableProperty] private byte _b = 0;
    [ObservableProperty] private byte _a = 255;
    [ObservableProperty] private string _hex = "";
    [ObservableProperty] private IBrush _previewBrush = Brushes.Transparent;

    public ColorPickerViewModel() { Update(); }

    partial void OnRChanged(byte v) => Update();
    partial void OnGChanged(byte v) => Update();
    partial void OnBChanged(byte v) => Update();
    partial void OnAChanged(byte v) => Update();

    private void Update()
    {
        Hex = $"#{A:X2}{R:X2}{G:X2}{B:X2}";
        PreviewBrush = new SolidColorBrush(Color.FromArgb(A, R, G, B));
    }

    [RelayCommand]
    private void ParseHex(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        s = s.TrimStart('#');
        try
        {
            if (s.Length == 6)
            {
                R = Convert.ToByte(s[..2], 16);
                G = Convert.ToByte(s.Substring(2, 2), 16);
                B = Convert.ToByte(s.Substring(4, 2), 16);
                A = 255;
            }
            else if (s.Length == 8)
            {
                A = Convert.ToByte(s[..2], 16);
                R = Convert.ToByte(s.Substring(2, 2), 16);
                G = Convert.ToByte(s.Substring(4, 2), 16);
                B = Convert.ToByte(s.Substring(6, 2), 16);
            }
        }
        catch { }
    }
}
