using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private List<System.Drawing.Point> _freehandPoints = new();

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

        private void ScreenshotImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not ScreenshotViewModel vm) return;
            if (vm.CurrentMode == AnnotationMode.None) return;

            _isDrawing = true;
            var pos = e.GetPosition(ScreenshotImage);
            _startPoint = new System.Drawing.Point((int)pos.X, (int)pos.Y);
            _freehandPoints.Clear();
            _freehandPoints.Add(_startPoint);

            ScreenshotImage.CaptureMouse();
        }

        private void ScreenshotImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;
            if (DataContext is not ScreenshotViewModel vm) return;

            if (vm.CurrentMode == AnnotationMode.Freehand)
            {
                var pos = e.GetPosition(ScreenshotImage);
                _freehandPoints.Add(new System.Drawing.Point((int)pos.X, (int)pos.Y));
            }
        }

        private void ScreenshotImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            if (DataContext is not ScreenshotViewModel vm) return;

            _isDrawing = false;
            ScreenshotImage.ReleaseMouseCapture();

            var pos = e.GetPosition(ScreenshotImage);
            var endPoint = new System.Drawing.Point((int)pos.X, (int)pos.Y);

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
    }
}
