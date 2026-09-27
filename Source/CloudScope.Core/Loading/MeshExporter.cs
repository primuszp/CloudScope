using System.Globalization;

namespace CloudScope.Loading;

public static class MeshExporter
{
    public static void WriteObj(TextWriter writer, SurfaceMesh mesh)
    {
        writer.WriteLine("# CloudScope reconstructed surface");
        foreach (var vertex in mesh.Vertices)
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"v {vertex.X + mesh.OriginX:R} {vertex.Y + mesh.OriginY:R} {vertex.Z + mesh.OriginZ:R}"));
        for (int i = 0; i < mesh.Indices.Length; i += 3)
            writer.WriteLine($"f {mesh.Indices[i] + 1} {mesh.Indices[i + 1] + 1} {mesh.Indices[i + 2] + 1}");
    }
}
