using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class Model3DEditorViewModel : BindableBase
    {
        #region Properties

        private string _currentFilePath = string.Empty;
        public string CurrentFilePath
        {
            get => _currentFilePath;
            set { _currentFilePath = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasFile)); RaisePropertyChanged(nameof(FileName)); }
        }

        public bool HasFile => !string.IsNullOrEmpty(CurrentFilePath) && File.Exists(CurrentFilePath);
        public string FileName => string.IsNullOrEmpty(CurrentFilePath) ? "No file loaded" : Path.GetFileName(CurrentFilePath);

        private Model3DGroup? _model3D;
        public Model3DGroup? Model3D
        {
            get => _model3D;
            set { _model3D = value; RaisePropertyChanged(); }
        }

        private double _extrusionDepth = 5.0;
        public double ExtrusionDepth
        {
            get => _extrusionDepth;
            set { _extrusionDepth = value; RaisePropertyChanged(); }
        }

        private double _scale = 1.0;
        public double Scale
        {
            get => _scale;
            set { _scale = value; RaisePropertyChanged(); }
        }

        private double _modelScale = 1.0;
        public double ModelScale
        {
            get => _modelScale;
            set { _modelScale = Math.Max(0.1, Math.Min(10, value)); RaisePropertyChanged(); UpdateTransform(); }
        }

        private double _rotationX = 0;
        public double RotationX
        {
            get => _rotationX;
            set { _rotationX = value; RaisePropertyChanged(); UpdateTransform(); }
        }

        private double _rotationY = 0;
        public double RotationY
        {
            get => _rotationY;
            set { _rotationY = value; RaisePropertyChanged(); UpdateTransform(); }
        }

        private double _rotationZ = 0;
        public double RotationZ
        {
            get => _rotationZ;
            set { _rotationZ = value; RaisePropertyChanged(); UpdateTransform(); }
        }

        private double _zoom = 100;
        public double Zoom
        {
            get => _zoom;
            set { _zoom = Math.Max(10, Math.Min(500, value)); RaisePropertyChanged(); UpdateCamera(); }
        }

        private double _panX = 0;
        public double PanX
        {
            get => _panX;
            set { _panX = value; RaisePropertyChanged(); UpdateCamera(); }
        }

        private double _panY = 0;
        public double PanY
        {
            get => _panY;
            set { _panY = value; RaisePropertyChanged(); UpdateCamera(); }
        }

        private double _positionX = 0;
        public double PositionX
        {
            get => _positionX;
            set { _positionX = value; RaisePropertyChanged(); UpdateTransform(); }
        }

        private double _positionY = 0;
        public double PositionY
        {
            get => _positionY;
            set { _positionY = value; RaisePropertyChanged(); UpdateTransform(); }
        }

        private double _positionZ = 0;
        public double PositionZ
        {
            get => _positionZ;
            set { _positionZ = value; RaisePropertyChanged(); UpdateTransform(); }
        }

        private Point3D _cameraPosition = new Point3D(0, 0, 100);
        public Point3D CameraPosition
        {
            get => _cameraPosition;
            set { _cameraPosition = value; RaisePropertyChanged(); }
        }

        private Transform3D? _modelTransform;
        public Transform3D? ModelTransform
        {
            get => _modelTransform;
            set { _modelTransform = value; RaisePropertyChanged(); }
        }

        private string _selectedShape = "Cube";
        public string SelectedShape
        {
            get => _selectedShape;
            set { _selectedShape = value; RaisePropertyChanged(); }
        }

        public ObservableCollection<string> AvailableShapes { get; } = new ObservableCollection<string>
        {
            "Cube", "Cylinder", "Sphere", "Pyramid", "Cone"
        };

        private string _textToExtrude = string.Empty;
        public string TextToExtrude
        {
            get => _textToExtrude;
            set { _textToExtrude = value; RaisePropertyChanged(); }
        }

        private double _textHeight = 10;
        public double TextHeight
        {
            get => _textHeight;
            set { _textHeight = value; RaisePropertyChanged(); }
        }

        private double _shapeSize = 20;
        public double ShapeSize
        {
            get => _shapeSize;
            set { _shapeSize = value; RaisePropertyChanged(); }
        }

        private Color _modelColor = Colors.SteelBlue;
        public Color ModelColor
        {
            get => _modelColor;
            set { _modelColor = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ModelBrush)); }
        }

        public SolidColorBrush ModelBrush => new SolidColorBrush(ModelColor);

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; RaisePropertyChanged(); }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; RaisePropertyChanged(); }
        }

        private string _log = string.Empty;
        public string Log
        {
            get => _log;
            set { _log = value; RaisePropertyChanged(); }
        }

        public ObservableCollection<string> RecentFiles { get; } = new ObservableCollection<string>();

        #endregion

        #region Commands

        public ICommand LoadSvgCommand { get; }
        public ICommand ExportStlCommand { get; }
        public ICommand ExportObjCommand { get; }
        public ICommand CreateShapeCommand { get; }
        public ICommand CreateTextCommand { get; }
        public ICommand ResetViewCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CenterModelCommand { get; }

        #endregion

        public Model3DEditorViewModel()
        {
            LoadSvgCommand = new RelayCommand(_ => LoadSvg());
            ExportStlCommand = new AsyncRelayCommand(ExportStlAsync, () => Model3D != null);
            ExportObjCommand = new AsyncRelayCommand(ExportObjAsync, () => Model3D != null);
            CreateShapeCommand = new RelayCommand(_ => CreateShape());
            CreateTextCommand = new RelayCommand(_ => CreateText());
            ResetViewCommand = new RelayCommand(_ => ResetView());
            ClearCommand = new RelayCommand(_ => Clear());
            CenterModelCommand = new RelayCommand(_ => CenterModel());

            UpdateTransform();
            UpdateCamera();
        }

        #region Methods

        private void LoadSvg()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SVG Files|*.svg|All Files|*.*",
                Title = "Select SVG file to convert to 3D"
            };

            if (dlg.ShowDialog() == true)
            {
                CurrentFilePath = dlg.FileName;
                AddLog($"Loaded: {FileName}");
                PreviewSvgAs3D();
            }
        }

        private void PreviewSvgAs3D()
        {
            if (!HasFile) return;

            try
            {
                // Create a temporary STL and load it for preview
                var tempStl = Path.Combine(Path.GetTempPath(), "preview.stl");
                if (SvgTo3DService.ConvertSvgToStl(CurrentFilePath, tempStl, (float)ExtrusionDepth, (float)Scale, out string diagnosticInfo))
                {
                    LoadStlModel(tempStl);
                    StatusMessage = "SVG loaded and previewing in 3D";
                    AddLog($"✓ 3D preview generated. {diagnosticInfo}");
                }
                else
                {
                    StatusMessage = "Failed to convert SVG to 3D";
                    AddLog($"Error: Could not parse SVG. {diagnosticInfo}");
                    AddLog("Tip: Only vector SVGs with paths/shapes can be converted. Embedded raster images are not supported.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AddLog($"Error: {ex.Message}");
            }
        }

        private void LoadStlModel(string stlPath)
        {
            try
            {
                var content = File.ReadAllText(stlPath);
                var modelGroup = new Model3DGroup();

                // Parse STL and create mesh
                var mesh = ParseStlToMesh(content);
                if (mesh != null)
                {
                    var material = new DiffuseMaterial(new SolidColorBrush(ModelColor));
                    var geometryModel = new GeometryModel3D(mesh, material);
                    geometryModel.BackMaterial = material;
                    modelGroup.Children.Add(geometryModel);
                    
                    // Add lighting
                    modelGroup.Children.Add(new AmbientLight(Color.FromRgb(80, 80, 80)));
                    modelGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1, -1)));
                    modelGroup.Children.Add(new DirectionalLight(Color.FromRgb(100, 100, 100), new Vector3D(1, 1, 1)));

                    Model3D = modelGroup;
                    
                    ((AsyncRelayCommand)ExportStlCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)ExportObjCommand).RaiseCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error loading STL: {ex.Message}");
            }
        }

        private MeshGeometry3D? ParseStlToMesh(string stlContent)
        {
            var mesh = new MeshGeometry3D();
            var lines = stlContent.Split('\n');
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("vertex"))
                {
                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        if (double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x) &&
                            double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y) &&
                            double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double z))
                        {
                            mesh.Positions.Add(new Point3D(x, y, z));
                            mesh.TriangleIndices.Add(mesh.Positions.Count - 1);
                        }
                    }
                }
            }

            return mesh.Positions.Count > 0 ? mesh : null;
        }

        private async Task ExportStlAsync()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "STL Files|*.stl",
                DefaultExt = ".stl",
                FileName = Path.GetFileNameWithoutExtension(CurrentFilePath) + ".stl"
            };

            if (dlg.ShowDialog() == true)
            {
                IsProcessing = true;
                StatusMessage = "Exporting STL...";

                try
                {
                    await Task.Run(() =>
                    {
                        if (!string.IsNullOrEmpty(CurrentFilePath) && File.Exists(CurrentFilePath))
                        {
                            SvgTo3DService.ConvertSvgToStl(CurrentFilePath, dlg.FileName, (float)ExtrusionDepth, (float)Scale);
                        }
                    });

                    StatusMessage = "STL exported successfully";
                    AddLog($"Exported: {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                    AddLog($"Error: {ex.Message}");
                }
                finally
                {
                    IsProcessing = false;
                }
            }
        }

        private async Task ExportObjAsync()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "OBJ Files|*.obj",
                DefaultExt = ".obj",
                FileName = Path.GetFileNameWithoutExtension(CurrentFilePath) + ".obj"
            };

            if (dlg.ShowDialog() == true)
            {
                IsProcessing = true;
                StatusMessage = "Exporting OBJ...";

                try
                {
                    await Task.Run(() =>
                    {
                        if (!string.IsNullOrEmpty(CurrentFilePath) && File.Exists(CurrentFilePath))
                        {
                            SvgTo3DService.ConvertSvgToObj(CurrentFilePath, dlg.FileName, (float)ExtrusionDepth, (float)Scale);
                        }
                    });

                    StatusMessage = "OBJ exported successfully";
                    AddLog($"Exported: {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                    AddLog($"Error: {ex.Message}");
                }
                finally
                {
                    IsProcessing = false;
                }
            }
        }

        private void CreateShape()
        {
            try
            {
                var tempStl = Path.Combine(Path.GetTempPath(), "shape.stl");
                var shape = SelectedShape switch
                {
                    "Cube" => BasicShape.Cube,
                    "Cylinder" => BasicShape.Cylinder,
                    "Sphere" => BasicShape.Sphere,
                    "Pyramid" => BasicShape.Pyramid,
                    "Cone" => BasicShape.Cone,
                    _ => BasicShape.Cube
                };

                if (SvgTo3DService.CreateBasicShape(shape, tempStl, (float)ShapeSize, (float)ExtrusionDepth))
                {
                    LoadStlModel(tempStl);
                    CurrentFilePath = tempStl;
                    StatusMessage = $"{SelectedShape} created";
                    AddLog($"Created {SelectedShape} ({ShapeSize}mm)");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AddLog($"Error creating shape: {ex.Message}");
            }
        }

        private void CreateText()
        {
            if (string.IsNullOrWhiteSpace(TextToExtrude))
            {
                StatusMessage = "Please enter text to create";
                AddLog("Error: No text entered");
                return;
            }

            try
            {
                var tempStl = Path.Combine(Path.GetTempPath(), $"text_{DateTime.Now:HHmmss}.stl");
                AddLog($"Creating 3D text: \"{TextToExtrude}\" (Height: {TextHeight}mm, Depth: {ExtrusionDepth}mm)");
                
                if (SvgTo3DService.CreateTextStl(TextToExtrude, tempStl, (float)TextHeight, (float)ExtrusionDepth))
                {
                    if (File.Exists(tempStl))
                    {
                        LoadStlModel(tempStl);
                        CurrentFilePath = tempStl;
                        StatusMessage = "Text model created";
                        AddLog($"✓ Created 3D text: \"{TextToExtrude}\"");
                    }
                    else
                    {
                        StatusMessage = "Failed to create text STL";
                        AddLog("Error: STL file was not created");
                    }
                }
                else
                {
                    StatusMessage = "Failed to create text model";
                    AddLog("Error: CreateTextStl returned false");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AddLog($"Error creating text: {ex.Message}");
            }
        }

        private void CenterModel()
        {
            PositionX = 0;
            PositionY = 0;
            PositionZ = 0;
            StatusMessage = "Model centered";
        }

        private void ResetView()
        {
            RotationX = 0;
            RotationY = 0;
            RotationZ = 0;
            PositionX = 0;
            PositionY = 0;
            PositionZ = 0;
            PanX = 0;
            PanY = 0;
            Zoom = 100;
            ModelScale = 1.0;
            StatusMessage = "View reset";
        }

        private void Clear()
        {
            Model3D = null;
            CurrentFilePath = string.Empty;
            StatusMessage = "Cleared";
            Log = string.Empty;
            PositionX = 0;
            PositionY = 0;
            PositionZ = 0;
            ModelScale = 1.0;
            
            ((AsyncRelayCommand)ExportStlCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ExportObjCommand).RaiseCanExecuteChanged();
        }

        private void UpdateTransform()
        {
            var transformGroup = new Transform3DGroup();
            // Apply scale
            transformGroup.Children.Add(new ScaleTransform3D(ModelScale, ModelScale, ModelScale));
            // Apply translation (position)
            transformGroup.Children.Add(new TranslateTransform3D(PositionX, PositionY, PositionZ));
            // Apply rotations
            transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), RotationX)));
            transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), RotationY)));
            transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), RotationZ)));
            ModelTransform = transformGroup;
        }

        private void UpdateCamera()
        {
            CameraPosition = new Point3D(PanX, PanY, Zoom);
        }

        private void AddLog(string message)
        {
            Log += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        }

        #endregion
    }
}
