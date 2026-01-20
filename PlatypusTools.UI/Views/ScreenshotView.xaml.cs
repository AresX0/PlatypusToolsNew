using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for ScreenshotView.xaml
    /// </summary>
    public partial class ScreenshotView : UserControl
    {
        private bool _isDrawing;
        private System.Drawing.Point _startPoint;
        private Point _startWpfPoint;
        private List<System.Drawing.Point> _freehandPoints = new();
        private Shape? _previewShape;
        private Polyline? _freehandLine;

        public ScreenshotView()
        {
            InitializeComponent();
        }

        private void ColorSwatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Background is SolidColorBrush brush)
            {
                if (DataContext is ScreenshotViewModel vm)
                {
                    vm.SelectedColor = brush.Color;
                }
            }
        }

        private void AnnotationOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not ScreenshotViewModel vm) return;
            if (vm.CurrentMode == AnnotationMode.None) return;

            _isDrawing = true;
            var pos = e.GetPosition(AnnotationOverlay);
            _startPoint = new System.Drawing.Point((int)pos.X, (int)pos.Y);
            _startWpfPoint = pos;
            _freehandPoints.Clear();
            _freehandPoints.Add(_startPoint);

            // Create preview shape based on current mode
            CreatePreviewShape(vm);

            AnnotationOverlay.CaptureMouse();
        }

        private void CreatePreviewShape(ScreenshotViewModel vm)
        {
            var brush = new SolidColorBrush(vm.SelectedColor);
            brush.Freeze();

            switch (vm.CurrentMode)
            {
                case AnnotationMode.Arrow:
                    _previewShape = new Line
                    {
                        Stroke = brush,
                        StrokeThickness = vm.LineThickness,
                        X1 = _startWpfPoint.X,
                        Y1 = _startWpfPoint.Y,
                        X2 = _startWpfPoint.X,
                        Y2 = _startWpfPoint.Y,
                        StrokeEndLineCap = PenLineCap.Triangle
                    };
                    AnnotationOverlay.Children.Add(_previewShape);
                    break;

                case AnnotationMode.Rectangle:
                    _previewShape = new Rectangle
                    {
                        Stroke = brush,
                        StrokeThickness = vm.LineThickness,
                        Fill = new SolidColorBrush(Color.FromArgb(50, vm.SelectedColor.R, vm.SelectedColor.G, vm.SelectedColor.B))
                    };
                    Canvas.SetLeft(_previewShape, _startWpfPoint.X);
                    Canvas.SetTop(_previewShape, _startWpfPoint.Y);
                    AnnotationOverlay.Children.Add(_previewShape);
                    break;

                case AnnotationMode.Ellipse:
                    _previewShape = new Ellipse
                    {
                        Stroke = brush,
                        StrokeThickness = vm.LineThickness,
                        Fill = new SolidColorBrush(Color.FromArgb(50, vm.SelectedColor.R, vm.SelectedColor.G, vm.SelectedColor.B))
                    };
                    Canvas.SetLeft(_previewShape, _startWpfPoint.X);
                    Canvas.SetTop(_previewShape, _startWpfPoint.Y);
                    AnnotationOverlay.Children.Add(_previewShape);
                    break;

                case AnnotationMode.Highlight:
                    _previewShape = new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0)), // Yellow highlight
                        StrokeThickness = 0
                    };
                    Canvas.SetLeft(_previewShape, _startWpfPoint.X);
                    Canvas.SetTop(_previewShape, _startWpfPoint.Y);
                    AnnotationOverlay.Children.Add(_previewShape);
                    break;

                case AnnotationMode.Blur:
                    _previewShape = new Rectangle
                    {
                        Stroke = new SolidColorBrush(Colors.Gray),
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128))
                    };
                    Canvas.SetLeft(_previewShape, _startWpfPoint.X);
                    Canvas.SetTop(_previewShape, _startWpfPoint.Y);
                    AnnotationOverlay.Children.Add(_previewShape);
                    break;

                case AnnotationMode.Freehand:
                    _freehandLine = new Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = vm.LineThickness,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    _freehandLine.Points.Add(_startWpfPoint);
                    AnnotationOverlay.Children.Add(_freehandLine);
                    break;

                case AnnotationMode.Text:
                    // Show text cursor indicator
                    _previewShape = new Rectangle
                    {
                        Width = 2,
                        Height = 20,
                        Fill = brush
                    };
                    Canvas.SetLeft(_previewShape, _startWpfPoint.X);
                    Canvas.SetTop(_previewShape, _startWpfPoint.Y);
                    AnnotationOverlay.Children.Add(_previewShape);
                    break;
            }
        }

        private void AnnotationOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;
            if (DataContext is not ScreenshotViewModel vm) return;

            var pos = e.GetPosition(AnnotationOverlay);

            switch (vm.CurrentMode)
            {
                case AnnotationMode.Arrow:
                    if (_previewShape is Line line)
                    {
                        line.X2 = pos.X;
                        line.Y2 = pos.Y;
                    }
                    break;

                case AnnotationMode.Rectangle:
                case AnnotationMode.Highlight:
                case AnnotationMode.Blur:
                    if (_previewShape != null)
                    {
                        double x = System.Math.Min(_startWpfPoint.X, pos.X);
                        double y = System.Math.Min(_startWpfPoint.Y, pos.Y);
                        double w = System.Math.Abs(pos.X - _startWpfPoint.X);
                        double h = System.Math.Abs(pos.Y - _startWpfPoint.Y);
                        Canvas.SetLeft(_previewShape, x);
                        Canvas.SetTop(_previewShape, y);
                        _previewShape.Width = w;
                        _previewShape.Height = h;
                    }
                    break;

                case AnnotationMode.Ellipse:
                    if (_previewShape is Ellipse ellipse)
                    {
                        double x = System.Math.Min(_startWpfPoint.X, pos.X);
                        double y = System.Math.Min(_startWpfPoint.Y, pos.Y);
                        double w = System.Math.Abs(pos.X - _startWpfPoint.X);
                        double h = System.Math.Abs(pos.Y - _startWpfPoint.Y);
                        Canvas.SetLeft(ellipse, x);
                        Canvas.SetTop(ellipse, y);
                        ellipse.Width = w;
                        ellipse.Height = h;
                    }
                    break;

                case AnnotationMode.Freehand:
                    if (_freehandLine != null)
                    {
                        _freehandLine.Points.Add(pos);
                        _freehandPoints.Add(new System.Drawing.Point((int)pos.X, (int)pos.Y));
                    }
                    break;
            }
        }

        private void AnnotationOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            if (DataContext is not ScreenshotViewModel vm) return;

            _isDrawing = false;
            AnnotationOverlay.ReleaseMouseCapture();

            var pos = e.GetPosition(AnnotationOverlay);
            var endPoint = new System.Drawing.Point((int)pos.X, (int)pos.Y);

            // Clear preview shapes
            AnnotationOverlay.Children.Clear();
            _previewShape = null;
            _freehandLine = null;

            // Apply the annotation to the actual image
            if (vm.CurrentMode == AnnotationMode.Freehand)
            {
                _freehandPoints.Add(endPoint);
                if (_freehandPoints.Count > 1)
                {
                    vm.ApplyFreehandAnnotation(_freehandPoints.ToArray());
                }
            }
            else
            {
                vm.ApplyAnnotation(_startPoint, endPoint);
            }
        }

        // Keep old methods for backwards compatibility but they won't be used
        private void ScreenshotImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
        private void ScreenshotImage_MouseMove(object sender, MouseEventArgs e) { }
        private void ScreenshotImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }
    }
}
