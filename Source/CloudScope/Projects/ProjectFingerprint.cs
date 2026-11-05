using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CloudScope.Projects;

namespace CloudScope;

/// <summary>Content checkpoints: geometry is hashed once per immutable entity state, labels use their undo revision.</summary>
internal sealed class ProjectFingerprint
{
    private readonly ConditionalWeakTable<object, Value> _geometry = new();
    private readonly ConditionalWeakTable<object, Value> _surfaces = new();
    private long _nextSurface;
    public string Capture(ProjectState state, long labelsRevision, object? surface)
    {
        string Hash(object entity, Func<ProjectState> wrap) => _geometry.GetValue(entity,
            _ => new(Digest(ProjectFile.Serialize(wrap())))).Text;
        var hashes = state.Polylines.Select(line => Hash(line, () => new() { Polylines = [line] }))
            .Concat(state.PlanarPolylines.Select(line => Hash(line, () => new() { PlanarPolylines = [line] })));
        string mesh = surface == null ? "" : _surfaces.GetValue(surface, _ => new((++_nextSurface).ToString(System.Globalization.CultureInfo.InvariantCulture))).Text;
        return Digest(ProjectFile.Serialize(state with { Polylines = [], PlanarPolylines = [], Annotations = [] })
            + string.Join("|", hashes) + ":" + labelsRevision + ":" + mesh);
    }
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private sealed record Value(string Text);
}
