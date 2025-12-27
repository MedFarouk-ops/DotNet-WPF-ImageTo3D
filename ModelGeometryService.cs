using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Collections.Generic;
using System.Linq;
using Assimp;
using Assimp.Configs;

namespace ImageToModelConverter
{
    public enum ExtrusionMethod
    {
        DepthMap,
        EdgeDetection,
        ContourBased
    }

    public enum DetailLevel
    {
        Low,
        Medium,
        High
    }

    public class ModelGeometryService
    {
        public MeshGeometry3D GenerateMeshFromImage(
            BitmapSource image,
            ExtrusionMethod method,
            DetailLevel detail,
            double extrusionDepth,
            bool smoothNormals)
        {
            switch (method)
            {
                case ExtrusionMethod.DepthMap:
                    return GenerateDepthMapMesh(image, detail, extrusionDepth, smoothNormals);
                case ExtrusionMethod.EdgeDetection:
                    return GenerateEdgeBasedMesh(image, detail, extrusionDepth, smoothNormals);
                case ExtrusionMethod.ContourBased:
                    return GenerateContourMesh(image, detail, extrusionDepth, smoothNormals);
                default:
                    return GenerateDepthMapMesh(image, detail, extrusionDepth, smoothNormals);
            }
        }

        private MeshGeometry3D GenerateDepthMapMesh(
            BitmapSource image,
            DetailLevel detail,
            double depth,
            bool smoothNormals)
        {
            int step = GetStepSize(detail);
            int width = image.PixelWidth;
            int height = image.PixelHeight;

            FormatConvertedBitmap converted = new FormatConvertedBitmap(
                image, PixelFormats.Bgra32, null, 0);

            byte[] pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);

            MeshGeometry3D mesh = new MeshGeometry3D();

            // Generate vertices
            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    int idx = (y * width + x) * 4;

                    double brightness = (pixels[idx + 2] * 0.299 +
                                       pixels[idx + 1] * 0.587 +
                                       pixels[idx] * 0.114) / 255.0;

                    double z = brightness * depth;

                    double nx = (x - width / 2.0) / (width / 2.0) * 5;
                    double ny = -(y - height / 2.0) / (height / 2.0) * 5;

                    mesh.Positions.Add(new Point3D(nx, ny, z));
                    mesh.TextureCoordinates.Add(new Point((double)x / width, (double)y / height));
                }
            }

            // Generate triangles
            int cols = (width / step);
            int rows = (height / step);

            for (int y = 0; y < rows - 1; y++)
            {
                for (int x = 0; x < cols - 1; x++)
                {
                    int topLeft = y * cols + x;
                    int topRight = topLeft + 1;
                    int bottomLeft = (y + 1) * cols + x;
                    int bottomRight = bottomLeft + 1;

                    mesh.TriangleIndices.Add(topLeft);
                    mesh.TriangleIndices.Add(bottomLeft);
                    mesh.TriangleIndices.Add(topRight);

                    mesh.TriangleIndices.Add(topRight);
                    mesh.TriangleIndices.Add(bottomLeft);
                    mesh.TriangleIndices.Add(bottomRight);
                }
            }

            if (smoothNormals)
                CalculateSmoothNormals(mesh);
            else
                CalculateFlatNormals(mesh);

            return mesh;
        }

        private MeshGeometry3D GenerateEdgeBasedMesh(
            BitmapSource image,
            DetailLevel detail,
            double depth,
            bool smoothNormals)
        {
            int step = GetStepSize(detail);
            int width = image.PixelWidth;
            int height = image.PixelHeight;

            FormatConvertedBitmap converted = new FormatConvertedBitmap(
                image, PixelFormats.Bgra32, null, 0);

            byte[] pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);

            double[,] edges = ApplySobelOperator(pixels, width, height);

            MeshGeometry3D mesh = new MeshGeometry3D();

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    double edgeStrength = edges[x, y];
                    double z = edgeStrength * depth;

                    double nx = (x - width / 2.0) / (width / 2.0) * 5;
                    double ny = -(y - height / 2.0) / (height / 2.0) * 5;

                    mesh.Positions.Add(new Point3D(nx, ny, z));
                    mesh.TextureCoordinates.Add(new Point((double)x / width, (double)y / height));
                }
            }

            int cols = (width / step);
            int rows = (height / step);

            for (int y = 0; y < rows - 1; y++)
            {
                for (int x = 0; x < cols - 1; x++)
                {
                    int topLeft = y * cols + x;
                    int topRight = topLeft + 1;
                    int bottomLeft = (y + 1) * cols + x;
                    int bottomRight = bottomLeft + 1;

                    mesh.TriangleIndices.Add(topLeft);
                    mesh.TriangleIndices.Add(bottomLeft);
                    mesh.TriangleIndices.Add(topRight);

                    mesh.TriangleIndices.Add(topRight);
                    mesh.TriangleIndices.Add(bottomLeft);
                    mesh.TriangleIndices.Add(bottomRight);
                }
            }

            if (smoothNormals)
                CalculateSmoothNormals(mesh);
            else
                CalculateFlatNormals(mesh);

            return mesh;
        }

        private MeshGeometry3D GenerateContourMesh(
            BitmapSource image,
            DetailLevel detail,
            double depth,
            bool smoothNormals)
        {
            int step = GetStepSize(detail);
            int width = image.PixelWidth;
            int height = image.PixelHeight;

            FormatConvertedBitmap converted = new FormatConvertedBitmap(
                image, PixelFormats.Bgra32, null, 0);

            byte[] pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);

            MeshGeometry3D mesh = new MeshGeometry3D();

            int levels = 5;
            for (int level = 0; level <= levels; level++)
            {
                double threshold = level / (double)levels;
                GenerateContourLevel(mesh, pixels, width, height, step, threshold, depth, level);
            }

            if (smoothNormals)
                CalculateSmoothNormals(mesh);
            else
                CalculateFlatNormals(mesh);

            return mesh;
        }

        private void GenerateContourLevel(
            MeshGeometry3D mesh,
            byte[] pixels,
            int width,
            int height,
            int step,
            double threshold,
            double maxDepth,
            int level)
        {
            double z = threshold * maxDepth;

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    int idx = (y * width + x) * 4;
                    double brightness = (pixels[idx + 2] * 0.299 +
                                       pixels[idx + 1] * 0.587 +
                                       pixels[idx] * 0.114) / 255.0;

                    if (brightness >= threshold)
                    {
                        double nx = (x - width / 2.0) / (width / 2.0) * 5;
                        double ny = -(y - height / 2.0) / (height / 2.0) * 5;

                        mesh.Positions.Add(new Point3D(nx, ny, z));
                        mesh.TextureCoordinates.Add(new Point((double)x / width, (double)y / height));
                    }
                }
            }
        }

        private double[,] ApplySobelOperator(byte[] pixels, int width, int height)
        {
            double[,] edges = new double[width, height];

            int[,] sobelX = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
            int[,] sobelY = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    double gx = 0, gy = 0;

                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int idx = ((y + ky) * width + (x + kx)) * 4;
                            double intensity = (pixels[idx + 2] * 0.299 +
                                              pixels[idx + 1] * 0.587 +
                                              pixels[idx] * 0.114) / 255.0;

                            gx += intensity * sobelX[ky + 1, kx + 1];
                            gy += intensity * sobelY[ky + 1, kx + 1];
                        }
                    }

                    edges[x, y] = Math.Sqrt(gx * gx + gy * gy);
                }
            }

            return edges;
        }

        private void CalculateSmoothNormals(MeshGeometry3D mesh)
        {
            System.Windows.Media.Media3D.Vector3D[] normals = new System.Windows.Media.Media3D.Vector3D[mesh.Positions.Count];

            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
            {
                int i0 = mesh.TriangleIndices[i];
                int i1 = mesh.TriangleIndices[i + 1];
                int i2 = mesh.TriangleIndices[i + 2];

                Point3D p0 = mesh.Positions[i0];
                Point3D p1 = mesh.Positions[i1];
                Point3D p2 = mesh.Positions[i2];

                System.Windows.Media.Media3D.Vector3D v1 = p1 - p0;
                System.Windows.Media.Media3D.Vector3D v2 = p2 - p0;
                System.Windows.Media.Media3D.Vector3D normal = System.Windows.Media.Media3D.Vector3D.CrossProduct(v1, v2);

                normals[i0] += normal;
                normals[i1] += normal;
                normals[i2] += normal;
            }

            mesh.Normals.Clear();
            foreach (var normal in normals)
            {
                System.Windows.Media.Media3D.Vector3D normalized = normal;
                normalized.Normalize();
                mesh.Normals.Add(normalized);
            }
        }

        private void CalculateFlatNormals(MeshGeometry3D mesh)
        {
            mesh.Normals.Clear();

            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
            {
                int i0 = mesh.TriangleIndices[i];
                int i1 = mesh.TriangleIndices[i + 1];
                int i2 = mesh.TriangleIndices[i + 2];

                Point3D p0 = mesh.Positions[i0];
                Point3D p1 = mesh.Positions[i1];
                Point3D p2 = mesh.Positions[i2];

                System.Windows.Media.Media3D.Vector3D v1 = p1 - p0;
                System.Windows.Media.Media3D.Vector3D v2 = p2 - p0;
                System.Windows.Media.Media3D.Vector3D normal = System.Windows.Media.Media3D.Vector3D.CrossProduct(v1, v2);
                normal.Normalize();

                if (mesh.Normals.Count <= i0) mesh.Normals.Add(normal);
                if (mesh.Normals.Count <= i1) mesh.Normals.Add(normal);
                if (mesh.Normals.Count <= i2) mesh.Normals.Add(normal);
            }
        }

        private int GetStepSize(DetailLevel detail)
        {
            return detail switch
            {
                DetailLevel.Low => 8,
                DetailLevel.Medium => 4,
                DetailLevel.High => 2,
                _ => 4
            };
        }

        // EXPORT METHOD USING ASSIMP
        public void ExportModel(MeshGeometry3D mesh, string filePath, BitmapSource textureImage = null)
        {
            string extension = System.IO.Path.GetExtension(filePath).ToLower();
            string formatId = GetFormatId(extension);

            // Create scene
            Scene scene = new Scene();
            scene.RootNode = new Node("root");

            // Create mesh
            Assimp.Mesh assimpMesh = new Assimp.Mesh("ImageMesh", PrimitiveType.Triangle);

            // Add vertices
            foreach (var vertex in mesh.Positions)
            {
                assimpMesh.Vertices.Add(new Assimp.Vector3D((float)vertex.X, (float)vertex.Y, (float)vertex.Z));
            }

            // Add normals
            foreach (var normal in mesh.Normals)
            {
                assimpMesh.Normals.Add(new Assimp.Vector3D((float)normal.X, (float)normal.Y, (float)normal.Z));
            }

            // Add texture coordinates
            if (mesh.TextureCoordinates.Count > 0)
            {
                List<Assimp.Vector3D> texCoords = new List<Assimp.Vector3D>();
                foreach (var uv in mesh.TextureCoordinates)
                {
                    texCoords.Add(new Assimp.Vector3D((float)uv.X, (float)(1.0 - uv.Y), 0f));
                }
                assimpMesh.TextureCoordinateChannels[0] = texCoords;
                assimpMesh.UVComponentCount[0] = 2;
            }

            // Add faces
            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
            {
                Face face = new Face();
                face.Indices.Add(mesh.TriangleIndices[i]);
                face.Indices.Add(mesh.TriangleIndices[i + 1]);
                face.Indices.Add(mesh.TriangleIndices[i + 2]);
                assimpMesh.Faces.Add(face);
            }

            // Create material
            Assimp.Material material = new Assimp.Material();
            material.Name = "ImageMaterial";
            material.ColorDiffuse = new Color4D(1.0f, 1.0f, 1.0f, 1.0f);
            material.ColorAmbient = new Color4D(0.2f, 0.2f, 0.2f, 1.0f);
            material.ColorSpecular = new Color4D(0.5f, 0.5f, 0.5f, 1.0f);
            material.Shininess = 96.0f;
            material.Opacity = 1.0f;

            // Add texture
            if (textureImage != null)
            {
                string textureName = System.IO.Path.GetFileNameWithoutExtension(filePath) + "_texture.png";
                string texturePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath), textureName);

                SaveTexture(textureImage, texturePath);

                TextureSlot textureSlot = new TextureSlot();
                textureSlot.FilePath = textureName;
                textureSlot.TextureType = TextureType.Diffuse;
                textureSlot.TextureIndex = 0;
                textureSlot.UVIndex = 0;
                textureSlot.BlendFactor = 1.0f;
                textureSlot.Operation = TextureOperation.Multiply;

                material.AddMaterialTexture(ref textureSlot);
            }

            scene.Materials.Add(material);
            assimpMesh.MaterialIndex = 0;
            scene.Meshes.Add(assimpMesh);
            scene.RootNode.MeshIndices.Add(0);

            // Export
            using (AssimpContext context = new AssimpContext())
            {
                context.ExportFile(scene, filePath, formatId, PostProcessSteps.None);
            }
        }

        private string GetFormatId(string extension)
        {
            return extension.TrimStart('.').ToLower() switch
            {
                "obj" => "obj",
                "stl" => "stl",
                "ply" => "ply",
                "fbx" => "fbx",
                "dae" => "collada",
                "gltf" => "gltf2",
                "glb" => "glb2",
                _ => "obj"
            };
        }

        private void SaveTexture(BitmapSource image, string filePath)
        {
            BitmapSource source = image;

            if (image.Format != PixelFormats.Bgra32)
            {
                source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            }

            using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(fileStream);
            }
        }
    }
}