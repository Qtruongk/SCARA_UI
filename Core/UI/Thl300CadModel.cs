using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Test_1.UI
{
    /// <summary>Loads tessellated manufacturer IGES solids, without a procedural robot fallback.</summary>
    internal sealed class Thl300CadModel
    {
        private const string ResourceName = "Test_1.Assets.THL300.THL300.mesh";
        public Model3DGroup Base { get; private set; }
        public Model3DGroup Arm1 { get; private set; }
        public Model3DGroup Arm2 { get; private set; }
        public Model3DGroup Shaft { get; private set; }
        public string SourceCadSha256 { get; private set; }
        public int PartCount { get { return 4; } }
        public int TriangleCount { get; private set; }
        public Rect3D Bounds { get; private set; }

        public static Thl300CadModel Load()
        {
            Thl300CadModel model = new Thl300CadModel();
            Model3DGroup[] parts = { new Model3DGroup(), new Model3DGroup(), new Model3DGroup(), new Model3DGroup() };
            Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidDataException("Embedded THL300 CAD mesh is missing. Rebuild with Assets/THL300/THL300.mesh.");
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "THL300M1") throw new InvalidDataException("Invalid THL300 CAD mesh format.");
                    byte[] sourceHash = reader.ReadBytes(32);
                    if (sourceHash.Length != 32) throw new InvalidDataException("Incomplete THL300 CAD source hash.");
                    model.SourceCadSha256 = BitConverter.ToString(sourceHash).Replace("-", "");
                    int sectionCount = reader.ReadInt32();
                    if (sectionCount < 4 || sectionCount > 10000) throw new InvalidDataException("Invalid THL300 material section count.");
                    for (int section = 0; section < sectionCount; section++)
                    {
                        int groupId = reader.ReadInt32();
                        string name = reader.ReadString();
                        if (groupId < 0 || groupId > 3 || name.Length > 256) throw new InvalidDataException("Invalid THL300 CAD part.");
                        byte red = reader.ReadByte(), green = reader.ReadByte(), blue = reader.ReadByte(), alpha = reader.ReadByte();
                        Color color = Color.FromArgb(alpha, red, green, blue);
                        int vertexCount = reader.ReadInt32(), indexCount = reader.ReadInt32();
                        if (vertexCount < 3 || vertexCount > 2000000 || indexCount < 3 || indexCount > 12000000 || indexCount % 3 != 0)
                            throw new InvalidDataException("Invalid THL300 CAD geometry size.");
                        Point3DCollection positions = new Point3DCollection(vertexCount);
                        Vector3DCollection normals = new Vector3DCollection(vertexCount);
                        for (int vertex = 0; vertex < vertexCount; vertex++)
                        {
                            float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
                            float nx = reader.ReadSingle(), ny = reader.ReadSingle(), nz = reader.ReadSingle();
                            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(nx) || !Finite(ny) || !Finite(nz))
                                throw new InvalidDataException("Non-finite THL300 CAD vertex.");
                            positions.Add(new Point3D(x, y, z)); normals.Add(new Vector3D(nx, ny, nz));
                        }
                        Int32Collection indices = new Int32Collection(indexCount);
                        for (int index = 0; index < indexCount; index++)
                        {
                            int value = reader.ReadInt32();
                            if (value < 0 || value >= vertexCount) throw new InvalidDataException("Invalid THL300 CAD triangle index.");
                            indices.Add(value);
                        }
                        MeshGeometry3D mesh = new MeshGeometry3D { Positions = positions, Normals = normals, TriangleIndices = indices };
                        mesh.Freeze();
                        Material material;
                        if (!materials.TryGetValue(color, out material))
                        {
                            MaterialGroup group = new MaterialGroup();
                            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
                            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(80, 84, 92)), 45));
                            group.Freeze(); material = group; materials.Add(color, material);
                        }
                        parts[groupId].Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
                        model.TriangleCount += indexCount / 3;
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected data after THL300 CAD geometry.");
                }
            }
            Rect3D bounds = Rect3D.Empty;
            foreach (Model3DGroup part in parts)
            {
                if (part.Children.Count == 0) throw new InvalidDataException("THL300 CAD is missing an articulated solid.");
                part.Freeze(); bounds.Union(part.Bounds);
            }
            model.Base = parts[0]; model.Arm1 = parts[1]; model.Arm2 = parts[2]; model.Shaft = parts[3];
            model.Bounds = bounds;
            return model;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
