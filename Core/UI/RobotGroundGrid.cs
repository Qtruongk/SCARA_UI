using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Test_1.UI
{
    /// <summary>
    /// An open world-space XY grid projected behind the CAD viewport. Orthographic
    /// rays intersect the floor at every visible pixel, even at low orbit angles.
    /// </summary>
    internal sealed class RobotGroundGrid : FrameworkElement
    {
        private const int MaximumLinesPerDirection = 250;
        private readonly Pen _minorPen = CreatePen(Color.FromRgb(220, 228, 237), 0.6);
        private readonly Pen _majorPen = CreatePen(Color.FromRgb(196, 209, 224), 1.0);
        private StreamGeometry _minorLines, _majorLines;
        private RectangleGeometry _clip;
        internal Rect RenderedWorldBounds { get; private set; } = Rect.Empty;
        internal double GridSpacing { get; private set; }

        internal RobotGroundGrid()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        /// <summary>Called for camera/viewport changes only; feedback does not redraw the floor.</summary>
        internal void Update(OrthographicCamera camera, double width, double height)
        {
            _minorLines = _majorLines = null;
            _clip = null;
            RenderedWorldBounds = Rect.Empty;
            GridSpacing = 0;
            if (camera == null || !Finite(width) || !Finite(height) || width <= 0 || height <= 0 ||
                !Finite(camera.Width) || camera.Width <= 0)
            { InvalidateVisual(); return; }

            Vector3D forward = camera.LookDirection;
            if (forward.LengthSquared < 0.000001) { InvalidateVisual(); return; }
            forward.Normalize();
            Vector3D right = Vector3D.CrossProduct(forward, camera.UpDirection);
            if (right.LengthSquared < 0.000001 || Math.Abs(forward.Z) < 0.000001)
            { InvalidateVisual(); return; }
            right.Normalize();
            Vector3D up = Vector3D.CrossProduct(right, forward);
            double halfWidth = camera.Width * 0.5;
            double halfHeight = halfWidth * height / width;
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 })
                {
                    Point3D origin = camera.Position + right * (sx * halfWidth) + up * (sy * halfHeight);
                    double distance = (RobotCoordinateSystem.MinZ - origin.Z) / forward.Z;
                    Point3D floor = origin + forward * distance;
                    if (!Finite(floor.X) || !Finite(floor.Y)) { InvalidateVisual(); return; }
                    minX = Math.Min(minX, floor.X); maxX = Math.Max(maxX, floor.X);
                    minY = Math.Min(minY, floor.Y); maxY = Math.Max(maxY, floor.Y);
                }

            double scale = width / camera.Width;
            if (!Finite(scale) || scale <= 0) { InvalidateVisual(); return; }
            Vector xProjected = new Vector(right.X * scale, -up.X * scale);
            Vector yProjected = new Vector(right.Y * scale, -up.Y * scale);
            double area = Math.Abs(Vector.CrossProduct(xProjected, yProjected));
            double density = Math.Min(area / Math.Max(0.000001, xProjected.Length),
                area / Math.Max(0.000001, yProjected.Length));
            double spacing = 50;
            // Fixed world multiples prevent the grid from swimming with camera motion.
            // Coarser spacing avoids aliasing and bounds the cost at shallow elevations.
            while ((maxX - minX) / spacing + 5 > MaximumLinesPerDirection ||
                (maxY - minY) / spacing + 5 > MaximumLinesPerDirection || spacing * density < 6)
            {
                spacing *= 2;
                if (!Finite(spacing)) { InvalidateVisual(); return; }
            }
            double majorSpacing = spacing == 50 ? 100 : spacing * 5;
            minX = Math.Floor(minX / spacing) * spacing - spacing;
            maxX = Math.Ceiling(maxX / spacing) * spacing + spacing;
            minY = Math.Floor(minY / spacing) * spacing - spacing;
            maxY = Math.Ceiling(maxY / spacing) * spacing + spacing;
            RenderedWorldBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            GridSpacing = spacing;

            StreamGeometry minor = new StreamGeometry();
            StreamGeometry major = new StreamGeometry();
            using (StreamGeometryContext minorContext = minor.Open())
            using (StreamGeometryContext majorContext = major.Open())
            {
                int xCount = Math.Min(MaximumLinesPerDirection, (int)Math.Round((maxX - minX) / spacing) + 1);
                int yCount = Math.Min(MaximumLinesPerDirection, (int)Math.Round((maxY - minY) / spacing) + 1);
                for (int i = 0; i < xCount; i++)
                {
                    double x = minX + i * spacing;
                    AddLine(IsMajor(x, majorSpacing) ? majorContext : minorContext,
                        Project(x, minY, camera.Position, right, up, scale, width, height),
                        Project(x, maxY, camera.Position, right, up, scale, width, height));
                }
                for (int i = 0; i < yCount; i++)
                {
                    double y = minY + i * spacing;
                    AddLine(IsMajor(y, majorSpacing) ? majorContext : minorContext,
                        Project(minX, y, camera.Position, right, up, scale, width, height),
                        Project(maxX, y, camera.Position, right, up, scale, width, height));
                }
            }
            minor.Freeze(); major.Freeze();
            _minorLines = minor; _majorLines = major;
            _clip = new RectangleGeometry(new Rect(0, 0, width, height)); _clip.Freeze();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (_clip == null) return;
            drawingContext.PushClip(_clip);
            drawingContext.DrawGeometry(null, _minorPen, _minorLines);
            drawingContext.DrawGeometry(null, _majorPen, _majorLines);
            drawingContext.Pop();
        }

        private static Point Project(double x, double y, Point3D cameraPosition,
            Vector3D right, Vector3D up, double scale, double width, double height)
        {
            Vector3D relative = new Point3D(x, y, RobotCoordinateSystem.MinZ) - cameraPosition;
            return new Point(width * 0.5 + Vector3D.DotProduct(relative, right) * scale,
                height * 0.5 - Vector3D.DotProduct(relative, up) * scale);
        }

        private static void AddLine(StreamGeometryContext context, Point from, Point to)
        {
            context.BeginFigure(from, false, false);
            context.LineTo(to, true, false);
        }

        private static bool IsMajor(double value, double spacing)
        {
            return Math.Abs(value / spacing - Math.Round(value / spacing)) < 0.000001;
        }

        private static Pen CreatePen(Color color, double width)
        {
            Pen pen = new Pen(new SolidColorBrush(color), width);
            pen.Freeze();
            return pen;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
