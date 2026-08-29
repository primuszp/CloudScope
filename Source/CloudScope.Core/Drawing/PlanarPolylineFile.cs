using System.Text.Json;
using OpenTK.Mathematics;

namespace CloudScope.Drawing;

/// <summary>Portable, versioned JSON persistence for CloudScope planar polylines.</summary>
public static class PlanarPolylineFile
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(IEnumerable<PlanarPolyline> polylines)
    {
        var document = new Document(CurrentVersion, polylines.Select(ToDto).ToArray());
        return JsonSerializer.Serialize(document, Options);
    }

    public static PlanarPolyline[] Deserialize(string json)
    {
        Document document = JsonSerializer.Deserialize<Document>(json, Options)
            ?? throw new InvalidDataException("The polyline document is empty.");
        if (document.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported polyline document version: {document.Version}.");
        return document.Polylines.Select(FromDto).ToArray();
    }

    public static void Save(string path, IEnumerable<PlanarPolyline> polylines) =>
        File.WriteAllText(Path.GetFullPath(path), Serialize(polylines));

    public static PlanarPolyline[] Load(string path) =>
        Deserialize(File.ReadAllText(Path.GetFullPath(path)));

    private static PolylineDto ToDto(PlanarPolyline polyline) => new(
        polyline.Name,
        ToArray(polyline.Origin), ToArray(polyline.AxisX), ToArray(polyline.AxisY),
        polyline.Closed,
        polyline.Vertices.Select(vertex => new VertexDto(
            vertex.Position.X, vertex.Position.Y, vertex.Bulge,
            vertex.StartWidth, vertex.EndWidth)).ToArray());

    private static PlanarPolyline FromDto(PolylineDto dto)
    {
        if (dto.Origin.Length != 3 || dto.AxisX.Length != 3 || dto.AxisY.Length != 3)
            throw new InvalidDataException("Polyline plane vectors must contain three numbers.");
        if (dto.Vertices.Length < 2)
            throw new InvalidDataException("A stored polyline must contain at least two vertices.");
        Vector3 axisX = ToVector(dto.AxisX);
        Vector3 axisY = ToVector(dto.AxisY);
        if (axisX.LengthSquared < 1e-10f || axisY.LengthSquared < 1e-10f
            || Vector3.Cross(axisX, axisY).LengthSquared < 1e-10f)
            throw new InvalidDataException("Polyline plane axes are degenerate.");
        axisX.Normalize();
        axisY = (axisY - axisX * Vector3.Dot(axisY, axisX)).Normalized();
        return new PlanarPolyline(0, dto.Name, ToVector(dto.Origin), axisX, axisY,
            dto.Vertices.Select(vertex => new PlanarPolylineVertex(
                new Vector2(vertex.X, vertex.Y), vertex.Bulge,
                MathF.Max(0f, vertex.StartWidth), MathF.Max(0f, vertex.EndWidth))).ToArray(),
            dto.Closed);
    }

    private static float[] ToArray(Vector3 value) => [value.X, value.Y, value.Z];
    private static Vector3 ToVector(float[] value) => new(value[0], value[1], value[2]);

    private sealed record Document(int Version, PolylineDto[] Polylines);
    private sealed record PolylineDto(
        string Name, float[] Origin, float[] AxisX, float[] AxisY,
        bool Closed, VertexDto[] Vertices);
    private sealed record VertexDto(float X, float Y, float Bulge, float StartWidth, float EndWidth);
}
