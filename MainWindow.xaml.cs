using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ImageToModelConverter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ModelGeometryService _geometryService;
        private BitmapSource _currentImage;
        private MeshGeometry3D _currentMesh;
        private Point _lastMousePosition;
        private bool _isRotating;
        private bool _isPanning;

        // New fields for mirror and reference plane
        private ModelVisual3D _referencePlaneVisual;
        private bool _showReferencePlane = true;
        private MirrorAxis _currentMirrorAxis = MirrorAxis.None;

        public MainWindow()
        {
            InitializeComponent();
            _geometryService = new ModelGeometryService();
            SetupCameraControls();
            InitializeReferencePlane();
        }

        private void InitializeReferencePlane()
        {
            // Create a reference plane (grid) at Y=0
            _referencePlaneVisual = new ModelVisual3D();
            Model3DGroup planeGroup = new Model3DGroup();

            // Create grid lines
            double gridSize = 10;
            double spacing = 0.5;

            for (double i = -gridSize; i <= gridSize; i += spacing)
            {
                // Lines parallel to X-axis
                planeGroup.Children.Add(CreateGridLine(
                    new Point3D(-gridSize, 0, i),
                    new Point3D(gridSize, 0, i)));

                // Lines parallel to Z-axis
                planeGroup.Children.Add(CreateGridLine(
                    new Point3D(i, 0, -gridSize),
                    new Point3D(i, 0, gridSize)));
            }

            _referencePlaneVisual.Content = planeGroup;
            viewport3D.Children.Add(_referencePlaneVisual);
        }

        private GeometryModel3D CreateGridLine(Point3D start, Point3D end)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            double thickness = 0.01;

            // Create a thin box for the line
            mesh.Positions.Add(new Point3D(start.X - thickness, start.Y, start.Z - thickness));
            mesh.Positions.Add(new Point3D(start.X + thickness, start.Y, start.Z + thickness));
            mesh.Positions.Add(new Point3D(end.X + thickness, end.Y, end.Z + thickness));
            mesh.Positions.Add(new Point3D(end.X - thickness, end.Y, end.Z - thickness));

            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);

            return new GeometryModel3D
            {
                Geometry = mesh,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)))
            };
        }

        private void SetupCameraControls()
        {
            viewport3D.MouseLeftButtonDown += Viewport_MouseLeftButtonDown;
            viewport3D.MouseLeftButtonUp += Viewport_MouseLeftButtonUp;
            viewport3D.MouseRightButtonDown += Viewport_MouseRightButtonDown;
            viewport3D.MouseRightButtonUp += Viewport_MouseRightButtonUp;
            viewport3D.MouseMove += Viewport_MouseMove;
            viewport3D.MouseWheel += Viewport_MouseWheel;
        }

        private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
                Title = "Select an Image"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dialog.FileName);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    _currentImage = bitmap;
                    imgPreview.Source = _currentImage;

                    txtImageInfo.Text = $"Size: {_currentImage.PixelWidth}x{_currentImage.PixelHeight}px\n" +
                                       $"Format: {_currentImage.Format}";

                    txtStatus.Text = "Image loaded successfully. Click 'Generate 3D Model' to continue.";
                    btnGenerate.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading image: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("Please load an image first.", "No Image",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnGenerate.IsEnabled = false;
            txtStatus.Text = "Generating 3D model...";

            try
            {
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ExtrusionMethod method = GetSelectedMethod();
                        DetailLevel detail = GetSelectedDetail();
                        double depth = sliderDepth.Value;
                        bool smoothNormals = chkSmoothNormals.IsChecked ?? true;

                        _currentMesh = _geometryService.GenerateMeshFromImage(
                            _currentImage, method, detail, depth, smoothNormals);
                    });
                });

                DisplayModel();
                btnExport.IsEnabled = true;
                txtStatus.Text = $"3D model generated successfully!\n" +
                                $"Vertices: {_currentMesh.Positions.Count:N0}\n" +
                                $"Triangles: {_currentMesh.TriangleIndices.Count / 3:N0}";

                txtVertexCount.Text = $"Vertices: {_currentMesh.Positions.Count:N0} | " +
                                     $"Triangles: {_currentMesh.TriangleIndices.Count / 3:N0}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating 3D model: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                txtStatus.Text = "Error generating model.";
            }
            finally
            {
                btnGenerate.IsEnabled = true;
            }
        }

        private void DisplayModel()
        {
            modelVisual.Content = null;

            Model3DGroup group = new Model3DGroup();

            // Original model
            GeometryModel3D model = new GeometryModel3D
            {
                Geometry = _currentMesh,
                Material = CreateMaterial(),
                BackMaterial = new DiffuseMaterial(Brushes.Gray)
            };
            group.Children.Add(model);

            // Add mirrored model if mirror is enabled
            if (_currentMirrorAxis != MirrorAxis.None)
            {
                MeshGeometry3D mirroredMesh = CreateMirroredMesh(_currentMesh, _currentMirrorAxis);
                GeometryModel3D mirroredModel = new GeometryModel3D
                {
                    Geometry = mirroredMesh,
                    Material = CreateMaterial(),
                    BackMaterial = new DiffuseMaterial(Brushes.Gray)
                };
                group.Children.Add(mirroredModel);
            }

            modelVisual.Content = group;
        }

        private MeshGeometry3D CreateMirroredMesh(MeshGeometry3D originalMesh, MirrorAxis axis)
        {
            MeshGeometry3D mirrored = new MeshGeometry3D();

            // Mirror positions
            foreach (Point3D position in originalMesh.Positions)
            {
                Point3D mirroredPos = position;
                switch (axis)
                {
                    case MirrorAxis.X:
                        mirroredPos.X = -position.X;
                        break;
                    case MirrorAxis.Y:
                        mirroredPos.Y = -position.Y;
                        break;
                    case MirrorAxis.Z:
                        mirroredPos.Z = -position.Z;
                        break;
                }
                mirrored.Positions.Add(mirroredPos);
            }

            // Mirror normals if they exist
            if (originalMesh.Normals.Count > 0)
            {
                foreach (Vector3D normal in originalMesh.Normals)
                {
                    Vector3D mirroredNormal = normal;
                    switch (axis)
                    {
                        case MirrorAxis.X:
                            mirroredNormal.X = -normal.X;
                            break;
                        case MirrorAxis.Y:
                            mirroredNormal.Y = -normal.Y;
                            break;
                        case MirrorAxis.Z:
                            mirroredNormal.Z = -normal.Z;
                            break;
                    }
                    mirrored.Normals.Add(mirroredNormal);
                }
            }

            // Mirror texture coordinates
            foreach (Point texCoord in originalMesh.TextureCoordinates)
            {
                mirrored.TextureCoordinates.Add(texCoord);
            }

            // Reverse triangle winding order for correct face orientation
            for (int i = 0; i < originalMesh.TriangleIndices.Count; i += 3)
            {
                mirrored.TriangleIndices.Add(originalMesh.TriangleIndices[i]);
                mirrored.TriangleIndices.Add(originalMesh.TriangleIndices[i + 2]);
                mirrored.TriangleIndices.Add(originalMesh.TriangleIndices[i + 1]);
            }

            return mirrored;
        }

        private Material CreateMaterial()
        {
            var selectedColor = (cmbColor.SelectedItem as ComboBoxItem)?.Content.ToString();

            if (selectedColor == "From Image" && _currentImage != null)
            {
                ImageBrush imageBrush = new ImageBrush(_currentImage)
                {
                    ViewportUnits = BrushMappingMode.Absolute
                };

                MaterialGroup materialGroup = new MaterialGroup();
                materialGroup.Children.Add(new DiffuseMaterial(imageBrush));
                materialGroup.Children.Add(new SpecularMaterial(Brushes.White,
                    sliderSpecular.Value * 100));

                return materialGroup;
            }
            else
            {
                Brush brush = selectedColor switch
                {
                    "Gray" => Brushes.Gray,
                    "Blue" => Brushes.CornflowerBlue,
                    "Red" => Brushes.IndianRed,
                    "Green" => Brushes.SeaGreen,
                    _ => Brushes.Gray
                };

                MaterialGroup materialGroup = new MaterialGroup();
                materialGroup.Children.Add(new DiffuseMaterial(brush));
                materialGroup.Children.Add(new SpecularMaterial(Brushes.White,
                    sliderSpecular.Value * 100));

                return materialGroup;
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMesh == null)
            {
                MessageBox.Show("Please generate a 3D model first.", "No Model",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "OBJ Files|*.obj|All Files|*.*",
                DefaultExt = ".obj",
                FileName = "model.obj"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // If mirror is enabled, export combined mesh
                    if (_currentMirrorAxis != MirrorAxis.None)
                    {
                        MeshGeometry3D combinedMesh = CombineMeshes(_currentMesh,
                            CreateMirroredMesh(_currentMesh, _currentMirrorAxis));
                        _geometryService.ExportModel(combinedMesh, dialog.FileName);
                    }
                    else
                    {
                        _geometryService.ExportModel(_currentMesh, dialog.FileName);
                    }

                    MessageBox.Show($"Model exported successfully to:\n{dialog.FileName}",
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtStatus.Text = "Model exported successfully.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting model: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private MeshGeometry3D CombineMeshes(MeshGeometry3D mesh1, MeshGeometry3D mesh2)
        {
            MeshGeometry3D combined = new MeshGeometry3D();
            int vertexOffset = 0;

            // Add first mesh
            foreach (var pos in mesh1.Positions)
                combined.Positions.Add(pos);
            foreach (var normal in mesh1.Normals)
                combined.Normals.Add(normal);
            foreach (var texCoord in mesh1.TextureCoordinates)
                combined.TextureCoordinates.Add(texCoord);
            foreach (var index in mesh1.TriangleIndices)
                combined.TriangleIndices.Add(index);

            vertexOffset = mesh1.Positions.Count;

            // Add second mesh with offset indices
            foreach (var pos in mesh2.Positions)
                combined.Positions.Add(pos);
            foreach (var normal in mesh2.Normals)
                combined.Normals.Add(normal);
            foreach (var texCoord in mesh2.TextureCoordinates)
                combined.TextureCoordinates.Add(texCoord);
            foreach (var index in mesh2.TriangleIndices)
                combined.TriangleIndices.Add(index + vertexOffset);

            return combined;
        }

        // New event handlers for mirror and reference plane
        private void BtnTogglePlane_Click(object sender, RoutedEventArgs e)
        {
            _showReferencePlane = !_showReferencePlane;

            // Show or hide by adding/removing from viewport
            if (_showReferencePlane)
            {
                if (!viewport3D.Children.Contains(_referencePlaneVisual))
                {
                    viewport3D.Children.Add(_referencePlaneVisual);
                }
            }
            else
            {
                if (viewport3D.Children.Contains(_referencePlaneVisual))
                {
                    viewport3D.Children.Remove(_referencePlaneVisual);
                }
            }

            if (sender is Button btn)
            {
                btn.Content = _showReferencePlane ? "Hide Reference Plane" : "Show Reference Plane";
            }

            txtStatus.Text = _showReferencePlane ?
                "Reference plane visible" : "Reference plane hidden";
        }

        private void CmbMirror_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            var selected = (cmbMirror.SelectedItem as ComboBoxItem)?.Content.ToString();
            _currentMirrorAxis = selected switch
            {
                "Mirror X" => MirrorAxis.X,
                "Mirror Y" => MirrorAxis.Y,
                "Mirror Z" => MirrorAxis.Z,
                _ => MirrorAxis.None
            };

            if (_currentMesh != null)
            {
                DisplayModel();

                int totalVertices = _currentMesh.Positions.Count;
                int totalTriangles = _currentMesh.TriangleIndices.Count / 3;

                if (_currentMirrorAxis != MirrorAxis.None)
                {
                    totalVertices *= 2;
                    totalTriangles *= 2;
                }

                txtVertexCount.Text = $"Vertices: {totalVertices:N0} | Triangles: {totalTriangles:N0}";
                txtStatus.Text = _currentMirrorAxis != MirrorAxis.None ?
                    $"Mirror enabled along {_currentMirrorAxis} axis" : "Mirror disabled";
            }
        }

        private ExtrusionMethod GetSelectedMethod()
        {
            var selected = (cmbMethod.SelectedItem as ComboBoxItem)?.Content.ToString();
            return selected switch
            {
                "Edge Detection" => ExtrusionMethod.EdgeDetection,
                "Contour Based" => ExtrusionMethod.ContourBased,
                _ => ExtrusionMethod.DepthMap
            };
        }

        private DetailLevel GetSelectedDetail()
        {
            return cmbDetail.SelectedIndex switch
            {
                0 => DetailLevel.Low,
                2 => DetailLevel.High,
                3 => DetailLevel.VeryHigh,
                4 => DetailLevel.Ultra,
                _ => DetailLevel.Medium
            };
        }

        // Camera Controls
        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isRotating = true;
            _lastMousePosition = e.GetPosition(viewport3D);
            viewport3D.CaptureMouse();
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isRotating = false;
            viewport3D.ReleaseMouseCapture();
        }

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isPanning = true;
            _lastMousePosition = e.GetPosition(viewport3D);
            viewport3D.CaptureMouse();
        }

        private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            viewport3D.ReleaseMouseCapture();
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentPosition = e.GetPosition(viewport3D);
            Vector delta = currentPosition - _lastMousePosition;

            if (_isRotating)
            {
                double angleX = delta.X * 0.5;
                double angleY = delta.Y * 0.5;
                RotateCamera(angleX, angleY);
            }
            else if (_isPanning)
            {
                Point3D position = camera.Position;
                position.X -= delta.X * 0.02;
                position.Y += delta.Y * 0.02;
                camera.Position = position;
            }

            _lastMousePosition = currentPosition;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point3D position = camera.Position;
            double zoomFactor = e.Delta > 0 ? 0.9 : 1.1;

            position.X *= zoomFactor;
            position.Y *= zoomFactor;
            position.Z *= zoomFactor;

            double distance = Math.Sqrt(position.X * position.X +
                                       position.Y * position.Y +
                                       position.Z * position.Z);

            if (distance > 5 && distance < 50)
            {
                camera.Position = position;
            }
        }

        private void RotateCamera(double angleX, double angleY)
        {
            Point3D position = camera.Position;

            double radius = Math.Sqrt(position.X * position.X +
                                     position.Y * position.Y +
                                     position.Z * position.Z);
            double theta = Math.Atan2(position.X, position.Z) + angleX * Math.PI / 180;
            double phi = Math.Acos(position.Y / radius) + angleY * Math.PI / 180;

            phi = Math.Max(0.1, Math.Min(Math.PI - 0.1, phi));

            position.X = radius * Math.Sin(phi) * Math.Sin(theta);
            position.Y = radius * Math.Cos(phi);
            position.Z = radius * Math.Sin(phi) * Math.Cos(theta);

            camera.Position = position;
            camera.LookDirection = new Vector3D(-position.X, -position.Y, -position.Z);
        }

        private void SliderDepth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentMesh != null && IsLoaded)
            {
                txtStatus.Text = "Depth changed. Click 'Generate 3D Model' to update.";
            }
        }

        private void CmbDetail_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentMesh != null && IsLoaded)
            {
                txtStatus.Text = "Detail level changed. Click 'Generate 3D Model' to update.";
            }
        }

        private void CmbMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentMesh != null && IsLoaded)
            {
                txtStatus.Text = "Method changed. Click 'Generate 3D Model' to update.";
            }
        }

        private void ChkSmoothNormals_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_currentMesh != null && IsLoaded)
            {
                txtStatus.Text = "Normals setting changed. Click 'Generate 3D Model' to update.";
            }
        }

        private void CmbColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentMesh != null && IsLoaded)
            {
                DisplayModel();
            }
        }

        private void SliderSpecular_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentMesh != null && IsLoaded)
            {
                DisplayModel();
            }
        }
    }

    // Enum for mirror axis
    public enum MirrorAxis
    {
        None,
        X,
        Y,
        Z
    }
}