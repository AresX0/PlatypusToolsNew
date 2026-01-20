using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for converting SVG files to 3D printable formats (STL, OBJ)
    /// </summary>
    public static class SvgTo3DService
    {
        /// <summary>
        /// Converts an SVG file to STL format by extruding the paths
        /// </summary>
        /// <param name="svgPath">Path to the SVG file</param>
        /// <param name="outputPath">Path for the output STL file</param>
        /// <param name="extrusionDepth">Depth of extrusion in mm (default 5mm)</param>
        /// <param name="scale">Scale factor (default 1.0)</param>
        /// <returns>True if conversion was successful</returns>
        public static bool ConvertSvgToStl(string svgPath, string outputPath, float extrusionDepth = 5f, float scale = 1f)
        {
            return ConvertSvgToStl(svgPath, outputPath, extrusionDepth, scale, out _);
        }

        /// <summary>
        /// Converts an SVG file to STL format with diagnostic info
        /// </summary>
        public static bool ConvertSvgToStl(string svgPath, string outputPath, float extrusionDepth, float scale, out string diagnosticInfo)
        {
            diagnosticInfo = "";
            if (!File.Exists(svgPath))
            {
                diagnosticInfo = "SVG file not found";
                return false;
            }

            try
            {
                var svgContent = File.ReadAllText(svgPath);
                var svgInfo = AnalyzeSvgContent(svgContent);
                diagnosticInfo = svgInfo;

                var paths = ParseSvgPaths(svgContent);
                
                if (!paths.Any())
                {
                    // Try to parse rectangles and other shapes
                    paths = ParseSvgShapes(svgContent);
                }

                // Try parsing lines as well
                if (!paths.Any())
                {
                    paths = ParseSvgLines(svgContent);
                }

                // If still no paths, try to extract and trace embedded images
                if (!paths.Any())
                {
                    paths = TraceEmbeddedImages(svgContent);
                    if (paths.Any())
                    {
                        diagnosticInfo = $"Traced {paths.Count} contours from embedded image. {svgInfo}";
                    }
                }

                if (!paths.Any())
                {
                    diagnosticInfo = $"No vector paths found and image tracing failed. {svgInfo}";
                    return false;
                }

                if (!diagnosticInfo.Contains("Traced"))
                {
                    diagnosticInfo = $"Found {paths.Count} paths. {svgInfo}";
                }
                var stlContent = GenerateStlFromPaths(paths, extrusionDepth, scale);
                File.WriteAllText(outputPath, stlContent, Encoding.ASCII);
                return true;
            }
            catch (Exception ex)
            {
                diagnosticInfo = $"Parse error: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Analyzes SVG content and returns diagnostic info
        /// </summary>
        private static string AnalyzeSvgContent(string svgContent)
        {
            try
            {
                var doc = XDocument.Parse(svgContent);
                var info = new List<string>();
                
                int pathCount = doc.Descendants().Count(e => e.Name.LocalName == "path");
                int rectCount = doc.Descendants().Count(e => e.Name.LocalName == "rect");
                int circleCount = doc.Descendants().Count(e => e.Name.LocalName == "circle");
                int ellipseCount = doc.Descendants().Count(e => e.Name.LocalName == "ellipse");
                int polygonCount = doc.Descendants().Count(e => e.Name.LocalName == "polygon");
                int polylineCount = doc.Descendants().Count(e => e.Name.LocalName == "polyline");
                int lineCount = doc.Descendants().Count(e => e.Name.LocalName == "line");
                int imageCount = doc.Descendants().Count(e => e.Name.LocalName == "image");
                int useCount = doc.Descendants().Count(e => e.Name.LocalName == "use");
                int groupCount = doc.Descendants().Count(e => e.Name.LocalName == "g");
                int textCount = doc.Descendants().Count(e => e.Name.LocalName == "text");
                
                if (pathCount > 0) info.Add($"paths:{pathCount}");
                if (rectCount > 0) info.Add($"rects:{rectCount}");
                if (circleCount > 0) info.Add($"circles:{circleCount}");
                if (ellipseCount > 0) info.Add($"ellipses:{ellipseCount}");
                if (polygonCount > 0) info.Add($"polygons:{polygonCount}");
                if (polylineCount > 0) info.Add($"polylines:{polylineCount}");
                if (lineCount > 0) info.Add($"lines:{lineCount}");
                if (imageCount > 0) info.Add($"images:{imageCount} (embedded rasters)");
                if (useCount > 0) info.Add($"use:{useCount}");
                if (groupCount > 0) info.Add($"groups:{groupCount}");
                if (textCount > 0) info.Add($"text:{textCount}");

                if (imageCount > 0 && pathCount == 0 && rectCount == 0)
                {
                    return $"SVG contains embedded raster image(s). Elements: [{string.Join(", ", info)}]";
                }

                return info.Any() ? $"[{string.Join(", ", info)}]" : "No supported elements found";
            }
            catch
            {
                return "Could not parse SVG structure";
            }
        }

        /// <summary>
        /// Parse SVG line elements
        /// </summary>
        private static List<List<Point2D>> ParseSvgLines(string svgContent)
        {
            var lines = new List<List<Point2D>>();
            
            try
            {
                var doc = XDocument.Parse(svgContent);
                
                foreach (var line in doc.Descendants().Where(e => e.Name.LocalName == "line"))
                {
                    float x1 = ParseFloat(line.Attribute("x1")?.Value);
                    float y1 = ParseFloat(line.Attribute("y1")?.Value);
                    float x2 = ParseFloat(line.Attribute("x2")?.Value);
                    float y2 = ParseFloat(line.Attribute("y2")?.Value);
                    
                    // Create a thin rectangle from the line
                    float dx = x2 - x1;
                    float dy = y2 - y1;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len > 0)
                    {
                        float thickness = 1f; // 1 unit thick line
                        float nx = -dy / len * thickness / 2;
                        float ny = dx / len * thickness / 2;
                        
                        lines.Add(new List<Point2D>
                        {
                            new Point2D(x1 + nx, y1 + ny),
                            new Point2D(x2 + nx, y2 + ny),
                            new Point2D(x2 - nx, y2 - ny),
                            new Point2D(x1 - nx, y1 - ny)
                        });
                    }
                }

                // Also parse polylines
                foreach (var polyline in doc.Descendants().Where(e => e.Name.LocalName == "polyline"))
                {
                    var pointsAttr = polyline.Attribute("points")?.Value;
                    if (!string.IsNullOrEmpty(pointsAttr))
                    {
                        var points = ParsePolygonPoints(pointsAttr);
                        if (points.Count > 1)
                        {
                            // Convert polyline to a closed polygon with thickness
                            lines.Add(points);
                        }
                    }
                }
            }
            catch { }
            
            return lines;
        }

        /// <summary>
        /// Extract and trace embedded raster images from SVG
        /// </summary>
        private static List<List<Point2D>> TraceEmbeddedImages(string svgContent)
        {
            var allContours = new List<List<Point2D>>();
            
            try
            {
                var doc = XDocument.Parse(svgContent);
                
                foreach (var imageElement in doc.Descendants().Where(e => e.Name.LocalName == "image"))
                {
                    // Try to get the image data from href or xlink:href
                    var href = imageElement.Attribute("href")?.Value 
                        ?? imageElement.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"))?.Value;
                    
                    if (string.IsNullOrEmpty(href)) continue;
                    
                    byte[]? imageData = null;
                    
                    // Check if it's a base64 embedded image
                    if (href.StartsWith("data:image"))
                    {
                        var base64Start = href.IndexOf("base64,");
                        if (base64Start > 0)
                        {
                            var base64Data = href.Substring(base64Start + 7);
                            try
                            {
                                imageData = Convert.FromBase64String(base64Data);
                            }
                            catch { }
                        }
                    }
                    
                    if (imageData != null)
                    {
                        var contours = TraceImageToContours(imageData);
                        allContours.AddRange(contours);
                    }
                }
            }
            catch { }
            
            return allContours;
        }

        /// <summary>
        /// Trace an image to vector contours using edge detection
        /// </summary>
        private static List<List<Point2D>> TraceImageToContours(byte[] imageData)
        {
            var contours = new List<List<Point2D>>();
            
            try
            {
                using var image = Image.Load<Rgba32>(imageData);
                
                // Resize if too large (for performance)
                int maxSize = 500;
                if (image.Width > maxSize || image.Height > maxSize)
                {
                    float ratio = Math.Min((float)maxSize / image.Width, (float)maxSize / image.Height);
                    int newWidth = (int)(image.Width * ratio);
                    int newHeight = (int)(image.Height * ratio);
                    image.Mutate(x => x.Resize(newWidth, newHeight));
                }

                // Convert to grayscale and detect edges
                var edges = DetectEdges(image);
                
                // Trace contours from edges
                contours = TraceContoursFromEdges(edges, image.Width, image.Height);
            }
            catch { }
            
            return contours;
        }

        /// <summary>
        /// Simple edge detection using gradient magnitude
        /// </summary>
        private static bool[,] DetectEdges(Image<Rgba32> image)
        {
            int width = image.Width;
            int height = image.Height;
            var edges = new bool[width, height];
            var grayscale = new float[width, height];
            
            // Convert to grayscale
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = image[x, y];
                    grayscale[x, y] = (pixel.R * 0.299f + pixel.G * 0.587f + pixel.B * 0.114f) / 255f;
                }
            }
            
            // Sobel edge detection
            float threshold = 0.15f;
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    // Sobel X
                    float gx = -grayscale[x - 1, y - 1] - 2 * grayscale[x - 1, y] - grayscale[x - 1, y + 1]
                             + grayscale[x + 1, y - 1] + 2 * grayscale[x + 1, y] + grayscale[x + 1, y + 1];
                    
                    // Sobel Y
                    float gy = -grayscale[x - 1, y - 1] - 2 * grayscale[x, y - 1] - grayscale[x + 1, y - 1]
                             + grayscale[x - 1, y + 1] + 2 * grayscale[x, y + 1] + grayscale[x + 1, y + 1];
                    
                    float magnitude = (float)Math.Sqrt(gx * gx + gy * gy);
                    edges[x, y] = magnitude > threshold;
                }
            }
            
            return edges;
        }

        /// <summary>
        /// Trace contours from edge detection result using a marching squares-like approach
        /// </summary>
        private static List<List<Point2D>> TraceContoursFromEdges(bool[,] edges, int width, int height)
        {
            var contours = new List<List<Point2D>>();
            var visited = new bool[width, height];
            
            // Sample at intervals to reduce point count
            int step = Math.Max(2, Math.Min(width, height) / 100);
            
            for (int startY = 1; startY < height - 1; startY += step)
            {
                for (int startX = 1; startX < width - 1; startX += step)
                {
                    if (edges[startX, startY] && !visited[startX, startY])
                    {
                        var contour = TraceContour(edges, visited, startX, startY, width, height);
                        if (contour.Count >= 3)
                        {
                            // Simplify the contour
                            var simplified = SimplifyContour(contour, 2.0f);
                            if (simplified.Count >= 3)
                            {
                                contours.Add(simplified);
                            }
                        }
                    }
                }
            }
            
            // Also find large filled regions
            var filledRegions = FindFilledRegions(edges, width, height);
            contours.AddRange(filledRegions);
            
            return contours;
        }

        /// <summary>
        /// Trace a single contour starting from a point
        /// </summary>
        private static List<Point2D> TraceContour(bool[,] edges, bool[,] visited, int startX, int startY, int width, int height)
        {
            var contour = new List<Point2D>();
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));
            
            // 8-directional neighbors
            int[] dx = { -1, 0, 1, 1, 1, 0, -1, -1 };
            int[] dy = { -1, -1, -1, 0, 1, 1, 1, 0 };
            
            int maxPoints = 5000;
            
            while (queue.Count > 0 && contour.Count < maxPoints)
            {
                var (x, y) = queue.Dequeue();
                
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                if (visited[x, y]) continue;
                if (!edges[x, y]) continue;
                
                visited[x, y] = true;
                contour.Add(new Point2D(x, y));
                
                // Add unvisited edge neighbors
                for (int i = 0; i < 8; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited[nx, ny] && edges[nx, ny])
                    {
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            
            return contour;
        }

        /// <summary>
        /// Simplify contour using Ramer-Douglas-Peucker algorithm
        /// </summary>
        private static List<Point2D> SimplifyContour(List<Point2D> points, float epsilon)
        {
            if (points.Count < 3) return points;
            
            // Sort points to form a proper contour (by angle from centroid)
            float cx = points.Average(p => p.X);
            float cy = points.Average(p => p.Y);
            
            var sorted = points.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToList();
            
            // Sample points at regular intervals
            int targetCount = Math.Min(100, sorted.Count);
            var simplified = new List<Point2D>();
            
            for (int i = 0; i < targetCount; i++)
            {
                int index = i * sorted.Count / targetCount;
                simplified.Add(sorted[index]);
            }
            
            return simplified;
        }

        /// <summary>
        /// Find large filled regions (areas with consistent color)
        /// </summary>
        private static List<List<Point2D>> FindFilledRegions(bool[,] edges, int width, int height)
        {
            var regions = new List<List<Point2D>>();
            
            // Create a grid to detect rectangular regions
            int gridSize = 20;
            int gridW = width / gridSize;
            int gridH = height / gridSize;
            
            for (int gy = 0; gy < gridH - 1; gy++)
            {
                for (int gx = 0; gx < gridW - 1; gx++)
                {
                    // Count edges in this grid cell
                    int edgeCount = 0;
                    for (int y = gy * gridSize; y < (gy + 1) * gridSize && y < height; y++)
                    {
                        for (int x = gx * gridSize; x < (gx + 1) * gridSize && x < width; x++)
                        {
                            if (edges[x, y]) edgeCount++;
                        }
                    }
                    
                    // If significant edges, create a rectangle
                    int cellArea = gridSize * gridSize;
                    if (edgeCount > cellArea * 0.1f && edgeCount < cellArea * 0.8f)
                    {
                        float x1 = gx * gridSize;
                        float y1 = gy * gridSize;
                        float x2 = (gx + 1) * gridSize;
                        float y2 = (gy + 1) * gridSize;
                        
                        regions.Add(new List<Point2D>
                        {
                            new Point2D(x1, y1),
                            new Point2D(x2, y1),
                            new Point2D(x2, y2),
                            new Point2D(x1, y2)
                        });
                    }
                }
            }
            
            return regions;
        }

        /// <summary>
        /// Converts an SVG file to OBJ format
        /// </summary>
        public static bool ConvertSvgToObj(string svgPath, string outputPath, float extrusionDepth = 5f, float scale = 1f)
        {
            if (!File.Exists(svgPath)) return false;

            try
            {
                var svgContent = File.ReadAllText(svgPath);
                var paths = ParseSvgPaths(svgContent);
                
                if (!paths.Any())
                {
                    paths = ParseSvgShapes(svgContent);
                }

                if (!paths.Any()) return false;

                var objContent = GenerateObjFromPaths(paths, extrusionDepth, scale);
                File.WriteAllText(outputPath, objContent, Encoding.ASCII);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a simple 3D text extrusion as STL
        /// </summary>
        public static bool CreateTextStl(string text, string outputPath, float height = 10f, float depth = 3f)
        {
            try
            {
                var shapes = new List<List<Point2D>>();
                float xOffset = 0;
                
                foreach (char c in text.ToUpper())
                {
                    var charShape = GetCharacterShape(c, xOffset, height);
                    if (charShape != null && charShape.Count > 0)
                    {
                        shapes.Add(charShape);
                    }
                    xOffset += height * 0.7f; // Character spacing
                }

                if (!shapes.Any()) return false;

                var stlContent = GenerateStlFromPaths(shapes, depth, 1f);
                File.WriteAllText(outputPath, stlContent, Encoding.ASCII);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a basic 3D shape as STL
        /// </summary>
        public static bool CreateBasicShape(BasicShape shape, string outputPath, float size = 20f, float height = 10f)
        {
            try
            {
                string stlContent = shape switch
                {
                    BasicShape.Cube => GenerateCubeStl(size),
                    BasicShape.Cylinder => GenerateCylinderStl(size / 2, height, 32),
                    BasicShape.Sphere => GenerateSphereStl(size / 2, 16, 16),
                    BasicShape.Pyramid => GeneratePyramidStl(size, height),
                    BasicShape.Cone => GenerateConeStl(size / 2, height, 32),
                    _ => GenerateCubeStl(size)
                };

                File.WriteAllText(outputPath, stlContent, Encoding.ASCII);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #region SVG Parsing

        private static List<List<Point2D>> ParseSvgPaths(string svgContent)
        {
            var paths = new List<List<Point2D>>();
            
            try
            {
                var doc = XDocument.Parse(svgContent);
                XNamespace ns = "http://www.w3.org/2000/svg";
                
                foreach (var pathElement in doc.Descendants().Where(e => e.Name.LocalName == "path"))
                {
                    var d = pathElement.Attribute("d")?.Value;
                    if (!string.IsNullOrEmpty(d))
                    {
                        var points = ParsePathData(d);
                        if (points.Count > 2)
                        {
                            paths.Add(points);
                        }
                    }
                }
            }
            catch { }

            return paths;
        }

        private static List<List<Point2D>> ParseSvgShapes(string svgContent)
        {
            var shapes = new List<List<Point2D>>();
            
            try
            {
                var doc = XDocument.Parse(svgContent);
                
                // Parse rectangles
                foreach (var rect in doc.Descendants().Where(e => e.Name.LocalName == "rect"))
                {
                    float x = ParseFloat(rect.Attribute("x")?.Value);
                    float y = ParseFloat(rect.Attribute("y")?.Value);
                    float w = ParseFloat(rect.Attribute("width")?.Value);
                    float h = ParseFloat(rect.Attribute("height")?.Value);
                    
                    if (w > 0 && h > 0)
                    {
                        shapes.Add(new List<Point2D>
                        {
                            new Point2D(x, y),
                            new Point2D(x + w, y),
                            new Point2D(x + w, y + h),
                            new Point2D(x, y + h)
                        });
                    }
                }

                // Parse circles
                foreach (var circle in doc.Descendants().Where(e => e.Name.LocalName == "circle"))
                {
                    float cx = ParseFloat(circle.Attribute("cx")?.Value);
                    float cy = ParseFloat(circle.Attribute("cy")?.Value);
                    float r = ParseFloat(circle.Attribute("r")?.Value);
                    
                    if (r > 0)
                    {
                        var points = new List<Point2D>();
                        for (int i = 0; i < 32; i++)
                        {
                            double angle = 2 * Math.PI * i / 32;
                            points.Add(new Point2D(
                                cx + (float)(r * Math.Cos(angle)),
                                cy + (float)(r * Math.Sin(angle))
                            ));
                        }
                        shapes.Add(points);
                    }
                }

                // Parse ellipses
                foreach (var ellipse in doc.Descendants().Where(e => e.Name.LocalName == "ellipse"))
                {
                    float cx = ParseFloat(ellipse.Attribute("cx")?.Value);
                    float cy = ParseFloat(ellipse.Attribute("cy")?.Value);
                    float rx = ParseFloat(ellipse.Attribute("rx")?.Value);
                    float ry = ParseFloat(ellipse.Attribute("ry")?.Value);
                    
                    if (rx > 0 && ry > 0)
                    {
                        var points = new List<Point2D>();
                        for (int i = 0; i < 32; i++)
                        {
                            double angle = 2 * Math.PI * i / 32;
                            points.Add(new Point2D(
                                cx + (float)(rx * Math.Cos(angle)),
                                cy + (float)(ry * Math.Sin(angle))
                            ));
                        }
                        shapes.Add(points);
                    }
                }

                // Parse polygons
                foreach (var polygon in doc.Descendants().Where(e => e.Name.LocalName == "polygon"))
                {
                    var pointsAttr = polygon.Attribute("points")?.Value;
                    if (!string.IsNullOrEmpty(pointsAttr))
                    {
                        var points = ParsePolygonPoints(pointsAttr);
                        if (points.Count > 2)
                        {
                            shapes.Add(points);
                        }
                    }
                }
            }
            catch { }

            return shapes;
        }

        private static List<Point2D> ParsePathData(string d)
        {
            var points = new List<Point2D>();
            float currentX = 0, currentY = 0;
            
            var regex = new Regex(@"([MmLlHhVvCcSsQqTtAaZz])\s*([^MmLlHhVvCcSsQqTtAaZz]*)");
            var matches = regex.Matches(d);

            foreach (Match match in matches)
            {
                char cmd = match.Groups[1].Value[0];
                string args = match.Groups[2].Value.Trim();
                var numbers = ParseNumbers(args);

                switch (cmd)
                {
                    case 'M':
                        if (numbers.Count >= 2)
                        {
                            currentX = numbers[0];
                            currentY = numbers[1];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'm':
                        if (numbers.Count >= 2)
                        {
                            currentX += numbers[0];
                            currentY += numbers[1];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'L':
                        for (int i = 0; i + 1 < numbers.Count; i += 2)
                        {
                            currentX = numbers[i];
                            currentY = numbers[i + 1];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'l':
                        for (int i = 0; i + 1 < numbers.Count; i += 2)
                        {
                            currentX += numbers[i];
                            currentY += numbers[i + 1];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'H':
                        if (numbers.Count >= 1)
                        {
                            currentX = numbers[0];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'h':
                        if (numbers.Count >= 1)
                        {
                            currentX += numbers[0];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'V':
                        if (numbers.Count >= 1)
                        {
                            currentY = numbers[0];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'v':
                        if (numbers.Count >= 1)
                        {
                            currentY += numbers[0];
                            points.Add(new Point2D(currentX, currentY));
                        }
                        break;
                    case 'Z':
                    case 'z':
                        if (points.Count > 0)
                        {
                            points.Add(points[0]); // Close path
                        }
                        break;
                }
            }

            return points;
        }

        private static List<float> ParseNumbers(string input)
        {
            var numbers = new List<float>();
            var regex = new Regex(@"-?\d+\.?\d*");
            foreach (Match match in regex.Matches(input))
            {
                if (float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float num))
                {
                    numbers.Add(num);
                }
            }
            return numbers;
        }

        private static List<Point2D> ParsePolygonPoints(string pointsAttr)
        {
            var points = new List<Point2D>();
            var numbers = ParseNumbers(pointsAttr);
            
            for (int i = 0; i + 1 < numbers.Count; i += 2)
            {
                points.Add(new Point2D(numbers[i], numbers[i + 1]));
            }
            
            return points;
        }

        private static float ParseFloat(string? value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            // Remove units like px, mm, etc.
            var numStr = Regex.Replace(value, @"[a-zA-Z%]+", "");
            return float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0;
        }

        #endregion

        #region STL Generation

        private static string GenerateStlFromPaths(List<List<Point2D>> paths, float depth, float scale)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid svgextrusion");

            foreach (var path in paths)
            {
                if (path.Count < 3) continue;

                // Scale and center the path
                var scaledPath = path.Select(p => new Point2D(p.X * scale, p.Y * scale)).ToList();
                
                // Generate triangles for the front face (using fan triangulation)
                var centroid = new Point2D(
                    scaledPath.Average(p => p.X),
                    scaledPath.Average(p => p.Y)
                );

                // Front face (z = 0)
                for (int i = 0; i < scaledPath.Count - 1; i++)
                {
                    WriteTriangle(sb,
                        centroid.X, centroid.Y, 0,
                        scaledPath[i].X, scaledPath[i].Y, 0,
                        scaledPath[i + 1].X, scaledPath[i + 1].Y, 0,
                        0, 0, -1);
                }

                // Back face (z = depth)
                for (int i = 0; i < scaledPath.Count - 1; i++)
                {
                    WriteTriangle(sb,
                        centroid.X, centroid.Y, depth,
                        scaledPath[i + 1].X, scaledPath[i + 1].Y, depth,
                        scaledPath[i].X, scaledPath[i].Y, depth,
                        0, 0, 1);
                }

                // Side walls
                for (int i = 0; i < scaledPath.Count - 1; i++)
                {
                    var p1 = scaledPath[i];
                    var p2 = scaledPath[i + 1];

                    // Calculate normal for side face
                    float dx = p2.X - p1.X;
                    float dy = p2.Y - p1.Y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    float nx = dy / len;
                    float ny = -dx / len;

                    // Two triangles for the quad
                    WriteTriangle(sb,
                        p1.X, p1.Y, 0,
                        p2.X, p2.Y, 0,
                        p2.X, p2.Y, depth,
                        nx, ny, 0);

                    WriteTriangle(sb,
                        p1.X, p1.Y, 0,
                        p2.X, p2.Y, depth,
                        p1.X, p1.Y, depth,
                        nx, ny, 0);
                }
            }

            sb.AppendLine("endsolid svgextrusion");
            return sb.ToString();
        }

        private static void WriteTriangle(StringBuilder sb, 
            float x1, float y1, float z1,
            float x2, float y2, float z2,
            float x3, float y3, float z3,
            float nx, float ny, float nz)
        {
            sb.AppendLine(FormattableString.Invariant($"  facet normal {nx:F6} {ny:F6} {nz:F6}"));
            sb.AppendLine("    outer loop");
            sb.AppendLine(FormattableString.Invariant($"      vertex {x1:F6} {y1:F6} {z1:F6}"));
            sb.AppendLine(FormattableString.Invariant($"      vertex {x2:F6} {y2:F6} {z2:F6}"));
            sb.AppendLine(FormattableString.Invariant($"      vertex {x3:F6} {y3:F6} {z3:F6}"));
            sb.AppendLine("    endloop");
            sb.AppendLine("  endfacet");
        }

        private static string GenerateCubeStl(float size)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid cube");
            float s = size / 2;

            // Front face
            WriteTriangle(sb, -s, -s, s, s, -s, s, s, s, s, 0, 0, 1);
            WriteTriangle(sb, -s, -s, s, s, s, s, -s, s, s, 0, 0, 1);
            // Back face
            WriteTriangle(sb, s, -s, -s, -s, -s, -s, -s, s, -s, 0, 0, -1);
            WriteTriangle(sb, s, -s, -s, -s, s, -s, s, s, -s, 0, 0, -1);
            // Top face
            WriteTriangle(sb, -s, s, -s, -s, s, s, s, s, s, 0, 1, 0);
            WriteTriangle(sb, -s, s, -s, s, s, s, s, s, -s, 0, 1, 0);
            // Bottom face
            WriteTriangle(sb, -s, -s, s, -s, -s, -s, s, -s, -s, 0, -1, 0);
            WriteTriangle(sb, -s, -s, s, s, -s, -s, s, -s, s, 0, -1, 0);
            // Right face
            WriteTriangle(sb, s, -s, s, s, -s, -s, s, s, -s, 1, 0, 0);
            WriteTriangle(sb, s, -s, s, s, s, -s, s, s, s, 1, 0, 0);
            // Left face
            WriteTriangle(sb, -s, -s, -s, -s, -s, s, -s, s, s, -1, 0, 0);
            WriteTriangle(sb, -s, -s, -s, -s, s, s, -s, s, -s, -1, 0, 0);

            sb.AppendLine("endsolid cube");
            return sb.ToString();
        }

        private static string GenerateCylinderStl(float radius, float height, int segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid cylinder");

            for (int i = 0; i < segments; i++)
            {
                double a1 = 2 * Math.PI * i / segments;
                double a2 = 2 * Math.PI * (i + 1) / segments;
                
                float x1 = (float)(radius * Math.Cos(a1));
                float y1 = (float)(radius * Math.Sin(a1));
                float x2 = (float)(radius * Math.Cos(a2));
                float y2 = (float)(radius * Math.Sin(a2));

                // Top cap
                WriteTriangle(sb, 0, 0, height, x1, y1, height, x2, y2, height, 0, 0, 1);
                // Bottom cap
                WriteTriangle(sb, 0, 0, 0, x2, y2, 0, x1, y1, 0, 0, 0, -1);
                // Side
                float nx = (x1 + x2) / 2 / radius;
                float ny = (y1 + y2) / 2 / radius;
                WriteTriangle(sb, x1, y1, 0, x2, y2, 0, x2, y2, height, nx, ny, 0);
                WriteTriangle(sb, x1, y1, 0, x2, y2, height, x1, y1, height, nx, ny, 0);
            }

            sb.AppendLine("endsolid cylinder");
            return sb.ToString();
        }

        private static string GenerateSphereStl(float radius, int latSegments, int lonSegments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid sphere");

            for (int lat = 0; lat < latSegments; lat++)
            {
                double lat1 = Math.PI * lat / latSegments - Math.PI / 2;
                double lat2 = Math.PI * (lat + 1) / latSegments - Math.PI / 2;

                for (int lon = 0; lon < lonSegments; lon++)
                {
                    double lon1 = 2 * Math.PI * lon / lonSegments;
                    double lon2 = 2 * Math.PI * (lon + 1) / lonSegments;

                    float x1 = (float)(radius * Math.Cos(lat1) * Math.Cos(lon1));
                    float y1 = (float)(radius * Math.Cos(lat1) * Math.Sin(lon1));
                    float z1 = (float)(radius * Math.Sin(lat1));

                    float x2 = (float)(radius * Math.Cos(lat1) * Math.Cos(lon2));
                    float y2 = (float)(radius * Math.Cos(lat1) * Math.Sin(lon2));
                    float z2 = (float)(radius * Math.Sin(lat1));

                    float x3 = (float)(radius * Math.Cos(lat2) * Math.Cos(lon2));
                    float y3 = (float)(radius * Math.Cos(lat2) * Math.Sin(lon2));
                    float z3 = (float)(radius * Math.Sin(lat2));

                    float x4 = (float)(radius * Math.Cos(lat2) * Math.Cos(lon1));
                    float y4 = (float)(radius * Math.Cos(lat2) * Math.Sin(lon1));
                    float z4 = (float)(radius * Math.Sin(lat2));

                    // Normals point outward
                    WriteTriangle(sb, x1, y1, z1, x2, y2, z2, x3, y3, z3, 
                        (x1 + x2 + x3) / 3 / radius, (y1 + y2 + y3) / 3 / radius, (z1 + z2 + z3) / 3 / radius);
                    WriteTriangle(sb, x1, y1, z1, x3, y3, z3, x4, y4, z4,
                        (x1 + x3 + x4) / 3 / radius, (y1 + y3 + y4) / 3 / radius, (z1 + z3 + z4) / 3 / radius);
                }
            }

            sb.AppendLine("endsolid sphere");
            return sb.ToString();
        }

        private static string GeneratePyramidStl(float baseSize, float height)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid pyramid");
            float s = baseSize / 2;

            // Base (two triangles)
            WriteTriangle(sb, -s, -s, 0, s, s, 0, s, -s, 0, 0, 0, -1);
            WriteTriangle(sb, -s, -s, 0, -s, s, 0, s, s, 0, 0, 0, -1);

            // Sides
            WriteTriangle(sb, -s, -s, 0, s, -s, 0, 0, 0, height, 0, -0.707f, 0.707f);
            WriteTriangle(sb, s, -s, 0, s, s, 0, 0, 0, height, 0.707f, 0, 0.707f);
            WriteTriangle(sb, s, s, 0, -s, s, 0, 0, 0, height, 0, 0.707f, 0.707f);
            WriteTriangle(sb, -s, s, 0, -s, -s, 0, 0, 0, height, -0.707f, 0, 0.707f);

            sb.AppendLine("endsolid pyramid");
            return sb.ToString();
        }

        private static string GenerateConeStl(float radius, float height, int segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("solid cone");

            for (int i = 0; i < segments; i++)
            {
                double a1 = 2 * Math.PI * i / segments;
                double a2 = 2 * Math.PI * (i + 1) / segments;
                
                float x1 = (float)(radius * Math.Cos(a1));
                float y1 = (float)(radius * Math.Sin(a1));
                float x2 = (float)(radius * Math.Cos(a2));
                float y2 = (float)(radius * Math.Sin(a2));

                // Base
                WriteTriangle(sb, 0, 0, 0, x2, y2, 0, x1, y1, 0, 0, 0, -1);
                
                // Side
                float slantHeight = (float)Math.Sqrt(radius * radius + height * height);
                float nx = (x1 + x2) / 2 * height / slantHeight / radius;
                float ny = (y1 + y2) / 2 * height / slantHeight / radius;
                float nz = radius / slantHeight;
                WriteTriangle(sb, x1, y1, 0, x2, y2, 0, 0, 0, height, nx, ny, nz);
            }

            sb.AppendLine("endsolid cone");
            return sb.ToString();
        }

        #endregion

        #region OBJ Generation

        private static string GenerateObjFromPaths(List<List<Point2D>> paths, float depth, float scale)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# OBJ file generated from SVG");
            sb.AppendLine("# PlatypusTools");
            sb.AppendLine();

            int vertexOffset = 1;

            foreach (var path in paths)
            {
                if (path.Count < 3) continue;

                var scaledPath = path.Select(p => new Point2D(p.X * scale, p.Y * scale)).ToList();
                int n = scaledPath.Count;

                // Front vertices (z = 0)
                foreach (var p in scaledPath)
                {
                    sb.AppendLine(FormattableString.Invariant($"v {p.X:F6} {p.Y:F6} 0.000000"));
                }

                // Back vertices (z = depth)
                foreach (var p in scaledPath)
                {
                    sb.AppendLine(FormattableString.Invariant($"v {p.X:F6} {p.Y:F6} {depth:F6}"));
                }

                sb.AppendLine();

                // Front face
                sb.Append("f");
                for (int i = 0; i < n; i++)
                {
                    sb.Append($" {vertexOffset + i}");
                }
                sb.AppendLine();

                // Back face
                sb.Append("f");
                for (int i = n - 1; i >= 0; i--)
                {
                    sb.Append($" {vertexOffset + n + i}");
                }
                sb.AppendLine();

                // Side faces
                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    sb.AppendLine($"f {vertexOffset + i} {vertexOffset + next} {vertexOffset + n + next} {vertexOffset + n + i}");
                }

                vertexOffset += n * 2;
            }

            return sb.ToString();
        }

        #endregion

        #region Character Shapes

        private static List<Point2D>? GetCharacterShape(char c, float xOffset, float height)
        {
            // Simple block letter shapes for 3D text
            float w = height * 0.6f;
            float h = height;

            return c switch
            {
                'A' => new List<Point2D> { new(xOffset, 0), new(xOffset + w/2, h), new(xOffset + w, 0), new(xOffset + w*0.8f, 0), new(xOffset + w/2, h*0.6f), new(xOffset + w*0.2f, 0) },
                'B' => CreateRectangle(xOffset, 0, w, h),
                'C' => new List<Point2D> { new(xOffset + w, h*0.2f), new(xOffset + w, 0), new(xOffset, 0), new(xOffset, h), new(xOffset + w, h), new(xOffset + w, h*0.8f), new(xOffset + w*0.3f, h*0.8f), new(xOffset + w*0.3f, h*0.2f) },
                'D' => CreateRectangle(xOffset, 0, w, h),
                'E' => CreateRectangle(xOffset, 0, w, h),
                'F' => CreateRectangle(xOffset, 0, w, h),
                'G' => CreateRectangle(xOffset, 0, w, h),
                'H' => CreateRectangle(xOffset, 0, w, h),
                'I' => CreateRectangle(xOffset + w*0.3f, 0, w*0.4f, h),
                'J' => CreateRectangle(xOffset, 0, w, h),
                'K' => CreateRectangle(xOffset, 0, w, h),
                'L' => new List<Point2D> { new(xOffset, h), new(xOffset, 0), new(xOffset + w, 0), new(xOffset + w, h*0.2f), new(xOffset + w*0.3f, h*0.2f), new(xOffset + w*0.3f, h) },
                'M' => CreateRectangle(xOffset, 0, w, h),
                'N' => CreateRectangle(xOffset, 0, w, h),
                'O' => CreateRectangle(xOffset, 0, w, h),
                'P' => CreateRectangle(xOffset, 0, w, h),
                'Q' => CreateRectangle(xOffset, 0, w, h),
                'R' => CreateRectangle(xOffset, 0, w, h),
                'S' => CreateRectangle(xOffset, 0, w, h),
                'T' => new List<Point2D> { new(xOffset, h), new(xOffset, h*0.8f), new(xOffset + w*0.35f, h*0.8f), new(xOffset + w*0.35f, 0), new(xOffset + w*0.65f, 0), new(xOffset + w*0.65f, h*0.8f), new(xOffset + w, h*0.8f), new(xOffset + w, h) },
                'U' => CreateRectangle(xOffset, 0, w, h),
                'V' => new List<Point2D> { new(xOffset, h), new(xOffset + w/2, 0), new(xOffset + w, h), new(xOffset + w*0.7f, h), new(xOffset + w/2, h*0.3f), new(xOffset + w*0.3f, h) },
                'W' => CreateRectangle(xOffset, 0, w, h),
                'X' => CreateRectangle(xOffset, 0, w, h),
                'Y' => CreateRectangle(xOffset, 0, w, h),
                'Z' => CreateRectangle(xOffset, 0, w, h),
                '0' => CreateRectangle(xOffset, 0, w, h),
                '1' => CreateRectangle(xOffset + w*0.3f, 0, w*0.4f, h),
                '2' => CreateRectangle(xOffset, 0, w, h),
                '3' => CreateRectangle(xOffset, 0, w, h),
                '4' => CreateRectangle(xOffset, 0, w, h),
                '5' => CreateRectangle(xOffset, 0, w, h),
                '6' => CreateRectangle(xOffset, 0, w, h),
                '7' => CreateRectangle(xOffset, 0, w, h),
                '8' => CreateRectangle(xOffset, 0, w, h),
                '9' => CreateRectangle(xOffset, 0, w, h),
                _ => null
            };
        }

        private static List<Point2D> CreateRectangle(float x, float y, float w, float h)
        {
            return new List<Point2D>
            {
                new(x, y),
                new(x + w, y),
                new(x + w, y + h),
                new(x, y + h)
            };
        }

        #endregion
    }

    public struct Point2D
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Point2D(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public enum BasicShape
    {
        Cube,
        Cylinder,
        Sphere,
        Pyramid,
        Cone
    }
}
