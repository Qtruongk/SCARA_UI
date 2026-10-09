using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Test_1.UI
{
    internal enum CoordinateLabelKind { AxisLabel, Tick, Origin }
    internal enum CoordinateAxis { None, X, Y, Z }

    /// <summary>A fixed world-space label, projected by the view as its camera moves.</summary>
    internal sealed class CoordinateLabelAnchor
    {
        public Point3D Position { get; private set; }
        public string Text { get; private set; }
        public Color Color { get; private set; }
        public CoordinateLabelKind Kind { get; private set; }
        public CoordinateAxis Axis { get; private set; }
        public int Value { get; private set; }

        internal CoordinateLabelAnchor(Point3D position, string text, Color color,
            CoordinateLabelKind kind, CoordinateAxis axis, int value = 0)
        {
            Position = position; Text = text; Color = color;
            Kind = kind; Axis = axis; Value = value;
        }
    }

    /// <summary>
    /// Open millimetre axes, independent of all moving CAD assemblies.
    /// The mounting plane is Z=0; RobotGroundGrid supplies the unbounded floor at Z=-100.
    /// </summary>
    internal static class RobotCoordinateSystem
    {
        public const int MinXY = -600;
        public const int MaxXY = 600;
        public const int MinZ = -100;
        public const int MaxZ = 800;
        public const int MajorStep = 100;

        private static readonly Color TickColor = Color.FromRgb(89, 105, 124);
        private static readonly Color XColor = Color.FromRgb(192, 62, 57);
        private static readonly Color YColor = Color.FromRgb(44, 133, 79);
        private static readonly Color ZColor = Color.FromRgb(48, 110, 189);
        private static readonly IReadOnlyList<CoordinateLabelAnchor> LabelAnchors = CreateLabels();

        public static IReadOnlyList<CoordinateLabelAnchor> Labels { get { return LabelAnchors; } }

        public static Model3DGroup CreateGeometry()
        {
            Model3DGroup result = new Model3DGroup();
            MeshBuilder ticks = new MeshBuilder();
            MeshBuilder datum = new MeshBuilder();

            // Tick marks belong to the actual axes, rather than to a surrounding box.
            for (int value = MinXY; value <= MaxXY; value += MajorStep)
            {
                if (value == 0) continue;
                ticks.Line(new Point3D(value, -6, 0), new Point3D(value, 6, 0), 1.8);
                ticks.Line(new Point3D(-6, value, 0), new Point3D(6, value, 0), 1.8);
            }
            for (int z = MinZ; z <= MaxZ; z += MajorStep)
                if (z != 0) ticks.Line(new Point3D(-6, 0, z), new Point3D(6, 0, z), 1.8);

            // The broken circle marks the THL300's 300 mm outer planar reach.
            // It is a visual distance reference, not an exact collision/workspace boundary.
            const int ringSegments = 96;
            for (int segment = 0; segment < ringSegments; segment += 2)
            {
                double from = segment * 2 * Math.PI / ringSegments;
                double to = (segment + 1) * 2 * Math.PI / ringSegments;
                datum.Line(new Point3D(300 * Math.Cos(from), 300 * Math.Sin(from), 0),
                    new Point3D(300 * Math.Cos(to), 300 * Math.Sin(to), 0), 2.0);
            }

            AddModel(result, ticks, Color.FromRgb(114, 132, 153));
            AddModel(result, datum, Color.FromRgb(138, 164, 191));

            AddAxis(result, new Point3D(MinXY, 0, 0), new Point3D(0, 0, 0),
                new Point3D(MaxXY, 0, 0), XColor);
            AddAxis(result, new Point3D(0, MinXY, 0), new Point3D(0, 0, 0),
                new Point3D(0, MaxXY, 0), YColor);
            AddAxis(result, new Point3D(0, 0, MinZ), new Point3D(0, 0, 0),
                new Point3D(0, 0, MaxZ), ZColor);

            result.Freeze();
            return result;
        }

        private static IReadOnlyList<CoordinateLabelAnchor> CreateLabels()
        {
            List<CoordinateLabelAnchor> labels = new List<CoordinateLabelAnchor>();
            for (int value = MinXY; value <= MaxXY; value += MajorStep)
            {
                string text = value.ToString(CultureInfo.InvariantCulture);
                if (value == 0) continue;
                labels.Add(new CoordinateLabelAnchor(new Point3D(value, 0, 0),
                    text, TickColor, CoordinateLabelKind.Tick, CoordinateAxis.X, value));
                labels.Add(new CoordinateLabelAnchor(new Point3D(0, value, 0),
                    text, TickColor, CoordinateLabelKind.Tick, CoordinateAxis.Y, value));
            }
            for (int z = MinZ; z <= MaxZ; z += MajorStep)
                if (z != 0) labels.Add(new CoordinateLabelAnchor(new Point3D(0, 0, z),
                    z.ToString(CultureInfo.InvariantCulture), TickColor,
                    CoordinateLabelKind.Tick, CoordinateAxis.Z, z));

            labels.Add(new CoordinateLabelAnchor(new Point3D(MaxXY + 35, 0, 0),
                "X (mm)", XColor, CoordinateLabelKind.AxisLabel, CoordinateAxis.X));
            labels.Add(new CoordinateLabelAnchor(new Point3D(0, MaxXY + 35, 0),
                "Y (mm)", YColor, CoordinateLabelKind.AxisLabel, CoordinateAxis.Y));
            labels.Add(new CoordinateLabelAnchor(new Point3D(0, 0, MaxZ + 28),
                "Z (mm)", ZColor, CoordinateLabelKind.AxisLabel, CoordinateAxis.Z));
            labels.Add(new CoordinateLabelAnchor(new Point3D(0, 0, 0),
                "O", TickColor, CoordinateLabelKind.Origin, CoordinateAxis.None));
            return new ReadOnlyCollection<CoordinateLabelAnchor>(labels);
        }

        private static void AddAxis(Model3DGroup target, Point3D negative, Point3D origin,
            Point3D positive, Color color)
        {
            MeshBuilder faded = new MeshBuilder();
            faded.Line(negative, origin, 2.2);
            Color fadedColor = Color.FromRgb((byte)((color.R + 220) / 2),
                (byte)((color.G + 226) / 2), (byte)((color.B + 232) / 2));
            AddModel(target, faded, fadedColor);

            MeshBuilder axis = new MeshBuilder();
            Vector3D direction = positive - origin;
            direction.Normalize();
            Point3D headBase = positive - direction * 22;
            axis.Line(origin, headBase, 4.0);
            axis.Cone(headBase, positive, 8.0);
            AddModel(target, axis, color);
        }

        private static void AddModel(Model3DGroup target, MeshBuilder builder, Color color)
        {
            if (builder.Mesh.TriangleIndices.Count == 0) return;
            // Matte material and explicit normals avoid additive colour saturation.
            Material material = new DiffuseMaterial(new SolidColorBrush(color));
            target.Children.Add(new GeometryModel3D(builder.Mesh, material) { BackMaterial = material });
        }

        /// <summary>Small square rods, batched by colour, keep the scene inexpensive.</summary>
        private sealed class MeshBuilder
        {
            internal readonly MeshGeometry3D Mesh = new MeshGeometry3D();

            internal void Line(Point3D from, Point3D to, double width)
            {
                Vector3D along = to - from;
                if (along.LengthSquared < 0.000001) return;
                along.Normalize();
                Vector3D perpendicular = Vector3D.CrossProduct(along,
                    Math.Abs(along.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0));
                perpendicular.Normalize();
                Vector3D second = Vector3D.CrossProduct(along, perpendicular);
                perpendicular *= width / 2; second *= width / 2;
                Point3D[] fromCorners = { from - perpendicular - second, from + perpendicular - second,
                    from + perpendicular + second, from - perpendicular + second };
                Point3D[] toCorners = { to - perpendicular - second, to + perpendicular - second,
                    to + perpendicular + second, to - perpendicular + second };
                Quad(fromCorners[0], fromCorners[3], fromCorners[2], fromCorners[1]);
                Quad(toCorners[0], toCorners[1], toCorners[2], toCorners[3]);
                for (int side = 0; side < 4; side++)
                {
                    int next = (side + 1) % 4;
                    Quad(fromCorners[side], fromCorners[next], toCorners[next], toCorners[side]);
                }
            }

            internal void Cone(Point3D baseCenter, Point3D tip, double radius)
            {
                Vector3D along = tip - baseCenter; along.Normalize();
                Vector3D u = Vector3D.CrossProduct(along,
                    Math.Abs(along.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0));
                u.Normalize();
                Vector3D v = Vector3D.CrossProduct(along, u);
                const int sides = 12;
                Point3D[] ring = new Point3D[sides];
                for (int side = 0; side < sides; side++)
                {
                    double angle = side * 2 * Math.PI / sides;
                    ring[side] = baseCenter + radius * (u * Math.Cos(angle) + v * Math.Sin(angle));
                }
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    Triangle(tip, ring[side], ring[next]);
                    Triangle(baseCenter, ring[next], ring[side]);
                }
            }

            private void Quad(Point3D a, Point3D b, Point3D c, Point3D d)
            {
                Vector3D normal = Vector3D.CrossProduct(b - a, c - a); normal.Normalize();
                int first = Mesh.Positions.Count;
                foreach (Point3D point in new[] { a, b, c, d })
                {
                    Mesh.Positions.Add(point); Mesh.Normals.Add(normal);
                }
                Indices(first, first + 1, first + 2);
                Indices(first, first + 2, first + 3);
            }

            private void Triangle(Point3D a, Point3D b, Point3D c)
            {
                Vector3D normal = Vector3D.CrossProduct(b - a, c - a); normal.Normalize();
                int first = Mesh.Positions.Count;
                foreach (Point3D point in new[] { a, b, c })
                {
                    Mesh.Positions.Add(point); Mesh.Normals.Add(normal);
                }
                Indices(first, first + 1, first + 2);
            }

            private void Indices(int a, int b, int c)
            {
                Mesh.TriangleIndices.Add(a); Mesh.TriangleIndices.Add(b); Mesh.TriangleIndices.Add(c);
            }
        }
    }
}
