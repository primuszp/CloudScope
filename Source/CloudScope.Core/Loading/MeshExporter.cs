using System.Globalization;

namespace CloudScope.Loading;

public static class MeshExporter
{
    public static void WriteObj(TextWriter writer, SurfaceMesh mesh)
    {
        writer.WriteLine("# CloudScope reconstructed surface");
        bool normals = mesh.Normals.Length == mesh.Vertices.Length;
        bool colors = mesh.Colors.Length == mesh.Vertices.Length;
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            var vertex = mesh.Vertices[i];
            string line = string.Create(CultureInfo.InvariantCulture,
                $"v {vertex.X + mesh.OriginX:R} {vertex.Y + mesh.OriginY:R} {vertex.Z + mesh.OriginZ:R}");
            if (colors)
            {
                var color = mesh.Colors[i];
                line += string.Create(CultureInfo.InvariantCulture, $" {color.X:R} {color.Y:R} {color.Z:R}");
            }
            writer.WriteLine(line);
        }
        if (normals)
            foreach (var normal in mesh.Normals)
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"vn {normal.X:R} {normal.Y:R} {normal.Z:R}"));
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            int a = mesh.Indices[i] + 1, b = mesh.Indices[i + 1] + 1, c = mesh.Indices[i + 2] + 1;
            writer.WriteLine(normals ? $"f {a}//{a} {b}//{b} {c}//{c}" : $"f {a} {b} {c}");
        }
    }
}
