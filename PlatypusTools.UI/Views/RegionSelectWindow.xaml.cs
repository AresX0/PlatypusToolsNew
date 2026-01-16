using System;
using System.Drawing;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Window for selecting a screen region.
    /// </summary>
    public partial class RegionSelectWindow : Window
    {
        private bool _isSelecting;
        private System.Windows.Point _startPoint;
        
        /// <summary>
        /// Gets the selected region in screen coordinates.
        /// </summary>
        public Rectangle SelectedRegion { get; private set; }

        public RegionSelectWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(this);
            
            SelectionRectangle.Visibility = Visibility.Visible;
            SizeIndicator.Visibility = Visibility.Visible;
            
            UpdateSelectionRectangle(e.GetPosition(this));
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;
            
            UpdateSelectionRectangle(e.GetPosition(this));
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;
            
            _isSelecting = false;
            ReleaseMouseCapture();
            
            var endPoint = e.GetPosition(this);
            
            // Calculate the selection rectangle in window coordinates
            double rectX = Math.Min(_startPoint.X, endPoint.X);
            double rectY = Math.Min(_startPoint.Y, endPoint.Y);
            double rectWidth = Math.Abs(endPoint.X - _startPoint.X);
            double rectHeight = Math.Abs(endPoint.Y - _startPoint.Y);
            
            if (rectWidth > 5 && rectHeight > 5) // Minimum size check
            {
                // Convert window coordinates to screen coordinates
                var topLeft = PointToScreen(new System.Windows.Point(rectX, rectY));
                
                SelectedRegion = new Rectangle(
                    (int)topLeft.X, 
                    (int)topLeft.Y, 
                    (int)rectWidth, 
                    (int)rectHeight);
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }
            
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void UpdateSelectionRectangle(System.Windows.Point currentPoint)
        {
            double x = Math.Min(_startPoint.X, currentPoint.X);
            double y = Math.Min(_startPoint.Y, currentPoint.Y);
            double width = Math.Abs(currentPoint.X - _startPoint.X);
            double height = Math.Abs(currentPoint.Y - _startPoint.Y);
            
            SelectionRectangle.Margin = new Thickness(x, y, 0, 0);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;
            SelectionRectangle.HorizontalAlignment = HorizontalAlignment.Left;
            SelectionRectangle.VerticalAlignment = VerticalAlignment.Top;
            
            // Update size indicator
            SizeText.Text = $"{(int)width} x {(int)height}";
            SizeIndicator.Margin = new Thickness(x + width + 10, y, 0, 0);
        }
    }
}
