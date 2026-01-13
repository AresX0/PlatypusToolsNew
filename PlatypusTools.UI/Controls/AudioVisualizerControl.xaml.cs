using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PlatypusTools.UI.Controls;

/// <summary>
/// Audio visualizer control with multiple visualization modes:
/// - Spectrum Bars
/// - Mirror Bars
/// - Waveform
/// - Circular
/// </summary>
public partial class AudioVisualizerControl : UserControl
{
    private readonly DispatcherTimer _animationTimer;
    private readonly Rectangle[] _bars = new Rectangle[64];
    private readonly float[] _smoothedData = new float[64];
    private const float SmoothingFactor = 0.3f;
    private VisualizerMode _currentMode = VisualizerMode.Bars;
    
    // Dependency Properties
    public static readonly DependencyProperty SpectrumDataProperty =
        DependencyProperty.Register(nameof(SpectrumData), typeof(float[]), typeof(AudioVisualizerControl),
            new PropertyMetadata(null, OnSpectrumDataChanged));
    
    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(AudioVisualizerControl),
            new PropertyMetadata(false, OnIsPlayingChanged));
    
    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register(nameof(BarColor), typeof(Brush), typeof(AudioVisualizerControl),
            new PropertyMetadata(null));
    
    public static readonly DependencyProperty BarCountProperty =
        DependencyProperty.Register(nameof(BarCount), typeof(int), typeof(AudioVisualizerControl),
            new PropertyMetadata(32));
    
    public float[]? SpectrumData
    {
        get => (float[])GetValue(SpectrumDataProperty);
        set => SetValue(SpectrumDataProperty, value);
    }
    
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }
    
    public Brush? BarColor
    {
        get => (Brush)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }
    
    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }
    
    public AudioVisualizerControl()
    {
        InitializeComponent();
        
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
        };
        _animationTimer.Tick += OnAnimationTick;
        
        SizeChanged += OnSizeChanged;
    }
    
    private static void OnSpectrumDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioVisualizerControl control && e.NewValue is float[] data)
        {
            control.UpdateVisualization(data);
        }
    }
    
    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AudioVisualizerControl control)
        {
            if ((bool)e.NewValue)
                control._animationTimer.Start();
            else
                control._animationTimer.Stop();
        }
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeBars();
        if (IsPlaying)
            _animationTimer.Start();
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _animationTimer.Stop();
    }
    
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        InitializeBars();
    }
    
    private void InitializeBars()
    {
        VisualizerCanvas.Children.Clear();
        
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        
        var barCount = Math.Min(BarCount, 64);
        var barWidth = ActualWidth / barCount;
        var gap = barWidth * 0.2;
        var effectiveBarWidth = barWidth - gap;
        
        var barColor = BarColor ?? CreateGradientBrush();
        
        for (int i = 0; i < barCount; i++)
        {
            var bar = new Rectangle
            {
                Width = effectiveBarWidth,
                Height = 2,
                Fill = barColor,
                RadiusX = 2,
                RadiusY = 2
            };
            
            Canvas.SetLeft(bar, i * barWidth + gap / 2);
            Canvas.SetBottom(bar, 0);
            
            _bars[i] = bar;
            VisualizerCanvas.Children.Add(bar);
        }
    }
    
    private Brush CreateGradientBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(76, 175, 80), 0),    // Green
                new GradientStop(Color.FromRgb(255, 235, 59), 0.6), // Yellow
                new GradientStop(Color.FromRgb(244, 67, 54), 1)     // Red
            }
        };
    }
    
    private void UpdateVisualization(float[] data)
    {
        if (data == null || data.Length == 0) return;
        
        // Apply smoothing
        for (int i = 0; i < Math.Min(data.Length, _smoothedData.Length); i++)
        {
            _smoothedData[i] = _smoothedData[i] * (1 - SmoothingFactor) + data[i] * SmoothingFactor;
        }
    }
    
    private void OnAnimationTick(object? sender, EventArgs e)
    {
        switch (_currentMode)
        {
            case VisualizerMode.Bars:
                RenderBars();
                break;
            case VisualizerMode.MirrorBars:
                RenderMirrorBars();
                break;
            case VisualizerMode.Waveform:
                RenderWaveform();
                break;
            case VisualizerMode.Circular:
                RenderCircular();
                break;
        }
    }
    
    private void RenderBars()
    {
        var barCount = Math.Min(BarCount, _bars.Length);
        var maxHeight = ActualHeight - 4;
        
        for (int i = 0; i < barCount; i++)
        {
            if (_bars[i] == null) continue;
            
            var dataIndex = (int)(i * (_smoothedData.Length / (float)barCount));
            var value = dataIndex < _smoothedData.Length ? _smoothedData[dataIndex] : 0;
            var height = Math.Max(2, value * maxHeight);
            
            _bars[i].Height = height;
        }
    }
    
    private void RenderMirrorBars()
    {
        var barCount = Math.Min(BarCount, _bars.Length);
        var maxHeight = (ActualHeight / 2) - 2;
        
        for (int i = 0; i < barCount; i++)
        {
            if (_bars[i] == null) continue;
            
            var dataIndex = (int)(i * (_smoothedData.Length / (float)barCount));
            var value = dataIndex < _smoothedData.Length ? _smoothedData[dataIndex] : 0;
            var height = Math.Max(2, value * maxHeight * 2);
            
            _bars[i].Height = height;
            Canvas.SetBottom(_bars[i], (ActualHeight - height) / 2);
        }
    }
    
    private void RenderWaveform()
    {
        VisualizerCanvas.Children.Clear();
        
        var path = new Path
        {
            Stroke = BarColor ?? Brushes.LimeGreen,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var centerY = ActualHeight / 2;
            var points = Math.Min(64, _smoothedData.Length);
            var stepX = ActualWidth / points;
            
            context.BeginFigure(new Point(0, centerY), false, false);
            
            for (int i = 0; i < points; i++)
            {
                var x = i * stepX;
                var y = centerY - (_smoothedData[i] * centerY * 0.9);
                context.LineTo(new Point(x, y), true, true);
            }
        }
        
        geometry.Freeze();
        path.Data = geometry;
        VisualizerCanvas.Children.Add(path);
    }
    
    private void RenderCircular()
    {
        VisualizerCanvas.Children.Clear();
        
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        var baseRadius = Math.Min(centerX, centerY) * 0.5;
        var maxBarLength = Math.Min(centerX, centerY) * 0.4;
        
        var barCount = Math.Min(32, _smoothedData.Length);
        var angleStep = 360.0 / barCount;
        
        var barColor = BarColor ?? CreateGradientBrush();
        
        for (int i = 0; i < barCount; i++)
        {
            var angle = (i * angleStep - 90) * Math.PI / 180;
            var value = _smoothedData[i * (_smoothedData.Length / barCount)];
            var barLength = baseRadius + value * maxBarLength;
            
            var x1 = centerX + baseRadius * Math.Cos(angle);
            var y1 = centerY + baseRadius * Math.Sin(angle);
            var x2 = centerX + barLength * Math.Cos(angle);
            var y2 = centerY + barLength * Math.Sin(angle);
            
            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = barColor,
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            
            VisualizerCanvas.Children.Add(line);
        }
        
        // Center circle
        var circle = new Ellipse
        {
            Width = baseRadius * 1.5,
            Height = baseRadius * 1.5,
            Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            Stroke = barColor,
            StrokeThickness = 2
        };
        Canvas.SetLeft(circle, centerX - baseRadius * 0.75);
        Canvas.SetTop(circle, centerY - baseRadius * 0.75);
        VisualizerCanvas.Children.Add(circle);
    }
    
    private void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentMode = (VisualizerMode)ModeSelector.SelectedIndex;
        InitializeBars();
    }
}

public enum VisualizerMode
{
    Bars,
    MirrorBars,
    Waveform,
    Circular
}
