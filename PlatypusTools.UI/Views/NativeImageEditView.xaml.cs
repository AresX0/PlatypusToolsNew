using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = System.Windows.Media.Color;
using Image = SixLabors.ImageSharp.Image;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using WpfResizeMode = System.Windows.ResizeMode;

namespace PlatypusTools.UI.Views
{
    public partial class NativeImageEditView : UserControl
    {
        private Image<Rgba32>? _currentImage;
        private string? _originalImagePath;
        private Stack<Image<Rgba32>> _undoStack = new();
        private const int MaxUndoLevels = 20;

        // Annotation state
        private enum EditMode { None, Arrow, Rectangle, Ellipse, Text, Highlight, Freehand, Fill, Crop, ColorPick, MagicSelect }
        private EditMode _currentMode = EditMode.None;
        private bool _isDrawing;
        private Point _startPoint;
        private Shape? _previewShape;
        private Polyline? _freehandLine;
        private List<Point> _freehandPoints = new();
        
        // Drawing settings
        private Color _selectedColor = Colors.Red;
        private Color _pickedColor = Colors.Transparent;
        private int _lineThickness = 3;
        private int _colorTolerance = 30;

        public NativeImageEditView()
        {
            InitializeComponent();
            ThicknessSlider.ValueChanged += (s, e) => ThicknessText.Text = $"{(int)ThicknessSlider.Value} px";
            ToleranceSlider.ValueChanged += (s, e) => ToleranceText.Text = $"{(int)ToleranceSlider.Value}%";
        }

        #region Image Loading

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tiff;*.tif;*.webp|All Files|*.*",
                Title = "Select Image File"
            };
            if (dialog.ShowDialog() == true)
            {
                LoadImage(dialog.FileName);
            }
        }

        private void NewImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewImageDialog();
            if (dialog.ShowDialog() == true)
            {
                CreateNewImage(dialog.ImageWidth, dialog.ImageHeight, dialog.BackgroundColor);
            }
        }

        private void PasteImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    var bitmapSource = Clipboard.GetImage();
                    if (bitmapSource != null)
                    {
                        // Convert BitmapSource to ImageSharp Image
                        using var ms = new MemoryStream();
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                        encoder.Save(ms);
                        ms.Position = 0;

                        _currentImage?.Dispose();
                        _currentImage = Image.Load<Rgba32>(ms);
                        _originalImagePath = null;
                        ImageFilePathBox.Text = "(Pasted from clipboard)";
                        PlaceholderText.Visibility = Visibility.Collapsed;
                        _undoStack.Clear();
                        UpdatePreview();
                        StatusText.Text = $"Pasted: {_currentImage.Width}x{_currentImage.Height}";
                    }
                }
                else
                {
                    MessageBox.Show("No image in clipboard.", "Paste", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error pasting image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadImage(string filePath)
        {
            try
            {
                _originalImagePath = filePath;
                _currentImage?.Dispose();
                _currentImage = Image.Load<Rgba32>(filePath);
                ImageFilePathBox.Text = filePath;
                PlaceholderText.Visibility = Visibility.Collapsed;
                _undoStack.Clear();
                UpdatePreview();
                StatusText.Text = $"Loaded: {_currentImage.Width}x{_currentImage.Height}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateNewImage(int width, int height, Color bgColor)
        {
            try
            {
                _currentImage?.Dispose();
                _currentImage = new Image<Rgba32>(width, height);

                // Fill with background color
                var rgba = new Rgba32(bgColor.R, bgColor.G, bgColor.B, bgColor.A);
                _currentImage.Mutate(ctx => ctx.BackgroundColor(rgba));

                _originalImagePath = null;
                ImageFilePathBox.Text = $"(New {width}x{height})";
                PlaceholderText.Visibility = Visibility.Collapsed;
                _undoStack.Clear();
                UpdatePreview();
                StatusText.Text = $"Created new image: {width}x{height}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePreview()
        {
            if (_currentImage == null) return;

            try
            {
                using var ms = new MemoryStream();
                _currentImage.SaveAsPng(ms);
                ms.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                ImagePreview.Source = bitmap;
                DrawingOverlay.Width = _currentImage.Width;
                DrawingOverlay.Height = _currentImage.Height;
                ImageInfoText.Text = $"Size: {_currentImage.Width} x {_currentImage.Height}\nFormat: RGBA 32-bit";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating preview: {ex.Message}");
            }
        }

        private void SaveUndoState()
        {
            if (_currentImage == null) return;

            if (_undoStack.Count >= MaxUndoLevels)
            {
                // Remove oldest
                var list = _undoStack.ToList();
                list[list.Count - 1].Dispose();
                list.RemoveAt(list.Count - 1);
                _undoStack = new Stack<Image<Rgba32>>(list.AsEnumerable().Reverse());
            }

            _undoStack.Push(_currentImage.Clone());
        }

        #endregion

        #region Transform Operations

        private void RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Rotate(RotateMode.Rotate270));
            UpdatePreview();
            StatusText.Text = "Rotated left 90°";
        }

        private void RotateRight_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Rotate(RotateMode.Rotate90));
            UpdatePreview();
            StatusText.Text = "Rotated right 90°";
        }

        private void FlipH_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Flip(FlipMode.Horizontal));
            UpdatePreview();
            StatusText.Text = "Flipped horizontally";
        }

        private void FlipV_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Flip(FlipMode.Vertical));
            UpdatePreview();
            StatusText.Text = "Flipped vertically";
        }

        private void StartCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SetMode(EditMode.Crop);
        }

        private void Resize_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;

            var dialog = new ResizeDialog(_currentImage.Width, _currentImage.Height);
            if (dialog.ShowDialog() == true)
            {
                SaveUndoState();
                _currentImage.Mutate(x => x.Resize(dialog.NewWidth, dialog.NewHeight));
                UpdatePreview();
                StatusText.Text = $"Resized to {dialog.NewWidth}x{dialog.NewHeight}";
            }
        }

        #endregion

        #region Effects

        private void Grayscale_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Grayscale());
            UpdatePreview();
            StatusText.Text = "Applied grayscale";
        }

        private void Invert_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Invert());
            UpdatePreview();
            StatusText.Text = "Inverted colors";
        }

        private void Brightness_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Brightness(1.2f));
            UpdatePreview();
            StatusText.Text = "Increased brightness";
        }

        private void Contrast_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.Contrast(1.2f));
            UpdatePreview();
            StatusText.Text = "Increased contrast";
        }

        private void Blur_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.GaussianBlur(3));
            UpdatePreview();
            StatusText.Text = "Applied blur";
        }

        private void Sharpen_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SaveUndoState();
            _currentImage.Mutate(x => x.GaussianSharpen(2));
            UpdatePreview();
            StatusText.Text = "Applied sharpen";
        }

        #endregion

        #region Annotation Mode

        private void SetAnnotationMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string mode)
            {
                var editMode = mode switch
                {
                    "Arrow" => EditMode.Arrow,
                    "Rectangle" => EditMode.Rectangle,
                    "Ellipse" => EditMode.Ellipse,
                    "Text" => EditMode.Text,
                    "Highlight" => EditMode.Highlight,
                    "Freehand" => EditMode.Freehand,
                    "Fill" => EditMode.Fill,
                    _ => EditMode.None
                };
                SetMode(editMode);
            }
        }

        private void ShapeButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Right-click toggles fill mode and sets the annotation mode
            FillShapesCheckBox.IsChecked = !FillShapesCheckBox.IsChecked;
            if (sender is Button btn && btn.Tag is string mode)
            {
                var editMode = mode switch
                {
                    "Arrow" => EditMode.Arrow,
                    "Rectangle" => EditMode.Rectangle,
                    "Ellipse" => EditMode.Ellipse,
                    _ => EditMode.None
                };
                SetMode(editMode);
                StatusText.Text = FillShapesCheckBox.IsChecked == true ? $"{mode} mode (Filled)" : $"{mode} mode (Outline)";
            }
            e.Handled = true;
        }

        private void SetMode(EditMode mode)
        {
            _currentMode = mode;
            CurrentToolText.Text = mode.ToString();
            DrawingOverlay.Cursor = mode == EditMode.ColorPick ? Cursors.Cross : 
                                    mode == EditMode.Fill ? Cursors.Hand : Cursors.Arrow;
        }

        private void ColorPicker_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SetMode(EditMode.ColorPick);
            StatusText.Text = "Click on image to pick a color";
        }

        private void ReplaceColor_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;

            if (_pickedColor == Colors.Transparent)
            {
                MessageBox.Show("First use Color Picker to select a color to replace.", "Replace Color", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveUndoState();
            int tolerance = (int)ToleranceSlider.Value;
            var targetRgba = new Rgba32(_pickedColor.R, _pickedColor.G, _pickedColor.B, _pickedColor.A);
            var replaceRgba = new Rgba32(_selectedColor.R, _selectedColor.G, _selectedColor.B, _selectedColor.A);

            int replaced = 0;
            _currentImage.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        if (ColorDistance(row[x], targetRgba) <= tolerance)
                        {
                            row[x] = replaceRgba;
                            replaced++;
                        }
                    }
                }
            });

            UpdatePreview();
            StatusText.Text = $"Replaced {replaced} pixels";
        }

        private void RemoveBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;

            // Get the corner colors to detect background
            var corners = new[]
            {
                _currentImage[0, 0],
                _currentImage[_currentImage.Width - 1, 0],
                _currentImage[0, _currentImage.Height - 1],
                _currentImage[_currentImage.Width - 1, _currentImage.Height - 1]
            };

            // Use the most common corner color as background
            var bgColor = corners.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;

            SaveUndoState();
            int tolerance = (int)ToleranceSlider.Value;
            int removed = 0;

            _currentImage.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        if (ColorDistance(row[x], bgColor) <= tolerance)
                        {
                            row[x] = new Rgba32(0, 0, 0, 0); // Transparent
                            removed++;
                        }
                    }
                }
            });

            UpdatePreview();
            StatusText.Text = $"Removed {removed} background pixels";
        }

        private void MagicSelect_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null) return;
            SetMode(EditMode.MagicSelect);
            StatusText.Text = "Click on image to select similar colors";
        }

        private int ColorDistance(Rgba32 c1, Rgba32 c2)
        {
            int dr = c1.R - c2.R;
            int dg = c1.G - c2.G;
            int db = c1.B - c2.B;
            return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count == 0)
            {
                StatusText.Text = "Nothing to undo";
                return;
            }

            _currentImage?.Dispose();
            _currentImage = _undoStack.Pop();
            UpdatePreview();
            StatusText.Text = $"Undo ({_undoStack.Count} remaining)";
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_originalImagePath) && File.Exists(_originalImagePath))
            {
                LoadImage(_originalImagePath);
                StatusText.Text = "Reset to original";
            }
            else
            {
                StatusText.Text = "No original to reset to";
            }
        }

        #endregion

        #region Drawing Canvas Events

        private void DrawingOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentImage == null) return;
            if (_currentMode == EditMode.None) return;

            _startPoint = e.GetPosition(DrawingOverlay);
            int x = (int)_startPoint.X;
            int y = (int)_startPoint.Y;

            // Bounds check
            if (x < 0 || x >= _currentImage.Width || y < 0 || y >= _currentImage.Height)
                return;

            if (_currentMode == EditMode.ColorPick)
            {
                // Pick color from image
                var pixel = _currentImage[x, y];
                _pickedColor = Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B);
                PickedColorPreview.Background = new SolidColorBrush(_pickedColor);
                PickedColorHex.Text = $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}";
                StatusText.Text = $"Picked color: #{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}";
                return;
            }

            if (_currentMode == EditMode.Fill)
            {
                SaveUndoState();
                FloodFill(x, y, new Rgba32(_selectedColor.R, _selectedColor.G, _selectedColor.B, _selectedColor.A));
                UpdatePreview();
                return;
            }

            if (_currentMode == EditMode.MagicSelect)
            {
                // Highlight similar colors
                var targetColor = _currentImage[x, y];
                SaveUndoState();
                int tolerance = (int)ToleranceSlider.Value;
                var highlightColor = new Rgba32(255, 0, 255, 128); // Magenta overlay

                _currentImage.ProcessPixelRows(accessor =>
                {
                    for (int py = 0; py < accessor.Height; py++)
                    {
                        var row = accessor.GetRowSpan(py);
                        for (int px = 0; px < row.Length; px++)
                        {
                            if (ColorDistance(row[px], targetColor) <= tolerance)
                            {
                                // Blend with highlight
                                row[px] = new Rgba32(
                                    (byte)((row[px].R + highlightColor.R) / 2),
                                    (byte)((row[px].G + highlightColor.G) / 2),
                                    (byte)((row[px].B + highlightColor.B) / 2),
                                    row[px].A);
                            }
                        }
                    }
                });

                UpdatePreview();
                StatusText.Text = "Magic select applied - similar colors highlighted";
                return;
            }

            _isDrawing = true;
            _freehandPoints.Clear();
            _freehandPoints.Add(_startPoint);

            CreatePreviewShape();
            DrawingOverlay.CaptureMouse();
        }

        private void CreatePreviewShape()
        {
            _lineThickness = (int)ThicknessSlider.Value;
            var brush = new SolidColorBrush(_selectedColor);

            switch (_currentMode)
            {
                case EditMode.Arrow:
                    _previewShape = new Line
                    {
                        Stroke = brush,
                        StrokeThickness = _lineThickness,
                        X1 = _startPoint.X,
                        Y1 = _startPoint.Y,
                        X2 = _startPoint.X,
                        Y2 = _startPoint.Y,
                        StrokeEndLineCap = PenLineCap.Triangle
                    };
                    DrawingOverlay.Children.Add(_previewShape);
                    break;

                case EditMode.Rectangle:
                case EditMode.Crop:
                    bool fillRect = FillShapesCheckBox.IsChecked == true && _currentMode == EditMode.Rectangle;
                    _previewShape = new Rectangle
                    {
                        Stroke = _currentMode == EditMode.Crop ? System.Windows.Media.Brushes.DarkBlue : brush,
                        StrokeThickness = _currentMode == EditMode.Crop ? 2 : _lineThickness,
                        StrokeDashArray = _currentMode == EditMode.Crop ? new DoubleCollection { 4, 2 } : null,
                        Fill = fillRect ? brush : new SolidColorBrush(Color.FromArgb(50, _selectedColor.R, _selectedColor.G, _selectedColor.B))
                    };
                    Canvas.SetLeft(_previewShape, _startPoint.X);
                    Canvas.SetTop(_previewShape, _startPoint.Y);
                    DrawingOverlay.Children.Add(_previewShape);
                    break;

                case EditMode.Ellipse:
                    bool fillEllipse = FillShapesCheckBox.IsChecked == true;
                    _previewShape = new Ellipse
                    {
                        Stroke = brush,
                        StrokeThickness = _lineThickness,
                        Fill = fillEllipse ? brush : new SolidColorBrush(Color.FromArgb(50, _selectedColor.R, _selectedColor.G, _selectedColor.B))
                    };
                    Canvas.SetLeft(_previewShape, _startPoint.X);
                    Canvas.SetTop(_previewShape, _startPoint.Y);
                    DrawingOverlay.Children.Add(_previewShape);
                    break;

                case EditMode.Highlight:
                    _previewShape = new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0))
                    };
                    Canvas.SetLeft(_previewShape, _startPoint.X);
                    Canvas.SetTop(_previewShape, _startPoint.Y);
                    DrawingOverlay.Children.Add(_previewShape);
                    break;

                case EditMode.Freehand:
                    _freehandLine = new Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = _lineThickness,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    _freehandLine.Points.Add(_startPoint);
                    DrawingOverlay.Children.Add(_freehandLine);
                    break;

                case EditMode.Text:
                    // Just show cursor
                    _previewShape = new Rectangle
                    {
                        Width = 2,
                        Height = 20,
                        Fill = brush
                    };
                    Canvas.SetLeft(_previewShape, _startPoint.X);
                    Canvas.SetTop(_previewShape, _startPoint.Y);
                    DrawingOverlay.Children.Add(_previewShape);
                    break;
            }
        }

        private void DrawingOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;
            var pos = e.GetPosition(DrawingOverlay);

            switch (_currentMode)
            {
                case EditMode.Arrow:
                    if (_previewShape is Line line)
                    {
                        line.X2 = pos.X;
                        line.Y2 = pos.Y;
                    }
                    break;

                case EditMode.Rectangle:
                case EditMode.Highlight:
                case EditMode.Crop:
                    if (_previewShape != null)
                    {
                        double x = Math.Min(_startPoint.X, pos.X);
                        double y = Math.Min(_startPoint.Y, pos.Y);
                        double w = Math.Abs(pos.X - _startPoint.X);
                        double h = Math.Abs(pos.Y - _startPoint.Y);
                        Canvas.SetLeft(_previewShape, x);
                        Canvas.SetTop(_previewShape, y);
                        _previewShape.Width = w;
                        _previewShape.Height = h;
                    }
                    break;

                case EditMode.Ellipse:
                    if (_previewShape is Ellipse ellipse)
                    {
                        double x = Math.Min(_startPoint.X, pos.X);
                        double y = Math.Min(_startPoint.Y, pos.Y);
                        double w = Math.Abs(pos.X - _startPoint.X);
                        double h = Math.Abs(pos.Y - _startPoint.Y);
                        Canvas.SetLeft(ellipse, x);
                        Canvas.SetTop(ellipse, y);
                        ellipse.Width = w;
                        ellipse.Height = h;
                    }
                    break;

                case EditMode.Freehand:
                    if (_freehandLine != null)
                    {
                        _freehandLine.Points.Add(pos);
                        _freehandPoints.Add(pos);
                    }
                    break;
            }
        }

        private void DrawingOverlay_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            DrawingOverlay.ReleaseMouseCapture();

            var endPoint = e.GetPosition(DrawingOverlay);

            // Clear preview
            DrawingOverlay.Children.Clear();
            _previewShape = null;
            _freehandLine = null;

            if (_currentImage == null) return;

            // Apply annotation to image
            SaveUndoState();
            ApplyAnnotation(endPoint);
            UpdatePreview();
        }

        private void ApplyAnnotation(Point endPoint)
        {
            if (_currentImage == null) return;

            var color = new SixLabors.ImageSharp.Color(new Rgba32(_selectedColor.R, _selectedColor.G, _selectedColor.B, _selectedColor.A));
            int thickness = _lineThickness;

            int x1 = (int)_startPoint.X;
            int y1 = (int)_startPoint.Y;
            int x2 = (int)endPoint.X;
            int y2 = (int)endPoint.Y;

            bool fillShapes = FillShapesCheckBox.IsChecked == true;

            switch (_currentMode)
            {
                case EditMode.Arrow:
                    DrawLineOnImage(x1, y1, x2, y2, color, thickness);
                    // Draw arrowhead
                    DrawArrowhead(x1, y1, x2, y2, color, thickness);
                    StatusText.Text = "Arrow drawn";
                    break;

                case EditMode.Rectangle:
                    DrawRectangleOnImage(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1), color, thickness, fillShapes);
                    StatusText.Text = fillShapes ? "Filled rectangle drawn" : "Rectangle drawn";
                    break;

                case EditMode.Ellipse:
                    DrawEllipseOnImage(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1), color, thickness, fillShapes);
                    StatusText.Text = fillShapes ? "Filled ellipse drawn" : "Ellipse drawn";
                    break;

                case EditMode.Text:
                    string text = AnnotationTextBox.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        DrawTextOnImage(x1, y1, text, color);
                        StatusText.Text = $"Text drawn: {text}";
                    }
                    break;

                case EditMode.Highlight:
                    DrawHighlightOnImage(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
                    StatusText.Text = "Highlight applied";
                    break;

                case EditMode.Freehand:
                    if (_freehandPoints.Count > 1)
                    {
                        DrawFreehandOnImage(_freehandPoints, color, thickness);
                        StatusText.Text = "Freehand drawn";
                    }
                    break;

                case EditMode.Crop:
                    int cx = Math.Max(0, Math.Min(x1, x2));
                    int cy = Math.Max(0, Math.Min(y1, y2));
                    int cw = Math.Abs(x2 - x1);
                    int ch = Math.Abs(y2 - y1);
                    if (cw > 0 && ch > 0)
                    {
                        _currentImage.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(cx, cy, cw, ch)));
                        StatusText.Text = $"Cropped to {cw}x{ch}";
                    }
                    SetMode(EditMode.None);
                    break;
            }
        }

        private void DrawLineOnImage(int x1, int y1, int x2, int y2, SixLabors.ImageSharp.Color color, int thickness)
        {
            var pen = new SolidPen(color, thickness);
            _currentImage!.Mutate(ctx => ctx.DrawLine(pen, new SixLabors.ImageSharp.PointF(x1, y1), new SixLabors.ImageSharp.PointF(x2, y2)));
        }

        private void DrawArrowhead(int x1, int y1, int x2, int y2, SixLabors.ImageSharp.Color color, int thickness)
        {
            double angle = Math.Atan2(y2 - y1, x2 - x1);
            double arrowLength = thickness * 4;
            double arrowAngle = Math.PI / 6; // 30 degrees

            var p1 = new SixLabors.ImageSharp.PointF(
                (float)(x2 - arrowLength * Math.Cos(angle - arrowAngle)),
                (float)(y2 - arrowLength * Math.Sin(angle - arrowAngle)));
            var p2 = new SixLabors.ImageSharp.PointF(
                (float)(x2 - arrowLength * Math.Cos(angle + arrowAngle)),
                (float)(y2 - arrowLength * Math.Sin(angle + arrowAngle)));

            var pen = new SolidPen(color, thickness);
            _currentImage!.Mutate(ctx =>
            {
                ctx.DrawLine(pen, new SixLabors.ImageSharp.PointF(x2, y2), p1);
                ctx.DrawLine(pen, new SixLabors.ImageSharp.PointF(x2, y2), p2);
            });
        }

        private void DrawRectangleOnImage(int x, int y, int w, int h, SixLabors.ImageSharp.Color color, int thickness, bool fill = false)
        {
            if (w <= 0 || h <= 0) return;
            var rect = new RectangularPolygon(x, y, w, h);
            if (fill)
            {
                var brush = new SolidBrush(color);
                _currentImage!.Mutate(ctx => ctx.Fill(brush, rect));
            }
            else
            {
                var pen = new SolidPen(color, thickness);
                _currentImage!.Mutate(ctx => ctx.Draw(pen, rect));
            }
        }

        private void DrawEllipseOnImage(int x, int y, int w, int h, SixLabors.ImageSharp.Color color, int thickness, bool fill = false)
        {
            if (w <= 0 || h <= 0) return;
            var ellipse = new EllipsePolygon(x + w / 2f, y + h / 2f, w / 2f, h / 2f);
            if (fill)
            {
                var brush = new SolidBrush(color);
                _currentImage!.Mutate(ctx => ctx.Fill(brush, ellipse));
            }
            else
            {
                var pen = new SolidPen(color, thickness);
                _currentImage!.Mutate(ctx => ctx.Draw(pen, ellipse));
            }
        }

        private void DrawTextOnImage(int x, int y, string text, SixLabors.ImageSharp.Color color)
        {
            var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 16, SixLabors.Fonts.FontStyle.Regular);
            _currentImage!.Mutate(ctx => ctx.DrawText(text, font, color, new SixLabors.ImageSharp.PointF(x, y)));
        }

        private void DrawHighlightOnImage(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            var highlightColor = new Rgba32(255, 255, 0, 100);
            var brush = new SolidBrush(highlightColor);
            var rect = new RectangularPolygon(x, y, w, h);
            _currentImage!.Mutate(ctx => ctx.Fill(brush, rect));
        }

        private void DrawFreehandOnImage(List<Point> points, SixLabors.ImageSharp.Color color, int thickness)
        {
            if (points.Count < 2) return;
            var pen = new SolidPen(color, thickness);
            var imagePoints = points.Select(p => new SixLabors.ImageSharp.PointF((float)p.X, (float)p.Y)).ToArray();

            for (int i = 0; i < imagePoints.Length - 1; i++)
            {
                _currentImage!.Mutate(ctx => ctx.DrawLine(pen, imagePoints[i], imagePoints[i + 1]));
            }
        }

        private void FloodFill(int startX, int startY, Rgba32 fillColor)
        {
            if (_currentImage == null) return;

            var targetColor = _currentImage[startX, startY];
            if (targetColor.Equals(fillColor)) return;

            int tolerance = (int)ToleranceSlider.Value;
            var visited = new bool[_currentImage.Width, _currentImage.Height];
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                if (x < 0 || x >= _currentImage.Width || y < 0 || y >= _currentImage.Height)
                    continue;
                if (visited[x, y]) continue;
                visited[x, y] = true;

                if (ColorDistance(_currentImage[x, y], targetColor) <= tolerance)
                {
                    _currentImage[x, y] = fillColor;
                    queue.Enqueue((x + 1, y));
                    queue.Enqueue((x - 1, y));
                    queue.Enqueue((x, y + 1));
                    queue.Enqueue((x, y - 1));
                }
            }

            StatusText.Text = "Fill applied";
        }

        #endregion

        #region Color Selection

        private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Background is SolidColorBrush brush)
            {
                _selectedColor = brush.Color;
                SelectedColorPreview.Background = brush;
            }
        }

        private void CustomColor_Click(object sender, RoutedEventArgs e)
        {
            // Simple color dialog using Windows Forms
            using var colorDialog = new System.Windows.Forms.ColorDialog();
            colorDialog.Color = System.Drawing.Color.FromArgb(_selectedColor.A, _selectedColor.R, _selectedColor.G, _selectedColor.B);
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _selectedColor = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                SelectedColorPreview.Background = new SolidColorBrush(_selectedColor);
            }
        }

        private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ThicknessText != null)
                ThicknessText.Text = $"{(int)e.NewValue} px";
        }

        private void ToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ToleranceText != null)
                ToleranceText.Text = $"{(int)e.NewValue}%";
        }

        #endregion

        #region Save Operations

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("Please load or create an image first.", "No Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var format = (OutputFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToUpperInvariant() ?? "PNG";
            
            if (format == "SVG")
            {
                MessageBox.Show("SVG export requires vector graphics. Use PNG for raster images.", "SVG Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var extension = format.ToLowerInvariant();

            var saveDialog = new SaveFileDialog
            {
                Filter = $"{format} Files (*.{extension})|*.{extension}|All Files (*.*)|*.*",
                FileName = _originalImagePath != null
                    ? $"{System.IO.Path.GetFileNameWithoutExtension(_originalImagePath)}_edited.{extension}"
                    : $"image.{extension}",
                Title = "Save Image"
            };

            if (saveDialog.ShowDialog() != true) return;
            SaveImageToFile(saveDialog.FileName, format);
        }

        private void QuickSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("Please load or create an image first.", "No Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var format = (OutputFormat.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToUpperInvariant() ?? "PNG";
            var extension = format.ToLowerInvariant();

            string outputPath;
            if (!string.IsNullOrEmpty(_originalImagePath))
            {
                var dir = System.IO.Path.GetDirectoryName(_originalImagePath)!;
                var name = System.IO.Path.GetFileNameWithoutExtension(_originalImagePath);
                outputPath = System.IO.Path.Combine(dir, $"{name}_edited.{extension}");
            }
            else
            {
                outputPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"image_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
            }

            SaveImageToFile(outputPath, format);
        }

        private void SaveImageToFile(string filePath, string format)
        {
            try
            {
                switch (format)
                {
                    case "PNG":
                        _currentImage!.Save(filePath, new PngEncoder());
                        break;
                    case "JPEG":
                        _currentImage!.Save(filePath, new JpegEncoder { Quality = 90 });
                        break;
                    case "BMP":
                        _currentImage!.Save(filePath, new BmpEncoder());
                        break;
                    case "GIF":
                        _currentImage!.Save(filePath, new GifEncoder());
                        break;
                    case "WEBP":
                        _currentImage!.Save(filePath, new WebpEncoder { Quality = 90 });
                        break;
                    case "TIFF":
                        _currentImage!.Save(filePath, new TiffEncoder());
                        break;
                    default:
                        _currentImage!.Save(filePath, new PngEncoder());
                        break;
                }

                StatusText.Text = $"Saved: {System.IO.Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("No image to copy.", "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var ms = new MemoryStream();
                _currentImage.SaveAsPng(ms);
                ms.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                Clipboard.SetImage(bitmap);
                StatusText.Text = "Copied to clipboard";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }

    #region Dialogs

    public class NewImageDialog : Window
    {
        public int ImageWidth { get; private set; } = 800;
        public int ImageHeight { get; private set; } = 600;
        public Color BackgroundColor { get; private set; } = Colors.White;

        public NewImageDialog()
        {
            Title = "New Image";
            Width = 300;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = WpfResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var widthPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            widthPanel.Children.Add(new TextBlock { Text = "Width:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var widthBox = new TextBox { Width = 100, Text = "800" };
            widthPanel.Children.Add(widthBox);
            widthPanel.Children.Add(new TextBlock { Text = "px", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(widthPanel, 0);
            grid.Children.Add(widthPanel);

            var heightPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            heightPanel.Children.Add(new TextBlock { Text = "Height:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var heightBox = new TextBox { Width = 100, Text = "600" };
            heightPanel.Children.Add(heightBox);
            heightPanel.Children.Add(new TextBlock { Text = "px", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(heightPanel, 1);
            grid.Children.Add(heightPanel);

            var bgPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            bgPanel.Children.Add(new TextBlock { Text = "Background:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var bgCombo = new ComboBox { Width = 100 };
            bgCombo.Items.Add("White");
            bgCombo.Items.Add("Transparent");
            bgCombo.Items.Add("Black");
            bgCombo.SelectedIndex = 0;
            bgPanel.Children.Add(bgCombo);
            Grid.SetRow(bgPanel, 2);
            grid.Children.Add(bgPanel);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Create", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            okButton.Click += (s, e) =>
            {
                if (int.TryParse(widthBox.Text, out int w) && int.TryParse(heightBox.Text, out int h) && w > 0 && h > 0)
                {
                    ImageWidth = w;
                    ImageHeight = h;
                    BackgroundColor = bgCombo.SelectedItem?.ToString() switch
                    {
                        "Transparent" => Colors.Transparent,
                        "Black" => Colors.Black,
                        _ => Colors.White
                    };
                    DialogResult = true;
                }
                else
                {
                    MessageBox.Show("Please enter valid dimensions.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    public class ResizeDialog : Window
    {
        public int NewWidth { get; private set; }
        public int NewHeight { get; private set; }

        public ResizeDialog(int currentWidth, int currentHeight)
        {
            NewWidth = currentWidth;
            NewHeight = currentHeight;

            Title = "Resize Image";
            Width = 300;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = WpfResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var widthPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            widthPanel.Children.Add(new TextBlock { Text = "Width:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var widthBox = new TextBox { Width = 100, Text = currentWidth.ToString() };
            widthPanel.Children.Add(widthBox);
            widthPanel.Children.Add(new TextBlock { Text = "px", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(widthPanel, 0);
            grid.Children.Add(widthPanel);

            var heightPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            heightPanel.Children.Add(new TextBlock { Text = "Height:", Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var heightBox = new TextBox { Width = 100, Text = currentHeight.ToString() };
            heightPanel.Children.Add(heightBox);
            heightPanel.Children.Add(new TextBlock { Text = "px", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(heightPanel, 1);
            grid.Children.Add(heightPanel);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Resize", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            okButton.Click += (s, e) =>
            {
                if (int.TryParse(widthBox.Text, out int w) && int.TryParse(heightBox.Text, out int h) && w > 0 && h > 0)
                {
                    NewWidth = w;
                    NewHeight = h;
                    DialogResult = true;
                }
                else
                {
                    MessageBox.Show("Please enter valid dimensions.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    #endregion
}
