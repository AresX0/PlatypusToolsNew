using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

/// <summary>
/// Audio Player view with integrated visualizer supporting multiple visualization modes.
/// </summary>
public partial class AudioPlayerView : UserControl
{
    private AudioPlayerViewModel? _viewModel;
    
    public AudioPlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as AudioPlayerViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdatePlaceholderVisibility();
            UpdateEmptyQueueVisibility();
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioPlayerViewModel.SpectrumData))
        {
            Dispatcher.Invoke(() => UpdateVisualizer(_viewModel?.SpectrumData ?? Array.Empty<double>()));
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.CurrentTrack))
        {
            Dispatcher.Invoke(UpdatePlaceholderVisibility);
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.Queue) || 
                 (sender is AudioPlayerViewModel vm && vm.Queue.Count == 0))
        {
            Dispatcher.Invoke(UpdateEmptyQueueVisibility);
        }
    }
    
    private void UpdatePlaceholderVisibility()
    {
        NoTrackPlaceholder.Visibility = _viewModel?.CurrentTrack == null 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }
    
    private void UpdateEmptyQueueVisibility()
    {
        EmptyQueueMessage.Visibility = (_viewModel?.Queue.Count ?? 0) == 0 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }
    
    private void UpdateVisualizer(double[] spectrumData)
    {
        if (spectrumData == null || spectrumData.Length == 0) return;
        
        VisualizerCanvas.Children.Clear();
        
        var width = VisualizerCanvas.ActualWidth;
        var height = VisualizerCanvas.ActualHeight;
        
        if (width <= 0 || height <= 0) return;
        
        // Get visualizer mode from ViewModel (0=Bars, 1=Mirror, 2=Waveform, 3=Circular, 4=None)
        var mode = _viewModel?.VisualizerModeIndex ?? 0;
        
        switch (mode)
        {
            case 0: // Bars
                DrawBarsVisualizer(spectrumData, width, height);
                break;
            case 1: // Mirror Bars
                DrawMirrorBarsVisualizer(spectrumData, width, height);
                break;
            case 2: // Waveform
                DrawWaveformVisualizer(spectrumData, width, height);
                break;
            case 3: // Circular
                DrawCircularVisualizer(spectrumData, width, height);
                break;
            case 4: // None
                break;
        }
    }
    
    private void DrawBarsVisualizer(double[] spectrumData, double width, double height)
    {
        var barCount = Math.Min(spectrumData.Length, 32);
        var barWidth = (width - (barCount - 1) * 2) / barCount;
        
        for (int i = 0; i < barCount; i++)
        {
            var barHeight = spectrumData[i] * height;
            var x = i * (barWidth + 2);
            
            var hue = (double)i / barCount;
            var color = HsvToRgb(hue * 240, 0.8, 0.9);
            
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(2, barHeight),
                Fill = new SolidColorBrush(color),
                RadiusX = 2,
                RadiusY = 2
            };
            
            Canvas.SetLeft(rect, x);
            Canvas.SetBottom(rect, 0);
            
            VisualizerCanvas.Children.Add(rect);
        }
    }
    
    private void DrawMirrorBarsVisualizer(double[] spectrumData, double width, double height)
    {
        var barCount = Math.Min(spectrumData.Length, 32);
        var barWidth = (width - (barCount - 1) * 2) / barCount;
        var halfHeight = height / 2;
        
        for (int i = 0; i < barCount; i++)
        {
            var barHeight = spectrumData[i] * halfHeight;
            var x = i * (barWidth + 2);
            
            var hue = (double)i / barCount;
            var color = HsvToRgb(hue * 300, 0.7, 0.95);
            
            // Top bar (going up from center)
            var topRect = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(1, barHeight),
                Fill = new SolidColorBrush(color),
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(topRect, x);
            Canvas.SetTop(topRect, halfHeight - barHeight);
            VisualizerCanvas.Children.Add(topRect);
            
            // Bottom bar (going down from center - mirrored)
            var bottomRect = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(1, barHeight),
                Fill = new SolidColorBrush(color),
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(bottomRect, x);
            Canvas.SetTop(bottomRect, halfHeight);
            VisualizerCanvas.Children.Add(bottomRect);
        }
    }
    
    private void DrawWaveformVisualizer(double[] spectrumData, double width, double height)
    {
        if (spectrumData.Length < 2) return;
        
        var points = new PointCollection();
        var centerY = height / 2;
        
        for (int i = 0; i < spectrumData.Length; i++)
        {
            var x = (double)i / (spectrumData.Length - 1) * width;
            var y = centerY - (spectrumData[i] * centerY * 0.8);
            points.Add(new Point(x, y));
        }
        
        // Add mirrored points for bottom wave
        for (int i = spectrumData.Length - 1; i >= 0; i--)
        {
            var x = (double)i / (spectrumData.Length - 1) * width;
            var y = centerY + (spectrumData[i] * centerY * 0.8);
            points.Add(new Point(x, y));
        }
        
        var polygon = new Polygon
        {
            Points = points,
            Fill = new LinearGradientBrush(
                Color.FromArgb(180, 0, 150, 255),
                Color.FromArgb(180, 0, 255, 200),
                90),
            Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
            StrokeThickness = 1
        };
        
        VisualizerCanvas.Children.Add(polygon);
    }
    
    private void DrawCircularVisualizer(double[] spectrumData, double width, double height)
    {
        var centerX = width / 2;
        var centerY = height / 2;
        var baseRadius = Math.Min(width, height) / 4;
        var maxBarLength = Math.Min(width, height) / 4;
        
        var barCount = Math.Min(spectrumData.Length, 32);
        
        for (int i = 0; i < barCount; i++)
        {
            var angle = (2 * Math.PI * i / barCount) - Math.PI / 2;
            var barLength = spectrumData[i] * maxBarLength;
            
            var x1 = centerX + baseRadius * Math.Cos(angle);
            var y1 = centerY + baseRadius * Math.Sin(angle);
            var x2 = centerX + (baseRadius + barLength) * Math.Cos(angle);
            var y2 = centerY + (baseRadius + barLength) * Math.Sin(angle);
            
            var hue = (double)i / barCount;
            var color = HsvToRgb(hue * 360, 0.8, 0.95);
            
            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = Math.Max(4, width / barCount * 0.6),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            
            VisualizerCanvas.Children.Add(line);
        }
        
        // Draw center circle
        var circle = new Ellipse
        {
            Width = baseRadius * 2 - 4,
            Height = baseRadius * 2 - 4,
            Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            StrokeThickness = 2
        };
        Canvas.SetLeft(circle, centerX - baseRadius + 2);
        Canvas.SetTop(circle, centerY - baseRadius + 2);
        VisualizerCanvas.Children.Add(circle);
    }
    
    private static Color HsvToRgb(double h, double s, double v)
    {
        h = h % 360;
        if (h < 0) h += 360;
        
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = v - c;
        
        double r, g, b;
        
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        
        return Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
