using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for Model3DEditorView.xaml
    /// </summary>
    public partial class Model3DEditorView : UserControl
    {
        private Point _lastMousePosition;
        private bool _isLeftButtonDown;
        private bool _isRightButtonDown;

        public Model3DEditorView()
        {
            InitializeComponent();
        }

        private Model3DEditorViewModel? ViewModel => DataContext as Model3DEditorViewModel;

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isLeftButtonDown = true;
            _lastMousePosition = e.GetPosition(viewportBorder);
            viewportBorder.CaptureMouse();
            e.Handled = true;
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isLeftButtonDown = false;
            if (!_isRightButtonDown)
                viewportBorder.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isRightButtonDown = true;
            _lastMousePosition = e.GetPosition(viewportBorder);
            viewportBorder.CaptureMouse();
            e.Handled = true;
        }

        private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isRightButtonDown = false;
            if (!_isLeftButtonDown)
                viewportBorder.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (ViewModel == null) return;

            var currentPosition = e.GetPosition(viewportBorder);
            var deltaX = currentPosition.X - _lastMousePosition.X;
            var deltaY = currentPosition.Y - _lastMousePosition.Y;

            if (_isLeftButtonDown)
            {
                // Left mouse button: Rotate model
                ViewModel.RotationY = Clamp(ViewModel.RotationY + deltaX * 0.5, -180, 180);
                ViewModel.RotationX = Clamp(ViewModel.RotationX - deltaY * 0.5, -180, 180);
            }
            else if (_isRightButtonDown)
            {
                // Right mouse button: Pan camera
                ViewModel.PanX = Clamp(ViewModel.PanX + deltaX * 0.3, -200, 200);
                ViewModel.PanY = Clamp(ViewModel.PanY - deltaY * 0.3, -200, 200);
            }

            _lastMousePosition = currentPosition;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ViewModel == null) return;

            // Mouse wheel: Zoom (adjust camera distance)
            double zoomDelta = e.Delta > 0 ? -10 : 10;
            ViewModel.Zoom = Clamp(ViewModel.Zoom + zoomDelta, 20, 500);
            e.Handled = true;
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
