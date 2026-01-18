using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Screenshot Tool.
    /// </summary>
    public class ScreenshotViewModel : BindableBase
    {
        private readonly ScreenCaptureService _captureService;
        private readonly ScreenshotAnnotationService _annotationService;
        private Bitmap? _currentScreenshot;
        
        public ScreenshotViewModel()
        {
            _captureService = new ScreenCaptureService();
            _annotationService = new ScreenshotAnnotationService();
            
            AnnotationColors = new ObservableCollection<System.Windows.Media.Color>
            {
                Colors.Red, Colors.Yellow, Colors.Green, Colors.Blue, 
                Colors.Orange, Colors.Purple, Colors.White, Colors.Black
            };
            SelectedColor = Colors.Red;
            
            // Commands
            CaptureFullScreenCommand = new RelayCommand(_ => CaptureFullScreen());
            CapturePrimaryScreenCommand = new RelayCommand(_ => CapturePrimaryScreen());
            CaptureActiveWindowCommand = new RelayCommand(_ => CaptureActiveWindow());
            CaptureRegionCommand = new RelayCommand(_ => CaptureRegion());
            CaptureScrollingCommand = new RelayCommand(_ => CaptureScrolling());
            
            SaveCommand = new RelayCommand(_ => Save(), _ => _currentScreenshot != null);
            CopyCommand = new RelayCommand(_ => CopyToClipboard(), _ => _currentScreenshot != null);
            
            // Annotation commands
            DrawArrowCommand = new RelayCommand(_ => SetAnnotationMode(AnnotationMode.Arrow), _ => _currentScreenshot != null);
            DrawRectangleCommand = new RelayCommand(_ => SetAnnotationMode(AnnotationMode.Rectangle), _ => _currentScreenshot != null);
            DrawEllipseCommand = new RelayCommand(_ => SetAnnotationMode(AnnotationMode.Ellipse), _ => _currentScreenshot != null);
            DrawTextCommand = new RelayCommand(_ => SetAnnotationMode(AnnotationMode.Text), _ => _currentScreenshot != null);
            DrawHighlightCommand = new RelayCommand(_ => SetAnnotationMode(AnnotationMode.Highlight), _ => _currentScreenshot != null);
            BlurRegionCommand = new RelayCommand(_ => SetAnnotationMode(AnnotationMode.Blur), _ => _currentScreenshot != null);
            DrawFreehandCommand = new RelayCommand(_ => SetAnnotationMode(AnnotationMode.Freehand), _ => _currentScreenshot != null);
            
            ClearAnnotationsCommand = new RelayCommand(_ => ClearAnnotations(), _ => _currentScreenshot != null);
        }
        
        #region Properties
        
        public ObservableCollection<System.Windows.Media.Color> AnnotationColors { get; }
        
        private ImageSource? _screenshotImage;
        public ImageSource? ScreenshotImage
        {
            get => _screenshotImage;
            set => SetProperty(ref _screenshotImage, value);
        }
        
        private System.Windows.Media.Color _selectedColor;
        public System.Windows.Media.Color SelectedColor
        {
            get => _selectedColor;
            set => SetProperty(ref _selectedColor, value);
        }
        
        private int _lineThickness = 3;
        public int LineThickness
        {
            get => _lineThickness;
            set => SetProperty(ref _lineThickness, value);
        }
        
        private string _annotationText = "";
        public string AnnotationText
        {
            get => _annotationText;
            set => SetProperty(ref _annotationText, value);
        }
        
        private AnnotationMode _currentMode = AnnotationMode.None;
        public AnnotationMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (SetProperty(ref _currentMode, value))
                {
                    RaisePropertyChanged(nameof(CurrentModeText));
                }
            }
        }
        
        public string CurrentModeText => CurrentMode switch
        {
            AnnotationMode.Arrow => "Arrow - Click and drag to draw",
            AnnotationMode.Rectangle => "Rectangle - Click and drag to draw",
            AnnotationMode.Ellipse => "Ellipse - Click and drag to draw",
            AnnotationMode.Text => "Text - Click to place text",
            AnnotationMode.Highlight => "Highlight - Click and drag to highlight",
            AnnotationMode.Blur => "Blur - Click and drag to blur",
            AnnotationMode.Freehand => "Freehand - Click and drag to draw",
            _ => "Select a capture mode or annotation tool"
        };
        
        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        private int _screenshotWidth;
        public int ScreenshotWidth
        {
            get => _screenshotWidth;
            set => SetProperty(ref _screenshotWidth, value);
        }
        
        private int _screenshotHeight;
        public int ScreenshotHeight
        {
            get => _screenshotHeight;
            set => SetProperty(ref _screenshotHeight, value);
        }
        
        #endregion
        
        #region Commands
        
        public ICommand CaptureFullScreenCommand { get; }
        public ICommand CapturePrimaryScreenCommand { get; }
        public ICommand CaptureActiveWindowCommand { get; }
        public ICommand CaptureRegionCommand { get; }
        public ICommand CaptureScrollingCommand { get; }
        
        public ICommand SaveCommand { get; }
        public ICommand CopyCommand { get; }
        
        public ICommand DrawArrowCommand { get; }
        public ICommand DrawRectangleCommand { get; }
        public ICommand DrawEllipseCommand { get; }
        public ICommand DrawTextCommand { get; }
        public ICommand DrawHighlightCommand { get; }
        public ICommand BlurRegionCommand { get; }
        public ICommand DrawFreehandCommand { get; }
        public ICommand ClearAnnotationsCommand { get; }
        
        #endregion
        
        #region Methods
        
        private void CaptureFullScreen()
        {
            Window? mainWindow = null;
            try
            {
                // Hide the main window briefly
                mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }
                
                System.Threading.Thread.Sleep(300); // Wait for window to minimize
                
                _currentScreenshot?.Dispose();
                _currentScreenshot = _captureService.CaptureFullScreen();
                UpdatePreview();
                StatusMessage = $"Captured full screen ({_currentScreenshot.Width}x{_currentScreenshot.Height})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
            }
        }
        
        private void CapturePrimaryScreen()
        {
            Window? mainWindow = null;
            try
            {
                mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }
                
                System.Threading.Thread.Sleep(300);
                
                _currentScreenshot?.Dispose();
                _currentScreenshot = _captureService.CapturePrimaryScreen();
                UpdatePreview();
                StatusMessage = $"Captured primary screen ({_currentScreenshot.Width}x{_currentScreenshot.Height})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
            }
        }
        
        private void CaptureActiveWindow()
        {
            Window? mainWindow = null;
            try
            {
                mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }
                
                System.Threading.Thread.Sleep(500); // Wait longer to ensure another window is active
                
                _currentScreenshot?.Dispose();
                _currentScreenshot = _captureService.CaptureActiveWindow();
                UpdatePreview();
                StatusMessage = $"Captured active window ({_currentScreenshot.Width}x{_currentScreenshot.Height})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
            }
        }
        
        private void CaptureRegion()
        {
            Window? mainWindow = null;
            try
            {
                mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }
                
                System.Threading.Thread.Sleep(300);
                
                // Open region selection window
                var regionWindow = new Views.RegionSelectWindow();
                var result = regionWindow.ShowDialog();
                
                if (result == true && regionWindow.SelectedRegion.Width > 0 && regionWindow.SelectedRegion.Height > 0)
                {
                    _currentScreenshot?.Dispose();
                    _currentScreenshot = _captureService.CaptureRegion(regionWindow.SelectedRegion);
                    UpdatePreview();
                    StatusMessage = $"Captured region ({_currentScreenshot.Width}x{_currentScreenshot.Height})";
                }
                else
                {
                    StatusMessage = "Region capture cancelled";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
            }
        }
        
        private async void CaptureScrolling()
        {
            Window? mainWindow = null;
            try
            {
                mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }
                
                StatusMessage = "Select a window to capture scrolling content...";
                await System.Threading.Tasks.Task.Delay(500);
                
                _currentScreenshot?.Dispose();
                _currentScreenshot = await _captureService.CaptureScrollingWindowAsync();
                
                if (_currentScreenshot != null)
                {
                    UpdatePreview();
                    StatusMessage = $"Captured scrolling content ({_currentScreenshot.Width}x{_currentScreenshot.Height})";
                }
                else
                {
                    StatusMessage = "Scrolling capture cancelled or failed";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }
            }
        }
        
        private void UpdatePreview()
        {
            if (_currentScreenshot == null) return;
            
            ScreenshotWidth = _currentScreenshot.Width;
            ScreenshotHeight = _currentScreenshot.Height;
            
            // Convert to WPF ImageSource
            var hBitmap = _currentScreenshot.GetHbitmap();
            try
            {
                ScreenshotImage = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
            
            RaiseCommandsCanExecuteChanged();
        }
        
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        
        private void Save()
        {
            if (_currentScreenshot == null) return;
            
            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
                FileName = _captureService.GenerateFilename(),
                Title = "Save Screenshot"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _captureService.SaveScreenshot(_currentScreenshot, dialog.FileName);
                    StatusMessage = $"Saved to {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Save failed: {ex.Message}";
                }
            }
        }
        
        private void CopyToClipboard()
        {
            if (_currentScreenshot == null) return;
            
            try
            {
                // Use WPF Clipboard directly since CopyToClipboard is in the UI layer
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    _currentScreenshot.GetHbitmap(),
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                System.Windows.Clipboard.SetImage(bitmapSource);
                StatusMessage = "Copied to clipboard";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Copy failed: {ex.Message}";
            }
        }
        
        private void SetAnnotationMode(AnnotationMode mode)
        {
            CurrentMode = mode;
        }
        
        private void ClearAnnotations()
        {
            // Re-capture the same area to clear annotations
            StatusMessage = "Use capture buttons to get a fresh screenshot";
        }
        
        /// <summary>
        /// Called from the view when user draws an annotation.
        /// </summary>
        public void ApplyAnnotation(System.Drawing.Point start, System.Drawing.Point end)
        {
            if (_currentScreenshot == null) return;
            
            var drawColor = System.Drawing.Color.FromArgb(SelectedColor.A, SelectedColor.R, SelectedColor.G, SelectedColor.B);
            
            switch (CurrentMode)
            {
                case AnnotationMode.Arrow:
                    _annotationService.DrawArrow(_currentScreenshot, start, end, drawColor, LineThickness);
                    break;
                    
                case AnnotationMode.Rectangle:
                    var rect = GetRectFromPoints(start, end);
                    _annotationService.DrawRectangle(_currentScreenshot, rect, drawColor, LineThickness);
                    break;
                    
                case AnnotationMode.Ellipse:
                    var ellipseRect = GetRectFromPoints(start, end);
                    _annotationService.DrawEllipse(_currentScreenshot, ellipseRect, drawColor, LineThickness);
                    break;
                    
                case AnnotationMode.Text:
                    if (!string.IsNullOrEmpty(AnnotationText))
                        _annotationService.DrawText(_currentScreenshot, AnnotationText, start, drawColor);
                    break;
                    
                case AnnotationMode.Highlight:
                    var highlightRect = GetRectFromPoints(start, end);
                    _annotationService.DrawHighlight(_currentScreenshot, highlightRect, System.Drawing.Color.Yellow);
                    break;
                    
                case AnnotationMode.Blur:
                    var blurRect = GetRectFromPoints(start, end);
                    _annotationService.BlurRegion(_currentScreenshot, blurRect, 15);
                    break;
            }
            
            UpdatePreview();
        }
        
        /// <summary>
        /// Called from the view for freehand drawing.
        /// </summary>
        public void ApplyFreehandAnnotation(System.Drawing.Point[] points)
        {
            if (_currentScreenshot == null || points.Length < 2) return;
            
            var drawColor = System.Drawing.Color.FromArgb(SelectedColor.A, SelectedColor.R, SelectedColor.G, SelectedColor.B);
            _annotationService.DrawFreehand(_currentScreenshot, points, drawColor, LineThickness);
            
            UpdatePreview();
        }
        
        private Rectangle GetRectFromPoints(System.Drawing.Point p1, System.Drawing.Point p2)
        {
            int x = Math.Min(p1.X, p2.X);
            int y = Math.Min(p1.Y, p2.Y);
            int width = Math.Abs(p2.X - p1.X);
            int height = Math.Abs(p2.Y - p1.Y);
            
            return new Rectangle(x, y, width, height);
        }
        
        private void RaiseCommandsCanExecuteChanged()
        {
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DrawArrowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DrawRectangleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DrawEllipseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DrawTextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DrawHighlightCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BlurRegionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DrawFreehandCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearAnnotationsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        
        #endregion
    }
    
    /// <summary>
    /// Annotation drawing modes.
    /// </summary>
    public enum AnnotationMode
    {
        None,
        Arrow,
        Rectangle,
        Ellipse,
        Text,
        Highlight,
        Blur,
        Freehand
    }
}
