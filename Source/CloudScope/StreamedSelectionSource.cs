using System;
using System.Collections.Generic;
using System.Threading;
using CloudScope.Labeling;
using CloudScope.Rendering;
using CloudScope.Selection;
using CloudScope.Store;
using OpenTK.Mathematics;

namespace CloudScope
{
    /// <summary>
    /// Resolves selections against the open stores, and remembers the points they caught.
    /// </summary>
    /// <remarks>
    /// The trick that keeps labelling possible without the cloud in memory: a point's durable
    /// identity is a 64-bit <see cref="PointRef"/> into its store's point file, but everything
    /// downstream — the label manager, the highlight renderer — is keyed by a position in a
    /// small array. That array holds only the points somebody actually selected, so it grows
    /// with the labelling rather than with the cloud, and the refs beside it are what a label
    /// means once the session is over.
    /// </remarks>
    internal sealed class StreamedSelectionSource
    {
        private readonly IReadOnlyList<PointTileLayer> _layers;

        /// <summary>Points that have been caught by a selection, in the order they were first seen.</summary>
        private readonly List<PointData> _points = [];

        /// <summary>The store-file identity of each entry of <see cref="_points"/>.</summary>
        private readonly List<PointRef> _refs = [];

        /// <summary>Where a point already known sits in <see cref="_points"/>.</summary>
        private readonly Dictionary<PointRef, int> _known = [];

        public StreamedSelectionSource(IReadOnlyList<PointTileLayer> layers) => _layers = layers;

        /// <summary>Every point any selection has touched, for the highlight renderer.</summary>
        public PointData[] LabelPoints => _points.ToArray();

        /// <summary>The durable identity of each labelled point, parallel to <see cref="LabelPoints"/>.</summary>
        public IReadOnlyList<PointRef> LabelRefs => _refs;

        /// <summary>
        /// Resolves the volume and returns positions into <see cref="LabelPoints"/>, adding
        /// anything newly caught.
        /// </summary>
        /// <remarks>
        /// Only for a selection that is being kept. A preview resolves through
        /// <see cref="ResolvePreview"/> instead, which does not grow anything: dragging a
        /// gizmo across a cloud would otherwise accumulate every point it swept over.
        /// </remarks>
        public IReadOnlyList<int> ResolveAndRemember(
            IPointSelectionQuery query, CancellationToken cancellationToken = default)
        {
            var indices = new List<int>();
            foreach ((PointRef reference, Vector3 position) in Resolve(query, cancellationToken))
            {
                if (_known.TryGetValue(reference, out int existing))
                {
                    indices.Add(existing);
                    continue;
                }

                _known[reference] = _points.Count;
                indices.Add(_points.Count);
                _points.Add(new PointData { X = position.X, Y = position.Y, Z = position.Z });
                _refs.Add(reference);
            }

            return indices;
        }

        /// <summary>Resolves the volume for a preview, keeping nothing.</summary>
        public SelectionPreviewWorker.PreviewResult ResolvePreview(
            IPointSelectionQuery query, CancellationToken cancellationToken)
        {
            var points = new List<PointData>();
            var indices = new List<int>();
            foreach ((PointRef _, Vector3 position) in Resolve(query, cancellationToken))
            {
                indices.Add(points.Count);
                points.Add(new PointData { X = position.X, Y = position.Y, Z = position.Z });
            }

            return new SelectionPreviewWorker.PreviewResult(points.ToArray(), indices);
        }

        /// <summary>Walks every visible layer's cell tree for the points inside the volume.</summary>
        private IEnumerable<(PointRef Reference, Vector3 Position)> Resolve(
            IPointSelectionQuery query, CancellationToken cancellationToken)
        {
            for (int layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
            {
                PointTileLayer layer = _layers[layerIndex];
                if (!layer.Visible)
                    continue;

                List<PointRef> hits = PointTileVolumeQuery.Resolve(
                    layer.Store, query, layerIndex, cancellationToken);
                Vector3[] positions = PointTileVolumeQuery.ReadPositions(layer.Store, hits);
                for (int i = 0; i < hits.Count; i++)
                    yield return (hits[i], positions[i]);
            }
        }

        /// <summary>The directory labels for this scene are written beside.</summary>
        /// <remarks>
        /// The first layer's, so a scene keeps its labels where its primary cloud lives. A
        /// label carries its own layer index, so a second cloud's points are still identified
        /// correctly inside that one file.
        /// </remarks>
        public string LabelDirectory => _layers.Count > 0 ? _layers[0].Store.Directory : "";

        /// <summary>
        /// Re-establishes labelled points from a saved file, returning the annotations keyed
        /// the way the label manager wants them.
        /// </summary>
        /// <remarks>
        /// The positions come back out of the stores rather than out of the file: a saved
        /// label names a point, and the point is where the store says it is.
        /// </remarks>
        public Dictionary<int, PointAnnotation> Restore(
            IReadOnlyList<(PointRef Reference, PointAnnotation Annotation)> saved)
        {
            var restored = new Dictionary<int, PointAnnotation>(saved.Count);
            foreach ((PointRef reference, PointAnnotation annotation) in saved)
            {
                if ((uint)reference.LayerIndex >= (uint)_layers.Count)
                    continue;

                if (!_known.TryGetValue(reference, out int index))
                {
                    var single = new GpuPointVertex[1];
                    _layers[reference.LayerIndex].Store.ReadPoints(reference.PointIndex, single);
                    index = _points.Count;
                    _known[reference] = index;
                    _points.Add(new PointData { X = single[0].X, Y = single[0].Y, Z = single[0].Z });
                    _refs.Add(reference);
                }

                restored[index] = annotation;
            }

            return restored;
        }

        /// <summary>
        /// Maps labelled points onto records of the LAS they were built from, per source file.
        /// </summary>
        /// <remarks>
        /// Only layers whose store kept the source column can answer, and each store names its
        /// own LAS — two layers built from two files are written back to two copies, which is
        /// why the result is grouped by path rather than flattened.
        /// </remarks>
        public Dictionary<string, Dictionary<int, byte>> MapToSourceRecords(
            IReadOnlyDictionary<int, PointAnnotation> annotations, Func<string, byte?> codeFor)
        {
            var byFile = new Dictionary<string, Dictionary<int, byte>>(StringComparer.OrdinalIgnoreCase);
            var single = new long[1];
            foreach ((int index, PointAnnotation annotation) in annotations)
            {
                if ((uint)index >= (uint)_refs.Count || codeFor(annotation.LabelName) is not byte code)
                    continue;

                PointRef reference = _refs[index];
                if ((uint)reference.LayerIndex >= (uint)_layers.Count)
                    continue;

                PointTileStore store = _layers[reference.LayerIndex].Store;
                if (store.SourcePath is not { Length: > 0 } path
                    || !store.ReadSourceIndices(reference.PointIndex, single))
                    continue;

                // The writer addresses records with an int, which is what a LAS under two
                // billion points needs; a record beyond that cannot be written anyway.
                if (single[0] > int.MaxValue)
                    continue;

                if (!byFile.TryGetValue(path, out Dictionary<int, byte>? map))
                {
                    map = [];
                    byFile[path] = map;
                }

                map[(int)single[0]] = code;
            }

            return byFile;
        }

        /// <summary>Forgets every remembered point, for when the scene is replaced.</summary>
        public void Clear()
        {
            _points.Clear();
            _refs.Clear();
            _known.Clear();
        }
    }
}
