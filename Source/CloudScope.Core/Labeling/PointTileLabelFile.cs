using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CloudScope.Store;

namespace CloudScope.Labeling;

/// <summary>
/// Reads and writes labels for a streamed cloud, keyed by where each point sits in its store.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="LabelFileIO"/>. That file's indices are positions in the source
/// LAS, which a store does not record; these are positions in the store's own point file, and
/// the two would be silently mixed up if they shared a format. A label written here means the
/// same point every time the store is opened, however large the cloud is.
/// </remarks>
public static class PointTileLabelFile
{
    public const string FileName = "labels.json";

    private const int FormatVersion = 1;

    /// <summary>
    /// Writes the labels beside the store they belong to.
    /// </summary>
    /// <param name="refs">
    /// The store identity of each labelled point, indexed the way the annotations are.
    /// </param>
    public static void Save(
        string directory,
        IReadOnlyList<PointRef> refs,
        IReadOnlyDictionary<int, PointAnnotation> annotations)
    {
        var entries = new List<LabelEntry>(annotations.Count);
        foreach ((int index, PointAnnotation annotation) in annotations.OrderBy(pair => pair.Key))
        {
            if ((uint)index >= (uint)refs.Count)
                continue;

            PointRef reference = refs[index];
            entries.Add(new LabelEntry(
                reference.LayerIndex, reference.PointIndex, annotation.LabelName, annotation.InstanceId));
        }

        var document = new LabelDocument(FormatVersion, entries);
        File.WriteAllText(
            Path.Combine(directory, FileName),
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Reads labels back, returning the points and what they were labelled as.
    /// </summary>
    /// <remarks>
    /// The positions are not stored — a reference is enough to read the point back out of the
    /// store, and storing them again would let the file disagree with the cloud it describes.
    /// </remarks>
    public static List<(PointRef Reference, PointAnnotation Annotation)> Load(string directory)
    {
        var loaded = new List<(PointRef, PointAnnotation)>();
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            return loaded;

        LabelDocument? document = JsonSerializer.Deserialize<LabelDocument>(File.ReadAllText(path));
        if (document is null || document.FormatVersion != FormatVersion)
            return loaded;

        foreach (LabelEntry entry in document.Labels)
        {
            loaded.Add((
                new PointRef(entry.Layer, entry.Point),
                new PointAnnotation(entry.Name, entry.Instance)));
        }

        return loaded;
    }

    private sealed record LabelDocument(int FormatVersion, List<LabelEntry> Labels);

    private sealed record LabelEntry(int Layer, long Point, string Name, int? Instance);
}
