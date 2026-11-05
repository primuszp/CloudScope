using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudScope.IO;
using CloudScope.Loading;
using OpenTK.Mathematics;

namespace CloudScope.Projects;

/// <summary>A versioned ZIP document, atomically published. Large source surveys remain external references.</summary>
public static class ProjectFile
{
    public const string Extension = "cloudscope";
    internal static readonly JsonSerializerOptions Json = CreateOptions();

    public static string Serialize(ProjectState state) => JsonSerializer.Serialize(state, Json);

    public static void Save(string path, ProjectState state, PointCloudDataset? embedded, SurfaceMesh? surface,
        CancellationToken cancellation = default)
    {
        ProjectValidation.Validate(state);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        state = state with
        {
            Source = state.Source is { Path.Length: > 0 } source ? source with { Path = Path.GetRelativePath(directory, source.Path) } : state.Source,
            Layers = state.Layers.Select(layer => layer with { Path = Path.GetRelativePath(directory, layer.Path) }).ToArray()
        };
        AtomicFile.Write(path, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            using (var output = zip.CreateEntry("project.json").Open()) JsonSerializer.Serialize(output, state, Json);
            if (state.Source is { Format: "Embedded" })
            {
                if (embedded == null) throw new InvalidDataException("Embedded project geometry is missing.");
                using var writer = new BinaryWriter(zip.CreateEntry("resident.bin", CompressionLevel.Fastest).Open());
                for (int i = 0; i < embedded.LoadedCount; i++)
                {
                    if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                    var p = embedded.SourcePoints[i];
                    Write(writer, new Vector3(p.X, p.Y, p.Z)); Write(writer, new Vector3(p.R, p.G, p.B));
                    writer.Write(embedded.Attributes.Class[i]); writer.Write(embedded.Attributes.Intensity[i]);
                    writer.Write(embedded.Attributes.ReturnNumber[i]);
                    if (!double.IsFinite(embedded.Attributes.Z[i])) throw new InvalidDataException("Invalid embedded height.");
                    writer.Write(embedded.Attributes.Z[i]);
                }
            }
            if (state.HasSurface)
            {
                if (surface == null) throw new InvalidDataException("Project surface is missing.");
                using var writer = new BinaryWriter(zip.CreateEntry("surface.bin", CompressionLevel.Fastest).Open());
                writer.Write(surface.OriginX); writer.Write(surface.OriginY); writer.Write(surface.OriginZ);
                writer.Write(surface.Vertices.Length); writer.Write(surface.Indices.Length);
                writer.Write(surface.Normals.Length); writer.Write(surface.Colors.Length);
                foreach (var points in new[] { surface.Vertices, surface.Normals, surface.Colors })
                    for (int i = 0; i < points.Length; i++)
                    { if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested(); Write(writer, points[i]); }
                for (int i = 0; i < surface.Indices.Length; i++)
                { if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested(); writer.Write(surface.Indices[i]); }
            }
            cancellation.ThrowIfCancellationRequested();
        });
    }

    public static ProjectArchive Read(string path, CancellationToken cancellation = default)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = Required(zip, "project.json");
        if (entry.Length > 256L * 1024 * 1024) throw new InvalidDataException("Project metadata exceeds 256 MiB.");
        ProjectState state;
        using (var input = entry.Open()) state = JsonSerializer.Deserialize<ProjectState>(input, Json)
            ?? throw new InvalidDataException("The project is empty.");
        ProjectValidation.Validate(state);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        state = state with
        {
            Source = state.Source is { Path.Length: > 0 } source ? source with { Path = Path.GetFullPath(source.Path, directory) } : state.Source,
            Layers = state.Layers.Select(layer => layer with { Path = Path.GetFullPath(layer.Path, directory) }).ToArray()
        };
        PointCloudDataset? embedded = null;
        if (state.Source is { Format: "Embedded" } spec)
        {
            int count = spec.PointCount;
            entry = Required(zip, "resident.bin");
            if (entry.Length != count * 36L) throw new InvalidDataException("Invalid embedded point data length.");
            var points = new PointData[count]; var classes = new byte[count]; var intensity = new ushort[count];
            var returns = new byte[count]; var z = new double[count];
            using var reader = new BinaryReader(entry.Open());
            for (int i = 0; i < count; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                var position = ReadVector(reader); var rgb = ReadVector(reader);
                points[i] = new PointData { X = position.X, Y = position.Y, Z = position.Z, R = rgb.X, G = rgb.Y, B = rgb.Z };
                classes[i] = reader.ReadByte(); intensity[i] = reader.ReadUInt16(); returns[i] = reader.ReadByte(); z[i] = reader.ReadDouble();
                if (!double.IsFinite(z[i])) throw new InvalidDataException("Invalid embedded height.");
            }
            embedded = new PointCloudDataset(points, count, spec.Radius, spec.HasColor, spec.ColorScale,
                new(classes, intensity, returns, z, count == 0 ? 0 : z.Min(), count == 0 ? 0 : z.Max()),
                spec.Identity.OriginX, spec.Identity.OriginY, spec.Identity.OriginZ);
        }
        SurfaceMesh? surface = null;
        if (state.HasSurface)
        {
            entry = Required(zip, "surface.bin");
            using var reader = new BinaryReader(entry.Open());
            double x = reader.ReadDouble(), y = reader.ReadDouble(), z = reader.ReadDouble();
            int vertices = reader.ReadInt32(), indices = reader.ReadInt32(), normals = reader.ReadInt32(), colors = reader.ReadInt32();
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || vertices < 0 || indices < 0 || indices % 3 != 0
                || (normals != 0 && normals != vertices) || (colors != 0 && colors != vertices)
                || entry.Length != 40L + ((long)vertices + normals + colors) * 12 + indices * 4L)
                throw new InvalidDataException("Invalid project surface dimensions.");
            Vector3[] ReadVectors(int count)
            {
                var array = new Vector3[count];
                for (int i = 0; i < count; i++)
                { if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested(); array[i] = ReadVector(reader); }
                return array;
            }
            var v = ReadVectors(vertices); var n = ReadVectors(normals); var c = ReadVectors(colors); var faces = new int[indices];
            for (int i = 0; i < indices; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                faces[i] = reader.ReadInt32();
                if ((uint)faces[i] >= (uint)vertices) throw new InvalidDataException("Invalid project surface index.");
            }
            surface = new(v, faces, x, y, z) { Normals = n, Colors = c };
        }
        cancellation.ThrowIfCancellationRequested();
        return new(state, embedded, surface);
    }

    private static ZipArchiveEntry Required(ZipArchive zip, string name)
    {
        if (zip.Entries.Count(e => e.FullName == name) != 1) throw new InvalidDataException($"Missing or duplicate project entry: {name}.");
        return zip.GetEntry(name)!;
    }
    private static void Write(BinaryWriter writer, Vector3 p) { ProjectValidation.Finite(p); writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
    private static Vector3 ReadVector(BinaryReader reader)
    {
        var v = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        ProjectValidation.Finite(v); return v;
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, IgnoreReadOnlyProperties = true, RespectRequiredConstructorParameters = true };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new Vector3Json()); options.Converters.Add(new Vector2Json());
        return options;
    }
    private sealed class Vector3Json : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a 3D vector.");
            reader.Read(); float x = reader.GetSingle(); reader.Read(); float y = reader.GetSingle(); reader.Read(); float z = reader.GetSingle();
            reader.Read(); if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Expected three coordinates.");
            var v = new Vector3(x,y,z); ProjectValidation.Finite(v); return v;
        }
        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        { ProjectValidation.Finite(value); writer.WriteStartArray(); writer.WriteNumberValue(value.X); writer.WriteNumberValue(value.Y); writer.WriteNumberValue(value.Z); writer.WriteEndArray(); }
    }
    private sealed class Vector2Json : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a 2D vector.");
            reader.Read(); float x = reader.GetSingle(); reader.Read(); float y = reader.GetSingle(); reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray || !float.IsFinite(x) || !float.IsFinite(y)) throw new JsonException("Invalid 2D vector.");
            return new(x,y);
        }
        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        { writer.WriteStartArray(); writer.WriteNumberValue(value.X); writer.WriteNumberValue(value.Y); writer.WriteEndArray(); }
    }
}

public sealed record ProjectArchive(ProjectState State, PointCloudDataset? Embedded, SurfaceMesh? Surface);
